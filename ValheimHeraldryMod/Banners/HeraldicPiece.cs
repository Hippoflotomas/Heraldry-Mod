using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using ValheimHeraldryMod.Rendering;
using ValheimHeraldryMod.Shields;

namespace ValheimHeraldryMod.Banners
{
    /// <summary>
    ///     A build piece from another mod with a tagged face (ValheimHeraldryMod_Face): edited in place like our
    ///     banners (ward access, blazon in the piece's ZDO, paint costs as banners). Added at runtime to any piece
    ///     whose model carries the tag. E edits it when the piece has nothing else to do; when it has (a chest, a
    ///     door), the alternative key does (Shift+E by default, configurable; Hippo 2026-10-14).
    /// </summary>
    public class HeraldicPiece : MonoBehaviour, Hoverable, Interactable, TextReceiver
    {
        private ZNetView nview;
        private TaggedFacePainter painter;
        private uint lastRevision = uint.MaxValue;
        private int lastGeneration = -1;
        private string currentKey;
        private string cacheKey;

        internal TaggedFace Face => painter?.Face;

        private void Start()
        {
            nview = GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) { enabled = false; return; } // the placement ghost
            painter = TaggedFacePainter.On(gameObject);
            if (painter == null) { enabled = false; return; }
            Refresh();
            InvokeRepeating(nameof(Refresh), 1f, 1f);
        }

        private void OnDestroy()
        {
            painter?.Destroy();
            BlazonTextureCache.Release(cacheKey);
            cacheKey = null;
        }

        private void Refresh()
        {
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null || painter == null) return;
            if (zdo.DataRevision == lastRevision && lastGeneration == ChargeLibrary.Generation && currentKey != null) return;
            lastRevision = zdo.DataRevision;
            lastGeneration = ChargeLibrary.Generation;

            string text = BlazonTextureCache.Normalise(zdo.GetString(HeraldicBanner.BlazonZdoKey, ""));
            string key = text.IndexOf('[') >= 0 ? text + "|" + ChargeLibrary.Generation : text;
            if (key == currentKey) return;
            currentKey = key;

