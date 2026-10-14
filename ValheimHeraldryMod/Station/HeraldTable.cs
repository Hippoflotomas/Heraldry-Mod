using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using ValheimHeraldryMod.Banners;
using ValheimHeraldryMod.Rendering;
using ValheimHeraldryMod.UI;

namespace ValheimHeraldryMod.Station
{
    /// <summary>
    ///     The herald's table. E opens its crafting menu (it's a CraftingStation); Shift+E opens the blazon editor
    ///     to paint one of your shields (the blazon goes into the item's custom data). The shield lying on the
    ///     table shows the arms last painted there, for everyone (blazon in the table's ZDO).
    /// </summary>
    public class HeraldTable : MonoBehaviour
    {
        /// <summary>Item custom-data key holding a shield's (or later tabard's) blazon.</summary>
        public const string ItemBlazonKey = "ValheimHeraldryMod_Blazon";
        public const string ShapeId = "Heraldry_HeraldTable_Display";

        // Set on the prefab by TableRegistry
        public float[] m_displayUV;
        public int[] m_displayTriangles;

        private ZNetView nview;
        private MeshRenderer display;
        private Material displayMaterial;
        private uint lastRevision = uint.MaxValue;
        private string cacheKey;
        private string currentText;

        private void Start()
        {
            nview = GetComponent<ZNetView>();
            var t = transform.Find("Model/Display");
            display = t != null ? t.GetComponent<MeshRenderer>() : null;
            if (display != null)
            {
                displayMaterial = new Material(display.sharedMaterial);
                display.sharedMaterial = displayMaterial;
                display.enabled = false;
            }
            InvokeRepeating(nameof(Refresh), Random.Range(0f, 0.5f), 1f);
        }

        private void OnDestroy()
        {
            BlazonTextureCache.Release(cacheKey);
            cacheKey = null;
            if (displayMaterial != null) Destroy(displayMaterial);
        }

        public string GetDisplayText()
        {
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo?.GetString(HeraldicBanner.BlazonZdoKey, "") ?? "";
        }

        /// <summary>Shows these arms on the table's shield (if the player may change this table: ward access).</summary>
        public void SetDisplayText(string text)
        {
            if (nview == null || !nview.IsValid()) return;
            if (!PrivateArea.CheckAccess(transform.position, 0f, false, false)) return;
            if (!nview.IsOwner()) nview.ClaimOwnership();
            nview.GetZDO().Set(HeraldicBanner.BlazonZdoKey, text ?? "");
            Refresh();
        }

        private void Refresh()
        {
            if (display == null) return;
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null) return;
            if (zdo.DataRevision == lastRevision && currentText != null) return;
            lastRevision = zdo.DataRevision;

            string text = BlazonTextureCache.Normalise(zdo.GetString(HeraldicBanner.BlazonZdoKey, ""));
            if (text == currentText) return;
            currentText = text;
            if (text.Length == 0)
            {
                BlazonTextureCache.Release(cacheKey);
                cacheKey = null;
                display.enabled = false; // the plain wooden shield
                return;
            }
            int size = Mathf.Min(512, HeraldryConfig.EffectiveBannerResolution());
            var shape = new BlazonTextureCache.Shape
            {
                Id = ShapeId, UV = m_displayUV, Triangles = m_displayTriangles,
                Arrangement = Heraldry.FieldRenderer.Arrangement.Compact,
            };
            var tex = BlazonTextureCache.Acquire(text, size, size, shape, out string newKey);
            BlazonTextureCache.Release(cacheKey);
            cacheKey = newKey;
            displayMaterial.mainTexture = tex;
            display.enabled = true;
            Jotunn.Logger.LogDebug($"[Heraldry] Table at {transform.position:F1} shows \"{text}\" ({tex.width}x{tex.height}, {displayMaterial.shader.name}).");
        }

