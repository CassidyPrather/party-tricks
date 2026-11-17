using System;
using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDKBase;
using Wirenook.Assets.Editor;
using Wirenook.WorldSync.Editor;

namespace Wirenook.ShapeCollector.Editor
{
    /// <summary>
    /// Generates the shape collector: a course the wearer lays out in any
    /// world, then opens to the instance. Three blue shapes are collected in
    /// order by walking into them, only the next one showing at a time, and
    /// when the last is taken a gate goes away.
    ///
    /// Six things are placed: the three shapes, the two ends of the gate and
    /// a sign that names the next shape. They ride an
    /// <see cref="AnchorBus"/>, so all six cost what one synced position
    /// would, and the layout is kept with the avatar between sessions.
    ///
    /// Everything shown is one skinned mesh whose bones are held on the
    /// bus's slots. A thing left in the world has to be drawn apart from the
    /// avatar's own mesh, and one mesh with a bone per thing is one mesh and
    /// one material slot however many things there are.
    ///
    /// The wearer's client keeps score. Contacts on the shapes feel any
    /// avatar's standard body contacts, and the count of shapes taken is one
    /// synced number that every client shows the course from.
    /// </summary>
    public static class ShapeCollectorBuild
    {
        public const string Package = "Packages/net.wirenook.shape-collector";
        private const string Models = Package + "/Models";
        private const string Generated = Package + "/Generated";
        private const string Prefabs = Package + "/Prefabs";

        public const string CoursePrefab = Prefabs + "/Wirenook Shape Collector.prefab";
        public const string Controller = Generated + "/shape-collector-fx.controller";

        public const string BusPrefix = "SC/Bus";

        /// <summary>Set to a slot's number by the menu to place that thing where the wearer is pointing.</summary>
        public const string Place = "SC/Place";
        public const string Clear = "SC/Clear";

        /// <summary>Synced: the course is open to the instance.</summary>
        public const string Live = "SC/Live";

        /// <summary>Synced: how many shapes have been taken, 0 to 3. At 3 the gate is open.</summary>
        public const string Next = "SC/Next";
        public const string Reset = "SC/Reset";

        private static readonly string[] ShapeNames = { "Tri", "Square", "Circle" };
        private static readonly string[] ShapeLabels = { "Triangle", "Square", "Circle" };
        private const int PostA = 4;
        private const int PostB = 5;
        private const int Sign = 6;
        public const int Slots = 6;
        private const int Bars = 5;

        private static string Hit(int shape) => "SC/Hit" + shape;

        /// <summary>Checks the course's bus as far as the editor can; see <see cref="AnchorBusCheck"/>.</summary>
        [MenuItem("Tools/Wirenook/Shape Collector/Verify")]
        public static string Verify()
        {
            var report = AnchorBusCheck.Run(CoursePrefab, Controller, BusPrefix, Slots, Place, Clear, Live);
            Debug.Log("[shape-collector] " + report);
            return report;
        }

