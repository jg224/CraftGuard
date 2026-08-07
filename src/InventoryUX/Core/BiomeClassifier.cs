using System;
using System.Collections.Generic;

namespace InventoryUX.Core
{
    internal static class BiomeClassifier
    {
        private static readonly IReadOnlyDictionary<ProgressionBiome, string[]> Markers =
            new Dictionary<ProgressionBiome, string[]>
            {
                [ProgressionBiome.Ashlands] = new[]
                {
                    "ashlands", "ashwood", "asksvin", "bellfragment", "blackwood", "bonemaw",
                    "celestialfeather", "charred", "flametal", "grausten", "morgen", "proustite",
                    "sulfur", "vineberry"
                },
                [ProgressionBiome.Mistlands] = new[]
                {
                    "blackcore", "carapace", "dvergr", "eitr", "haremeat", "jotunpuffs",
                    "magecap", "mandible", "refinedeitr", "royaljelly", "scalehide", "seeker",
                    "softtissue", "yggdrasil"
                },
                [ProgressionBiome.Plains] = new[]
                {
                    "barley", "blackmetal", "bloodpudding", "bread", "cloudberry", "deathsquito",
                    "flax", "linen", "lox", "needle", "padded", "porcupine", "tar", "wolfjerky"
                },
                [ProgressionBiome.Mountains] = new[]
                {
                    "crystal", "drake", "fenring", "fenris", "freeze", "frost", "golem",
                    "moder", "obsidian", "onion", "silver", "wolf"
                },
                [ProgressionBiome.Ocean] = new[]
                {
                    "abyssal", "barnacle", "chitin", "leviathan", "serpent"
                },
                [ProgressionBiome.Swamp] = new[]
                {
                    "ancientbark", "bloodbag", "bonemass", "chain", "draugr", "entrails", "guck",
                    "iron", "ooze", "root", "scrapiron", "surtling", "turnip", "witheredbone"
                },
                [ProgressionBiome.BlackForest] = new[]
                {
                    "blueberry", "bronze", "carrot", "copper", "corewood", "deerhide", "elder",
                    "finewood", "greydwarf", "tin", "troll"
                },
                [ProgressionBiome.Meadows] = new[]
                {
                    "boar", "club", "dandelion", "deer", "feather", "flint", "leatherscraps",
                    "mushroom", "necktail", "raspberry", "resin", "stone", "wood"
                }
            };

        internal static ProgressionBiome Classify(string itemId, IReadOnlyList<string> ingredients)
        {
            ProgressionBiome latest = ProgressionBiome.Other;
            Consider(itemId, ref latest);

            for (int i = 0; i < ingredients.Count; i++)
            {
                Consider(ingredients[i], ref latest);
            }

            return latest;
        }

        internal static RecipeGroup ToGroup(ProgressionBiome biome)
        {
            // Lower group order renders first. Late-game biomes therefore lead
            // every Biome view and every within-Type biome sort.
            switch (biome)
            {
                case ProgressionBiome.Ashlands:
                    return new RecipeGroup("Ashlands", 0);
                case ProgressionBiome.Mistlands:
                    return new RecipeGroup("Mistlands", 1);
                case ProgressionBiome.Plains:
                    return new RecipeGroup("Plains", 2);
                case ProgressionBiome.Mountains:
                    return new RecipeGroup("Mountains", 3);
                case ProgressionBiome.Ocean:
                    return new RecipeGroup("Ocean", 4);
                case ProgressionBiome.Swamp:
                    return new RecipeGroup("Swamp", 5);
                case ProgressionBiome.BlackForest:
                    return new RecipeGroup("Black Forest", 6);
                case ProgressionBiome.Meadows:
                    return new RecipeGroup("Meadows", 7);
                default:
                    return new RecipeGroup("Other", 8);
            }
        }

        internal static RecipeGroup GetGroup(RecipeFacts facts)
            => ToGroup(Classify(facts.Id, facts.IngredientIds));

        private static void Consider(string? raw, ref ProgressionBiome latest)
        {
            string value = Normalize(raw);
            if (value.Length == 0) return;

            foreach (KeyValuePair<ProgressionBiome, string[]> entry in Markers)
            {
                for (int i = 0; i < entry.Value.Length; i++)
                {
                    if (value.IndexOf(entry.Value[i], StringComparison.Ordinal) < 0) continue;
                    if ((int)entry.Key > (int)latest) latest = entry.Key;
                    break;
                }
            }
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            char[] buffer = new char[value!.Length];
            int cursor = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c)) buffer[cursor++] = char.ToLowerInvariant(c);
            }

            return new string(buffer, 0, cursor);
        }
    }
}
