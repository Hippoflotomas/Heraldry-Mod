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
        private Func<int, string> source;
        private string current;
        private float nextCheck;

        private MeshRenderer model;
        private Material[] original;
        private ShieldTextures.Entry entry;
        private bool applied;

        // Another modder's tagged faces: the arms go on the faces themselves (up to four patterns)
        private bool taggedChecked;
        private TaggedFacePainter tagged;

        /// <summary>
        ///     Starts (or retargets) painting on this model. <paramref name="source"/> returns a pattern's blazon (1–4)
        ///     or ""; vanilla shields only ever ask for pattern 1.
        /// </summary>
        public static void Attach(GameObject root, string prefabName, Func<int, string> source)
        {
            if (root == null || string.IsNullOrEmpty(prefabName)) return;
            var s = root.GetComponent<HeraldicShield>() ?? root.AddComponent<HeraldicShield>();
            if (s.prefabName != prefabName)
            {
                s.current = null; // force a redraw
                s.tagged?.Destroy();
                s.tagged = null;
                s.taggedChecked = false;
            }
            s.prefabName = prefabName;
            s.source = source;
            s.nextCheck = 0f;
        }

        /// <summary>The blazon stored on an item ("" for none): pattern 1 unless another is asked for.</summary>
        public static string BlazonOf(ItemDrop.ItemData item, int pattern = 1) =>
            item?.m_customData != null && item.m_customData.TryGetValue(Patterns.Key(HeraldTable.ItemBlazonKey, pattern), out var text) ? text ?? "" : "";

        public static bool IsShield(ItemDrop.ItemData item) =>
            item?.m_shared != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;

        private void Update()
        {
            // Put the painted material on as soon as its texture is baked
            if (entry != null && entry.Ready && !applied) Apply();

            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 0.25f;
            if (!taggedChecked)
            {
                taggedChecked = true;
                tagged = TaggedFacePainter.On(gameObject, prefabName);
            }
            if (tagged != null)
            {
                // Tagged faces: every pattern, each part redrawn only when its own arms change
                tagged.Refresh(Safe, 512, _ => Heraldry.FieldRenderer.Arrangement.Compact, "Heraldry_Tagged_");
                return;
            }

            string text = BlazonTextureCache.Normalise(Safe(1));
            string key = text.IndexOf('[') >= 0 ? text + "|" + ChargeLibrary.Generation : text; // picture charges arriving: redraw
            if (key == current) return;
            current = key;
            Show(text);
        }

        private string Safe(int pattern)
        {
            try { return source?.Invoke(pattern) ?? ""; }
            catch { return ""; } // the source's object went away
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
            tagged?.Destroy();
            Restore();
            ShieldTextures.Release(entry);
            entry = null;
        }
    }
}
