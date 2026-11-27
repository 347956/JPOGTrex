using BepInEx;
using BepInEx.Logging;
using Dawn;
using JPOGTrex.Configuration;

namespace JPOGTrex {
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(DawnLib.PLUGIN_GUID)]
    public class Plugin : BaseUnityPlugin {
        internal static new ManualLogSource Logger = null!;
        internal static PluginConfig BoundConfig { get; private set; } = null!;

        public static readonly NamespacedKey<DawnEnemyInfo> TrexKey = NamespacedKey<DawnEnemyInfo>.From("jpog_trex", "trex");

        private void Awake() {
            Logger = base.Logger;
            BoundConfig = new PluginConfig(base.Config);
            Logger.LogInfo($"Plugin [{PluginInfo.PLUGIN_GUID}] is loaded!");
        }
    }
}