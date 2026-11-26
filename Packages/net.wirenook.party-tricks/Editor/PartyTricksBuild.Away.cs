using System.IO;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using Wirenook.Assets;
using Wirenook.Assets.Editor;

namespace Wirenook.PartyTricks.Editor
{
    public static partial class PartyTricksBuild
    {
        public const string AwayPrefab = Prefabs + "/Wirenook Away.prefab";
        public const string AwayLitePrefab = Prefabs + "/Wirenook Away (Lite).prefab";

        /// <summary>
        /// The number of the error window among an avatar's signs (see
        /// <see cref="SignMesh"/>): one sign shows at a time, and each
        /// feature that brings one has its own number.
        /// </summary>
        public const int AwaySign = 1;

        private const string ProxyClips = "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/ProxyAnim/";

        /// <summary>Seconds from stepping away to standing crashed, and from coming back to being back.</summary>
        private const float AwayLeaveSeconds = 0.6f;
        private const float AwayReturnSeconds = 0.45f;

        /// <summary>
        /// Seconds the crashed state takes to come round to the same glitch
        /// again, and when in it the hard one lands. The body's flick to a
        /// T-pose is timed to that one.
        /// </summary>
        private const float AwayPeriod = 6f;
        private const float AwayJolt = 2.2f;
        private const float AwayJoltSeconds = 0.12f;

        /// <summary>
        /// What the avatar does while its wearer is away: it crashes. The
        /// body locks in a standing pose and stops following the headset,
        /// the mesh tears and snaps and throws out spikes, a device-unplugged
        /// cue plays and a low buzz sets in, and an error window reading AFK
        /// stands at the edge of the avatar on whichever side each viewer
        /// is. Coming back plays the cue in reverse and puts it all right.
        ///
        /// It starts on VRChat's own <c>AFK</c> or on a menu switch,
        /// <see cref="WirenookSignals.Away"/>, which an avatar's own
        /// animator can read as well (a face gone to an error screen, say).
        ///
        /// The glitch, the window and the screen are drawn by the shader of
        /// the materials <c>net.wirenook.assets</c> generates; the feature
        /// animates them through a stand-in for every renderer on the avatar
        /// (<see cref="WirenookAvatarMaterials"/>). On an avatar with other
        /// materials the body still locks and the sounds still play.
        ///
        /// The lite prefab is the same without the window and the sounds:
        /// nothing in it is a mesh, a material or an audio source.
        /// </summary>
        public static void Away()
        {
            Away(AwayPrefab, "away", lite: false);
            Away(AwayLitePrefab, "away-lite", lite: true);
        }

