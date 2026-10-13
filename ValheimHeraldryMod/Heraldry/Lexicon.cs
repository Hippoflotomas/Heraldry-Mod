using System.Collections.Generic;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Every word the parser knows. Leniency is on words, never on meaning: plain-English aliases are
    ///     accepted as Passable with the correct term, ambiguous words are Wrong.
    /// </summary>
    internal static class Lexicon
    {
        public sealed class TinctureEntry
        {
            public string Canonical;
            public TinctureKind Kind;
            public string PassableNote; // null = correct blazon
        }

        public static readonly Dictionary<string, TinctureEntry> Tinctures = new Dictionary<string, TinctureEntry>();

        // Placeholder dyed-cloth palette (not final; choose from the swatch card).
        public static readonly Dictionary<string, Rgba> Palette = new Dictionary<string, Rgba>
        {
            { "or", new Rgba(204, 160, 52) },
            { "argent", new Rgba(232, 228, 214) },
            { "azure", new Rgba(36, 74, 140) },
            { "gules", new Rgba(166, 34, 38) },
            { "sable", new Rgba(30, 28, 28) },
            { "vert", new Rgba(34, 108, 56) },
            { "purpure", new Rgba(104, 46, 112) },
            { "tenne", new Rgba(178, 98, 34) },
            { "sanguine", new Rgba(118, 26, 26) },
            { "murrey", new Rgba(128, 34, 74) },
        };

        public static readonly Dictionary<string, PartitionKind> PartitionWords = new Dictionary<string, PartitionKind>
        {
            { "pale", PartitionKind.Pale }, { "fess", PartitionKind.Fess }, { "fesse", PartitionKind.Fess },
            { "bend", PartitionKind.Bend }, { "chevron", PartitionKind.Chevron }, { "saltire", PartitionKind.Saltire },
            { "cross", PartitionKind.Quarterly },
        };

        public sealed class VariationEntry
        {
            public VariationKind Kind;
            public int DefaultCount;
            public string PassableNote;
        }

        public static readonly Dictionary<string, VariationEntry> Variations = new Dictionary<string, VariationEntry>
        {
            { "paly", new VariationEntry { Kind = VariationKind.Paly, DefaultCount = 6 } },
            { "barry", new VariationEntry { Kind = VariationKind.Barry, DefaultCount = 6 } },
            { "bendy", new VariationEntry { Kind = VariationKind.Bendy, DefaultCount = 6 } },
            { "chequy", new VariationEntry { Kind = VariationKind.Chequy } },
            { "checky", new VariationEntry { Kind = VariationKind.Chequy } },
            { "lozengy", new VariationEntry { Kind = VariationKind.Lozengy } },
            { "gyronny", new VariationEntry { Kind = VariationKind.Gyronny, DefaultCount = 8 } },
            { "checkered", new VariationEntry { Kind = VariationKind.Chequy, PassableNote = "the heraldic term is 'chequy'" } },
            { "chequered", new VariationEntry { Kind = VariationKind.Chequy, PassableNote = "the heraldic term is 'chequy'" } },
            { "checked", new VariationEntry { Kind = VariationKind.Chequy, PassableNote = "the heraldic term is 'chequy'" } },
            { "checkerboard", new VariationEntry { Kind = VariationKind.Chequy, PassableNote = "the heraldic term is 'chequy'" } },
            { "diamonds", new VariationEntry { Kind = VariationKind.Lozengy, PassableNote = "the heraldic term is 'lozengy'" } },
        };

        public sealed class LineEntry
        {
            public LineKind Kind;
            public string PassableNote;
        }

        public static readonly Dictionary<string, LineEntry> Lines = new Dictionary<string, LineEntry>
        {
            { "engrailed", new LineEntry { Kind = LineKind.Engrailed } },
            { "invected", new LineEntry { Kind = LineKind.Invected } },
            { "indented", new LineEntry { Kind = LineKind.Indented } },
            { "dancetty", new LineEntry { Kind = LineKind.Dancetty } },
            { "dancette", new LineEntry { Kind = LineKind.Dancetty } },
            { "wavy", new LineEntry { Kind = LineKind.Wavy } },
            { "undy", new LineEntry { Kind = LineKind.Wavy } },
            { "nebuly", new LineEntry { Kind = LineKind.Nebuly } },
            { "embattled", new LineEntry { Kind = LineKind.Embattled } },
            { "crenellated", new LineEntry { Kind = LineKind.Embattled, PassableNote = "the heraldic term is 'embattled'" } },
            { "battlemented", new LineEntry { Kind = LineKind.Embattled, PassableNote = "the heraldic term is 'embattled'" } },
            { "scalloped", new LineEntry { Kind = LineKind.Engrailed, PassableNote = "the heraldic term is 'engrailed' (points outward) or 'invected' (points inward)" } },
            { "zigzag", new LineEntry { Kind = LineKind.Dancetty, PassableNote = "the heraldic term is 'dancetty' (large teeth) or 'indented' (small teeth)" } },
            { "wavey", new LineEntry { Kind = LineKind.Wavy, PassableNote = "spelt 'wavy'" } },
        };

        public sealed class OrdinaryEntry
        {
            public OrdinaryKind Kind;
            public bool Plural;
            public bool Diminutive;
            /// <summary>Single-only ordinaries: chief, cross, saltire, bordure, pall.</summary>
            public bool SingleOnly;
            public string PassableNote;
        }

        public static readonly Dictionary<string, OrdinaryEntry> Ordinaries = new Dictionary<string, OrdinaryEntry>
        {
            { "chief", new OrdinaryEntry { Kind = OrdinaryKind.Chief, SingleOnly = true } },
            { "fess", new OrdinaryEntry { Kind = OrdinaryKind.Fess, SingleOnly = true } },
            { "fesse", new OrdinaryEntry { Kind = OrdinaryKind.Fess, SingleOnly = true } },
            { "bar", new OrdinaryEntry { Kind = OrdinaryKind.Fess, Diminutive = true } },
            { "bars", new OrdinaryEntry { Kind = OrdinaryKind.Fess, Diminutive = true, Plural = true } },
            { "fesses", new OrdinaryEntry { Kind = OrdinaryKind.Fess, Diminutive = true, Plural = true, PassableNote = "more than one fess is blazoned as 'bars'" } },
            { "pale", new OrdinaryEntry { Kind = OrdinaryKind.Pale } },
            { "pales", new OrdinaryEntry { Kind = OrdinaryKind.Pale, Plural = true, Diminutive = true } },
            { "pallet", new OrdinaryEntry { Kind = OrdinaryKind.Pale, Diminutive = true } },
            { "pallets", new OrdinaryEntry { Kind = OrdinaryKind.Pale, Diminutive = true, Plural = true } },
            { "bend", new OrdinaryEntry { Kind = OrdinaryKind.Bend } },
            { "bends", new OrdinaryEntry { Kind = OrdinaryKind.Bend, Plural = true, Diminutive = true } },
            { "bendlet", new OrdinaryEntry { Kind = OrdinaryKind.Bend, Diminutive = true } },
            { "bendlets", new OrdinaryEntry { Kind = OrdinaryKind.Bend, Diminutive = true, Plural = true } },
            { "chevron", new OrdinaryEntry { Kind = OrdinaryKind.Chevron } },
            { "chevrons", new OrdinaryEntry { Kind = OrdinaryKind.Chevron, Plural = true, Diminutive = true } },
            { "chevronel", new OrdinaryEntry { Kind = OrdinaryKind.Chevron, Diminutive = true } },
            { "chevronels", new OrdinaryEntry { Kind = OrdinaryKind.Chevron, Diminutive = true, Plural = true } },
            { "cross", new OrdinaryEntry { Kind = OrdinaryKind.Cross, SingleOnly = true } },
            { "saltire", new OrdinaryEntry { Kind = OrdinaryKind.Saltire, SingleOnly = true } },
            { "pile", new OrdinaryEntry { Kind = OrdinaryKind.Pile } },
            { "piles", new OrdinaryEntry { Kind = OrdinaryKind.Pile, Plural = true } },
            { "bordure", new OrdinaryEntry { Kind = OrdinaryKind.Bordure, SingleOnly = true } },
            { "border", new OrdinaryEntry { Kind = OrdinaryKind.Bordure, SingleOnly = true, PassableNote = "the heraldic term is 'bordure'" } },
            { "pall", new OrdinaryEntry { Kind = OrdinaryKind.Pall, SingleOnly = true } },
        };

        /// <summary>Words that come after "cross" and make it a cross-shaped charge rather than the ordinary.</summary>
        public static readonly HashSet<string> CrossCharges = new HashSet<string>
        {
            "crosslet", "crosslets", "moline", "patonce", "fleury", "flory", "formy", "formee", "pattee", "patty",
            "potent", "botonny", "fitchy", "fitchee", "avellane", "pommy", "bottony",
        };

        public sealed class ChargeEntry
        {
            public string Singular;
            public bool Plural;
            /// <summary>Roundels with a built-in tincture (a bezant is a roundel or).</summary>
            public string ImpliedTincture;
        }

        public static readonly Dictionary<string, ChargeEntry> Charges = new Dictionary<string, ChargeEntry>();

        public static readonly HashSet<string> Attitudes = new HashSet<string>
        {
            "rampant", "passant", "statant", "sejant", "salient", "couchant", "courant", "segreant",
            "guardant", "gardant", "regardant", "displayed", "volant", "rising", "close", "naiant", "erect",
            "contourne", "contourny", "reversed", "inverted", "affronte", "addorsed", "combatant", "caboshed",
            "couped", "erased", "elevated", "inverted", "respectant", "embowed", "nowed", "hauriant",
        };

        /// <summary>Things charges can be "armed and langued" with; parsed, not drawn yet.</summary>
        public static readonly HashSet<string> ChargeDetails = new HashSet<string>
        {
            "armed", "langued", "attired", "unguled", "crowned", "collared", "beaked", "membered", "fructed", "eyed",
        };

        public static readonly Dictionary<string, int> Numbers = new Dictionary<string, int>
        {
            { "a", 1 }, { "an", 1 }, { "one", 1 }, { "two", 2 }, { "three", 3 }, { "four", 4 }, { "five", 5 },
            { "six", 6 }, { "seven", 7 }, { "eight", 8 }, { "nine", 9 }, { "ten", 10 }, { "eleven", 11 },
            { "twelve", 12 }, { "fourteen", 14 }, { "sixteen", 16 },
        };

        public static readonly Dictionary<string, int> Ordinals = new Dictionary<string, int>
        {
            { "first", 1 }, { "second", 2 }, { "third", 3 }, { "fourth", 4 }, { "fifth", 5 }, { "last", -1 },
        };

        /// <summary>Words with a specific "wrong" explanation (ambiguous, or not heraldry).</summary>
        public static readonly Dictionary<string, string> KnownWrong = new Dictionary<string, string>
        {
            { "striped", "ambiguous: 'paly' is vertical stripes, 'barry' is horizontal" },
            { "stripes", "ambiguous: 'paly' is vertical stripes, 'barry' is horizontal" },
            { "stripe", "ambiguous: 'pale' (vertical), 'fess' (horizontal) or 'bend' (diagonal)" },
            { "band", "ambiguous: 'pale' (vertical), 'fess' (horizontal) or 'bend' (diagonal)" },
            { "diagonal", "use 'bend' (from top left) or 'bend sinister' (from top right)" },
            { "horizontal", "use 'fess' for a horizontal band, 'per fess' to divide the field" },
            { "vertical", "use 'pale' for a vertical band, 'per pale' to divide the field" },
            { "quartered", null }, // handled as passable 'quarterly' by the parser
        };

        /// <summary>Accepted filler words: no meaning, always Correct.</summary>
        public static readonly HashSet<string> Fillers = new HashSet<string> { "field", "with", "all", "the" };

        static Lexicon()
        {
            void T(string word, string canonical, TinctureKind kind, string note = null) =>
                Tinctures[word] = new TinctureEntry { Canonical = canonical, Kind = kind, PassableNote = note };

            T("or", "or", TinctureKind.Metal);
            T("argent", "argent", TinctureKind.Metal);
            T("azure", "azure", TinctureKind.Colour);
            T("gules", "gules", TinctureKind.Colour);
            T("sable", "sable", TinctureKind.Colour);
            T("vert", "vert", TinctureKind.Colour);
            T("purpure", "purpure", TinctureKind.Colour);
            T("tenne", "tenne", TinctureKind.Stain);
            T("tawny", "tenne", TinctureKind.Stain);
            T("sanguine", "sanguine", TinctureKind.Stain);
            T("murrey", "murrey", TinctureKind.Stain);

            T("ermine", "ermine", TinctureKind.Fur);
            T("ermines", "ermines", TinctureKind.Fur);
            T("erminois", "erminois", TinctureKind.Fur);
            T("pean", "pean", TinctureKind.Fur);
            T("vair", "vair", TinctureKind.Fur);
            T("countervair", "counter-vair", TinctureKind.Fur);
            T("counter-vair", "counter-vair", TinctureKind.Fur);
            T("potent", "potent", TinctureKind.Fur);
            T("counterpotent", "counter-potent", TinctureKind.Fur);
            T("counter-potent", "counter-potent", TinctureKind.Fur);

            T("gold", "or", TinctureKind.Metal, "the heraldic term is 'or'");
            T("golden", "or", TinctureKind.Metal, "the heraldic term is 'or'");
            T("yellow", "or", TinctureKind.Metal, "the heraldic term is 'or'");
            T("silver", "argent", TinctureKind.Metal, "the heraldic term is 'argent'");
            T("white", "argent", TinctureKind.Metal, "the heraldic term is 'argent'");
            T("blue", "azure", TinctureKind.Colour, "the heraldic term is 'azure'");
            T("red", "gules", TinctureKind.Colour, "the heraldic term is 'gules'");
            T("black", "sable", TinctureKind.Colour, "the heraldic term is 'sable'");
            T("green", "vert", TinctureKind.Colour, "the heraldic term is 'vert'");
            T("purple", "purpure", TinctureKind.Colour, "the heraldic term is 'purpure'");
            T("orange", "tenne", TinctureKind.Stain, "the heraldic term is 'tenné'");

            void C(string singular, string plural = null, string implied = null)
            {
                Charges[singular] = new ChargeEntry { Singular = singular, ImpliedTincture = implied };
                if (plural != null)
                    Charges[plural] = new ChargeEntry { Singular = singular, Plural = true, ImpliedTincture = implied };
            }

            C("mullet", "mullets"); C("estoile", "estoiles"); C("roundel", "roundels"); C("annulet", "annulets");
            C("lozenge", "lozenges"); C("mascle", "mascles"); C("rustre", "rustres"); C("fusil", "fusils");
            C("billet", "billets"); C("crescent", "crescents"); C("fleur-de-lis", "fleurs-de-lis");
            C("martlet", "martlets"); C("escallop", "escallops"); C("lion", "lions"); C("eagle", "eagles");
            C("wolf", "wolves"); C("boar", "boars"); C("stag", "stags"); C("hart", "harts"); C("raven", "ravens");
            C("dragon", "dragons"); C("wyvern", "wyverns"); C("tree", "trees"); C("longship", "longships");
            C("ship", "ships"); C("hammer", "hammers"); C("valknut", "valknuts"); C("rune", "runes");
            C("sword", "swords"); C("axe", "axes"); C("tower", "towers"); C("key", "keys"); C("horn", "horns");
            C("saltorel", "saltorels"); C("heart", "hearts"); C("star", "stars");
            C("bezant", "bezants", "or"); C("plate", "plates", "argent"); C("torteau", "torteaux", "gules");
            C("hurt", "hurts", "azure"); C("pellet", "pellets", "sable"); C("pomme", "pommes", "vert");
            C("ogress", "ogresses", "sable"); C("golpe", "golpes", "purpure");
            // More than one saltire or cross is a charge, not the ordinary
            Charges["saltires"] = new ChargeEntry { Singular = "saltire", Plural = true };
            Charges["crosses"] = new ChargeEntry { Singular = "cross", Plural = true };
        }

        /// <summary>Every known word, for "did you mean" suggestions.</summary>
        public static IEnumerable<string> AllWords()
        {
            foreach (var k in Tinctures.Keys) yield return k;
            foreach (var k in PartitionWords.Keys) yield return k;
            foreach (var k in Variations.Keys) yield return k;
            foreach (var k in Lines.Keys) yield return k;
            foreach (var k in Ordinaries.Keys) yield return k;
            foreach (var k in Charges.Keys) yield return k;
            foreach (var k in Attitudes) yield return k;
            yield return "per"; yield return "party"; yield return "quarterly"; yield return "sinister";
            yield return "counterchanged"; yield return "proper"; yield return "between"; yield return "on";
        }

        public static string Suggest(string word)
        {
            string best = null;
            int bestDistance = word.Length <= 4 ? 1 : 2;
            foreach (var candidate in AllWords())
            {
                if (candidate == word || System.Math.Abs(candidate.Length - word.Length) > bestDistance) continue;
                int d = Distance(word, candidate);
                if (d <= bestDistance && (best == null || d < Distance(word, best)))
                    best = candidate;
            }
            return best;
        }

        private static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
            {
                int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = System.Math.Min(System.Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                    d[i, j] = System.Math.Min(d[i, j], d[i - 2, j - 2] + 1); // swapped letters count once
            }
            return d[a.Length, b.Length];
        }
    }
}
