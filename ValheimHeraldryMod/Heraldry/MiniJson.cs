using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     A small, strict JSON reader for charge-pack manifests. Unity's JsonUtility silently dropped the
    ///     manifest's charge list in game, so this replaces it: no reflection, plain C# (the preview tool runs the
    ///     same code), and errors say where they are ("line 6: expected ',' or '}'").
    ///     Objects become Dictionary&lt;string, object&gt; (keys case-insensitive), arrays List&lt;object&gt;,
    ///     numbers double, plus string, bool and null.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            var p = new Reader(text ?? "");
            p.SkipSpace();
            var v = p.Value(0);
            p.SkipSpace();
            if (!p.AtEnd) throw p.Error("unexpected text after the end");
            return v;
        }

        // ------------------------------------------------------------------ helpers for reading the result

        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;

        public static List<object> List(Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) ? v as List<object> : null;

        public static string Str(Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) ? v as string : null;

        public static int Int(Dictionary<string, object> o, string key, int fallback = 0) =>
            o != null && o.TryGetValue(key, out var v) && v is double d ? (int)d : fallback;

        public static bool Bool(Dictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) && v is bool b && b;

        /// <summary>A list of strings; a single string counts as a one-item list.</summary>
        public static string[] Strings(Dictionary<string, object> o, string key)
        {
            if (o == null || !o.TryGetValue(key, out var v) || v == null) return new string[0];
            if (v is string s) return new[] { s };
            var result = new List<string>();
            if (v is List<object> list)
                foreach (var item in list)
                    if (item is string str) result.Add(str);
            return result.ToArray();
        }

        private sealed class Reader
        {
            private readonly string s;
            private int i;

            public Reader(string text)
            {
                s = text;
                if (s.Length > 0 && s[0] == '﻿') i = 1; // byte order mark
            }

            public bool AtEnd => i >= s.Length;

            public FormatException Error(string what)
            {
                int line = 1, col = 1;
                for (int k = 0; k < i && k < s.Length; k++)
                    if (s[k] == '\n') { line++; col = 1; }
                    else col++;
                return new FormatException($"line {line}, column {col}: {what}");
            }

            public void SkipSpace()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            public object Value(int depth)
            {
                if (depth > 64) throw Error("nested too deeply");
                SkipSpace();
                if (AtEnd) throw Error("unexpected end of file");
                char c = s[i];
                switch (c)
                {
                    case '{': return Object(depth);
                    case '[': return Array(depth);
                    case '"': return String();
                    case 't': Word("true"); return true;
                    case 'f': Word("false"); return false;
                    case 'n': Word("null"); return null;
                }
                if (c == '-' || (c >= '0' && c <= '9')) return Number();
                throw Error($"unexpected '{c}'");
            }

            private Dictionary<string, object> Object(int depth)
            {
                var o = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                i++; // {
                SkipSpace();
                if (!AtEnd && s[i] == '}') { i++; return o; }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || s[i] != '"') throw Error("expected a \"name\"");
                    string key = String();
                    SkipSpace();
                    if (AtEnd || s[i] != ':') throw Error($"expected ':' after \"{key}\"");
                    i++;
                    o[key] = Value(depth + 1);
                    SkipSpace();
                    if (AtEnd) throw Error("unexpected end of file; a '}' is missing");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return o; }
                    throw Error("expected ',' or '}'");
                }
            }

            private List<object> Array(int depth)
            {
                var a = new List<object>();
                i++; // [
                SkipSpace();
                if (!AtEnd && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(depth + 1));
                    SkipSpace();
                    if (AtEnd) throw Error("unexpected end of file; a ']' is missing");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return a; }
                    throw Error("expected ',' or ']'");
                }
            }

            private string String()
            {
                i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("a string is missing its closing quote");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("a string is missing its closing quote");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw Error("bad \\u escape");
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default: throw Error($"unknown escape '\\{e}' (Windows paths need '\\\\' or '/')");
                    }
                }
            }

            private double Number()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw Error("bad number");
                return d;
            }

            private void Word(string w)
            {
                if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) throw Error($"unexpected text (did you mean {w}?)");
                i += w.Length;
            }
        }
    }
}