        private static void Away(string prefab, string name, bool lite)
        {
            var action = AwayAction(name);
            FeaturePrefab.Edit(prefab, root =>
            {
                var materials = FeaturePrefab.AvatarMaterials(root);

                var window = root.transform.Find("Window");
                if (lite)
                {
                    if (window != null) Object.DestroyImmediate(window.gameObject);
                    var sound = root.transform.Find(WirenookSounds.SourceName);
                    if (sound != null) Object.DestroyImmediate(sound.gameObject);
                }
                else
                {
                    // A second material on the avatar's mesh, which Android
                    // and iOS have no room for; nothing there could unfold it
                    // anyway, without the shader.
                    var holder = FeaturePrefab.Child(root, "Window");
                    FeaturePrefab.Ensure<WirenookPcOnly>(holder);
                    var mount = FeaturePrefab.Child(holder, "Mount");
                    AttachUpright(mount, HumanBodyBones.Hips);
                    AwayWindow().Place(mount, "Pivot", Generated + "/away-window.asset")
                        // Chest height on a figure about 1.7 m tall; it scales with the avatar.
                        .transform.localPosition = new Vector3(0f, 0.3f, 0f);
                    // A loop, which a cue from another feature would cut off for good.
                    WirenookSounds.Source(root, shared: false);
                }

                var fx = ControllerBuilder.Rebuild(Generated + "/" + name + "-fx.controller");
                fx.Bool(WirenookSignals.Afk).Bool(WirenookSignals.Away);

                AnimationClip Clip(string phase, float seconds, AnimationCurve glitch, AnimationCurve screen, AnimationCurve show, bool loop = false) =>
                    fx.Clip(name + " " + phase, clip =>
                    {
                        clip.Material<SkinnedMeshRenderer>(materials, ToonMaterial.Glitch, glitch)
                            .Material<SkinnedMeshRenderer>(materials, ToonMaterial.Screen, screen);
                        if (!lite)
                        {
                            clip.Material<SkinnedMeshRenderer>(materials, ToonMaterial.Sign, AnimationCurve.Constant(0f, seconds, show == null ? 0f : AwaySign))
                                .Material<SkinnedMeshRenderer>(materials, ToonMaterial.SignShow, show ?? AnimationCurve.Constant(0f, seconds, 0f));
                        }

                        if (loop) clip.Loop();
                    });

                const float moment = 1f / 60f;
                var layer = fx.Layer("Away");
                var here = layer.State("Here", Clip("here", moment, Flat(moment, 0f), Flat(moment, 0f), null));
                // The mesh goes first and hardest; the eyes blank a beat
                // later; the window comes up last, once there is something
                // for it to be about.
                var leaving = layer.State("Leaving", Clip("leaving", AwayLeaveSeconds,
                    Stepped((0f, 0f), (0.04f, 1f), (0.22f, 0.55f), (0.4f, 0.3f), (AwayLeaveSeconds, 0.15f)),
                    Stepped((0f, 0f), (0.16f, 1f), (AwayLeaveSeconds, 1f)),
                    Delayed(0.3f, PopToggle.Overshoot(0f, 1f, PopToggle.AppearSeconds), AwayLeaveSeconds)));
                // A low simmer that never quite settles, one hard jolt each
                // time round and a smaller one later.
                var gone = layer.State("Gone", Clip("gone", AwayPeriod,
                    Stepped((0f, 0.15f), (AwayJolt, 0.9f), (AwayJolt + AwayJoltSeconds, 0.4f), (AwayJolt + 0.3f, 0.12f),
                        (4.4f, 0.5f), (4.5f, 0.15f), (AwayPeriod, 0.15f)),
                    Flat(AwayPeriod, 1f), Flat(AwayPeriod, 1f), loop: true));
                var returning = layer.State("Returning", Clip("returning", AwayReturnSeconds,
                    Stepped((0f, 0.9f), (0.12f, 0.5f), (0.3f, 0.2f), (AwayReturnSeconds, 0f)),
                    Stepped((0f, 1f), (0.3f, 0f), (AwayReturnSeconds, 0f)),
                    Padded(PopToggle.Overshoot(1f, 0f, PopToggle.DisappearSeconds), AwayReturnSeconds)));

                if (!lite)
                {
                    WirenookSounds.PlayOnEnter(leaving, WirenookSounds.SourceName, "unplug");
                    WirenookSounds.PlayOnEnter(gone, WirenookSounds.SourceName, "buzz", loop: true);
                    WirenookSounds.PlayOnEnter(returning, WirenookSounds.SourceName, "plug");
                }

                here.To(leaving).IfTrue(WirenookSignals.Afk).Or().IfTrue(WirenookSignals.Away);
                leaving.To(gone).AfterTime(1f);
                gone.To(returning).IfFalse(WirenookSignals.Afk).IfFalse(WirenookSignals.Away);
                returning.To(here).AfterTime(1f);
                fx.Save();
                FeaturePrefab.Merge(root, fx.Controller);

                // The pose is humanoid muscles, which have no path to make
                // relative: it is merged as it stands, on an object of its own.
                var body = FeaturePrefab.Child(root, "Body Lock");
                var merge = FeaturePrefab.Ensure<ModularAvatarMergeAnimator>(body);
                merge.animator = action;
                merge.layerType = VRCAvatarDescriptor.AnimLayerType.Action;
                merge.pathMode = MergeAnimatorPathMode.Absolute;
                merge.matchAvatarWriteDefaults = true;
                merge.deleteAttachedAnimator = false;

                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedBool(WirenookSignals.Away));
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuToggle(root, "AFK", "away", WirenookSignals.Away);
                FeaturePrefab.Describe(root, "away", "Crash while away", "away", "afk animation", 20, joinAvatarMesh: !lite);
            });
        }

