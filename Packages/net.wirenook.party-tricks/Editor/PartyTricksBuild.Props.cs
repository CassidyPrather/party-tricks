using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDKBase;
using Wirenook.Assets;
using Wirenook.Assets.Editor;
using Wirenook.WorldSync.Editor;

namespace Wirenook.PartyTricks.Editor
{
    public static partial class PartyTricksBuild
    {
        public const string HammerPrefab = Prefabs + "/Wirenook Bonk Hammer.prefab";
        public const string VanishPrefab = Prefabs + "/Wirenook Vanishing Act.prefab";
        public const string BusyPrefab = Prefabs + "/Wirenook Busy Sign.prefab";
        public const string BundlePrefab = Prefabs + "/Wirenook Party Tricks.prefab";

        /// <summary>The synced switch that hides the wearer; see <see cref="Vanish"/>.</summary>
        public const string VanishParameter = "WN/Vanish";

        /// <summary>
        /// Where a humanoid avatar's hips usually are. The vanishing act
        /// hides the wearer by shrinking this one bone, and it is the only
        /// thing in the package that has to name part of the avatar itself.
        /// </summary>
        public const string UsualHipsPath = "Armature/Hips";

        /// <summary>
        /// A toy mallet for the right hand that goes BONK when its head lands
        /// on someone's head or body, with a burst of stars at the face.
        ///
        /// The wearer's client is the one that notices a hit: a contact on
        /// the mallet's head that fires when another avatar's standard head
        /// or torso contact enters it at speed. It then raises one synced bit
        /// for a quarter of a second, and everyone plays the bonk from that
        /// bit, so nobody hears a hit the wearer did not land, or misses one
        /// that they did.
        /// </summary>
        public static void Hammer()
        {
            const string parameter = "WN/Hammer";
            const string hit = "WN/HammerHit";
            const string bonk = "WN/Bonk";

            FeaturePrefab.Edit(HammerPrefab, root =>
            {
                var mount = FeaturePrefab.Child(root, "Mount");
                FeaturePrefab.AttachToBone(mount, HumanBodyBones.RightHand);
                var grip = FeaturePrefab.Child(mount, "Grip");
                var fit = FeaturePrefab.Ensure<WirenookHandGrip>(grip);
                fit.hand = HumanBodyBones.RightHand;
                // The handle lies across the palm rather than a little way
                // out along the fingers.
                fit.alongFingers = 0.05f;
                // A fist round a thin handle.
                fit.held = new List<WirenookHeldWhen> { WirenookHeldWhen.On(parameter) };
                fit.curl = 0.8f;
                fit.indexCurl = 0.8f;
                fit.thumbCurl = 0.85f;

                // A grip points a tool where the fingers point, with its top
                // to the back of the hand. A hammer's handle runs out of the
                // thumb side of the fist instead, so it is laid over on its
                // side; the head still strikes the way the fingers point.
                var hold = FeaturePrefab.Child(grip, "Hold");
                hold.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                var model = FeaturePrefab.Model(hold, Models + "/hammer.fbx");
                var body = FeaturePrefab.Bone(model, "Hammer");
                var impact = FeaturePrefab.Bone(model, "Hammer.Impact");
                var bodyPath = FeaturePrefab.PathTo(root, body);
                var impactPath = FeaturePrefab.PathTo(root, impact);

                // The mallet's head, in the model's own space.
                var strike = FeaturePrefab.Child(body.gameObject, "Strike");
                strike.transform.position = model.transform.TransformPoint(new Vector3(0f, 0.36f, 0f));
                var receiver = FeaturePrefab.Ensure<VRCContactReceiver>(strike);
                receiver.shapeType = ContactBase.ShapeType.Sphere;
                receiver.radius = 0.11f;
                // Only the wearer's client keeps score.
                receiver.localOnly = true;
                receiver.allowSelf = true;
                receiver.allowOthers = true;
                // VRChat gives every avatar contacts with these tags.
                receiver.collisionTags = new List<string> { "Head", "Torso" };
                receiver.receiverType = ContactReceiver.ReceiverType.OnEnter;
                // Resting the mallet on someone is not a bonk.
                receiver.minVelocity = 0.6f;
                receiver.parameter = hit;

                WirenookSounds.Source(root);

                var fx = ControllerBuilder.Rebuild(Generated + "/hammer-fx.controller");
                // The stars stay folded away whatever the mallet is doing; the
                // layer below unfolds them for a hit.
                PopToggle.Layer(fx, "Hammer", parameter, bodyPath, (clip, phase) =>
                    PopToggle.Scale(clip, impactPath, AnimationCurve.Constant(0f, 1f / 60f, 0f)));

                fx.Bool(bonk).Bool(hit).Bool("IsLocal");
                var burst = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.06f, 1.25f), new Keyframe(0.3f, 0f));
                for (var key = 0; key < burst.length; key++) burst.SmoothTangents(key, 0f);
                AnimationClip Stars(string name, AnimationCurve scale) =>
                    fx.Clip("bonk " + name, clip => PopToggle.Scale(clip, impactPath, scale));

                var layer = fx.Layer("Bonk");
                var idle = layer.State("Idle", Stars("idle", AnimationCurve.Constant(0f, 1f / 60f, 0f)));
                var landed = layer.State("Landed", Stars("landed", burst));
                var spent = layer.State("Spent", Stars("spent", AnimationCurve.Constant(0f, 1f / 60f, 0f)));
                WirenookSounds.PlayOnEnter(landed, WirenookSounds.SourceName, "bonk");
                idle.To(landed).IfTrue(bonk);
                landed.To(spent).AfterTime(1f);
                // Waits for the bit to drop, so one hit is one bonk.
                spent.To(idle).IfFalse(bonk);

                var watch = fx.Layer("Bonk Watch");
                var ready = watch.State("Ready");
                var raise = watch.State("Raise", fx.Clip("bonk raise", clip => clip.Hold(0.25f)));
                var lower = watch.State("Lower", fx.Clip("bonk lower", clip => clip.Hold(0.2f)));
                raise.Behaviour<VRCAvatarParameterDriver>(driver => SetLocally(driver, bonk, 1f));
                lower.Behaviour<VRCAvatarParameterDriver>(driver => SetLocally(driver, bonk, 0f));
                ready.To(raise).IfTrue("IsLocal").IfTrue(parameter).IfTrue(hit);
                raise.To(lower).AfterTime(1f);
                lower.To(ready).AfterTime(1f);
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedBool(parameter), FeaturePrefab.SyncedBool(bonk), Unsynced(hit));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Bonk Hammer", "hammer", parameter);
                FeaturePrefab.Describe(root, "bonk-hammer", "Bonk hammer", "hammer", "bonk hammer", 30, joinAvatarMesh: true);
            });
        }

        /// <summary>
        /// The substitution trick: the wearer goes up in a puff of smoke and
        /// a log is left standing where they were. They stay hidden, free to
        /// walk off, until the switch goes off again, when they come back in
        /// another puff wherever they have got to and the log goes.
        ///
        /// The log's place is synced, so people who arrive during the trick
        /// see it where it was left. The wearer is hidden by shrinking the
        /// hips to nothing, which takes the whole skinned body, and anything
        /// worn or held, with it; the log and the smoke hang off the avatar's
        /// root instead and stay.
        /// </summary>
        /// <param name="hipsPath">Path of the hips bone from the avatar's root.</param>
        public static void Vanish(string hipsPath = UsualHipsPath)
        {
            const float coverSeconds = 0.1f;

            SyncedAnchor.Prepare();
            FeaturePrefab.Edit(VanishPrefab, root =>
            {
                // Dropped at the wearer's feet, facing the way they faced.
                var carry = FeaturePrefab.Child(root, "Carry");
                var standIn = FeaturePrefab.Child(root, "Stand In");
                var rig = SyncedAnchor.Configure(root, standIn, carry.transform, VanishParameter);
                var log = RigidProp.Build(standIn, Models + "/log.fbx", Generated);
                var logPath = FeaturePrefab.PathTo(root, FeaturePrefab.Find(log, "Log"));

                // The smoke is part of the avatar's mesh: it only ever shows
                // where the wearer is.
                var smoke = FeaturePrefab.Model(FeaturePrefab.Child(root, "Smoke"), Models + "/puff.fbx");
                var smokePath = FeaturePrefab.PathTo(root, FeaturePrefab.Bone(smoke, "Puff"));
                WirenookSounds.Source(root);

                var fx = ControllerBuilder.Rebuild(Generated + "/vanish-fx.controller");
                rig.DropLayers(fx, "Vanish Log", VanishParameter, logPath);

                // Up fast, hang, and thin away to nothing.
                var billow = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(coverSeconds, 1.15f),
                    new Keyframe(0.38f, 1.4f), new Keyframe(0.65f, 0f));
                for (var key = 0; key < billow.length; key++) billow.SmoothTangents(key, 0f);
                AnimationClip Smoke(string name, AnimationCurve scale) =>
                    fx.Clip("vanish smoke " + name, clip => PopToggle.Scale(clip, smokePath, scale));
                var clear = AnimationCurve.Constant(0f, 1f / 60f, 0f);

                var layer = fx.Layer("Vanish Smoke");
                var here = layer.State("Here", Smoke("here", clear));
                var going = layer.State("Going", Smoke("going", billow));
                var gone = layer.State("Gone", Smoke("gone", clear));
                var coming = layer.State("Coming", Smoke("coming", billow));
                WirenookSounds.PlayOnEnter(going, WirenookSounds.SourceName, "poof");
                WirenookSounds.PlayOnEnter(coming, WirenookSounds.SourceName, "poof");
                here.To(going).IfTrue(VanishParameter);
                going.To(gone).AfterTime(1f);
                gone.To(coming).IfFalse(VanishParameter);
                coming.To(here).AfterTime(1f);
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);

                // The one animation that reaches outside this prefab, into
                // the avatar it is placed on. It is merged by its path from
                // the avatar's root, on an object of its own.
                var hide = ControllerBuilder.Rebuild(Generated + "/vanish-hide-fx.controller");
                hide.Bool(VanishParameter);
                AnimationClip Body(string name, float scale, float seconds = 1f / 60f) => hide.Clip("vanish body " + name,
                    clip => PopToggle.Scale(clip, hipsPath, AnimationCurve.Constant(0f, seconds, scale)));
                var body = hide.Layer("Vanish Body");
                var shown = body.State("Shown", Body("shown", 1f));
                // Each change waits until the smoke is thick enough to hide it.
                var covering = body.State("Covering", Body("covering", 1f, coverSeconds));
                var hidden = body.State("Hidden", Body("hidden", 0f));
                var uncovering = body.State("Uncovering", Body("uncovering", 0f, coverSeconds));
                shown.To(covering).IfTrue(VanishParameter);
                covering.To(hidden).AfterTime(1f);
                hidden.To(uncovering).IfFalse(VanishParameter);
                uncovering.To(shown).AfterTime(1f);
                hide.Save();

                var hideHolder = FeaturePrefab.Child(root, "Hide Body");
                var merge = FeaturePrefab.Ensure<ModularAvatarMergeAnimator>(hideHolder);
                merge.animator = hide.Controller;
                merge.layerType = VRCAvatarDescriptor.AnimLayerType.FX;
                merge.pathMode = MergeAnimatorPathMode.Absolute;
                merge.matchAvatarWriteDefaults = true;
                merge.deleteAttachedAnimator = true;

                FeaturePrefab.Parameters(root, rig.Parameters().Prepend(FeaturePrefab.SyncedBool(VanishParameter)).ToArray());
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Vanish", "vanish", VanishParameter);
                FeaturePrefab.Describe(root, "vanishing-act", "Vanishing act", "vanish", "vanish", 45, joinAvatarMesh: true);
            });
        }

        /// <summary>
        /// A little window that hovers by the wearer's head while they are
        /// looking at something nobody else can see: a desktop overlay, a
        /// menu, a message. It is one synced switch, <c>WN/Busy</c>, in the
        /// menu and open to OSC, so a program that knows when an overlay is
        /// up can set it; VRChat itself does not tell an avatar.
        /// </summary>
        public static void Busy()
        {
            const string parameter = "WN/Busy";
            FeaturePrefab.Edit(BusyPrefab, root =>
            {
                var mount = FeaturePrefab.Child(root, "Mount");
                AttachUpright(mount, HumanBodyBones.Head);
                var place = FeaturePrefab.Child(mount, "Place");
                // Beside the head and a little above it, clear of ears and hats.
                place.transform.localPosition = new Vector3(0.27f, 0.2f, 0.04f);
                var model = FeaturePrefab.Model(place, Models + "/busy.fbx");
                var sign = FeaturePrefab.PathTo(root, FeaturePrefab.Bone(model, "Busy"));

                var fx = ControllerBuilder.Rebuild(Generated + "/busy-fx.controller");
                PopToggle.Layer(fx, "Busy Sign", parameter, sign, (clip, phase) =>
                {
                    if (phase != "on") return;
                    // It rocks a little, so it reads as held up rather than stuck on.
                    var rock = new AnimationCurve(new Keyframe(0f, -5f), new Keyframe(1.3f, 5f), new Keyframe(2.6f, -5f));
                    for (var key = 0; key < rock.length; key++) rock.SmoothTangents(key, 0f);
                    clip.Curve(sign, typeof(Transform), "localEulerAnglesRaw.z", rock).Loop();
                });
                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedBool(parameter));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "Busy", "busy", parameter);
                FeaturePrefab.Describe(root, "busy-sign", "Busy sign", "busy", "busy sign", 15, joinAvatarMesh: true);
            });
        }

        /// <summary>All the tricks behind one "Tricks" menu entry.</summary>
        private static void Bundle()
        {
            FeaturePrefab.Edit(BundlePrefab, root =>
            {
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuFolder(root, "Tricks", "tricks");
                var wanted = new[] { HammerPrefab, VanishPrefab, CensorPrefab, BusyPrefab }
                    .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path)).ToList();
                foreach (var child in root.transform.Cast<Transform>().ToList())
                {
                    if (!wanted.Contains(PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject)))
                        Object.DestroyImmediate(child.gameObject);
                }

                for (var i = 0; i < wanted.Count; i++)
                {
                    var prefab = wanted[i];
                    var instance = root.transform.Cast<Transform>()
                                       .Select(child => child.gameObject)
                                       .FirstOrDefault(child => PrefabUtility.GetCorrespondingObjectFromSource(child) == prefab)
                                   ?? (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    // The menu lists them in the order they sit in.
                    instance.transform.SetSiblingIndex(i);
                    // Each trick installs itself at the top of the menu when
                    // used alone; inside the set the folder above does it.
                    var installer = instance.GetComponent<ModularAvatarMenuInstaller>();
                    if (installer != null) Object.DestroyImmediate(installer);
                }
            });
        }

        /// <summary>
        /// Moves an object onto a bone at build time but leaves it upright,
        /// so offsets below it are in the avatar's own directions whichever
        /// way the rig's bone axes happen to point.
        /// </summary>
        private static void AttachUpright(GameObject target, HumanBodyBones bone)
        {
            FeaturePrefab.AttachToBone(target, bone);
            target.GetComponent<ModularAvatarBoneProxy>().attachmentMode = BoneProxyAttachmentMode.AsChildKeepRotation;
        }

        private static void SetLocally(VRCAvatarParameterDriver driver, string parameter, float value)
        {
            driver.localOnly = true;
            driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
            {
                new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = parameter, value = value },
            };
        }

        private static ParameterConfig Unsynced(string name)
        {
            return new ParameterConfig { nameOrPrefix = name, syncType = ParameterSyncType.NotSynced, localOnly = true };
        }
    }
}
