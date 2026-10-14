using System;
using System.Collections.Generic;
using System.Text;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Secret blazons (Hippo, 2026-10-13): text that isn't blazon at all, matched as a whole (case ignored),
    ///     standing for a fixed design. The code is what's stored and shown; it's expanded into an ordinary blazon
    ///     only to draw it. Adding one = adding a row. Kept out of the guide on purpose. The server can turn them
    ///     off (AllowSecretBlazons).
    /// </summary>
    public static class SecretCodes
    {
        private sealed class Code
        {
            public string Blazon;
            public string Note;
        }

        private static readonly Dictionary<string, Code> Codes = new Dictionary<string, Code>(StringComparer.OrdinalIgnoreCase)
        {
            { "IRONGATE!", new Code { Blazon = "Sable, a bordure or, a [logo:irongate] or",
                                      Note = "The gate of the makers. Mind your manners in their hall." } },
            { "VVVVV", new Code { Blazon = "Sable, a bordure gules, a [logo:valheim] proper",
                                  Note = "Five Vs, and the name of the land itself." } },
            { "HIPPOTECH", new Code { Blazon = "Quarterly azure and sable, in the first and fourth quarters a [hippoflotomas] proper",
                                      Note = "A hippoflotomas! The rarest beast in all the ten worlds." } },
            { "UGLYASSIN", new Code { Blazon = "Quarterly magenta and sable, a bordure sable",
                                      Note = "Ugly as sin, and proud of it." } },
            { "TESTSTANDARD1", new Code { Blazon = "Sanguine, a cross or inscribed \"TEST\" in runes sable",
                                          Note = "A test standard. This is only a test." } },
            { "UGLIERTHANSIN", new Code { Blazon = "Chequy magenta and sable, a bordure sable",
                                          Note = "Uglier than sin. Your eyes will never forgive you." } },
        };

        /// <summary>The blazon a code stands for, and its playful note; false if the text isn't a code.</summary>
        public static bool TryExpand(string text, out string blazon, out string note)
        {
            blazon = note = null;
            if (string.IsNullOrWhiteSpace(text) || !Codes.TryGetValue(text.Trim(), out var c)) return false;
            blazon = c.Blazon;
            note = c.Note;
            return true;
        }
    }

    /// <summary>Latin letters to Elder Futhark runes ("in runes"), as the game's Norse font draws them.</summary>
    public static class RuneText
    {
        private static readonly Dictionary<char, string> Map = new Dictionary<char, string>
        {
            { 'a', "ᚨ" }, { 'b', "ᛒ" }, { 'c', "ᚲ" }, { 'd', "ᛞ" }, { 'e', "ᛖ" },
            { 'f', "ᚠ" }, { 'g', "ᚷ" }, { 'h', "ᚺ" }, { 'i', "ᛁ" }, { 'j', "ᛃ" },
            { 'k', "ᚲ" }, { 'l', "ᛚ" }, { 'm', "ᛗ" }, { 'n', "ᚾ" }, { 'o', "ᛟ" },
            { 'p', "ᛈ" }, { 'q', "ᚲ" }, { 'r', "ᚱ" }, { 's', "ᛋ" }, { 't', "ᛏ" },
            { 'u', "ᚢ" }, { 'v', "ᚹ" }, { 'w', "ᚹ" }, { 'x', "ᚲᛋ" }, { 'y', "ᛃ" },
            { 'z', "ᛉ" },
        };

        /// <summary>"TEST" → ᛏᛖᛋᛏ. "th" → ᚦ, "ng" → ᛜ; spaces become word dividers (᛫); other characters stay.</summary>
        public static string FromLatin(string text)
        {
            var sb = new StringBuilder();
            string s = (text ?? "").ToLowerInvariant();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == 't' && i + 1 < s.Length && s[i + 1] == 'h') { sb.Append('ᚦ'); i++; continue; }
                if (c == 'n' && i + 1 < s.Length && s[i + 1] == 'g') { sb.Append('ᛜ'); i++; continue; }
                if (c == ' ') { sb.Append(" ᛫ "); continue; }
                if (Map.TryGetValue(c, out var r)) sb.Append(r);
                else sb.Append(text[i]);
            }
            return sb.ToString();
        }
    }
}
