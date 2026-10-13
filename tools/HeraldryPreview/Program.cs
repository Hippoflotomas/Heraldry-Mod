using System;
using System.IO;
using System.Linq;
using ValheimHeraldryMod.Heraldry;

static class Program
{
    static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "parse";
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
                }
            }
            Directory.CreateDirectory(args[2]);
            int i = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            foreach (var line in File.ReadAllLines(args[1]))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
                var b = BlazonParser.Parse(line);
                var t0 = sw.ElapsedMilliseconds;
                var px = FieldRenderer.Render(b, w, h, mask);
                Console.WriteLine($"{i:D2} {sw.ElapsedMilliseconds - t0,4} ms  {line}");
                Png.Save(Path.Combine(args[2], $"{i:D2}.png"), px, w, h);
                i++;
            }
            return;
        }
        if (mode == "parse")
        {
            foreach (var line in File.ReadAllLines(args[1]))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
                var b = BlazonParser.Parse(line);
                Console.WriteLine($"> {line}");
                Console.WriteLine($"  valid={b.IsValid} field={Describe(b.Field)} ords=[{string.Join("; ", b.Ordinaries.Select(o => $"{o.Count}x{o.Kind}{(o.Diminutive?"(dim)":"")} {o.Line} {o.Tincture}"))}] charges=[{string.Join("; ", b.Charges.Select(c => $"{c.Count}x{c.Noun} {string.Join(" ",c.Attitudes)} {c.Tincture} {c.Placement}"))}]");
                foreach (var w in b.Words.Where(w => w.Status != WordStatus.Correct || w.Message != null))
                    Console.WriteLine($"    {w.Word}: {w.Status}{(w.Message == null ? "" : " - " + w.Message)}");
                foreach (var m in b.Messages) Console.WriteLine($"    !! {m}");
            }
        }
    }

    static string Describe(Field f) => f == null ? "null" :
        $"{(f.Partition != PartitionKind.None ? "per " + f.Partition : f.Variation != VariationKind.None ? f.Variation + " of " + f.Count : "plain")} {f.Line} [{string.Join(",", f.Tinctures)}]";
}
