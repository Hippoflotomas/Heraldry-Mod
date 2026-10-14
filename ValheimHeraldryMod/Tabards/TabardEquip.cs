using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using ValheimHeraldryMod.Rendering;

namespace ValheimHeraldryMod.Tabards
{
    /// <summary>
    ///     Wearing the tabard (TabardShare's method): tabards are ItemType.Misc, which vanilla never equips, so these
    ///     patches make ours equipable and keep the worn one in a <see cref="HeraldicTabardWearer"/> on the player.
    ///     Nothing else is displaced. If TabardShare is installed too, the two share the slot: putting one on takes
    ///     the other off (Hippo, 2026-10-14).
    /// </summary>
    [HarmonyPatch]
    internal static class TabardEquip
    {
        private static readonly MethodInfo TriggerEquipEffect = AccessTools.Method(typeof(Humanoid), "TriggerEquipEffect");

        // TabardShare, if installed (looked up once, by name; it's optional)
        private static bool lookedUp;
        private static MethodInfo otherIsTabard;
        private static Type otherWearer;
        private static FieldInfo otherWearerItem;

        private static void LookUpTabardShare()
        {
            if (lookedUp) return;
            lookedUp = true;
            var items = FindType("TabardShare.TabardItems");
            otherIsTabard = items != null ? AccessTools.Method(items, "IsTabard", new[] { typeof(ItemDrop.ItemData) }) : null;
            otherWearer = FindType("TabardShare.TabardWearer");
            otherWearerItem = otherWearer != null ? AccessTools.Field(otherWearer, "Item") : null;
            if (otherIsTabard != null) Jotunn.Logger.LogInfo("[Heraldry] TabardShare found: its tabards and ours share one slot.");
        }

