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
                    // Hanging-loop gaps along the top (rough stand-in for the vanilla cloth), to test the notch fill
                    if (fy < 0.03f && Frac(fx * 4) > 0.75f) mask[y * w + x] = false;
                }
                OutlineMask.FillTopNotches(mask, w, h);
                OutlineMask.HideTop(mask, w, h, 0.065f); // roughly what the vanilla banner's beam hides
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

    static float Frac(float v) => v - (float)Math.Floor(v);

    static string Describe(Field f) => f == null ? "null" :
        $"{(f.Partition != PartitionKind.None ? "per " + f.Partition : f.Variation != VariationKind.None ? f.Variation + " of " + f.Count : "plain")} {f.Line} [{string.Join(",", f.Tinctures)}]";
}
