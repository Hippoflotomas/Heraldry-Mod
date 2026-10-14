using System;
using System.IO;
using System.IO.Compression;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     A small PNG reader and writer in plain C#, so atlas cells can be cut into single charges anywhere,
    ///     including on a dedicated server (no graphics), and so the result is the same bytes on every machine.
    ///     Reads 8-bit greyscale, RGB, palette (with transparency), grey+alpha and RGBA, not interlaced; that covers
    ///     what image editors save by default. Writes RGBA with the usual per-row filters.
    /// </summary>
    public static class MiniPng
    {
        private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        /// <summary>Pixels, rows top to bottom. Throws InvalidDataException with a readable reason.</summary>
        public static Rgba[] Decode(byte[] png, int maxSide, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length < 33) throw new InvalidDataException("not a PNG");
            for (int i = 0; i < 8; i++) if (png[i] != Signature[i]) throw new InvalidDataException("not a PNG");

            int pos = 8, bitDepth = 0, colourType = 0, interlace = 0;
            byte[] palette = null, trns = null;
            var idat = new MemoryStream();
            while (pos + 8 <= png.Length)
            {
                int len = BigInt(png, pos);
                string type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
                int data = pos + 8;
                if (len < 0 || data + len > png.Length) throw new InvalidDataException("damaged PNG");
                if (type == "IHDR")
                {
                    width = BigInt(png, data);
                    height = BigInt(png, data + 4);
                    bitDepth = png[data + 8];
                    colourType = png[data + 9];
                    interlace = png[data + 12];
                }
                else if (type == "PLTE") { palette = new byte[len]; Array.Copy(png, data, palette, 0, len); }
                else if (type == "tRNS") { trns = new byte[len]; Array.Copy(png, data, trns, 0, len); }
                else if (type == "IDAT") idat.Write(png, data, len);
                else if (type == "IEND") break;
                pos = data + len + 4; // + CRC
            }
            if (width < 1 || height < 1 || width > maxSide || height > maxSide)
                throw new InvalidDataException($"{width}x{height}; the limit is {maxSide} on each side");
            if (bitDepth != 8) throw new InvalidDataException($"{bitDepth}-bit PNG; save it as 8-bit");
            if (interlace != 0) throw new InvalidDataException("interlaced PNG; save it without interlacing");
            int channels;
            switch (colourType)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 3: channels = 1; if (palette == null) throw new InvalidDataException("palette PNG without a palette"); break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: throw new InvalidDataException("unknown PNG colour type " + colourType);
            }

            int stride = width * channels;
            var raw = new byte[(long)height * (stride + 1)];
            idat.Position = 2; // zlib header; the deflate data follows
            using (var z = new DeflateStream(idat, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int n = z.Read(raw, read, raw.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read < raw.Length) throw new InvalidDataException("PNG image data is cut short");
            }

            Unfilter(raw, height, stride, channels);

            var px = new Rgba[width * height];
            for (int y = 0; y < height; y++)
            {
                int row = y * (stride + 1) + 1;
                for (int x = 0; x < width; x++)
                {
                    int s = row + x * channels;
                    Rgba c;
                    switch (colourType)
                    {
                        case 0: c = new Rgba(raw[s], raw[s], raw[s], (byte)(trns != null && trns.Length >= 2 && raw[s] == trns[1] ? 0 : 255)); break;
                        case 2:
                            c = new Rgba(raw[s], raw[s + 1], raw[s + 2]);
                            if (trns != null && trns.Length >= 6 && raw[s] == trns[1] && raw[s + 1] == trns[3] && raw[s + 2] == trns[5]) c.A = 0;
                            break;
                        case 3:
                        {
                            int i = raw[s];
                            if (i * 3 + 2 >= palette.Length) throw new InvalidDataException("PNG palette index out of range");
                            c = new Rgba(palette[i * 3], palette[i * 3 + 1], palette[i * 3 + 2], trns != null && i < trns.Length ? trns[i] : (byte)255);
                            break;
                        }
                        case 4: c = new Rgba(raw[s], raw[s], raw[s], raw[s + 1]); break;
                        default: c = new Rgba(raw[s], raw[s + 1], raw[s + 2], raw[s + 3]); break;
                    }
                    px[y * width + x] = c;
                }
            }
            return px;
        }

        /// <summary>Width and height from the header, without decoding.</summary>
        public static bool Size(byte[] png, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length < 24) return false;
            for (int i = 0; i < 8; i++) if (png[i] != Signature[i]) return false;
            if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R') return false;
            width = BigInt(png, 16);
            height = BigInt(png, 20);
            return true;
        }

        private static void Unfilter(byte[] raw, int height, int stride, int bpp)
        {
            for (int y = 0; y < height; y++)
            {
                int row = y * (stride + 1), prev = row - (stride + 1);
                byte filter = raw[row];
                for (int x = 0; x < stride; x++)
                {
                    int i = row + 1 + x;
                    int a = x >= bpp ? raw[i - bpp] : 0;
                    int b = y > 0 ? raw[prev + 1 + x] : 0;
                    int c = y > 0 && x >= bpp ? raw[prev + 1 + x - bpp] : 0;
                    switch (filter)
                    {
                        case 0: break;
                        case 1: raw[i] = (byte)(raw[i] + a); break;
                        case 2: raw[i] = (byte)(raw[i] + b); break;
                        case 3: raw[i] = (byte)(raw[i] + ((a + b) >> 1)); break;
                        case 4: raw[i] = (byte)(raw[i] + Paeth(a, b, c)); break;
                        default: throw new InvalidDataException("damaged PNG (filter " + filter + ")");
                    }
                }
            }
        }

        /// <summary>RGBA PNG from pixels (rows top to bottom). Same pixels give the same bytes.</summary>
        public static byte[] Encode(Rgba[] px, int width, int height)
        {
            int stride = width * 4;
            var cur = new byte[stride];
            var prev = new byte[stride];
            var filtered = new byte[5][];
            for (int f = 0; f < 5; f++) filtered[f] = new byte[stride];
            var data = new MemoryStream();
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var c = px[y * width + x];
                    cur[x * 4] = c.R; cur[x * 4 + 1] = c.G; cur[x * 4 + 2] = c.B; cur[x * 4 + 3] = c.A;
                }
                // Each filter, keep the one with the smallest sum of signed bytes (the usual heuristic)
                int best = 0;
                long bestScore = long.MaxValue;
                for (int f = 0; f < 5; f++)
                {
                    long score = 0;
                    for (int x = 0; x < stride; x++)
                    {
                        int a = x >= 4 ? cur[x - 4] : 0, b = y > 0 ? prev[x] : 0, c = y > 0 && x >= 4 ? prev[x - 4] : 0;
                        int p = f == 0 ? 0 : f == 1 ? a : f == 2 ? b : f == 3 ? (a + b) >> 1 : Paeth(a, b, c);
                        byte v = (byte)(cur[x] - p);
                        filtered[f][x] = v;
                        score += v < 128 ? v : 256 - v;
                    }
                    if (score < bestScore) { bestScore = score; best = f; }
                }
                data.WriteByte((byte)best);
                data.Write(filtered[best], 0, stride);
                var t = prev; prev = cur; cur = t;
            }

            var raw = data.ToArray();
            var z = new MemoryStream();
            z.WriteByte(0x78); z.WriteByte(0x9C);
            using (var d = new DeflateStream(z, CompressionMode.Compress, true)) d.Write(raw, 0, raw.Length);
            uint adler = Adler32(raw);
            z.WriteByte((byte)(adler >> 24)); z.WriteByte((byte)(adler >> 16)); z.WriteByte((byte)(adler >> 8)); z.WriteByte((byte)adler);

            var png = new MemoryStream();
            png.Write(Signature, 0, 8);
            var ihdr = new byte[13];
            PutInt(ihdr, 0, width); PutInt(ihdr, 4, height);
            ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
            Chunk(png, "IHDR", ihdr);
            Chunk(png, "IDAT", z.ToArray());
            Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        private static void Chunk(Stream s, string type, byte[] data)
        {
            var head = new byte[8];
            PutInt(head, 0, data.Length);
            var t = System.Text.Encoding.ASCII.GetBytes(type);
            Array.Copy(t, 0, head, 4, 4);
            s.Write(head, 0, 8);
            s.Write(data, 0, data.Length);
            uint crc = Crc(t, 0xFFFFFFFFu);
            crc = Crc(data, crc) ^ 0xFFFFFFFFu;
            var c = new byte[4];
            PutInt(c, 0, (int)crc);
            s.Write(c, 0, 4);
        }

        private static uint[] crcTable;

        private static uint Crc(byte[] data, uint crc)
        {
            if (crcTable == null)
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                crcTable = table;
            }
            foreach (byte b in data) crc = crcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (byte x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
            return (b << 16) | a;
        }

        private static int BigInt(byte[] b, int i) => (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

        private static void PutInt(byte[] b, int i, int v)
        {
            b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16); b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v;
        }
    }
}
