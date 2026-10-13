using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimHeraldryMod.Banners
{
    /// <summary>
    ///     Registers the heraldic banner pieces. Step 2 only does the vanilla hanging banner; StandardLarge
    ///     (from this mod's own asset bundle) slots in here later as a second model.
    ///     One piece per banner shape: what's painted on it is per instance, not per prefab.
    /// </summary>
    internal static class BannerRegistry
    {
        public const string ClothShaderName = "Custom/Vegetation";
        private const string VanillaHangingBanner = "piece_banner01";
        public const string Category = "Heraldry";

        public static void Init()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
        }

        private static void Register()
        {
            // Fires every time the main menu loads; Jotunn keeps our pieces alive, so register once.
            PrefabManager.OnVanillaPrefabsAvailable -= Register;

            AddBanner("Heraldry_BannerHanging", VanillaHangingBanner, "Heraldic banner (hanging)",
                "A plain cloth banner. Write a blazon on it and every viewer sees the arms painted.");
        }

        private static void AddBanner(string prefabName, string source, string displayName, string description)
        {
            var prefab = PrefabManager.Instance.CreateClonedPrefab(prefabName, source);
            if (prefab == null)
            {
                Jotunn.Logger.LogError($"[Heraldry] Could not clone '{source}' - '{prefabName}' is unavailable.");
                return;
            }

            // Re-project every cloth to a single 0..1 image and learn its shape.
            float mirror = 0f, widthOverHeight = 0f;
            int cloths = 0;
            foreach (var renderer in ClothRenderers(prefab))
            {
                var meshFilter = renderer.GetComponent<MeshFilter>();
                if (ClothUV.Remap(meshFilter, renderer.transform, prefabName, ref mirror, out float wh))
                {
                    if (widthOverHeight == 0f)
                        widthOverHeight = wh;
                    cloths++;
                }
            }
            if (cloths == 0)
            {
                Jotunn.Logger.LogError($"[Heraldry] '{source}' has no '{ClothShaderName}' cloth - '{prefabName}' is unavailable.");
                return;
            }

            var banner = prefab.AddComponent<HeraldicBanner>();
            banner.m_displayName = displayName;
            banner.m_widthOverHeight = widthOverHeight;
            banner.m_shapeId = prefabName;
            CollectOutline(prefab, banner);

            // Requirements left empty = keep the vanilla banner's recipe and crafting station.
            var config = new PieceConfig
            {
                Name = displayName,
                Description = description,
                PieceTable = PieceTables.Hammer,
                Category = Category,
            };
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));

            Jotunn.Logger.LogInfo($"[Heraldry] Registered '{prefabName}' from '{source}': {cloths} cloth mesh(es), " +
                                  $"width:height {widthOverHeight:0.00}, e.g. {Mathf.RoundToInt(1024 * widthOverHeight)}x1024 at full resolution.");
        }

        /// <summary>All cloth triangles in texture space, so the renderer knows the cloth's real outline.</summary>
        private static void CollectOutline(GameObject prefab, HeraldicBanner banner)
        {
            var uv = new List<float>();
            var tris = new List<int>();
            foreach (var renderer in ClothRenderers(prefab))
            {
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (mesh == null || !mesh.isReadable) continue;
                int offset = uv.Count / 2;
                foreach (var v in mesh.uv) { uv.Add(v.x); uv.Add(v.y); }
                foreach (int i in mesh.triangles) tris.Add(i + offset);
            }
            banner.m_outlineUV = uv.ToArray();
            banner.m_outlineTriangles = tris.ToArray();
        }

        public static bool IsCloth(Material m) => m != null && m.shader != null && m.shader.name == ClothShaderName;

        /// <summary>Renderers using the cloth shader, excluding the pieces that fly off when the banner breaks.</summary>
        public static List<Renderer> ClothRenderers(GameObject root)
        {
            var fragmentRoots = new HashSet<Transform>();
            var wearNTear = root.GetComponent<WearNTear>();
            if (wearNTear != null && wearNTear.m_fragmentRoots != null)
                foreach (var r in wearNTear.m_fragmentRoots)
                    if (r != null)
                        fragmentRoots.Add(r.transform);

            var result = new List<Renderer>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;
                if (!renderer.sharedMaterials.Any(IsCloth))
                    continue;
                if (IsFragment(renderer.transform, fragmentRoots, root.transform))
                    continue;
                result.Add(renderer);
            }
            return result;
        }

        private static bool IsFragment(Transform t, HashSet<Transform> fragmentRoots, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
                if (fragmentRoots.Contains(t) || t.name == "destruction")
                    return true;
            return false;
        }
    }
}