        [MenuItem("Tools/Wirenook/Shape Collector/Rebuild")]
        public static void Rebuild()
        {
            SyncedAnchor.Prepare();
            FeaturePrefab.Edit(CoursePrefab, root =>
            {
                // Shapes are put down a step ahead at chest height, where
                // they hover; the gate and the sign on the ground.
                var chest = FeaturePrefab.Child(root, "Carry Chest");
                chest.transform.localPosition = new Vector3(0f, 1.1f, 0.9f);
                var ground = FeaturePrefab.Child(root, "Carry Ground");
                ground.transform.localPosition = new Vector3(0f, 0f, 0.9f);
                var bus = AnchorBus.Configure(root, BusPrefix, Slots, new[] { chest.transform, ground.transform });
                var world = bus.Pose(1).parent.parent;

                var model = FeaturePrefab.Model(FeaturePrefab.Child(root, "Course"), Models + "/shapes.fbx");
                Transform Bone(string name) => FeaturePrefab.Bone(model, "Shapes." + name);
                string Path(string name) => FeaturePrefab.PathTo(root, Bone(name));

                for (var shape = 1; shape <= ShapeNames.Length; shape++) Hold(Bone(ShapeNames[shape - 1]), bus.Pose(shape));
                Hold(Bone("PostA"), bus.Pose(PostA));
                Hold(Bone("PostB"), bus.Pose(PostB));
                Hold(Bone("Sign"), bus.Pose(Sign));
                for (var bar = 1; bar <= Bars; bar++)
                {
                    // Evenly between the posts, wherever the posts are.
                    var share = (float)bar / (Bars + 1);
                    var between = Constrain<VRCPositionConstraint>(Bone("Bar" + bar).gameObject);
                    between.AddSource(new VRCConstraintSource { SourceTransform = bus.Pose(PostA), Weight = 1f - share });
                    between.AddSource(new VRCConstraintSource { SourceTransform = bus.Pose(PostB), Weight = share });
                }

                // The mesh is drawn wherever its bones are, which can be
                // anywhere in the world, so its bounds are the bus's whole
                // range, measured from the world's origin. The object they
                // are measured from rests at no size, which makes them a
                // point until there is a course to show.
                var extent = FeaturePrefab.Child(world.gameObject, "Extent");
                var skin = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                skin.rootBone = extent.transform;
                skin.localBounds = new Bounds(Vector3.zero, Vector3.one * (2f * bus.Grid.Range));
                skin.updateWhenOffscreen = false;
                var extentPath = FeaturePrefab.PathTo(root, extent.transform);

                for (var shape = 1; shape <= ShapeNames.Length; shape++)
                {
                    var touch = FeaturePrefab.Ensure<VRCContactReceiver>(FeaturePrefab.Child(Bone(ShapeNames[shape - 1]).gameObject, "Touch"));
                    touch.shapeType = ContactBase.ShapeType.Sphere;
                    touch.radius = 0.45f;
                    // Only the wearer's client keeps score.
                    touch.localOnly = true;
                    touch.allowSelf = true;
                    touch.allowOthers = true;
                    // VRChat gives every avatar contacts with these tags.
                    touch.collisionTags = new List<string> { "Head", "Torso", "Hand", "Foot", "Finger" };
                    touch.receiverType = ContactReceiver.ReceiverType.Constant;
                    touch.parameter = Hit(shape);
                }

                WirenookSounds.Source(root);

                var fx = ControllerBuilder.Rebuild(Controller);
                fx.Int(Place).Bool(Clear).Bool(Live).Int(Next).Bool(Reset).Bool("IsLocal");
                for (var shape = 1; shape <= ShapeNames.Length; shape++) fx.Bool(Hit(shape));
                bus.AddLayers(fx, Place, Clear, Live, new[] { 0, 0, 0, 1, 1, 1 });

                // While it is being laid out, only the wearer sees it, all of
                // it. Live, everyone sees what the game has on show.
                var laying = new[] { Is("IsLocal"), Not(Live) };
                List<List<Condition>> When(Condition[] placed, params Condition[] live)
                {
                    return new List<List<Condition>>
                    {
                        placed.Concat(new[] { Is(Live) }).Concat(live).ToList(),
                        placed.Concat(laying).ToList(),
                    };
                }

                for (var shape = 1; shape <= ShapeNames.Length; shape++)
                {
                    var spin = Path(ShapeNames[shape - 1] + "Spin");
                    Shown(fx, "SC " + ShapeLabels[shape - 1], new[] { spin },
                        When(new[] { Is(bus.Placed(shape)) }, Equal(Next, shape - 1)), (clip, phase) =>
                        {
                            if (phase != "on") return;
                            clip.Curve(spin, typeof(Transform), "localEulerAnglesRaw.y", AnimationCurve.Linear(0f, 0f, 4f, 360f)).Loop();
                        });
                }

                var shut = Less(Next, ShapeNames.Length);
                Shown(fx, "SC Gate Left", new[] { Path("PostA") }, When(new[] { Is(bus.Placed(PostA)) }, shut));
                Shown(fx, "SC Gate Right", new[] { Path("PostB") }, When(new[] { Is(bus.Placed(PostB)) }, shut));
                Shown(fx, "SC Gate Bars", Enumerable.Range(1, Bars).Select(bar => Path("Bar" + bar)).ToList(),
                    When(new[] { Is(bus.Placed(PostA)), Is(bus.Placed(PostB)) }, shut));
                Shown(fx, "SC Sign", new[] { Path("Sign") }, When(new[] { Is(bus.Placed(Sign)) }));
                for (var shape = 1; shape <= ShapeNames.Length; shape++)
                {
                    Shown(fx, "SC Sign " + ShapeLabels[shape - 1], new[] { Path("Sign" + ShapeNames[shape - 1]) },
                        new List<List<Condition>> { new List<Condition> { Equal(Next, shape - 1) } });
                }

                Shown(fx, "SC Sign Open", new[] { Path("SignOpen") },
                    new List<List<Condition>> { new List<Condition> { Equal(Next, ShapeNames.Length) } });

                // The bounds: whole while there may be something to draw.
                var reach = fx.Layer("SC Extent");
                AnimationClip Extent(bool whole) => fx.Clip("SC extent " + (whole ? "whole" : "none"), clip =>
                    PopToggle.Scale(clip, extentPath, AnimationCurve.Constant(0f, 1f / 60f, whole ? 1f : 0f)));
                var none = reach.State("None", Extent(false));
                var whole = reach.State("Whole", Extent(true));
                AnyOf(none.To(whole), new List<List<Condition>> { new List<Condition> { Is(Live) }, new List<Condition> { Is("IsLocal") } });
                AnyOf(whole.To(none), new List<List<Condition>> { new List<Condition> { Not(Live), Not("IsLocal") } });

                // Keeping score, on the wearer's client. Each shape counts
                // only while it is the next one.
                var game = fx.Layer("SC Game");
                var taken = new List<StateBuilder>();
                for (var count = 0; count <= ShapeNames.Length; count++)
                {
                    var value = count;
                    var state = game.State("Taken " + count);
                    state.Behaviour<VRCAvatarParameterDriver>(driver =>
                    {
                        driver.localOnly = true;
                        driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>
                        {
                            new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = Next, value = value },
                        };
                    });
                    taken.Add(state);
                }

                for (var count = 0; count < ShapeNames.Length; count++)
                    taken[count].To(taken[count + 1]).IfTrue("IsLocal").IfTrue(Live).IfTrue(Hit(count + 1));
                // Closing the course, or asking to, starts it over.
                for (var count = 1; count <= ShapeNames.Length; count++)
                    taken[count].To(taken[0]).IfTrue(Reset).Or().IfFalse(Live);

                // The one sound: a shape has been taken.
                var chime = fx.Layer("SC Chime");
                var quiet = chime.State("Quiet");
                chime.Any(quiet).Equal(Next, 0).Or().IfFalse(Live);
                for (var count = 1; count <= ShapeNames.Length; count++)
                {
                    var rang = chime.State("Rang " + count);
                    WirenookSounds.PlayOnEnter(rang, WirenookSounds.SourceName, "collect", 0.6f);
                    chime.Any(rang).Equal(Next, count).IfTrue(Live);
                }

                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                var parameters = bus.Parameters().ToList();
                parameters.Add(FeaturePrefab.SyncedBool(Live));
                parameters.Add(FeaturePrefab.SyncedInt(Next));
                // In the menu, so they are expression parameters; nobody else
                // needs them, so they are not sent.
                parameters.Add(new ParameterConfig { nameOrPrefix = Place, syncType = ParameterSyncType.Int, localOnly = true });
                parameters.Add(new ParameterConfig { nameOrPrefix = Clear, syncType = ParameterSyncType.Bool, localOnly = true });
                parameters.Add(new ParameterConfig { nameOrPrefix = Reset, syncType = ParameterSyncType.Bool, localOnly = true });
                for (var shape = 1; shape <= ShapeNames.Length; shape++)
                    parameters.Add(new ParameterConfig { nameOrPrefix = Hit(shape), syncType = ParameterSyncType.NotSynced, localOnly = true });
                FeaturePrefab.Parameters(root, parameters.ToArray());

                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuFolder(root, "Shape Course", "tricks");
                var placing = FeaturePrefab.Child(root, "Menu Place");
                FeaturePrefab.MenuFolder(placing, "Place", "drop");
                var labels = ShapeLabels.Concat(new[] { "Gate Left", "Gate Right", "Sign" }).ToList();
                for (var slot = 1; slot <= Slots; slot++)
                    FeaturePrefab.MenuButton(FeaturePrefab.Child(placing, "Menu " + slot), labels[slot - 1], "drop", Place, slot);
                FeaturePrefab.MenuToggle(FeaturePrefab.Child(root, "Menu Live"), "Open Course", "world-anchor", Live);
                FeaturePrefab.MenuButton(FeaturePrefab.Child(root, "Menu Reset"), "Start Over", "reset", Reset);
                FeaturePrefab.MenuButton(FeaturePrefab.Child(root, "Menu Clear"), "Clear All", "pick-up", Clear);
                FeaturePrefab.Describe(root, "shape-collector", "Shape collector", "tricks", "shape collector", 5);
            });
            AssetDatabase.SaveAssets();
            Debug.Log("[shape-collector] Rebuilt the course.");
        }

