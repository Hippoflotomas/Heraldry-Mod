using System.Collections.Generic;
using HarmonyLib;

namespace ValheimHeraldryMod.UI
{
    /// <summary>
    ///     Heraldry pages in the compendium (the Raven tab of the inventory's texts; Hippo, 2026-10-25): what blazon is
    ///     and how to write it, in plain words, with examples that all parse. Added to the list each time it's built,
    ///     so they're never stored in the character file and always match this version of the mod.
    /// </summary>
    [HarmonyPatch]
    internal static class Compendium
    {
        private const string Ex = "<color=#f0c860>";  // example blazons
        private const string Hd = "<color=#ffa040><b>"; // headings
        private const string End = "</color>";
        private const string HdEnd = "</b></color>";

        private static string E(string blazon) => Ex + blazon + End;
        private static string H(string heading) => Hd + heading + HdEnd;

        internal static readonly KeyValuePair<string, string>[] Pages =
        {
            new KeyValuePair<string, string>("Heraldry 1: Writing a blazon", string.Join("\n",
                "A <b>blazon</b> is a coat of arms written in words. Write it once and every player draws the same arms from it.",
                "",
                H("The order"),
                "1. The <b>field</b> (the background) and its colour.",
                "2. The <b>ordinaries</b> (big bands and shapes) and <b>charges</b> (pictures), each followed by its colour.",
                "",
                E("Azure, a bend or") + " - a blue field with a gold diagonal band.",
                E("Gules, a chevron or between three mullets argent") + " - red, a gold chevron, three silver stars around it.",
                "Capitals and commas are optional. Colours come <i>after</i> the thing they colour.",
                "",
                H("Dividing the field"),
                "<b>Per pale</b>: split down the middle. <b>Per fess</b>: split across. <b>Per bend</b> / <b>per bend sinister</b>: split diagonally. " +
                "<b>Per chevron</b>: split in a V shape. <b>Per saltire</b>: split in an X. <b>Quarterly</b>: four quarters. Give two colours:",
                E("Per pale gules and argent, a chief sable"),
                "",
                H("Patterns"),
                "<b>Paly</b>: vertical stripes. <b>Barry</b>: horizontal stripes. <b>Bendy</b>: diagonal stripes. <b>Chequy</b>: a chessboard. " +
                "<b>Lozengy</b>: diamonds. <b>Gyronny</b>: wedges meeting in the middle. Add a count if you like:",
                E("Barry of eight or and sable"),
                "",
                H("Lines"),
                "Edges don't have to be straight. Put the line after the division or the ordinary:",
                "<b>Engrailed</b>: scallops pointing out. <b>Invected</b>: scallops pointing in. <b>Indented</b>: small teeth. " +
                "<b>Dancetty</b>: big zigzag. <b>Wavy</b>: waves. <b>Nebuly</b>: cloud-like lobes. <b>Embattled</b>: like castle battlements.",
                E("Quarterly or and azure, a bordure engrailed gules"),
                "",
                H("Tattered (this mod's own words)"),
                "Start with <b>slightly tattered</b>, <b>tattered</b> or <b>badly tattered</b> for battle-worn cloth (shields get worn paint):",
                E("Tattered gules, a saltire argent"),
                "",
                H("The editor"),
                "The <b>Basic</b> tab builds a blazon from drop-downs and shows the words it makes. The <b>Advanced</b> tab lets you write it: " +
                "red wavy underline = not understood, yellow dashed = understood but not proper blazon. Hover over a word to see why.")),

            new KeyValuePair<string, string>("Heraldry 2: Colours (tinctures)", string.Join("\n",
                "Heraldic colours are called <b>tinctures</b>.",
                "",
                H("Metals"),
                "<b>Or</b>: gold or yellow. <b>Argent</b>: silver or white.",
                H("Colours"),
                "<b>Gules</b>: red. <b>Azure</b>: blue. <b>Sable</b>: black. <b>Vert</b>: green. <b>Purpure</b>: purple.",
                H("Stains (rarer)"),
                "<b>Tenné</b>: orange-brown. <b>Sanguine</b>: blood red. <b>Murrey</b>: mulberry.",
                H("Furs (patterns that count as a colour)"),
                "<b>Ermine</b>: black spots on white. <b>Ermines</b>: white on black. <b>Erminois</b>: black on gold. <b>Pean</b>: gold on black. " +
                "<b>Vair</b> and <b>counter-vair</b>: blue and white bells. <b>Potent</b> and <b>counter-potent</b>: blue and white T shapes.",
                E("Ermine, a chief gules"),
                "",
                H("Plain names and colour codes"),
                "Plain names work too (red, blue, gold, silver, white, black, green, purple, orange); they're marked as not proper blazon, " +
                "but they draw. Any colour can be a hex code with a #:",
                E("Azure, a fess or, a chief #2a6041"),
                "",
                H("Special words that go where a colour goes"),
                "<b>Counterchanged</b>: on a divided field, the charge takes the opposite colour on each side. " + E("Per pale or and azure, a mullet counterchanged"),
                "<b>Proper</b>: a picture in its own natural colours (game items, trophies, your own pictures). " + E("Sable, a \"bronze axe\" proper"),
                "<b>Of the first / second / last</b>: the same colour as the first (second, last) one named. " +
                E("Azure, a fess argent between three mullets of the second"),
                "<b>Fretty</b> / <b>fretted</b>: covered with an interlaced trellis of the second colour. " +
                E("Argent, fretty gules") + "  " + E("Argent, a fess azure fretted or"),
                "<b>Tartan</b>: a woven tartan from a threadcount (see the tartan page).",
                "",
                "Old heraldry says never put a metal on a metal or a colour on a colour. This mod doesn't enforce it.")),

            new KeyValuePair<string, string>("Heraldry 3: Ordinaries", string.Join("\n",
                "Ordinaries are the big, simple shapes. Write <b>a</b> + the ordinary + its colour: " + E("Azure, a bend or"),
                "",
                "<b>Chief</b>: a band across the top. " + E("Azure, on a chief or three mullets gules"),
                "<b>Fess</b>: a band across the middle. <b>Bars</b>: two or more narrower ones. " + E("Argent, two bars gules"),
                "<b>Pale</b>: a band down the middle. <b>Pallets</b>: narrower ones. " + E("Argent, three pallets azure"),
                "<b>Bend</b>: a diagonal band from top left (as you look at it). <b>Bend sinister</b>: from top right. <b>Bendlets</b>: narrower ones. " +
                E("Azure, three bendlets or"),
                "<b>Chevron</b>: an upside-down V. <b>Chevronels</b>: narrower ones. " + E("Or, two chevronels sable"),
                "<b>Cross</b>: a plus sign across the whole field. <b>Saltire</b>: an X across the whole field.",
                "<b>Pile</b>: a wedge pointing down from the top (up to three). " + E("Vert, a pile or"),
                "<b>Pall</b>: a Y shape. " + E("Gules, a pall argent"),
                "<b>Bordure</b>: a border all round the edge. " + E("Quarterly or and azure, a bordure engrailed gules"),
                "",
                H("Extras"),
                "<b>Lines</b>: any ordinary can be engrailed, wavy, embattled and so on. " + E("Vert, a fess wavy argent"),
                "<b>Voided</b>: only the outline, the field showing through. " + E("Or, a cross voided gules"),
                "<b>Fimbriated</b>: a thin edging in another colour. " + E("Or, a cross gules fimbriated argent"),
                "<b>Over all</b>: laid on top of everything else, charges included. " + E("Azure, three mullets or, a bend gules over all"),
                "<b>Inscribed</b>: words along a fess, chief, cross, pale or bend (see the scrolls page).")),

            new KeyValuePair<string, string>("Heraldry 4: Charges", string.Join("\n",
                "Charges are the pictures. Write how many, what, then the colour: " + E("Gules, three mullets or"),
                "Use plurals for more than one (three mullets, two wolves' heads).",
                "",
                H("Where they go"),
                "With no placement, charges spread over the field in the usual arrangement for their number. To choose:",
                "<b>In chief</b> / <b>in base</b>: along the top / bottom. <b>In pale</b>: in a column. <b>In fess</b>: in a row. " +
                "<b>In bend</b>, <b>in saltire</b>, <b>in cross</b>: along those lines. <b>In orle</b>: in a ring round the edge.",
                E("Azure, three roundels in pale argent"),
                "<b>Rows</b>: say how many in each row from the top. " + E("Gules, three mullets two and one or"),
                "<b>Between</b>: around an ordinary. " + E("Gules, a chevron or between three mullets argent"),
                "<b>On</b>: laid on an ordinary. " + E("Azure, on a chief or three mullets gules"),
                "<b>Charged with</b>: the same, after the ordinary. " + E("Gules, a bordure argent charged with eight bezants"),
                "<b>Points of the shield</b>: in dexter / middle / sinister chief, in dexter or sinister (the sides), " +
                "in dexter / middle / sinister base, in honour point, in fess point (the centre), in nombril point. " +
                "Dexter is the bearer's right, which is <i>your left</i> as you look at it. " + E("Or, a mullet in dexter chief azure"),
                "<b>Quarters</b>: " + E("Quarterly azure and sable, in the first and fourth quarters a mullet or"),
                "",
                H("Turning and styling"),
                "<b>Contourné</b>: facing the other way. <b>Inverted</b> or <b>reversed</b>: upside down. " + E("Gules, a crescent inverted argent"),
                "<b>Voided</b> and <b>fimbriated</b> work on charges as on ordinaries.",
                "",
                H("Which charges you can use"),
                "<b>Shapes the mod draws</b>: mullets, roundels, crosses and more (see the last heraldry page).",
                "<b>Trophy heads</b>: a wolf's head, boar, stag, elk, bear, troll, lox, seal, serpent, goblin, greydwarf, draugr, drake or asksvin, " +
                "a skull or death's head, and the Forsaken by name (Eikthyr, the Elder, Bonemass, Moder, Yagluth, the Queen, Fader).",
                E("Gules, two stags' heads caboshed or") + "  " + E("Azure, Eikthyr proper"),
                "<b>Any game item</b>: its in-game name in double quotes. Give a colour for a silhouette, or <b>proper</b> for its own colours.",
                E("Sable, a \"bronze axe\" proper") + "  " + E("Or, a \"wood\" sable"),
                "<b>Your own pictures</b>: a PNG in your Charges folder, written in square brackets: " + Ex + "Or, a [black dog] sable" + End +
                ". See <b>Heraldry 7: Your own charges</b> below.",
                "",
                "Charges the mod knows but has no drawing for (lion, eagle, dragon, raven, fleur-de-lis...) show a magenta and black " +
                "placeholder roundel. Use your own picture for those.")),

            new KeyValuePair<string, string>("Heraldry 5: Scrolls and mottos", string.Join("\n",
                "Arms can carry words.",
                "",
                H("A scroll"),
                "A ribbon with a motto, normally across the bottom. The scroll's colour comes first, then <b>inscribed</b>, the words in double " +
                "quotes, and the ink colour:",
                E("Azure, a scroll argent inscribed \"FOR ODIN\" sable"),
                "",
                H("Along an ordinary"),
                "A fess, chief, cross, pale, bend or bend sinister can carry the words itself:",
                E("Gules, a fess or inscribed \"SKOL\" in runes sable"),
                "",
                H("Runes"),
                "Add <b>in runes</b> after the words to write them in Elder Futhark runes.",
                "",
                H("Good to know"),
                "If you leave out the ink colour, it's black on light colours and white on dark ones. " +
                "The server sets the longest motto (40 letters unless changed), and can hide mottos from players; admins still see them.")),

            new KeyValuePair<string, string>("Heraldry 6: Tartan", string.Join("\n",
                "<b>Tartan</b> isn't a blazon word; it's this mod's own addition. Write <b>tartan</b> followed by a traditional " +
                "<b>threadcount</b> in double quotes. It works <b>anywhere a colour goes</b>: the field, an ordinary, a charge.",
                E("Tartan \"K4 R24 K24 Y4\"") + "  " + E("Or, a bend tartan \"K4 R24 K24 Y4\""),
                "",
                H("Reading a threadcount"),
                "Each stripe is a colour code and its number of threads: <b>K4</b> = 4 threads of black.",
                "Codes: <b>K</b> black, <b>W</b> white, <b>R</b> red, <b>B</b> blue, <b>G</b> green, <b>Y</b> yellow, <b>O</b> orange, " +
                "<b>P</b> purple, <b>N</b> grey, <b>T</b> brown. Put <b>L</b> (light) or <b>D</b> (dark) in front for shades: DB, LG...",
                "",
                H("How it's woven"),
                "The count mirrors at its first and last stripes, as most tartans do. A slash marks a half-count end stripe: " +
                E("Tartan \"K/2 R24 K24 Y/2\"") + " is the same as K4 R24 K24 Y4.",
                "Put <b>...</b> at both ends for a pattern that repeats without mirroring: " + E("Tartan \"...K4 R24 K24 Y4...\""),
                "It's woven as a 2/2 twill and centred on the middle of the arms.",
                "",
                "Real tartans and their threadcounts are listed by the Scottish Register of Tartans (tartanregister.gov.uk). " +
                "If a code isn't recognised, the editor underlines it; the nearest code above will do.")),

            new KeyValuePair<string, string>("Heraldry 7: Your own charges", string.Join("\n",
                H("Adding a picture"),
                "Put a PNG in <b>Documents\\ValheimHeraldryMod\\Charges</b> (made the first time the mod runs). Its file name is its name: " +
                "<b>black_dog.png</b> becomes <b>[black dog]</b>. Type <b>heraldry_reload</b> in the console (F5) if the game is running.",
                Ex + "Or, a [black dog] sable" + End + " - the picture's shape in black.",
                Ex + "Or, a [black dog] proper" + End + " - the picture in its own colours.",
                "",
                H("Claws and tongue: the mask"),
                "An optional second PNG, <b>black_dog.mask.png</b>, the same size, marks parts of the picture by colour:",
                "<b>Red</b>: interior lines (drawn black, or white on a black charge).",
                "<b>Green</b>: claws, teeth, horns, beak: the <b>armed</b> parts.",
                "<b>Blue</b>: the tongue: the <b>langued</b> part.",
                "Then colour them in the blazon:",
                Ex + "Or, a [black dog] sable armed and langued gules" + End + " - red claws and tongue.",
                Ex + "Or, a [black dog] sable armed argent langued gules" + End + " - white claws, red tongue.",
                "Not named = the same colour as the charge. <b>Disarmed</b> leaves the claws off, <b>morné</b> leaves claws and tongue off, " +
                "and <b>plain</b> (this mod's word) ignores the mask altogether.",
                "Without red lines in the mask, interior lines are worked out from the picture's own shading.",
                "",
                H("Packs"),
                "A folder or .zip of PNGs is a pack. A <b>manifest.json</b> in it can give each picture several names, attitudes " +
                "(" + Ex + "[wolf] rampant" + End + ") or cut many from one atlas image. If two packs share a name, use " +
                Ex + "[pack:name]" + End + ". Pictures up to 1024 pixels.",
                "",
                H("Sharing on a server"),
                "Other players get your pictures through the server, but only when they choose to. Someone who meets a picture they don't " +
                "have is told so, and sees a placeholder until they type <b>heraldry_get</b> (or heraldry_get all). " +
                "<b>heraldry_pending</b> lists what's missing, <b>heraldry_block</b> hides a picture for you, and admins can " +
                "<b>heraldry_blacklist</b> one for everyone. <b>heraldry_info</b> shows the arms you're looking at.")),

            new KeyValuePair<string, string>("Heraldry 8: Charges drawn by the mod", string.Join("\n",
                "These shapes are drawn by the mod itself, sharp at any size, in any colour:",
                "",
                "<b>Mullet</b>: a five-pointed star (" + E("a mullet of six points") + ", " + E("a mullet pierced") + " with a round hole).",
                "<b>Roundel</b>: a disc. Roundels have their own names by colour: <b>bezant</b> (gold), <b>plate</b> (silver), " +
                "<b>torteau</b> (red), <b>hurt</b> (blue), <b>pellet</b> or <b>ogress</b> (black), <b>pomme</b> (green), <b>golpe</b> (purple). " +
                "These need no colour: " + E("Azure, eight bezants in orle"),
                "<b>Annulet</b>: a ring.",
                "<b>Lozenge</b>: a diamond. <b>Fusil</b>: a narrow diamond. <b>Mascle</b>: a hollow diamond. <b>Rustre</b>: a diamond with a round hole.",
                "<b>Billet</b>: an upright rectangle.",
                "<b>Crescent</b>: a crescent moon, horns up (" + E("inverted") + " for horns down).",
                "<b>Cross couped</b>: a small cross not touching the edges. <b>Cross crosslet</b>: each arm crossed again. " +
                "<b>Cross pattée</b>: arms widening outwards. <b>Cross potent</b>: arms ending in T shapes.",
                "<b>Saltorel</b>: a small X. Two or more <b>crosses</b> or <b>saltires</b> are small ones too.",
                "<b>Scroll</b>: a ribbon for a motto (see the scrolls page).",
                "",
                "Also drawn from the game itself: trophy heads, the Forsaken and any item in quotes (see the charges page).")),
        };

        private static readonly AccessTools.FieldRef<TextsDialog, List<TextsDialog.TextInfo>> Texts =
            AccessTools.FieldRefAccess<TextsDialog, List<TextsDialog.TextInfo>>("m_texts");

        /// <summary>After the game fills the compendium's list: our pages at the end, in order.</summary>
        [HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
        [HarmonyPostfix]
        private static void AddPages(TextsDialog __instance)
        {
            if (!HeraldryConfig.CompendiumPages) return;
            var list = Texts(__instance);
            if (list == null) return;
            foreach (var p in Pages) list.Add(new TextsDialog.TextInfo(p.Key, p.Value));
        }
    }
}
