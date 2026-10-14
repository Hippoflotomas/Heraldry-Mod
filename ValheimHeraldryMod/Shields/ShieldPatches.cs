using HarmonyLib;
using UnityEngine;
using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Shields
{
    /// <summary>
    ///     Where painted shields appear, and how other clients learn their arms (the game only sends item hashes):
    ///     <list type="bullet">
    ///         <item>in hand and on the back: the owner writes both blazons into the player's ZDO (ShieldShare's trick);</item>
    ///         <item>on the ground: the item's own data, which the game already saves with dropped items;</item>
    ///         <item>on item stands: written next to the item in the stand's ZDO whenever the game saves an item there.</item>
    ///     </list>
    ///     Armour stands: the same, under the slot's number.
    /// </summary>
    [HarmonyPatch]
    internal static class ShieldPatches
    {
        internal const string ZdoLeft = "ValheimHeraldryMod_ShieldLeft";
        internal const string ZdoBack = "ValheimHeraldryMod_ShieldBack";
        internal const string ZdoItem = "ValheimHeraldryMod_Item";

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> LeftItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_leftItem");
        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenLeftItem = AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenLeftItem");
        private static readonly AccessTools.FieldRef<Humanoid, VisEquipment> HumanoidVis = AccessTools.FieldRefAccess<Humanoid, VisEquipment>("m_visEquipment");
        private static readonly AccessTools.FieldRef<VisEquipment, ZNetView> VisNView = AccessTools.FieldRefAccess<VisEquipment, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<VisEquipment, bool> VisIsPlayer = AccessTools.FieldRefAccess<VisEquipment, bool>("m_isPlayer");
        private static readonly AccessTools.FieldRef<VisEquipment, bool> VisIsArmorStand = AccessTools.FieldRefAccess<VisEquipment, bool>("m_isArmorStand");
        private static readonly AccessTools.FieldRef<ArmorStand, ZNetView> StandNViewA = AccessTools.FieldRefAccess<ArmorStand, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> VisLeft = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> VisLeftBack = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_leftBackItemInstance");
        private static readonly AccessTools.FieldRef<VisEquipment, int> VisLeftHash = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLeftItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> VisLeftBackHash = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLeftBackItemHash");
        private static readonly AccessTools.FieldRef<ItemStand, ZNetView> StandNView = AccessTools.FieldRefAccess<ItemStand, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<ItemStand, GameObject> StandVisual = AccessTools.FieldRefAccess<ItemStand, GameObject>("m_visualItem");
        private static readonly AccessTools.FieldRef<ItemStand, int> StandVisualHash = AccessTools.FieldRefAccess<ItemStand, int>("m_visualHash");
        private static readonly System.Reflection.MethodInfo SetupVisEquipment = AccessTools.Method(typeof(Humanoid), "SetupVisEquipment");

        /// <summary>Re-publish a player's equipment, e.g. right after painting the shield in their hand.</summary>
        public static void RefreshEquipment(Player player)
        {
            var vis = player != null ? HumanoidVis(player) : null;
            if (vis != null) SetupVisEquipment?.Invoke(player, new object[] { vis, false });
        }

        /// <summary>The face of an item's shield model (for the editor's preview), or null.</summary>
        public static ShieldFace FaceForItem(ItemDrop.ItemData item)
        {
            var prefab = item?.m_dropPrefab;
            if (prefab == null) return null;
            var tagged = TaggedFace.For(prefab, out _); // another modder's tagged face wins
            if (tagged != null) return ShieldFace.FromTagged(tagged, prefab.name);
            return ShieldFace.For(prefab.name, ShieldFace.FindModel(prefab));
        }

        private static string PrefabName(int hash)
        {
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(hash) : null;
            var drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            return drop != null && HeraldicShield.IsShield(drop.m_itemData) && Paintable(prefab) ? prefab.name : null;
        }

        /// <summary>Not on the exclude list, or carrying a tagged face (its modder asked for it to be painted).</summary>
        public static bool Paintable(GameObject prefab) =>
            prefab != null && (!HeraldryConfig.IsShieldExcluded(prefab.name) || TaggedFace.HasTag(prefab));

        private static void SetIfChanged(ZDO zdo, string key, string value)
        {
            if (zdo.GetString(key, "") != value) zdo.Set(key, value);
        }

        /// <summary>Owner: publish the arms on the shield in hand and on the back.</summary>
        [HarmonyPatch(typeof(Humanoid), "SetupVisEquipment")]
        [HarmonyPostfix]
        private static void PublishShields(Humanoid __instance)
        {
            if (!(__instance is Player)) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            var zdo = nview.GetZDO();
            SetIfChanged(zdo, ZdoLeft, HeraldicShield.BlazonOf(LeftItem(__instance)));
            SetIfChanged(zdo, ZdoBack, HeraldicShield.BlazonOf(HiddenLeftItem(__instance)));
        }

        /// <summary>Everyone: paint the shield models a player is showing (in hand, on the back).</summary>
        [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
        [HarmonyPostfix]
        private static void PaintEquipment(VisEquipment __instance)
        {
            if (VisIsPlayer(__instance))
            {
                var nview = VisNView(__instance);
                if (nview == null || !nview.IsValid()) return;
                Hook(VisLeft(__instance), VisLeftHash(__instance), nview, ZdoLeft);
                Hook(VisLeftBack(__instance), VisLeftBackHash(__instance), nview, ZdoBack);
            }
            else if (VisIsArmorStand(__instance))
            {
                // Armour stands: each slot's item is saved under its slot number, and we note the arms beside it
                var left = VisLeft(__instance);
                var back = VisLeftBack(__instance);
                bool needLeft = left != null && left.GetComponent<HeraldicShield>() == null;
                bool needBack = back != null && back.GetComponent<HeraldicShield>() == null;
                if (!needLeft && !needBack) return;
                var stand = __instance.GetComponentInParent<ArmorStand>();
                var nview = stand != null ? StandNViewA(stand) : null;
                if (nview == null || !nview.IsValid()) return;
                for (int i = 0; i < stand.m_slots.Count; i++)
                {
                    var slot = stand.m_slots[i].m_slot;
                    if (needLeft && slot == VisSlot.HandLeft) Hook(left, VisLeftHash(__instance), nview, i + "_" + ZdoItem);
                    if (needBack && slot == VisSlot.BackLeft) Hook(back, VisLeftBackHash(__instance), nview, i + "_" + ZdoItem);
                }
            }
        }

        private static void Hook(GameObject instance, int hash, ZNetView nview, string key)
        {
            if (instance == null || instance.GetComponent<HeraldicShield>() != null) return;
            string name = PrefabName(hash);
            if (name == null) return;
            HeraldicShield.Attach(instance, name, () => nview != null && nview.IsValid() ? nview.GetZDO().GetString(key, "") : "");
        }

        /// <summary>Shields lying on the ground.</summary>
        [HarmonyPatch(typeof(ItemDrop), "Start")]
        [HarmonyPostfix]
        private static void PaintDropped(ItemDrop __instance)
        {
            var item = __instance.m_itemData;
            if (!HeraldicShield.IsShield(item)) return;
            string name = item.m_dropPrefab != null ? item.m_dropPrefab.name : Utils.GetPrefabName(__instance.gameObject);
            if (HeraldryConfig.IsShieldExcluded(name) && !TaggedFace.HasTag(item.m_dropPrefab)) return;
            HeraldicShield.Attach(__instance.gameObject, name, () => HeraldicShield.BlazonOf(__instance.m_itemData));
        }

        /// <summary>
        ///     Whenever the game saves an item into a ZDO (dropped item, item stand, armour-stand slot), note its arms
        ///     beside it, so clients showing it only from the ZDO can paint it. Same key scheme as the game's own.
        /// </summary>
        [HarmonyPatch(typeof(ItemDrop), "SaveToZDO", typeof(ItemDrop.ItemData), typeof(ZDO), typeof(int))]
        [HarmonyPostfix]
        private static void NoteArms(ItemDrop.ItemData itemData, ZDO zdo, int index)
        {
            if (zdo == null) return;
            string key = index < 0 ? ZdoItem : index + "_" + ZdoItem;
            string text = HeraldicShield.BlazonOf(itemData);
            if (text.Length > 0 || zdo.GetString(key, "").Length > 0) SetIfChanged(zdo, key, text);
        }

        /// <summary>Shields hung on item stands.</summary>
        [HarmonyPatch(typeof(ItemStand), "SetVisualItem")]
        [HarmonyPostfix]
        private static void PaintOnStand(ItemStand __instance)
        {
            var visual = StandVisual(__instance);
            if (visual == null) return;
            string name = PrefabName(StandVisualHash(__instance));
            if (name == null) return;
            var nview = StandNView(__instance);
            HeraldicShield.Attach(visual, name, () => nview != null && nview.IsValid() ? nview.GetZDO().GetString(ZdoItem, "") : "");
        }
    }

    /// <summary>The arms in a painted item's tooltip (its own class, so a signature change only loses this).</summary>
    [HarmonyPatch]
    internal static class ShieldTooltipPatch
    {
        /// <summary>The arms in the item's tooltip.</summary>
        [HarmonyPatch(typeof(ItemDrop.ItemData), "GetTooltip",
            typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
        [HarmonyPostfix]
        private static void Tooltip(ItemDrop.ItemData item, ref string __result)
        {
            string text = HeraldicShield.BlazonOf(item);
            if (text.Length > 0) __result += $"\n\n<color=orange>Arms:</color> {Rendering.TextArt.ForDisplay(BlazonText.Visible(text))}";
        }
    }
}
