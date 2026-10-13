using System.Collections.Generic;

namespace ValheimHeraldryMod.Heraldry
{
    // Pure C#: nothing in Heraldry/ may touch UnityEngine, so the parser can run on a dedicated server
    // and the whole folder can be tested outside the game. Stick to APIs that exist in .NET Framework 4.8
    // (no MathF, Math.Clamp, string.Contains(char), GetValueOrDefault, records).

    /// <summary>8-bit RGBA colour. Converted to Color32 by the Unity side.</summary>
    public struct Rgba
    {
        public byte R, G, B, A;
        public Rgba(byte r, byte g, byte b, byte a = 255) { R = r; G = g; B = b; A = a; }
        public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
    }

    /// <summary>How the editor should mark one word (maps to the underline styles in the editing window).</summary>
    public enum WordStatus
    {
        /// <summary>Proper blazon, or accepted filler ("a", "field", "with", punctuation).</summary>
        Correct,
        /// <summary>Understood, but not the heraldic term ("gold" for or). Message gives the right word.</summary>
        Passable,
        /// <summary>Not understood, or ambiguous ("striped"). Message explains and may suggest.</summary>
        Wrong,
        /// <summary>Correct blazon that this version understands but doesn't draw yet (charges, for now).</summary>
        NotDrawnYet,
        /// <summary>Real blazon this version doesn't handle at all (e.g. full quartering of separate arms).</summary>
        Unsupported,
    }

    public sealed class WordNote
    {
        public int Start;      // character index in the original text
        public int Length;
        public string Word;    // as typed
        public WordStatus Status;
        public string Message; // null when there is nothing to say

        public override string ToString() => $"{Word}:{Status}{(Message == null ? "" : " (" + Message + ")")}";
    }

    public enum TinctureKind { Metal, Colour, Stain, Fur, Hex, Proper, Counterchanged }

    public sealed class Tincture
    {
        public TinctureKind Kind;
        /// <summary>Canonical heraldic name ("or", "ermine"), or "#rrggbb" for hex.</summary>
        public string Name;
        public Rgba Hex;

        public override string ToString() => Name;
    }

    public enum PartitionKind { None, Pale, Fess, Bend, BendSinister, Chevron, Saltire, Quarterly }

    public enum VariationKind { None, Paly, Barry, Bendy, BendySinister, Chequy, Lozengy, Gyronny }

    public enum LineKind { Straight, Engrailed, Invected, Indented, Dancetty, Wavy, Nebuly, Embattled }

    public enum OrdinaryKind { Chief, Fess, Pale, Bend, BendSinister, Chevron, Cross, Saltire, Pile, Bordure, Pall }

    /// <summary>The field: one tincture, a partition, or a variation (two tinctures repeated).</summary>
    public sealed class Field
    {
        public PartitionKind Partition;
        public VariationKind Variation;
        /// <summary>For variations: number of pieces ("barry of eight").</summary>
        public int Count;
        public LineKind Line;
        /// <summary>One tincture for a plain field, two for partitions and variations.</summary>
        public List<Tincture> Tinctures = new List<Tincture>();
    }

    public sealed class Ordinary
    {
        public OrdinaryKind Kind;
        /// <summary>1 for a single ordinary; more for diminutives ("three bars", "two chevronels").</summary>
        public int Count = 1;
        /// <summary>Drawn narrower than the full ordinary (bars, pallets, bendlets, chevronels, or plurals).</summary>
        public bool Diminutive;
        public LineKind Line;
        public Tincture Tincture;
        /// <summary>Charges placed on this ordinary ("on a chief azure three mullets or").</summary>
        public List<Charge> Charges = new List<Charge>();
    }

    /// <summary>Parsed, not drawn yet (build order steps 5-6).</summary>
    public sealed class Charge
    {
        public int Count = 1;
        public string Noun;          // canonical singular
        public List<string> Attitudes = new List<string>();
        public Tincture Tincture;
        public string Placement;     // "in chief", "between", ...
    }

    public sealed class Blazon
    {
        public string Text;
        public Field Field;
        public List<Ordinary> Ordinaries = new List<Ordinary>();
        public List<Charge> Charges = new List<Charge>();
        public List<WordNote> Words = new List<WordNote>();
        /// <summary>Problems that don't belong to one word ("per pale needs two tinctures").</summary>
        public List<string> Messages = new List<string>();

        /// <summary>True when nothing is marked Wrong and there is a field to draw.</summary>
        public bool IsValid
        {
            get
            {
                if (Field == null) return false;
                foreach (var w in Words)
                    if (w.Status == WordStatus.Wrong) return false;
                return true;
            }
        }
    }
}
