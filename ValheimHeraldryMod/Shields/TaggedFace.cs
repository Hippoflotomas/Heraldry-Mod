using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValheimHeraldryMod.Rendering;

namespace ValheimHeraldryMod.Shields
{
    /// <summary>
    ///     One paintable part another modder tagged on their model (docs/ModderGuide.md):
    ///     <list type="bullet">
    ///         <item>a mesh object named <c>ValheimHeraldryMod_Face</c> (switched off by its modder; we switch it on once
    ///         it carries arms), or</item>
    ///         <item>the faces of a mesh using a material named <c>ValheimHeraldryMod_Face</c> (visible as modelled until
    ///         painted).</item>
    ///     </list>
    ///     Up to four patterns (Hippo, 2026-10-16): <c>ValheimHeraldryMod_Face_1</c> … <c>_4</c>, unnumbered = 1. Every part
    ///     with the same number shows the same arms, each laid out on its own shape (a full copy per part). Centre points
    ///     follow the same numbering. An object named <c>ValheimHeraldryMod_UseUVs</c> anywhere in the model switches every
    ///     tagged part to the modder's own UVs (0–1 per part); without it, every part is projected flat.
    /// </summary>
    internal sealed class TaggedFace
    {
        public const string FaceTag = "ValheimHeraldryMod_Face";
        public const string CentreTag = "ValheimHeraldryMod_CentrePoint";
        public const string UseUVsTag = "ValheimHeraldryMod_UseUVs";
        public const int MaxPatterns = 4;

        /// <summary>Which pattern (1–4) this part shows.</summary>
        public int Number = 1;
        /// <summary>-1: the whole object is the face. Else the material slot (submesh) that is.</summary>
        public int Slot;
        /// <summary>Laid out in the modder's own UVs rather than projected.</summary>
        public bool FromUVs;
        public float[] OutlineUV;
        public int[] OutlineTriangles;
        /// <summary>Width over height of the field as it really is on the model.</summary>
        public float Aspect;
        public float CentreU = -1f, CentreV = -1f;
        public string Key;
        public string Note;
        /// <summary>Position on the mesh → our UVs (projected parts only).</summary>
        internal Func<Vector3, Vector2> Project;
        internal int[] Triangles;

        // ================================================================== names

        /// <summary>Name without Unity's runtime suffixes (" (Instance)", "(Clone)") and Blender's ".001".</summary>
        internal static string Clean(string name)
        {
            if (name == null) return "";
            string s = name.Trim();
            bool again = true;
            while (again)
            {
                again = false;
                foreach (var suffix in new[] { " (Instance)", "(Clone)" })
                    if (s.EndsWith(suffix, StringComparison.Ordinal)) { s = s.Substring(0, s.Length - suffix.Length).TrimEnd(); again = true; }
                int dot = s.LastIndexOf('.');
                if (dot > 0 && dot < s.Length - 1 && s.Substring(dot + 1).All(char.IsDigit)) { s = s.Substring(0, dot); again = true; }
            }
            return s;
        }

        private static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>
        ///     The pattern number a (cleaned) name gives for this tag: the tag alone = 1, the tag + "_" + 1–4 = that
        ///     number, anything else = 0. A number outside 1–4 is ignored with a warning, once.
        /// </summary>
        internal static int NumberOf(string cleaned, string tag)
        {
            if (cleaned == tag) return 1;
            if (!cleaned.StartsWith(tag + "_", StringComparison.Ordinal)) return 0;
            string rest = cleaned.Substring(tag.Length + 1);
            if (rest.Length == 0 || !rest.All(char.IsDigit)) return 0;
            int n = rest.Length > 3 ? 0 : int.Parse(rest);
            if (n >= 1 && n <= MaxPatterns) return n;
            if (Warned.Add(cleaned))
                Jotunn.Logger.LogWarning($"[Heraldry] '{cleaned}': only {tag}_1 to _{MaxPatterns} are used; this one is ignored.");
            return 0;
        }

        // ================================================================== building one part

