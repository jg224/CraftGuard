using System;

namespace InventoryUX.Core
{
    internal static class RecipeClassifier
    {
        internal static RecipeGroup GetEquipmentGroup(RecipeFacts facts, EquipmentGroupingMode mode)
        {
            if (mode == EquipmentGroupingMode.Default)
            {
                return new RecipeGroup("", 0);
            }

            if (mode == EquipmentGroupingMode.Biome)
            {
                return BiomeClassifier.ToGroup(BiomeClassifier.Classify(facts.Id, facts.IngredientIds));
            }

            return GetTypeGroup(facts);
        }

        internal static RecipeGroup GetFoodGroup(RecipeFacts facts, FoodGroupingMode mode)
        {
            if (mode == FoodGroupingMode.Default)
            {
                return new RecipeGroup("", 0);
            }

            if (mode == FoodGroupingMode.Biome)
            {
                return BiomeClassifier.ToGroup(BiomeClassifier.Classify(facts.Id, facts.IngredientIds));
            }

            return facts.IsFood
                ? FoodClassifier.ToGroup(FoodClassifier.Classify(facts.Health, facts.Stamina, facts.Eitr))
                : new RecipeGroup("Other", 4);
        }

        internal static RecipeGroup GetFoodPrepGroup(RecipeFacts facts, FoodGroupingMode mode)
        {
            if (mode == FoodGroupingMode.Stat && (facts.IsFeast || !facts.IsFood))
            {
                return new RecipeGroup("Feasts", 4);
            }

            return GetFoodGroup(facts, mode);
        }

        internal static RecipeGroup GetTypeGroup(RecipeFacts facts)
        {
            string itemType = facts.ItemType;
            string skill = facts.Skill;

            if (EqualsAny(itemType, "Tool"))
            {
                return new RecipeGroup("Tools", 0);
            }

            if (EqualsAny(itemType, "Shield"))
            {
                return new RecipeGroup("Shields", 20);
            }

            if (EqualsAny(itemType, "Helmet", "Chest", "Legs", "Hands", "Shoulder"))
            {
                return new RecipeGroup("Armor", 30);
            }

            if (EqualsAny(itemType, "Ammo", "AmmoNonEquipable"))
            {
                return new RecipeGroup("Ammunition", 40);
            }

            if (EqualsAny(itemType, "Utility", "Torch", "Trinket"))
            {
                return new RecipeGroup("Utility", 50);
            }

            if (EqualsAny(itemType, "OneHandedWeapon", "TwoHandedWeapon", "TwoHandedWeaponLeft", "Bow", "Attach_Atgeir"))
            {
                return WeaponGroup(skill);
            }

            return new RecipeGroup("Other", 60);
        }

        private static RecipeGroup WeaponGroup(string skill)
        {
            switch (skill)
            {
                case "Swords":
                    return new RecipeGroup("Swords", 10);
                case "Axes":
                case "WoodCutting":
                    return new RecipeGroup("Axes", 11);
                case "Clubs":
                    return new RecipeGroup("Maces", 12);
                case "Spears":
                    return new RecipeGroup("Spears", 13);
                case "Knives":
                    return new RecipeGroup("Knives", 14);
                case "Polearms":
                    return new RecipeGroup("Polearms / Atgeirs", 15);
                case "Bows":
                case "Crossbows":
                    return new RecipeGroup("Bows / Ranged", 16);
                default:
                    return new RecipeGroup("Weapons", 17);
            }
        }

        private static bool EqualsAny(string value, params string[] choices)
        {
            for (int i = 0; i < choices.Length; i++)
            {
                if (string.Equals(value, choices[i], StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
