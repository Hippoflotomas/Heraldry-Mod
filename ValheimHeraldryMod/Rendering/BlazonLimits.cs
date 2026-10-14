using ValheimHeraldryMod.Heraldry;

namespace ValheimHeraldryMod.Rendering
{
    /// <summary>
    ///     The server's blazon limits (length, elements; Hippo 2026-10-13: 500 / 25 by default, server-synced).
    ///     The editor refuses over-limit arms, and since a blazon is written by whichever client owns the object,
    ///     every client checks again before drawing: over-limit arms are drawn as the placeholder roundel instead
    ///     of a slow render.
    /// </summary>
    internal static class BlazonLimits
    {
        /// <summary>What's drawn instead of over-limit arms: a lion has no art yet, so this is the placeholder roundel.</summary>
        public const string StandIn = "argent, a lion";

        /// <summary>Null if the arms are within the limits, otherwise why not.</summary>
        public static string Problem(string text, Blazon parsed = null)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int visibleLength = BlazonText.Visible(text).Length; // the hidden fingerprints don't count
            int maxLength = HeraldryConfig.MaxBlazonLength, maxElements = HeraldryConfig.MaxBlazonElements;
            if (visibleLength > maxLength)
                return $"too long for this server: {visibleLength} characters, at most {maxLength}";
            int elements = (parsed ?? BlazonParser.Parse(text)).Elements;
            if (elements > maxElements)
                return $"too complex for this server: {elements} things to draw, at most {maxElements}";
            return null;
        }

        /// <summary>The text to draw: the arms themselves, or the stand-in if they break the limits.</summary>
        public static string Drawable(string normalisedText) =>
            Problem(normalisedText) == null ? normalisedText : StandIn;
    }
}
