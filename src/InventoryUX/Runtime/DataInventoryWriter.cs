using BepInEx;
using InventoryUX.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace InventoryUX.Runtime
{
    internal static class DataInventoryWriter
    {
        private static bool _written;

        internal static void TryWriteOnce()
        {
            if (_written || ObjectDB.instance == null || ZNetScene.instance == null)
            {
                return;
            }

            try
            {
                string directory = Path.Combine(Paths.ConfigPath, "CraftGuard");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "loaded-data-inventory.csv");
                File.WriteAllText(path, BuildCsv(), new UTF8Encoding(true));
                _written = true;
                Plugin.LogInstance.LogInfo($"Wrote the loaded piece/recipe inventory to {path}");
            }
            catch (Exception exception)
            {
                _written = true;
                Plugin.LogInstance.LogError($"Could not write the data inventory: {exception}");
            }
        }

        private static string BuildCsv()
        {
            var output = new StringBuilder();
            WriteRow(output,
                "ContentType", "PrefabID", "DisplayName", "KnownUnlockMechanism", "HammerCategory",
                "PieceType", "CraftingStation", "StationExtensionParent", "StationLevelContribution",
                "ItemType", "WeaponFamily", "ArmorType", "Recipe", "Ingredients", "RequiredStation",
                "RequiredStationLevel", "FoodHP", "FoodStamina", "FoodEitr", "FoodDuration", "FoodRegen",
                "ProposedGroup", "ProposedBiome", "ProposedSortOrder");

            WritePieces(output);
            WriteRecipes(output);
            return output.ToString();
        }

        private static void WritePieces(StringBuilder output)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<GameObject> prefabs = ZNetScene.instance.m_prefabs;
            for (int i = 0; i < prefabs.Count; i++)
            {
                GameObject? prefab = prefabs[i];
                if (prefab == null) continue;
                Piece? piece = prefab.GetComponent<Piece>();
                if (piece == null || !seen.Add(prefab.name)) continue;

                CraftingStation station = prefab.GetComponent<CraftingStation>();
                StationExtension extension = prefab.GetComponent<StationExtension>();
                PieceGroup group = HammerOrganizer.Classify(piece, piece.m_category);
                string pieceType = station != null ? "Crafting station" : extension != null ? "Station extension" : "Build piece";

                WriteRow(output,
                    "Piece", prefab.name, Localize(piece.m_name), "Piece.m_name in Player.m_knownRecipes",
                    piece.m_category.ToString(), pieceType,
                    piece.m_craftingStation != null ? Localize(piece.m_craftingStation.m_name) : station != null ? Localize(station.m_name) : string.Empty,
                    extension != null && extension.m_craftingStation != null ? Localize(extension.m_craftingStation.m_name) : string.Empty,
                    extension != null ? "Native unique attached extension (+1)" : string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty, FormatPieceIngredients(piece), string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    group.Label, string.Empty, $"{group.Order}:{group.Suborder}");
            }
        }

        private static void WriteRecipes(StringBuilder output)
        {
            List<Recipe> recipes = ObjectDB.instance.m_recipes;
            for (int i = 0; i < recipes.Count; i++)
            {
                Recipe recipe = recipes[i];
                if (recipe == null || recipe.m_item == null) continue;
                ItemDrop.ItemData.SharedData shared = recipe.m_item.m_itemData.m_shared;
                RecipeFacts facts = ToFacts(recipe, i);
                RecipeGroup group = facts.IsFeast
                    ? new RecipeGroup("Feasts", 4)
                    : facts.IsFood
                    ? FoodClassifier.ToGroup(FoodClassifier.Classify(facts.Health, facts.Stamina, facts.Eitr))
                    : RecipeClassifier.GetTypeGroup(facts);
                ProgressionBiome biome = BiomeClassifier.Classify(facts.Id, facts.IngredientIds);

                WriteRow(output,
                    "Recipe", facts.Id, facts.DisplayName, "Player.GetAvailableRecipes / Player.IsRecipeKnown",
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    shared.m_itemType.ToString(), shared.m_skillType.ToString(), ArmorType(shared.m_itemType),
                    recipe.name, FormatRecipeIngredients(recipe), recipe.m_craftingStation != null ? Localize(recipe.m_craftingStation.m_name) : string.Empty,
                    recipe.m_minStationLevel.ToString(CultureInfo.InvariantCulture),
                    shared.m_food.ToString(CultureInfo.InvariantCulture),
                    shared.m_foodStamina.ToString(CultureInfo.InvariantCulture),
                    shared.m_foodEitr.ToString(CultureInfo.InvariantCulture),
                    shared.m_foodBurnTime.ToString(CultureInfo.InvariantCulture),
                    shared.m_foodRegen.ToString(CultureInfo.InvariantCulture),
                    group.Label, biome.ToString(), $"{BiomeClassifier.ToGroup(biome).Order}:{group.Order}");
            }
        }

        private static RecipeFacts ToFacts(Recipe recipe, int originalIndex)
        {
            ItemDrop.ItemData.SharedData shared = recipe.m_item.m_itemData.m_shared;
            var ingredients = new List<string>();
            if (recipe.m_resources != null)
            {
                for (int i = 0; i < recipe.m_resources.Length; i++)
                {
                    ItemDrop? item = recipe.m_resources[i]?.m_resItem;
                    if (item == null) continue;
                    ingredients.Add(item.gameObject.name);
                    ingredients.Add(item.m_itemData.m_shared.m_name);
                }
            }

            ResolvedFoodStats resolved = FoodStatsResolver.Resolve(recipe.m_item);
            float health = resolved.Resolved ? resolved.Health : shared.m_food;
            float stamina = resolved.Resolved ? resolved.Stamina : shared.m_foodStamina;
            float eitr = resolved.Resolved ? resolved.Eitr : shared.m_foodEitr;
            return new RecipeFacts(recipe.m_item.gameObject.name, Localize(shared.m_name), shared.m_itemType.ToString(),
                shared.m_skillType.ToString(), ingredients, health, stamina, eitr, originalIndex, resolved.IsFeast);
        }

        private static string FormatPieceIngredients(Piece piece)
        {
            if (piece.m_resources == null) return string.Empty;
            var values = new List<string>();
            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                Piece.Requirement requirement = piece.m_resources[i];
                if (requirement?.m_resItem != null)
                {
                    values.Add($"{Localize(requirement.m_resItem.m_itemData.m_shared.m_name)} x{requirement.m_amount}");
                }
            }
            return string.Join("; ", values);
        }

        private static string FormatRecipeIngredients(Recipe recipe)
        {
            if (recipe.m_resources == null) return string.Empty;
            var values = new List<string>();
            for (int i = 0; i < recipe.m_resources.Length; i++)
            {
                Piece.Requirement requirement = recipe.m_resources[i];
                if (requirement?.m_resItem != null)
                {
                    values.Add($"{Localize(requirement.m_resItem.m_itemData.m_shared.m_name)} x{requirement.m_amount}");
                }
            }
            return string.Join("; ", values);
        }

        private static string ArmorType(ItemDrop.ItemData.ItemType itemType)
        {
            switch (itemType)
            {
                case ItemDrop.ItemData.ItemType.Helmet: return "Helmet";
                case ItemDrop.ItemData.ItemType.Chest: return "Chest";
                case ItemDrop.ItemData.ItemType.Legs: return "Legs";
                case ItemDrop.ItemData.ItemType.Hands: return "Hands";
                case ItemDrop.ItemData.ItemType.Shoulder: return "Shoulder";
                default: return string.Empty;
            }
        }

        private static string Localize(string value)
        {
            return Localization.instance != null ? Localization.instance.Localize(value) : value;
        }

        private static void WriteRow(StringBuilder output, params string[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) output.Append(',');
                output.Append(Escape(values[i]));
            }
            output.AppendLine();
        }

        private static string Escape(string? value)
        {
            value = value ?? string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? "\"" + value.Replace("\"", "\"\"") + "\""
                : value;
        }
    }
}
