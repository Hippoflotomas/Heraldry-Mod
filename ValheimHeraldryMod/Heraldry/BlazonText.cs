using System;
using System.Collections.Generic;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     A stored blazon is the text players see, optionally followed by a hidden tail naming the exact picture
    ///     of each [bracketed] charge, in order (2026-10-14: two players' [wolf] can be different pictures):
    ///     <c>Azure, a [wolf] argent␟1a2b3c4d5e6f7a8b</c>. The separator is the ASCII unit separator, which can't be
    ///     typed; the tail is a comma list of 16-hex fingerprints, empty where a charge has none. Text with no tail
    ///     is an ordinary blazon, so everything stored before this still works (charges found by name).
    /// </summary>
    public static class BlazonText
    {
        public const char Separator = '\u001F';

        /// <summary>The part players see and write.</summary>
        public static string Visible(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return stored ?? "";
            int i = stored.IndexOf(Separator);
            return i < 0 ? stored : stored.Substring(0, i);
        }

        /// <summary>The fingerprint of each bracketed charge, in order ("" where none); empty if no tail.</summary>
        public static string[] Hashes(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return new string[0];
            int i = stored.IndexOf(Separator);
            if (i < 0) return new string[0];
            var parts = stored.Substring(i + 1).Split(',');
            var result = new string[Math.Min(parts.Length, 200)];
            for (int k = 0; k < result.Length; k++)
            {
                string h = parts[k].Trim().ToLowerInvariant();
                result[k] = IsFingerprint(h) ? h : "";
            }
            return result;
        }

        /// <summary>Visible text plus a tail; no tail at all when there are no fingerprints.</summary>
        public static string Join(string visible, IList<string> hashes)
        {
            visible = Visible(visible ?? "");
            bool any = false;
            if (hashes != null) foreach (var h in hashes) if (!string.IsNullOrEmpty(h)) any = true;
            if (!any) return visible;
            return visible + Separator + string.Join(",", hashes);
        }

        public static bool IsFingerprint(string h)
        {
            if (h == null || h.Length != ChargeBundle.FingerprintLength) return false;
            foreach (char c in h)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
