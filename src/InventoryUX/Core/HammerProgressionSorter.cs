using System;
using System.Collections.Generic;

namespace InventoryUX.Core
{
    internal enum HammerSortCategory
    {
        Misc,
        Building,
        HeavyBuilding,
        Furniture
    }

    internal readonly struct HammerSortKey : IComparable<HammerSortKey>
    {
        internal HammerSortKey(int progression, int family, int variation, int cosmetic)
        {
            Progression = progression;
            Family = family;
            Variation = variation;
            Cosmetic = cosmetic;
        }

        internal int Progression { get; }
        internal int Family { get; }
        internal int Variation { get; }
        internal int Cosmetic { get; }

        public int CompareTo(HammerSortKey other)
        {
            int comparison = Progression.CompareTo(other.Progression);
            if (comparison != 0) return comparison;
            comparison = Family.CompareTo(other.Family);
            if (comparison != 0) return comparison;
            comparison = Variation.CompareTo(other.Variation);
            return comparison != 0 ? comparison : Cosmetic.CompareTo(other.Cosmetic);
        }
    }

    internal static class HammerProgressionSorter
    {
        internal static int MiscTravelOrder(string itemId)
        {
            string id = Normalize(itemId);
            if (ContainsAny(id, "cartography", "maptable")) return -1;
            if (id.Contains("cart")) return 0;
            if (id.Contains("raft")) return 10;
            if (id.Contains("karve")) return 20;
            if (id.Contains("longship")) return 30;
            if (ContainsAny(id, "drakkar", "ashlandship", "vikingshipashlands")) return 40;
            if (ContainsAny(id, "portal", "teleport")) return 50;
            if (ContainsAny(id, "boat", "ship")) return 60;
            return -1;
        }

        internal static int MiscDefenseOrder(string itemId)
        {
            string id = Normalize(itemId);
            if (ContainsAny(id, "roundpolefence", "roundpole", "woodfence")) return 0;
            if (ContainsAny(id, "shieldgenerator", "shieldgen")) return 100;
            return 50;
        }

        internal static HammerSortKey Create(
            HammerSortCategory category,
            string itemId,
            IReadOnlyList<string> requirements)
        {
            string id = Normalize(itemId);
            int progression = ProgressionOrder(BiomeClassifier.Classify(id, requirements));
            if (IsSeasonal(id)) progression = 90;
            else if (IsSpecial(id)) progression = 80;

            return new HammerSortKey(
                progression,
                FamilyOrder(category, id),
                VariationOrder(id),
                CosmeticOrder(id));
        }

        private static int ProgressionOrder(ProgressionBiome biome)
        {
            switch (biome)
            {
                case ProgressionBiome.Meadows: return 0;
                case ProgressionBiome.BlackForest: return 10;
                case ProgressionBiome.Swamp: return 20;
                case ProgressionBiome.Ocean: return 25;
                case ProgressionBiome.Mountains: return 30;
                case ProgressionBiome.Plains: return 40;
                case ProgressionBiome.Mistlands: return 50;
                case ProgressionBiome.Ashlands: return 60;
                default: return 70;
            }
        }

        private static int FamilyOrder(HammerSortCategory category, string id)
        {
            switch (category)
            {
                case HammerSortCategory.Misc:
                    return MiscFamilyOrder(id);
                case HammerSortCategory.Building:
                case HammerSortCategory.HeavyBuilding:
                    return BuildingFamilyOrder(id);
                case HammerSortCategory.Furniture:
                    return FurnitureFamilyOrder(id);
                default:
                    return 500;
            }
        }

