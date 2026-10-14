using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValheimHeraldryMod.Shields
{
    /// <summary>
    ///     The paintable face of one shield model, built at runtime from the vanilla mesh (nothing of Iron Gate's
    ///     ships): the outward-facing triangles, copied into a new mesh lifted a hair off the shield, with flat 0..1
    ///     UVs (a straight-on "photo" of the face). Metal shields keep only the triangles vanilla paint styles cover,
    ///     so rims, studs and bosses stay metal. Which side is the outside, and which way is up, come from
    ///     ShieldShare's in-game testing; shields it never tested are worked out from the vanilla paint styles.
    /// </summary>
    internal sealed class ShieldFace
    {
        public Mesh Mesh;          // in the model's local space; put it on a child of the model with an identity transform
        public float[] OutlineUV;  // for the renderer: where the face is, in the texture
        public int[] OutlineTriangles;
        public float Aspect;       // width / height of the face
        public string ShapeId;
        public string Note;

        /// <summary>Outside face direction, flips and masking per vanilla shield (from ShieldShare's testing).</summary>
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

        private const float FrontThreshold = 0.5f;
        private const byte MaskHalf = 56;    // between ShieldShare's MaskLow (16) and MaskHigh (96)
        private const int AtlasRead = 512;

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
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] No paintable face for '{prefabName}': {ex.Message}");
                face = null;
            }
            Cache[key] = face;
            if (face != null) Jotunn.Logger.LogInfo($"[Heraldry] Shield face for '{prefabName}': {face.Note}");
            return face;
        }

        /// <summary>The shield's visible model: the mesh whose material supports paint styles, else the biggest.</summary>
        public static MeshFilter FindModel(GameObject root)
        {
            MeshFilter best = null;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null || mf.GetComponent<HeraldicShield.Overlay>() != null) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null) continue;
                if (r.sharedMaterials.Any(m => m != null && m.HasProperty("_StyleTex") && m.GetTexture("_StyleTex") != null)) return mf;
                if (best == null || mf.sharedMesh.vertexCount > best.sharedMesh.vertexCount) best = mf;
            }
            return best;
        }

        private static ShieldFace Build(string prefabName, MeshFilter model)
        {
            var mesh = model.sharedMesh;
            var renderer = model.GetComponent<MeshRenderer>();
            var snap = MeshReader.Read(mesh);
            bool haveNormals = snap.Normals != null && snap.Normals.Length == snap.Vertices.Length;

            // Only the parts whose material takes paint styles (metal shields have separate boss/trim parts)
            Texture styleTex = null;
            var tris = new List<int>();
            var mats = renderer != null ? renderer.sharedMaterials : new Material[0];
            for (int s = 0; s < snap.SubMeshTriangles.Count; s++)
            {
                var m = s < mats.Length ? mats[s] : null;
                if (m != null && m.HasProperty("_StyleTex") && m.GetTexture("_StyleTex") != null)
                {
                    tris.AddRange(snap.SubMeshTriangles[s]);
                    styleTex = styleTex ?? m.GetTexture("_StyleTex");
                }
            }
            if (tris.Count == 0) tris.AddRange(snap.SubMeshTriangles.SelectMany(t => t));

            // Vanilla paint area, read once from the style atlas: per pixel of one cell, the most any style paints
            byte[] paintMask = null;
            int cell = AtlasRead / 4;
            if (styleTex != null)
            {
                var atlas = ReadTexturePixels(styleTex, AtlasRead);
                paintMask = new byte[cell * cell];
                for (int i = 0; i < 16; i++)
                {
                    int ox = (i % 4) * cell, oy = (i / 4) * cell;
                    for (int y = 0; y < cell; y++)
                        for (int x = 0; x < cell; x++)
                        {
                            byte a = atlas[(oy + y) * AtlasRead + ox + x].a;
                            if (a > paintMask[y * cell + x]) paintMask[y * cell + x] = a;
                        }
                }
            }

            string how;
            if (!Known.TryGetValue(prefabName, out var settings))
            {
                int sign = paintMask != null ? DetectFaceSign(snap, haveNormals, tris, paintMask, cell) : 0;
                settings = new Settings(sign != 0 ? sign : -1, false, false, paintMask != null);
                how = sign != 0 ? "outside found from the paint styles" : "outside not found (no paint styles), assuming -Z; UNTESTED";
            }
            else how = "ShieldShare's tested settings";

            // Front triangles (and, for metal shields, only those vanilla paints)
            var front = new List<int>();
            for (int t = 0; t + 2 < tris.Count; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                if (FacingZ(snap, haveNormals, a, b, c) * settings.FaceSign <= FrontThreshold) continue;
                if (settings.Mask && paintMask != null && !Painted(snap.Uvs, a, b, c, paintMask, cell)) continue;
                front.Add(a); front.Add(b); front.Add(c);
            }
            if (front.Count == 0) throw new InvalidOperationException($"no outward-facing triangles ({how})");

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (int i in front)
            {
                var v = snap.Vertices[i];
                minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y);
            }
            float rangeX = Mathf.Max(maxX - minX, 1e-4f), rangeY = Mathf.Max(maxY - minY, 1e-4f);
            float lift = 0.004f * Mathf.Max(rangeX, rangeY); // ~3 mm on a 70 cm shield, whatever the model's units

            // The overlay: the front triangles' corners, lifted, with straight-on UVs (ShieldShare's mapping, so its
            // tested flips hold: V = 1 at the top of the arms)
            var index = new Dictionary<int, int>();
            var pos = new List<Vector3>(); var nrm = new List<Vector3>(); var uv = new List<Vector2>(); var idx = new List<int>();
            foreach (int i in front)
            {
                if (!index.TryGetValue(i, out int j))
                {
                    j = pos.Count;
                    index[i] = j;
                    var n = haveNormals ? snap.Normals[i].normalized : new Vector3(0, 0, settings.FaceSign);
                    var v = snap.Vertices[i];
                    pos.Add(v + n * lift);
                    nrm.Add(n);
                    float u = (v.x - minX) / rangeX, vv = 1f - (v.y - minY) / rangeY;
                    if (settings.FaceSign > 0) u = 1f - u;
                    if (settings.FlipV) vv = 1f - vv;
                    if (settings.FlipH) u = 1f - u;
                    uv.Add(new Vector2(u, vv));
                }
                idx.Add(j);
            }

            var overlay = new Mesh { name = "Heraldry_Face_" + prefabName };
            if (pos.Count > 65000) overlay.indexFormat = IndexFormat.UInt32;
            overlay.SetVertices(pos);
            overlay.SetNormals(nrm);
            overlay.SetUVs(0, uv);
            overlay.SetTriangles(idx, 0);
            overlay.RecalculateBounds();
            overlay.RecalculateTangents();

            return new ShieldFace
            {
                Mesh = overlay,
                OutlineUV = uv.SelectMany(p => new[] { p.x, p.y }).ToArray(),
                OutlineTriangles = idx.ToArray(),
                Aspect = rangeX / rangeY,
                ShapeId = "Heraldry_Shield_" + prefabName,
                Note = $"{idx.Count / 3} of {tris.Count / 3} triangles, aspect {rangeX / rangeY:F2}, {how}" +
                       $"{(settings.Mask && paintMask != null ? ", kept to the vanilla paint area" : "")}{(snap.FromGpu ? ", mesh read from the GPU" : "")}.",
            };
        }

        private static float FacingZ(MeshSnapshot s, bool haveNormals, int a, int b, int c) => haveNormals
            ? (s.Normals[a].z + s.Normals[b].z + s.Normals[c].z) / 3f
            : Vector3.Cross(s.Vertices[b] - s.Vertices[a], s.Vertices[c] - s.Vertices[a]).normalized.z;

        /// <summary>At least half of the triangle (centre and corners) lies where vanilla paint goes.</summary>
        private static bool Painted(Vector2[] uvs, int a, int b, int c, byte[] mask, int cell)
        {
            var pts = new[] { (uvs[a] + uvs[b] + uvs[c]) / 3f, uvs[a], uvs[b], uvs[c],
                              (uvs[a] + uvs[b]) / 2f, (uvs[b] + uvs[c]) / 2f, (uvs[c] + uvs[a]) / 2f };
            int hits = 0;
            foreach (var p in pts)
            {
                int x = Mathf.Clamp((int)(Mathf.Clamp01(p.x) * cell), 0, cell - 1);
                int y = Mathf.Clamp((int)(Mathf.Clamp01(p.y) * cell), 0, cell - 1);
                if (mask[y * cell + x] > MaskHalf) hits++;
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

        /// <summary>Any texture (even compressed, not CPU-readable) as pixels, via the GPU. Unity rows: bottom first.</summary>
        private static Color32[] ReadTexturePixels(Texture source, int size)
        {
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
                copy.ReadPixels(new Rect(0, 0, size, size), 0, 0);
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
