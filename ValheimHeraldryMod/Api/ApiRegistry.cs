using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValheimHeraldryMod.Api
{
    /// <summary>
    ///     What other mods registered through <see cref="HeraldryAPI"/> (2026-10-25): faces named by path instead of by
    ///     tag, and providers of suggested arms. The tag scan (<see cref="Shields.TaggedModel.Scan"/>) reads the faces
    ///     from here, so a registered part goes down exactly the same road as a tagged one.
    /// </summary>
    internal static class ApiRegistry
    {
        internal sealed class Face
        {
            public string Path;       // from the prefab's root ("" = the root itself)
            public int Slot = -1;     // -1 = the whole object, else the material slot
            public int Number = 1;    // pattern 1–4
            public string CentrePath; // optional: an object marking the centre of the arms
        }

        internal sealed class Model
        {
            public readonly List<Face> Faces = new List<Face>();
            public bool UseUVs;
        }

        private static readonly Dictionary<string, Model> Models = new Dictionary<string, Model>();

        /// <summary>The faces registered for this prefab, or null.</summary>
        public static Model For(string prefabName) =>
            prefabName != null && Models.TryGetValue(prefabName, out var m) ? m : null;

        public static void Add(string prefabName, Face face, bool useUVs)
        {
            if (!Models.TryGetValue(prefabName, out var m)) Models[prefabName] = m = new Model();
            m.Faces.RemoveAll(f => f.Path == face.Path && f.Slot == face.Slot); // registering again replaces
            m.Faces.Add(face);
            if (useUVs) m.UseUVs = true; // all or nothing per model, as with the tag
            Shields.TaggedModel.Forget(prefabName);
            Banners.HeraldicPiecePatches.Forget(prefabName);
        }

        /// <summary>
        ///     The object at <paramref name="path"/> under <paramref name="root"/>. An instance can be rooted lower than its
        ///     prefab (a shield in hand is the prefab's "attach" child), so leading parts of the path are dropped until it
        ///     matches.
        /// </summary>
        public static Transform Find(Transform root, string path)
        {
            if (root == null) return null;
            string p = (path ?? "").Trim('/');
            while (true)
            {
                var t = p.Length == 0 ? root : root.Find(p);
                if (t != null) return t;
                int i = p.IndexOf('/');
                if (i < 0) return null;
                p = p.Substring(i + 1);
            }
        }

        // ================================================================== suggested arms (guild mods)

        internal sealed class Provider
        {
            public string Label;
            public Func<Player, string> Arms;
        }

        public static readonly List<Provider> Providers = new List<Provider>();

        /// <summary>Every provider's arms for the local player right now (label, stored blazon); failures are skipped.</summary>
        public static List<KeyValuePair<string, string>> SuggestedArms()
        {
            var result = new List<KeyValuePair<string, string>>();
            var player = Player.m_localPlayer;
            foreach (var p in Providers)
            {
                string arms;
                try { arms = p.Arms(player); }
                catch (Exception ex)
                {
                    Jotunn.Logger.LogWarning($"[Heraldry API] The arms provider '{p.Label}' failed: {ex.Message}");
                    continue;
                }
                if (!string.IsNullOrWhiteSpace(arms)) result.Add(new KeyValuePair<string, string>(p.Label, arms.Trim()));
            }
            return result;
        }

        // ================================================================== warnings, once each

        private static readonly HashSet<string> Warned = new HashSet<string>();

        public static void WarnOnce(string message)
        {
            if (Warned.Add(message)) Jotunn.Logger.LogWarning("[Heraldry API] " + message);
        }
    }
}
