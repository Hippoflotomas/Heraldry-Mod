using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ValheimHeraldryMod.Tabards
{
    /// <summary>
    ///     The heraldic tabard item: a cosmetic, built at the herald's table (3 deer hide, 2 leather scraps; Hippo),
    ///     painted there like a shield (blazon in its custom data). A clone of the linen cape turned into a plain item
    ///     (no armour, no effects, no durability) worn in its own slot (TabardEquip), as in TabardShare.
    /// </summary>
    internal static class TabardItem
    {
        public const string PrefabName = "ValheimHeraldryMod_Tabard";
        private const string BasePrefab = "CapeLinen";
        private static Material template;

        public static bool IsTabard(ItemDrop.ItemData item) =>
            item?.m_dropPrefab != null && item.m_dropPrefab.name == PrefabName;

        public static void Init()
        {
            PrefabManager.OnVanillaPrefabsAvailable += Register;
            LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
            {
                { "ValheimHeraldryMod_Tabard", "Heraldic tabard" },
                { "ValheimHeraldryMod_Tabard_description", "A plain linen tabard, to be painted with arms at the herald's table. Worn over your armour." },
            });
        }

        private static void Register()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Register;
            try
            {
                if (!TabardMesh.Load()) return;
                SetupMaterialTemplate();
                AddItem();
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogError($"[Heraldry] The tabard couldn't be registered: {ex}");
            }
        }

        private static void AddItem()
        {
            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, BasePrefab);
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null) throw new System.Exception("couldn't clone " + BasePrefab);

            // A plain cosmetic in its own slot. Misc stops vanilla equipping it as a cape; TabardEquip makes it equipable.
            var shared = drop.m_itemData.m_shared;
            shared.m_name = "$ValheimHeraldryMod_Tabard";
            shared.m_description = "$ValheimHeraldryMod_Tabard_description";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc;
            shared.m_armor = 0f;
            shared.m_armorPerLevel = 0f;
            shared.m_equipStatusEffect = null;
            shared.m_setStatusEffect = null;
            shared.m_setName = "";
            shared.m_setSize = 0;
            shared.m_damageModifiers = new List<HitData.DamageModPair>();
            shared.m_movementModifier = 0f;
            shared.m_eitrRegenModifier = 0f;
            shared.m_heatResistanceModifier = 0f;
            shared.m_maxQuality = 1;
            shared.m_useDurability = false;
            shared.m_weight = 1f;
            shared.m_teleportable = true;
            shared.m_variants = 0;

            var config = new ItemConfig
            {
                Name = "$ValheimHeraldryMod_Tabard",
                Description = "$ValheimHeraldryMod_Tabard_description",
                CraftingStation = Station.TableRegistry.PrefabName,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("DeerHide", 3, 0, true),
                    new RequirementConfig("LeatherScraps", 2, 0, true),
                },
            };
            ItemManager.Instance.AddItem(new CustomItem(prefab, false, config));
            Jotunn.Logger.LogInfo("[Heraldry] Registered the heraldic tabard.");
        }

        /// <summary>
        ///     The tabard's material, as TabardShare makes it: the linen cape's (Custom/Creature), drawn from both sides
        ///     (_Cull 0), with the cape's own paint styles switched off and the banners' cloth weave as its normal map.
        /// </summary>
        private static void SetupMaterialTemplate()
        {
            var cape = PrefabManager.Instance.GetPrefab(BasePrefab);
            var source = cape != null ? cape.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null) : null;
            if (source == null)
            {
                Jotunn.Logger.LogWarning("[Heraldry] No linen cape material for the tabard; using Custom/Creature.");
                source = new Material(Shader.Find("Custom/Creature") ?? Shader.Find("Standard"));
            }
            template = new Material(source) { name = "Heraldry_Tabard_Template" };
            SetFloat(template, "_UseStyles", 0f);
            template.DisableKeyword("_USESTYLES_ON");
            if (template.HasProperty("_StyleTex")) template.SetTexture("_StyleTex", null);
            // The banners' plain twill weave instead of the cape's normal map (Hippo, 2026-10-14)
            var weave = Rendering.ClothNormal.Get();
            if (template.HasProperty("_BumpMap")) template.SetTexture("_BumpMap", weave);
            if (weave != null) template.EnableKeyword("_NORMALMAP");
            else template.DisableKeyword("_NORMALMAP");
            SetFloat(template, "_Cull", 0f);
            if (template.HasProperty("_Color")) template.SetColor("_Color", Color.white);
            template.mainTextureScale = Vector2.one;
            template.mainTextureOffset = Vector2.zero;
            Jotunn.Logger.LogInfo($"[Heraldry] Tabard material based on '{source.name}' ({source.shader.name}).");
        }

        public static Material CreateMaterial(Texture2D mainTex, string name)
        {
            var mat = new Material(template) { name = name };
            mat.SetTexture("_MainTex", mainTex);
            return mat;
        }

        private static void SetFloat(Material m, string property, float value)
        {
            if (m.HasProperty(property)) m.SetFloat(property, value);
        }
    }
}
