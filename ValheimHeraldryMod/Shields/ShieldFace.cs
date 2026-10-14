using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValheimHeraldryMod.Shields
{
    /// <summary>
    ///     How to paint one shield model, worked out at runtime from the vanilla mesh (ShieldShare's method): its
    ///     outward-facing triangles, each known both in the mesh's own UV layout (where the shader looks) and as a
    ///     straight-on "photo" of the face (where the arms are drawn). The arms are then baked through the UVs into
    ///     the shield's own paint-style texture, so they sit exactly on the shield with its relief and lighting.
    ///     Which side is the outside, and which way is up, come from ShieldShare's in-game testing; shields it never
    ///     tested are worked out from the vanilla paint styles.
    /// </summary>
    internal sealed class ShieldFace
    {
        public struct Tri
        {
            public Vector2 UvA, UvB, UvC; // vanilla UVs
            public Vector2 PA, PB, PC;    // on the arms, 0..1, V up
        }

        public readonly List<Tri> Front = new List<Tri>();
        /// <summary>Vanilla paint area per pixel of one style cell (metal shields), or null to paint the whole face.</summary>
        public byte[] PaintMask;
        public int MaskCell;
        /// <summary>True: paint into the paint-style slot (_StyleTex). False: no styles, paint into a copy of _MainTex.</summary>
        public bool UsesStyleSlot;
        public Texture MainTex;
        public float[] OutlineUV;      // the part of the face the arms are laid out on (for metal shields, the painted panel)
        public int[] OutlineTriangles;
        public float Aspect;
        /// <summary>The field's centre (fess point) as fractions from the top left; -1 = the middle.</summary>
        public float CentreU = -1f, CentreV = -1f;
        /// <summary>Set when this is another modder's tagged face (painted on the face itself, not baked).</summary>
        public TaggedFace Tagged;
        public string ShapeId;
        public string Key;
        public string Note;

        private sealed class Settings
        {
            public readonly int FaceSign; public readonly bool FlipV, FlipH, Mask;
            public Settings(int faceSign, bool flipV, bool flipH, bool mask) { FaceSign = faceSign; FlipV = flipV; FlipH = flipH; Mask = mask; }
        }

        private static readonly Dictionary<string, Settings> Known = new Dictionary<string, Settings>(StringComparer.OrdinalIgnoreCase)
        {
            //                                         face  flipV  flipH  mask     (ShieldShare: verified in game)
            { "ShieldWood",            new Settings(-1, false, false, false) },
            { "ShieldBanded",          new Settings(-1, false, true,  false) },
            { "ShieldWoodTower",       new Settings(+1, false, true,  false) },
            { "ShieldSilver",          new Settings(+1, true,  false, false) },
            { "ShieldBlackmetal",      new Settings(+1, true,  false, true)  },
            { "ShieldBlackmetalTower", new Settings(+1, true,  false, true)  },
            { "ShieldIronTower",       new Settings(+1, true,  false, true)  },
            { "ShieldFlametal",        new Settings(+1, false, true,  true)  },
            { "ShieldFlametalTower",   new Settings(-1, false, true,  true)  },
        };

        /// <summary>Shields whose centre is their boss (the face's most raised point), not the middle (Hippo, 2026-10-14).</summary>
        private static readonly HashSet<string> CentreOnBoss = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ShieldSerpentscale",
        };

        /// <summary>A tagged face described the way the table, preview and icons expect.</summary>
        public static ShieldFace FromTagged(TaggedFace t, string prefabName) => new ShieldFace
        {
            Tagged = t,
            OutlineUV = t.OutlineUV,
            OutlineTriangles = t.OutlineTriangles,
            Aspect = t.Aspect,
            CentreU = t.CentreU,
            CentreV = t.CentreV,
            ShapeId = "Heraldry_Tagged_" + prefabName,
            Key = t.Key,
            Note = t.Note,
        };

        private const float FrontThreshold = 0.5f;
        public const byte MaskLow = 16, MaskHigh = 96;
        private const int MaskRead = 512;

        private static readonly Dictionary<string, ShieldFace> Cache = new Dictionary<string, ShieldFace>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The face for this shield's model, or null if it has none we can use (logged once).</summary>
        public static ShieldFace For(string prefabName, MeshFilter model)
        {
            if (model == null || model.sharedMesh == null) return null;
            string key = prefabName + "|" + model.sharedMesh.name + "|" + model.sharedMesh.vertexCount;
            if (Cache.TryGetValue(key, out var face)) return face;
            try
            {
                face = Build(prefabName, model);
                face.Key = key;
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] '{prefabName}' can't be painted: {ex.Message}");
                face = null;
            }
            Cache[key] = face;
            if (face != null) Jotunn.Logger.LogInfo($"[Heraldry] Shield face for '{prefabName}': {face.Note}");
            return face;
        }

        /// <summary>The shield's visible model: the mesh whose material takes paint styles, else the biggest.</summary>
        public static MeshFilter FindModel(GameObject root)
        {
            MeshFilter best = null;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null) continue;
                if (r.sharedMaterials.Any(HasStyleSlot)) return mf;
                if (best == null || mf.sharedMesh.vertexCount > best.sharedMesh.vertexCount) best = mf;
            }
            return best;
        }

        public static bool HasStyleSlot(Material m) => m != null && m.HasProperty("_StyleTex") && m.GetTexture("_StyleTex") != null;

        private static ShieldFace Build(string prefabName, MeshFilter model)
        {
            var mesh = model.sharedMesh;
            var renderer = model.GetComponent<MeshRenderer>();
            var snap = MeshReader.Read(mesh);
            bool haveNormals = snap.Normals != null && snap.Normals.Length == snap.Vertices.Length;
            var mats = renderer != null ? renderer.sharedMaterials : new Material[0];

            // Only the parts whose material takes paint styles (metal shields have separate boss/trim parts)
            Texture styleTex = null;
            var tris = new List<int>();
            for (int s = 0; s < snap.SubMeshTriangles.Count; s++)
            {
                var m = s < mats.Length ? mats[s] : null;
                if (!HasStyleSlot(m)) continue;
                tris.AddRange(snap.SubMeshTriangles[s]);
                styleTex = styleTex ?? m.GetTexture("_StyleTex");
            }
            var face = new ShieldFace { UsesStyleSlot = styleTex != null };
            if (styleTex == null)
            {
                tris.AddRange(snap.SubMeshTriangles.SelectMany(t => t));
                face.MainTex = mats.Where(m => m != null && m.HasProperty("_MainTex")).Select(m => m.GetTexture("_MainTex")).FirstOrDefault(t => t != null);
                if (face.MainTex == null) throw new InvalidOperationException("no paint-style slot and no main texture");
            }

            // Vanilla paint area (per pixel of one cell: the most any style paints there)
            byte[] paintMask = null;
            int cell = MaskRead / 4;
            if (styleTex != null)
            {
                var atlas = Textures.ReadPixels(styleTex, MaskRead, MaskRead);
                paintMask = new byte[cell * cell];
                for (int i = 0; i < 16; i++)
                {
                    int ox = (i % 4) * cell, oy = (i / 4) * cell;
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            byte a = atlas[(oy + y) * MaskRead + ox + x].a;
                            if (a > paintMask[y * cell + x]) paintMask[y * cell + x] = a;
                        }
                }
            }

            string how;
            if (!Known.TryGetValue(prefabName, out var settings))
            {
                int sign = paintMask != null ? DetectFaceSign(snap, haveNormals, tris, paintMask, cell) : 0;
                settings = new Settings(sign != 0 ? sign : -1, false, false, paintMask != null);
                how = sign != 0 ? "outside found from the paint styles, UNTESTED" : "no paint styles: assuming -Z, UNTESTED";
            }
            else how = "ShieldShare's tested settings";
            if (settings.Mask && paintMask != null) { face.PaintMask = paintMask; face.MaskCell = cell; }

            // Pass 1: front triangles; the bounds of the part the arms are laid out on (for metal shields, the panel)
            var front = new List<int>();
            var layout = new List<int>();
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (FacingZ(snap, haveNormals, a, b, c) * settings.FaceSign <= FrontThreshold) continue;
                front.Add(t);
                if (face.PaintMask == null || Painted(snap.Uvs, a, b, c, paintMask, cell)) layout.Add(t);
            }
            if (layout.Count == 0) layout = front;
            if (front.Count == 0) throw new InvalidOperationException($"no outward-facing triangles ({how})");

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (int t in layout)
                for (int k = 0; k < 3; k++)
                {
                    var v = snap.Vertices[tris[t + k]];
                    minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                    minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
                }
            float rangeX = Mathf.Max(maxX - minX, 1e-4f), rangeY = Mathf.Max(maxY - minY, 1e-4f);

            // Straight-on projection, ShieldShare's mapping (so its tested flips hold)
            Vector2 P(Vector3 v)
            {
                float u = (v.x - minX) / rangeX, vv = 1f - (v.y - minY) / rangeY;
                if (settings.FaceSign > 0) u = 1f - u;
                if (settings.FlipV) vv = 1f - vv;
                if (settings.FlipH) u = 1f - u;
                return new Vector2(u, vv);
            }

            foreach (int t in front)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                face.Front.Add(new Tri
                {
                    UvA = snap.Uvs[a], UvB = snap.Uvs[b], UvC = snap.Uvs[c],
                    PA = P(snap.Vertices[a]), PB = P(snap.Vertices[b]), PC = P(snap.Vertices[c]),
                });
            }

            var outlineUV = new List<float>();
            var outlineTris = new List<int>();
            foreach (int t in layout)
                for (int k = 0; k < 3; k++)
                {
                    var p = P(snap.Vertices[tris[t + k]]);
                    outlineTris.Add(outlineUV.Count / 2);
                    outlineUV.Add(p.x); outlineUV.Add(p.y);
                }
            face.OutlineUV = outlineUV.ToArray();
            face.OutlineTriangles = outlineTris.ToArray();
            face.Aspect = rangeX / rangeY;
            if (CentreOnBoss.Contains(prefabName))
            {
                // The front vertex standing furthest out towards the viewer
                Vector3 best = Vector3.zero;
                float bestZ = float.MinValue;
                foreach (int t in layout)
                    for (int k = 0; k < 3; k++)
                    {
                        var v = snap.Vertices[tris[t + k]];
                        float z = v.z * settings.FaceSign; // the face looks along FaceSign * Z: outward = that way
                        if (z > bestZ) { bestZ = z; best = v; }
                    }
                var c = P(best);
                face.CentreU = c.x;
                face.CentreV = 1f - c.y; // the renderer counts from the top
            }
            face.ShapeId = "Heraldry_Shield_" + prefabName;
            face.Note = $"{front.Count} of {tris.Count / 3} triangles, aspect {face.Aspect:F2}, {how}, " +
                        (face.UsesStyleSlot ? "paint-style slot" : "main texture (no paint styles)") +
                        $"{(face.PaintMask != null ? ", kept to the vanilla paint area" : "")}{(snap.FromGpu ? ", mesh read from the GPU" : "")}" +
                        (face.CentreU >= 0 ? $", centre on the boss at {face.CentreU:F2},{face.CentreV:F2}" : "") + ".";
            return face;
        }

        private static float FacingZ(MeshSnapshot s, bool haveNormals, int a, int b, int c) => haveNormals
            ? (s.Normals[a].z + s.Normals[b].z + s.Normals[c].z) / 3f
            : Vector3.Cross(s.Vertices[b] - s.Vertices[a], s.Vertices[c] - s.Vertices[a]).normalized.z;

        /// <summary>At least half of the triangle (centre, corners, edge middles) lies where vanilla paint goes.</summary>
        private static bool Painted(Vector2[] uvs, int a, int b, int c, byte[] mask, int cell)
        {
            var pts = new[] { (uvs[a] + uvs[b] + uvs[c]) / 3f, uvs[a], uvs[b], uvs[c],
                              (uvs[a] + uvs[b]) / 2f, (uvs[b] + uvs[c]) / 2f, (uvs[c] + uvs[a]) / 2f };
            int hits = 0;
            foreach (var p in pts)
            {
                int x = Mathf.Clamp((int)(Mathf.Clamp01(p.x) * cell), 0, cell - 1);
                int y = Mathf.Clamp((int)(Mathf.Clamp01(p.y) * cell), 0, cell - 1);
                if (mask[y * cell + x] > (MaskLow + MaskHigh) / 2) hits++;
            }
            return hits * 2 >= pts.Length;
        }

        /// <summary>-1 or +1: the side (local Z) that vanilla paint covers most; 0 if it can't be told.</summary>
        private static int DetectFaceSign(MeshSnapshot s, bool haveNormals, List<int> tris, byte[] mask, int cell)
        {
            int negF = 0, negP = 0, posF = 0, posP = 0;
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                float z = FacingZ(s, haveNormals, a, b, c);
                if (Mathf.Abs(z) <= FrontThreshold) continue;
                bool painted = Painted(s.Uvs, a, b, c, mask, cell);
                if (z < 0) { negF++; if (painted) negP++; }
                else { posF++; if (painted) posP++; }
            }
            float neg = negF > 0 ? negP / (float)negF : 0f, pos = posF > 0 ? posP / (float)posF : 0f;
            float hi = Mathf.Max(neg, pos), lo = Mathf.Min(neg, pos);
            if (hi < 0.02f || hi < lo * 1.5f) return 0;
            return neg > pos ? -1 : 1;
        }
    }

    internal static class Textures
    {
        /// <summary>Any texture (even compressed, not CPU-readable) as pixels, via the GPU. Unity rows: bottom first.</summary>
        public static Color32[] ReadPixels(Texture source, int width, int height, bool linear = true)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false, linear);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply();
                return copy.GetPixels32();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null) UnityEngine.Object.Destroy(copy);
            }
        }
    }
}
