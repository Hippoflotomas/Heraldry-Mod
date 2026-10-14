using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ValheimHeraldryMod.Station
{
    /// <summary>
    ///     Reads the .hmesh files made by tools/ModelConvert (embedded resources): one Unity Mesh per material
    ///     slot. Models are built in code instead of shipped in an asset bundle.
    /// </summary>
    internal static class HMesh
    {
        public sealed class Part
        {
            public string Name;
            public Mesh Mesh;
            public Vector2[] UV;
            public int[] Triangles;
        }

        public static List<Part> Load(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null) throw new FileNotFoundException("embedded model missing: " + resourceName);
                using (var r = new BinaryReader(stream))
                {
                    if (new string(r.ReadChars(4)) != "HMSH") throw new InvalidDataException("not an .hmesh file");
                    int version = r.ReadInt32();
                    if (version != 1) throw new InvalidDataException("unknown .hmesh version " + version);
                    int count = r.ReadInt32();
                    var parts = new List<Part>(count);
                    for (int s = 0; s < count; s++)
                    {
                        string name = System.Text.Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                        int n = r.ReadInt32();
                        var pos = new Vector3[n];
                        var nrm = new Vector3[n];
                        var uv = new Vector2[n];
                        for (int i = 0; i < n; i++) pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        for (int i = 0; i < n; i++) nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                        for (int i = 0; i < n; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                        int ic = r.ReadInt32();
                        var idx = new int[ic];
                        for (int i = 0; i < ic; i++) idx[i] = r.ReadInt32();

                        var mesh = new Mesh { name = "Heraldry_" + name };
                        mesh.vertices = pos;
                        mesh.normals = nrm;
                        mesh.uv = uv;
                        mesh.triangles = idx;
                        mesh.RecalculateBounds();
                        mesh.RecalculateTangents(); // vanilla materials use normal maps
                        parts.Add(new Part { Name = name, Mesh = mesh, UV = uv, Triangles = idx });
                    }
                    return parts;
                }
            }
        }
    }
}
