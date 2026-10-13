using System;
using System.IO;
using System.IO.Compression;
using ValheimHeraldryMod.Heraldry;

static class Png
{
    static readonly uint[] Table = MakeTable();
    static uint[] MakeTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; t[n] = c; }
        return t;
    }
    static uint Crc(byte[] type, byte[] data)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in type) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (var b in data) c = Table[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
    static void Chunk(Stream s, string type, byte[] data)
    {
        var tb = System.Text.Encoding.ASCII.GetBytes(type);
        void U32(uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        U32((uint)data.Length); s.Write(tb); s.Write(data); U32(Crc(tb, data));
    }
    public static void Save(string path, Rgba[] px, int w, int h)
    {
        var raw = new byte[(w * 4 + 1) * h];
        int i = 0;
        for (int y = 0; y < h; y++)
        {
            raw[i++] = 0;
            for (int x = 0; x < w; x++) { var c = px[y * w + x]; raw[i++] = c.R; raw[i++] = c.G; raw[i++] = c.B; raw[i++] = c.A; }
        }
        byte[] z;
        using (var ms = new MemoryStream())
        {
            using (var zs = new ZLibStream(ms, CompressionLevel.Fastest, true)) zs.Write(raw, 0, raw.Length);
            z = ms.ToArray();
        }
        using var f = File.Create(path);
        f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        ihdr[0] = (byte)(w >> 24); ihdr[1] = (byte)(w >> 16); ihdr[2] = (byte)(w >> 8); ihdr[3] = (byte)w;
        ihdr[4] = (byte)(h >> 24); ihdr[5] = (byte)(h >> 16); ihdr[6] = (byte)(h >> 8); ihdr[7] = (byte)h;
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(f, "IHDR", ihdr); Chunk(f, "IDAT", z); Chunk(f, "IEND", new byte[0]);
    }
}
