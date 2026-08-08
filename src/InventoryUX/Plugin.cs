using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using InventoryUX.Runtime;

namespace InventoryUX
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        // Retain the original GUID so existing users keep their BepInEx settings
        // and an old InventoryUX DLL cannot load beside CraftGuard by accident.
        internal const string PluginGuid = "com.inventoryux.valheim";
        internal const string PluginName = "CraftGuard";
        internal const string PluginVersion = "0.2.0";

        internal static ManualLogSource LogInstance { get; private set; } = null!;
        internal static Plugin Instance { get; private set; } = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Instance = this;
            LogInstance = Logger;
            ModConfig.Bind(Config);

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Undiscovered content is never rendered.");
        }

        private void Update()
        {
            if (ModConfig.Enabled.Value && ModConfig.WriteDataInventoryOnStartup.Value)
            {
                DataInventoryWriter.TryWriteOnce();
            }
        }

        private void OnDestroy()
        {
            HammerGroupDecorations.Shutdown();
            HammerGridSizer.Restore();
            _harmony?.UnpatchSelf();
        }
    }
}