        /// <summary>Holds a bone on a slot of the bus: there, and upright in the world's axes.</summary>
        private static void Hold(Transform bone, Transform pose)
        {
            Constrain<VRCParentConstraint>(bone.gameObject).AddSource(new VRCConstraintSource { SourceTransform = pose, Weight = 1f });
        }

        private static T Constrain<T>(GameObject target) where T : VRCConstraintBase
        {
            var constraint = FeaturePrefab.Ensure<T>(target);
            constraint.ClearSources();
            constraint.IsActive = true;
            // Unlocked constraints re-measure their offset instead of moving the object.
            constraint.Locked = true;
            constraint.FreezeToWorld = false;
            constraint.RebakeOffsetsWhenUnfrozen = false;
            return constraint;
        }

        /// <summary>
        /// A layer that pops some bones in while any one set of conditions
        /// holds, and away when none does.
        /// </summary>
        private static void Shown(ControllerBuilder fx, string name, IReadOnlyList<string> paths, List<List<Condition>> when,
            Action<ClipBuilder, string> extras = null)
        {
            PopToggle.Layer(fx, name, paths[0], on => AnyOf(on, when), off => AnyOf(off, Condition.NoneOf(when)), (clip, phase) =>
            {
                // The first bone's curve is the layer's own; the rest follow it.
                var seconds = phase == "appear" ? PopToggle.AppearSeconds : phase == "disappear" ? PopToggle.DisappearSeconds : 1f / 60f;
                var scale = phase == "appear" ? PopToggle.Overshoot(0f, 1f, seconds)
                    : phase == "disappear" ? PopToggle.Overshoot(1f, 0f, seconds)
                    : AnimationCurve.Constant(0f, seconds, phase == "on" ? 1f : 0f);
                foreach (var path in paths.Skip(1)) PopToggle.Scale(clip, path, scale);
                extras?.Invoke(clip, phase);
            });
        }

