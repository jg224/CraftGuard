namespace InventoryUX.Runtime
{
    internal static class UnlockVisibilityService
    {
        internal static bool IsKnownRecipe(Player? player, Recipe? recipe)
        {
            if (player == null || recipe == null || recipe.m_item == null)
            {
                return false;
            }

            return player.IsRecipeKnown(recipe.m_item.m_itemData.m_shared.m_name);
        }

        internal static bool IsKnownPiece(Player? player, Piece? piece)
        {
            if (player == null || piece == null || string.IsNullOrEmpty(piece.m_name))
            {
                return false;
            }

            // This is the same key PieceTable.UpdateAvailable gates on. It never
            // attempts to predict future unlocks from ingredients or metadata.
            return player.IsRecipeKnown(piece.m_name);
        }
    }
}