        /// <summary>Shift+E: paint one of the player's shields here.</summary>
        public void OpenPaint(Player player)
        {
            BlazonEditor.Open(new TableTarget(this, player));
        }
    }

    /// <summary>The blazon editor's view of the herald's table: choose a shield, paint it.</summary>
    internal sealed class TableTarget : IBlazonTarget
    {
        private const float MaxDistance = 5f;
        private readonly HeraldTable table;
        private readonly Player player;
        private readonly List<ItemDrop.ItemData> shields; // shields and tabards: everything paintable
        private int chosen;

        public TableTarget(HeraldTable table, Player player)
        {
            this.table = table;
            this.player = player;
            // Equipped things first, then the rest in inventory order
            shields = player.GetInventory().GetAllItemsInGridOrder()
                .Where(i => (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield && !HeraldryConfig.IsShieldExcluded(i.m_dropPrefab?.name))
                            || Tabards.TabardItem.IsTabard(i))
                .OrderByDescending(i => i.m_equipped)
                .ToList();
        }

        public string Title => "Herald's table";
        public string ApplyLabel => "Paint";
        public bool IsAlive => table != null && player != null &&
                               Vector3.Distance(player.transform.position, table.transform.position) < MaxDistance;
        // The preview takes the chosen shield's own face shape; the table's round shield if it can't be worked out
        // (a tabard: its front panel)
        private bool IsTabard => shields.Count > 0 && Tabards.TabardItem.IsTabard(shields[chosen]);
        private Tabards.TabardMesh.Panel Panel => IsTabard ? Tabards.TabardMesh.Front : null;
        private Shields.ShieldFace Face => shields.Count == 0 || IsTabard ? null : Shields.ShieldPatches.FaceForItem(shields[chosen]);
        public float Aspect => Panel?.Aspect ?? Face?.Aspect ?? 1f;
        public float[] OutlineUV => Panel?.OutlineUV ?? Face?.OutlineUV ?? table.m_displayUV;
        public int[] OutlineTriangles => Panel?.OutlineTriangles ?? Face?.OutlineTriangles ?? table.m_displayTriangles;
        public float HiddenTop => 0f;
        public Heraldry.FieldRenderer.Arrangement Arrangement =>
            IsTabard ? Heraldry.FieldRenderer.Arrangement.Chest : Heraldry.FieldRenderer.Arrangement.Compact;

        public IList<string> Choices => shields.Select(Label).ToList();
        public IList<Sprite> ChoiceIcons => shields.Select(i => i.GetIcon()).ToList();
        public int Chosen => shields.Count == 0 ? -1 : chosen;
        public string Blocked => shields.Count == 0 ? "You have no shields or tabards to paint." : null;

        public string Choose(int index)
        {
            if (shields.Count == 0) return "";
            chosen = Mathf.Clamp(index, 0, shields.Count - 1);
            return GetText();
        }

        public bool CostsPaint => HeraldryConfig.PaintCosts && shields.Count > 0;

        public string GetText()
        {
            if (shields.Count == 0) return table.GetDisplayText();
            var data = shields[chosen].m_customData;
            return data != null && data.TryGetValue(HeraldTable.ItemBlazonKey, out var text) ? text : "";
        }

        public string Apply(string text)
        {
            if (shields.Count == 0) return null;
            var item = shields[chosen];
            if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
            if (text.Length == 0) item.m_customData.Remove(HeraldTable.ItemBlazonKey);
            else item.m_customData[HeraldTable.ItemBlazonKey] = text;
            if (text.Length > 0) table.SetDisplayText(text);
            Shields.ShieldPatches.RefreshEquipment(player); // so a shield in hand or on the back shows it at once
            if (Tabards.TabardItem.IsTabard(item)) player.GetComponent<Tabards.HeraldicTabardWearer>()?.Publish();
            return text.Length == 0 ? $"Paint removed from {Name(item)}." : $"Painted {Name(item)}.";
        }

        private static string Name(ItemDrop.ItemData item) => Localization.instance.Localize(item.m_shared.m_name);

        private static string Label(ItemDrop.ItemData item)
        {
            string label = Name(item);
            if (item.m_quality > 1) label += " ★" + item.m_quality;
            if (item.m_equipped) label += "  <color=#a0a0a0>(equipped)</color>";
            return label;
        }
    }
}