        /// <summary>A part projected flat: its normals averaged, the model's up as up.</summary>
        internal static TaggedFace Projected(MeshSnapshot snap, int[] tris, int number, int slot, Vector3? centreLocal)
        {
            var used = tris.Distinct().ToArray();
            // Which way the face looks: its normals averaged by area. How much they agree says how curved it is.
            var normal = Vector3.zero;
            float total = 0f;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                var n = Vector3.Cross(snap.Vertices[tris[t + 1]] - snap.Vertices[tris[t]], snap.Vertices[tris[t + 2]] - snap.Vertices[tris[t]]);
                if (snap.Normals != null && snap.Normals.Length == snap.Vertices.Length &&
                    Vector3.Dot(n, snap.Normals[tris[t]] + snap.Normals[tris[t + 1]] + snap.Normals[tris[t + 2]]) < 0) n = -n; // winding against the normals
                normal += n;
                total += n.magnitude;
            }
            float agreement = total > 0 ? normal.magnitude / total : 0f;
            if (agreement < 0.1f)
                throw new InvalidOperationException("it wraps around (its sides face opposite ways), so it can't be projected flat; " +
                                                    "add ValheimHeraldryMod_UseUVs and unwrap it 0–1");
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

            var face = new TaggedFace { Number = number, Slot = slot, Aspect = rangeU / rangeV, Project = P, Triangles = tris };
            // Agreement 0.7 ≈ the sides turned ~45° away: the projection stretches them noticeably
            if (agreement < 0.7f) face.Note = $"it curves a lot (normals agree {agreement:P0}): the sides will look stretched; ValheimHeraldryMod_UseUVs fixes that";
            var outlineUV = new float[tris.Length * 2];
            var outlineTris = new int[tris.Length];
            for (int t = 0; t < tris.Length; t++)
            {
                var p = P(snap.Vertices[tris[t]]);
                outlineTris[t] = t;
                outlineUV[t * 2] = p.x; outlineUV[t * 2 + 1] = p.y;
            }
            face.OutlineUV = outlineUV;
            face.OutlineTriangles = outlineTris;

            if (centreLocal.HasValue)
            {
                var c = P(centreLocal.Value);
                face.CentreU = Mathf.Clamp01(c.x);
                face.CentreV = Mathf.Clamp01(1f - c.y); // the renderer counts from the top
            }
            return face;
        }

        /// <summary>A part laid out in the modder's own UV0 (0–1 across its field, +V up).</summary>
        internal static TaggedFace FromModUVs(MeshSnapshot snap, int[] tris, int number, int slot, Vector3? centreLocal)
        {
            if (snap.Uvs == null || snap.Uvs.Length != snap.Vertices.Length)
                throw new InvalidOperationException("ValheimHeraldryMod_UseUVs is set but the mesh has no UVs");
            var face = new TaggedFace { Number = number, Slot = slot, FromUVs = true, Triangles = tris };
            var outlineUV = new float[tris.Length * 2];
            var outlineTris = new int[tris.Length];
            int outside = 0;
            for (int t = 0; t < tris.Length; t++)
            {
                var uv = snap.Uvs[tris[t]];
                if (uv.x < -0.01f || uv.x > 1.01f || uv.y < -0.01f || uv.y > 1.01f) outside++;
                outlineTris[t] = t;
                outlineUV[t * 2] = uv.x; outlineUV[t * 2 + 1] = uv.y;
            }
            face.OutlineUV = outlineUV;
            face.OutlineTriangles = outlineTris;

            // The field's real proportions: how long one unit of U and of V is on the model, averaged by area
            double su = 0, sv = 0, weight = 0;
            int mirrored = 0;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 p0 = snap.Vertices[tris[t]], p1 = snap.Vertices[tris[t + 1]], p2 = snap.Vertices[tris[t + 2]];
                Vector2 a = snap.Uvs[tris[t]], b = snap.Uvs[tris[t + 1]], c = snap.Uvs[tris[t + 2]];
                Vector3 e1 = p1 - p0, e2 = p2 - p0;
                float du1 = b.x - a.x, dv1 = b.y - a.y, du2 = c.x - a.x, dv2 = c.y - a.y;
                float det = du1 * dv2 - du2 * dv1;
                if (Mathf.Abs(det) < 1e-10f) continue;
                var dPdu = (e1 * dv2 - e2 * dv1) / det;
                var dPdv = (e2 * du1 - e1 * du2) / det;
                double w = Mathf.Abs(det) * 0.5;
                su += w * dPdu.magnitude;
                sv += w * dPdv.magnitude;
                weight += w;
                // Seen from the front, U must run to the viewer's right (normal × V, as the projection does; Unity is
                // left-handed), i.e. normal · (dP/du × dP/dv) < 0. The other way round the arms read backwards.
                var n = snap.Normals != null && snap.Normals.Length == snap.Vertices.Length ? snap.Normals[tris[t]] : Vector3.Cross(e1, e2);
                if (Vector3.Dot(Vector3.Cross(dPdu, dPdv), n) > 0) mirrored++;
            }
            if (weight <= 0 || sv <= 0) throw new InvalidOperationException("the face's UVs have no area");
            face.Aspect = (float)(su / sv);

