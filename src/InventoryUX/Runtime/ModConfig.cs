using BepInEx.Configuration;
using InventoryUX.Core;

namespace InventoryUX.Runtime
{
    internal static class ModConfig
    {
        internal static ConfigFile File { get; private set; } = null!;

        internal static ConfigEntry<bool> Enabled { get; private set; } = null!;
        internal static ConfigEntry<bool> ShowSeparators { get; private set; } = null!;
        internal static ConfigEntry<bool> ShowHammerPieceNames { get; private set; } = null!;
        internal static ConfigEntry<string> HammerToolViews { get; private set; } = null!;
        internal static ConfigEntry<bool> OrganizeCrafting { get; private set; } = null!;
        internal static ConfigEntry<bool> OrganizeBuilding { get; private set; } = null!;
        internal static ConfigEntry<bool> OrganizeHeavyBuilding { get; private set; } = null!;
        internal static ConfigEntry<bool> OrganizeFurniture { get; private set; } = null!;
        internal static ConfigEntry<bool> OrganizeRecipes { get; private set; } = null!;
        internal static ConfigEntry<string> WorkbenchGrouping { get; private set; } = null!;
        internal static ConfigEntry<string> FoodGrouping { get; private set; } = null!;
        internal static ConfigEntry<bool> WriteDataInventoryOnStartup { get; private set; } = null!;

        internal static EquipmentGroupingMode EquipmentMode
        {
            get
            {
                if (string.Equals(WorkbenchGrouping.Value, "Family", System.StringComparison.OrdinalIgnoreCase))
                {
                    return EquipmentGroupingMode.Type;
                }

                if (string.Equals(WorkbenchGrouping.Value, "Tier", System.StringComparison.OrdinalIgnoreCase))
                {
                    return EquipmentGroupingMode.Biome;
                }

                return System.Enum.TryParse(WorkbenchGrouping.Value, true, out EquipmentGroupingMode mode)
                    ? mode
                    : EquipmentGroupingMode.Default;
            }
        }

        internal static FoodGroupingMode FoodMode
        {
            get
            {
                if (string.Equals(FoodGrouping.Value, "Tier", System.StringComparison.OrdinalIgnoreCase))
                {
                    return FoodGroupingMode.Biome;
                }

                return System.Enum.TryParse(FoodGrouping.Value, true, out FoodGroupingMode mode)
                    ? mode
                    : FoodGroupingMode.Default;
            }
        }

        internal static void Bind(ConfigFile config)
        {
            File = config;
            Enabled = config.Bind("General", "Enabled", true, "Enable CraftGuard.");
            ShowSeparators = config.Bind("General", "ShowSeparators", true, "Show restrained separator lines around recipe groups.");
            ShowHammerPieceNames = config.Bind("Hammer", "ShowPieceNames", false, "Show names beneath individual pieces in organized Hammer tabs.");
            HammerToolViews = config.Bind(
                "Hammer",
                "ToolViewModes",
                ToolViewPreferences.DefaultValue,
                "Remember Default or Mod View independently for each build tool.");
            OrganizeCrafting = config.Bind("Hammer", "OrganizeCrafting", true, "Group stations and their known extensions.");
            OrganizeBuilding = config.Bind("Hammer", "OrganizeBuilding", true, "Group building pieces by material and structure.");
            OrganizeHeavyBuilding = config.Bind("Hammer", "OrganizeHeavyBuilding", true, "Group heavy building pieces by material and structure.");
            OrganizeFurniture = config.Bind("Hammer", "OrganizeFurniture", true, "Group furniture by function.");

            OrganizeRecipes = config.Bind("CraftingUI", "OrganizeRecipes", true, "Enable crafting-station views and search without changing recipes or craftability.");
            WorkbenchGrouping = config.Bind("CraftingUI", "WorkbenchGrouping", "Default", "Remembered equipment-station view: Default, Type, or Biome.");
            FoodGrouping = config.Bind("CraftingUI", "FoodGrouping", "Default", "Remembered food-station view: Default, Stat, or Biome.");

            WriteDataInventoryOnStartup = config.Bind(
                "Diagnostics",
                "WriteDataInventoryOnStartup",
                false,
                "Write a CSV inventory of the currently loaded vanilla and modded pieces/recipes to BepInEx/config/CraftGuard once per launch.");
        }

        internal static void SetEquipmentMode(EquipmentGroupingMode mode)
        {
            WorkbenchGrouping.Value = mode.ToString();
            File.Save();
        }

        internal static void SetFoodMode(FoodGroupingMode mode)
        {
            FoodGrouping.Value = mode.ToString();
            File.Save();
        }

        internal static bool GetToolModView(string toolKey)
        {
            return ToolViewPreferences.IsModView(HammerToolViews.Value, toolKey);
        }

        internal static void SetToolModView(string toolKey, bool useModView)
        {
            HammerToolViews.Value = ToolViewPreferences.Set(HammerToolViews.Value, toolKey, useModView);
            File.Save();
        }

    }

}
