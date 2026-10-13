using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;

namespace ValheimHeraldryMod.UI
{
    /// <summary>
    ///     The player's named blazons: one JSON file on this computer, so they carry across servers.
    ///     BepInEx/config/Heraldry/saved_blazons.json
    /// </summary>
    internal static class SavedBlazons
    {
        [Serializable]
        public class Entry
        {
            public string name;
            public string blazon;
        }

        [Serializable]
        private class FileData
        {
            public List<Entry> entries = new List<Entry>();
        }

        private static FileData data;

        private static string FilePath => Path.Combine(Path.Combine(Paths.ConfigPath, "Heraldry"), "saved_blazons.json");

        public static List<Entry> All
        {
            get
            {
                if (data == null) Load();
                return data.entries;
            }
        }

        private static void Load()
        {
            data = new FileData();
            try
            {
                if (File.Exists(FilePath))
                    data = JsonUtility.FromJson<FileData>(File.ReadAllText(FilePath)) ?? new FileData();
                if (data.entries == null) data.entries = new List<Entry>();
            }
            catch (Exception ex)
            {
                // Keep the broken file rather than overwrite someone's list with an empty one
                Jotunn.Logger.LogError($"[Heraldry] Couldn't read {FilePath}: {ex.Message}");
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
                data = new FileData();
            }
        }

        private static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(data, true));
            }
            catch (Exception ex)
            {
                Jotunn.Logger.LogError($"[Heraldry] Couldn't write {FilePath}: {ex.Message}");
            }
        }

        /// <summary>Adds or replaces (same name, any case). Returns true if it replaced one.</summary>
        public static bool Put(string name, string blazon)
        {
            name = name.Trim();
            foreach (var e in All)
                if (string.Equals(e.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    e.blazon = blazon;
                    Save();
                    return true;
                }
            All.Add(new Entry { name = name, blazon = blazon });
            All.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            Save();
            return false;
        }

        public static void Remove(Entry e)
        {
            All.Remove(e);
            Save();
        }

        /// <summary>Default name for a new entry: the first few words.</summary>
        public static string SuggestName(string blazon)
        {
            var words = (blazon ?? "").Replace(",", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string name = string.Join(" ", words, 0, Math.Min(4, words.Length));
            if (name.Length > 0) name = char.ToUpperInvariant(name[0]) + name.Substring(1);
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }
    }
}
