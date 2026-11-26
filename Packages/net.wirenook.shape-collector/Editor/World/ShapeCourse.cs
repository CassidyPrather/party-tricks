using System;
using System.Collections.Generic;
using UdonSharp;
using UnityEngine;
using VRC.Udon;
using Wirenook.SkillChecks;
using Wirenook.SkillChecks.Editor;

namespace Wirenook.ShapeCollector.World.Editor
{
    /// <summary>One shape of a course: what it is called, where it stands, and what it looks like.</summary>
    public sealed class ShapeCourseShape
    {
        /// <summary>The name of its object under the course's root. Unique within the course.</summary>
        public string Name;

        /// <summary>Where it stands, local to the course's root: the point on the ground under it.</summary>
        public Vector3 Position;

        /// <summary>
        /// Fills the object that is on show while this shape is the next one to take: add the
        /// mesh, or whatever stands for the shape, under the object handed in. Its origin is the
        /// middle of the shape. Called every time the course is built.
        /// </summary>
        public Action<GameObject> Make;

        public ShapeCourseShape(string name, Vector3 position, Action<GameObject> make)
        {
            Name = name;
            Position = position;
            Make = make;
        }
    }

    /// <summary>What a course is made of. Only <see cref="Shapes"/> is required.</summary>
    public sealed class ShapeCourseOptions
    {
        /// <summary>The shapes, in the order they are to be taken.</summary>
        public readonly List<ShapeCourseShape> Shapes = new List<ShapeCourseShape>();

        /// <summary>How far above its point on the ground the middle of a shape, and of what feels for it, is.</summary>
        public float Height = 1.1f;

        /// <summary>The radius of the contact receiver round each shape.</summary>
        public float Radius = 0.45f;

        /// <summary>
        /// The contact tags that take a shape. The default is the senders VRChat puts on every
        /// avatar's body, as the avatar package's course uses: walking into a shape takes it.
        /// </summary>
        public string[] Tags = { SkillTags.Head, SkillTags.Torso, SkillTags.Hand, SkillTags.Foot, SkillTags.Finger };

        /// <summary>
        /// Tags that take a shape out of turn, or put a taken one back, at a touch: a master key.
        /// None by default.
        /// </summary>
        public string[] MasterTags = new string[0];

        /// <summary>Objects that bar the way: shown until the last shape is taken. Made by the caller.</summary>
        public GameObject[] Gate = new GameObject[0];

        /// <summary>Objects shown once the last shape is taken. Made by the caller.</summary>
        public GameObject[] Open = new GameObject[0];

        /// <summary>Where the gate's switch goes. Optional; the course's root when not given.</summary>
        public GameObject GateHolder;

        /// <summary>
        /// Something with a collider, made by the caller, that becomes the control which puts the
        /// first shape back. Optional.
        /// </summary>
        public GameObject StartOver;

        /// <summary>What a player reads when aiming at the start-over control.</summary>
        public string StartOverText = "Start over";

        /// <summary>
        /// Finds the child of that name under a parent, or makes it. Give the one your generator
        /// uses if it keeps track of what it made; the default looks with Transform.Find and
        /// creates an empty object when there is none, so that building twice changes nothing.
        /// </summary>
        public Func<GameObject, string, GameObject> Node = FindOrMake;

        /// <summary>
        /// Called for each shape with the object its check is on and the check, after both exist:
        /// the place to add a sound, a light, or anything else that should follow the taking of
        /// that shape. Optional.
        /// </summary>
        public Action<GameObject, SkillCheck> Feedback;

        private static GameObject FindOrMake(GameObject parent, string name)
        {
            Transform found = parent.transform.Find(name);
            if (found != null) return found.gameObject;
            var made = new GameObject(name);
            made.transform.SetParent(parent.transform, false);
            return made;
        }
    }

    /// <summary>What <see cref="ShapeCourse.Build"/> made.</summary>
    public sealed class ShapeCourseResult
    {
        /// <summary>One check per shape, in order: complete once that shape is taken. The last one being complete is the gate being open.</summary>
        public SkillCheck[] Taken;

        /// <summary>The object of each shape under the root, in order: the place to add a mark on the ground or a label.</summary>
        public GameObject[] Spots;

        /// <summary>
        /// Every component the course put on an object, the caller's objects included. A generator
        /// that removes what it did not make itself has to be told about these.
        /// </summary>
        public readonly List<Component> Components = new List<Component>();
    }

