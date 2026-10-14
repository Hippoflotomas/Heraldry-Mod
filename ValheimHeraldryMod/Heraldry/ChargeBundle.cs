using System;
using System.IO;
using System.Text;

namespace ValheimHeraldryMod.Heraldry
{
    /// <summary>
    ///     One picture charge packed to travel on its own (2026-10-14): its picture, its optional mask (lines,
    ///     armed, langued), its names and attitudes, and the pack it came from. The fingerprint (first 16 hex of
    ///     SHA-256 over the bytes) is its identity: blazons name it, players approve it, admins blacklist it. Any
    ///     change makes a new fingerprint. Servers only pass the bytes along; nothing here needs graphics.
    /// </summary>
    public sealed class ChargeBundle
    {
        public const int FingerprintLength = 16;
        /// <summary>Picture-art key of a charge by fingerprint ("charge:1a2b...").</summary>
        public const string ArtKeyPrefix = "charge:";
        public static string ArtKey(string fingerprint) => ArtKeyPrefix + fingerprint;
        private const string Magic = "HCHG";
        private const int Version = 1;

        public string Pack = "";
        public string[] Names = new string[0];
        public string[] Attitudes = new string[0];
        public bool NoLines;
        public byte[] Image;
        public byte[] Mask; // null = none

        /// <summary>The bundle as it travels and is stored.</summary>
        public byte[] Bytes { get; private set; }
        public string Fingerprint { get; private set; }

        /// <summary>The name to show for it: its first name.</summary>
        public string Name => Names.Length > 0 ? Names[0] : Fingerprint;

        public static ChargeBundle Create(string pack, string[] names, string[] attitudes, bool noLines, byte[] image, byte[] mask)
        {
            var b = new ChargeBundle
            {
                Pack = pack ?? "",
                Names = names ?? new string[0],
                Attitudes = attitudes ?? new string[0],
                NoLines = noLines,
                Image = image,
                Mask = mask != null && mask.Length > 0 ? mask : null,
            };
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(Encoding.ASCII.GetBytes(Magic));
                w.Write(Version);
                w.Write(b.Pack);
                w.Write(b.Names.Length);
                foreach (var n in b.Names) w.Write(n ?? "");
                w.Write(b.Attitudes.Length);
                foreach (var a in b.Attitudes) w.Write(a ?? "");
                w.Write(b.NoLines);
                w.Write(image.Length);
                w.Write(image);
                w.Write(b.Mask?.Length ?? 0);
                if (b.Mask != null) w.Write(b.Mask);
            }
            b.Bytes = ms.ToArray();
            b.Fingerprint = FingerprintOf(b.Bytes);
            return b;
        }

        /// <summary>Reads a bundle; throws InvalidDataException if it's damaged, too big or not a bundle.</summary>
        public static ChargeBundle Read(byte[] bytes, long maxBytes)
        {
            if (bytes == null || bytes.Length < 16) throw new InvalidDataException("not a charge");
            if (bytes.Length > maxBytes) throw new InvalidDataException($"{bytes.Length >> 10} KB, over the {maxBytes >> 10} KB limit");
            try
            {
                using (var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                {
                    if (Encoding.ASCII.GetString(r.ReadBytes(4)) != Magic) throw new InvalidDataException("not a charge");
                    int version = r.ReadInt32();
                    if (version != Version) throw new InvalidDataException("charge format " + version + " (a newer mod version?)");
                    var b = new ChargeBundle { Pack = r.ReadString() };
                    b.Names = ReadStrings(r);
                    b.Attitudes = ReadStrings(r);
                    b.NoLines = r.ReadBoolean();
                    b.Image = ReadBlob(r, bytes.Length);
                    var mask = ReadBlob(r, bytes.Length);
                    b.Mask = mask.Length > 0 ? mask : null;
                    if (b.Image.Length == 0) throw new InvalidDataException("charge without a picture");
                    b.Bytes = bytes;
                    b.Fingerprint = FingerprintOf(bytes);
                    return b;
                }
            }
            catch (EndOfStreamException)
            {
                throw new InvalidDataException("damaged charge");
            }
        }

        private static string[] ReadStrings(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 64) throw new InvalidDataException("damaged charge");
            var a = new string[n];
            for (int i = 0; i < n; i++)
            {
                a[i] = r.ReadString();
                if (a[i].Length > 100) throw new InvalidDataException("damaged charge");
            }
            return a;
        }

        private static byte[] ReadBlob(BinaryReader r, int total)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > total) throw new InvalidDataException("damaged charge");
            var b = r.ReadBytes(n);
            if (b.Length != n) throw new InvalidDataException("damaged charge");
            return b;
        }

        public static string FingerprintOf(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var h = sha.ComputeHash(bytes);
                var sb = new StringBuilder(FingerprintLength);
                for (int i = 0; i < FingerprintLength / 2; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