        /// <summary>A type by full name from the loaded assemblies, quietly (AccessTools.TypeByName logs a warning when it's absent).</summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = assembly.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { /* dynamic or unloadable assemblies */ }
            }
            return null;
        }

        private static bool IsOtherTabard(ItemDrop.ItemData item)
        {
            LookUpTabardShare();
            try { return otherIsTabard != null && item != null && (bool)otherIsTabard.Invoke(null, new object[] { item }); }
            catch { return false; }
        }

        private static ItemDrop.ItemData OtherWorn(Player player)
        {
            LookUpTabardShare();
            if (otherWearer == null || otherWearerItem == null) return null;
            var w = player.GetComponent(otherWearer);
            return w != null ? otherWearerItem.GetValue(w) as ItemDrop.ItemData : null;
        }

        private static void PlayEquipEffect(Humanoid humanoid, ItemDrop.ItemData item)
        {
            try { TriggerEquipEffect?.Invoke(humanoid, new object[] { item }); }
            catch { } // purely cosmetic
        }

        internal static bool Equip(Player player, ItemDrop.ItemData item, bool triggerEquipEffects)
        {
            var wearer = HeraldicTabardWearer.Get(player);
            if (wearer == null || wearer.Item == item || !player.GetInventory().ContainsItem(item)) return false;
            if (wearer.Item != null) Unequip(player, wearer.Item, triggerEquipEffects);
            var other = OtherWorn(player);
            if (other != null) player.UnequipItem(other, false); // one tabard at a time

            wearer.Item = item;
            item.m_equipped = true;
            wearer.Publish();
            if (triggerEquipEffects) PlayEquipEffect(player, item);
            return true;
        }

        internal static void Unequip(Player player, ItemDrop.ItemData item, bool triggerEquipEffects)
        {
            var wearer = HeraldicTabardWearer.Get(player);
            if (wearer == null || wearer.Item != item) return;
            wearer.Item = null;
            item.m_equipped = false;
            wearer.Publish();
            if (triggerEquipEffects) PlayEquipEffect(player, item);
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.IsEquipable))]
        [HarmonyPostfix]
        private static void IsEquipable(ItemDrop.ItemData __instance, ref bool __result)
        {
            if (!__result && TabardItem.IsTabard(__instance)) __result = true;
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        [HarmonyPrefix]
        private static bool EquipItem(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects, ref bool __result)
        {
            if (!TabardItem.IsTabard(item)) return true;
            __result = __instance is Player player && Equip(player, item, triggerEquipEffects);
            return false;
        }

        /// <summary>A TabardShare tabard went on: take ours off.</summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        [HarmonyPostfix]
        private static void AfterEquip(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || !(__instance is Player player) || TabardItem.IsTabard(item) || !IsOtherTabard(item)) return;
            var wearer = player.GetComponent<HeraldicTabardWearer>();
            if (wearer != null && wearer.Item != null) Unequip(player, wearer.Item, false);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        [HarmonyPrefix]
        private static bool UnequipItem(Humanoid __instance, ItemDrop.ItemData item, bool triggerEquipEffects)
        {
            if (!TabardItem.IsTabard(item)) return true;
            if (__instance is Player player) Unequip(player, item, triggerEquipEffects);
            return false;
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsItemEquiped))]
        [HarmonyPostfix]
        private static void IsItemEquiped(Humanoid __instance, ItemDrop.ItemData item, ref bool __result)
        {
            if (__result || item == null || !(__instance is Player)) return;
            var wearer = __instance.GetComponent<HeraldicTabardWearer>();
            if (wearer != null && wearer.Item == item) __result = true;
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipAllItems))]
        [HarmonyPostfix]
        private static void UnequipAll(Humanoid __instance)
        {
            if (!(__instance is Player player)) return;
            var wearer = player.GetComponent<HeraldicTabardWearer>();
            if (wearer != null && wearer.Item != null) Unequip(player, wearer.Item, false);
        }
    }

    /// <summary>The tabard a player wears; publishes "1|blazon" (or "") to the player's ZDO for everyone to draw.</summary>
    internal class HeraldicTabardWearer : MonoBehaviour
    {
        public const string ZdoKey = "ValheimHeraldryMod_Tabard";
        public ItemDrop.ItemData Item;
        private Player player;
        private ZNetView nview;

        public static HeraldicTabardWearer Get(Player player)
        {
            if (player == null) return null;
            return player.GetComponent<HeraldicTabardWearer>() ?? player.gameObject.AddComponent<HeraldicTabardWearer>();
        }

        private void Awake()
        {
            player = GetComponent<Player>();
            nview = GetComponent<ZNetView>();
        }

        /// <summary>Owner only. Also call after repainting the worn tabard.</summary>
        public void Publish()
        {
            if (nview == null) nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            string value = Item != null ? "1|" + Shields.HeraldicShield.BlazonOf(Item) : "";
            var zdo = nview.GetZDO();
            if (zdo.GetString(ZdoKey, "") != value) zdo.Set(ZdoKey, value);
        }

        private void Update()
        {
            // Safety net: the worn tabard left the inventory by a route that didn't unequip it
            if (Item != null && player != null && !player.GetInventory().ContainsItem(Item))
            {
                Item.m_equipped = false;
                Item = null;
                Publish();
            }
        }
    }

    /// <summary>Draws worn tabards on every client, from the player's ZDO.</summary>
    [HarmonyPatch]
    internal static class TabardVisuals
    {
        private sealed class State
        {
            public string Current = "";
            public TabardTextures.Entry Entry;
            public GameObject Instance;
            public bool Failed;
            public int ModelIndex = -1;
        }

        private static readonly ConditionalWeakTable<VisEquipment, State> States = new ConditionalWeakTable<VisEquipment, State>();
        private static readonly AccessTools.FieldRef<VisEquipment, ZNetView> VisNView = AccessTools.FieldRefAccess<VisEquipment, ZNetView>("m_nview");
        private static readonly AccessTools.FieldRef<VisEquipment, bool> VisIsPlayer = AccessTools.FieldRefAccess<VisEquipment, bool>("m_isPlayer");
        private static readonly AccessTools.FieldRef<VisEquipment, int> VisModelIndex = AccessTools.FieldRefAccess<VisEquipment, int>("m_modelIndex");
        private static readonly MethodInfo UpdateLodgroup = AccessTools.Method(typeof(VisEquipment), "UpdateLodgroup");
        private const int FemaleModelIndex = 1;
        private static bool loggedError;

        [HarmonyPatch(typeof(VisEquipment), "UpdateEquipmentVisuals")]
        [HarmonyPostfix]
        private static void Postfix(VisEquipment __instance)
        {
            try { Refresh(__instance); }
            catch (Exception ex)
            {
                if (loggedError) return;
                loggedError = true;
                Jotunn.Logger.LogError($"[Heraldry] Drawing a tabard failed (further errors hidden): {ex}");
            }
        }

        private static void Refresh(VisEquipment vis)
        {
            if (!VisIsPlayer(vis)) return;
            var nview = VisNView(vis);
            if (nview == null || !nview.IsValid()) return;
            var state = States.GetOrCreateValue(vis);

            string wanted = nview.GetZDO().GetString(HeraldicTabardWearer.ZdoKey, "");
            string key = wanted.IndexOf('[') >= 0 ? wanted + "|" + ChargeLibrary.Generation : wanted; // picture charges arriving: redraw
            if (key != state.Current)
            {
                if (state.Instance != null) UnityEngine.Object.Destroy(state.Instance);
                state.Instance = null;
                TabardTextures.Release(state.Entry);
                state.Entry = null;
                state.Failed = false;
                state.Current = key;
                if (wanted.Length > 0)
                {
                    string blazon = wanted.Length > 2 ? wanted.Substring(2) : "";
                    state.Entry = TabardTextures.Acquire(BlazonTextureCache.Normalise(blazon));
                }
                else Lods(vis);
            }

            if (state.Instance == null && state.Entry != null && state.Entry.Ready && !state.Failed)
            {
                state.Instance = Attach(vis, state.Entry.Material);
                state.Failed = state.Instance == null;
                state.ModelIndex = -1;
            }

            if (state.Instance != null)
            {
                int model = VisModelIndex(vis);
                if (model != state.ModelIndex)
                {
                    state.ModelIndex = model;
                    var smr = state.Instance.GetComponent<SkinnedMeshRenderer>();
                    int shape = TabardMesh.Mesh.GetBlendShapeIndex("Female");
                    if (smr != null && shape >= 0) smr.SetBlendShapeWeight(shape, model == FemaleModelIndex ? 100f : 0f);
                }
            }
        }

        /// <summary>Builds the tabard on a character, its mesh bound to the body's bones by name (TabardShare's method).</summary>
        private static GameObject Attach(VisEquipment vis, Material material)
        {
            var body = vis.m_bodyModel;
            if (body == null || TabardMesh.Mesh == null || material == null) return null;

            var bodyBones = new Dictionary<string, Transform>();
            foreach (var bone in body.bones)
                if (bone != null) bodyBones[bone.name] = bone;
            var names = TabardMesh.BoneNames;
            var bones = new Transform[names.Length];
            var missing = new List<string>();
            for (int i = 0; i < names.Length; i++)
                if (!bodyBones.TryGetValue(names[i], out bones[i])) missing.Add(names[i]);
            if (missing.Count > 0)
            {
                Jotunn.Logger.LogError($"[Heraldry] The tabard uses bones this character doesn't have: {string.Join(", ", missing.ToArray())}");
                return null;
            }

            var go = new GameObject("Heraldry_Tabard");
            go.layer = body.gameObject.layer;
            go.transform.SetParent(body.transform.parent, false);
            go.transform.localPosition = body.transform.localPosition;
            go.transform.localRotation = body.transform.localRotation;
            var smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = TabardMesh.Mesh;
            smr.bones = bones;
            smr.rootBone = body.rootBone;
            smr.sharedMaterial = material;
            var bounds = body.localBounds;
            bounds.Expand(0.3f);
            smr.localBounds = bounds;
            Lods(vis);
            return go;
        }

        private static void Lods(VisEquipment vis)
        {
            try { UpdateLodgroup?.Invoke(vis, null); }
            catch { } // without it the tabard just doesn't take part in LOD switching
        }
    }
}
