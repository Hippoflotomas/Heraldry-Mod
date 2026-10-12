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

            // Dev-only: readback prototype (icons + fonts). Remove once the charge pipeline is settled.
            CommandManager.Instance.AddConsoleCommand(new Dev.ReadbackProbe());
        }
    }
}
