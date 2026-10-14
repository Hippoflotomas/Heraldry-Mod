using System.Collections.Generic;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     A charge pack's manifest.json, read with <see cref="MiniJson"/>. Pure C#, so the preview tool reads
    ///     manifests exactly as the mod does. Field names match the JSON keys (case doesn't matter in the file).
    /// </summary>
    public sealed class PackManifest
    {
        public string pack;
        public int version;
        public List<ChargeDef> charges = new List<ChargeDef>();
        public List<AtlasDef> atlases = new List<AtlasDef>();

        public sealed class ChargeDef
        {
            public string[] names;     // singular first, then plural and any aliases
            public string[] attitudes; // what this drawing shows ("rampant"); empty = the default drawing
            public string image;       // own PNG (not used for atlas cells)
            public string mask;
            public int cell;           // atlas cell, row by row from the top left
            public bool noLines;       // don't derive interior lines when the mask has none
        }

        public sealed class AtlasDef
        {
            public string file;
            public string mask;
            public int cellSize;
            public int cols;
            public int rows;
            public List<ChargeDef> charges = new List<ChargeDef>();
        }

        /// <summary>Reads a manifest; throws FormatException (with line and column) if it isn't valid JSON.</summary>
        public static PackManifest Parse(string json)
        {
            var root = MiniJson.Obj(MiniJson.Parse(json));
            if (root == null) throw new System.FormatException("the file must be one JSON object: { ... }");
            var m = new PackManifest { pack = MiniJson.Str(root, "pack"), version = MiniJson.Int(root, "version") };
            var list = MiniJson.List(root, "charges");
            if (list != null)
                foreach (var item in list)
                    m.charges.Add(Charge(MiniJson.Obj(item)));
            var atlases = MiniJson.List(root, "atlases");
            if (atlases != null)
                foreach (var item in atlases)
                {
                    var a = MiniJson.Obj(item);
                    if (a == null) continue;
                    var def = new AtlasDef
                    {
                        file = MiniJson.Str(a, "file"),
                        mask = MiniJson.Str(a, "mask"),
                        cellSize = MiniJson.Int(a, "cellSize"),
                        cols = MiniJson.Int(a, "cols"),
                        rows = MiniJson.Int(a, "rows"),
                    };
                    var cells = MiniJson.List(a, "charges");
                    if (cells != null)
                        foreach (var c in cells)
                            def.charges.Add(Charge(MiniJson.Obj(c)));
                    m.atlases.Add(def);
                }
            return m;
        }

        private static ChargeDef Charge(Dictionary<string, object> o)
        {
            if (o == null) return null;
            return new ChargeDef
            {
                names = MiniJson.Strings(o, "names"),
                attitudes = MiniJson.Strings(o, "attitudes"),
                image = MiniJson.Str(o, "image"),
                mask = MiniJson.Str(o, "mask"),
                cell = MiniJson.Int(o, "cell"),
                noLines = MiniJson.Bool(o, "noLines"),
            };
        }
    }
}
