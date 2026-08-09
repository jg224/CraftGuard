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
        private static int _sceneInstanceId = int.MinValue;
        private static int _scenePrefabCount = -1;

        internal static void Reset()
        {
            CookedByInput.Clear();
            FeastFoodByInput.Clear();
            _sceneInstanceId = int.MinValue;
            _scenePrefabCount = -1;
        }

        internal static ResolvedFoodStats Resolve(ItemDrop output)
        {
            EnsureIndex();
            if (ReferenceEquals(output, null) || output == null) return default;

            Feast? directFeast = output.GetComponent<Feast>() ?? output.GetComponentInChildren<Feast>(true);
            ItemDrop? directFeastFood = directFeast != null ? GetFeastFood(directFeast) : null;
            if (directFeastFood != null)
            {
                return FromItem(directFeastFood, true, true);
            }

            string outputId = output.gameObject.name;

            if (FeastFoodByInput.TryGetValue(outputId, out ItemDrop? feastFood) && feastFood != null)
            {
                return FromItem(feastFood!, true, true);
            }

            ItemDrop current = output;
            bool converted = false;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (int depth = 0; depth < 4; depth++)
            {
                string currentId = current.gameObject.name;
                if (!visited.Add(currentId)
                    || !CookedByInput.TryGetValue(currentId, out ItemDrop? next)
                    || next == null)
                {
                    break;
                }

                current = next!;
                converted = true;
            }

            return FromItem(current, converted, false);
        }

        private static void EnsureIndex()
        {
            ZNetScene? scene = ZNetScene.instance;
            if (scene == null || scene.m_prefabs == null) return;
            int instanceId = scene.GetInstanceID();
            int prefabCount = scene.m_prefabs.Count;
            if (_sceneInstanceId == instanceId && _scenePrefabCount == prefabCount) return;

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
