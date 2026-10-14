using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     Just enough zip for charge packs, on top of DeflateStream (in System.dll, which Jötunn already relies
    ///     on in game; the separate System.IO.Compression.dll with ZipArchive may not ship with Valheim).
    ///     Reads stored and deflated entries via the central directory (so zips from Windows, 7-Zip and the
    ///     like work); writes deterministic zips (sorted names, fixed timestamps) so the same pack always has the
    ///     same fingerprint. No zip64, no encryption. Limits are enforced while inflating, so a "zip bomb" stops
    ///     at the limit instead of filling memory.
    /// </summary>
    public static class MiniZip
    {
        public sealed class Limits
        {
            public int MaxEntries = 512;
            public long MaxEntryBytes = 16L << 20;
            public long MaxTotalBytes = 64L << 20;
        }

        /// <summary>Entries by name (forward slashes, as stored). Throws InvalidDataException on anything unexpected.</summary>
        public static Dictionary<string, byte[]> Read(byte[] zip, Limits limits)
        {
            // End of central directory: 22 bytes, possibly followed by a comment of up to 64 KB
            int eocd = -1;
            for (int i = zip.Length - 22; i >= Math.Max(0, zip.Length - 22 - 65535); i--)
                if (U32(zip, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0) throw new InvalidDataException("not a zip file");
            int count = U16(zip, eocd + 10);
            long cdOffset = U32(zip, eocd + 16);
            if (count > limits.MaxEntries) throw new InvalidDataException($"too many files ({count})");

            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            int p = (int)cdOffset;
            for (int n = 0; n < count; n++)
            {
                if (p < 0 || p + 46 > zip.Length || U32(zip, p) != 0x02014b50) throw new InvalidDataException("damaged central directory");
                int flags = U16(zip, p + 8), method = U16(zip, p + 10);
                long csize = U32(zip, p + 20), usize = U32(zip, p + 24);
                int nameLen = U16(zip, p + 28), extraLen = U16(zip, p + 30), commentLen = U16(zip, p + 32);
                long localOffset = U32(zip, p + 42);
                if (p + 46 + nameLen > zip.Length) throw new InvalidDataException("damaged central directory");
                string name = Encoding.UTF8.GetString(zip, p + 46, nameLen).Replace('\\', '/');
                p += 46 + nameLen + extraLen + commentLen;

                if (name.EndsWith("/")) continue; // folder entry
                if ((flags & 1) != 0) throw new InvalidDataException($"'{name}' is encrypted");
                if (usize > limits.MaxEntryBytes) throw new InvalidDataException($"'{name}' is too big ({usize >> 10} KB)");
                total += usize;
                if (total > limits.MaxTotalBytes) throw new InvalidDataException("the pack is too big unpacked");

                if (localOffset + 30 > zip.Length || U32(zip, (int)localOffset) != 0x04034b50) throw new InvalidDataException($"'{name}': damaged entry");
                int dataStart = (int)localOffset + 30 + U16(zip, (int)localOffset + 26) + U16(zip, (int)localOffset + 28);
                if (dataStart + csize > zip.Length) throw new InvalidDataException($"'{name}': truncated");

                byte[] data;
                if (method == 0)
                {
                    data = new byte[usize];
                    Array.Copy(zip, dataStart, data, 0, usize);
                }
                else if (method == 8)
                    data = Inflate(zip, dataStart, (int)csize, usize, name);
                else throw new InvalidDataException($"'{name}' uses an unsupported compression method ({method})");

                if (Crc32(data) != U32(zip, p - (46 + nameLen + extraLen + commentLen) + 16))
                    throw new InvalidDataException($"'{name}' is corrupt (checksum)");
                result[name] = data;
            }
            return result;
        }

        private static byte[] Inflate(byte[] zip, int start, int length, long expected, string name)
        {
            using (var input = new MemoryStream(zip, start, length, false))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream((int)Math.Min(expected, int.MaxValue)))
            {
                var buffer = new byte[16384];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read);
                    if (output.Length > expected) throw new InvalidDataException($"'{name}' unpacks to more than it says");
                }
                if (output.Length != expected) throw new InvalidDataException($"'{name}' is truncated");
                return output.ToArray();
            }
        }

        /// <summary>A deterministic zip: entries sorted by name, fixed timestamp, deflated.</summary>
        public static byte[] Write(IDictionary<string, byte[]> files)
        {
            var names = new List<string>(files.Keys);
            names.Sort(StringComparer.Ordinal);
            using (var zip = new MemoryStream())
            using (var central = new MemoryStream())
            {
                const ushort Time = 0, Date = (2026 - 1980) << 9 | 1 << 5 | 1; // 2026-01-01 00:00
                foreach (var name in names)
                {
                    byte[] data = files[name];
                    byte[] packed = Deflate(data);
                    bool store = packed.Length >= data.Length;
                    if (store) packed = data;
                    byte[] nameBytes = Encoding.UTF8.GetBytes(name.Replace('\\', '/'));
                    uint crc = Crc32(data);
                    long offset = zip.Position;

                    W32(zip, 0x04034b50); W16(zip, 20); W16(zip, 0x0800); W16(zip, store ? 0 : 8);
                    W16(zip, Time); W16(zip, Date); W32(zip, crc); W32(zip, (uint)packed.Length); W32(zip, (uint)data.Length);
                    W16(zip, nameBytes.Length); W16(zip, 0);
                    zip.Write(nameBytes, 0, nameBytes.Length);
                    zip.Write(packed, 0, packed.Length);

                    W32(central, 0x02014b50); W16(central, 20); W16(central, 20); W16(central, 0x0800); W16(central, store ? 0 : 8);
                    W16(central, Time); W16(central, Date); W32(central, crc); W32(central, (uint)packed.Length); W32(central, (uint)data.Length);
                    W16(central, nameBytes.Length); W16(central, 0); W16(central, 0); W16(central, 0); W16(central, 0);
                    W32(central, 0); W32(central, (uint)offset);
                    central.Write(nameBytes, 0, nameBytes.Length);
                }
                long cdStart = zip.Position;
                central.Position = 0;
                central.CopyTo(zip);
                W32(zip, 0x06054b50); W16(zip, 0); W16(zip, 0); W16(zip, names.Count); W16(zip, names.Count);
                W32(zip, (uint)central.Length); W32(zip, (uint)cdStart); W16(zip, 0);
                return zip.ToArray();
            }
        }

        private static byte[] Deflate(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionMode.Compress, true))
                    deflate.Write(data, 0, data.Length);
                return output.ToArray();
            }
        }

        /// <summary>SHA-256 of the zip, as lower-case hex: a pack's fingerprint.</summary>
        public static string Fingerprint(byte[] zip)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var hash = sha.ComputeHash(zip);
                var sb = new StringBuilder(64);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static readonly uint[] CrcTable = MakeCrcTable();

        private static uint[] MakeCrcTable()
        {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[n] = c;
            }
            return t;
        }

        public static uint Crc32(byte[] data)
        {
            uint c = 0xFFFFFFFFu;
            foreach (byte b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
            return c ^ 0xFFFFFFFFu;
        }

        private static int U16(byte[] b, int i) => b[i] | b[i + 1] << 8;
        private static uint U32(byte[] b, int i) => (uint)(b[i] | b[i + 1] << 8 | b[i + 2] << 16 | b[i + 3] << 24);

        private static void W16(Stream s, int v)
        {
            s.WriteByte((byte)v);
            s.WriteByte((byte)(v >> 8));
        }

        private static void W32(Stream s, uint v)
        {
            s.WriteByte((byte)v);
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)(v >> 16));
            s.WriteByte((byte)(v >> 24));
        }
    }
}
