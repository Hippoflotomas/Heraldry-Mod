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
        public const string PluginVersion = "0.0.1";

        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private void Awake()
        {
            Jotunn.Logger.LogInfo($"{PluginName} {PluginVersion} has landed");

            HeraldryConfig.Bind(Config);
            Heraldry.ChargeArtLibrary.IsBlocked = Rendering.IconArt.IsBlocked;
            Heraldry.Lexicon.GameItems = Rendering.IconArt.Instance;
            Rendering.ChargePacks.Instance.LoadLocal();
            Rendering.PackSync.Init();
            CommandManager.Instance.AddConsoleCommand(new Rendering.PacksCommand());
            CommandManager.Instance.AddConsoleCommand(new Rendering.TrustCommand());
            CommandManager.Instance.AddConsoleCommand(new Rendering.UntrustCommand());
            Heraldry.Lexicon.Packs = Rendering.ChargePacks.Instance;
            Banners.BannerRegistry.Init();
            Station.TableRegistry.Init();

            // Patches applied one class at a time, so if a game update breaks one, only that feature is lost
            var harmony = new HarmonyLib.Harmony(PluginGUID);
            foreach (var type in new[] { typeof(Station.TablePatches), typeof(Shields.ShieldPatches), typeof(Shields.ShieldTooltipPatch) })
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

            // Dev-only: readback prototype (icons + fonts). Remove once the charge pipeline is settled.
            CommandManager.Instance.AddConsoleCommand(new Dev.ReadbackProbe());
        }

        private void Update()
        {
            // Upload banner textures that finished rendering on worker threads
            Rendering.BlazonTextureCache.Pump();
            // Notice joining and leaving other people's servers (their charge packs)
            Rendering.PackSync.Update();
        }
    }
}
