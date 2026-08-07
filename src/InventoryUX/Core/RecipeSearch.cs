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

            string search = query!.Trim();
            return facts.DisplayName.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0
                || facts.Id.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
