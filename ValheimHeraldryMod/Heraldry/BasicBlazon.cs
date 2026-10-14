using System;
using System.Collections.Generic;
using System.Text;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Basic mode (Hippo, 2026-10-13): the dropdown editor's choices, and the blazon they make. Pure C#, so the
    ///     preview tool can check every combination parses. The string is the only output; saving, syncing and
    ///     drawing are the same as for written arms. Choices are stored as heraldic words ("azure", "per pale").
    /// </summary>
    public sealed class BasicBlazon
    {
        // ------------------------------------------------------------------ the menus

        /// <summary>A dropdown entry: what the player sees, and the word it stands for (null = a divider).</summary>
        public sealed class Option
        {
            public string Label, Value;
            public Option(string label, string value) { Label = label; Value = value; }
            public bool IsDivider => Value == null;
        }

        public static readonly Option[] Fields =
        {
            new Option("Plain", "plain"),
            new Option("Per pale (side by side)", "per pale"),
            new Option("Per fess (top / bottom)", "per fess"),
            new Option("Per bend (diagonal)", "per bend"),
            new Option("Per bend sinister", "per bend sinister"),
            new Option("Per chevron", "per chevron"),
            new Option("Per saltire (an X)", "per saltire"),
            new Option("Quarterly (4 quarters)", "quarterly"),
            new Option("Paly (upright stripes)", "paly"),
            new Option("Barry (stripes across)", "barry"),
            new Option("Bendy (diagonal stripes)", "bendy"),
            new Option("Chequy (chequered)", "chequy"),
            new Option("Gyronny (wedges)", "gyronny"),
        };

        /// <summary>Plain colour first, tincture in brackets; the two metals on top, under a divider (Hippo).</summary>
        public static readonly Option[] Colours =
        {
            new Option("Gold (or)", "or"),
            new Option("Silver (argent)", "argent"),
            new Option("- - - - - - - -", null),
            new Option("Red (gules)", "gules"),
            new Option("Blue (azure)", "azure"),
            new Option("Black (sable)", "sable"),
            new Option("Green (vert)", "vert"),
            new Option("Purple (purpure)", "purpure"),
            new Option("- - - - - - - -", null),
            new Option("Ermine (fur)", "ermine"),
            new Option("Vair (fur)", "vair"),
        };

        /// <summary>Three line styles only; the rest is advanced mode (Hippo).</summary>
        public static readonly Option[] Lines =
        {
            new Option("Straight", "plain"),
            new Option("Wavy", "wavy"),
            new Option("Indented", "indented"),
        };

        public static readonly Option[] Ordinaries =
        {
            new Option("None", "none"),
            new Option("Chief (top band)", "chief"),
            new Option("Pale (upright band)", "pale"),
            new Option("Fess (band across)", "fess"),
            new Option("Bend (diagonal band)", "bend"),
            new Option("Bend sinister", "bend sinister"),
            new Option("Chevron", "chevron"),
            new Option("Cross", "cross"),
            new Option("Saltire (an X)", "saltire"),
            new Option("Pile (wedge)", "pile"),
            new Option("Bordure (border)", "bordure"),
        };

        /// <summary>The second ordinary: only these combine cleanly with anything (Hippo).</summary>
        public static readonly Option[] SecondOrdinaries =
        {
            new Option("None", "none"),
            new Option("Chief (top band)", "chief"),
            new Option("Bordure (border)", "bordure"),
        };

        public static readonly Option[] Styles =
        {
            new Option("Solid", "solid"),
            new Option("Outline (voided)", "voided"),
            new Option("Proper (natural)", "proper"),
        };

        public static readonly Option[] Placements =
        {
            new Option("In the middle", "alone"),
            new Option("On the ordinary", "on"),
            new Option("Around the ordinary", "between"),
            new Option("In chief (along the top)", "in chief"),
            new Option("In base (along the bottom)", "in base"),
        };

        public static readonly Option[] Counts = { new Option("One", "1"), new Option("Two", "2"), new Option("Three", "3") };

        /// <summary>A charge on the menu: the words for one and for several ("mullet" / "mullets").</summary>
        public sealed class ChargeChoice
        {
            public string Label, One, Many;
            /// <summary>Drawn shapes have no natural colours, so "proper" makes no sense for them.</summary>
            public bool Geometric;
            public bool IsDivider => One == null;
            /// <summary>Only ever one (the Forsaken).</summary>
            public bool OnlyOne => Many == null;
        }

        private static ChargeChoice Shape(string label, string one, string many) => new ChargeChoice { Label = label, One = one, Many = many, Geometric = true };
        private static ChargeChoice Head(string label, string animal, string animals) =>
            new ChargeChoice { Label = label, One = animal + "'s head", Many = animals + "' heads" };
        /// <summary>One of a kind (the Forsaken): never more than one.</summary>
        private static ChargeChoice Single(string label, string one) => new ChargeChoice { Label = label, One = one, Many = null };
        private static ChargeChoice Divider(string label) => new ChargeChoice { Label = label };

        /// <summary>The built-in charges. Charge packs are added after these by the editor (<see cref="PackCharge"/>).</summary>
        public static readonly ChargeChoice[] BuiltInCharges =
        {
            new ChargeChoice { Label = "None", One = "", Many = "" },
            Divider("--- Shapes ---"),
            Shape("Mullet (star)", "mullet", "mullets"),
            Shape("Roundel (disc)", "roundel", "roundels"),
            Shape("Annulet (ring)", "annulet", "annulets"),
            Shape("Lozenge (diamond)", "lozenge", "lozenges"),
            Shape("Mascle (hollow diamond)", "mascle", "mascles"),
            Shape("Fusil (narrow diamond)", "fusil", "fusils"),
            Shape("Billet (block)", "billet", "billets"),
            Shape("Crescent", "crescent", "crescents"),
            Shape("Cross", "cross couped", "crosses couped"),
            Shape("Cross crosslet", "cross crosslet", "crosses crosslet"),
            Shape("Cross pattée", "cross pattee", "crosses pattee"),
            Shape("Cross potent", "cross potent", "crosses potent"),
            Shape("Saltorel (small X)", "saltorel", "saltorels"),
            Divider("--- Trophies ---"),
            Head("Wolf's head", "wolf", "wolves"),
            Head("Boar's head", "boar", "boars"),
            Head("Stag's head", "stag", "stags"),
            Head("Elk's head", "elk", "elks"),
            Head("Bear's head", "bear", "bears"),
            Head("Troll's head", "troll", "trolls"),
            Head("Lox's head", "lox", "loxes"),
            Head("Seal's head", "seal", "seals"),
            Head("Serpent's head", "serpent", "serpents"),
            Head("Greydwarf's head", "greydwarf", "greydwarfs"),
            Head("Draugr's head", "draugr", "draugrs"),
            Head("Goblin's head", "goblin", "goblins"),
            Head("Drake's head", "drake", "drakes"),
            Head("Asksvin's head", "asksvin", "asksvins"),
            new ChargeChoice { Label = "Skull", One = "skull", Many = "skulls" },
            Single("Eikthyr", "eikthyr"),
            Single("The Elder", "elder"),
            Single("Bonemass", "bonemass"),
            Single("Moder", "moder"),
            Single("Yagluth", "yagluth"),
            Single("The Queen", "queen"),
            Single("Fader", "fader"),
        };

        public static ChargeChoice PackDivider => Divider("--- Charge packs ---");

        /// <summary>A charge from a pack: written in brackets, the same for one or several.</summary>
        public static ChargeChoice PackCharge(string name) =>
            new ChargeChoice { Label = name, One = "[" + name + "]", Many = "[" + name + "]" };

        // ------------------------------------------------------------------ the choices

        public string Field = "plain", Colour1 = "azure", Colour2 = "or", FieldLine = "plain";
        public string Ordinary = "none", OrdinaryColour = "or", OrdinaryLine = "plain";
        public string Second = "none", SecondColour = "argent", SecondLine = "plain";
        public ChargeChoice Charge = BuiltInCharges[0];
        public string Style = "solid", ChargeColour = "argent", Placement = "alone";
        public int Count = 1;

        // ------------------------------------------------------------------ what's greyed out

        public bool FieldHasSecondColour => Field != "plain";
        /// <summary>Partitions and stripes take a line style; a plain field, quarterly, chequy and gyronny don't.</summary>
        public bool FieldHasLine => Field.StartsWith("per ") || Field == "paly" || Field == "barry" || Field == "bendy";
        public bool HasOrdinary => Ordinary != "none";
        public bool HasSecond => Second != "none";
        public bool HasCharge => Charge != null && !string.IsNullOrEmpty(Charge.One);
        public bool ChargeHasColour => HasCharge && Style != "proper";
        public bool CanBeProper => HasCharge && !Charge.Geometric;

        /// <summary>Placements that make sense now: with no ordinary only alone / in chief / in base; never between a chief or bordure.</summary>
        public bool PlacementAllowed(string placement) =>
            placement == "on" ? HasOrdinary && Ordinary != "bordure" // a bordure is charged with eight, not one to three
            : placement == "between" ? HasOrdinary && Ordinary != "chief" && Ordinary != "bordure" // nothing is "between" a chief or bordure
            : true;

        /// <summary>Puts dependent choices back in range after a change (e.g. proper on a shape, "on" with no ordinary).</summary>
        public void Tidy()
        {
            if (HasCharge && Charge.Geometric && Style == "proper") Style = "solid";
            if (!PlacementAllowed(Placement)) Placement = "alone";
            if (Count < 1) Count = 1;
            if (Count > 3) Count = 3;
            if (HasCharge && Charge.OnlyOne) Count = 1;
            // A bordure or chief chosen in both rows would be blazoned twice
            if (Second == Ordinary) Second = "none";
        }

        // ------------------------------------------------------------------ the blazon

        private static readonly string[] CountWords = { "", "a", "two", "three" };

        public string Build()
        {
            Tidy();
            var sb = new StringBuilder();

            // Field
            if (Field == "plain") sb.Append(Cap(Colour1));
            else
            {
                sb.Append(Cap(Field));
                if (FieldHasLine && FieldLine != "plain") sb.Append(' ').Append(FieldLine);
                sb.Append(' ').Append(Colour1).Append(" and ").Append(Colour2);
            }

            string chargeText = HasCharge ? ChargeWords() : null;

            // Main ordinary, with the charge on it or around it if so placed
            if (HasOrdinary)
            {
                string ord = Ordinary + (OrdinaryLine != "plain" ? " " + OrdinaryLine : "");
                if (HasCharge && Placement == "on")
                    sb.Append(", on ").Append(Article(ord)).Append(' ').Append(ord).Append(' ').Append(OrdinaryColour).Append(' ').Append(chargeText);
                else
                {
                    sb.Append(", ").Append(Article(ord)).Append(' ').Append(ord).Append(' ').Append(OrdinaryColour);
                    if (HasCharge && Placement == "between") sb.Append(" between ").Append(chargeText);
                }
            }

            // Charges anywhere else
            if (HasCharge && Placement != "on" && Placement != "between")
            {
                if (Placement == "in chief" || Placement == "in base") sb.Append(", ").Append(Placement).Append(' ').Append(chargeText);
                else sb.Append(", ").Append(chargeText);
            }

            // The second ordinary goes last ("Azure, three mullets argent, a chief or")
            if (HasSecond)
            {
                string ord = Second + (SecondLine != "plain" ? " " + SecondLine : "");
                sb.Append(", ").Append(Article(ord)).Append(' ').Append(ord).Append(' ').Append(SecondColour);
            }
            return sb.ToString();
        }

        /// <summary>"three mullets voided argent", "a wolf's head proper", "two [black dog] or".</summary>
        private string ChargeWords()
        {
            string noun = Count > 1 ? Charge.Many : Charge.One;
            string count = Count > 1 ? CountWords[Count] : Article(noun);
            string tincture = Style == "proper" ? "proper" : (Style == "voided" ? "voided " : "") + ChargeColour;
            return $"{count} {noun} {tincture}";
        }

        private static string Article(string word)
        {
            string w = word.TrimStart('[', '"').ToLowerInvariant();
            return w.Length > 0 && "aeiou".IndexOf(w[0]) >= 0 ? "an" : "a";
        }

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