    /// <summary>
    /// The shape collector as part of a world: shapes to be taken in order by touching them, only
    /// the next one on show, and a gate that opens after the last. The same game the avatar
    /// package lays out from a menu, here built into a scene from editor code.
    ///
    /// There is no behaviour of its own behind it. Each shape is a skill check
    /// (the skill checks of net.wirenook.contacts) that latches at a touch and needs, besides a contact, the shape
    /// before it to be complete; what is shown follows from those checks. So it is synced, and
    /// right for late joiners, the way every skill check is.
    ///
    ///     var options = new ShapeCourseOptions { Gate = new[] { bars }, StartOver = button };
    ///     options.Shapes.Add(new ShapeCourseShape("Pyramid", new Vector3(-2.5f, 0f, 0f), holder => ...));
    ///     options.Shapes.Add(new ShapeCourseShape("Cube", Vector3.zero, holder => ...));
    ///     ShapeCourseResult course = ShapeCourse.Build(root, options);
    ///
    /// Under the root it makes, per shape, an object of the shape's name holding "Check" (the
    /// receiver and the check; always on), "Turn" (on from when the shape before is taken) with
    /// "Shape" under it (on until this one is taken; what Make fills), and the two switches.
    /// Building again over what is there changes nothing.
    /// </summary>
    public static class ShapeCourse
    {
        public static ShapeCourseResult Build(GameObject root, ShapeCourseOptions options)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.Shapes.Count == 0) throw new ArgumentException("a course needs at least one shape");

            int count = options.Shapes.Count;
            var result = new ShapeCourseResult { Taken = new SkillCheck[count], Spots = new GameObject[count] };
            var above = new Vector3(0f, options.Height, 0f);

            for (int i = 0; i < count; i++)
            {
                ShapeCourseShape shape = options.Shapes[i];
                GameObject spot = options.Node(root, shape.Name);
                spot.transform.localPosition = shape.Position;
                result.Spots[i] = spot;

                // A touch takes it for good, but only once the one before it is taken: the second
                // group is a signal no contact can bring.
                var spec = new CheckSpec { AdvanceRate = 50f, Latch = true }.Need(TagGroup.AnyOf(options.Tags));
                spec.CompleteTags = options.MasterTags;
                spec.ResetTags = options.MasterTags;
                if (i > 0) spec.Need(TagGroup.AnyOf("@before")).Signal(SignalSpec.WhenComplete("@before"));

                GameObject feeler = options.Node(spot, "Check");
                feeler.transform.localPosition = above;
                SkillCheck taken = i == 0
                    ? SkillCheckBuilder.Check(feeler, spec)
                    : SkillCheckBuilder.Check(feeler, spec, result.Taken[i - 1]);
                taken.receiver.shapeType = VRC.Dynamics.ContactBase.ShapeType.Sphere;
                taken.receiver.radius = options.Radius;
                taken.receiver.position = Vector3.zero;
                UnityEditor.EditorUtility.SetDirty(taken.receiver);
                result.Taken[i] = taken;
                result.Components.Add(taken.receiver);
                Kept(result, taken);

                // On show from when the one before is taken until it is taken itself: two switches,
                // one on each of two nested objects. Neither sits on what it switches off.
                GameObject turn = options.Node(spot, "Turn");
                GameObject shown = options.Node(turn, "Shape");
                shown.transform.localPosition = above;
                shape.Make?.Invoke(shown);

                if (i > 0) Switch(result, options.Node(spot, "Switch Turn"), result.Taken[i - 1], new[] { turn }, null);
                else turn.SetActive(true);
                Switch(result, options.Node(spot, "Switch Shape"), taken, null, new[] { shown });

                options.Feedback?.Invoke(feeler, taken);
            }

            SkillCheck last = result.Taken[count - 1];
            if (options.Gate.Length > 0 || options.Open.Length > 0)
                Switch(result, options.Node(options.GateHolder != null ? options.GateHolder : root, "Switch"), last, options.Open, options.Gate);

            if (options.StartOver != null)
            {
                SkillButton again = UdonPrograms.Behaviour<SkillButton>(options.StartOver);
                again.checks = result.Taken;
                again.completes = false;
                UdonPrograms.Apply(again, options.StartOverText);
                Kept(result, again);
            }

            return result;
        }

        /// <summary>A switch on <paramref name="holder"/>: some objects while a check is complete, others while it is not, saved the way the check starts.</summary>
        private static void Switch(ShapeCourseResult result, GameObject holder, SkillCheck check, GameObject[] whenComplete, GameObject[] whenIncomplete)
        {
            whenComplete = whenComplete ?? new GameObject[0];
            whenIncomplete = whenIncomplete ?? new GameObject[0];
            foreach (GameObject shown in whenComplete) shown.SetActive(false);
            foreach (GameObject shown in whenIncomplete) shown.SetActive(true);

            SkillToggle toggle = UdonPrograms.Behaviour<SkillToggle>(holder);
            toggle.check = check;
            toggle.whenComplete = whenComplete;
            toggle.whenIncomplete = whenIncomplete;
            toggle.whenAdvancing = new GameObject[0];
            toggle.whenRegressing = new GameObject[0];
            toggle.rangeMin = 0f;
            toggle.rangeMax = 1f;
            toggle.insideRange = new GameObject[0];
            toggle.belowRange = new GameObject[0];
            toggle.aboveRange = new GameObject[0];
            UdonPrograms.Apply(toggle);
            Kept(result, toggle);
        }

        /// <summary>Records an UdonSharp behaviour and the UdonBehaviour behind it.</summary>
        private static void Kept(ShapeCourseResult result, UdonSharpBehaviour behaviour)
        {
            result.Components.Add(behaviour);
            UdonBehaviour backing = UdonSharpEditor.UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour);
            if (backing != null) result.Components.Add(backing);
        }
    }
}
