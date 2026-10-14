using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Paints
{
    /// <summary>
    ///     The five paints (Hippo, 2026-10-14): bottles crafted at the herald's table, paid when painting arms.
    ///     Mixed like real paint (red, yellow, blue, black, white). The model is Hippo's paint bottle
    ///     (Art/PaintBottle1.fbx → PaintBottle.hmesh); its "Colour" band takes each paint's colour, so one model
    ///     serves all five. Recipes and the paid items are all config (HeraldryConfig, section Paint).
    /// </summary>
    internal static class PaintItems
    {
        private const string BasePrefab = "Coal";
        private const string ModelResource = "ValheimHeraldryMod.PaintBottle.hmesh";
        /// <summary>The bottle as modelled is the table's pot size (11.5 cm), too small to spot on the ground.</summary>
        private const float BottleScale = 2f;

        public static string PrefabName(Paint paint) => "ValheimHeraldryMod_Paint" + paint;

        public static string DefaultRecipe(Paint paint)
        {
            switch (paint)
            {
                case Paint.Red: return "Raspberry:1";
                case Paint.Yellow: return "Dandelion:1";
                case Paint.Blue: return "Blueberries:1";
                case Paint.Black: return "Coal:1";
                default: return "BoneFragments:1";
            }
        }

        /// <summary>The bottle's colour: the tincture it paints, in the mod's cloth palette.</summary>
        private static Color32 BottleColour(Paint paint)
        {
            string t = paint == Paint.Red ? "gules" : paint == Paint.Yellow ? "or" : paint == Paint.Blue ? "azure"
                     : paint == Paint.Black ? "sable" : "argent";
            var c = Lexicon.Palette[t];
            return new Color32(c.R, c.G, c.B, 255);
        }

        public static void Init()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
            var english = new Dictionary<string, string>();
            foreach (var paint in PaintCost.All)
            {
                string name = paint.ToString();
                english["ValheimHeraldryMod_Paint" + name] = name + " paint";
                english["ValheimHeraldryMod_Paint" + name + "_description"] =
                    $"A bottle of {name.ToLowerInvariant()} paint, for painting arms at the herald's table.";
            }
            LocalizationManager.Instance.GetLocalization().AddTranslation("English", english);

            // Recipes follow the config, also when the server's values arrive or an admin changes them
            HeraldryConfig.PaintSettingsChanged += ApplyRecipes;
            SynchronizationManager.OnConfigurationSynchronized += (_, __) => ApplyRecipes();
            ItemManager.OnItemsRegistered += ApplyRecipes;
        }

        private static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            try
            {
                var parts = Station.HMesh.Load(ModelResource);
                foreach (var paint in PaintCost.All) AddItem(paint, parts);
                Jotunn.Logger.LogInfo("[Heraldry] Registered the five paints.");
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogError($"[Heraldry] The paints couldn't be registered: {ex}");
            }
        }

        private static void AddItem(Paint paint, List<Station.HMesh.Part> parts)
        {
            string prefabName = PrefabName(paint);
            var prefab = PrefabManager.Instance.CreateClonedPrefab(prefabName, BasePrefab);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) throw new System.Exception("couldn't clone " + BasePrefab);

            // Out with the coal's looks (model, attach, effects); the rigidbody, ItemDrop and ZNetView stay
            foreach (Transform child in prefab.transform.Cast<Transform>().ToList()) Object.DestroyImmediate(child.gameObject);
            foreach (var ps in prefab.GetComponents<ParticleSystem>()) Object.DestroyImmediate(ps); // the coal's glow; before its renderer
            foreach (var lod in prefab.GetComponents<LODGroup>()) Object.DestroyImmediate(lod);
            foreach (var col in prefab.GetComponents<Collider>()) Object.DestroyImmediate(col);
            foreach (var r in prefab.GetComponents<Renderer>()) Object.DestroyImmediate(r);
            foreach (var mf in prefab.GetComponents<MeshFilter>()) Object.DestroyImmediate(mf);

            var materials = Materials(paint);
            var model = Bottle("model", prefab, parts, materials);
            var size = new Vector3(0.1f, 0.115f, 0.1f);
            var box = model.AddComponent<BoxCollider>();
            box.size = size;
            box.center = new Vector3(0f, size.y / 2f, 0f);
            Bottle("attach", prefab, parts, materials); // what item stands show

            var shared = drop.m_itemData.m_shared;
            shared.m_name = "$" + prefabName;
            shared.m_description = "$" + prefabName + "_description";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Material;
            shared.m_maxStackSize = 50;
            shared.m_weight = 0.3f;
            shared.m_teleportable = true;
            shared.m_value = 0;

            var icon = Icon(prefab);
            if (icon != null) shared.m_icons = new[] { icon };

            var config = new ItemConfig
            {
                Name = shared.m_name,
                Description = shared.m_description,
                CraftingStation = Station.TableRegistry.PrefabName,
                MinStationLevel = 1,
                Amount = HeraldryConfig.PaintRecipeAmount,
                Requirements = ParseRecipe(HeraldryConfig.PaintRecipe(paint))
                    .Select(r => new RequirementConfig(r.Key, r.Value, 0, true)).ToArray(),
            };
            if (icon != null) config.Icons = new[] { icon };
            ItemManager.Instance.AddItem(new CustomItem(prefab, false, config));
        }

        private static GameObject Bottle(string name, GameObject prefab, List<Station.HMesh.Part> parts, Dictionary<string, Material> materials)
        {
            var root = new GameObject(name) { layer = prefab.layer };
            root.transform.SetParent(prefab.transform, false);
            root.transform.localScale = Vector3.one * BottleScale;
            foreach (var part in parts)
            {
                var go = new GameObject(part.Name) { layer = prefab.layer };
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial =
                    materials.TryGetValue(part.Name, out var m) ? m : materials["Glass"];
            }
            return root;
        }

        private static Texture2D white;

        /// <summary>Glass as on the herald's table's pots; the Colour band a flat, slightly glossy paint.</summary>
        private static Dictionary<string, Material> Materials(Paint paint)
        {
            var table = Station.TableRegistry.VanillaMaterials();
            if (white == null)
            {
                white = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Heraldry_White" };
                var px = new Color32[16];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                white.SetPixels32(px);
                white.Apply(false, true);
            }
            var colour = new Material(table["Metal"]) { name = "Heraldry_Paint" + paint, color = BottleColour(paint) };
            colour.mainTexture = white;
            colour.mainTextureScale = Vector2.one;
            if (colour.HasProperty("_BumpMap")) colour.SetTexture("_BumpMap", null);
            colour.DisableKeyword("_NORMALMAP");
            if (colour.HasProperty("_MetalTex")) colour.SetTexture("_MetalTex", null);
            if (colour.HasProperty("_Metallic")) colour.SetFloat("_Metallic", 0f);
            if (colour.HasProperty("_Glossiness")) colour.SetFloat("_Glossiness", 0.45f);
            return new Dictionary<string, Material> { { "Glass", table["Glass"] }, { "Colour", colour } };
        }

        private static Sprite Icon(GameObject prefab)
        {
            try
            {
                return RenderManager.Instance.Render(new RenderManager.RenderRequest(prefab)
                {
                    Rotation = Quaternion.Euler(20f, 25f, 0f),
                    Width = 128,
                    Height = 128,
                });
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't render a paint icon ({ex.Message}).");
                return null;
            }
        }

        /// <summary>"Raspberry:1, Dandelion:2" → item → amount. A bare name counts one; junk is skipped and logged.</summary>
        public static List<KeyValuePair<string, int>> ParseRecipe(string text)
        {
            var result = new List<KeyValuePair<string, int>>();
            foreach (var raw in (text ?? "").Split(','))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                int colon = part.IndexOf(':');
                string item = (colon < 0 ? part : part.Substring(0, colon)).Trim();
                int amount = 1;
                if (colon >= 0 && (!int.TryParse(part.Substring(colon + 1).Trim(), out amount) || amount < 1))
                {
                    Jotunn.Logger.LogWarning($"[Heraldry] Paint recipe: '{part}' isn't item:amount; skipped.");
                    continue;
                }
                if (item.Length > 0) result.Add(new KeyValuePair<string, int>(item, amount));
            }
            return result;
        }

        /// <summary>Re-reads the recipes from the config into the game's recipe list (server values, admin changes).</summary>
        private static void ApplyRecipes()
        {
            if (ObjectDB.instance == null || ObjectDB.instance.m_recipes == null) return;
            foreach (var paint in PaintCost.All)
            {
                string prefabName = PrefabName(paint);
                var recipe = ObjectDB.instance.m_recipes.FirstOrDefault(r => r != null && r.m_item != null && r.m_item.name == prefabName);
                if (recipe == null) continue;
                var resources = new List<Piece.Requirement>();
                foreach (var kv in ParseRecipe(HeraldryConfig.PaintRecipe(paint)))
                {
                    var item = ObjectDB.instance.GetItemPrefab(kv.Key)?.GetComponent<ItemDrop>();
                    if (item == null)
                    {
                        Jotunn.Logger.LogWarning($"[Heraldry] {paint} paint recipe: no item '{kv.Key}'; left out.");
                        continue;
                    }
                    resources.Add(new Piece.Requirement { m_resItem = item, m_amount = kv.Value, m_recover = true });
                }
                if (resources.Count == 0) continue; // keep the old recipe rather than make paint from nothing
                recipe.m_resources = resources.ToArray();
                recipe.m_amount = HeraldryConfig.PaintRecipeAmount;
            }
        }

        // ================================================================== paying

        /// <summary>The item paid for this paint (by the config), or null if the config names no item.</summary>
        public static ItemDrop PaidItem(Paint paint)
        {
            string name = HeraldryConfig.PaintItem(paint);
            var prefab = ObjectDB.instance != null && name.Length > 0 ? ObjectDB.instance.GetItemPrefab(name) : null;
            return prefab != null ? prefab.GetComponent<ItemDrop>() : null;
        }

        public static Sprite IconOf(Paint paint)
        {
            var item = PaidItem(paint);
            return item != null ? item.m_itemData.GetIcon() : null;
        }

        public static string NameOf(Paint paint)
        {
            var item = PaidItem(paint);
            return item != null ? Localization.instance.Localize(item.m_itemData.m_shared.m_name) : HeraldryConfig.PaintItem(paint);
        }

        public static int Have(Player player, Paint paint)
        {
            var item = PaidItem(paint);
            return item == null || player == null ? 0 : player.GetInventory().CountItems(item.m_itemData.m_shared.m_name);
        }

        /// <summary>A paint whose configured item doesn't exist is free (a typo in the config shouldn't stop all painting).</summary>
        public static bool CanPay(Player player, Dictionary<Paint, int> cost) =>
            cost.All(kv => PaidItem(kv.Key) == null || Have(player, kv.Key) >= kv.Value);

        public static void Pay(Player player, Dictionary<Paint, int> cost)
        {
            foreach (var kv in cost)
            {
                var item = PaidItem(kv.Key);
                if (item != null) player.GetInventory().RemoveItem(item.m_itemData.m_shared.m_name, kv.Value);
            }
        }
    }
}
