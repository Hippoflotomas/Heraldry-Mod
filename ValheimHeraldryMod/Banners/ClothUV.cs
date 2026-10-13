using UnityEngine;

namespace ValheimHeraldryMod.Banners
{
    /// <summary>
    ///     Cloth mesh helpers, carried over from BannerShare (same author, MIT-0).
    ///     The vanilla cloth UVs point into the vanilla banner art; we re-project them so the
    ///     whole cloth maps to one 0..1 image, read the right way round from the side it faces.
    /// </summary>
    internal static class ClothUV
    {
        /// <summary>
        ///     Projects the image flat onto the cloth: V runs bottom to top, U runs left to right as seen
        ///     from the side the cloth faces. Works in the mesh's own space, so a copy of the cloth rotated
        ///     180 degrees (a back face) also reads correctly from its side.
        ///     <paramref name="mirror"/> starts at 0 and is fixed by the first cloth so every cloth on the
        ///     prefab agrees. Returns false if the mesh can't be projected.
        /// </summary>
        public static bool Remap(MeshFilter meshFilter, Transform transform, string tag, ref float mirror, out float widthOverHeight)
        {
            widthOverHeight = 0f;
            Mesh original = meshFilter.sharedMesh;
            Vector3[] vertices = original.vertices;
            if (vertices.Length == 0)
                return false;

            Vector3 up = Vector3.up;
            Vector3 right = Vector3.Cross(Facing(original), up).normalized;
            if (mirror == 0f)
                mirror = Vector3.Dot(right, Vector3.forward) < 0f ? -1f : 1f;
            right *= mirror;

            float minU = float.MaxValue, maxU = float.MinValue;
            foreach (var v in vertices)
            {
                float d = Vector3.Dot(v, right);
                if (d < minU) minU = d;
                if (d > maxU) maxU = d;
            }
            Bounds bounds = original.bounds;
            float width = maxU - minU;
            float height = bounds.size.y;
            if (width < 0.0001f || height < 0.0001f)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] '{tag}': cloth mesh '{original.name}' is flat in the wrong direction - skipping it.");
                return false;
            }

            var uvs = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                uvs[i] = new Vector2((Vector3.Dot(vertices[i], right) - minU) / width, (vertices[i].y - bounds.min.y) / height);

            // A full copy keeps sub-meshes, vertex colours (wind weighting), uv2 and so on.
            Mesh custom = Object.Instantiate(original);
            custom.name = original.name + "_" + tag;
            custom.uv = uvs;
            custom.RecalculateTangents(); // bump-map lighting needs tangents that match the UVs
            meshFilter.sharedMesh = custom;

            float worldWidth = transform.TransformVector(right * width).magnitude;
            float worldHeight = transform.TransformVector(up * height).magnitude;
            widthOverHeight = worldWidth / worldHeight;
            return true;
        }

        /// <summary>Which horizontal direction the cloth faces, from its averaged normals; falls back to the thinnest axis.</summary>
        private static Vector3 Facing(Mesh mesh)
        {
            Vector3[] normals = mesh.normals;
            Vector3 sum = Vector3.zero;
            foreach (var n in normals)
                sum += n;
            sum.y = 0f;
            if (normals.Length > 0 && sum.magnitude > 0.1f * normals.Length)
                return sum.normalized;

            Vector3 size = mesh.bounds.size;
            return size.x <= size.z ? Vector3.right : Vector3.forward;
        }
    }
}
