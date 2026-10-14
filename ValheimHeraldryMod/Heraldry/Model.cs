using System;
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
        /// <summary>Set when this word names a tincture (for the editor's colour swatches).</summary>
        public Tincture Tincture;

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

    /// <summary>
    ///     Voided (hollowed out, showing what's beneath or a given tincture) and fimbriated (a narrow edging in
    ///     another tincture). Applies to ordinaries and charges alike.
    /// </summary>
    public sealed class Style
    {
        public bool Voided;
        /// <summary>Tincture inside a voided shape; null = whatever lies beneath shows through.</summary>
        public Tincture VoidTincture;
        /// <summary>Tincture of the edging; null = not fimbriated.</summary>
        public Tincture Fimbriation;

        public bool IsPlain => !Voided && Fimbriation == null;
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
        public Style Style = new Style();
        /// <summary>Charges placed on this ordinary ("on a chief azure three mullets or").</summary>
        public List<Charge> Charges = new List<Charge>();
    }

    /// <summary>
    ///     What a picture charge's mask adds. Normal: lines, claws and tongue. Disarmed: no claws, beak or teeth
    ///     (the armed layer). Morné: no claws or tongue either. Plain (this mod's own term): the colour image
    ///     alone, ignoring the mask entirely.
    /// </summary>
    public enum MaskMode { Normal, Disarmed, Morne, Plain }

    /// <summary>Where a group of charges goes. None = the default arrangement for the count.</summary>
    public enum Placement
    {
        None, Pale, Fess, Bend, BendSinister, Saltire, Cross, Chief, Base, Orle, Between, On,
        // Points of the shield ("in dexter chief"): dexter is the bearer's right, the viewer's left
        DexterChief, MiddleChief, SinisterChief, DexterFlank, SinisterFlank, DexterBase, MiddleBase, SinisterBase,
        HonourPoint, FessPoint, NombrilPoint,
    }

    public sealed class Charge
    {
        public int Count = 1;
        public string Noun;          // canonical singular ("mullet", "cross crosslet", "stag's head")
        public List<string> Attitudes = new List<string>();
        public Tincture Tincture;
        public Placement Placement;
        /// <summary>The ordinary these charges are "between" or "on".</summary>
        public Ordinary Host;
        /// <summary>Explicit rows ("two and one" = {2, 1}); null = default for the count.</summary>
        public int[] Rows;
        public Style Style = new Style();
        /// <summary>Mullets: number of points (5 unless "of six points").</summary>
        public int Points = 5;
        /// <summary>Mullets: pierced (a round hole showing what's beneath).</summary>
        public bool Pierced;
        /// <summary>
        ///     The renderer can draw this: a geometric shape, or picture art (see <see cref="ArtKey"/>).
        ///     Anything else is drawn as the fallback roundel until art for it exists.
        /// </summary>
        public bool Drawable;
        /// <summary>Picture art to draw ("trophywolf"); null for geometric charges. Missing art = fallback roundel.</summary>
        public string ArtKey;
        /// <summary>A charge from a charge pack, by the name in its brackets ("wolf", "northern-beasts:wolf").</summary>
        public string PackName;
        /// <summary>"armed gules": claws, horns, beak, hooves, antlers. Null = same as the charge.</summary>
        public Tincture Armed;
        /// <summary>"langued azure": the tongue. Null = same as the charge.</summary>
        public Tincture Langued;
        public MaskMode Mask;
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

        /// <summary>
        ///     How much there is to draw, for the server's element limit: one for a partitioned or patterned field,
        ///     one per ordinary, one per charge (three mullets = three), charges on ordinaries included.
        /// </summary>
        public int Elements
        {
            get
            {
                int n = Field != null && (Field.Partition != PartitionKind.None || Field.Variation != VariationKind.None) ? 1 : 0;
                foreach (var o in Ordinaries)
                {
                    n += Math.Max(1, o.Count);
                    foreach (var c in o.Charges) n += Math.Max(1, c.Count);
                }
                foreach (var c in Charges) n += Math.Max(1, c.Count);
                return n;
            }
        }

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
