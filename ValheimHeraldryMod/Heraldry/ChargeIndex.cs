using System;
using System.Collections.Generic;
using System.Linq;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>Where a picture charge came from. Decides who wins when two have the same name.</summary>
    public enum ChargeSource
    {
        /// <summary>The server's own charges (its charge folder), listed when you join.</summary>
        Server,
        /// <summary>Your own charges (your charge folder).</summary>
        Own,
        /// <summary>Charges you downloaded from other players.</summary>
        Downloaded,
        /// <summary>Built into the mod (everyone has them): [hippoflotomas].</summary>
        BuiltIn,
    }

    /// <summary>
    ///     Picture charges by name, for the parser: <c>[wolf]</c> → a fingerprint. Several charges can share a
    ///     name; the server's win over your own, yours over downloaded ones (2026-10-13: server wins by default,
    ///     configurable with <see cref="OwnFirst"/>). Within one source, the first pack with the name wins
    ///     (<c>[pack:name]</c> picks another), and the drawing whose attitudes match best is used. Pure C#.
    /// </summary>
    public sealed class ChargeIndex
    {
        public sealed class Entry
        {
            public string Fingerprint;
            public string Pack;
            public string[] Names;      // folded; first = the main name
            public string[] Attitudes;  // folded
            public ChargeSource Source;
            public long Size;
            public int Order;           // load order, for "first pack wins"
        }

        private readonly Dictionary<string, Entry> byFingerprint = new Dictionary<string, Entry>();
        private int order;

        /// <summary>Your own charges win over the server's of the same name (off by default).</summary>
        public bool OwnFirst;

        public IEnumerable<Entry> All => byFingerprint.Values;
        public int Count => byFingerprint.Count;

        public Entry Get(string fingerprint) =>
            fingerprint != null && byFingerprint.TryGetValue(fingerprint, out var e) ? e : null;

        /// <summary>Adds (or re-sources) a charge. A charge known from several sources keeps the strongest.</summary>
        public void Add(string fingerprint, string pack, string[] names, string[] attitudes, ChargeSource source, long size)
        {
            if (byFingerprint.TryGetValue(fingerprint, out var old) && Rank(old.Source) <= Rank(source)) return;
            byFingerprint[fingerprint] = new Entry
            {
                Fingerprint = fingerprint,
                Pack = PackReader.PackName(pack),
                Names = (names ?? new string[0]).Select(Fold).Where(n => n.Length > 0).Distinct().ToArray(),
                Attitudes = (attitudes ?? new string[0]).Select(Fold).Where(n => n.Length > 0).ToArray(),
                Source = source,
                Size = size,
                Order = old?.Order ?? order++,
            };
        }

        public void Remove(string fingerprint) => byFingerprint.Remove(fingerprint);

        public void RemoveSource(ChargeSource source)
        {
            foreach (var k in byFingerprint.Where(kv => kv.Value.Source == source).Select(kv => kv.Key).ToList())
                byFingerprint.Remove(k);
        }

        private int Rank(ChargeSource s) =>
            s == ChargeSource.BuiltIn ? -1 : s == ChargeSource.Downloaded ? 2 : OwnFirst ? (s == ChargeSource.Own ? 0 : 1) : (s == ChargeSource.Server ? 0 : 1);

        /// <summary>The fingerprint for "[name]" with these attitudes, or null. Name already folded.</summary>
        public string Resolve(string name, IList<string> attitudes, out string unmatched)
        {
            unmatched = null;
            int colon = name.IndexOf(':');
            string pack = colon > 0 ? name.Substring(0, colon) : null;
            string bare = colon > 0 ? name.Substring(colon + 1) : name;
            var candidates = byFingerprint.Values
                .Where(e => e.Names.Contains(bare) && (pack == null || e.Pack == pack))
                .ToList();
            if (candidates.Count == 0) return null;
            // Strongest source, then the first pack that has the name
            int bestRank = candidates.Min(e => Rank(e.Source));
            candidates = candidates.Where(e => Rank(e.Source) == bestRank).ToList();
            string firstPack = candidates.OrderBy(e => e.Order).First().Pack;
            candidates = candidates.Where(e => e.Pack == firstPack).ToList();

            var wanted = attitudes.Where(a => !Lexicon.IsFlip(a)).ToList();
            Entry best = null;
            int bestScore = int.MinValue;
            foreach (var e in candidates.OrderBy(e => e.Order))
            {
                int matches = wanted.Count(a => e.Attitudes.Contains(a));
                int score = matches * 100 - e.Attitudes.Length; // most matches, then the plainest drawing
                if (score > bestScore) { bestScore = score; best = e; }
            }
            unmatched = wanted.FirstOrDefault(a => !best.Attitudes.Contains(a) && Lexicon.Attitudes.Contains(a));
            return best.Fingerprint;
        }

        /// <summary>Every charge name, sorted (basic mode's list).</summary>
        public List<string> Names()
        {
            var names = byFingerprint.Values.SelectMany(e => e.Names).Distinct().ToList();
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>Charges by name or fingerprint ("wolf", "pack:wolf", "1a2b3c4d5e6f7a8b").</summary>
        public List<Entry> Find(string nameOrFingerprint)
        {
            string q = Fold(nameOrFingerprint ?? "").Trim('[', ']');
            if (byFingerprint.TryGetValue(q, out var e)) return new List<Entry> { e };
            int colon = q.IndexOf(':');
            string pack = colon > 0 ? q.Substring(0, colon) : null;
            string bare = colon > 0 ? q.Substring(colon + 1) : q;
            return byFingerprint.Values.Where(x => x.Names.Contains(bare) && (pack == null || x.Pack == pack)).OrderBy(x => x.Order).ToList();
        }

        public string Suggest(string name)
        {
            string best = null;
            int bestD = Math.Max(1, name.Length / 4) + 1;
            foreach (var n in byFingerprint.Values.SelectMany(e => e.Names).Distinct())
            {
                if (Math.Abs(n.Length - name.Length) >= bestD) continue;
                int d = Levenshtein(name, n);
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }

        /// <summary>Same folding as the parser: lower case, accents off, single spaces.</summary>
        public static string Fold(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char ch in s.Trim().ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD))
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            return string.Join(" ", sb.ToString().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static int Levenshtein(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
