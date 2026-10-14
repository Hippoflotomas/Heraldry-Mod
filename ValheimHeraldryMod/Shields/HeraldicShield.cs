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

        // Another modder's tagged face: the arms go on the face itself
        private bool taggedChecked;
        private TaggedFacePainter tagged;
        private string taggedKey;

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
            string key = text.IndexOf('[') >= 0 ? text + "|" + ChargeLibrary.Generation : text; // picture charges arriving: redraw
            if (key == current) return;
            current = key;
            Show(text);
        }

        private void Show(string text)
        {
            Restore();
            ShieldTextures.Release(entry);
            entry = null;

            if (!taggedChecked)
            {
                taggedChecked = true;
                tagged = TaggedFacePainter.On(gameObject);
            }
            if (tagged != null)
            {
                ShowTagged(text);
                return;
            }
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

        /// <summary>Arms on a tagged face: rendered in its own flat outline, around its centre point.</summary>
        private void ShowTagged(string text)
        {
            BlazonTextureCache.Release(taggedKey);
            taggedKey = null;
            if (text.Length == 0)
            {
                tagged.Hide();
                return;
            }
            var f = tagged.Face;
            const int Longest = 512;
            int w = f.Aspect >= 1f ? Longest : Mathf.Max(32, Mathf.RoundToInt(Longest * f.Aspect));
            int h = f.Aspect >= 1f ? Mathf.Max(32, Mathf.RoundToInt(Longest / f.Aspect)) : Longest;
            var shape = new BlazonTextureCache.Shape
            {
                Id = "Heraldry_Tagged_" + f.Key, UV = f.OutlineUV, Triangles = f.OutlineTriangles,
                Arrangement = Heraldry.FieldRenderer.Arrangement.Compact, CentreU = f.CentreU, CentreV = f.CentreV,
            };
            var tex = BlazonTextureCache.Acquire(text, w, h, shape, out taggedKey);
            tagged.Show(tex);
        }

        private void OnDestroy()
        {
            tagged?.Destroy();
            BlazonTextureCache.Release(taggedKey);
            taggedKey = null;
            Restore();
            ShieldTextures.Release(entry);
            entry = null;
        }
    }
}