            if (text.Length == 0)
            {
                painter.Hide();
                BlazonTextureCache.Release(cacheKey);
                cacheKey = null;
                return;
            }
            var f = painter.Face;
            int longest = HeraldryConfig.EffectiveBannerResolution();
            int w = f.Aspect >= 1f ? longest : Mathf.Max(32, Mathf.RoundToInt(longest * f.Aspect));
            int h = f.Aspect >= 1f ? Mathf.Max(32, Mathf.RoundToInt(longest / f.Aspect)) : longest;
            var tex = BlazonTextureCache.Acquire(text, w, h, Shape(f), out string newKey);
            BlazonTextureCache.Release(cacheKey);
            cacheKey = newKey;
            painter.Show(tex);
        }

        internal static BlazonTextureCache.Shape Shape(TaggedFace f) => new BlazonTextureCache.Shape
        {
            Id = "Heraldry_TaggedPiece_" + f.Key, UV = f.OutlineUV, Triangles = f.OutlineTriangles,
            Arrangement = Arrangement(f), CentreU = f.CentreU, CentreV = f.CentreV,
        };

        /// <summary>Tall faces spread their charges down the cloth like banners; others keep them together like shields.</summary>
        internal static Heraldry.FieldRenderer.Arrangement Arrangement(TaggedFace f) =>
            f.Aspect < 0.75f ? Heraldry.FieldRenderer.Arrangement.Spread : Heraldry.FieldRenderer.Arrangement.Compact;

        private string DisplayName()
        {
            var piece = GetComponent<Piece>();
            return piece != null ? Localization.instance.Localize(piece.m_name) : "";
        }

        internal bool CanEdit(bool flash) => PrivateArea.CheckAccess(transform.position, 0f, flash, false);

        internal void OpenEditor()
        {
            if (!CanEdit(true)) return;
            UI.BlazonEditor.Open(new PieceTarget(this, DisplayName()));
        }

        // ------------------------------------------------------------ Hoverable / Interactable (when the piece has none of its own)

        public string GetHoverText()
        {
            if (nview == null || !nview.IsValid() || painter == null) return "";
            string text = GetText();
            string shown = string.IsNullOrEmpty(text) ? "<i>unpainted</i>" : $"\"{TextArt.ForDisplay(Heraldry.BlazonText.Visible(text))}\"";
            if (!CanEdit(false)) return Localization.instance.Localize($"{DisplayName()}\n{shown}\n$piece_noaccess");
            return Localization.instance.Localize($"{DisplayName()}\n{shown}\n[<color=yellow><b>$KEY_Use</b></color>] Write blazon");
        }

        public string GetHoverName() => DisplayName();

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || painter == null) return false;
            OpenEditor();
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // ------------------------------------------------------------ TextReceiver

        public string GetText() =>
            nview != null && nview.IsValid() ? nview.GetZDO().GetString(HeraldicBanner.BlazonZdoKey, "") : "";

        public void SetText(string text)
        {
            if (nview == null || !nview.IsValid() || !CanEdit(true)) return;
            if (text != null && text.Length > HeraldicBanner.MaxBlazonLength) text = text.Substring(0, HeraldicBanner.MaxBlazonLength);
            nview.ClaimOwnership();
            nview.GetZDO().Set(HeraldicBanner.BlazonZdoKey, text ?? "");
            Refresh();
        }

        /// <summary>True if the piece answers E itself (a chest, a door): then the alternative key edits the arms.</summary>
        internal bool HasOwnInteraction()
        {
            if (ownInteraction.HasValue) return ownInteraction.Value;
            bool own = false;
            foreach (var i in GetComponentsInParent<Interactable>(true))
                if (!ReferenceEquals(i, this)) own = true;
            foreach (var i in GetComponentsInChildren<Interactable>(true))
                if (!ReferenceEquals(i, this)) own = true;
            ownInteraction = own;
            return own;
        }

        private bool? ownInteraction;

        // ------------------------------------------------------------ the alternative key

        /// <summary>From the plugin's Update: the alternative edit key, when it isn't the game's own Shift+E.</summary>
        internal static void CheckAlternativeKey()
        {
            if (HeraldryConfig.EditKeyIsAltUse) return; // handled in the Player.Interact patch
            var player = Player.m_localPlayer;
            if (player == null || UI.BlazonEditor.IsOpen || global::Console.IsVisible() || Chat.instance?.HasFocus() == true ||
                InventoryGui.IsVisible() || Menu.IsVisible() || TextInput.IsVisible()) return;
            var mod = HeraldryConfig.EditModifier;
            if (mod != KeyCode.None && !ZInput.GetKey(mod, false)) return;
            if (!ZInput.GetKeyDown(HeraldryConfig.EditKey, false)) return;
            var piece = player.GetHoverObject()?.GetComponentInParent<HeraldicPiece>();
            if (piece != null && piece.painter != null && piece.HasOwnInteraction()) piece.OpenEditor();
        }

        internal string AlternativeKeyLabel =>
            HeraldryConfig.EditModifier == KeyCode.None ? HeraldryConfig.EditKey.ToString()
            : HeraldryConfig.EditModifier.ToString().Replace("Left", "").Replace("Right", "") + "+" + HeraldryConfig.EditKey;
    }

    /// <summary>The editor's view of a tagged piece.</summary>
    internal sealed class PieceTarget : UI.IBlazonTarget
    {
        private readonly HeraldicPiece piece;
        private readonly string name;
        public PieceTarget(HeraldicPiece piece, string name) { this.piece = piece; this.name = name; }

        public string Title => string.IsNullOrEmpty(name) ? "Arms" : name;
        public string ApplyLabel => "Apply";
        public bool IsAlive => piece != null;
        public float Aspect => piece.Face?.Aspect ?? 1f;
        public float[] OutlineUV => piece.Face?.OutlineUV;
        public int[] OutlineTriangles => piece.Face?.OutlineTriangles;
        public float HiddenTop => 0f;
        public Heraldry.FieldRenderer.Arrangement Arrangement =>
            piece.Face != null ? HeraldicPiece.Arrangement(piece.Face) : Heraldry.FieldRenderer.Arrangement.Compact;
        public float CentreU => piece.Face?.CentreU ?? -1f;
        public float CentreV => piece.Face?.CentreV ?? -1f;
        public IList<string> Choices => null;
        public IList<Sprite> ChoiceIcons => null;
        public int Chosen => -1;
        public string Choose(int index) => GetText();
        public string Blocked => null;
        public bool CostsPaint => HeraldryConfig.PaintCostsOnBanners;
        public string GetText() => piece.GetText();

        public string Apply(string text)
        {
            piece.SetText(text);
            return null;
        }
    }

    /// <summary>Finds tagged pieces as they're built or loaded, and the alternative edit key.</summary>
    [HarmonyPatch]
    internal static class HeraldicPiecePatches
    {
        private static readonly Dictionary<string, bool> Tagged = new Dictionary<string, bool>();

        [HarmonyPatch(typeof(Piece), "Awake")]
        [HarmonyPostfix]
        private static void Discover(Piece __instance)
        {
            var go = __instance.gameObject;
            if (go.GetComponent<HeraldicBanner>() != null || go.GetComponent<Station.HeraldTable>() != null) return; // ours
            string name = Utils.GetPrefabName(go);
            if (!Tagged.TryGetValue(name, out bool has))
            {
                has = TaggedFace.Find(go, out _, out _);
                Tagged[name] = has;
                if (has) Jotunn.Logger.LogInfo($"[Heraldry] Piece '{name}' has a tagged heraldry face: arms can be painted on it.");
            }
            if (has && go.GetComponent<HeraldicPiece>() == null) go.AddComponent<HeraldicPiece>();
        }

        /// <summary>Shift+E (the game's alternative use) on a tagged piece that answers E itself: edit the arms instead.</summary>
        [HarmonyPatch(typeof(Player), "Interact", typeof(GameObject), typeof(bool), typeof(bool))]
        [HarmonyPrefix]
        private static bool AltUse(GameObject __0, bool __1, bool __2) // (go, hold, alt), by position
        {
            GameObject go = __0;
            bool hold = __1, alt = __2;
            if (!alt || hold || go == null || !HeraldryConfig.EditKeyIsAltUse) return true;
            var piece = go.GetComponentInParent<HeraldicPiece>();
            if (piece == null || piece.Face == null || !piece.HasOwnInteraction()) return true;
            piece.OpenEditor();
            return false;
        }

        /// <summary>The edit hint under the piece's own hover text, when E belongs to the piece.</summary>
        [HarmonyPatch(typeof(Hud), "UpdateCrosshair")]
        [HarmonyPostfix]
        private static void Hint(Hud __instance, Player __0) // the hovering player, by position
        {
            var player = __0;
            var piece = player?.GetHoverObject()?.GetComponentInParent<HeraldicPiece>();
            if (piece == null || piece.Face == null || !piece.HasOwnInteraction()) return;
            var label = __instance.m_hoverName;
            if (label == null || string.IsNullOrEmpty(label.text) || label.text.Contains("Edit arms")) return;
            label.text += $"\n[<color=yellow><b>{piece.AlternativeKeyLabel}</b></color>] Edit arms";
        }
    }
}
