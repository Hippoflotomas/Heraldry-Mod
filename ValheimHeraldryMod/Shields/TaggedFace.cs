using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimHeraldryMod.Shields
{
    /// <summary>
    ///     A paintable face another modder put on their model (2026-10-13/14, docs/ModderGuide.md):
    ///     <list type="bullet">
    ///         <item>a mesh object named <c>ValheimHeraldryMod_Face</c> (switched off by its modder; we switch it on
    ///         once it carries arms), or</item>
    ///         <item>faces using a material named <c>ValheimHeraldryMod_Face</c> on any mesh (visible as modelled
    ///         until painted, like the herald's table).</item>
    ///     </list>
    ///     An empty named <c>ValheimHeraldryMod_CentrePoint</c> sets the field's centre (lopsided shapes).
    ///     The face is projected flat here (the modder's UVs are ignored): its triangles are the outline, and a copy
    ///     of the mesh carries our UVs. Names match exactly, ignoring Unity's " (Instance)"/"(Clone)" suffixes.
    /// </summary>
    internal sealed class TaggedFace
    {
        public const string FaceTag = "ValheimHeraldryMod_Face";
        public const string CentreTag = "ValheimHeraldryMod_CentrePoint";

        /// <summary>-1: the whole object is the face. Else the material slot (submesh) that is.</summary>
        public int Slot;
        /// <summary>The mesh to show when painted: the face with our UVs (object tag), or the whole mesh with the face's
        /// triangles moved onto copies of their vertices carrying our UVs (material tag).</summary>
        public Mesh PaintedMesh;
        public float[] OutlineUV;
        public int[] OutlineTriangles;
        public float Aspect;
        public float CentreU = -1f, CentreV = -1f;
        public string Key;
        public string Note;

        private static readonly Dictionary<string, bool> TaggedPrefabs = new Dictionary<string, bool>();
        private static readonly Dictionary<string, TaggedFace> Prepared = new Dictionary<string, TaggedFace>();

        /// <summary>Name without Unity's runtime suffixes.</summary>
        private static string Clean(string name)
        {
            if (name == null) return "";
            string s = name.Trim();
            foreach (var suffix in new[] { " (Instance)", "(Clone)" })
                while (s.EndsWith(suffix, StringComparison.Ordinal)) s = s.Substring(0, s.Length - suffix.Length).TrimEnd();
            return s;
        }

        /// <summary>The tagged face on this object or its children, if any.</summary>
        public static bool Find(GameObject root, out MeshRenderer renderer, out int slot)
        {
            renderer = null;
            slot = -1;
            if (root == null) return false;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponent<MeshFilter>() == null) continue;
                if (Clean(r.gameObject.name) == FaceTag) { renderer = r; slot = -1; return true; }
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && Clean(mats[i].name) == FaceTag) { renderer = r; slot = i; return true; }
            }
            return false;
        }

        /// <summary>True if this prefab carries a tagged face (looked up once per prefab).</summary>
        public static bool HasTag(GameObject prefab)
        {
            if (prefab == null) return false;
            if (TaggedPrefabs.TryGetValue(prefab.name, out bool has)) return has;
            has = Find(prefab, out _, out _);
            TaggedPrefabs[prefab.name] = has;
            if (has) Jotunn.Logger.LogInfo($"[Heraldry] '{prefab.name}' has a tagged heraldry face.");
            return has;
        }

        /// <summary>The prepared face for this object (built once per mesh and slot), or null if it has none.</summary>
        public static TaggedFace For(GameObject root, out MeshRenderer renderer)
        {
            if (!Find(root, out renderer, out int slot)) return null;
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null) return null;
            string key = mesh.name + "|" + mesh.GetInstanceID() + "|" + slot;
            if (Prepared.TryGetValue(key, out var face)) return face;
            try
            {
                face = Build(mesh, slot, renderer.transform, root);
                face.Key = key;
                Jotunn.Logger.LogInfo($"[Heraldry] Tagged face on '{Clean(root.name)}': {face.Note}");
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Tagged face on '{Clean(root.name)}' can't be used: {ex.Message}");
                face = null;
            }
            Prepared[key] = face;
            return face;
        }

        private static TaggedFace Build(Mesh mesh, int slot, Transform faceTransform, GameObject root)
        {
            var snap = MeshReader.Read(mesh);
            var tris = slot < 0 ? snap.SubMeshTriangles.SelectMany(t => t).ToArray()
                     : slot < snap.SubMeshTriangles.Count ? snap.SubMeshTriangles[slot] : new int[0];
            if (tris.Length < 3) throw new InvalidOperationException("the tagged face has no triangles");
            var used = tris.Distinct().ToArray();

            // The way the face looks: its normals (or windings) averaged
            var normal = Vector3.zero;
            if (snap.Normals != null && snap.Normals.Length == snap.Vertices.Length)
                foreach (int i in used) normal += snap.Normals[i];
            else
                for (int t = 0; t + 2 < tris.Length; t += 3)
                    normal += Vector3.Cross(snap.Vertices[tris[t + 1]] - snap.Vertices[tris[t]], snap.Vertices[tris[t + 2]] - snap.Vertices[tris[t]]);
            if (normal.sqrMagnitude < 1e-12f) throw new InvalidOperationException("can't tell which way the face looks");
            normal.Normalize();

            // Up: the model's up, flattened onto the face (a face lying flat: its forward)
            var up = Vector3.up - Vector3.Dot(Vector3.up, normal) * normal;
            if (up.magnitude < 0.05f) up = Vector3.forward - Vector3.Dot(Vector3.forward, normal) * normal;
            up.Normalize();
            var right = Vector3.Cross(normal, up); // as seen from the front

            var centre = Vector3.zero;
            foreach (int i in used) centre += snap.Vertices[i];
            centre /= used.Length;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (int i in used)
            {
                var d = snap.Vertices[i] - centre;
                float u = Vector3.Dot(d, right), v = Vector3.Dot(d, up);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, v); maxV = Mathf.Max(maxV, v);
            }
            float rangeU = Mathf.Max(maxU - minU, 1e-5f), rangeV = Mathf.Max(maxV - minV, 1e-5f);
            Vector2 P(Vector3 p)
            {
                var d = p - centre;
                return new Vector2((Vector3.Dot(d, right) - minU) / rangeU, (Vector3.Dot(d, up) - minV) / rangeV);
            }

            var face = new TaggedFace { Slot = slot, Aspect = rangeU / rangeV };

            // Outline: the face's own triangles, flat
            var outlineUV = new List<float>();
            var outlineTris = new List<int>();
            for (int t = 0; t < tris.Length; t++)
            {
                var p = P(snap.Vertices[tris[t]]);
                outlineTris.Add(outlineUV.Count / 2);
                outlineUV.Add(p.x); outlineUV.Add(p.y);
            }
            face.OutlineUV = outlineUV.ToArray();
            face.OutlineTriangles = outlineTris.ToArray();

            // Centre point: the tagged empty, seen from the front
            var centreMark = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => Clean(t.name) == CentreTag);
            if (centreMark != null)
            {
                var local = faceTransform.InverseTransformPoint(centreMark.position);
                var c = P(local);
                face.CentreU = Mathf.Clamp01(c.x);
                face.CentreV = Mathf.Clamp01(1f - c.y); // the renderer counts from the top
            }

            face.PaintedMesh = slot < 0 ? FaceOnly(snap, tris, P) : WithFaceCopied(snap, slot, P);
            face.PaintedMesh.name = mesh.name + "_heraldry";
            face.Note = $"{tris.Length / 3} triangles, aspect {face.Aspect:F2}, tagged by {(slot < 0 ? "object name" : "material (slot " + slot + ")")}" +
                        (centreMark != null ? $", centre point at {face.CentreU:F2},{face.CentreV:F2}" : "") +
                        (snap.FromGpu ? ", mesh read from the GPU" : "") + ".";
            return face;
        }

        /// <summary>The face alone, with flat UVs (object tag: the whole object is the face).</summary>
        private static Mesh FaceOnly(MeshSnapshot snap, int[] tris, Func<Vector3, Vector2> P)
        {
            var map = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var idx = new int[tris.Length];
            bool haveNormals = snap.Normals != null && snap.Normals.Length == snap.Vertices.Length;
            for (int t = 0; t < tris.Length; t++)
            {
                int i = tris[t];
                if (!map.TryGetValue(i, out int j))
                {
                    j = verts.Count;
                    map[i] = j;
                    verts.Add(snap.Vertices[i]);
                    if (haveNormals) norms.Add(snap.Normals[i]);
                    uvs.Add(P(snap.Vertices[i]));
                }
                idx[t] = j;
            }
            var m = new Mesh { indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(verts);
            if (haveNormals) m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(idx, 0);
            if (!haveNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>
        ///     The whole mesh again, but the tagged slot's triangles use copies of their vertices with flat UVs
        ///     (material tag: the face is part of a bigger mesh whose other parts keep their own UVs).
        /// </summary>
        private static Mesh WithFaceCopied(MeshSnapshot snap, int slot, Func<Vector3, Vector2> P)
        {
            bool haveNormals = snap.Normals != null && snap.Normals.Length == snap.Vertices.Length;
            var verts = new List<Vector3>(snap.Vertices);
            var norms = haveNormals ? new List<Vector3>(snap.Normals) : null;
            var uvs = new List<Vector2>(snap.Uvs != null && snap.Uvs.Length == snap.Vertices.Length ? snap.Uvs : new Vector2[snap.Vertices.Length]);
            var map = new Dictionary<int, int>();
            var faceTris = snap.SubMeshTriangles[slot];
            var newFace = new int[faceTris.Length];
            for (int t = 0; t < faceTris.Length; t++)
            {
                int i = faceTris[t];
                if (!map.TryGetValue(i, out int j))
                {
                    j = verts.Count;
                    map[i] = j;
                    verts.Add(snap.Vertices[i]);
                    if (haveNormals) norms.Add(snap.Normals[i]);
                    uvs.Add(P(snap.Vertices[i]));
                }
                newFace[t] = j;
            }
            var m = new Mesh { indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(verts);
            if (haveNormals) m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.subMeshCount = snap.SubMeshTriangles.Count;
            for (int s = 0; s < snap.SubMeshTriangles.Count; s++)
                m.SetTriangles(s == slot ? newFace : snap.SubMeshTriangles[s], s);
            if (!haveNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        // ================================================================== painting an instance

        /// <summary>The material the arms go on: a copy of the face's own (or the herald's table's wood if it has no texture slot).</summary>
        public static Material MakeMaterial(Material original, Texture arms)
        {
            Material m;
            if (original != null && original.HasProperty("_MainTex")) m = new Material(original);
            else
            {
                var table = Station.TableRegistry.VanillaMaterials();
                m = new Material(table["Display"]);
            }
            m.name = "Heraldry_TaggedFace";
            m.mainTexture = arms;
            m.mainTextureScale = Vector2.one;
            m.mainTextureOffset = Vector2.zero;
            if (m.HasProperty("_Color")) m.color = Color.white;
            if (m.HasProperty("_StyleTex")) m.SetTexture("_StyleTex", null);
            if (m.HasProperty("_UseStyles")) m.SetFloat("_UseStyles", 0f);
            m.DisableKeyword("_USESTYLES_ON");
            return m;
        }
    }

    /// <summary>
    ///     Shows arms on one instance's tagged face (a shield in a hand, on a stand, on the ground; a piece).
    ///     Remembers what the face looked like so it can be put back when the arms are taken off.
    /// </summary>
    internal sealed class TaggedFacePainter
    {
        private readonly MeshRenderer renderer;
        private readonly MeshFilter filter;
        private readonly TaggedFace face;
        private readonly Mesh originalMesh;
        private readonly Material[] originalMaterials;
        private readonly bool originalEnabled, originalActive;
        private Material painted;
        private bool applied;

        private TaggedFacePainter(MeshRenderer r, TaggedFace f)
        {
            renderer = r;
            filter = r.GetComponent<MeshFilter>();
            face = f;
            originalMesh = filter.sharedMesh;
            originalMaterials = r.sharedMaterials;
            originalEnabled = r.enabled;
            originalActive = r.gameObject.activeSelf;
        }

        public TaggedFace Face => face;

        public static TaggedFacePainter On(GameObject root)
        {
            var f = TaggedFace.For(root, out var r);
            return f != null && r != null ? new TaggedFacePainter(r, f) : null;
        }

        public void Show(Texture arms)
        {
            if (renderer == null) return;
            if (painted == null)
            {
                var source = face.Slot >= 0 && face.Slot < originalMaterials.Length ? originalMaterials[face.Slot]
                           : originalMaterials.FirstOrDefault(m => m != null);
                painted = TaggedFace.MakeMaterial(source, arms);
            }
            else painted.mainTexture = arms;
            filter.sharedMesh = face.PaintedMesh;
            if (face.Slot < 0) renderer.sharedMaterials = new[] { painted };
            else
            {
                var mats = (Material[])originalMaterials.Clone();
                mats[face.Slot] = painted;
                renderer.sharedMaterials = mats;
            }
            renderer.enabled = true;
            if (!renderer.gameObject.activeSelf) renderer.gameObject.SetActive(true); // switched off as an object rather than a renderer
            applied = true;
        }

        public void Hide()
        {
            if (!applied || renderer == null) return;
            filter.sharedMesh = originalMesh;
            renderer.sharedMaterials = originalMaterials;
            renderer.enabled = originalEnabled;
            if (renderer.gameObject.activeSelf != originalActive) renderer.gameObject.SetActive(originalActive);
            applied = false;
        }

        public void Destroy()
        {
            Hide();
            if (painted != null) UnityEngine.Object.Destroy(painted);
            painted = null;
        }
    }
}
