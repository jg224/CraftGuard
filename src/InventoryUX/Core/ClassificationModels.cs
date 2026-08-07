using System;
using System.Collections.Generic;

namespace InventoryUX.Core
{
    internal enum EquipmentGroupingMode
    {
        Default,
        Type,
        Biome
    }

    internal enum FoodGroupingMode
    {
        Default,
        Stat,
        Biome
    }

    internal enum FoodRole
    {
        Health,
        Stamina,
        Eitr,
        Balanced
    }

    internal enum MeadRole
    {
        Health,
        Stamina,
        Utility
    }

    internal enum ProgressionBiome
    {
        Other = -1,
        Meadows,
        BlackForest,
        Swamp,
        Ocean,
        Mountains,
        Plains,
        Mistlands,
        Ashlands
    }

    internal sealed class RecipeFacts
    {
        internal RecipeFacts(
            string id,
            string displayName,
            string itemType,
            string skill,
            IReadOnlyList<string> ingredientIds,
            float health,
            float stamina,
            float eitr,
            int originalIndex,
            bool isFeast = false)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ItemType = itemType ?? string.Empty;
            Skill = skill ?? string.Empty;
            IngredientIds = ingredientIds ?? Array.Empty<string>();
            Health = health;
            Stamina = stamina;
            Eitr = eitr;
            OriginalIndex = originalIndex;
            IsFeast = isFeast;
        }

        internal string Id { get; }
        internal string DisplayName { get; }
        internal string ItemType { get; }
        internal string Skill { get; }
        internal IReadOnlyList<string> IngredientIds { get; }
        internal float Health { get; }
        internal float Stamina { get; }
        internal float Eitr { get; }
        internal int OriginalIndex { get; }
        internal bool IsFeast { get; }
        internal bool IsFood => Health > 0f || Stamina > 0f || Eitr > 0f;
    }

    internal readonly struct RecipeGroup
    {
        internal RecipeGroup(string label, int order)
        {
            Label = label;
            Order = order;
        }

        internal string Label { get; }
        internal int Order { get; }
    }
}
