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

        /// <summary>Things to choose between (shields in the inventory); null = nothing to choose.</summary>
        IList<string> Choices { get; }
        /// <summary>An icon per choice (item icons), or null.</summary>
        IList<UnityEngine.Sprite> ChoiceIcons { get; }
        int Chosen { get; }
        /// <summary>Picks one; returns its current blazon for the text box.</summary>
        string Choose(int index);
        /// <summary>Why painting can't happen right now (no shields...), or null.</summary>
        string Blocked { get; }

        /// <summary>Changing the arms here costs paint (when paint costs are on at all).</summary>
        bool CostsPaint { get; }

        string GetText();
        /// <summary>Applies the text; returns a message for the status line, or null.</summary>
        string Apply(string text);
    }

    /// <summary>A heraldic banner: edited in place, the blazon in its ZDO.</summary>
    internal sealed class BannerTarget : IBlazonTarget
    {
        private readonly HeraldicBanner banner;
        public BannerTarget(HeraldicBanner banner) { this.banner = banner; }

        public string Title => "Heraldic banner";
        public string ApplyLabel => "Apply";
        public bool IsAlive => banner != null;
        public float Aspect => banner.m_widthOverHeight;
        public float[] OutlineUV => banner.m_outlineUV;
        public int[] OutlineTriangles => banner.m_outlineTriangles;
        public float HiddenTop => banner.m_hiddenTop;
        public Heraldry.FieldRenderer.Arrangement Arrangement => Heraldry.FieldRenderer.Arrangement.Spread;
        public IList<string> Choices => null;
        public IList<UnityEngine.Sprite> ChoiceIcons => null;
        public int Chosen => -1;
        public string Choose(int index) => GetText();
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
