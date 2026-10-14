using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ValheimHeraldryMod.Tabards
{
    /// <summary>
    ///     The tabard's skinned mesh (TabardShare's model, read out of its bundle by tools/ModelConvert/bundle_to_hskin.py
    ///     and embedded as Tabard.hskin), and its two painted panels. UV layout (TabardShare's contract): left half the
    ///     front panel, right half the back, both the right way round as seen by someone looking at that side.
    /// </summary>
    internal static class TabardMesh
    {
        private const string Resource = "ValheimHeraldryMod.Tabard.hskin";

        public static Mesh Mesh { get; private set; }
        public static string[] BoneNames { get; private set; }

        /// <summary>One panel: where it sits in the texture (0..1) and its outline, relative to that rectangle.</summary>
        public sealed class Panel
        {
            public Rect UV;
            public float[] OutlineUV;
            public int[] OutlineTriangles;
            /// <summary>Width over height, in a square texture.</summary>
            public float Aspect => UV.width / UV.height;
        }

        public static Panel Front { get; private set; }
        public static Panel Back { get; private set; }

        public static bool Load()
        {
            if (Mesh != null) return true;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (stream == null)
                {
                    Jotunn.Logger.LogError("[Heraldry] The tabard model isn't embedded (" + Resource + ").");
                    return false;
                }
                using (var r = new BinaryReader(stream))
                {
                    if (new string(r.ReadChars(4)) != "HSKN" || r.ReadInt32() != 1) throw new InvalidDataException("not a version 1 .hskin");
                    int n = r.ReadInt32();
                    var pos = new Vector3[n]; var nrm = new Vector3[n]; var uv = new Vector2[n];
                    for (int i = 0; i < n; i++) pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int i = 0; i < n; i++) nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                    for (int i = 0; i < n; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                    var idx = new int[r.ReadInt32()];
                    for (int i = 0; i < idx.Length; i++) idx[i] = r.ReadInt32();
                    var weights = new BoneWeight[n];
                    for (int i = 0; i < n; i++)
                    {
                        int b0 = r.ReadInt32(), b1 = r.ReadInt32(), b2 = r.ReadInt32(), b3 = r.ReadInt32();
                        weights[i] = new BoneWeight
                        {
                            boneIndex0 = b0, boneIndex1 = b1, boneIndex2 = b2, boneIndex3 = b3,
                            weight0 = r.ReadSingle(), weight1 = r.ReadSingle(), weight2 = r.ReadSingle(), weight3 = r.ReadSingle(),
                        };
                    }
                    int bones = r.ReadInt32();
                    var names = new string[bones];
                    var bind = new Matrix4x4[bones];
                    for (int b = 0; b < bones; b++)
                    {
                        names[b] = System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                        var m = new Matrix4x4();
                        for (int c = 0; c < 4; c++)
                            for (int row = 0; row < 4; row++)
                                m[row, c] = r.ReadSingle();
                        bind[b] = m;
                    }

                    var mesh = new Mesh { name = "Heraldry_Tabard" };
                    mesh.vertices = pos;
                    mesh.normals = nrm;
                    mesh.uv = uv;
                    mesh.triangles = idx;
                    mesh.boneWeights = weights;
                    mesh.bindposes = bind;
                    int shapes = r.ReadInt32();
                    for (int s = 0; s < shapes; s++)
                    {
                        string name = System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                        var dp = new Vector3[n]; var dn = new Vector3[n];
                        for (int i = 0; i < n; i++) dp[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        for (int i = 0; i < n; i++) dn[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        mesh.AddBlendShapeFrame(name, 100f, dp, dn, null);
                    }
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    Mesh = mesh;
                    BoneNames = names;

                    Front = MakePanel(uv, idx, true);
                    Back = MakePanel(uv, idx, false);
                    Jotunn.Logger.LogDebug($"[Heraldry] Tabard model: {n} vertices, {idx.Length / 3} triangles, {bones} bones, {shapes} body shape(s); " +
                                          $"front panel aspect {Front.Aspect:F2}, back {Back.Aspect:F2}.");
                    return true;
                }
            }
        }

        private static Panel MakePanel(Vector2[] uv, int[] idx, bool front)
        {
            var tris = new List<int>();
            float minU = 1, minV = 1, maxU = 0, maxV = 0;
            for (int t = 0; t + 2 < idx.Length; t += 3)
            {
                float cu = (uv[idx[t]].x + uv[idx[t + 1]].x + uv[idx[t + 2]].x) / 3f;
                if ((cu < 0.5f) != front) continue;
                for (int k = 0; k < 3; k++)
                {
                    var p = uv[idx[t + k]];
                    minU = Mathf.Min(minU, p.x); maxU = Mathf.Max(maxU, p.x);
                    minV = Mathf.Min(minV, p.y); maxV = Mathf.Max(maxV, p.y);
                    tris.Add(idx[t + k]);
                }
            }
            var rect = Rect.MinMaxRect(minU, minV, maxU, maxV);
            var outline = new float[tris.Count * 2];
            var outTris = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++)
            {
                var p = uv[tris[i]];
                outline[i * 2] = (p.x - rect.xMin) / rect.width;
                outline[i * 2 + 1] = (p.y - rect.yMin) / rect.height;
                outTris[i] = i;
            }
            return new Panel { UV = rect, OutlineUV = outline, OutlineTriangles = outTris };
        }
    }
}
