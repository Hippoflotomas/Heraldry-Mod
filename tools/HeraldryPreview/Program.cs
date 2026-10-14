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
}
