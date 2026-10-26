using BepInEx;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;

namespace ValheimHeraldryMod
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    // Mandatory on the server and every client, with exactly matching versions.
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
    internal class ValheimHeraldryMod : BaseUnityPlugin
    {
        public const string PluginGUID = "com.hippotech.valheimheraldrymod";
        public const string PluginName = "Valheim Heraldry Mod";
        public const string PluginVersion = "1.5.0";

        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        /// <summary>The plugin, for coroutines.</summary>
        public static ValheimHeraldryMod Instance { get; private set; }

        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private void Awake()
        {
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} has landed");

            Instance = this;
            HeraldryConfig.Bind(Config);
            // Placeholder instead: blocked/blacklisted picture charges, and game icons the server excludes
            Heraldry.ChargeArtLibrary.IsBlocked = key => Rendering.ChargeLibrary.Instance.IsArtKeyHidden(key) || Rendering.IconArt.IsBlocked(key);
            Heraldry.Lexicon.GameItems = Rendering.IconArt.Instance;
            Heraldry.Lexicon.SecretsAllowed = () => HeraldryConfig.AllowSecretBlazons;
            Heraldry.Lexicon.MaxInscription = () => HeraldryConfig.MaxInscriptionLength;
            // Your own charges (a dedicated server has no player, only its server folder, loaded when it starts)
            if (UnityEngine.SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
                Rendering.ChargeLibrary.Instance.LoadOwn();
            Rendering.ChargeSync.Init();
            Rendering.ChargeCommands.Register();
            Heraldry.Lexicon.Packs = Rendering.ChargeLibrary.Instance;
            Banners.BannerRegistry.Init();
            Station.TableRegistry.Init();
            Tabards.TabardItem.Init();
            Paints.PaintItems.Init();

            // Patches applied one class at a time, so if a game update breaks one, only that feature is lost
            var harmony = new HarmonyLib.Harmony(PluginGUID);
            foreach (var type in new[] { typeof(Station.TablePatches), typeof(Shields.ShieldPatches), typeof(Shields.ShieldTooltipPatch), typeof(Tabards.TabardEquip), typeof(Tabards.TabardVisuals), typeof(Rendering.ItemIcons), typeof(Banners.HeraldicPiecePatches), typeof(Rendering.LogoArt), typeof(UI.Compendium), typeof(Station.HuginTutorial) })
            {
                try
                {
                    harmony.PatchAll(type);
                }
                catch (System.Exception ex)
                {
                    Jotunn.Logger.LogError($"[Heraldry] Patch {type.Name} failed: {ex.Message}");
                }
            }
        }

        private void Update()
        {
            // Upload banner textures that finished rendering on worker threads
            Rendering.BlazonTextureCache.Pump();
            Shields.ShieldTextures.Pump();
            Tabards.TabardTextures.Pump();
            Rendering.ItemIcons.Pump();
            Banners.HeraldicPiece.CheckAlternativeKey();
            // Notice joining and leaving other people's servers (their charge packs)
            Rendering.ChargeSync.Update();
        }
    }
}