        private static int MiscFamilyOrder(string id)
        {
            int travelOrder = MiscTravelOrder(id);
            if (travelOrder >= 0) return travelOrder;

            if (ContainsAny(id, "campfire", "firepit", "bonfire")) return 30;
            if (id.Contains("hearth")) return 31;
            if (id.Contains("brazier")) return 32;

            if (ContainsAny(id, "fence", "palisade", "barricade")) return 40;
            if (ContainsAny(id, "stake", "spike")) return 41;
            if (id.Contains("trap")) return 42;
            if (ContainsAny(id, "shieldgenerator", "shieldgen")) return 43;

            if (id.Contains("ballista")) return 50;
            if (id.Contains("catapult")) return 51;
            if (id.Contains("batteringram")) return 52;
            if (ContainsAny(id, "cannon", "turret")) return 53;

            if (ContainsAny(id, "workbench", "station", "table")) return 60;
            if (ContainsAny(id, "plant", "garden", "crop", "seed", "cultiv")) return 70;
            if (ContainsAny(id, "sign", "marker", "cartography", "maptable")) return 80;
            return 90;
        }

        private static int BuildingFamilyOrder(string id)
        {
            if (id.Contains("roofcross") || (id.Contains("roof") && id.Contains("cross"))) return 9;
            if (id.Contains("beam")) return 0;
            if (id.Contains("pole")) return 1;
            if (ContainsAny(id, "pillar", "column")) return 2;
            if (ContainsAny(id, "foundation", "base")) return 3;
            if (id.Contains("floor")) return 10;
            if (id.Contains("tile")) return 11;
            if (ContainsAny(id, "wall", "halfwall")) return 20;
            if (id.Contains("roof")) return 30;
            if (ContainsAny(id, "stair", "staircase")) return 40;
            if (id.Contains("ladder")) return 41;
            if (id.Contains("door")) return 50;
            if (id.Contains("gate")) return 51;
            if (id.Contains("window")) return 52;
            if (id.Contains("arch")) return 53;
            return 60;
        }

        private static int FurnitureFamilyOrder(string id)
        {
            if (id.Contains("chest")) return 0;
            if (ContainsAny(id, "crate", "storage")) return 1;
            if (id.Contains("stool")) return 10;
            if (id.Contains("chair")) return 11;
            if (id.Contains("bench")) return 12;
            if (id.Contains("throne")) return 13;
            if (ContainsAny(id, "table", "desk")) return 20;
            if (id.Contains("bed")) return 21;
            if (ContainsAny(id, "torch", "fire")) return 30;
            if (id.Contains("sconce")) return 31;
            if (id.Contains("brazier")) return 32;
            if (ContainsAny(id, "lamp", "lantern")) return 33;
            if (id.Contains("itemstand")) return 40;
            if (id.Contains("armorstand")) return 41;
            if (ContainsAny(id, "rug", "carpet")) return 80;
            if (id.Contains("banner")) return 90;
            return 50;
        }

        private static int VariationOrder(string id)
        {
            if (ContainsAny(id, "extension", "upgrade", "improvement"))
            {
                int suffix = TrailingNumber(id);
                return 100 + (suffix >= 0 ? suffix : 0);
            }

            if (ContainsAny(id, "1x1", "1m", "small", "short")) return 0;
            if (ContainsAny(id, "2x2", "2m")) return 10;
            if (ContainsAny(id, "4x4", "4m", "large", "tall", "wide")) return 20;
            if (id.Contains("26")) return 30;
            if (id.Contains("45")) return 31;
            if (ContainsAny(id, "corner", "end", "cap")) return 35;
            return 5;
        }

        private static int CosmeticOrder(string id)
            => ContainsAny(id,
                "decor", "ornament", "adornment", "carved", "painted", "variant") ? 10 : 0;

        private static bool IsSeasonal(string id)
            => ContainsAny(id,
                "seasonal", "yule", "xmas", "christmas", "halloween",
                "midsummer", "maypole", "jackoturnip", "mistletoe");

        private static bool IsSpecial(string id)
            => ContainsAny(id, "special", "event", "anniversary", "celebration");

        private static bool ContainsAny(string value, params string[] markers)
        {
            for (int i = 0; i < markers.Length; i++)
            {
                if (value.IndexOf(markers[i], StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static int TrailingNumber(string value)
        {
            int end = value.Length - 1;
            while (end >= 0 && char.IsDigit(value[end])) end--;
            if (end == value.Length - 1) return -1;
            return int.TryParse(value.Substring(end + 1), out int number) ? number : -1;
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            char[] buffer = new char[value.Length];
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