        /// <summary>
        /// The body's part, for the Action playable layer: while away it
        /// stands locked in VRChat's standing pose, stopped dead, with every
        /// part handed from tracking to animation and locomotion off, and
        /// once each time round it flicks to a T-pose for an instant.
        ///
        /// VRChat's own Action controller does the handing over for its own
        /// <c>AFK</c>, and hands back when that ends, whatever a menu switch
        /// says. So the lock is not set once: the holding states are entered
        /// over and over, and each entry sets it again.
        /// </summary>
        private static RuntimeAnimatorController AwayAction(string name)
        {
            AnimationClip Proxy(string clip) =>
                AssetDatabase.LoadAssetAtPath<AnimationClip>(ProxyClips + clip + ".anim")
                ?? throw new FileNotFoundException("The VRChat SDK's sample animations are missing; reinstall the Avatars SDK.", ProxyClips + clip + ".anim");

            var action = ControllerBuilder.Rebuild(Generated + "/" + name + "-action.controller");
            action.Bool(WirenookSignals.Afk).Bool(WirenookSignals.Away);

            // The layers are named by their place: control 0, pose 1, flick 2.
            var control = action.Layer("Away");
            var awake = control.State("Awake");
            var held = control.State("Held", action.Clip(name + " held", clip => clip.Hold(AwayJolt)));
            var flick = control.State("Flick", action.Clip(name + " flick", clip => clip.Hold(AwayJoltSeconds)));
            var settled = control.State("Settled", action.Clip(name + " settled", clip => clip.Hold(AwayPeriod - AwayJolt - AwayJoltSeconds)));

            Lock(awake, false);
            Weight(awake, 1, 0f, 0.25f);
            Weight(awake, 2, 0f, 0f);
            foreach (var state in new[] { held, flick, settled })
            {
                Lock(state, true);
                Weight(state, 1, 1f, 0.2f);
                Weight(state, 2, state == flick ? 1f : 0f, 0f);
                state.To(awake).IfFalse(WirenookSignals.Afk).IfFalse(WirenookSignals.Away);
            }

            awake.To(held).IfTrue(WirenookSignals.Afk).Or().IfTrue(WirenookSignals.Away);
            held.To(flick).AfterTime(1f);
            flick.To(settled).AfterTime(1f);
            settled.To(held).AfterTime(1f);

            // Speed zero: the pose's first frame, held. A crashed body does not breathe.
            action.Layer("Away Pose", weight: 0f).State("Locked", Proxy("proxy_stand_still")).Speed(0f);
            action.Layer("Away Flick", weight: 0f).State("T-Pose", Proxy("proxy_tpose")).Speed(0f);
            action.Save();
            return action.Controller;
        }

        /// <summary>Hands the whole body to animation and stops locomotion, or hands it all back.</summary>
        private static void Lock(StateBuilder state, bool locked)
        {
            var tracking = locked ? VRC_AnimatorTrackingControl.TrackingType.Animation : VRC_AnimatorTrackingControl.TrackingType.Tracking;
            state.Behaviour<VRCAnimatorTrackingControl>(control =>
            {
                control.trackingHead = tracking;
                control.trackingLeftHand = tracking;
                control.trackingRightHand = tracking;
                control.trackingHip = tracking;
                control.trackingLeftFoot = tracking;
                control.trackingRightFoot = tracking;
                control.trackingLeftFingers = tracking;
                control.trackingRightFingers = tracking;
                control.trackingEyes = tracking;
                control.trackingMouth = tracking;
            });
            state.Behaviour<VRCAnimatorLocomotionControl>(control => control.disableLocomotion = locked);
            state.Behaviour<VRCPlayableLayerControl>(control =>
            {
                control.layer = VRC_PlayableLayerControl.BlendableLayer.Action;
                control.goalWeight = locked ? 1f : 0f;
                control.blendDuration = locked ? 0.2f : 0.25f;
            });
        }

