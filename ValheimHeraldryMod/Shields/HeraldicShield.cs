using System;
using System.Linq;
using UnityEngine;
using ValheimHeraldryMod.Rendering;
using ValheimHeraldryMod.Station;

namespace ValheimHeraldryMod.Shields
{
    /// <summary>
    ///     Paints arms on one shield model in the world: in a hand, on a back, on the ground or on an item stand.
    ///     Sits on the model's root; a few times a second it asks its source for the blazon (the player's ZDO, the
    ///     item's own data, the stand's ZDO) and, when that changes, puts the face overlay on or takes it off.
    ///     The shield item itself is untouched apart from the blazon in its custom data, so removing the mod just
    ///     leaves plain shields.
    /// </summary>
    internal class HeraldicShield : MonoBehaviour
    {
        /// <summary>Marks our overlay, so it's never mistaken for the shield's own model.</summary>
        internal sealed class Overlay : MonoBehaviour { }

        private const int TextureSize = 512; // longest side
        private static Material material;
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");

        private string prefabName;
        private Func<string> source;
        private string current;
        private string cacheKey;
        private GameObject overlay;
        private MeshRenderer overlayRenderer;
        private MaterialPropertyBlock block;
        private float nextCheck;

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
            BlazonTextureCache.Release(cacheKey);
            cacheKey = null;
            if (text.Length == 0)
            {
                if (overlay != null) overlay.SetActive(false);
                return;
            }

            if (overlay == null && !Build()) return;
            var face = faceInfo;
            int w = face.Aspect >= 1f ? TextureSize : Mathf.Max(32, Mathf.RoundToInt(TextureSize * face.Aspect));
            int h = face.Aspect >= 1f ? Mathf.Max(32, Mathf.RoundToInt(TextureSize / face.Aspect)) : TextureSize;
            var shape = new BlazonTextureCache.Shape
            {
                Id = face.ShapeId,
                UV = face.OutlineUV,
                Triangles = face.OutlineTriangles,
                Arrangement = Heraldry.FieldRenderer.Arrangement.Compact,
            };
            var tex = BlazonTextureCache.Acquire(text, w, h, shape, out cacheKey);
            block = block ?? new MaterialPropertyBlock();
            overlayRenderer.GetPropertyBlock(block);
            block.SetTexture(MainTex, tex);
            overlayRenderer.SetPropertyBlock(block);
            overlay.SetActive(true);
        }

        private ShieldFace faceInfo;

        private bool Build()
        {
            var model = ShieldFace.FindModel(gameObject);
            faceInfo = ShieldFace.For(prefabName, model);
            var mat = Material();
            if (faceInfo == null || mat == null) return false;

            overlay = new GameObject("HeraldryFace", typeof(MeshFilter), typeof(MeshRenderer), typeof(Overlay));
            overlay.layer = model.gameObject.layer;
            overlay.transform.SetParent(model.transform, false);
            overlay.GetComponent<MeshFilter>().sharedMesh = faceInfo.Mesh;
            overlayRenderer = overlay.GetComponent<MeshRenderer>();
            overlayRenderer.sharedMaterial = mat;
            overlayRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // the shield already casts one
            return true;
        }

        private void OnDestroy()
        {
            BlazonTextureCache.Release(cacheKey);
            cacheKey = null;
        }

        /// <summary>
        ///     One shared material for every painted face (each face's texture goes in a property block): the wood
        ///     wall's, as on the herald's table, which is known to show our textures; its plank relief is turned off.
        /// </summary>
        private static Material Material()
        {
            if (material != null) return material;
            var wall = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("woodwall") : null;
            var wood = wall?.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial)
                .FirstOrDefault(m => m != null && m.name.StartsWith("woodwall", StringComparison.OrdinalIgnoreCase));
            if (wood == null)
            {
                Jotunn.Logger.LogWarning("[Heraldry] No material for painted shields (woodwall not found).");
                return null;
            }
            material = new Material(wood) { name = "Heraldry_ShieldFace", color = Color.white };
            material.mainTextureScale = Vector2.one;
            material.mainTextureOffset = Vector2.zero;
            if (material.HasProperty("_BumpMap")) material.SetTexture("_BumpMap", null);
            Jotunn.Logger.LogInfo($"[Heraldry] Painted shields use '{wood.name}' ({wood.shader.name}).");
            return material;
        }
    }
}
