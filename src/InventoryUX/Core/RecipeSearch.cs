using System;

namespace InventoryUX.Core
{
    internal static class RecipeSearch
    {
        internal static bool Matches(RecipeFacts facts, string? query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            return MatchesPrepared(facts, query!.Trim());
        }

        internal static bool MatchesPrepared(RecipeFacts facts, string preparedQuery)
            => preparedQuery.Length == 0
                || facts.DisplayName.IndexOf(preparedQuery, StringComparison.CurrentCultureIgnoreCase) >= 0
                || facts.Id.IndexOf(preparedQuery, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
