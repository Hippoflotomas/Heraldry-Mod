using System;
using System.IO;
using System.Linq;
using ValheimHeraldryMod.Heraldry;

static class Program
{
    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "parse";
        LoadIcons();
        LoadPacks();
        if (mode == "basic")
        {
            // basic: every basic-mode combination (sampled) must parse with no Wrong words and within the default limits
            BasicCheck();
            return;
        }
        if (mode == "paint")
        {
            // paint <perTincture> <maxTotal> <blazon...>: the paint the herald's table asks for
            int per = int.Parse(args[1]), max = int.Parse(args[2]);
            string text = string.Join(" ", args.Skip(3));
            var cost = PaintCost.For(BlazonParser.Parse(text), per, max);
            Console.WriteLine($"{text}  =>  " + (cost.Count == 0 ? "free" : string.Join(", ", cost.Select(kv => kv.Value + " " + kv.Key))));
            return;
        }
        if (mode == "tabard")
        {
            // tabard <list> <outdir> <Tabard.hskin>: the tabard texture as the mod paints it (front: arms on the
            // chest; back: field and ordinaries only), 1024 x 1024, UV layout as in TabardUVLayout.png
            Tabard(args[1], args[2], args[3]);
            return;
        }
        if (mode == "render")
        {
            // render <list> <outdir> <width> <height>
            int w = int.Parse(args[3]), h = int.Parse(args[4]);
            // Approximate vanilla hanging-banner cloth: pointed bottom from 84% of the height
            bool[] mask = null;
            if (args.Length > 5 && args[5] == "point")
            {
                mask = new bool[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float fy = (y + 0.5f) / h, fx = (x + 0.5f) / w;
                    mask[y * w + x] = fy < 0.84f || Math.Abs(fx - 0.5f) < (1 - fy) / 0.16f * 0.5f;
                    // Hanging-loop gaps along the top (rough stand-in for the vanilla cloth), to test the notch fill
                    if (fy < 0.03f && Frac(fx * 4) > 0.75f) mask[y * w + x] = false;
                }
                OutlineMask.FillTopNotches(mask, w, h);
                OutlineMask.HideTop(mask, w, h, 0.065f); // roughly what the vanilla banner's beam hides
            }
            if (args.Length > 5 && args[5] == "round")
            {
                // A round shield face (the herald's table display)
                mask = new bool[w * h];
                for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = (x + 0.5f) / w - 0.5f, dy = (y + 0.5f) / h - 0.5f;
                    mask[y * w + x] = dx * dx + dy * dy < 0.25f;
                }
            }
            // optional 7th argument: compact | spread | reflow (how field charges use the tall banner)
            var arrangement = args.Length > 6
                ? (FieldRenderer.Arrangement)Enum.Parse(typeof(FieldRenderer.Arrangement), args[6], true)
                : FieldRenderer.Arrangement.Spread;
            Directory.CreateDirectory(args[2]);
            int i = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var line in File.ReadAllLines(args[1]))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
                var b = BlazonParser.Parse(line);
                var t0 = sw.ElapsedMilliseconds;
                var px = FieldRenderer.Render(b, w, h, mask, arrangement);
                Console.WriteLine($"{i:D2} {sw.ElapsedMilliseconds - t0,4} ms  {line}");
                Png.Save(Path.Combine(args[2], $"{i:D2}.png"), px, w, h);
                i++;
            }
            return;
        }
        if (mode == "manifest")
        {
            // manifest <file>: read a pack manifest exactly as the mod does
            var m = PackManifest.Parse(File.ReadAllText(args[1]));
            Console.WriteLine($"pack={m.pack} version={m.version} charges={m.charges.Count} atlases={m.atlases.Count}");
            foreach (var c in m.charges)
                Console.WriteLine($"  names=[{string.Join(", ", c.names)}] attitudes=[{string.Join(", ", c.attitudes)}] image={c.image} mask={c.mask} noLines={c.noLines}");
            return;
        }
        if (mode == "parse")
        {
            foreach (var line in File.ReadAllLines(args[1]))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
                var b = BlazonParser.Parse(line);
                Console.WriteLine($"> {line}");
                Console.WriteLine($"  valid={b.IsValid} field={Describe(b.Field)} ords=[{string.Join("; ", b.Ordinaries.Select(o => $"{o.Count}x{o.Kind}{(o.Diminutive?"(dim)":"")} {o.Line} {o.Tincture}{Style(o.Style)}"))}] charges=[{string.Join("; ", b.Charges.Select(Charge))}]");
                foreach (var o in b.Ordinaries)
                    if (o.Charges.Count > 0) Console.WriteLine($"  on {o.Kind}: {string.Join("; ", o.Charges.Select(Charge))}");
                foreach (var w in b.Words.Where(w => w.Status != WordStatus.Correct || w.Message != null))
                    Console.WriteLine($"    {w.Word}: {w.Status}{(w.Message == null ? "" : " - " + w.Message)}");
                foreach (var m in b.Messages) Console.WriteLine($"    !! {m}");
            }
        }
    }

    static string Charge(Charge c) =>
        $"{c.Count}x{c.Noun}{(c.Drawable ? "" : "(art)")} {string.Join(" ", c.Attitudes)} {c.Tincture} {c.Placement}" +
        $"{(c.Host != null ? "(" + c.Host.Kind + ")" : "")}{(c.Rows != null ? " rows " + string.Join("-", c.Rows) : "")}" +
        $"{(c.Points != 5 ? " pts" + c.Points : "")}{(c.Pierced ? " pierced" : "")}{Style(c.Style)}";

    static string Style(Style s) =>
        (s.Voided ? " voided" + (s.VoidTincture != null ? " " + s.VoidTincture : "") : "") +
        (s.Fimbriation != null ? " fimbriated " + s.Fimbriation : "");

    /// <summary>
    ///     Game icons for picture charges, outside the game: set HERALDRY_ICONS to a folder of .rgba files
    ///     (4-byte width, 4-byte height, then RGBA rows top to bottom), named like trophywolf.rgba.
    ///     icons_to_rgba.py makes them from the PNGs the in-game 'heraldry_probe icons' command saves.
    /// </summary>
    static void LoadIcons()
    {
        string dir = Environment.GetEnvironmentVariable("HERALDRY_ICONS");
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        int n = 0;
        foreach (var f in Directory.GetFiles(dir, "*.rgba"))
        {
            var bytes = File.ReadAllBytes(f);
            int w = BitConverter.ToInt32(bytes, 0), h = BitConverter.ToInt32(bytes, 4);
            var px = new Rgba[w * h];
            for (int i = 0; i < w * h; i++)
                px[i] = new Rgba(bytes[8 + i * 4], bytes[9 + i * 4], bytes[10 + i * 4], bytes[11 + i * 4]);
            ChargeArtLibrary.Register(Path.GetFileNameWithoutExtension(f).ToLowerInvariant(), px, w, h, deriveLines: true);
            n++;
        }
        Console.Error.WriteLine($"{n} icons from {dir}");
    }

    /// <summary>
    ///     Charge packs outside the game: set HERALDRY_PACKS to a folder of pack folders (as in
    ///     BepInEx/config/Heraldry/Charges). Each PNG needs a .rgba twin made by icons_to_rgba.py (wolf.png.rgba).
    ///     Single-image charges only; atlases are read by the mod itself.
    /// </summary>
    static void LoadPacks()
    {
        string root = Environment.GetEnvironmentVariable("HERALDRY_PACKS");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
        var packs = new TestPacks();
        foreach (var dir in Directory.GetDirectories(root))
        {
            var m = PackManifest.Parse(File.ReadAllText(Path.Combine(dir, "manifest.json"))); // the mod's own reader
            string pack = (m.pack ?? Path.GetFileName(dir)).ToLowerInvariant().Replace(' ', '-');
            foreach (var ch in m.charges)
            {
                if (ch == null || ch.image == null || ch.names.Length == 0) continue;
                var (px, w, h) = ReadRgba(FindCaseless(dir, ch.image + ".rgba"));
                Rgba[] mask = ch.mask != null ? ReadRgba(FindCaseless(dir, ch.mask + ".rgba")).px : null;
                string key = $"pack:{pack}:{packs.Count}";
                ChargeArtLibrary.Register(key, px, w, h, !ch.noLines, mask);
                foreach (var n in ch.names) packs.Add(n, pack, key, ch.attitudes);
            }
        }
        Lexicon.Packs = packs;
        Console.Error.WriteLine($"{packs.Count} pack drawings from {root}");
    }

    /// <summary>Windows file names ignore case; this harness runs on Linux, so match the way Windows would.</summary>
    static string FindCaseless(string dir, string name) =>
        Directory.GetFiles(dir).FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase))
        ?? Path.Combine(dir, name);

    static (Rgba[] px, int w, int h) ReadRgba(string f)
    {
        var bytes = File.ReadAllBytes(f);
        int w = BitConverter.ToInt32(bytes, 0), h = BitConverter.ToInt32(bytes, 4);
        var px = new Rgba[w * h];
        for (int i = 0; i < w * h; i++) px[i] = new Rgba(bytes[8 + i * 4], bytes[9 + i * 4], bytes[10 + i * 4], bytes[11 + i * 4]);
        return (px, w, h);
    }

    /// <summary>Minimal stand-in for the mod's ChargePacks (first pack wins, best attitude match).</summary>
    sealed class TestPacks : IPackCharges
    {
        readonly System.Collections.Generic.List<(string name, string pack, string key, string[] atts)> all = new();
        public int Count;
        public void Add(string name, string pack, string key, string[] atts) { all.Add((name.ToLowerInvariant(), pack, key, atts)); Count++; }
        public string Resolve(string name, System.Collections.Generic.IList<string> attitudes, out string unmatched)
        {
            unmatched = null;
            var c = all.Where(d => d.name == name || d.pack + ":" + d.name == name).ToList();
            if (c.Count == 0) return null;
            var best = c.OrderByDescending(d => attitudes.Count(a => d.atts.Contains(a))).First();
            unmatched = attitudes.FirstOrDefault(a => !best.atts.Contains(a) && a != "contourne" && a != "inverted" && a != "reversed");
            return best.key;
        }
        public string Suggest(string name) => null;
    }

    static float Frac(float v) => v - (float)Math.Floor(v);

    static string Describe(Field f) => f == null ? "null" :
        $"{(f.Partition != PartitionKind.None ? "per " + f.Partition : f.Variation != VariationKind.None ? f.Variation + " of " + f.Count : "plain")} {f.Line} [{string.Join(",", f.Tinctures)}]";

    static void Tabard(string list, string outDir, string hskin)
    {
        const int Size = 1024;
        float[] uv; int[] idx;
        using (var r = new BinaryReader(File.OpenRead(hskin)))
        {
            r.ReadChars(4); r.ReadInt32();
            int n = r.ReadInt32();
            for (int k = 0; k < n * 6; k++) r.ReadSingle();
            uv = new float[n * 2];
            for (int k = 0; k < n * 2; k++) uv[k] = r.ReadSingle();
            idx = new int[r.ReadInt32()];
            for (int k = 0; k < idx.Length; k++) idx[k] = r.ReadInt32();
        }
        Directory.CreateDirectory(outDir);
        int i = 0;
        foreach (var line in File.ReadAllLines(list))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
            var px = new Rgba[Size * Size];
            for (int k = 0; k < px.Length; k++) px[k] = new Rgba(214, 204, 178);
            var front = BlazonParser.Parse(line);
            var back = BlazonParser.Parse(line);
            back.Charges.Clear();
            foreach (var o in back.Ordinaries) o.Charges.Clear();
            Panel(px, Size, uv, idx, true, front);
            Panel(px, Size, uv, idx, false, back);
            Png.Save(Path.Combine(outDir, $"{i:D2}.png"), px, Size, Size);
            Console.WriteLine($"{i:D2}  {line}");
            i++;
        }
    }

    // Same as TabardTextures.Paint, with image rows top down (v = 1 at the top)
    static void Panel(Rgba[] dst, int size, float[] uv, int[] idx, bool front, Blazon b)
    {
        var tris = new System.Collections.Generic.List<int>();
        float minU = 1, minV = 1, maxU = 0, maxV = 0;
        for (int t = 0; t + 2 < idx.Length; t += 3)
        {
            float cu = (uv[idx[t] * 2] + uv[idx[t + 1] * 2] + uv[idx[t + 2] * 2]) / 3f;
            if ((cu < 0.5f) != front) continue;
            for (int k = 0; k < 3; k++)
            {
                int v = idx[t + k];
                minU = Math.Min(minU, uv[v * 2]); maxU = Math.Max(maxU, uv[v * 2]);
                minV = Math.Min(minV, uv[v * 2 + 1]); maxV = Math.Max(maxV, uv[v * 2 + 1]);
                tris.Add(v);
            }
        }
        var outline = new float[tris.Count * 2]; var ot = new int[tris.Count];
        for (int k = 0; k < tris.Count; k++)
        {
            outline[k * 2] = (uv[tris[k] * 2] - minU) / (maxU - minU);
            outline[k * 2 + 1] = (uv[tris[k] * 2 + 1] - minV) / (maxV - minV);
            ot[k] = k;
        }
        int x0 = (int)Math.Floor(minU * size), y0 = (int)Math.Floor(minV * size);
        int x1 = (int)Math.Ceiling(maxU * size), y1 = (int)Math.Ceiling(maxV * size);
        int pw = x1 - x0, ph = y1 - y0;
        var mask = OutlineMask.FromTriangles(outline, ot, pw, ph);
        var art = FieldRenderer.Render(b, pw, ph, mask, FieldRenderer.Arrangement.Chest);
        for (int r = 0; r < ph; r++)
        {
            int ty = y1 - 1 - r, row = size - 1 - ty;
            for (int x = 0; x < pw; x++) dst[row * size + x0 + x] = art[r * pw + x];
        }
    }

    static void BasicCheck()
    {
        var b = new BasicBlazon();
        int n = 0, bad = 0;
        var rnd = new Random(1);
        var charges = BasicBlazon.BuiltInCharges.Where(c => !c.IsDivider).ToList();
        var colours = BasicBlazon.Colours.Where(c => !c.IsDivider).Select(c => c.Value).ToList();
        void Check()
        {
            string text = b.Build();
            var p = BlazonParser.Parse(text);
            n++;
            var wrong = p.Words.Where(w => w.Status == WordStatus.Wrong || w.Status == WordStatus.Passable || w.Status == WordStatus.Unsupported).ToList();
            if (!p.IsValid || wrong.Count > 0 || p.Messages.Count > 0 || p.Elements > 25)
            {
                bad++;
                if (bad <= 4000) Console.WriteLine($"BAD {text}  ->  " + string.Join("; ", wrong.Select(w => $"{w.Word}: {w.Status} {w.Message}")) + " " + string.Join("; ", p.Messages) + $" el={p.Elements}");
            }
            else if (n % 400 == 0) Console.WriteLine("ok  " + text);
        }
        // Every field x line x two colours
        foreach (var f in BasicBlazon.Fields) foreach (var l in BasicBlazon.Lines) foreach (var c in new[] { "azure", "ermine", "or" })
        { b = new BasicBlazon { Field = f.Value, FieldLine = l.Value, Colour1 = c, Colour2 = "gules" }; Check(); }
        // Every ordinary x line x second x placement x charge (count/style varied)
        foreach (var o in BasicBlazon.Ordinaries) foreach (var l in BasicBlazon.Lines) foreach (var s2 in BasicBlazon.SecondOrdinaries)
        foreach (var pl in BasicBlazon.Placements) foreach (var ch in charges)
        {
            b = new BasicBlazon
            {
                Field = BasicBlazon.Fields[rnd.Next(BasicBlazon.Fields.Length)].Value, Colour1 = colours[rnd.Next(colours.Count)], Colour2 = colours[rnd.Next(colours.Count)],
                Ordinary = o.Value, OrdinaryLine = l.Value, OrdinaryColour = colours[rnd.Next(colours.Count)],
                Second = s2.Value, SecondLine = BasicBlazon.Lines[rnd.Next(3)].Value, SecondColour = colours[rnd.Next(colours.Count)],
                Charge = ch, Placement = pl.Value, Count = 1 + rnd.Next(3), Style = BasicBlazon.Styles[rnd.Next(3)].Value,
                ChargeColour = colours[rnd.Next(colours.Count)],
            };
            Check();
        }
        // Pack charge
        b = new BasicBlazon { Charge = BasicBlazon.PackCharge("black dog"), Count = 2 }; Console.WriteLine("pack: " + b.Build());
        Console.WriteLine($"{n} checked, {bad} bad");
    }
}
