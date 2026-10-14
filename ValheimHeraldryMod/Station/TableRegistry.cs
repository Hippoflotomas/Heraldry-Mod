using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using ValheimHeraldryMod.Banners;

namespace ValheimHeraldryMod.Station
{
    /// <summary>
    ///     Registers the herald's table: a clone of the vanilla workbench (so it's a real crafting station, with
    ///     its area marker and effects) whose visuals are replaced by Hippo's model, built at runtime from the
    ///     embedded .hmesh with vanilla materials on each slot.
    /// </summary>
    internal static class TableRegistry
    {
        public const string PrefabName = "ValheimHeraldryMod_HeraldsTable";
        public const string StationName = "$ValheimHeraldryMod_HeraldsTable";
        private const string ModelResource = "ValheimHeraldryMod.HeraldTable.hmesh";

        public static void Init()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
            LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
            {
                { "ValheimHeraldryMod_HeraldsTable", "Herald's table" },
                { "ValheimHeraldryMod_HeraldsTable_description", "Paint your shields with arms (Shift+E), and craft heraldic gear." },
            });
        }

        private static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            try
            {
                AddTable();
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogError($"[Heraldry] The herald's table couldn't be registered: {ex}");
            }
        }

        private static void AddTable()
        {
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, "piece_workbench");
            if (prefab == null) throw new System.Exception("couldn't clone piece_workbench");

            // Out with the workbench's looks; keep its collider object, area marker, base and effect points
            foreach (var name in new[] { "New", "Worn", "Broken", "WorkbenchBroken_Destruction", "floor_2x2_snow", "Particle System" })
            {
                var child = prefab.transform.Find(name);
                if (child != null) Object.DestroyImmediate(child.gameObject);
            }

            // Hippo's model, slot by slot
            var model = new GameObject("Model");
            model.transform.SetParent(prefab.transform, false);
            var parts = HMesh.Load(ModelResource);
            var materials = VanillaMaterials();
            Bounds bounds = default;
            bool first = true;
            HMesh.Part display = null;
            foreach (var part in parts)
            {
                var go = new GameObject(part.Name);
                go.transform.SetParent(model.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = materials.TryGetValue(part.Name, out var mat) ? mat : materials["Wood"];
                if (part.Name == "Display") display = part;
                else
                {
                    if (first) { bounds = part.Mesh.bounds; first = false; }
                    else bounds.Encapsulate(part.Mesh.bounds);
                }
            }

            // One box collider for the whole table, on the workbench's own collider object (right layer already)
            var colliderObject = prefab.transform.Find("collider");
            var box = colliderObject != null ? colliderObject.GetComponent<BoxCollider>() : null;
            if (box != null)
            {
                colliderObject.localPosition = Vector3.zero;
                colliderObject.localRotation = Quaternion.identity;
                colliderObject.localScale = Vector3.one;
                box.center = new Vector3(bounds.center.x, 0.95f * 0.5f + 0.02f, bounds.center.z);
                box.size = new Vector3(bounds.size.x, 0.95f, bounds.size.z); // the table top; the shield and pots sit on it
            }

            var wnt = prefab.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.m_new = model;
                wnt.m_worn = model;
                wnt.m_broken = model;
                wnt.m_fragmentRoots = new GameObject[0];
            }

            var station = prefab.GetComponent<CraftingStation>();
            station.m_name = StationName;
            station.m_showBasicRecipies = false; // only heraldic recipes here
            station.m_canRepair = false;         // shields repair at the forge or workbench, as before
            station.m_craftRequireRoof = true;   // like the workbench (Hippo, 2026-10-13)

            var table = prefab.AddComponent<HeraldTable>();
            if (display != null)
            {
                table.m_displayUV = display.UV.SelectMany(v => new[] { v.x, v.y }).ToArray();
                table.m_displayTriangles = display.Triangles;
            }

            var piece = prefab.GetComponent<Piece>();
            piece.m_name = StationName;
            piece.m_description = "$ValheimHeraldryMod_HeraldsTable_description";

            var config = new PieceConfig
            {
                Name = StationName,
                Description = "$ValheimHeraldryMod_HeraldsTable_description",
                PieceTable = PieceTables.Hammer,
                Category = BannerRegistry.Category,
                CraftingStation = CraftingStations.Workbench,
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 10, 0, true),
                    new RequirementConfig("Raspberry", 1, 0, true),
                    new RequirementConfig("Blueberries", 1, 0, true),
                    new RequirementConfig("Guck", 1, 0, true),
                    new RequirementConfig("LeatherScraps", 6, 0, true),
                },
            };
            var icon = Icon(prefab);
            if (icon != null)
            {
                config.Icon = icon;
                station.m_icon = icon;
            }
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
            Jotunn.Logger.LogInfo($"[Heraldry] Registered the herald's table ({parts.Count} parts, footprint {bounds.size.x:0.00} x {bounds.size.z:0.00} m).");
        }

        /// <summary>Vanilla materials for the model's slots: wood from the wood wall, metal from the iron wall.</summary>
        private static Dictionary<string, Material> materialCache;

        /// <summary>Also used by the paint bottles (glass), so built once.</summary>
        internal static Dictionary<string, Material> VanillaMaterials()
        {
            if (materialCache != null && materialCache.Values.All(m => m != null)) return materialCache;
            // By name: a piece's first renderer is often its snow layer (woodwall's is "snow_flat", whose shader
            // pushes the surface outwards and paints it white; that was the first test's spiky white table).
            Material From(string prefabName, string materialName)
            {
                var p = PrefabManager.Instance.GetPrefab(prefabName);
                var all = p != null ? p.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).Where(m => m != null).ToList()
                                    : new List<Material>();
                var found = all.FirstOrDefault(m => string.Equals(m.name, materialName, System.StringComparison.OrdinalIgnoreCase))
                            ?? all.FirstOrDefault(m => m.name.StartsWith(materialName, System.StringComparison.OrdinalIgnoreCase))
                            ?? all.FirstOrDefault(m => m.name.IndexOf("snow", System.StringComparison.OrdinalIgnoreCase) < 0);
                Jotunn.Logger.LogInfo($"[Heraldry] Table material from {prefabName}: {(found != null ? $"'{found.name}' ({found.shader.name})" : "none")}" +
                                      $"; it has {string.Join(", ", all.Select(m => m.name).Distinct())}.");
                return found;
            }

            var wood = From("woodwall", "woodwall");
            var metal = From("iron_wall_1x1", "metalwall") ?? wood;
            var result = new Dictionary<string, Material> { { "Wood", wood }, { "Metal", metal } };

            // No glass or bristle material in the game that's easy to borrow: tinted copies of the metal
            var glass = new Material(metal) { name = "Heraldry_Glass", color = new Color(0.78f, 0.86f, 0.92f) };
            if (glass.HasProperty("_Glossiness")) glass.SetFloat("_Glossiness", 0.85f);
            var brush = new Material(metal) { name = "Heraldry_Brush", color = new Color(0.08f, 0.07f, 0.06f) };
            result["Glass"] = glass;
            result["brush"] = brush;

            // The painted shield face: wood, so the grain shows through the arms; texture set per table
            var display = new Material(wood) { name = "Heraldry_TableDisplay", color = Color.white };
            display.mainTextureScale = Vector2.one;   // the wall tiles its planks; the arms must fit the face once
            display.mainTextureOffset = Vector2.zero;
            result["Display"] = display;
            materialCache = result;
            return result;
        }

        private static Sprite Icon(GameObject prefab)
        {
            try
            {
                return RenderManager.Instance.Render(new RenderManager.RenderRequest(prefab)
                {
                    Rotation = Quaternion.Euler(25f, 200f, 0f),
                    Width = 128,
                    Height = 128,
                });
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't render the table's icon ({ex.Message}); using the workbench's.");
                return null;
            }
        }
    }

    /// <summary>Shift+E on the herald's table paints a shield; E stays the normal crafting menu.</summary>
    [HarmonyPatch]
    internal static class TablePatches
    {
        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.Interact))]
        [HarmonyPrefix]
        private static bool Interact(CraftingStation __instance, Humanoid user, bool repeat, bool alt, ref bool __result)
        {
            if (!alt || repeat) return true;
            var table = __instance.GetComponent<HeraldTable>();
            if (table == null || !(user is Player player) || player != Player.m_localPlayer) return true;
            table.OpenPaint(player);
            __result = true;
            return false;
        }

        [HarmonyPatch(typeof(CraftingStation), nameof(CraftingStation.GetHoverText))]
        [HarmonyPostfix]
        private static void HoverText(CraftingStation __instance, ref string __result)
        {
            if (__instance.GetComponent<HeraldTable>() == null) return;
            __result += Localization.instance.Localize("\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Paint a shield");
        }
    }
}
