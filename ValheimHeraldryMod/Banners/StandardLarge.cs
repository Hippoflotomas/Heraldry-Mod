using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using ValheimHeraldryMod.Station;

namespace ValheimHeraldryMod.Banners
{
    /// <summary>
    ///     The large standard (Hippo's model from BannerShare): a free-standing frame with a cloth, built at runtime
    ///     from the embedded .hmesh like the herald's table. Its cloth keeps the material tag
    ///     <c>ValheimHeraldryMod_Face</c>, so it isn't a HeraldicBanner: the tagged-piece path finds and paints it,
    ///     exactly as it would another modder's piece. That makes this the test of that path.
    /// </summary>
    internal static class StandardLarge
    {
        public const string PrefabName = "ValheimHeraldryMod_StandardLarge";
        private const string ModelResource = "ValheimHeraldryMod.StandardLarge.hmesh";
        private const string Source = "piece_banner01"; // a banner's Piece, WearNTear, comfort and effects

        public static void Register()
        {
            try
            {
                Add();
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogError($"[Heraldry] The large standard couldn't be registered: {ex}");
            }
        }

        private static void Add()
        {
            LocalizationManager.Instance.GetLocalization().AddTranslation("English", new Dictionary<string, string>
            {
                { "ValheimHeraldryMod_StandardLarge", "Large standard" },
                { "ValheimHeraldryMod_StandardLarge_description", "A tall free-standing banner. Write a blazon on its cloth." },
            });

            var prefab = PrefabManager.Instance.CreateClonedPrefab(PrefabName, Source);
            if (prefab == null) throw new System.Exception("couldn't clone " + Source);
            var vanillaCloth = BannerRegistry.ClothRenderers(PrefabManager.Instance.GetPrefab(Source))
                .SelectMany(r => r.sharedMaterials).FirstOrDefault(BannerRegistry.IsCloth);

            // Out with the vanilla banner's looks, colliders and fragments
            foreach (Transform child in prefab.transform.Cast<Transform>().ToList())
                Object.DestroyImmediate(child.gameObject);

            int pieceLayer = LayerMask.NameToLayer("piece");
            var model = new GameObject("Model");
            model.transform.SetParent(prefab.transform, false);
            var materials = TableRegistry.VanillaMaterials();
            Bounds bounds = default;
            bool first = true;
            foreach (var part in HMesh.Load(ModelResource))
            {
                var go = new GameObject(part.Name == Shields.TaggedFace.FaceTag ? "Cloth" : part.Name);
                go.transform.SetParent(model.transform, false);
                Mesh mesh = part.Mesh;
                Material mat;
                if (part.Name == Shields.TaggedFace.FaceTag)
                {
                    // The cloth shader sways a mesh about its own origin, and the vanilla cloth's origin is its top
                    // edge (checked in the rip): move ours there too, so the bottom flaps and the top stays put.
                    var b = mesh.bounds;
                    var top = new Vector3(b.center.x, b.max.y, b.center.z);
                    mesh.vertices = mesh.vertices.Select(v => v - top).ToArray();
                    mesh.RecalculateBounds();
                    go.transform.localPosition = top;
                    mat = ClothMaterial(vanillaCloth);
                }
                else
                {
                    go.layer = pieceLayer;
                    mat = materials.TryGetValue(part.Name, out var m) ? m : materials["Wood"];
                }
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                var wb = new Bounds(go.transform.localPosition + mesh.bounds.center, mesh.bounds.size);
                if (first) { bounds = wb; first = false; }
                else bounds.Encapsulate(wb);
            }

            // One thin box over frame and cloth: something to hover and hit
            var colliderObject = new GameObject("collider") { layer = pieceLayer };
            colliderObject.transform.SetParent(prefab.transform, false);
            var box = colliderObject.AddComponent<BoxCollider>();
            float bottom = Mathf.Max(bounds.min.y, 0f);
            box.center = new Vector3(bounds.center.x, (bottom + bounds.max.y) / 2f, bounds.center.z);
            box.size = new Vector3(bounds.size.x, bounds.max.y - bottom, Mathf.Max(bounds.size.z, 0.2f));

            var wnt = prefab.GetComponent<WearNTear>();
            if (wnt != null)
            {
                wnt.m_new = model;
                wnt.m_worn = model;
                wnt.m_broken = model;
                wnt.m_fragmentRoots = new GameObject[0];
            }

            var piece = prefab.GetComponent<Piece>();
            piece.m_name = "$ValheimHeraldryMod_StandardLarge";
            piece.m_description = "$ValheimHeraldryMod_StandardLarge_description";
            piece.m_notOnFloor = false;           // it stands on the ground or a floor
            piece.m_notOnTiltingSurface = false;

            var config = new PieceConfig
            {
                Name = piece.m_name,
                Description = piece.m_description,
                PieceTable = PieceTables.Hammer,
                Category = BannerRegistry.Category,
                CraftingStation = CraftingStations.Workbench,
                Requirements = new[]
                {
                    new RequirementConfig("Wood", 6, 0, true),
                    new RequirementConfig("Stone", 4, 0, true),
                    new RequirementConfig("LeatherScraps", 4, 0, true),
                },
            };
            var icon = Icon(prefab);
            if (icon != null) config.Icon = icon;
            PieceManager.Instance.AddPiece(new CustomPiece(prefab, false, config));
            Jotunn.Logger.LogDebug($"[Heraldry] Registered the large standard ({bounds.size.x:0.00} x {bounds.size.y:0.00} m); " +
                                  $"its cloth is a tagged face, painted through the tagged-piece path.");
        }

        /// <summary>
        ///     The vanilla banner cloth (sway, ripple, cutout for tatters), without the vanilla emblem, plain linen
        ///     until painted, and named with the face tag so the tagged-piece path finds it.
        /// </summary>
        private static Material ClothMaterial(Material vanilla)
        {
            if (vanilla == null)
            {
                Jotunn.Logger.LogWarning("[Heraldry] No vanilla banner cloth found for the large standard; using wood.");
                vanilla = TableRegistry.VanillaMaterials()["Wood"];
            }
            var m = new Material(vanilla) { name = Shields.TaggedFace.FaceTag };
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return m; // dedicated server: nothing is drawn
            HeraldicBanner.StripVanillaArt(m);
            var linen = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Heraldry_Linen" };
            linen.SetPixels32(Enumerable.Repeat(Rendering.BlazonTextureCache.Linen, 16).ToArray());
            linen.Apply(false, true);
            m.mainTexture = linen;
            m.mainTextureScale = Vector2.one;
            m.mainTextureOffset = Vector2.zero;
            if (m.HasProperty("_Color")) m.color = Color.white;
            return m;
        }

        private static Sprite Icon(GameObject prefab)
        {
            try
            {
                return RenderManager.Instance.Render(new RenderManager.RenderRequest(prefab)
                {
                    Rotation = Quaternion.Euler(0f, 180f, 0f),
                    Width = 128,
                    Height = 128,
                });
            }
            catch (System.Exception ex)
            {
                Jotunn.Logger.LogWarning($"[Heraldry] Couldn't render the large standard's icon ({ex.Message}).");
                return null;
            }
        }
    }
}
