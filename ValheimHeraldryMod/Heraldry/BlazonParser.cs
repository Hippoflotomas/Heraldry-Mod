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
        }

        private readonly List<Tok> toks = new List<Tok>();
        private readonly Blazon result = new Blazon();
        private readonly List<Tincture> mentioned = new List<Tincture>(); // for "of the first" etc.
        private int pos;
        /// <summary>Ordinaries still waiting for a tincture given later ("a chevron between three mullets argent").</summary>
        private readonly List<Ordinary> awaitingTincture = new List<Ordinary>();

        public static Blazon Parse(string text) => new BlazonParser(text ?? "").Run();

        private BlazonParser(string text)
        {
            result.Text = text;
            Tokenize(text);
        }

        // ------------------------------------------------------------------ tokens

        private void Tokenize(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                int start = i;
                if (IsWordChar(c))
                {
                    while (i < text.Length && IsWordChar(text[i])) i++;
                }
                else
                {
                    i++; // punctuation: one character, always fine
                    result.Words.Add(new WordNote { Start = start, Length = 1, Word = text.Substring(start, 1), Status = WordStatus.Correct });
                    continue;
                }
                string raw = text.Substring(start, i - start).Trim('\'', '-');
                if (raw.Length == 0) continue;
                var note = new WordNote { Start = start, Length = i - start, Word = text.Substring(start, i - start), Status = (WordStatus)(-1) };
                result.Words.Add(note);
                toks.Add(new Tok { Text = Fold(raw), Note = note });
            }
        }

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
                        else if (n < 2 || n > 16 || n % 2 == 1 && v.Kind == VariationKind.Gyronny)
                            Mark(nt, WordStatus.Wrong, v.Kind == VariationKind.Gyronny ? "gyronny needs an even number of pieces" : "between two and sixteen pieces");
                        else
                            field.Count = n;
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
            if (Lexicon.Ordinaries.ContainsKey(w) || Lexicon.Charges.ContainsKey(w)) return true;
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

            if (w == "on")
            {
                Take();
                SkipFillers();
                var host = ParseOrdinaryOrCharge(null);
                if (host is Ordinary ord)
                {
                    SkipFillers();
                    if (!AtEnd && Cur != "and")
                    {
                        var c = ParseOrdinaryOrCharge(null);
                        if (c is Charge ch) ord.Charges.Add(ch);
                    }
                }
                return;
            }

            if (w == "between")
            {
                Take(WordStatus.NotDrawnYet, "charges are drawn in a later update");
                SkipFillers();
                var c = ParseOrdinaryOrCharge("between");
                return;
            }

            if (w == "in" && Peek(1) != null && IsPlacementWord(Peek(1)))
            {
                Take(WordStatus.NotDrawnYet, "charge placement is drawn in a later update");
                var place = Take(WordStatus.NotDrawnYet);
                if (result.Charges.Count > 0)
                    result.Charges[result.Charges.Count - 1].Placement = "in " + place.Text;
                return;
            }

            if (w == "counterchanged" || w == "cotised" || w == "fimbriated" || w == "voided")
            {
                Take(WordStatus.Unsupported, $"'{w}' here isn't supported yet");
                return;
            }

            if (Lexicon.KnownWrong.TryGetValue(w, out var why) && why != null)
            {
                Take(WordStatus.Wrong, why);
                return;
            }

            ParseOrdinaryOrCharge(null);
        }

        private static bool IsPlacementWord(string w) =>
            w == "chief" || w == "base" || w == "fess" || w == "pale" || w == "bend" || w == "saltire" ||
            w == "cross" || w == "orle" || w == "pile" || w == "chevron";

        /// <summary>Parses "[count] noun [line|attitudes] tincture [details]". Returns an Ordinary, a Charge, or null.</summary>
        private Tincture earlyTincture;

        private object ParseOrdinaryOrCharge(string placement)
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
                var c = new Charge { Count = count, Noun = "cross " + Peek(1), Placement = placement };
                Take(WordStatus.NotDrawnYet, "charges are drawn in a later update");
                Take(WordStatus.NotDrawnYet);
                Mark(countTok, WordStatus.NotDrawnYet);
                return FinishCharge(c, null);
            }

            if (Lexicon.Ordinaries.TryGetValue(w, out var oe) && !(count > 1 && (w == "saltire" || w == "cross")))
                return FinishOrdinary(oe, countTok, count, hadCount);

            // Possessive heads: "a stag's head caboshed"
            if ((w.EndsWith("'s") || w.EndsWith("s'")) && (Peek(1) == "head" || Peek(1) == "heads"))
            {
                string animal = w.EndsWith("'s") ? w.Substring(0, w.Length - 2) : w.Substring(0, w.Length - 2);
                var c = new Charge { Count = count, Noun = animal + "'s head", Placement = placement };
                Take(WordStatus.NotDrawnYet, "charges are drawn in a later update");
                Take(WordStatus.NotDrawnYet);
                Mark(countTok, WordStatus.NotDrawnYet);
                return FinishCharge(c, null);
            }

            if (Lexicon.Charges.TryGetValue(w, out var ce) || (count > 1 && (w == "saltire" || w == "cross")))
            {
                if (ce == null) ce = new Lexicon.ChargeEntry { Singular = w };
                var nounTok = Take(WordStatus.NotDrawnYet, "charges are drawn in a later update");
                Mark(countTok, WordStatus.NotDrawnYet);
                if (count > 1 && !ce.Plural && Lexicon.Charges.ContainsKey(w))
                    Mark(nounTok, WordStatus.Passable, "use the plural for more than one");
                else if (count == 1 && ce.Plural && countTok != null)
                    Mark(nounTok, WordStatus.Passable, "use the singular for one");
                var c = new Charge { Count = count, Noun = ce.Singular, Placement = placement };
                if (Cur == "head" || Cur == "heads") // "three stags' heads" (the apostrophe is trimmed off)
                {
                    Take(WordStatus.NotDrawnYet);
                    c.Noun += "'s head";
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

            // Unknown noun
            var bad = Take(WordStatus.Wrong);
            MarkUnknown(bad, countTok != null);
            Mark(countTok, WordStatus.Correct);
            // Skip what belongs to it (attitudes, tincture) so they aren't reported as more errors
            while (!AtEnd && (Lexicon.Attitudes.Contains(Cur) || Lexicon.ChargeDetails.Contains(Cur))) Take();
            if (Cur != null && Lexicon.Tinctures.ContainsKey(Cur)) Take();
            return null;
        }

        private Ordinary FinishOrdinary(Lexicon.OrdinaryEntry oe, Tok countTok, int count, bool hadCount)
        {
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
                Mark(countTok, WordStatus.Wrong, "at most three piles");
                o.Count = 3;
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

            SkipFillers();
            if (earlyTincture != null)
            {
                o.Tincture = earlyTincture;
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
            result.Ordinaries.Add(o);
            return o;
        }

        private Charge FinishCharge(Charge c, string impliedTincture)
        {
            // Attitudes and directions
            while (!AtEnd)
            {
                if (Lexicon.Attitudes.Contains(Cur))
                {
                    c.Attitudes.Add(Take(WordStatus.NotDrawnYet).Text);
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
                if (!TryTincture(out var t, allowProper: true, allowCounterchanged: true))
                {
                    result.Messages.Add($"'{c.Noun}' needs a tincture.");
                }
                else c.Tincture = t;
            }
            if (c.Tincture != null && c.Tincture.Kind != TinctureKind.Proper)
            {
                foreach (var o in awaitingTincture) o.Tincture = c.Tincture;
                awaitingTincture.Clear();
            }

            // "armed and langued gules"
            while (!AtEnd && Lexicon.ChargeDetails.Contains(Cur))
            {
                Take(WordStatus.NotDrawnYet);
                if (Cur == "and" && Peek(1) != null && Lexicon.ChargeDetails.Contains(Peek(1))) { Take(); continue; }
                TryTincture(out _, true, false);
            }

            result.Charges.Add(c);
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
                Take(e.PassableNote == null ? WordStatus.Correct : WordStatus.Passable, e.PassableNote);
                t = new Tincture { Kind = e.Kind, Name = e.Canonical };
                mentioned.Add(t);
                return true;
            }

            if (w.Length == 7 && w[0] == '#' && IsHex(w.Substring(1)))
            {
                Take();
                int rgb = int.Parse(w.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                t = new Tincture { Kind = TinctureKind.Hex, Name = w, Hex = new Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb) };
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
                Take(WordStatus.NotDrawnYet);
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

        private int TakeNumber()
        {
            var t = Take();
            if (Lexicon.Numbers.TryGetValue(t.Text, out int n)) return n;
            int.TryParse(t.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
            if (n < 1 || n > 20)
            {
                Mark(t, WordStatus.Wrong, "between one and twenty");
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