        /// <summary>Makes a transition fire when every condition of any one set holds.</summary>
        private static void AnyOf(TransitionBuilder transition, List<List<Condition>> sets)
        {
            if (sets.Count == 0)
            {
                // Never: a transition with no conditions would fire at once.
                transition.IfTrue("IsLocal").IfFalse("IsLocal");
                return;
            }

            for (var i = 0; i < sets.Count; i++)
            {
                if (i > 0) transition.Or();
                foreach (var condition in sets[i]) condition.AddTo(transition);
            }
        }

        private static Condition Is(string parameter) => new Condition(parameter, Condition.Kind.True, 0);
        private static Condition Not(string parameter) => new Condition(parameter, Condition.Kind.False, 0);
        private static Condition Equal(string parameter, int value) => new Condition(parameter, Condition.Kind.Equal, value);
        private static Condition Less(string parameter, int value) => new Condition(parameter, Condition.Kind.Less, value);
    }

    /// <summary>One test of one animator parameter, with its opposite.</summary>
    internal readonly struct Condition : IEquatable<Condition>
    {
        public enum Kind
        {
            True,
            False,
            Equal,
            NotEqual,
            Less,
            Greater,
        }

        public readonly string Parameter;
        public readonly Kind Test;
        public readonly int Value;

        public Condition(string parameter, Kind test, int value)
        {
            Parameter = parameter;
            Test = test;
            Value = value;
        }

        /// <summary>The test that holds exactly when this one does not. Numbers are whole.</summary>
        public Condition Opposite
        {
            get
            {
                switch (Test)
                {
                    case Kind.True: return new Condition(Parameter, Kind.False, 0);
                    case Kind.False: return new Condition(Parameter, Kind.True, 0);
                    case Kind.Equal: return new Condition(Parameter, Kind.NotEqual, Value);
                    case Kind.NotEqual: return new Condition(Parameter, Kind.Equal, Value);
                    case Kind.Less: return new Condition(Parameter, Kind.Greater, Value - 1);
                    default: return new Condition(Parameter, Kind.Less, Value + 1);
                }
            }
        }

        public void AddTo(TransitionBuilder transition)
        {
            switch (Test)
            {
                case Kind.True: transition.IfTrue(Parameter); break;
                case Kind.False: transition.IfFalse(Parameter); break;
                case Kind.Equal: transition.Equal(Parameter, Value); break;
                case Kind.NotEqual: transition.NotEqual(Parameter, Value); break;
                case Kind.Less: transition.Less(Parameter, Value); break;
                default: transition.Greater(Parameter, Value); break;
            }
        }

        public bool Equals(Condition other) => Parameter == other.Parameter && Test == other.Test && Value == other.Value;
        public override bool Equals(object other) => other is Condition condition && Equals(condition);
        public override int GetHashCode() => (Parameter ?? "").GetHashCode() ^ ((int)Test * 397) ^ Value;

        /// <summary>
        /// Turns "none of these hold" into "one of these holds". Each entry
        /// is a set of conditions that must all hold; so is each entry of
        /// the result. Sets that would need a condition both to hold and to
        /// fail are dropped.
        /// </summary>
        public static List<List<Condition>> NoneOf(IEnumerable<List<Condition>> anyOf)
        {
            var result = new List<List<Condition>> { new List<Condition>() };
            foreach (var all in anyOf)
            {
                var next = new List<List<Condition>>();
                foreach (var chosen in result)
                {
                    foreach (var condition in all)
                    {
                        if (chosen.Contains(condition)) continue;
                        var extended = new List<Condition>(chosen);
                        if (!extended.Contains(condition.Opposite)) extended.Add(condition.Opposite);
                        if (!next.Any(known => known.Count == extended.Count && known.All(extended.Contains))) next.Add(extended);
                    }
                }

                result = next;
            }

            return result;
        }
    }
}
