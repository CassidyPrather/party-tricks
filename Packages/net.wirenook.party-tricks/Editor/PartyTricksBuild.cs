using System.Collections.Generic;
using System.Linq;
using nadena.dev.modular_avatar.core;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using Wirenook.Assets.Editor;

namespace Wirenook.PartyTricks.Editor
{
    /// <summary>
    /// Generates the package's prefabs and animators. Everything under
    /// Generated/ and Prefabs/ is rebuilt in place by <see cref="Rebuild"/>;
    /// the sources are the models, the shaders and this file.
    /// </summary>
    public static partial class PartyTricksBuild
    {
        public const string Package = "Packages/net.wirenook.party-tricks";
        private const string Models = Package + "/Models";
        private const string Generated = Package + "/Generated";
        private const string Prefabs = Package + "/Prefabs";

        public const string CensorPrefab = Prefabs + "/Wirenook Censor.prefab";

        [MenuItem("Tools/Wirenook/Party Tricks/Rebuild")]
        public static void Rebuild()
        {
            Censor();
            Hammer();
            Vanish();
            Busy();
            Bundle();
            AssetDatabase.SaveAssets();
            Debug.Log("[party-tricks] Rebuilt prefabs and animators.");
        }

        /// <summary>When the censor applies; the shader's <c>_Mode</c>, in menu order.</summary>
        private static readonly (string label, string icon)[] CensorModes =
        {
            ("Always", "censor"),
            ("In Mirrors", "censor"),
            ("In Photos", "censor"),
            ("Mirrors Or Photos", "censor"),
            ("Mirrors In Photos", "censor"),
        };

        /// <summary>
        /// What the censor covers. The offsets and sizes are for a figure
        /// about 1.7 m tall with a large head, measured from the bone each
        /// follows in the avatar's own directions; they scale with the avatar.
        /// </summary>
        private static readonly (string label, HumanBodyBones bone, Vector3 offset, Vector3 size)[] CensorAreas =
        {
            ("Head", HumanBodyBones.Head, new Vector3(0f, 0.11f, 0.02f), new Vector3(0.46f, 0.5f, 0.46f)),
            ("Whole Body", HumanBodyBones.Hips, new Vector3(0f, 0.13f, 0f), new Vector3(1.15f, 2.4f, 1.15f)),
        };

