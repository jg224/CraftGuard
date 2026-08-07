using HarmonyLib;
using InventoryUX.Runtime;
using System.Collections.Generic;

namespace InventoryUX.Patches
{
    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class InventoryGuiUpdateRecipeListPatch
    {
        private static void Postfix(InventoryGui __instance, List<Recipe> recipes)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.OrganizeRecipes.Value)
            {
                RecipeOrganizer.Cleanup(__instance);
                return;
            }

            try
            {
                RecipeOrganizer.Organize(__instance);
            }
            catch (System.Exception exception)
            {
                Plugin.LogInstance.LogWarning($"Recipe organization skipped: {exception.Message}");
            }
        }
    }
}
