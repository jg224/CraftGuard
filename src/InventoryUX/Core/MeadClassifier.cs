using System;

namespace InventoryUX.Core
{
    internal static class MeadClassifier
    {
        internal static MeadRole Classify(RecipeFacts facts)
        {
            string value = (facts.Id + " " + facts.DisplayName).ToLowerInvariant();
            if (value.IndexOf("health", StringComparison.Ordinal) >= 0
                || value.IndexOf("healing", StringComparison.Ordinal) >= 0)
            {
                return MeadRole.Health;
            }

            if (value.IndexOf("stamina", StringComparison.Ordinal) >= 0)
            {
                return MeadRole.Stamina;
            }

            return MeadRole.Utility;
        }

        internal static RecipeGroup ToGroup(MeadRole role)
        {
            switch (role)
            {
                case MeadRole.Health:
                    return new RecipeGroup("HP", 0);
                case MeadRole.Stamina:
                    return new RecipeGroup("Stamina", 1);
                default:
                    return new RecipeGroup("Utility", 2);
            }
        }
    }
}