        /// <summary>
        /// A mosaic over the head or the whole body, with a choice of when:
        /// always, only in mirrors, only in photos, in either, or only in a
        /// mirror that is in a photo. One integer carries both choices.
        ///
        /// Only the avatar itself is pixelated, not the room seen past it:
        /// the shader draws where <see cref="ToonMaterial.AvatarStencil"/>
        /// is set, which the materials <see cref="ToonMaterial"/> configures
        /// do as they draw. On an avatar with other materials that leaves
        /// nothing to pixelate; see the README for what to do there.
        /// </summary>
        public static void Censor()
        {
            const string parameter = "WN/Censor";

            var shader = Shader.Find("Wirenook/Censor");
            var material = AssetFolders.LoadOrCreate(Generated + "/censor.mat", () => new Material(shader));
            material.shader = shader;
            material.SetFloat("_Show", 0f);
            material.SetFloat("_Mode", 0f);
            material.SetFloat("_Stencil", ToonMaterial.AvatarStencil);
            EditorUtility.SetDirty(material);

            var ball = AssetFolders.LoadOrCreate(Generated + "/censor-volume.asset", () => new Mesh());
            Ball(ball, "censor-volume", 12, 7);

            FeaturePrefab.Edit(CensorPrefab, root =>
            {
                // One volume serves both areas: it follows whichever bone the
                // chosen area names, through a constraint whose weights the
                // animator switches, and is scaled to that area's size.
                var follow = FeaturePrefab.Child(root, "Follow");
                var constraint = FeaturePrefab.Ensure<VRCParentConstraint>(follow);
                constraint.ClearSources();
                for (var area = 0; area < CensorAreas.Length; area++)
                {
                    var point = FeaturePrefab.Child(root, CensorAreas[area].label + " Point");
                    AttachUpright(point, CensorAreas[area].bone);
                    var centre = FeaturePrefab.Child(point, "Centre");
                    centre.transform.localPosition = CensorAreas[area].offset;
                    constraint.AddSource(new VRCConstraintSource { SourceTransform = centre.transform, Weight = area == 0 ? 1f : 0f });
                }

                constraint.IsActive = true;
                // Unlocked constraints re-measure their offset instead of moving the object.
                constraint.Locked = true;

                var volume = FeaturePrefab.Child(follow, "Volume");
                FeaturePrefab.Ensure<MeshFilter>(volume).sharedMesh = ball;
                var renderer = FeaturePrefab.Ensure<MeshRenderer>(volume);
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                volume.SetActive(false);

                var followPath = FeaturePrefab.PathTo(root, follow.transform);
                var volumePath = FeaturePrefab.PathTo(root, volume.transform);

                var fx = ControllerBuilder.Rebuild(Generated + "/censor-fx.controller");
                fx.Int(parameter);
                var layer = fx.Layer("Censor");

                AnimationClip Clip(string name, int area, int mode) => fx.Clip("censor " + name, clip =>
                {
                    var shown = area >= 0;
                    var size = shown ? CensorAreas[area].size : Vector3.zero;
                    clip.Active(volumePath, shown)
                        .Constant(volumePath, typeof(Transform), "m_LocalScale.x", size.x)
                        .Constant(volumePath, typeof(Transform), "m_LocalScale.y", size.y)
                        .Constant(volumePath, typeof(Transform), "m_LocalScale.z", size.z)
                        .Material<MeshRenderer>(volumePath, "_Show", shown ? 1f : 0f)
                        .Material<MeshRenderer>(volumePath, "_Mode", shown ? mode : 0f);
                    for (var source = 0; source < CensorAreas.Length; source++)
                    {
                        clip.Constant(followPath, typeof(VRCParentConstraint), "Sources.source" + source + ".Weight",
                            source == Mathf.Max(area, 0) ? 1f : 0f);
                    }
                });

                var off = layer.State("Off", Clip("off", -1, 0));
                layer.Any(off).Equal(parameter, 0);
                FeaturePrefab.InstallMenu(root);
                FeaturePrefab.MenuFolder(root, "Censor", "censor");
                for (var area = 0; area < CensorAreas.Length; area++)
                {
                    var folder = FeaturePrefab.Child(root, "Menu " + CensorAreas[area].label);
                    FeaturePrefab.MenuFolder(folder, CensorAreas[area].label, "censor");
                    for (var mode = 0; mode < CensorModes.Length; mode++)
                    {
                        var choice = 1 + area * CensorModes.Length + mode;
                        var name = CensorAreas[area].label + ", " + CensorModes[mode].label;
                        var state = layer.State(name, Clip(name.ToLowerInvariant(), area, mode));
                        layer.Any(state).Equal(parameter, choice);
                        FeaturePrefab.MenuToggle(FeaturePrefab.Child(folder, "Menu " + CensorModes[mode].label),
                            CensorModes[mode].label, CensorModes[mode].icon, parameter, choice);
                    }
                }

                fx.Save();

                FeaturePrefab.Merge(root, fx.Controller);
                FeaturePrefab.Parameters(root, FeaturePrefab.SyncedInt(parameter));
                // Drawn by a shader of this package's own, and mobile builds
                // may only use VRChat's.
                FeaturePrefab.Describe(root, "censor", "Mosaic censor", "censor", "censor", 25, pcOnly: true);
            });
        }

        /// <summary>A ball one unit across, facing outwards, for volumes that are scaled to size.</summary>
        private static void Ball(Mesh mesh, string name, int segments, int rings)
        {
            var vertices = new List<Vector3> { Vector3.up * 0.5f };
            for (var ring = 1; ring < rings; ring++)
            {
                var polar = Mathf.PI * ring / rings;
                for (var segment = 0; segment < segments; segment++)
                {
                    var turn = 2f * Mathf.PI * segment / segments;
                    vertices.Add(new Vector3(Mathf.Sin(polar) * Mathf.Cos(turn), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(turn)) * 0.5f);
                }
            }

            vertices.Add(Vector3.down * 0.5f);
            var bottom = vertices.Count - 1;

            var triangles = new List<int>();
            int At(int ring, int segment) => 1 + (ring - 1) * segments + segment % segments;
            for (var segment = 0; segment < segments; segment++)
            {
                triangles.AddRange(new[] { 0, At(1, segment + 1), At(1, segment) });
                triangles.AddRange(new[] { bottom, At(rings - 1, segment), At(rings - 1, segment + 1) });
                for (var ring = 1; ring < rings - 1; ring++)
                {
                    triangles.AddRange(new[] { At(ring, segment), At(ring, segment + 1), At(ring + 1, segment + 1) });
                    triangles.AddRange(new[] { At(ring, segment), At(ring + 1, segment + 1), At(ring + 1, segment) });
                }
            }

            mesh.Clear();
            mesh.name = name;
            mesh.SetVertices(vertices);
            mesh.SetNormals(vertices.Select(vertex => vertex.normalized).ToList());
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
        }
    }
}
