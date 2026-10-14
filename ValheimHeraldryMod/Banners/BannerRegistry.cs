using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimHeraldryMod.Banners
{
    /// <summary>
    ///     Registers the heraldic banner pieces. The vanilla hanging banner is a HeraldicBanner; the large standard
    ///     (StandardLarge.cs) is painted through the tagged-piece path instead.
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

            AddBanner("ValheimHeraldryMod_BannerHanging", VanillaHangingBanner, "Heraldic banner (hanging)",
                "A plain cloth banner. Write a blazon on it and every viewer sees the arms painted.");
            StandardLarge.Register();
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
            banner.m_hiddenTop = HiddenTop(prefab);

            // Requirements left empty = keep the vanilla banner's recipe and crafting station.
            var config = new PieceConfig
            {
                Name = displayName,
                Description = description,
                PieceTable = PieceTables.Hammer,
                Category = Category,
            };
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));

            Jotunn.Logger.LogDebug($"[Heraldry] Registered '{prefabName}' from '{source}': {cloths} cloth mesh(es), " +
                                  $"width:height {widthOverHeight:0.00}, e.g. {Mathf.RoundToInt(1024 * widthOverHeight)}x1024 at full resolution; " +
                                  $"top {banner.m_hiddenTop:P1} hidden by the beam ({Mathf.RoundToInt(1024 * banner.m_hiddenTop)} of 1024 rows).");
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

        /// <summary>
        ///     How much of the cloth's height (from the top) is hidden behind or wrapped over the beam it hangs from.
        ///     The cloth's texture is projected flat by height (ClothUV), so this is simply how far the bottom of
        ///     the beam sits below the top of the cloth mesh. Arms are laid out on the visible part only.
        /// </summary>
        private static float HiddenTop(GameObject prefab)
        {
            var cloth = ClothRenderers(prefab).FirstOrDefault();
            var clothMesh = cloth?.GetComponent<MeshFilter>()?.sharedMesh;
            if (clothMesh == null) return 0f;
            Bounds cb = clothMesh.bounds;
            if (cb.size.y < 0.0001f) return 0f;

            float beamBottom = float.MaxValue;
            string beamName = null;
            var clothSet = new HashSet<Renderer>(ClothRenderers(prefab));
            var fragmentRoots = FragmentRoots(prefab);
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (clothSet.Contains(r) || r is ParticleSystemRenderer) continue;
                if (IsFragment(r.transform, fragmentRoots, prefab.transform)) continue; // broken pieces, not the beam
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                if (r.sharedMaterials.Any(IsCloth)) continue; // a cloth fragment, not a beam
                // The other mesh's box, in the cloth mesh's own space
                Bounds b = mf.sharedMesh.bounds;
                float minY = float.MaxValue, maxY = float.MinValue;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float y = cloth.transform.InverseTransformPoint(r.transform.TransformPoint(corner)).y;
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
                // Something across the top of the cloth: reaches the top, and its bottom is in the upper half
                bool atTop = maxY >= cb.max.y - 0.1f * cb.size.y && minY < cb.max.y && minY > cb.center.y;
                if (atTop && minY < beamBottom)
                {
                    beamBottom = minY;
                    beamName = r.name;
                }
            }
            if (beamName == null)
            {
                Jotunn.Logger.LogDebug($"[Heraldry] '{prefab.name}': nothing found across the top of the cloth; using all of it.");
                return 0f;
            }
            float hidden = (cb.max.y - beamBottom) / cb.size.y;
            if (hidden > 0.3f)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] '{prefab.name}': '{beamName}' would hide {hidden:P0} of the cloth; ignoring it.");
                return 0f;
            }
            return Mathf.Max(0f, hidden);
        }

        public static bool IsCloth(Material m) => m != null && m.shader != null && m.shader.name == ClothShaderName;

        /// <summary>Renderers using the cloth shader, excluding the pieces that fly off when the banner breaks.</summary>
        public static List<Renderer> ClothRenderers(GameObject root)
        {
            var fragmentRoots = FragmentRoots(root);

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

        private static HashSet<Transform> FragmentRoots(GameObject root)
        {
            var fragmentRoots = new HashSet<Transform>();
            var wearNTear = root.GetComponent<WearNTear>();
            if (wearNTear != null && wearNTear.m_fragmentRoots != null)
                foreach (var r in wearNTear.m_fragmentRoots)
                    if (r != null)
                        fragmentRoots.Add(r.transform);
            return fragmentRoots;
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
