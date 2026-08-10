using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using InventoryUX.Runtime;
using System;

namespace InventoryUX
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        // Retain the original GUID so existing users keep their BepInEx settings
        // and an old InventoryUX DLL cannot load beside CraftIndex by accident.
        internal const string PluginGuid = "com.inventoryux.valheim";
        internal const string PluginName = "CraftIndex";
        internal const string PluginVersion = "0.3.0";

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

        private void OnDestroy()
        {
            Cleanup("Hammer UI", HammerGroupDecorations.Shutdown);
            Cleanup("Hammer sizing", HammerGridSizer.Restore);
            Cleanup("Hammer caches", HammerOrganizer.Reset);
            Cleanup("recipe UI", RecipeOrganizer.Shutdown);
            Cleanup("food cache", FoodStatsResolver.Reset);
            _harmony?.UnpatchSelf();
        }

        private static void Cleanup(string name, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                LogInstance.LogWarning($"CraftIndex could not fully release {name} during shutdown: {exception}");
            }
        }
    }
}
