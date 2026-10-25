using System.Collections.Generic;
using ValheimHeraldryMod.Banners;

namespace ValheimHeraldryMod.UI
{
    /// <summary>What the blazon editor is painting: a banner, the herald's table (a shield), later a tabard.</summary>
    internal interface IBlazonTarget
    {
        string Title { get; }
        string ApplyLabel { get; }
        /// <summary>False once the thing being edited is gone (destroyed, unloaded, out of reach).</summary>
        bool IsAlive { get; }
        /// <summary>Preview shape: width over height, outline triangles in 0..1 (V up), hidden top fraction.</summary>
        float Aspect { get; }
        float[] OutlineUV { get; }
        int[] OutlineTriangles { get; }
        float HiddenTop { get; }
        /// <summary>How plain charges use the surface (banners Spread, shields Compact).</summary>
        Heraldry.FieldRenderer.Arrangement Arrangement { get; }
        /// <summary>The field's centre as fractions from the top left; -1 = the middle.</summary>
        float CentreU { get; }
        float CentreV { get; }

        /// <summary>Things to choose between (shields in the inventory); null = nothing to choose.</summary>
        IList<string> Choices { get; }
        /// <summary>An icon per choice (item icons), or null.</summary>
        IList<UnityEngine.Sprite> ChoiceIcons { get; }
        int Chosen { get; }
        /// <summary>Picks one; returns its current blazon for the text box.</summary>
        string Choose(int index);
        /// <summary>Why painting can't happen right now (no shields...), or null.</summary>
        string Blocked { get; }

        /// <summary>
        ///     The pattern numbers (1–4) this thing has (tagged faces, 2026-10-16); null or one = nothing to choose,
        ///     and the editor greys its pattern picker out.
        /// </summary>
        IList<int> Patterns { get; }
        /// <summary>The pattern being edited; GetText, Apply and the preview shape are about this one.</summary>
        int Pattern { get; }
        /// <summary>Switches to another pattern; returns its current blazon for the text box.</summary>
        string ChoosePattern(int number);

        /// <summary>Changing the arms here costs paint (when paint costs are on at all).</summary>
        bool CostsPaint { get; }

        string GetText();
        /// <summary>Applies the text; returns a message for the status line, or null.</summary>
        string Apply(string text);
    }

    /// <summary>
    ///     Arms that aren't on anything yet (HeraldryAPI.OpenEditorForText, 2026-10-25): another mod opens the editor,
    ///     the player writes arms, and Apply hands them back. Nothing is painted, so nothing costs paint.
    /// </summary>
    internal sealed class TextTarget : IBlazonTarget
    {
        private readonly string title, text;
        private readonly float aspect;
        private readonly System.Action<string> onApply;

        public TextTarget(string title, string text, float aspect, System.Action<string> onApply)
        {
            this.title = string.IsNullOrWhiteSpace(title) ? "Arms" : title;
            this.text = text ?? "";
            this.aspect = aspect > 0f ? aspect : 0.83f;
            this.onApply = onApply;
        }

        public string Title => title;
        public string ApplyLabel => "Apply";
        public bool IsAlive => true;
        public float Aspect => aspect;
        public float[] OutlineUV => null; // the whole rectangle
        public int[] OutlineTriangles => null;
        public float HiddenTop => 0f;
        public Heraldry.FieldRenderer.Arrangement Arrangement =>
            aspect < 0.75f ? Heraldry.FieldRenderer.Arrangement.Spread : Heraldry.FieldRenderer.Arrangement.Compact;
        public float CentreU => -1f;
        public float CentreV => -1f;
        public IList<string> Choices => null;
        public IList<UnityEngine.Sprite> ChoiceIcons => null;
        public int Chosen => -1;
        public string Choose(int index) => GetText();
        public IList<int> Patterns => null;
        public int Pattern => 1;
        public string ChoosePattern(int number) => GetText();
        public string Blocked => null;
        public bool CostsPaint => false;
        public string GetText() => text;

        public string Apply(string newText)
        {
            try { onApply?.Invoke(newText ?? ""); }
            catch (System.Exception ex) { Jotunn.Logger.LogWarning($"[Heraldry API] The mod that opened the editor failed on Apply: {ex.Message}"); }
            return null;
        }
    }

    /// <summary>A heraldic banner: edited in place, the blazon in its ZDO.</summary>
    internal sealed class BannerTarget : IBlazonTarget
    {
        private readonly HeraldicBanner banner;
        public BannerTarget(HeraldicBanner banner) { this.banner = banner; }

        public string Title => banner.m_displayName;
        public string ApplyLabel => "Apply";
        public bool IsAlive => banner != null;
        public float Aspect => banner.m_widthOverHeight;
        public float[] OutlineUV => banner.m_outlineUV;
        public int[] OutlineTriangles => banner.m_outlineTriangles;
        public float HiddenTop => banner.m_hiddenTop;
        public Heraldry.FieldRenderer.Arrangement Arrangement => banner.Arrangement;
        public float CentreU => -1f;
        public float CentreV => -1f;
        public IList<string> Choices => null;
        public IList<UnityEngine.Sprite> ChoiceIcons => null;
        public int Chosen => -1;
        public string Choose(int index) => GetText();
        public IList<int> Patterns => null;
        public int Pattern => 1;
        public string ChoosePattern(int number) => GetText();
        public string Blocked => null;
        public bool CostsPaint => HeraldryConfig.PaintCostsOnBanners;
        public string GetText() => banner.GetText();

        public string Apply(string text)
        {
            banner.SetText(text);
            return null;
        }
    }
}