            if (centreLocal.HasValue)
            {
                var uv = UvAtNearest(snap, tris, centreLocal.Value);
                face.CentreU = Mathf.Clamp01(uv.x);
                face.CentreV = Mathf.Clamp01(1f - uv.y);
            }
            if (outside > 0) face.Note = $"{outside} of its UVs lie outside 0–1 (they're cut off)";
            if (mirrored > tris.Length / 6) face.Note = (face.Note == null ? "" : face.Note + "; ") + "its UVs look mirrored (arms will read backwards)";
            return face;
        }

        /// <summary>The UV at the point of the face nearest to <paramref name="p"/>.</summary>
        private static Vector2 UvAtNearest(MeshSnapshot snap, int[] tris, Vector3 p)
        {
            float best = float.MaxValue;
            Vector2 result = new Vector2(0.5f, 0.5f);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Vector3 a = snap.Vertices[tris[t]], b = snap.Vertices[tris[t + 1]], c = snap.Vertices[tris[t + 2]];
                var q = ClosestOnTriangle(p, a, b, c, out float wa, out float wb, out float wc);
                float d = (q - p).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                result = snap.Uvs[tris[t]] * wa + snap.Uvs[tris[t + 1]] * wb + snap.Uvs[tris[t + 2]] * wc;
            }
            return result;
        }

        /// <summary>Closest point on a triangle, with its barycentric weights (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float wa, out float wb, out float wc)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) { wa = 1; wb = 0; wc = 0; return a; }
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) { wa = 0; wb = 1; wc = 0; return b; }
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) { float v = d1 / (d1 - d3); wa = 1 - v; wb = v; wc = 0; return a + v * ab; }
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) { wa = 0; wb = 0; wc = 1; return c; }
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) { float w = d2 / (d2 - d6); wa = 1 - w; wb = 0; wc = w; return a + w * ac; }
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            {
                float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
                wa = 0; wb = 1 - w; wc = w;
                return b + w * (c - b);
            }
            float denom = 1f / (va + vb + vc);
            float vv = vb * denom, ww = vc * denom;
            wa = 1 - vv - ww; wb = vv; wc = ww;
            return a + ab * vv + ac * ww;
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
    ///     The tagged parts on one mesh, prepared once per mesh: their layouts, and the meshes to show while some of
    ///     them are painted (projected parts need their own UVs; with UseUVs the mesh never changes).
    /// </summary>
    internal sealed class TaggedMesh
    {
        public Mesh Source;
        public bool UseUVs;
        public readonly List<TaggedFace> Faces = new List<TaggedFace>();
        private MeshSnapshot snap;
        private readonly Dictionary<int, Mesh> variants = new Dictionary<int, Mesh>();

        private static readonly Dictionary<string, TaggedMesh> Cache = new Dictionary<string, TaggedMesh>();

        /// <summary>
        ///     The parts of this renderer's mesh named in <paramref name="hits"/> (slot → number), or null if the mesh can't be
        ///     used (logged once).
        /// </summary>
        public static TaggedMesh For(MeshRenderer renderer, List<KeyValuePair<int, int>> hits, bool useUVs,
            Func<int, Transform> centreFor, string owner)
        {
            var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) return null;
            string key = owner + "|" + mesh.name + "|" + mesh.GetInstanceID() + "|" + (useUVs ? "uv" : "proj") + "|" +
                         string.Join(",", hits.Select(h => h.Key + ":" + h.Value));
            if (Cache.TryGetValue(key, out var tm)) return tm;
            try
            {
                tm = new TaggedMesh { Source = mesh, UseUVs = useUVs, snap = MeshReader.Read(mesh) };
                foreach (var hit in hits)
                {
                    int slot = hit.Key, number = hit.Value;
                    var tris = slot < 0 ? tm.snap.SubMeshTriangles.SelectMany(t => t).ToArray()
                             : slot < tm.snap.SubMeshTriangles.Count ? tm.snap.SubMeshTriangles[slot] : new int[0];
                    string where = slot < 0 ? $"object '{TaggedFace.Clean(renderer.name)}'" : $"'{TaggedFace.Clean(renderer.name)}' material slot {slot}";
                    if (tris.Length < 3)
                    {
                        Jotunn.Logger.LogWarning($"[Heraldry] Tagged face on '{owner}' ({where}) has no triangles; skipped.");
                        continue;
                    }
                    var mark = centreFor(number);
                    Vector3? centre = mark != null ? renderer.transform.InverseTransformPoint(mark.position) : (Vector3?)null;
                    TaggedFace face;
                    try
                    {
                        face = useUVs ? TaggedFace.FromModUVs(tm.snap, tris, number, slot, centre)
                                      : TaggedFace.Projected(tm.snap, tris, number, slot, centre);
                    }
                    catch (Exception ex)
                    {
                        Jotunn.Logger.LogWarning($"[Heraldry] Tagged face on '{owner}' ({where}) can't be used: {ex.Message}");
                        continue;
                    }
                    face.Key = key + "|" + slot;
                    string extra = face.Note;
                    face.Note = $"pattern {number}, {where}, {tris.Length / 3} triangles, aspect {face.Aspect:F2}, " +
                                (useUVs ? "modder's UVs" : "projected flat") +
                                (mark != null ? $", centre point at {face.CentreU:F2},{face.CentreV:F2}" : "") +
                                (tm.snap.FromGpu ? ", mesh read from the GPU" : "") + (extra != null ? "; " + extra : "");
                    tm.Faces.Add(face);
                    Jotunn.Logger.LogInfo($"[Heraldry] Tagged face on '{owner}': {face.Note}.");
                }
                if (tm.Faces.Count == 0) tm = null;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Tagged faces on '{owner}' can't be read: {ex.Message}");
                tm = null;
            }
            Cache[key] = tm;
            return tm;
        }

        /// <summary>The mesh to show with these faces painted (bit i = Faces[i]).</summary>
        public Mesh MeshFor(int painted)
        {
            if (painted == 0 || UseUVs) return Source;
            if (variants.TryGetValue(painted, out var m)) return m;
            var whole = Faces.FirstOrDefault(f => f.Slot < 0);
            m = whole != null ? FaceOnly(whole) : WithFacesCopied(painted);
            m.name = Source.name + "_heraldry" + painted;
            variants[painted] = m;
            return m;
        }

        private bool HaveNormals => snap.Normals != null && snap.Normals.Length == snap.Vertices.Length;

        /// <summary>The face alone, with flat UVs (object tag: the whole object is the face).</summary>
        private Mesh FaceOnly(TaggedFace face)
        {
            var map = new Dictionary<int, int>();
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = face.Triangles;
            var idx = new int[tris.Length];
            for (int t = 0; t < tris.Length; t++)
            {
                int i = tris[t];
                if (!map.TryGetValue(i, out int j))
                {
                    j = verts.Count;
                    map[i] = j;
                    verts.Add(snap.Vertices[i]);
                    if (HaveNormals) norms.Add(snap.Normals[i]);
                    uvs.Add(face.Project(snap.Vertices[i]));
                }
                idx[t] = j;
            }
            var m = new Mesh { indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(verts);
            if (HaveNormals) m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(idx, 0);
            if (!HaveNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        /// <summary>
        ///     The whole mesh again, with the painted slots' triangles on copies of their vertices carrying our UVs (the
        ///     rest, including tagged slots not painted now, keep the modder's own UVs).
        /// </summary>
        private Mesh WithFacesCopied(int painted)
        {
            var verts = new List<Vector3>(snap.Vertices);
            var norms = HaveNormals ? new List<Vector3>(snap.Normals) : null;
            var uvs = new List<Vector2>(snap.Uvs != null && snap.Uvs.Length == snap.Vertices.Length ? snap.Uvs : new Vector2[snap.Vertices.Length]);
            var subs = snap.SubMeshTriangles.Select(t => t).ToList();
            for (int f = 0; f < Faces.Count; f++)
            {
                if ((painted & (1 << f)) == 0) continue;
                var face = Faces[f];
                var map = new Dictionary<int, int>();
                var old = snap.SubMeshTriangles[face.Slot];
                var copy = new int[old.Length];
                for (int t = 0; t < old.Length; t++)
                {
                    int i = old[t];
                    if (!map.TryGetValue(i, out int j))
                    {
                        j = verts.Count;
                        map[i] = j;
                        verts.Add(snap.Vertices[i]);
                        if (HaveNormals) norms.Add(snap.Normals[i]);
                        uvs.Add(face.Project(snap.Vertices[i]));
                    }
                    copy[t] = j;
                }
                subs[face.Slot] = copy;
            }
            var m = new Mesh { indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(verts);
            if (HaveNormals) m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.subMeshCount = subs.Count;
            for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s);
            if (!HaveNormals) m.RecalculateNormals();
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }
    }

    /// <summary>Every tagged part of one model (an instance, or a prefab for previews and icons).</summary>
    internal sealed class TaggedModel
    {
        public readonly List<KeyValuePair<MeshRenderer, TaggedMesh>> Renderers = new List<KeyValuePair<MeshRenderer, TaggedMesh>>();
        public bool UseUVs;

        public IEnumerable<TaggedFace> Faces => Renderers.SelectMany(r => r.Value.Faces);

        /// <summary>The pattern numbers present, lowest first.</summary>
        public int[] Numbers => Faces.Select(f => f.Number).Distinct().OrderBy(n => n).ToArray();

        /// <summary>The first part showing this pattern (its shape is used for previews), or null.</summary>
        public TaggedFace First(int number) => Faces.FirstOrDefault(f => f.Number == number);

        /// <summary>Pattern 1, or the lowest the model has (inventory icons).</summary>
        public int IconPattern
        {
            get { var n = Numbers; return n.Length == 0 || n.Contains(1) ? 1 : n[0]; }
        }

        /// <summary>
        ///     The tags on this object and its children, then the faces another mod registered for its prefab through the
        ///     API: (renderer, slot or -1, pattern number). A renderer that carries tags keeps them; a registration on it
        ///     is ignored. <paramref name="prefabName"/>: the prefab, when the object's own name doesn't say (a shield
        ///     in hand is the prefab's "attach" child).
        /// </summary>
        public static List<KeyValuePair<MeshRenderer, KeyValuePair<int, int>>> Scan(GameObject root, out bool useUVs, string prefabName = null)
        {
            var hits = ScanTags(root, out useUVs);
            if (root == null) return hits;
            string prefab = prefabName ?? Utils.GetPrefabName(root);
            var registered = Api.ApiRegistry.For(prefab);
            if (registered == null) return hits;
            if (registered.UseUVs) useUVs = true;
            foreach (var f in registered.Faces)
            {
                var t = Api.ApiRegistry.Find(root.transform, f.Path);
                var r = t != null ? t.GetComponent<MeshRenderer>() : null;
                if (r == null || r.GetComponent<MeshFilter>() == null)
                {
                    // A shield shown in hand or on the back is only part of its prefab, so a missing path is only worth a
                    // warning on the whole prefab or a placed piece (scanned under their own names)
                    if (t == null && prefabName == null)
                        Api.ApiRegistry.WarnOnce($"'{prefab}': no object at '{f.Path}'.");
                    else if (t != null)
                        Api.ApiRegistry.WarnOnce($"'{prefab}': '{f.Path}' has no MeshRenderer and MeshFilter (skinned meshes can't be painted this way; RenderBlazon can help).");
                    continue;
                }
                if (hits.Any(h => h.Key == r)) continue; // its tags win
                if (f.Slot >= 0 && f.Slot >= r.sharedMaterials.Length)
                {
                    Api.ApiRegistry.WarnOnce($"'{prefab}': '{f.Path}' has no material slot {f.Slot}.");
                    continue;
                }
                hits.Add(new KeyValuePair<MeshRenderer, KeyValuePair<int, int>>(r, new KeyValuePair<int, int>(f.Slot, f.Number)));
            }
            return hits;
        }

        private static List<KeyValuePair<MeshRenderer, KeyValuePair<int, int>>> ScanTags(GameObject root, out bool useUVs)
        {
            var hits = new List<KeyValuePair<MeshRenderer, KeyValuePair<int, int>>>();
            useUVs = false;
            if (root == null) return hits;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (TaggedFace.Clean(t.name) == TaggedFace.UseUVsTag) { useUVs = true; break; }
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponent<MeshFilter>() == null) continue;
                int n = TaggedFace.NumberOf(TaggedFace.Clean(r.gameObject.name), TaggedFace.FaceTag);
                if (n > 0) { hits.Add(new KeyValuePair<MeshRenderer, KeyValuePair<int, int>>(r, new KeyValuePair<int, int>(-1, n))); continue; }
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    n = TaggedFace.NumberOf(TaggedFace.Clean(mats[i].name), TaggedFace.FaceTag);
                    if (n > 0) hits.Add(new KeyValuePair<MeshRenderer, KeyValuePair<int, int>>(r, new KeyValuePair<int, int>(i, n)));
                }
            }
            return hits;
        }

        /// <summary>This object's tagged parts, prepared (meshes cached), or null if it has none.</summary>
        public static TaggedModel For(GameObject root, string prefabName = null)
        {
            var hits = Scan(root, out bool useUVs, prefabName);
            if (hits.Count == 0) return null;
            string owner = prefabName ?? Utils.GetPrefabName(root);
            var marks = root.GetComponentsInChildren<Transform>(true)
                .Select(t => new KeyValuePair<int, Transform>(TaggedFace.NumberOf(TaggedFace.Clean(t.name), TaggedFace.CentreTag), t))
                .Where(kv => kv.Key > 0).ToList();
            // Centres registered through the API come after the tagged ones (a tag wins)
            var registered = Api.ApiRegistry.For(owner);
            if (registered != null)
                foreach (var f in registered.Faces)
                {
                    if (string.IsNullOrEmpty(f.CentrePath)) continue;
                    var c = Api.ApiRegistry.Find(root.transform, f.CentrePath);
                    if (c != null) marks.Add(new KeyValuePair<int, Transform>(f.Number, c));
                }
            Transform CentreFor(int n) => marks.FirstOrDefault(kv => kv.Key == n).Value;

            var model = new TaggedModel { UseUVs = useUVs };
            foreach (var group in hits.GroupBy(h => h.Key))
            {
                var tm = TaggedMesh.For(group.Key, group.Select(h => h.Value).ToList(), useUVs, CentreFor, owner);
                if (tm != null) model.Renderers.Add(new KeyValuePair<MeshRenderer, TaggedMesh>(group.Key, tm));
            }
            return model.Renderers.Count > 0 ? model : null;
        }

        private static readonly Dictionary<string, TaggedModel> Prefabs = new Dictionary<string, TaggedModel>();
        private static readonly Dictionary<string, bool> Tagged = new Dictionary<string, bool>();

        /// <summary>A prefab's tagged parts (cached per prefab name), for previews and icons; null if none.</summary>
        public static TaggedModel ForPrefab(GameObject prefab)
        {
            if (prefab == null) return null;
            if (Prefabs.TryGetValue(prefab.name, out var m)) return m;
            m = For(prefab);
            Prefabs[prefab.name] = m;
            return m;
        }

        /// <summary>Drops what's cached for a prefab (another mod registered a face on it after it was scanned).</summary>
        public static void Forget(string prefabName)
        {
            Prefabs.Remove(prefabName);
            Tagged.Remove(prefabName);
        }

        /// <summary>True if this prefab carries a tagged face (scanned once per prefab, nothing built).</summary>
        public static bool HasTag(GameObject prefab)
        {
            if (prefab == null) return false;
            if (Tagged.TryGetValue(prefab.name, out bool has)) return has;
            has = Scan(prefab, out _).Count > 0;
            Tagged[prefab.name] = has;
            if (has) Jotunn.Logger.LogDebug($"[Heraldry] '{prefab.name}' has a tagged heraldry face.");
            return has;
        }
    }

    /// <summary>
    ///     Shows arms on one instance's tagged parts (a shield in a hand, on a stand, on the ground; a piece). Each part
    ///     gets its own texture, drawn in its own outline; parts sharing a number show the same blazon. Remembers what
    ///     every renderer looked like so it can be put back when the arms are taken off.
    /// </summary>
    internal sealed class TaggedFacePainter
    {
        private sealed class RendererState
        {
            public MeshRenderer Renderer;
            public MeshFilter Filter;
            public TaggedMesh Mesh;
            public Mesh OriginalMesh;
            public Material[] OriginalMaterials;
            public bool OriginalEnabled, OriginalActive;
            public Material[] Painted; // per face
            public string[] TextKeys;  // per face: what it shows now
            public string[] CacheKeys; // per face: its texture in BlazonTextureCache
            public int Shown;          // bit per face
        }

        private readonly List<RendererState> states = new List<RendererState>();
        public TaggedModel Model { get; private set; }

        public static TaggedFacePainter On(GameObject root, string prefabName = null)
        {
            var model = TaggedModel.For(root, prefabName);
            if (model == null) return null;
            var p = new TaggedFacePainter { Model = model };
            foreach (var kv in model.Renderers)
            {
                var r = kv.Key;
                int n = kv.Value.Faces.Count;
                p.states.Add(new RendererState
                {
                    Renderer = r, Filter = r.GetComponent<MeshFilter>(), Mesh = kv.Value,
                    OriginalMesh = r.GetComponent<MeshFilter>().sharedMesh, OriginalMaterials = r.sharedMaterials,
                    OriginalEnabled = r.enabled, OriginalActive = r.gameObject.activeSelf,
                    Painted = new Material[n], TextKeys = new string[n], CacheKeys = new string[n],
                });
            }
            return p;
        }

        /// <summary>
        ///     Brings every part up to date: <paramref name="textOf"/> gives a pattern's blazon (already normalised, "" for
        ///     none), <paramref name="longest"/> the texture's longer side, <paramref name="arrangement"/> how a part lays out
        ///     its charges. Parts whose arms haven't changed are left alone.
        /// </summary>
        public void Refresh(Func<int, string> textOf, int longest, Func<TaggedFace, Heraldry.FieldRenderer.Arrangement> arrangement, string shapePrefix)
        {
            var texts = new Dictionary<int, string>();
            foreach (var s in states)
            {
                if (s.Renderer == null) continue;
                bool changed = false;
                for (int i = 0; i < s.Mesh.Faces.Count; i++)
                {
                    var f = s.Mesh.Faces[i];
                    if (!texts.TryGetValue(f.Number, out string text))
                        texts[f.Number] = text = BlazonTextureCache.Normalise(textOf(f.Number) ?? "");
                    string key = text.IndexOf('[') >= 0 ? text + "|" + ChargeLibrary.Generation : text;
                    if (key == s.TextKeys[i]) continue;
                    s.TextKeys[i] = key;
                    changed = true;
                    BlazonTextureCache.Release(s.CacheKeys[i]);
                    s.CacheKeys[i] = null;
                    if (text.Length == 0) { s.Shown &= ~(1 << i); continue; }

                    int w = f.Aspect >= 1f ? longest : Mathf.Max(32, Mathf.RoundToInt(longest * f.Aspect));
                    int h = f.Aspect >= 1f ? Mathf.Max(32, Mathf.RoundToInt(longest / f.Aspect)) : longest;
                    var shape = new BlazonTextureCache.Shape
                    {
                        Id = shapePrefix + f.Key, UV = f.OutlineUV, Triangles = f.OutlineTriangles,
                        Arrangement = arrangement(f), CentreU = f.CentreU, CentreV = f.CentreV,
                    };
                    var tex = BlazonTextureCache.Acquire(text, w, h, shape, out s.CacheKeys[i]);
                    if (s.Painted[i] == null)
                    {
                        var source = f.Slot >= 0 && f.Slot < s.OriginalMaterials.Length ? s.OriginalMaterials[f.Slot]
                                   : s.OriginalMaterials.FirstOrDefault(m => m != null);
                        s.Painted[i] = TaggedFace.MakeMaterial(source, tex);
                    }
                    else s.Painted[i].mainTexture = tex;
                    s.Shown |= 1 << i;
                }
                if (changed) Apply(s);
            }
        }

        private static void Apply(RendererState s)
        {
            if (s.Renderer == null) return;
            if (s.Shown == 0)
            {
                s.Filter.sharedMesh = s.OriginalMesh;
                s.Renderer.sharedMaterials = s.OriginalMaterials;
                s.Renderer.enabled = s.OriginalEnabled;
                if (s.Renderer.gameObject.activeSelf != s.OriginalActive) s.Renderer.gameObject.SetActive(s.OriginalActive);
                return;
            }
            var mesh = s.Mesh.MeshFor(s.Shown);
            s.Filter.sharedMesh = mesh;
            var whole = s.Mesh.Faces.FindIndex(f => f.Slot < 0);
            if (whole >= 0)
            {
                // The whole object is the face: every submesh it has takes the painted material
                int count = Mathf.Max(1, mesh.subMeshCount);
                s.Renderer.sharedMaterials = Enumerable.Repeat(s.Painted[whole], count).ToArray();
            }
            else
            {
                var mats = (Material[])s.OriginalMaterials.Clone();
                for (int i = 0; i < s.Mesh.Faces.Count; i++)
                    if ((s.Shown & (1 << i)) != 0 && s.Mesh.Faces[i].Slot < mats.Length) mats[s.Mesh.Faces[i].Slot] = s.Painted[i];
                s.Renderer.sharedMaterials = mats;
            }
            s.Renderer.enabled = true;
            if (!s.Renderer.gameObject.activeSelf) s.Renderer.gameObject.SetActive(true); // switched off as an object rather than a renderer
        }

        /// <summary>Takes all arms off (as modelled again) and lets go of the textures.</summary>
        public void Clear()
        {
            foreach (var s in states)
            {
                for (int i = 0; i < s.CacheKeys.Length; i++)
                {
                    BlazonTextureCache.Release(s.CacheKeys[i]);
                    s.CacheKeys[i] = null;
                    s.TextKeys[i] = null;
                }
                s.Shown = 0;
                Apply(s);
            }
        }

        public void Destroy()
        {
            Clear();
            foreach (var s in states)
                for (int i = 0; i < s.Painted.Length; i++)
                {
                    if (s.Painted[i] != null) UnityEngine.Object.Destroy(s.Painted[i]);
                    s.Painted[i] = null;
                }
        }
    }

    /// <summary>Where each pattern is stored: pattern 1 keeps the old key, 2–4 add their number (2026-10-16).</summary>
    internal static class Patterns
    {
        public static string Key(string baseKey, int number) => number <= 1 ? baseKey : baseKey + number;

        public static string Label(int number) => "Pattern " + number;

        public static IEnumerable<int> All
        {
            get { for (int n = 1; n <= TaggedFace.MaxPatterns; n++) yield return n; }
        }
    }
}
