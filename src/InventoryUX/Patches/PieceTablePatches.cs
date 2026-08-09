using HarmonyLib;
using InventoryUX.Runtime;
using System.Collections.Generic;

namespace InventoryUX.Patches
{
    internal static class HammerPatchHealth
    {
        internal static readonly FailureCircuitBreaker Organization =
            new FailureCircuitBreaker("CraftGuard Hammer sorting");
        internal static readonly FailureCircuitBreaker Decoration =
            new FailureCircuitBreaker("CraftGuard Hammer layout");

        internal static void Release(Hud hud)
        {
            System.Exception? failure = null;
            try
            {
                HammerGroupDecorations.Release(hud);
            }
            catch (System.Exception exception)
            {
                failure = exception;
            }

            try
            {
                HammerGridSizer.Restore();
            }
            catch (System.Exception exception)
            {
                failure = failure == null
                    ? exception
                    : new System.AggregateException(failure, exception);
            }

            if (failure != null) Decoration.Trip(failure);
        }
    }

    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
    internal static class PieceTableUpdateAvailablePatch
    {
        private static void Postfix(PieceTable __instance)
        {
            if (!ModConfig.Enabled.Value || !HammerGroupDecorations.ShouldUseModView(__instance))
            {
                return;
            }

            if (HammerPatchHealth.Organization.IsOpen) return;

            try
            {
                HammerOrganizer.ReorderAvailablePieces(__instance);
                HammerGroupDecorations.NotifyPiecesChanged();
                HammerPatchHealth.Organization.Reset();
                HammerPatchHealth.Decoration.Reset();
            }
            catch (System.Exception exception)
            {
                HammerPatchHealth.Organization.Trip(exception);
            }
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdatePieceList")]
    internal static class HudUpdatePieceListPatch
    {
        private static void Postfix(Hud __instance, Player player, Piece.PieceCategory category)
        {
            if (player == null
                || player.IsTeleporting()
                || (Game.instance != null && Game.instance.IsShuttingDown()))
            {
                HammerPatchHealth.Release(__instance);
                return;
            }

            try
            {
                if (!ModConfig.Enabled.Value)
                {
                    HammerPatchHealth.Release(__instance);
                    return;
                }

                if (HammerPatchHealth.Decoration.IsOpen) return;

                HammerGridSizer.Apply(__instance);
                List<Piece>? pieces = player.GetBuildPieces();
                if (pieces != null)
                {
                    HammerGroupDecorations.Apply(__instance, pieces, category);
                    HammerPatchHealth.Decoration.Reset();
                }
            }
            catch (System.Exception exception)
            {
                HammerPatchHealth.Release(__instance);
                HammerPatchHealth.Decoration.Trip(exception);
            }
        }
    }

    [HarmonyPatch(typeof(Hud), "OnDestroy")]
    internal static class HudDestroyPatch
    {
        private static void Prefix(Hud __instance)
        {
            HammerPatchHealth.Release(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "OnDestroy")]
    internal static class ZNetSceneDestroyPatch
    {
        private static void Prefix()
        {
            FoodStatsResolver.Reset();
        }
    }

    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.LeftPiece))]
    internal static class PieceTableLeftPiecePatch
    {
        private static bool Prefix(PieceTable __instance)
            => !HammerGroupDecorations.TryNavigate(__instance, -1, 0);
    }

    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.RightPiece))]
    internal static class PieceTableRightPiecePatch
    {
        private static bool Prefix(PieceTable __instance)
            => !HammerGroupDecorations.TryNavigate(__instance, 1, 0);
    }

    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpPiece))]
    internal static class PieceTableUpPiecePatch
    {
        private static bool Prefix(PieceTable __instance)
            => !HammerGroupDecorations.TryNavigate(__instance, 0, -1);
    }

    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.DownPiece))]
    internal static class PieceTableDownPiecePatch
    {
        private static bool Prefix(PieceTable __instance)
            => !HammerGroupDecorations.TryNavigate(__instance, 0, 1);
    }
}
