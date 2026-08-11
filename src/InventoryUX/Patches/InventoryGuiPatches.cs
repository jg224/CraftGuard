using HarmonyLib;
using InventoryUX.Runtime;
using System.Collections.Generic;

namespace InventoryUX.Patches
{
    [HarmonyPatch(typeof(PlayerController), "TakeInput", new[] { typeof(bool) })]
    internal static class PlayerControllerCraftingSearchInputPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!RecipeOrganizer.IsSearchFocused && !HammerGroupDecorations.IsSearchFocused) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "TakeInput")]
    internal static class PlayerCraftingSearchInputPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!RecipeOrganizer.IsSearchFocused && !HammerGroupDecorations.IsSearchFocused) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
    internal static class InventoryGuiUpdateRecipeListPatch
    {
        private static readonly FailureCircuitBreaker Breaker =
            new FailureCircuitBreaker("CraftIndex recipe organization");

        private static void Prefix(InventoryGui __instance)
        {
            try
            {
                RecipeOrganizer.PrepareForVanillaRecipeRefresh(__instance);
            }
            catch (System.Exception exception)
            {
                Breaker.Trip(exception);
            }
        }

        private static void Postfix(InventoryGui __instance, List<Recipe> recipes)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.OrganizeRecipes.Value)
            {
                if (TryRelease(__instance)) Breaker.Reset();
                return;
            }

            if (Breaker.IsOpen)
            {
                return;
            }

            try
            {
                RecipeOrganizer.Organize(__instance);
                Breaker.Reset();
            }
            catch (System.Exception exception)
            {
                TryRelease(__instance);
                Breaker.Trip(exception);
            }
        }

        internal static bool TryRelease(InventoryGui gui)
        {
            try
            {
                RecipeOrganizer.Release(gui);
                return true;
            }
            catch (System.Exception exception)
            {
                Breaker.Trip(exception);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGui))]
    internal static class InventoryGuiRecipeTabScrollPatch
    {
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(InventoryGui), "OnTabCraftPressed");
            yield return AccessTools.Method(typeof(InventoryGui), "OnTabUpgradePressed");
        }

        private static void Postfix(InventoryGui __instance)
        {
            if (!ModConfig.Enabled.Value || !ModConfig.OrganizeRecipes.Value) return;
            RecipeOrganizer.ResetRecipeListScrollToTop(__instance);
        }
    }

    [HarmonyPatch(typeof(InventoryGui), "OnDestroy")]
    internal static class InventoryGuiDestroyPatch
    {
        private static void Prefix(InventoryGui __instance)
        {
            InventoryGuiUpdateRecipeListPatch.TryRelease(__instance);
        }
    }
}
