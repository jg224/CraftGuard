using System;
using System.Collections.Generic;

namespace InventoryUX.Runtime
{
    internal static class FoodStatsResolver
    {
        private static readonly Dictionary<string, ItemDrop> CookedByInput =
            new Dictionary<string, ItemDrop>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ItemDrop> FeastFoodByInput =
            new Dictionary<string, ItemDrop>(StringComparer.Ordinal);
        private static readonly Dictionary<int, FoodResolutionCacheEntry> ResolutionByOutput =
            new Dictionary<int, FoodResolutionCacheEntry>();
        private static int _sceneInstanceId = int.MinValue;
        private static int _scenePrefabCount = -1;

        internal static void Reset()
        {
            CookedByInput.Clear();
            FeastFoodByInput.Clear();
            ResolutionByOutput.Clear();
            _sceneInstanceId = int.MinValue;
            _scenePrefabCount = -1;
        }

        internal static ResolvedFoodStats Resolve(ItemDrop output)
        {
            if (ReferenceEquals(output, null) || output == null) return default;
            bool indexReady = EnsureIndex();
            int outputInstanceId = output.GetInstanceID();
            if (ResolutionByOutput.TryGetValue(outputInstanceId, out FoodResolutionCacheEntry cached)
                && ReferenceEquals(cached.Output, output))
            {
                return cached.Stats;
            }

            Feast? directFeast = output.GetComponent<Feast>() ?? output.GetComponentInChildren<Feast>(true);
            ItemDrop? directFeastFood = directFeast != null ? GetFeastFood(directFeast) : null;
            if (directFeastFood != null)
            {
                ResolvedFoodStats direct = FromItem(directFeastFood, true, true);
                if (indexReady) ResolutionByOutput[outputInstanceId] = new FoodResolutionCacheEntry(output, direct);
                return direct;
            }

            string outputId = output.gameObject.name;

            if (FeastFoodByInput.TryGetValue(outputId, out ItemDrop? feastFood) && feastFood != null)
            {
                ResolvedFoodStats feast = FromItem(feastFood!, true, true);
                if (indexReady) ResolutionByOutput[outputInstanceId] = new FoodResolutionCacheEntry(output, feast);
                return feast;
            }

            ItemDrop current = output;
            bool converted = false;
            string? visited0 = null;
            string? visited1 = null;
            string? visited2 = null;
            string? visited3 = null;
            for (int depth = 0; depth < 4; depth++)
            {
                string currentId = current.gameObject.name;
                if (string.Equals(currentId, visited0, StringComparison.Ordinal)
                    || string.Equals(currentId, visited1, StringComparison.Ordinal)
                    || string.Equals(currentId, visited2, StringComparison.Ordinal)
                    || string.Equals(currentId, visited3, StringComparison.Ordinal)
                    || !CookedByInput.TryGetValue(currentId, out ItemDrop? next)
                    || next == null)
                {
                    break;
                }

                if (depth == 0) visited0 = currentId;
                else if (depth == 1) visited1 = currentId;
                else if (depth == 2) visited2 = currentId;
                else visited3 = currentId;

                current = next!;
                converted = true;
            }

            ResolvedFoodStats result = FromItem(current, converted, false);
            if (indexReady) ResolutionByOutput[outputInstanceId] = new FoodResolutionCacheEntry(output, result);
            return result;
        }

        private static bool EnsureIndex()
        {
            ZNetScene? scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null) return false;
            int instanceId = scene.GetInstanceID();
            int prefabCount = scene.m_prefabs.Count;
            if (_sceneInstanceId == instanceId && _scenePrefabCount == prefabCount) return true;

            Reset();
            _sceneInstanceId = instanceId;
            _scenePrefabCount = prefabCount;

