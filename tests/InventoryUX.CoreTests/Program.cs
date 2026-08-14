using InventoryUX.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InventoryUX.CoreTests
{
    internal static class Program
    {
        private static int Main()
        {
            var tests = new Action[]
            {
                FoodRolesUseBalancedThreshold,
                HighestBiomeMarkerWins,
                TypeClassificationUsesGameMetadata,
                FoodStationGroupingUsesNutritionStats,
                FoodPrepUsesFeastGroup,
                FishingBaitGetsBottomFoodGroup,
                FeastStrengthUsesTotalNutrition,
                MeadStationGroupingUsesEffectType,
                LaterBiomesRenderFirst,
                SortingDoesNotDependOnCraftability,
                EmptyFutureGroupsAreNeverCreated,
                SearchMatchesOnlyVisibleRecipeFacts,
                HammerSearchMatchesNamesAndIds,
                FavoritePiecePreferencesAreStable,
                PlantEverythingPiecesUseCustomPlantDetection,
                MiscShelfSmokeTestCompletesAllRows,
                BuildingShelfUsesFullWidth,
                ShelfPlannerNeverOverflowsSupportedLayouts,
                HammerTabsSortEarlyToLateWithoutAlphabeticalFallback,
                HammerFamiliesAndVariantsStayCoherent,
                MiscTravelUsesRequestedProgression,
                MiscDefensePinsRoundpoleFirstAndShieldGeneratorLast,
                SeasonalHammerPiecesSortAfterNormalProgression,
                ToolViewsAreRememberedPerTool
            };

            try
            {
                foreach (Action test in tests) test();
                Console.WriteLine($"Passed {tests.Length} InventoryUX core tests.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static void FoodRolesUseBalancedThreshold()
        {
            Equal(FoodRole.Eitr, FoodClassifier.Classify(35, 35, 25), "Eitr food");
            Equal(FoodRole.Health, FoodClassifier.Classify(50, 20, 0), "Health food");
            Equal(FoodRole.Stamina, FoodClassifier.Classify(20, 50, 0), "Stamina food");
            Equal(FoodRole.Balanced, FoodClassifier.Classify(44, 40, 0), "Balanced food");
        }

        private static void HighestBiomeMarkerWins()
        {
            Equal(ProgressionBiome.Mistlands,
                BiomeClassifier.Classify("SomeModdedBlade", new[] { "Wood", "RefinedEitr" }),
                "Biome should use the latest meaningful ingredient, not the first ingredient");
        }

        private static void TypeClassificationUsesGameMetadata()
        {
            RecipeFacts sword = Facts("SwordBronze", "Bronze Sword", "OneHandedWeapon", "Swords", "Bronze");
            Equal("Swords", RecipeClassifier.GetTypeGroup(sword).Label, "Sword type");

            RecipeFacts helmet = Facts("HelmetIron", "Iron Helmet", "Helmet", "None", "Iron");
            Equal("Armor", RecipeClassifier.GetTypeGroup(helmet).Label, "Armor type");
        }

        private static void FoodStationGroupingUsesNutritionStats()
        {
            var staminaFood = new RecipeFacts("QueensJam", "Queens Jam", "Consumable", "None",
                Array.Empty<string>(), 14, 40, 0, 0);
            Equal("Stamina", RecipeClassifier.GetFoodGroup(staminaFood, FoodGroupingMode.Stat).Label,
                "Food stat group");

            RecipeFacts meadBase = Facts("MeadBaseHealthMinor", "Minor Healing Mead Base", "Consumable", "None", "Honey");
            Equal("Other", RecipeClassifier.GetFoodGroup(meadBase, FoodGroupingMode.Stat).Label,
                "Non-food output at a food station");
            Equal(string.Empty, RecipeClassifier.GetFoodGroup(staminaFood, FoodGroupingMode.Default).Label,
                "Default food view must not create headings");
        }

        private static void FoodPrepUsesFeastGroup()
        {
            var feast = new RecipeFacts("FeastPlains", "Plains Pie Picnic", "Material", "None",
                Array.Empty<string>(), 55, 55, 0, 0, true);
            Equal("Feasts", RecipeClassifier.GetFoodPrepGroup(feast, FoodGroupingMode.Stat).Label,
                "Resolved feast output");

            RecipeFacts unknownServing = Facts("ModdedFeastTray", "Modded Feast", "Material", "None");
            Equal("Feasts", RecipeClassifier.GetFoodPrepGroup(unknownServing, FoodGroupingMode.Stat).Label,
                "Non-food preparation output fallback");
        }

        private static void FeastStrengthUsesTotalNutrition()
        {
            var feast = new RecipeFacts("FeastMistlands", "Mushrooms Galore á la Mistlands", "Material", "None",
                Array.Empty<string>(), 65, 65, 25, 0, true);
            Equal(155f, FoodClassifier.Strength(feast),
                "Feasts should sort greatest-to-lowest using combined HP, Stamina, and Eitr");
        }

        private static void FishingBaitGetsBottomFoodGroup()
        {
            RecipeFacts bait = Facts("FishingBaitCold", "Cold Fishing Bait x20", "Consumable", "None", "Tuna");
            RecipeGroup cauldronGroup = RecipeClassifier.GetFoodGroup(bait, FoodGroupingMode.Stat);
            RecipeGroup foodPrepGroup = RecipeClassifier.GetFoodPrepGroup(bait, FoodGroupingMode.Stat);

            Equal("Bait", cauldronGroup.Label, "Cauldron fishing bait group");
            Equal(5, cauldronGroup.Order, "Cauldron fishing bait should render after food groups");
            Equal("Bait", foodPrepGroup.Label, "Food prep fishing bait group");
            Equal(5, foodPrepGroup.Order, "Food prep fishing bait should render after feasts");
        }

        private static void MeadStationGroupingUsesEffectType()
        {
            RecipeFacts healing = Facts("MeadBaseHealthMinor", "Minor Healing Mead Base", "Consumable", "None", "Honey");
            RecipeFacts stamina = Facts("MeadBaseStaminaLingering", "Lingering Stamina Mead Base", "Consumable", "None", "Cloudberry");
            RecipeFacts utility = Facts("MeadBaseFrostResist", "Frost Resistance Mead Base", "Consumable", "None", "Honey");

            Equal("HP", MeadClassifier.ToGroup(MeadClassifier.Classify(healing)).Label, "Healing mead");
            Equal("Stamina", MeadClassifier.ToGroup(MeadClassifier.Classify(stamina)).Label, "Stamina mead");
            Equal("Utility", MeadClassifier.ToGroup(MeadClassifier.Classify(utility)).Label, "Utility mead");
        }

        private static void LaterBiomesRenderFirst()
        {
            var rows = new[]
            {
                new Row(Facts("SwordBronze", "Bronze Sword", "OneHandedWeapon", "Swords", "Bronze"), true),
                new Row(Facts("SwordFlametal", "Flametal Sword", "OneHandedWeapon", "Swords", "Flametal"), true),
                new Row(Facts("SwordIron", "Iron Sword", "OneHandedWeapon", "Swords", "Iron"), true)
            };

            List<Row> sorted = StableRecipeSorter.Sort(rows, Group, row => row.Facts);
            Equal("SwordFlametal|SwordIron|SwordBronze", string.Join("|", sorted.Select(row => row.Facts.Id)),
                "Biome groups should be late-game first");
        }

        private static void SortingDoesNotDependOnCraftability()
        {
            var firstState = new[]
            {
                new Row(Facts("SwordBronze", "Bronze Sword", "OneHandedWeapon", "Swords", "Bronze"), false),
                new Row(Facts("AxeBronze", "Bronze Axe", "OneHandedWeapon", "Axes", "Bronze"), true),
                new Row(Facts("ShieldWood", "Wood Shield", "Shield", "Blocking", "Wood"), false)
            };
            var secondState = firstState.Select(row => new Row(row.Facts, !row.CanCraft)).ToArray();

            List<Row> firstSorted = StableRecipeSorter.Sort(firstState, Group, row => row.Facts);
            List<Row> secondSorted = StableRecipeSorter.Sort(secondState, Group, row => row.Facts);
            Equal(string.Join("|", firstSorted.Select(row => row.Facts.Id)),
                string.Join("|", secondSorted.Select(row => row.Facts.Id)),
                "Craftability must not affect recipe position");
        }

        private static void EmptyFutureGroupsAreNeverCreated()
        {
            var rows = new[]
            {
                new Row(Facts("SwordBronze", "Bronze Sword", "OneHandedWeapon", "Swords", "Bronze"), true)
            };
            IReadOnlyList<RecipeGroup> groups = StableRecipeSorter.VisibleGroups(rows, Group);
            Equal(1, groups.Count, "Only groups represented by visible recipes should be emitted");
            Equal("Black Forest", groups[0].Label, "Known group");
        }

        private static void SearchMatchesOnlyVisibleRecipeFacts()
        {
            RecipeFacts sword = Facts("SwordBronze", "Bronze Sword", "OneHandedWeapon", "Swords", "Bronze");
            Equal(true, RecipeSearch.Matches(sword, "sword"), "Localized-name search");
            Equal(true, RecipeSearch.Matches(sword, "SwordBronze"), "Prefab-id search");
            Equal(false, RecipeSearch.Matches(sword, "silver"), "Unmatched future term");
            Equal(true, RecipeSearch.Matches(sword, ""), "Empty search");
            Equal(true, RecipeSearch.MatchesPrepared(sword, "bronze"), "Prepared search query");
        }

        private static void HammerSearchMatchesNamesAndIds()
        {
            string indexed = HammerPieceSearch.Normalize("piece_wall Wood Wall");
            Equal(true, HammerPieceSearch.Matches(indexed, "wood wall"), "Hammer search localized name");
            Equal(true, HammerPieceSearch.Matches(indexed, "piece_wall"), "Hammer search prefab id");
            Equal(true, HammerPieceSearch.MatchesPrepared(indexed, HammerPieceSearch.Normalize("wood wall")),
                "Prepared Hammer search query");
            Equal(false, HammerPieceSearch.Matches(indexed, "stone"), "Hammer search mismatch");
        }

        private static void FavoritePiecePreferencesAreStable()
        {
            string added = FavoritePiecePreferences.Toggle("piece_wall", "piece_floor");
            Equal("piecefloor;piecewall", added, "Favorites serialize deterministically");
            Equal("piecewall", FavoritePiecePreferences.Toggle(added, "piece_floor"), "Favorite toggles off");
            Equal(string.Empty, FavoritePiecePreferences.Toggle("piece_wall", "piece_wall"),
                "The final favorite can be removed");
        }

        private static void PlantEverythingPiecesUseCustomPlantDetection()
        {
            Equal(true, PlantEverythingClassifier.IsCustomPlant("RaspberryBush"), "PlantEverything berry bush");
            Equal(true, PlantEverythingClassifier.IsCustomPlant("PE_VineAsh_sapling(Clone)"), "PlantEverything vine clone");
            Equal(false, PlantEverythingClassifier.IsCustomPlant("sapling_carrot"), "Vanilla crop stays in its normal group");
            Equal(0, PlantEverythingClassifier.GetSubgroup("PickableThistle"), "PlantEverything misc plants lead the group");
            Equal(1, PlantEverythingClassifier.GetSubgroup("RaspberryBush"), "PlantEverything bushes have their own subgroup");
            Equal(2, PlantEverythingClassifier.GetSubgroup("AncientSapling"), "PlantEverything trees have their own subgroup");
            Equal(0, VanillaCultivatorClassifier.GetSubgroup("$piece_cultivate"), "Cultivate leads vanilla plants");
            Equal(0, VanillaCultivatorClassifier.GetSubgroup("$piece_grass"), "Grass leads vanilla plants");
            Equal(1, VanillaCultivatorClassifier.GetSubgroup("sapling_carrot"), "Vanilla crops stay together");
            Equal(2, VanillaCultivatorClassifier.GetSubgroup("sapling_beech"), "Vanilla trees have their own subgroup");
        }

        private static void MiscShelfSmokeTestCompletesAllRows()
        {
            // 42 ordinary entries plus Valheim's static Repair action reproduces
            // the 43-piece Misc. tab. These sizes triggered the former planner's
            // tie case: it combined two groups and exhausted the list one row early.
            var groupSizes = new[] { 8, 5, 4, 4, 14, 7 };
            var names = new[]
            {
                "Travel", "Fire / Comfort", "Defense",
                "Siege", "Resources", "Utility"
            };
            var labels = new List<string>();
            for (int group = 0; group < groupSizes.Length; group++)
            {
                for (int item = 0; item < groupSizes[group]; item++) labels.Add(names[group]);
            }

            int cursor = 0;
            var renderedRows = new List<string>();
            for (int row = 0; row < names.Length; row++)
            {
                int take = ShelfRowPlanner.ChooseGroupRowSize(
                    labels,
                    cursor,
                    15,
                    names.Length - row);
                if (take < 1 || take > 15 || cursor + take > labels.Count)
                    throw new InvalidOperationException("Misc shelf planner produced an invalid row.");

                string rowLabel = labels[cursor];
                for (int item = 1; item < take; item++)
                {
                    Equal(rowLabel, labels[cursor + item], "Misc row must preserve its group");
                }
                renderedRows.Add(rowLabel);
                cursor += take;
            }

            Equal(labels.Count, cursor, "Misc smoke test must place every visible piece");
            Equal(string.Join("|", names), string.Join("|", renderedRows),
                "Misc smoke test section order");
        }

        private static void ShelfPlannerNeverOverflowsSupportedLayouts()
        {
            var random = new Random(91543);
            int verified = 0;
            for (int scenario = 0; scenario < 2000; scenario++)
            {
                int groupCount = random.Next(1, 7);
                var labels = new List<string>();
                int rows = 0;
                for (int group = 0; group < groupCount; group++)
                {
                    int size = random.Next(1, 26);
                    rows += (size + 14) / 15;
                    for (int item = 0; item < size; item++) labels.Add("Group " + group);
                }
                if (rows > 7 || labels.Count > 105) continue;

                int cursor = 0;
                for (int row = 0; row < rows; row++)
                {
                    int take = ShelfRowPlanner.ChooseGroupRowSize(labels, cursor, 15, rows - row);
                    if (take < 1 || take > 15 || cursor + take > labels.Count)
                        throw new InvalidOperationException("Shelf planner overflowed a supported layout.");
                    cursor += take;
                }
                Equal(labels.Count, cursor, "Shelf planner randomized completion");
                verified++;
            }
            if (verified < 500)
                throw new InvalidOperationException("Shelf planner smoke test did not cover enough layouts.");
        }

        private static void BuildingShelfUsesFullWidth()
        {
            var labels = Enumerable.Repeat("Wood", 30).ToArray();
            int cursor = 0;
            for (int row = 0; row < 2; row++)
            {
                int take = ShelfRowPlanner.ChooseGroupRowSize(labels, cursor, 15, 2 - row);
                Equal(15, take, "Thirty unlocked Wood pieces should use two full-width rows");
                cursor += take;
            }

            Equal(labels.Length, cursor, "Full-width Building cells must place every unlocked piece");
        }

        private static void HammerTabsSortEarlyToLateWithoutAlphabeticalFallback()
        {
            HammerSortKey meadows = HammerProgressionSorter.Create(
                HammerSortCategory.Furniture, "ZZZ Wood Chair", new[] { "Wood" });
            HammerSortKey swamp = HammerProgressionSorter.Create(
                HammerSortCategory.Furniture, "AAA Iron Chair", new[] { "Iron" });
            HammerSortKey mistlands = HammerProgressionSorter.Create(
                HammerSortCategory.Furniture, "AAA Dvergr Chair", new[] { "YggdrasilWood" });

            Equal(true, meadows.CompareTo(swamp) < 0, "Meadows before Swamp");
            Equal(true, swamp.CompareTo(mistlands) < 0, "Swamp before Mistlands");
        }

        private static void HammerFamiliesAndVariantsStayCoherent()
        {
            HammerSortKey wall1x1 = HammerProgressionSorter.Create(
                HammerSortCategory.Building, "Wood Wall 1x1", new[] { "Wood" });
            HammerSortKey wall2x2 = HammerProgressionSorter.Create(
                HammerSortCategory.Building, "Wood Wall 2x2", new[] { "Wood" });
            HammerSortKey wall26 = HammerProgressionSorter.Create(
                HammerSortCategory.Building, "Wood Wall 26", new[] { "Wood" });
            HammerSortKey roof = HammerProgressionSorter.Create(
                HammerSortCategory.Building, "Wood Roof", new[] { "Wood" });

            Equal(true, wall1x1.CompareTo(wall2x2) < 0, "Basic wall before larger wall");
            Equal(true, wall2x2.CompareTo(wall26) < 0, "Larger wall before angled wall");
            Equal(true, wall26.CompareTo(roof) < 0, "Wall family before roof family");
        }

        private static void SeasonalHammerPiecesSortAfterNormalProgression()
        {
            HammerSortKey ashlands = HammerProgressionSorter.Create(
                HammerSortCategory.Misc, "Grausten Decoration", new[] { "Grausten" });
            HammerSortKey special = HammerProgressionSorter.Create(
                HammerSortCategory.Misc, "Anniversary Decoration", Array.Empty<string>());
            HammerSortKey seasonal = HammerProgressionSorter.Create(
                HammerSortCategory.Misc, "Yule Decoration", new[] { "Wood" });

            Equal(true, ashlands.CompareTo(special) < 0, "Normal progression before special");
            Equal(true, special.CompareTo(seasonal) < 0, "Special before seasonal");
        }

        private static void MiscTravelUsesRequestedProgression()
        {
            string[] pieces =
            {
                "Cart",
                "Raft",
                "Karve",
                "Longship",
                "Drakkar",
                "Portal"
            };

            for (int i = 1; i < pieces.Length; i++)
            {
                int previous = HammerProgressionSorter.MiscTravelOrder(pieces[i - 1]);
                int current = HammerProgressionSorter.MiscTravelOrder(pieces[i]);
                Equal(true, previous < current, $"{pieces[i - 1]} before {pieces[i]}");
            }

            Equal(-1, HammerProgressionSorter.MiscTravelOrder("Cartography Table"),
                "Cartography Table must not be classified as Travel");
        }

        private static void MiscDefensePinsRoundpoleFirstAndShieldGeneratorLast()
        {
            int roundpole = HammerProgressionSorter.MiscDefenseOrder("piece_wood_fence");
            int palisade = HammerProgressionSorter.MiscDefenseOrder("piece_palisade");
            int shieldGenerator = HammerProgressionSorter.MiscDefenseOrder("piece_shield_generator");

            Equal(true, roundpole < palisade, "Roundpole Fence before other Defense pieces");
            Equal(true, palisade < shieldGenerator, "Shield Generator after other Defense pieces");
        }

        private static void ToolViewsAreRememberedPerTool()
        {
            string preferences = ToolViewPreferences.DefaultValue;

            Equal(true, ToolViewPreferences.IsModView(preferences, "_HammerPieceTable"),
                "Hammer defaults to Mod View");
            Equal(false, ToolViewPreferences.IsModView(preferences, "_HoePieceTable"),
                "Hoe defaults to Default View");
            Equal(false, ToolViewPreferences.IsModView(preferences, "_CultivatorPieceTable(Clone)"),
                "Cultivator clone defaults to Default View");

            preferences = ToolViewPreferences.Set(preferences, "_HoePieceTable(Clone)", true);
            preferences = ToolViewPreferences.Set(preferences, "_HammerPieceTable", false);

            Equal(true, ToolViewPreferences.IsModView(preferences, "_HoePieceTable"),
                "Hoe remembers Mod View independently");
            Equal(false, ToolViewPreferences.IsModView(preferences, "_HammerPieceTable(Clone)"),
                "Hammer remembers Default View independently");
            Equal(false, ToolViewPreferences.IsModView(preferences, "_CultivatorPieceTable"),
                "Cultivator remains unchanged");
            Equal(true, ToolViewPreferences.IsModView(preferences, "_ModdedPieceTable"),
                "Unknown tools default to Mod View");
        }

        private static RecipeGroup Group(Row row) => RecipeClassifier.GetEquipmentGroup(row.Facts, EquipmentGroupingMode.Biome);

        private static RecipeFacts Facts(string id, string name, string type, string skill, params string[] ingredients)
            => new RecipeFacts(id, name, type, skill, ingredients, 0, 0, 0, 0);

        private static void Equal<T>(T expected, T actual, string context)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"{context}: expected {expected}, got {actual}.");
        }

        private sealed class Row
        {
            internal Row(RecipeFacts facts, bool canCraft)
            {
                Facts = facts;
                CanCraft = canCraft;
            }
            internal RecipeFacts Facts { get; }
            internal bool CanCraft { get; }
        }
    }
}
