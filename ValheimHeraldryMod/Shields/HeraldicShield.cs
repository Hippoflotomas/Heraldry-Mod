using System;
using UnityEngine;
using ValheimHeraldryMod.Rendering;
using ValheimHeraldryMod.Station;

namespace ValheimHeraldryMod.Shields
{
    /// <summary>
    ///     Paints arms on one shield model in the world: in a hand, on a back, on the ground or on an item stand.
    ///     Sits on the model's root; a few times a second it asks its source for the blazon (the player's ZDO, the
    ///     item's own data, the stand's ZDO) and, when that changes, swaps the shield's material for a painted copy
    ///     (or back). The shield item itself is untouched apart from the blazon in its custom data, so removing the
    ///     mod just leaves plain shields.
    /// </summary>
    internal class HeraldicShield : MonoBehaviour
    {
        private string prefabName;
        private Func<string> source;
        private string current;
        private float nextCheck;

        private MeshRenderer model;
        private Material[] original;
        private ShieldTextures.Entry entry;
        private bool applied;

        /// <summary>Starts (or retargets) painting on this model. <paramref name="source"/> returns the blazon or "".</summary>
        public static void Attach(GameObject root, string prefabName, Func<string> source)
        {
            if (root == null || string.IsNullOrEmpty(prefabName)) return;
            var s = root.GetComponent<HeraldicShield>() ?? root.AddComponent<HeraldicShield>();
            if (s.prefabName != prefabName) s.current = null; // force a redraw
            s.prefabName = prefabName;
            s.source = source;
            s.nextCheck = 0f;
        }

        /// <summary>The blazon stored on an item ("" for none).</summary>
        public static string BlazonOf(ItemDrop.ItemData item) =>
            item?.m_customData != null && item.m_customData.TryGetValue(HeraldTable.ItemBlazonKey, out var text) ? text ?? "" : "";

        public static bool IsShield(ItemDrop.ItemData item) =>
            item?.m_shared != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;

        private void Update()
        {
            // Put the painted material on as soon as its texture is baked
            if (entry != null && entry.Ready && !applied) Apply();

            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 0.25f;
            string text;
            try { text = BlazonTextureCache.Normalise(source?.Invoke() ?? ""); }
            catch { text = ""; } // the source's object went away
            if (text == current) return;
            current = text;
            Show(text);
        }

        private void Show(string text)
        {
            Restore();
            ShieldTextures.Release(entry);
            entry = null;
            if (text.Length == 0) return;

            if (model == null)
            {
                var mf = ShieldFace.FindModel(gameObject);
                if (mf == null) return;
                model = mf.GetComponent<MeshRenderer>();
                original = model.sharedMaterials;
            }
            var face = ShieldFace.For(prefabName, model.GetComponent<MeshFilter>());
            if (face == null) return;
            entry = ShieldTextures.Acquire(face, text);
            if (entry.Ready) Apply(); // until then the shield stays plain
        }

        private void Apply()
        {
            if (model == null || entry == null) return;
            var mats = new Material[original.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = entry.Painted(original[i]);
            model.sharedMaterials = mats;
            applied = true;
        }

        private void Restore()
        {
            if (applied && model != null) model.sharedMaterials = original;
            applied = false;
        }

        private void OnDestroy()
        {
            Restore();
            ShieldTextures.Release(entry);
            entry = null;
        }
    }
}
