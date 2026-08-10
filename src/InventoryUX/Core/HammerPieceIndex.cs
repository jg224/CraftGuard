using System;
using System.Collections.Generic;
using System.Globalization;

namespace InventoryUX.Core
{
    internal static class HammerPieceSearch
    {
        internal static bool Matches(string searchableText, string? query)
        {
            return MatchesPrepared(searchableText, Normalize(query));
        }

        internal static bool MatchesPrepared(string searchableText, string normalizedQuery)
            => normalizedQuery.Length == 0
                || searchableText.IndexOf(normalizedQuery, StringComparison.Ordinal) >= 0;

        internal static string Normalize(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string source = value!;
            char[] buffer = new char[source.Length];
            int cursor = 0;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsLetterOrDigit(c))
                {
                    buffer[cursor++] = char.ToLower(c, CultureInfo.InvariantCulture);
                }
            }
            return new string(buffer, 0, cursor);
        }
    }

    internal static class FavoritePiecePreferences
    {
        internal static HashSet<string> Parse(string? value)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(value)) return result;
            string[] entries = value!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                string key = HammerPieceSearch.Normalize(entries[i]);
                if (key.Length > 0) result.Add(key);
            }
            return result;
        }

        internal static string Toggle(string? value, string key)
        {
            HashSet<string> entries = Parse(value);
            string normalized = HammerPieceSearch.Normalize(key);
            if (normalized.Length == 0) return Serialize(entries);
            if (!entries.Add(normalized)) entries.Remove(normalized);
            return Serialize(entries);
        }

        private static string Serialize(HashSet<string> entries)
        {
            var ordered = new List<string>(entries);
            ordered.Sort(StringComparer.Ordinal);
            return string.Join(";", ordered);
        }
    }

    internal static class PlantEverythingClassifier
    {
        private static readonly HashSet<string> CustomPlantPrefabIds = new HashSet<string>(StringComparer.Ordinal)
        {
            "raspberrybush", "blueberrybush", "cloudberrybush",
            "pickablemushroom", "pickablemushroomyellow", "pickablemushroomblue",
            "pickablethistle", "pickabledandelion", "pickablesmokepuff", "pickablefiddlehead",
            "ancientsapling", "yggasapling", "autumnbirchsapling", "ashwoodsapling",
            "beechsmall1", "firtreesmall", "firtreesmalldead",
            "bush01", "bush01heath", "bush02en", "shrub2", "shrub2heath",
            "yggashootsmall1", "vines", "pevineashsapling", "fernashlands",
            "pickablebranch", "pickablestone", "pickableflint"
        };

        internal static bool IsCustomPlant(string? prefabId)
        {
            string normalized = HammerPieceSearch.Normalize(prefabId);
            if (normalized.EndsWith("clone", StringComparison.Ordinal))
            {
                normalized = normalized.Substring(0, normalized.Length - "clone".Length);
            }
            return CustomPlantPrefabIds.Contains(normalized);
        }
    }
}
