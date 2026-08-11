using HarmonyLib;
using System;
using System.Reflection;

namespace InventoryUX.Runtime
{
    internal static class SearchInputBlocker
    {
        private static readonly MethodInfo TargetMethod = AccessTools.Method(
            typeof(ZInput),
            nameof(ZInput.GetButtonDown),
            new[] { typeof(string) });
        private static readonly MethodInfo PrefixMethod = AccessTools.Method(
            typeof(SearchInputBlocker),
            nameof(BlockButtonDown));
        private static readonly HarmonyMethod PrefixPatch = new HarmonyMethod(PrefixMethod);

        private static Harmony? _harmony;
        private static bool _recipeSearchFocused;
        private static bool _hammerSearchFocused;
        private static bool _isPatched;

        internal static void Initialize(Harmony harmony)
        {
            _harmony = harmony;
            _recipeSearchFocused = false;
            _hammerSearchFocused = false;
            _isPatched = HasInstalledPrefix();
            RefreshPatchState();
        }

        internal static void SetRecipeSearchFocused(bool focused)
        {
            if (_recipeSearchFocused == focused) return;
            _recipeSearchFocused = focused;
            RefreshPatchState();
        }

        internal static void SetHammerSearchFocused(bool focused)
        {
            if (_hammerSearchFocused == focused) return;
            _hammerSearchFocused = focused;
            RefreshPatchState();
        }

        internal static void Shutdown()
        {
            _recipeSearchFocused = false;
            _hammerSearchFocused = false;
            if (_harmony != null && (_isPatched || HasInstalledPrefix()))
            {
                _harmony.Unpatch(TargetMethod, PrefixMethod);
            }
            _isPatched = false;
            _harmony = null;
        }

        private static void RefreshPatchState()
        {
            if (_harmony == null) return;

            bool shouldPatch = _recipeSearchFocused || _hammerSearchFocused;
            if (shouldPatch == _isPatched) return;

            try
            {
                if (shouldPatch)
                {
                    _harmony.Patch(TargetMethod, prefix: PrefixPatch);
                }
                else
                {
                    _harmony.Unpatch(TargetMethod, PrefixMethod);
                }
                _isPatched = shouldPatch;
            }
            catch (Exception exception)
            {
                _isPatched = HasInstalledPrefix();
                Plugin.LogInstance.LogWarning($"CraftIndex could not update its focused-search input guard: {exception}");
            }
        }

        private static bool HasInstalledPrefix()
        {
            HarmonyLib.Patches? patches = Harmony.GetPatchInfo(TargetMethod);
            if (patches == null) return false;

            foreach (Patch patch in patches.Prefixes)
            {
                if (patch.PatchMethod == PrefixMethod) return true;
            }
            return false;
        }

        private static bool BlockButtonDown(ref bool __result)
        {
            if (!_recipeSearchFocused && !_hammerSearchFocused) return true;
            __result = false;
            return false;
        }
    }
}
