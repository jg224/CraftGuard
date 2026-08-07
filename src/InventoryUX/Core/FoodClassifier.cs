using System;

namespace InventoryUX.Core
{
    internal static class FoodClassifier
    {
        // A 20% lead avoids classifying near-even meals as a specialist food.
        internal const float SpecialistRatio = 1.2f;

        internal static FoodRole Classify(float health, float stamina, float eitr)
        {
            if (eitr > 0f)
            {
                return FoodRole.Eitr;
            }

            if (health <= 0f && stamina <= 0f)
            {
                return FoodRole.Balanced;
            }

            if (health >= stamina * SpecialistRatio)
            {
                return FoodRole.Health;
            }

            if (stamina >= health * SpecialistRatio)
            {
                return FoodRole.Stamina;
            }

            return FoodRole.Balanced;
        }

        internal static RecipeGroup ToGroup(FoodRole role)
        {
            switch (role)
            {
                case FoodRole.Health:
                    return new RecipeGroup("Health", 0);
                case FoodRole.Stamina:
                    return new RecipeGroup("Stamina", 1);
                case FoodRole.Eitr:
                    return new RecipeGroup("Eitr", 2);
                default:
                    return new RecipeGroup("Balanced", 3);
            }
        }

        internal static float Strength(RecipeFacts facts)
        {
            if (facts.IsFeast)
            {
                return facts.Health + facts.Stamina + facts.Eitr;
            }

            switch (Classify(facts.Health, facts.Stamina, facts.Eitr))
            {
                case FoodRole.Health:
                    return facts.Health;
                case FoodRole.Stamina:
                    return facts.Stamina;
                case FoodRole.Eitr:
                    return facts.Eitr;
                default:
                    return facts.Health + facts.Stamina + facts.Eitr;
            }
        }
    }
}
