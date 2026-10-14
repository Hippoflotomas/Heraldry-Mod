using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Hand-written recursive-descent blazon parser. Field first, then ordinaries and charges in order.
    ///     Every word gets a status and optional message (for the editor's underlines); after an error the
    ///     parser skips the word and carries on, so all problems show at once rather than only the first.
    ///     Deterministic and Unity-free, so a dedicated server can run it too.
    /// </summary>
    public sealed class BlazonParser
    {
        private sealed class Tok
        {
            public string Text;     // lower case, accents removed
            public WordNote Note;
            /// <summary>A comma or semicolon came just before this word.</summary>
            public bool AfterComma;
        }

        private readonly List<Tok> toks = new List<Tok>();
        private readonly Blazon result = new Blazon();
        private readonly List<Tincture> mentioned = new List<Tincture>(); // for "of the first" etc.
        private int pos;
        /// <summary>Ordinaries still waiting for a tincture given later ("a chevron between three mullets argent").</summary>
        private readonly List<Ordinary> awaitingTincture = new List<Ordinary>();
        /// <summary>The charge group just parsed, for a following "in pale" or "two and one".</summary>
        private Charge lastCharge;
        /// <summary>"in chief three mullets": a placement waiting for the charges that follow it.</summary>
        private Placement pendingPlacement;
        private const string NeedsArt = "this charge needs art; shown as a placeholder roundel for now";

        public static Blazon Parse(string text) => new BlazonParser(text ?? "").Run();

        /// <summary>The first thing worth telling the player ("'100': between two and sixteen pieces"), or null.</summary>
        public static string FirstProblem(Blazon b)
        {
            if (b == null) return null;
            foreach (var w in b.Words)
                if (w.Status == WordStatus.Wrong)
                    return $"'{w.Word}': {w.Message ?? "not understood"}";
            if (b.Messages.Count > 0) return b.Messages[0];
            foreach (var w in b.Words)
                if (w.Status == WordStatus.Unsupported)
                    return $"'{w.Word}': {w.Message ?? "not supported yet"}";
            return null;
        }

        private BlazonParser(string text)
        {
            result.Text = text;
            Tokenize(text);
        }

        // ------------------------------------------------------------------ tokens

        private void Tokenize(string text)
        {
            int i = 0;
            bool comma = false;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                int start = i;
                if (c == '[')
                {
                    // [wolf]: a charge from a charge pack. Like quotes, the text keeps its brackets.
                    i++;
                    while (i < text.Length && text[i] != ']') i++;
                    bool closed = i < text.Length;
                    if (closed) i++;
                    string inner = text.Substring(start + 1, (closed ? i - 1 : i) - start - 1);
                    var bn = new WordNote { Start = start, Length = i - start, Word = text.Substring(start, i - start), Status = (WordStatus)(-1) };
                    result.Words.Add(bn);
                    var bt = new Tok { Text = "[" + Spaces(Fold(inner)).Replace(" :", ":").Replace(": ", ":") + "]", Note = bn };
                    toks.Add(bt);
                    if (!closed) Mark(bt, WordStatus.Wrong, "the closing ] is missing");
                    continue;
                }
                if (IsQuote(c))
                {
                    // "bronze axe": a game item by its in-game name, one token whatever it contains.
                    // Its text keeps the quotes so it can never be mistaken for a heraldic word.
                    i++;
                    while (i < text.Length && !IsQuote(text[i])) i++;
                    bool closed = i < text.Length;
                    if (closed) i++;
                    string inner = text.Substring(start + 1, (closed ? i - 1 : i) - start - 1);
                    var qn = new WordNote { Start = start, Length = i - start, Word = text.Substring(start, i - start), Status = (WordStatus)(-1) };
                    result.Words.Add(qn);
                    var qt = new Tok { Text = "\"" + Spaces(Fold(inner)) + "\"", Note = qn };
                    toks.Add(qt);
                    if (!closed) Mark(qt, WordStatus.Wrong, "the closing quote is missing");
                    continue;
                }
                if (IsWordChar(c))
                {
                    while (i < text.Length && IsWordChar(text[i])) i++;
                }
                else
                {
                    if (c == ',' || c == ';') comma = true;
                    i++; // punctuation: one character, always fine
                    result.Words.Add(new WordNote { Start = start, Length = 1, Word = text.Substring(start, 1), Status = WordStatus.Correct });
                    continue;
                }
                string raw = text.Substring(start, i - start).Trim('\'', '-');
                if (raw.Length == 0) continue;
                var note = new WordNote { Start = start, Length = i - start, Word = text.Substring(start, i - start), Status = (WordStatus)(-1) };
                result.Words.Add(note);
                toks.Add(new Tok { Text = Fold(raw), Note = note, AfterComma = comma });
                comma = false;
            }
        }

        private static bool IsQuote(char c) => c == '"' || c == '\u201C' || c == '\u201D';

        private static bool IsBracketed(string t) => t != null && t.Length >= 2 && t[0] == '[';

        private static bool IsQuoted(string t) => t != null && t.Length >= 2 && t[0] == '"';

        private static string Spaces(string s) => string.Join(" ", s.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries));

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '\'' || c == '#' || c == '’';

        /// <summary>Lower case, accents removed ("contourné" = "contourne"), curly apostrophes straightened.</summary>
        private static string Fold(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant().Replace('’', '\'').Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString();
        }

        private bool AtEnd => pos >= toks.Count;
        private string Cur => pos < toks.Count ? toks[pos].Text : null;
        private string Peek(int k) => pos + k < toks.Count ? toks[pos + k].Text : null;
        private Tok CurTok => pos < toks.Count ? toks[pos] : null;

        private void Mark(Tok t, WordStatus s, string msg = null)
        {
            if (t == null) return;
            t.Note.Status = s;
            t.Note.Message = msg;
        }

        private Tok Take(WordStatus s = WordStatus.Correct, string msg = null)
        {
            var t = CurTok;
            Mark(t, s, msg);
            pos++;
            return t;
        }

        private void SkipFillers()
        {
            while (!AtEnd && Lexicon.Fillers.Contains(Cur) && !IsReference())
                Take();
        }

        // ------------------------------------------------------------------ top level

        private Blazon Run()
        {
            if (toks.Count > 0)
            {
                result.Field = ParseField();
                while (!AtEnd)
                    ParseObject();
            }

            foreach (var o in awaitingTincture)
            {
                result.Messages.Add($"The {o.Kind.ToString().ToLowerInvariant()} needs a tincture.");
                result.Ordinaries.Remove(o);
            }

            foreach (var t in toks)
                if ((int)t.Note.Status == -1)
                    Mark(t, WordStatus.Wrong, "not understood here");
            return result;
        }

        // ------------------------------------------------------------------ field

        private Field ParseField()
        {
            while (!AtEnd)
            {
                SkipFillers();
                if (AtEnd) break;
                string w = Cur;

                if (w == "party" || w == "parted")
                {
                    Take();
                    continue; // "per" must follow
                }

                if (w == "per")
                    return ParsePartition();

                if (w == "quarterly" || w == "quartered")
                {
                    var q = Take(w == "quarterly" ? WordStatus.Correct : WordStatus.Passable,
                        w == "quarterly" ? null : "the heraldic term is 'quarterly'");
                    if (IsQuarteringOfArms())
                    {
                        Mark(q, WordStatus.Unsupported, "quartering separate arms (1st and 4th ...) isn't supported yet");
                        while (!AtEnd) Take(WordStatus.Unsupported);
                        return null;
                    }
                    return TwoTinctureField(new Field { Partition = PartitionKind.Quarterly, Line = TryLine() }, q);
                }

                if (Lexicon.Variations.TryGetValue(w, out var v))
                {
                    var vt = Take(v.PassableNote == null ? WordStatus.Correct : WordStatus.Passable, v.PassableNote);
                    var field = new Field { Variation = v.Kind, Count = v.DefaultCount };
                    if (v.Kind == VariationKind.Bendy && Cur == "sinister")
                    {
                        Take();
                        field.Variation = VariationKind.BendySinister;
                    }
                    field.Line = TryLine();
                    if (Cur == "of" && Peek(1) != null && (Lexicon.Numbers.ContainsKey(Peek(1)) || IsDigits(Peek(1))))
                    {
                        Take();
                        var nt = CurTok;
                        int n = TakeNumber();
                        if (v.Kind == VariationKind.Chequy || v.Kind == VariationKind.Lozengy)
                            Mark(nt, WordStatus.Passable, "chequy and lozengy aren't counted; the number is ignored");
                        else
                        {
                            // Out of range: say so, but draw the nearest allowed count so the result still looks intended
                            int c = Math.Max(2, Math.Min(16, n));
                            if (v.Kind == VariationKind.Gyronny && c % 2 == 1) c++;
                            if (c != n)
                                Mark(nt, WordStatus.Wrong, v.Kind == VariationKind.Gyronny && n >= 2 && n <= 16
                                    ? $"gyronny needs an even number of pieces; drawn as {c}"
                                    : $"between two and sixteen pieces; drawn as {c}");
                            field.Count = c;
                        }
                    }
                    else if (Cur == "of" && Peek(1) != null && !Lexicon.Tinctures.ContainsKey(Peek(1)))
                    {
                        Take();
                        Take(WordStatus.Wrong, "expected a number of pieces, e.g. 'of eight'");
                    }
                    if (field.Line == LineKind.Straight) field.Line = TryLine();
                    return TwoTinctureField(field, vt);
                }

                if (Lexicon.KnownWrong.TryGetValue(w, out var why) && why != null)
                {
                    Take(WordStatus.Wrong, why);
                    continue;
                }

                var before = CurTok;
                if (TryTincture(out var tinc, allowProper: false, allowCounterchanged: false))
                {
                    var f = new Field();
                    f.Tinctures.Add(tinc);
                    return f;
                }
                if (pos > 0 && CurTok != before) continue; // TryTincture consumed a bad word

                if (StartsObject())
                {
                    result.Messages.Add("A blazon starts with the field, e.g. 'Azure, a bend or'.");
                    Mark(CurTok, WordStatus.Wrong, "start with the field's tincture, e.g. 'Azure, ...'");
                    return null;
                }

                MarkUnknown(Take(WordStatus.Wrong));
            }
            return null;
        }

        private Field ParsePartition()
        {
            var per = Take();
            var field = new Field();
            if (Cur == "bend")
            {
                Take();
                field.Partition = PartitionKind.Bend;
                if (Cur == "sinister") { Take(); field.Partition = PartitionKind.BendSinister; }
            }
            else if (Cur != null && Lexicon.PartitionWords.TryGetValue(Cur, out var kind))
            {
                Take();
                field.Partition = kind;
            }
            else if (Cur == "pall")
            {
                Take(WordStatus.Unsupported, "per pall (three tinctures) isn't supported yet");
                while (!AtEnd && !StartsObject()) Take(WordStatus.Unsupported);
                return null;
            }
            else
            {
                Mark(per, WordStatus.Wrong, "per what? pale, fess, bend, bend sinister, chevron, saltire or cross");
                field.Partition = PartitionKind.Pale;
            }
            field.Line = TryLine();
            return TwoTinctureField(field, per);
        }

        private Field TwoTinctureField(Field field, Tok owner)
        {
            SkipFillers();
            if (!TryTincture(out var a, false, false))
            {
                Mark(owner, WordStatus.Wrong, "needs two tinctures, e.g. 'azure and or'");
                return field;
            }
            field.Tinctures.Add(a);
            SkipFillers();
            if (Cur == "and") Take();
            if (!TryTincture(out var b, false, false))
            {
                Mark(owner, WordStatus.Wrong, "needs a second tincture");
                field.Tinctures.Add(a);
                return field;
            }
            field.Tinctures.Add(b);
            return field;
        }

        private bool IsQuarteringOfArms()
        {
            string w = Cur;
            return w != null && (Lexicon.Ordinals.ContainsKey(w) || IsDigits(w) || w == "i" || w == "1st");
        }

        // ------------------------------------------------------------------ ordinaries and charges

        private bool StartsObject()
        {
            string w = Cur;
            if (w == null) return false;
            if (Lexicon.Ordinaries.ContainsKey(w) || Lexicon.Charges.ContainsKey(w) || IsQuoted(w) || IsBracketed(w)) return true;
            if (w == "on" || w == "between") return true;
            if ((Lexicon.Numbers.ContainsKey(w) || IsDigits(w)) && Peek(1) != null &&
                (Lexicon.Ordinaries.ContainsKey(Peek(1)) || Lexicon.Charges.ContainsKey(Peek(1))))
                return true;
            return false;
        }

        private void ParseObject()
        {
            SkipFillers();
            if (AtEnd) return;
            string w = Cur;

            if (w == "and") { Take(); return; }

            if (w == "on" || (w == "charged" && Peek(1) == "with"))
            {
                var onTok = Take();
                object host;
                if (w == "charged")
                {
                    Take();
                    host = result.Ordinaries.Count > 0 ? result.Ordinaries[result.Ordinaries.Count - 1] : null;
                    if (host == null) Mark(onTok, WordStatus.Wrong, "charged? 'charged with' follows an ordinary, e.g. 'a chief azure charged with three mullets or'");
                }
                else if (Cur == "the" && Peek(1) != null && Lexicon.Ordinaries.TryGetValue(Peek(1), out var named) &&
                         result.Ordinaries.Exists(o => o.Kind == named.Kind))
                {
                    // "on the chief ...": the one already described
                    Take();
                    Take();
                    host = result.Ordinaries.FindLast(o => o.Kind == named.Kind);
                }
                else
                {
                    SkipFillers();
                    host = ParseOrdinaryOrCharge();
                }
                if (host is Ordinary ord)
                {
                    SkipFillers();
                    if (!AtEnd && Cur != "and")
                    {
                        var c = ParseOrdinaryOrCharge();
                        if (c is Charge ch)
                        {
                            result.Charges.Remove(ch);
                            ch.Placement = Placement.On;
                            ch.Host = ord;
                            ord.Charges.Add(ch);
                            if (ord.Kind == OrdinaryKind.Bordure && ch.Drawable && ch.Count < 4)
                                Mark(onTok, WordStatus.Passable, "charges on a bordure usually come in eights; drawn spaced around it");
                        }
                    }
                    else Mark(onTok, WordStatus.Wrong, "on the " + ord.Kind.ToString().ToLowerInvariant() + ", what?");
                }
                else if (host is Charge)
                    Mark(onTok, WordStatus.Unsupported, "charges on charges aren't supported yet; drawn separately");
                lastCharge = null;
                return;
            }

            if (w == "between")
            {
                var betweenTok = Take();
                Ordinary host = result.Ordinaries.Count > 0 ? result.Ordinaries[result.Ordinaries.Count - 1] : null;
                if (host == null)
                    Mark(betweenTok, WordStatus.Wrong, "between what? 'between' follows an ordinary, e.g. 'a chevron between three mullets'");
                else if (host.Kind == OrdinaryKind.Chief || host.Kind == OrdinaryKind.Bordure)
                    Mark(betweenTok, WordStatus.Wrong, $"nothing can be between a {host.Kind.ToString().ToLowerInvariant()}; just list the charges");
                SkipFillers();
                if (ParseOrdinaryOrCharge() is Charge c && host != null)
                {
                    c.Placement = Placement.Between;
                    c.Host = host;
                }
                return;
            }

            if (w == "in" && Peek(1) != null && (IsPlacementWord(Peek(1)) || IsPointStart(Peek(1), Peek(2))))
            {
                // "a mullet in dexter chief" places the mullet; ", in dexter chief a mullet" places what follows
                bool forNext = Take().AfterComma;
                if (Cur == "the") Take();
                var placeTok = Take();
                var place = PointFor(placeTok) ?? PlacementFor(placeTok.Text);
                if (place == Placement.Bend && Cur == "sinister")
                {
                    Take();
                    place = Placement.BendSinister;
                }
                if (place == Placement.None)
                    Mark(placeTok, WordStatus.Unsupported, $"'in {placeTok.Text}' isn't drawn yet; the charges keep their usual arrangement");
                if (lastCharge != null && lastCharge.Placement == Placement.None && !(forNext && !AtEnd))
                    lastCharge.Placement = place;
                else
                    pendingPlacement = place;
                return;
            }

            // "over all" / "overall": a charge laid over the ordinaries, which is how unplaced charges are drawn anyway
            if (w == "overall" || (w == "over" && Peek(1) == "all"))
            {
                Take();
                if (w == "over") Take();
                return;
            }

            if (w == "counterchanged" || w == "cotised")
            {
                Take(WordStatus.Unsupported, $"'{w}' here isn't supported yet");
                return;
            }

            if (Lexicon.KnownWrong.TryGetValue(w, out var why) && why != null)
            {
                Take(WordStatus.Wrong, why);
                return;
            }

            ParseOrdinaryOrCharge();
        }

        /// <summary>"in [the] dexter chief", "in sinister", "in fess point", "in honour point"...</summary>
        private static bool IsPointStart(string w, string next) =>
            w == "dexter" || w == "sinister" || w == "middle" || w == "honour" || w == "honor" || w == "nombril" ||
            (w == "the" && next != null && (IsPlacementWord(next) || IsPointStart(next, null))) ||
            ((w == "fess" || w == "fesse") && next == "point");

        /// <summary>A point of the shield starting with this word (more words are taken as needed), or null.</summary>
        private Placement? PointFor(Tok first)
        {
            switch (first.Text)
            {
                case "dexter":
                case "sinister":
                {
                    bool dexter = first.Text == "dexter";
                    if (Cur == "chief") { Take(); return dexter ? Placement.DexterChief : Placement.SinisterChief; }
                    if (Cur == "base") { Take(); return dexter ? Placement.DexterBase : Placement.SinisterBase; }
                    if (Cur == "flank" || Cur == "side") Take();
                    return dexter ? Placement.DexterFlank : Placement.SinisterFlank;
                }
                case "middle":
                    if (Cur == "chief") { Take(); return Placement.MiddleChief; }
                    if (Cur == "base") { Take(); return Placement.MiddleBase; }
                    return Placement.FessPoint;
                case "honour":
                case "honor":
                    if (Cur == "point") Take();
                    return Placement.HonourPoint;
                case "nombril":
                    if (Cur == "point") Take();
                    return Placement.NombrilPoint;
                case "fess":
                case "fesse":
                    if (Cur != "point") return null;
                    Take();
                    return Placement.FessPoint;
            }
            return null;
        }

        private static bool IsPlacementWord(string w) =>
            w == "chief" || w == "base" || w == "fess" || w == "pale" || w == "bend" || w == "saltire" ||
            w == "cross" || w == "orle" || w == "pile" || w == "chevron" || w == "fesse";

        private static Placement PlacementFor(string w)
        {
            switch (w)
            {
                case "chief": return Placement.Chief;
                case "base": return Placement.Base;
                case "fess": case "fesse": return Placement.Fess;
                case "pale": return Placement.Pale;
                case "bend": return Placement.Bend;
                case "saltire": return Placement.Saltire;
                case "cross": return Placement.Cross;
                case "orle": return Placement.Orle;
                default: return Placement.None; // pile, chevron: not drawn yet
            }
        }

        /// <summary>Parses "[count] noun [line|attitudes] tincture [details]". Returns an Ordinary, a Charge, or null.</summary>
        private Tincture earlyTincture;

        private object ParseOrdinaryOrCharge()
        {
            earlyTincture = null;
            Tok countTok = null;
            int count = 1;
            bool hadCount = false;
            if (Cur != null && (Lexicon.Numbers.ContainsKey(Cur) || IsDigits(Cur)))
            {
                countTok = CurTok;
                count = TakeNumber();
                hadCount = countTok.Text != "a" && countTok.Text != "an";
            }

            if (AtEnd)
            {
                Mark(countTok, WordStatus.Wrong, "a number of what?");
                return null;
            }

            string w = Cur;

            // A game item in quotes: "a \"bronze axe\" or"
            if (IsQuoted(w))
                return QuotedItem(countTok, count);

            // A charge from a charge pack: "three [wolves] rampant sable"
            if (IsBracketed(w))
                return PackCharge(countTok, count);

            // Plain-English order: "a gold bend". Blazon puts the tincture after; accept it but say so.
            Tincture early = null;
            if ((Lexicon.Tinctures.ContainsKey(w) || (w.Length == 7 && w[0] == '#')) && Peek(1) != null &&
                (Lexicon.Ordinaries.ContainsKey(Peek(1)) || Lexicon.Charges.ContainsKey(Peek(1))))
            {
                var tt = CurTok;
                TryTincture(out early, false, false);
                Mark(tt, WordStatus.Wrong, $"in blazon the tincture comes after: 'a {Peek(0)} {(early != null ? early.Name : w)}'");
                w = Cur;
                earlyTincture = early;
            }

            // "cross crosslet", "cross moline": a cross-shaped charge, not the ordinary
            if ((w == "cross" || w == "crosses") && Peek(1) != null && Lexicon.CrossCharges.Contains(Peek(1)))
            {
                var c = new Charge { Count = count, Noun = Lexicon.CrossName(Peek(1)) };
                c.Drawable = Lexicon.DrawnCharges.Contains(c.Noun);
                var crossTok = Take(c.Drawable ? WordStatus.Correct : WordStatus.NotDrawnYet, c.Drawable ? null : NeedsArt);
                Take(c.Drawable ? WordStatus.Correct : WordStatus.NotDrawnYet);
                if (count > 1 && w == "cross")
                    Mark(crossTok, WordStatus.Passable, "use the plural for more than one: 'crosses " + toks[pos - 1].Text + "'");
                if (Cur == "fitchy" || Cur == "fitchee")
                    Take(WordStatus.NotDrawnYet, "the pointed foot isn't drawn yet; drawn as a plain " + c.Noun);
                return FinishCharge(c, null);
            }

            if (Lexicon.Ordinaries.TryGetValue(w, out var oe) && !(count > 1 && (w == "saltire" || w == "cross")))
                return FinishOrdinary(oe, countTok, count, hadCount);

            // Possessive heads: "a stag's head caboshed"
            if ((w.EndsWith("'s") || w.EndsWith("s'")) && (Peek(1) == "head" || Peek(1) == "heads"))
            {
                string animal = w.Substring(0, w.Length - 2); // "stag's" and "stags'" both lose two characters
                var c = new Charge { Count = count, Noun = animal + "'s head" };
                SetHeadArt(c, w.EndsWith("s'") ? animal + "s" : animal, animal);
                Take(c.Drawable ? WordStatus.Correct : WordStatus.NotDrawnYet, c.Drawable ? null : NeedsArt);
                Take(c.Drawable ? WordStatus.Correct : WordStatus.NotDrawnYet);
                return FinishCharge(c, null);
            }

            if (Lexicon.Charges.TryGetValue(w, out var ce) || (count > 1 && (w == "saltire" || w == "cross")))
            {
                if (ce == null) ce = new Lexicon.ChargeEntry { Singular = w };
                Lexicon.NounIcons.TryGetValue(ce.Singular, out string art);
                bool drawable = Lexicon.DrawnCharges.Contains(ce.Singular) || art != null;
                var nounTok = Take(drawable ? WordStatus.Correct : WordStatus.NotDrawnYet, drawable ? null : NeedsArt);
                if (ce.PassableNote != null)
                    Mark(nounTok, WordStatus.Passable, ce.PassableNote);
                else if (count > 1 && !ce.Plural && Lexicon.Charges.ContainsKey(w))
                    Mark(nounTok, WordStatus.Passable, "use the plural for more than one");
                else if (count == 1 && ce.Plural && countTok != null)
                    Mark(nounTok, WordStatus.Passable, "use the singular for one");
                if (count > 20)
                {
                    Mark(countTok, WordStatus.Wrong, "at most twenty");
                    count = 20;
                }
                var c = new Charge { Count = count, Noun = ce.Singular, Drawable = drawable, ArtKey = art };
                if (Cur == "head" || Cur == "heads") // "three stags' heads" (the apostrophe is trimmed off)
                {
                    c.Noun += "'s head";
                    SetHeadArt(c, w, ce.Singular);
                    Take(c.Drawable ? WordStatus.Correct : WordStatus.NotDrawnYet);
                    Mark(nounTok, c.Drawable ? WordStatus.Correct : WordStatus.NotDrawnYet, c.Drawable ? null : NeedsArt);
                }
                if (w == "rune" && Cur != null && !Lexicon.Tinctures.ContainsKey(Cur))
                    c.Noun = "rune " + Take(WordStatus.NotDrawnYet).Text; // "a rune Algiz sable"
                return FinishCharge(c, ce.ImpliedTincture);
            }

            if (Lexicon.Tinctures.ContainsKey(w))
            {
                Take(WordStatus.Wrong, "a tincture here has nothing to colour; did you mean 'and'?");
                return null;
            }

            // A trophy by its internal name without quotes still works, but quotes are the way to name game items
            if (w.Length > 6 && w.StartsWith("trophy") && IsLetters(w))
            {
                string asWritten = CurTok.Note.Word;
                Take(WordStatus.Passable, $"write game items in quotes: \"{asWritten}\"");
                var c = new Charge { Count = Math.Min(count, 20), Noun = w, ArtKey = w, Drawable = true };
                return FinishCharge(c, null);
            }

            // Unknown noun
            var bad = Take(WordStatus.Wrong);
            MarkUnknown(bad, countTok != null);
            Mark(countTok, WordStatus.Correct);
            // Skip what belongs to it (attitudes, tincture) so they aren't reported as more errors
            while (!AtEnd && (Lexicon.Attitudes.Contains(Cur) || Lexicon.ChargeDetails.Contains(Cur))) Take();
            if (Cur != null && Lexicon.Tinctures.ContainsKey(Cur)) Take();
            return null;
        }

        /// <summary>
        ///     A pack charge. Its attitudes pick the drawing ("[wolf] rampant" vs "[wolf] passant"), so the name is
        ///     resolved after they're read. Unknown names are marked, and still drawn as the fallback roundel so
        ///     players without the pack see that something belongs there.
        /// </summary>
        private Charge PackCharge(Tok countTok, int count)
        {
            var tok = CurTok;
            string name = tok.Text.Substring(1, tok.Text.Length - 2);
            pos++;
            if (tok.Note.Status == WordStatus.Wrong) return null; // unclosed bracket, already explained
            if (name.Length == 0)
            {
                Mark(tok, WordStatus.Wrong, "empty brackets: put a pack charge's name inside, e.g. [wolf]");
                return null;
            }
            if (count > 20)
            {
                Mark(countTok, WordStatus.Wrong, "at most twenty");
                count = 20;
            }
            Mark(tok, WordStatus.Correct);
            var c = new Charge { Count = count, Noun = name, PackName = name, Drawable = true };
            attitudeToks.Clear();
            FinishCharge(c, null);

            var packs = Lexicon.Packs;
            string unmatched = null;
            c.ArtKey = packs?.Resolve(name, c.Attitudes, out unmatched);
            if (c.ArtKey == null)
            {
                string s = packs?.Suggest(name);
                Mark(tok, WordStatus.Wrong, s != null
                    ? $"no charge [{name}] in your charge packs: did you mean [{s}]?"
                    : $"no charge [{name}] in your charge packs; shown as a placeholder roundel");
                return c;
            }
            foreach (var at in attitudeToks)
                if (at.Text == unmatched)
                    Mark(at, WordStatus.NotDrawnYet, $"no {unmatched} drawing of [{name}] in your packs; using the one there is");
                else if (!Lexicon.IsFlip(at.Text))
                    Mark(at, WordStatus.Correct);
            return c;
        }

        private readonly List<Tok> attitudeToks = new List<Tok>();

        /// <summary>What a picture charge's mask adds: disarmed (no claws), morné (no claws or tongue), plain (no mask).</summary>
        private bool TryMaskWord(Charge c)
        {
            if (Cur != "disarmed" && Cur != "morne" && Cur != "plain") return false;
            var mt = Take();
            if (c.ArtKey == null && c.PackName == null)
                Mark(mt, WordStatus.Passable, $"'{mt.Text}' only changes picture charges; a {c.Noun} has no claws or tongue to leave off");
            else c.Mask = mt.Text == "disarmed" ? MaskMode.Disarmed : mt.Text == "morne" ? MaskMode.Morne : MaskMode.Plain;
            return true;
        }

        /// <summary>A quoted game item: checked against the game's items when a world is loaded.</summary>
        private Charge QuotedItem(Tok countTok, int count)
        {
            var tok = CurTok;
            string name = tok.Text.Substring(1, tok.Text.Length - 2);
            pos++;
            if (tok.Note.Status == WordStatus.Wrong) return null; // unclosed quote, already explained
            if (name.Length == 0)
            {
                Mark(tok, WordStatus.Wrong, "empty quotes: put a game item's name inside, e.g. \"wood\"");
                return null;
            }
            string key = "item:" + name;
            var items = Lexicon.GameItems;
            if (items != null && items.Ready)
            {
                key = items.Find(name);
                if (key == null)
                {
                    string s = items.Suggest(name);
                    Mark(tok, WordStatus.Wrong, s != null
                        ? $"no game item called \"{name}\": did you mean \"{s}\"?"
                        : $"no game item called \"{name}\" (use the name its tooltip shows)");
                    return null;
                }
            }
            Mark(tok, WordStatus.Correct);
            if (count > 20)
            {
                Mark(countTok, WordStatus.Wrong, "at most twenty");
                count = 20;
            }
            return FinishCharge(new Charge { Count = count, Noun = name, ArtKey = key, Drawable = true }, null);
        }

        /// <summary>"a wolf's head": the trophy icon for that animal, if there is one.</summary>
        private static void SetHeadArt(Charge c, string asTyped, string singular)
        {
            if (Lexicon.HeadIcons.TryGetValue(asTyped, out var key) || Lexicon.HeadIcons.TryGetValue(singular, out key))
            {
                c.ArtKey = key;
                c.Drawable = true;
            }
            else c.Drawable = false;
        }

        private static bool IsLetters(string s)
        {
            foreach (char ch in s) if (ch < 'a' || ch > 'z') return false;
            return true;
        }

        private Ordinary FinishOrdinary(Lexicon.OrdinaryEntry oe, Tok countTok, int count, bool hadCount)
        {
            lastCharge = null;
            var nounTok = Take(oe.PassableNote == null ? WordStatus.Correct : WordStatus.Passable, oe.PassableNote);
            var o = new Ordinary { Kind = oe.Kind, Count = count, Diminutive = oe.Diminutive };

            if (oe.Plural && !hadCount)
            {
                Mark(nounTok, WordStatus.Passable, "say how many, e.g. 'two " + nounTok.Text + "'");
                o.Count = 2;
            }
            else if (oe.SingleOnly && count > 1)
            {
                Mark(countTok, WordStatus.Wrong, "there can only be one " + nounTok.Text);
                o.Count = 1;
            }
            else if (!oe.Plural && count > 1)
            {
                Mark(nounTok, WordStatus.Passable, "use the plural for more than one");
                o.Diminutive = true;
            }
            if (o.Kind == OrdinaryKind.Pile && o.Count > 3)
            {
                Mark(countTok, WordStatus.Wrong, "at most three piles; drawn as three");
                o.Count = 3;
            }
            else if (o.Count > 9)
            {
                Mark(countTok, WordStatus.Wrong, "at most nine; drawn as nine");
                o.Count = 9;
            }

            if ((o.Kind == OrdinaryKind.Bend) && Cur == "sinister")
            {
                Take();
                o.Kind = OrdinaryKind.BendSinister;
            }
            o.Line = TryLine();
            if (o.Kind == OrdinaryKind.Bend && o.Line == LineKind.Straight && Cur == "sinister")
            {
                Take();
                o.Kind = OrdinaryKind.BendSinister;
            }

            ParseStyle(o.Style, beforeTincture: true);
            SkipFillers();
            if (earlyTincture != null)
            {
                o.Tincture = earlyTincture;
                ParseStyle(o.Style, beforeTincture: false);
                result.Ordinaries.Add(o);
                return o;
            }
            if (!TryTincture(out var t, allowProper: false, allowCounterchanged: true))
            {
                if (Cur == "between")
                {
                    // "a chevron between three mullets argent": the last tincture covers both
                    awaitingTincture.Add(o);
                    result.Ordinaries.Add(o);
                    return o;
                }
                Mark(nounTok, WordStatus.Wrong, "needs a tincture, e.g. 'a " + nounTok.Text + " or'");
                return o;
            }
            o.Tincture = t;
            ParseStyle(o.Style, beforeTincture: false);
            result.Ordinaries.Add(o);
            return o;
        }

        /// <summary>
        ///     "voided", "voided azure", "fimbriated or". Before the main tincture ("a cross voided gules") the
        ///     next tincture is the object's own, so a bare "voided" shows what's beneath; after it ("a mullet or
        ///     voided azure") a tincture is the colour inside.
        /// </summary>
        private void ParseStyle(Style style, bool beforeTincture)
        {
            while (!AtEnd)
            {
                if (Cur == "voided")
                {
                    var vt = Take();
                    style.Voided = true;
                    if (!beforeTincture && Cur == "of" && Peek(1) == "the" && Peek(2) == "field")
                    {
                        Take(); Take(); Take(); // "voided of the field" = shows the field, the default
                    }
                    else if (!beforeTincture && TryTincture(out var inner, false, false))
                        style.VoidTincture = inner;
                    continue;
                }
                if (Cur == "fimbriated")
                {
                    var ft = Take();
                    if (TryTincture(out var edge, false, false)) style.Fimbriation = edge;
                    else Mark(ft, WordStatus.Wrong, "fimbriated in what? e.g. 'fimbriated argent'");
                    continue;
                }
                break;
            }
        }

        /// <summary>"two and one", "3 and 2 and 1": explicit rows. Returns false (consuming nothing) if it isn't one.</summary>
        private bool TryRows(Charge c)
        {
            int start = pos;
            var rows = new List<int>();
            var first = CurTok;
            while (!AtEnd && Cur != "a" && Cur != "an" && (Lexicon.Numbers.ContainsKey(Cur) || IsDigits(Cur)))
            {
                int n;
                if (!Lexicon.Numbers.TryGetValue(Cur, out n) && !int.TryParse(Cur, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) break;
                rows.Add(n);
                pos++;
                if (Cur == "and" && Peek(1) != null && Peek(1) != "a" && Peek(1) != "an" &&
                    (Lexicon.Numbers.ContainsKey(Peek(1)) || IsDigits(Peek(1))))
                    pos++;
                else break;
            }
            // A number followed by a noun ("and two roundels") starts the next object, not a row list
            bool nounNext = Cur != null && (Lexicon.Ordinaries.ContainsKey(Cur) || Lexicon.Charges.ContainsKey(Cur) || Cur == "cross" || Cur == "crosses");
            if (rows.Count < 2 || nounNext)
            {
                pos = start;
                return false;
            }
            for (int i = start; i < pos; i++) Mark(toks[i], WordStatus.Correct);
            int sum = 0;
            foreach (int r in rows) sum += r;
            bool sane = true;
            foreach (int r in rows) if (r < 1 || r > 6) sane = false;
            if (sum != c.Count || !sane)
            {
                Mark(first, WordStatus.Wrong, sum != c.Count
                    ? $"these rows add up to {sum}, but there are {c.Count}"
                    : "rows of one to six");
                return true;
            }
            c.Rows = rows.ToArray();
            return true;
        }

        private Charge FinishCharge(Charge c, string impliedTincture)
        {
            if (pendingPlacement != Placement.None)
            {
                c.Placement = pendingPlacement;
                pendingPlacement = Placement.None;
            }

            // Attitudes, directions, and mullet details
            while (!AtEnd)
            {
                if (Cur == "of" && Peek(2) == "points" && Peek(1) != null && (Lexicon.Numbers.ContainsKey(Peek(1)) || IsDigits(Peek(1))))
                {
                    Take();
                    var nt = CurTok;
                    int n = TakeNumber();
                    Take();
                    if (c.Noun != "mullet")
                        Mark(nt, WordStatus.Wrong, "only mullets have a number of points");
                    else if (n < 4 || n > 8)
                        Mark(nt, WordStatus.Wrong, "four to eight points");
                    else c.Points = n;
                    continue;
                }
                if (TryMaskWord(c)) continue;
                if (Cur == "pierced")
                {
                    var pt = Take();
                    if (c.Noun == "mullet") c.Pierced = true;
                    else Mark(pt, WordStatus.Unsupported, "only pierced mullets are drawn so far");
                    continue;
                }
                if (Cur == "voided" || Cur == "fimbriated")
                {
                    ParseStyle(c.Style, beforeTincture: true);
                    continue;
                }
                if (TryRows(c)) continue;
                if (Lexicon.Attitudes.Contains(Cur))
                {
                    attitudeToks.Add(CurTok);
                    string a = Cur;
                    // Flips work on any drawn charge; trophy heads already face the front (caboshed, affronté)
                    bool drawnHere = c.Drawable && (Lexicon.IsFlip(a) || (a == "couped" && c.ArtKey == null) ||
                                                    (c.ArtKey != null && (a == "caboshed" || a == "affronte")));
                    c.Attitudes.Add(Take(drawnHere ? WordStatus.Correct : WordStatus.NotDrawnYet,
                        drawnHere || !c.Drawable ? null : $"'{a}' isn't drawn for a {c.Noun}").Text);
                    continue;
                }
                if (Cur == "facing" && (Peek(1) == "left" || Peek(1) == "sinister"))
                {
                    Take(WordStatus.Passable, "the heraldic term is 'contourné'");
                    Take(WordStatus.Passable);
                    c.Attitudes.Add("contourne");
                    continue;
                }
                break;
            }

            if (earlyTincture != null)
            {
                c.Tincture = earlyTincture;
            }
            else if (impliedTincture != null)
            {
                c.Tincture = new Tincture { Kind = TinctureKind.Metal, Name = impliedTincture };
            }
            else
            {
                SkipFillers();
                var tincTok = CurTok;
                if (!TryTincture(out var t, allowProper: true, allowCounterchanged: true))
                {
                    if (c.ArtKey != null && pos > 0 && toks[pos - 1].Note.Status == WordStatus.Correct)
                        Mark(toks[pos - 1], WordStatus.Passable, "no tincture given: drawn in its own colours (blazon this as 'proper')");
                    else if (c.ArtKey == null)
                        result.Messages.Add($"'{c.Noun}' needs a tincture.");
                }
                else if (t.Kind == TinctureKind.Proper && Lexicon.IsGeometric(c.Noun))
                    Mark(tincTok, WordStatus.Wrong, $"a {c.Noun} has no natural colour; give it a tincture");
                else c.Tincture = t;
            }
            ParseStyle(c.Style, beforeTincture: false);
            while (TryMaskWord(c)) { }
            TryRows(c);
            if (c.Rows != null && c.Placement != Placement.None && c.Placement != Placement.Between && c.Placement != Placement.On)
                c.Rows = null; // "in pale" wins over a row list
            if (c.Tincture != null && c.Tincture.Kind != TinctureKind.Proper)
            {
                foreach (var o in awaitingTincture) o.Tincture = c.Tincture;
                awaitingTincture.Clear();
            }

            // "armed and langued gules", "armed or langued gules": the details named so far share the next tincture.
            // Picture charges draw armed/langued from their mask; other details are understood but not drawn.
            var pending = new List<Tok>();
            while (!AtEnd && Lexicon.ChargeDetails.Contains(Cur))
            {
                var dt = CurTok;
                bool drawn = c.ArtKey != null || c.PackName != null;
                bool layer = dt.Text == "langued" || Lexicon.IsArmedWord(dt.Text);
                Take(drawn && layer ? WordStatus.Correct : WordStatus.NotDrawnYet,
                    drawn && !layer ? $"'{dt.Text}' isn't drawn yet" : null);
                pending.Add(dt);
                if (Cur == "and" && Peek(1) != null && Lexicon.ChargeDetails.Contains(Peek(1))) { Take(); continue; }
                if (TryTincture(out var dtTinc, true, false))
                    foreach (var p in pending)
                    {
                        if (p.Text == "langued") c.Langued = dtTinc;
                        else if (Lexicon.IsArmedWord(p.Text)) c.Armed = dtTinc;
                    }
                else
                    Mark(dt, WordStatus.Wrong, $"{dt.Text} in what? e.g. '{dt.Text} gules'");
                pending.Clear();
            }

            result.Charges.Add(c);
            lastCharge = c;
            return c;
        }

        // ------------------------------------------------------------------ tinctures, lines, numbers

        private bool IsReference() =>
            Cur == "of" && Peek(1) == "the" && Peek(2) != null && (Lexicon.Ordinals.ContainsKey(Peek(2)) || Peek(2) == "field");

        /// <summary>
        ///     Reads a tincture if the current word is one. Returns false without consuming anything if the word
        ///     clearly belongs elsewhere; consumes and marks a word that is a near-miss tincture.
        /// </summary>
        private bool TryTincture(out Tincture t, bool allowProper, bool allowCounterchanged)
        {
            t = null;
            string w = Cur;
            if (w == null) return false;

            if (Lexicon.Tinctures.TryGetValue(w, out var e))
            {
                var tt = Take(e.PassableNote == null ? WordStatus.Correct : WordStatus.Passable, e.PassableNote);
                t = new Tincture { Kind = e.Kind, Name = e.Canonical };
                tt.Note.Tincture = t;
                mentioned.Add(t);
                return true;
            }

            if (w.Length == 7 && w[0] == '#' && IsHex(w.Substring(1)))
            {
                var ht = Take();
                int rgb = int.Parse(w.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                t = new Tincture { Kind = TinctureKind.Hex, Name = w, Hex = new Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) };
                ht.Note.Tincture = t;
                mentioned.Add(t);
                return true;
            }

            if (w == "proper")
            {
                if (!allowProper)
                {
                    Take(WordStatus.Wrong, "'proper' (natural colours) is only for charges");
                    return false;
                }
                Take();
                t = new Tincture { Kind = TinctureKind.Proper, Name = "proper" };
                return true;
            }

            if (w == "counterchanged")
            {
                if (!allowCounterchanged)
                {
                    Take(WordStatus.Wrong, "the field itself can't be counterchanged");
                    return false;
                }
                Take();
                t = new Tincture { Kind = TinctureKind.Counterchanged, Name = "counterchanged" };
                return true;
            }

            if (IsReference())
            {
                var ofTok = Take();
                Take();
                var which = Take();
                Tincture target = null;
                if (which.Text == "field")
                {
                    if (result.Field != null && result.Field.Tinctures.Count == 1)
                        target = result.Field.Tinctures[0];
                    else
                        Mark(which, WordStatus.Wrong, "'of the field' only works when the field is one tincture");
                }
                else
                {
                    int n = Lexicon.Ordinals[which.Text];
                    var distinct = DistinctMentioned();
                    if (n == -1 && distinct.Count > 0) target = distinct[distinct.Count - 1];
                    else if (n >= 1 && n <= distinct.Count) target = distinct[n - 1];
                    else Mark(which, WordStatus.Wrong, $"only {distinct.Count} tincture(s) named so far");
                }
                if (target == null) return false;
                t = target;
                which.Note.Tincture = target;
                return true;
            }

            // A misspelt tincture: consume it as Wrong with a suggestion, but only if it looks like one.
            if (!Lexicon.Ordinaries.ContainsKey(w) && !Lexicon.Charges.ContainsKey(w) && !Lexicon.Numbers.ContainsKey(w) &&
                w != "and" && w != "on" && w != "between" && w != "in" && !Lexicon.Fillers.Contains(w))
            {
                string s = Lexicon.Suggest(w);
                if (s != null && Lexicon.Tinctures.ContainsKey(s))
                {
                    Take(WordStatus.Wrong, $"did you mean '{s}'?");
                    return false;
                }
            }
            return false;
        }

        /// <summary>Distinct tinctures in order of first mention ("of the first" counts tinctures, not words).</summary>
        private List<Tincture> DistinctMentioned()
        {
            var list = new List<Tincture>();
            foreach (var t in mentioned)
            {
                bool seen = false;
                foreach (var x in list) if (x.Name == t.Name) { seen = true; break; }
                if (!seen) list.Add(t);
            }
            return list;
        }

        private LineKind TryLine()
        {
            if (Cur != null && Lexicon.Lines.TryGetValue(Cur, out var le))
            {
                Take(le.PassableNote == null ? WordStatus.Correct : WordStatus.Passable, le.PassableNote);
                return le.Kind;
            }
            if (Cur == "raguly" || Cur == "dovetailed" || Cur == "potenty" || Cur == "rayonny" || Cur == "urdy")
            {
                Take(WordStatus.Unsupported, $"'{Cur}' lines aren't supported yet; drawn straight");
            }
            return LineKind.Straight;
        }

        /// <summary>Reads a number word or digits. Range checks are the caller's job (limits differ).</summary>
        private int TakeNumber()
        {
            var t = Take();
            if (Lexicon.Numbers.TryGetValue(t.Text, out int n)) return n;
            if (!int.TryParse(t.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1)
            {
                Mark(t, WordStatus.Wrong, "not a usable number");
                n = 1;
            }
            return n;
        }

        private void MarkUnknown(Tok t, bool asCharge = false)
        {
            if (t == null) return;
            string s = Lexicon.Suggest(t.Text);
            if (Lexicon.KnownWrong.TryGetValue(t.Text, out var why) && why != null)
                Mark(t, WordStatus.Wrong, why);
            else if (asCharge)
                Mark(t, WordStatus.Wrong, s != null
                    ? $"unknown charge '{t.Text}': did you mean '{s}'?"
                    : $"unknown charge '{t.Text}': misspelled, or not available on this server");
            else
                Mark(t, WordStatus.Wrong, s != null ? $"did you mean '{s}'?" : "not a heraldic term");
        }

        private static bool IsDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }

        private static bool IsHex(string s)
        {
            foreach (char c in s)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }
    }
}