        /// <summary>Sets the weight of one of the Action controller's own layers. Modular Avatar moves the index along when it merges.</summary>
        private static void Weight(StateBuilder state, int layer, float goal, float seconds)
        {
            state.Behaviour<VRCAnimatorLayerControl>(control =>
            {
                control.playable = VRC_AnimatorLayerControl.BlendableLayer.Action;
                control.layer = layer;
                control.goalWeight = goal;
                control.blendDuration = seconds;
            });
        }

        /// <summary>
        /// The error window, as a desktop would put one up: a title bar with
        /// a close box, a warning mark, AFK in letters to read across a room,
        /// and a button nobody can press. Lit colours on a dark panel, so it
        /// reads in a dark world and in a bright one. About 46 cm across.
        /// </summary>
        private static SignMesh AwayWindow()
        {
            const float left = -0.224f, right = 0.224f, top = 0.144f;
            const float title = top - 0.024f;
            return new SignMesh(AwaySign)
                // The site's hard extrusion: the window's own outline, set down and to the right.
                .Box("violet", 0, 0.014f, -0.014f, 0.46f, 0.3f)
                .Box("ink", 1, 0f, 0f, 0.46f, 0.3f)
                .Box("night-1", 2, 0f, 0f, right - left, top * 2f)
                .Box("glow-magenta", 3, 0f, title, right - left, 0.048f)
                .Text("ink", 4, "ERROR", left + 0.012f + 0.065f, title, 0.0045f)
                .Box("glow-white", 4, right - 0.026f, title, 0.034f, 0.034f)
                .Text("ink", 5, "x", right - 0.026f, title, 0.004f)
                .Disc("glow-orange", 3, -0.15f, -0.02f, 0.052f, 16)
                .Text("ink", 4, "!", -0.15f, -0.02f, 0.009f)
                .Text("glow-white", 3, "AFK", 0.068f, -0.01f, 0.016f)
                .Box("glow-cyan", 3, 0.15f, -0.11f, 0.11f, 0.042f)
                .Box("night-1", 4, 0.15f, -0.11f, 0.098f, 0.03f)
                .Text("glow-cyan", 5, "OK", 0.15f, -0.11f, 0.0035f);
        }

        private static AnimationCurve Flat(float seconds, float value) => AnimationCurve.Constant(0f, seconds, value);

        /// <summary>A curve that holds each value until the next key and then jumps: nothing about a crash is eased.</summary>
        private static AnimationCurve Stepped(params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (time, value) in keys) curve.AddKey(new Keyframe(time, value));
            for (var key = 0; key < curve.length; key++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, key, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, key, AnimationUtility.TangentMode.Constant);
            }

            return curve;
        }

        /// <summary>A curve played after a wait at its first value, then held at its last until <paramref name="seconds"/>.</summary>
        private static AnimationCurve Delayed(float wait, AnimationCurve curve, float seconds)
        {
            var delayed = new AnimationCurve();
            delayed.AddKey(new Keyframe(0f, curve.keys[0].value));
            foreach (var key in curve.keys) delayed.AddKey(new Keyframe(key.time + wait, key.value, key.inTangent, key.outTangent));
            var last = curve.keys[curve.length - 1];
            if (last.time + wait < seconds) delayed.AddKey(new Keyframe(seconds, last.value));
            return delayed;
        }

        /// <summary>A curve held at its last value until <paramref name="seconds"/>.</summary>
        private static AnimationCurve Padded(AnimationCurve curve, float seconds)
        {
            var padded = new AnimationCurve(curve.keys);
            var last = curve.keys[curve.length - 1];
            if (last.time < seconds) padded.AddKey(new Keyframe(seconds, last.value));
            return padded;
        }
    }
}
