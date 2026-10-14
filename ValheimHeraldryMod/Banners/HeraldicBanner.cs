using System.Collections.Generic;
using UnityEngine;
using ValheimHeraldryMod.Rendering;

namespace ValheimHeraldryMod.Banners
{
    /// <summary>
    ///     Sits on every heraldic banner. The only per-banner state is the blazon text in the ZDO; each
    ///     client renders it locally, so nothing image-like goes over the network.
    ///     Editing follows ward access exactly like vanilla signs (no ward = anyone can edit).
    /// </summary>
    public class HeraldicBanner : MonoBehaviour, Hoverable, Interactable, TextReceiver
    {
        /// <summary>ZDO key holding the raw blazon text.</summary>
        public const string BlazonZdoKey = "Heraldry_Blazon";
        public const int MaxBlazonLength = 300;

        // Set on the prefab by BannerRegistry; public fields are copied onto every instance.
        public string m_displayName = "Heraldic banner";
        public float m_widthOverHeight = 0.5f;
        /// <summary>Which banner shape this is (prefab name); textures are cached per shape because of the outline.</summary>
        public string m_shapeId = "";
        /// <summary>Cloth mesh triangles in texture space (x,y pairs, V up), for the bordure's outline.</summary>
        public float[] m_outlineUV;
        public int[] m_outlineTriangles;
        /// <summary>Fraction of the cloth's height, from the top, hidden by the beam it hangs from.</summary>
        public float m_hiddenTop;

        private ZNetView nview;
        private readonly List<Material> materials = new List<Material>();
        private uint lastRevision = uint.MaxValue;
        private string currentText;   // normalised; null = nothing rendered yet
        private string cacheKey;
        private string hoverProblemFor;   // text the cached problem belongs to
        private string hoverProblem;

        private void Start()
        {
            nview = GetComponent<ZNetView>();

            // One material copy per cloth material on this banner, so its texture doesn't leak onto others.
            foreach (var renderer in BannerRegistry.ClothRenderers(gameObject))
            {
                var shared = renderer.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (!BannerRegistry.IsCloth(shared[i]))
                        continue;
                    var copy = new Material(shared[i]) { name = shared[i].name + "_heraldry" };
                    copy.mainTextureScale = Vector2.one;
                    copy.mainTextureOffset = Vector2.zero;
                    StripVanillaArt(copy);
                    shared[i] = copy;
                    materials.Add(copy);
                }
                renderer.sharedMaterials = shared;
            }

            Refresh();
            // Same idea as vanilla Sign: poll the ZDO's data revision and only redo work when it moved.
            if (nview != null && nview.IsValid())
                InvokeRepeating(nameof(Refresh), 1f, 1f);
        }

        /// <summary>
        ///     The vanilla banner's emblem also lives in its normal map (and possibly the metal map), so it showed
        ///     through as relief under our colour. Swap in the plain twill-weave normal from BannerShare; if that
        ///     can't load, clear the slot so Custom/Vegetation falls back to its declared default ("bump" = flat).
        ///     _MetalTex cleared falls back to "black" (no metal).
        /// </summary>
        private static void StripVanillaArt(Material m)
        {
            if (m.HasProperty("_BumpMap"))
                m.SetTexture("_BumpMap", ClothNormal.Get());
            if (m.HasProperty("_MetalTex"))
                m.SetTexture("_MetalTex", null);
        }

        private void OnDestroy()
        {
            BlazonTextureCache.Release(cacheKey);
            cacheKey = null;
            foreach (var m in materials)
                if (m != null)
                    Destroy(m);
            materials.Clear();
        }

        /// <summary>Re-renders if the stored text (or the effective resolution) changed.</summary>
        private void Refresh()
        {
            var zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo != null)
            {
                if (zdo.DataRevision == lastRevision && currentText != null)
                    return;
                lastRevision = zdo.DataRevision;
            }

            string text = BlazonTextureCache.Normalise(zdo?.GetString(BlazonZdoKey, "") ?? "");
            int height = HeraldryConfig.EffectiveBannerResolution();
            int width = Mathf.Max(16, Mathf.RoundToInt(height * m_widthOverHeight));

            if (BlazonTextureCache.KeyFor(m_shapeId, text, width, height) == cacheKey)
                return;

            var shape = new BlazonTextureCache.Shape { Id = m_shapeId, UV = m_outlineUV, Triangles = m_outlineTriangles, HiddenTop = m_hiddenTop };
            var tex = BlazonTextureCache.Acquire(text, width, height, shape, out string newKey);
            BlazonTextureCache.Release(cacheKey);   // after Acquire, so an unchanged texture isn't destroyed and rebuilt
            cacheKey = newKey;
            currentText = text;
            foreach (var m in materials)
                m.mainTexture = tex;
        }

        // ------------------------------------------------------------ Hoverable / Interactable

        public string GetHoverText()
        {
            if (nview == null || !nview.IsValid())
                return "";
            string text = GetText();
            string shown = string.IsNullOrEmpty(text) ? "<i>unpainted</i>" : $"\"{text}\"";
            // Until the editing window exists, show the first problem the parser found (cached per text).
            if (text != hoverProblemFor)
            {
                hoverProblemFor = text;
                hoverProblem = string.IsNullOrEmpty(text) ? null : Heraldry.BlazonParser.FirstProblem(Heraldry.BlazonParser.Parse(text));
            }
            if (hoverProblem != null)
                shown += $"\n<color=#ff7060>{hoverProblem}</color>";
            if (!PrivateArea.CheckAccess(transform.position, 0f, false, false))
                return Localization.instance.Localize($"{m_displayName}\n{shown}\n$piece_noaccess");
            return Localization.instance.Localize($"{m_displayName}\n{shown}\n[<color=yellow><b>$KEY_Use</b></color>] Write blazon");
        }

        public string GetHoverName() => m_displayName;

        public float GetHoverOffset() => 0f;

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold)
                return false;
            if (!PrivateArea.CheckAccess(transform.position, 0f, true, false))
                return true;
            UI.BlazonEditor.Open(this);
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // ------------------------------------------------------------ TextReceiver

        public string GetText() =>
            nview != null && nview.IsValid() ? nview.GetZDO().GetString(BlazonZdoKey, "") : "";

        public void SetText(string text)
        {
            if (nview == null || !nview.IsValid())
                return;
            if (!PrivateArea.CheckAccess(transform.position, 0f, true, false))
                return;
            if (text != null && text.Length > MaxBlazonLength)
                text = text.Substring(0, MaxBlazonLength);
            nview.ClaimOwnership();
            nview.GetZDO().Set(BlazonZdoKey, text ?? "");
            Refresh();
        }
    }
}