            for (int prefabIndex = 0; prefabIndex < scene.m_prefabs.Count; prefabIndex++)
            {
                UnityEngine.GameObject? prefab = scene.m_prefabs[prefabIndex];
                if (prefab == null) continue;

                CookingStation[] cookingStations = prefab.GetComponentsInChildren<CookingStation>(true);
                for (int stationIndex = 0; stationIndex < cookingStations.Length; stationIndex++)
                {
                    List<CookingStation.ItemConversion>? conversions = cookingStations[stationIndex].m_conversion;
                    if (conversions == null) continue;
                    for (int conversionIndex = 0; conversionIndex < conversions.Count; conversionIndex++)
                    {
                        CookingStation.ItemConversion conversion = conversions[conversionIndex];
                        if (conversion?.m_from == null || conversion.m_to == null) continue;
                        string fromId = conversion.m_from.gameObject.name;
                        if (!CookedByInput.ContainsKey(fromId)) CookedByInput.Add(fromId, conversion.m_to);
                    }
                }

                Feast[] feasts = prefab.GetComponentsInChildren<Feast>(true);
                for (int feastIndex = 0; feastIndex < feasts.Length; feastIndex++)
                {
                    Feast feast = feasts[feastIndex];
                    Piece? piece = feast.GetComponent<Piece>() ?? feast.GetComponentInParent<Piece>();
                    ItemDrop? feastFood = GetFeastFood(feast);
                    if (feastFood == null) continue;

                    ItemDrop? feastItem = feast.GetComponent<ItemDrop>() ?? feast.GetComponentInParent<ItemDrop>();
                    if (feastItem != null)
                    {
                        AddFeastMapping(feastItem.gameObject.name, feastFood);
                    }

                    ItemDrop? prefabItem = prefab.GetComponent<ItemDrop>();
                    if (prefabItem != null)
                    {
                        AddFeastMapping(prefabItem.gameObject.name, feastFood);
                    }

                    if (piece == null || piece.m_category != Piece.PieceCategory.Feasts || piece.m_resources == null)
                    {
                        continue;
                    }

                    for (int requirementIndex = 0; requirementIndex < piece.m_resources.Length; requirementIndex++)
                    {
                        ItemDrop? input = piece.m_resources[requirementIndex]?.m_resItem;
                        if (input == null) continue;
                        AddFeastMapping(input.gameObject.name, feastFood);
                    }
                }
            }
            return true;
        }

        private static ItemDrop? GetFeastFood(Feast feast)
        {
            if (feast.m_foodItem != null) return feast.m_foodItem;

            // Feast.Start performs this same fallback at runtime. The resolver
            // scans prefabs before Start has necessarily populated m_foodItem,
            // so mirror it here rather than treating valid feast food as empty.
            return feast.GetComponent<ItemDrop>() ?? feast.GetComponentInParent<ItemDrop>();
        }

        private static void AddFeastMapping(string inputId, ItemDrop feastFood)
        {
            if (!string.IsNullOrEmpty(inputId) && !FeastFoodByInput.ContainsKey(inputId))
            {
                FeastFoodByInput.Add(inputId, feastFood);
            }
        }

        private static ResolvedFoodStats FromItem(ItemDrop item, bool resolved, bool isFeast)
        {
            ItemDrop.ItemData.SharedData shared = item.m_itemData.m_shared;
            return new ResolvedFoodStats(shared.m_food, shared.m_foodStamina, shared.m_foodEitr, resolved, isFeast);
        }

        private readonly struct FoodResolutionCacheEntry
        {
            internal FoodResolutionCacheEntry(ItemDrop output, ResolvedFoodStats stats)
            {
                Output = output;
                Stats = stats;
            }

            internal ItemDrop Output { get; }
            internal ResolvedFoodStats Stats { get; }
        }
    }

    internal readonly struct ResolvedFoodStats
    {
        internal ResolvedFoodStats(float health, float stamina, float eitr, bool resolved, bool isFeast)
        {
            Health = health;
            Stamina = stamina;
            Eitr = eitr;
            Resolved = resolved;
            IsFeast = isFeast;
        }

        internal float Health { get; }
        internal float Stamina { get; }
        internal float Eitr { get; }
        internal bool Resolved { get; }
        internal bool IsFeast { get; }
    }
}
