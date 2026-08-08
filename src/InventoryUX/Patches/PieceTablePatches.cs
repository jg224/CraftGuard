using HarmonyLib;
using InventoryUX.Runtime;
using System.Collections.Generic;

namespace InventoryUX.Patches
{
    [HarmonyPatch(typeof(PieceTable), nameof(PieceTable.UpdateAvailable))]
    internal static class PieceTableUpdateAvailablePatch
    {
        private static void Postfix(PieceTable __instance)
        {
            if (!ModConfig.Enabled.Value || !HammerGroupDecorations.UseModView)
            {
                return;
            }

            try
            {
                HammerOrganizer.ReorderAvailablePieces(__instance);
                HammerGroupDecorations.NotifyPiecesChanged();
            }
            catch (System.Exception exception)
            {
                Plugin.LogInstance.LogWarning($"Hammer organization skipped: {exception}");
            }
        }
    }

    [HarmonyPatch(typeof(Hud), "UpdatePieceList")]
    internal static class HudUpdatePieceListPatch
    {
        private static void Postfix(Hud __instance, Player player, Piece.PieceCategory category)
        {
            try
            {
                if (!ModConfig.Enabled.Value)
                {
                    HammerGridSizer.Restore();
                    HammerGroupDecorations.Shutdown();
                    return;
                }

                HammerGridSizer.Apply(__instance);
                List<Piece>? pieces = player.GetBuildPieces();
                if (pieces != null)
                {
                    HammerGroupDecorations.Apply(__instance, pieces, category);
                }
            }
            catch (System.Exception exception)
            {
                Plugin.LogInstance.LogWarning($"Hammer labels skipped: {exception}");
            }
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
