using HarmonyLib;
using InventoryUX.Core;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace InventoryUX.Runtime
{
    internal static class RecipeOrganizer
    {
        private const string ContentPrefix = "InventoryUX_Recipe_";
        private const string FixedPrefix = "InventoryUX_Crafting_";
        private const float ContentTopPadding = 2f;
        private const float SearchStripHeight = 34f;
        private const float SearchFieldHeight = 29f;
        private const float HeaderHeight = 22f;
        private const float ModeButtonWidth = 70f;
        private const float ModeButtonGap = 3f;
        private const float RecipeListWidthScale = 1.20f;
        private const float RecipeNameRightPadding = 5f;

        private static readonly Color Gold = new Color(0.83f, 0.62f, 0.25f, 1f);
        private static readonly Color ActiveBlue = new Color(0.24f, 0.55f, 0.78f, 0.95f);
        private static readonly Color InactiveBrown = new Color(0.12f, 0.10f, 0.09f, 0.96f);
        private static readonly Color SearchBrown = new Color(0.18f, 0.125f, 0.075f, 0.98f);

        private static readonly FieldInfo AvailableRecipesField = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");
        private static readonly FieldInfo SelectedRecipeField = AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
        private static readonly FieldInfo RecipeListBaseSizeField = AccessTools.Field(typeof(InventoryGui), "m_recipeListBaseSize");
        private static readonly MethodInfo GetSelectedRecipeIndexMethod = AccessTools.Method(
            typeof(InventoryGui),
            "GetSelectedRecipeIndex",
            new[] { typeof(bool) });
        private static readonly MethodInfo SetRecipeMethod = AccessTools.Method(
            typeof(InventoryGui),
            "SetRecipe",
            new[] { typeof(int), typeof(bool) });

        private static Type? _pairType;
        private static PropertyInfo? _recipeProperty;
        private static PropertyInfo? _elementProperty;
        private static FixedControls? _fixedControls;
        private static string _searchText = string.Empty;
        private static string _activeStationKey = string.Empty;
        private static bool _searchHasFocus;
        private static InventoryGui? _cachedRecipeOwner;
        private static List<RecipePairView>? _cachedRecipeViews;
        private static List<RecipePairView>? _orderedRecipeViews;
        private static readonly List<RecipePairView> VisibleRecipeScratch = new List<RecipePairView>();
        private static int _cachedOrderingKey = int.MinValue;
        private static StationRecipeContext _cachedRecipeContext;
        private static FoodStationKind _cachedFoodStationKind;
        private static RectTransform? _contentPoolOwner;
        private static readonly List<TMP_Text> ContentTextPool = new List<TMP_Text>();
        private static readonly List<Image> ContentLinePool = new List<Image>();
        private static int _usedContentTexts;
        private static int _usedContentLines;
        private static RecipePanelLayoutState? _recipePanelLayout;
        private static readonly Dictionary<int, RecipeRowLayoutState> RecipeRowLayouts =
            new Dictionary<int, RecipeRowLayoutState>();

        // Input patches run many times per frame. Event handlers keep this flag
        // authoritative so those patches never need to query Unity hierarchy state.
        internal static bool IsSearchFocused => _searchHasFocus;

        internal static void ResetRecipeListScrollToTop(InventoryGui gui)
        {
            if (gui == null) return;

            ScrollRect? scrollRect = gui.m_recipeEnsureVisible != null
                ? gui.m_recipeEnsureVisible.GetComponent<ScrollRect>()
                : null;
            if (scrollRect == null && gui.m_recipeListRoot != null)
            {
                scrollRect = gui.m_recipeListRoot.GetComponentInParent<ScrollRect>();
            }

            if (scrollRect != null)
            {
                scrollRect.StopMovement();
                scrollRect.verticalNormalizedPosition = 1f;
            }

            if (gui.m_recipeListScroll != null)
            {
                gui.m_recipeListScroll.value = 1f;
            }
        }

        internal static void Organize(InventoryGui gui)
        {
            if (gui.m_recipeListRoot == null)
            {
                return;
            }

            ClearContentDecorations(gui.m_recipeListRoot);
            Player? player = Player.m_localPlayer;
            CraftingStation? station = player != null ? player.GetCurrentCraftingStation() : null;
            if (!gui.InCraftTab() || station == null)
            {
                RemoveRecipeFavoriteButtons(gui.m_recipeListRoot);
                HideFixedControls(gui);
                RestoreRecipeRowLayouts();
                RestoreRecipePanelLayout(gui);
                return;
            }

            EnsureRecipePanelLayout(gui);
            PrepareStationSearch(station);
            FoodStationKind foodStationKind = GetFoodStationKind(station);
            bool resolveFoodStats = GuessContextFromStation(station) == StationRecipeContext.Food;

            IList pairs = (IList)AvailableRecipesField.GetValue(gui);
            if (pairs.Count == 0)
            {
                StationRecipeContext emptyContext = GuessContextFromStation(station);
                EnsureFixedControls(gui, emptyContext);
                CenterNativeRecipeTabs(gui);
                SetFixedControlsVisible(true);
                UpdateModeAppearance();
                CacheRecipeViews(gui, new List<RecipePairView>(), emptyContext, foodStationKind);
                ApplyCachedRecipeView(gui);
                return;
            }

            EnsurePairAccessors(pairs[0]!);
            var allViews = new List<RecipePairView>(pairs.Count);
            for (int i = 0; i < pairs.Count; i++)
            {
                object pair = pairs[i]!;
                Recipe? recipe = (Recipe?)_recipeProperty!.GetValue(pair, null);
                GameObject? element = (GameObject?)_elementProperty!.GetValue(pair, null);
                if (recipe == null || element == null)
                {
                    // Never drop an unusual modded entry. If a row does not use
                    // the native shape, leave the complete vanilla list alone.
                    RemoveRecipeFavoriteButtons(gui.m_recipeListRoot);
                    HideFixedControls(gui);
                    RestoreRecipeRowLayouts();
                    RestoreRecipePanelLayout(gui);
                    return;
                }

                allViews.Add(new RecipePairView(
                    pair,
                    element,
                    recipe,
                    ReadFacts(recipe, i, foodStationKind, resolveFoodStats)));
            }

            StationRecipeContext context = ClassifyContext(station, allViews);
            if (context == StationRecipeContext.Food && !resolveFoodStats)
            {
                // A modded food station may not use a recognizable station name.
                // Resolve its prepared-food mappings only after its recipes prove
                // that it is a food station, keeping equipment stations scan-free.
                for (int i = 0; i < allViews.Count; i++)
                {
                    allViews[i].Facts = ReadFacts(allViews[i].Recipe, i, foodStationKind, true);
                }
            }
            EnsureFixedControls(gui, context);
            CenterNativeRecipeTabs(gui);
            SetFixedControlsVisible(true);
            UpdateModeAppearance();

            CacheRecipeViews(gui, allViews, context, foodStationKind);
            ApplyCachedRecipeView(gui);
        }

        internal static void PrepareForVanillaRecipeRefresh(InventoryGui gui)
        {
            RestoreRecipeRowLayouts();
            RemoveRecipeFavoriteButtons(gui.m_recipeListRoot);
            RestoreCachedRecipeList(gui);
        }

        private static void CacheRecipeViews(
            InventoryGui gui,
            List<RecipePairView> views,
            StationRecipeContext context,
            FoodStationKind foodStationKind)
        {
            _cachedRecipeOwner = gui;
            _cachedRecipeViews = views;
            _orderedRecipeViews = null;
            _cachedOrderingKey = int.MinValue;
            _cachedRecipeContext = context;
            _cachedFoodStationKind = foodStationKind;
        }

        private static void ApplyCachedRecipeView(InventoryGui gui)
        {
            if (_cachedRecipeViews == null || !ReferenceEquals(_cachedRecipeOwner, gui)) return;

            BeginContentDecorationPass(gui.m_recipeListRoot);
            IList pairs = (IList)AvailableRecipesField.GetValue(gui);
            List<RecipePairView> allViews = _cachedRecipeViews;
            StationRecipeContext context = _cachedRecipeContext;
            FoodStationKind foodStationKind = _cachedFoodStationKind;
            List<RecipePairView> orderedViews = EnsureRecipeOrdering(allViews, context, foodStationKind);

            VisibleRecipeScratch.Clear();
            if (VisibleRecipeScratch.Capacity < orderedViews.Count)
            {
                VisibleRecipeScratch.Capacity = orderedViews.Count;
            }
            string preparedSearch = _searchText.Trim();
            for (int i = 0; i < orderedViews.Count; i++)
            {
                RecipePairView view = orderedViews[i];
                bool visible = view.Element != null && RecipeSearch.MatchesPrepared(view.Facts, preparedSearch);
                if (view.Element != null)
                {
                    view.Element.SetActive(visible);
                }

                if (visible)
                {
                    VisibleRecipeScratch.Add(view);
                }
            }

            bool grouped = IsGrouped(context);
            pairs.Clear();
            for (int i = 0; i < VisibleRecipeScratch.Count; i++)
            {
                pairs.Add(VisibleRecipeScratch[i].Pair);
            }

            Render(gui, VisibleRecipeScratch, context, foodStationKind, grouped);
            UpdateSelectedRecipe(gui, VisibleRecipeScratch);
        }

        private static List<RecipePairView> EnsureRecipeOrdering(
            List<RecipePairView> allViews,
            StationRecipeContext context,
            FoodStationKind foodStationKind)
        {
            int orderingKey = GetOrderingKey(context, foodStationKind);
            if (_orderedRecipeViews != null && _cachedOrderingKey == orderingKey)
            {
                return _orderedRecipeViews;
            }

            var ordered = new List<RecipePairView>(allViews);
            bool grouped = IsGrouped(context);
            bool hasFavorites = false;
            for (int i = 0; i < ordered.Count; i++)
            {
                RecipePairView view = ordered[i];
                view.IsFavorite = ModConfig.IsFavoriteRecipe(GetRecipeKey(view.Recipe));
                view.Group = new RecipeGroup("All Recipes", 0);
                if (view.IsFavorite) hasFavorites = true;
            }
            if (grouped && context == StationRecipeContext.Food)
            {
                FoodGroupingMode mode = ModConfig.FoodMode;
                for (int i = 0; i < ordered.Count; i++)
                {
                    RecipePairView view = ordered[i];
                    RecipeGroup biome = BiomeClassifier.GetGroup(view.Facts);
                    RecipeGroup stat;
                    if (foodStationKind == FoodStationKind.Mead)
                    {
                        stat = MeadClassifier.ToGroup(MeadClassifier.Classify(view.Facts));
                    }
                    else if (foodStationKind == FoodStationKind.FoodPrep)
                    {
                        stat = RecipeClassifier.GetFoodPrepGroup(view.Facts, FoodGroupingMode.Stat);
                    }
                    else
                    {
                        stat = RecipeClassifier.GetFoodGroup(view.Facts, FoodGroupingMode.Stat);
                    }

                    view.Group = mode == FoodGroupingMode.Biome ? biome : stat;
                    view.Suborder = mode == FoodGroupingMode.Biome ? stat.Order
                        : foodStationKind == FoodStationKind.Mead ? biome.Order : 0;
                    view.Strength = foodStationKind != FoodStationKind.Mead && view.Facts.IsFood
                        ? FoodClassifier.Strength(view.Facts)
                        : 0f;
                }
            }
            else if (grouped)
            {
                EquipmentGroupingMode mode = ModConfig.EquipmentMode;
                for (int i = 0; i < ordered.Count; i++)
                {
                    RecipePairView view = ordered[i];
                    RecipeGroup type = RecipeClassifier.GetTypeGroup(view.Facts);
                    RecipeGroup biome = BiomeClassifier.GetGroup(view.Facts);
                    view.Group = mode == EquipmentGroupingMode.Biome ? biome : type;
                    view.Suborder = mode == EquipmentGroupingMode.Type ? biome.Order : type.Order;
                }
            }

            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].IsFavorite)
                {
                    ordered[i].Group = new RecipeGroup("Favorites", int.MinValue);
                }
            }

            if (grouped)
            {
                ordered.Sort(CompareViews);
            }
            else if (hasFavorites)
            {
                ordered.Sort(CompareDefaultViews);
            }
            _orderedRecipeViews = ordered;
            _cachedOrderingKey = orderingKey;
            return ordered;
        }

        private static bool IsGrouped(StationRecipeContext context)
            => context == StationRecipeContext.Food
                ? ModConfig.FoodMode != FoodGroupingMode.Default
                : ModConfig.EquipmentMode != EquipmentGroupingMode.Default;

        private static int GetOrderingKey(StationRecipeContext context, FoodStationKind foodStationKind)
        {
            int mode = context == StationRecipeContext.Food
                ? (int)ModConfig.FoodMode
                : (int)ModConfig.EquipmentMode;
            return ((int)context << 16) ^ ((int)foodStationKind << 8) ^ mode;
        }

        internal static void Cleanup(InventoryGui gui)
        {
            RestoreRecipeRowLayouts();
            if (gui.m_recipeListRoot != null)
            {
                RestoreCachedRecipeList(gui);
                ClearContentDecorations(gui.m_recipeListRoot);
                RemoveRecipeFavoriteButtons(gui.m_recipeListRoot);
            }

            HideFixedControls(gui);
            RestoreRecipePanelLayout(gui);
        }

        internal static void Release(InventoryGui gui)
        {
            Exception? failure = null;
            try
            {
                RestoreRecipeRowLayouts();
                if (gui != null && gui.m_recipeListRoot != null)
                {
                    RestoreCachedRecipeList(gui);
                    ClearContentDecorations(gui.m_recipeListRoot);
                    RemoveRecipeFavoriteButtons(gui.m_recipeListRoot);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                if (_fixedControls != null && ReferenceEquals(_fixedControls.Owner, gui))
                {
                    DestroyFixedControls();
                }
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }

            try
            {
                if (_contentPoolOwner == null || gui == null || _contentPoolOwner == gui.m_recipeListRoot)
                {
                    DestroyContentPool();
                }
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }

            try
            {
                RestoreRecipePanelLayout(gui);
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }
            finally
            {
                ResetTransientState();
            }

            if (failure != null) throw new InvalidOperationException("Could not fully release CraftIndex recipe UI.", failure);
        }

        internal static void Shutdown()
        {
            InventoryGui? owner = _cachedRecipeOwner;
            try
            {
                RestoreRecipeRowLayouts();
                if (_cachedRecipeOwner != null && _cachedRecipeOwner.m_recipeListRoot != null)
                {
                    RemoveRecipeFavoriteButtons(_cachedRecipeOwner.m_recipeListRoot);
                }
                DestroyFixedControls();
                DestroyContentPool();
                RestoreRecipePanelLayout(owner);
            }
            finally
            {
                ResetTransientState();
                _pairType = null;
                _recipeProperty = null;
                _elementProperty = null;
                ClearRecipeCache();
            }
        }

        private static void PrepareStationSearch(CraftingStation station)
        {
            string stationKey = station.m_name + "|" + station.gameObject.name;
            if (string.Equals(_activeStationKey, stationKey, StringComparison.Ordinal)) return;
            _activeStationKey = stationKey;
            _searchText = string.Empty;
        }

        private static StationRecipeContext ClassifyContext(CraftingStation station, IReadOnlyList<RecipePairView> views)
        {
            StationRecipeContext guessed = GuessContextFromStation(station);
            if (guessed == StationRecipeContext.Food) return guessed;

            int foods = 0;
            int equipment = 0;
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].Facts.IsFood) foods++;
                if (RecipeClassifier.GetTypeGroup(views[i].Facts).Label != "Other") equipment++;
            }

            return foods > 0 && (equipment == 0 || foods * 2 >= views.Count)
                ? StationRecipeContext.Food
                : StationRecipeContext.Equipment;
        }

        private static StationRecipeContext GuessContextFromStation(CraftingStation station)
        {
            string value = (station.m_name + " " + station.gameObject.name).ToLowerInvariant();
            return value.Contains("cauldron")
                || value.Contains("cooking")
                || value.Contains("kitchen")
                || value.Contains("oven")
                || value.Contains("food")
                || value.Contains("mead")
                || value.Contains("ketill")
                || value.Contains("preptable")
                ? StationRecipeContext.Food
                : StationRecipeContext.Equipment;
        }

        private static FoodStationKind GetFoodStationKind(CraftingStation station)
        {
            string value = (station.m_name + " " + station.gameObject.name).ToLowerInvariant();
            if (value.Contains("foodpreparation") || value.Contains("preptable"))
            {
                return FoodStationKind.FoodPrep;
            }

            return value.Contains("mead")
                || value.Contains("ketill")
                || value.Contains("kettil")
                || value.Contains("kettle")
                ? FoodStationKind.Mead
                : FoodStationKind.Meals;
        }

        private static int CompareViews(RecipePairView left, RecipePairView right)
        {
            int comparison = left.Group.Order.CompareTo(right.Group.Order);
            if (comparison != 0) return comparison;
            comparison = left.Suborder.CompareTo(right.Suborder);
            if (comparison != 0) return comparison;
            comparison = right.Strength.CompareTo(left.Strength);
            if (comparison != 0) return comparison;
            comparison = string.Compare(left.Facts.DisplayName, right.Facts.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            if (comparison != 0) return comparison;
            comparison = string.Compare(left.Facts.Id, right.Facts.Id, StringComparison.Ordinal);
            return comparison != 0 ? comparison : left.Facts.OriginalIndex.CompareTo(right.Facts.OriginalIndex);
        }

        private static int CompareDefaultViews(RecipePairView left, RecipePairView right)
        {
            if (left.IsFavorite != right.IsFavorite) return left.IsFavorite ? -1 : 1;
            return left.Facts.OriginalIndex.CompareTo(right.Facts.OriginalIndex);
        }

        private static void Render(
            InventoryGui gui,
            IReadOnlyList<RecipePairView> views,
            StationRecipeContext context,
            FoodStationKind foodStationKind,
            bool grouped)
        {
            RectTransform root = gui.m_recipeListRoot;
            float cursor = ContentTopPadding;
            string? previousGroup = null;
            bool showFavoriteSections = false;
            for (int i = 0; i < views.Count; i++)
            {
                if (views[i].IsFavorite)
                {
                    showFavoriteSections = true;
                    break;
                }
            }

            if (views.Count == 0 && !string.IsNullOrWhiteSpace(_searchText))
            {
                CreateStatus(gui, root, "No matching unlocked recipes", cursor);
                cursor += HeaderHeight;
            }

            for (int i = 0; i < views.Count; i++)
            {
                RecipePairView view = views[i];
                string sectionLabel = view.IsFavorite
                    ? "Favorites"
                    : grouped ? view.Group.Label : "All Recipes";
                if ((grouped || showFavoriteSections)
                    && !string.Equals(previousGroup, sectionLabel, StringComparison.Ordinal))
                {
                    CreateHeader(gui, root, sectionLabel, cursor);
                    cursor += HeaderHeight;
                    previousGroup = sectionLabel;
                }

                RectTransform elementRect = (RectTransform)view.Element.transform;
                elementRect.anchoredPosition = new Vector2(0f, -cursor);
                float rowHeight = ConfigureRecipeRowLayout(gui, view);
                if (context == StationRecipeContext.Food
                    && foodStationKind != FoodStationKind.Mead
                    && (view.Facts.IsFood || view.Facts.IsFeast))
                {
                    AddMealStats(gui, view);
                }
                AddRecipeFavoriteButton(gui, view);
                cursor += rowHeight;
            }

            float baseSize = (float)RecipeListBaseSizeField.GetValue(gui);
            root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(baseSize, cursor));
        }

        private static void EnsureFixedControls(InventoryGui gui, StationRecipeContext context)
        {
            if (_fixedControls != null
                && _fixedControls.Owner == gui
                && _fixedControls.Context == context
                && _fixedControls.ModeRoot != null
                && _fixedControls.SearchRoot != null)
            {
                if (!string.Equals(_fixedControls.SearchInput.text, _searchText, StringComparison.Ordinal))
                {
                    _fixedControls.SearchInput.SetTextWithoutNotify(_searchText);
                }
                _fixedControls.SearchRoot.transform.SetAsLastSibling();
                return;
            }

            if (_fixedControls != null)
            {
                DestroyFixedControls();
            }

            GameObject modeRoot = CreateModeRoot(gui, context, out ModeButtonState[] modeButtons);
            GameObject searchRoot = CreateSearchRoot(gui, out TMP_InputField input, out RectTransform recipeViewport,
                out Vector2 viewportOffsetMin, out Vector2 viewportOffsetMax);
            _fixedControls = new FixedControls(gui, context, modeRoot, searchRoot, input, modeButtons,
                recipeViewport, viewportOffsetMin, viewportOffsetMax);
            input.SetTextWithoutNotify(_searchText);
        }

        private static GameObject CreateModeRoot(
            InventoryGui gui,
            StationRecipeContext context,
            out ModeButtonState[] buttons)
        {
            RectTransform upgradeRect = (RectTransform)gui.m_tabUpgrade.transform;
            RectTransform parent = gui.m_crafting;
            var rootObject = new GameObject(FixedPrefix + "Modes", typeof(RectTransform));
            rootObject.transform.SetParent(parent, false);
            rootObject.transform.SetAsLastSibling();

            var root = (RectTransform)rootObject.transform;
            float upgradeWidth = Mathf.Abs(upgradeRect.rect.width) > 1f ? upgradeRect.rect.width : upgradeRect.sizeDelta.x;
            float upgradeHeight = Mathf.Abs(upgradeRect.rect.height) > 1f ? upgradeRect.rect.height : upgradeRect.sizeDelta.y;
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0f, upgradeRect.pivot.y);
            root.sizeDelta = new Vector2(ModeButtonWidth * 3f + ModeButtonGap * 2f, Mathf.Max(25f, upgradeHeight));
            root.rotation = upgradeRect.rotation;
            root.position = _recipePanelLayout != null
                ? _recipePanelLayout.ModeRootWorldPosition
                : upgradeRect.TransformPoint(new Vector3(upgradeRect.rect.xMax + 5f, 0f, 0f));

            string primaryCaption = context == StationRecipeContext.Food ? "STAT" : "TYPE";
            buttons = new[]
            {
                CreateModeButton(gui, root, context, DisplayedMode.Default, "DEFAULT", 0),
                CreateModeButton(gui, root, context, DisplayedMode.Primary, primaryCaption, 1),
                CreateModeButton(gui, root, context, DisplayedMode.Biome, "BIOME", 2)
            };
            return rootObject;
        }

        private static ModeButtonState CreateModeButton(
            InventoryGui gui,
            RectTransform parent,
            StationRecipeContext context,
            DisplayedMode mode,
            string caption,
            int index)
        {
            var buttonObject = new GameObject(FixedPrefix + "Mode_" + caption, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(index * (ModeButtonWidth + ModeButtonGap), 0f);
            rect.sizeDelta = new Vector2(ModeButtonWidth, parent.sizeDelta.y);

            Image image = buttonObject.GetComponent<Image>();
            ApplyVanillaButtonSprite(gui, image);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                if (context == StationRecipeContext.Food)
                {
                    FoodGroupingMode selected = mode == DisplayedMode.Default
                        ? FoodGroupingMode.Default
                        : mode == DisplayedMode.Primary ? FoodGroupingMode.Stat : FoodGroupingMode.Biome;
                    if (ModConfig.FoodMode == selected) return;
                    ModConfig.SetFoodMode(selected);
                }
                else
                {
                    EquipmentGroupingMode selected = mode == DisplayedMode.Default
                        ? EquipmentGroupingMode.Default
                        : mode == DisplayedMode.Primary ? EquipmentGroupingMode.Type : EquipmentGroupingMode.Biome;
                    if (ModConfig.EquipmentMode == selected) return;
                    ModConfig.SetEquipmentMode(selected);
                }

                UpdateModeAppearance();
                ApplyCachedRecipeView(gui);
            });

            TMP_Text label = CreateText(gui, rect, FixedPrefix + "ModeLabel", caption, Vector2.zero, Vector2.zero,
                10.5f, Color.white, TextAlignmentOptions.Center, true);
            return new ModeButtonState(mode, image, label);
        }

        private static GameObject CreateSearchRoot(
            InventoryGui gui,
            out TMP_InputField input,
            out RectTransform recipeViewport,
            out Vector2 viewportOffsetMin,
            out Vector2 viewportOffsetMax)
        {
            RectTransform content = gui.m_recipeListRoot;
            recipeViewport = content.parent as RectTransform
                ?? throw new InvalidOperationException("The crafting recipe list does not have a RectTransform viewport.");
            RectTransform parent = recipeViewport.parent as RectTransform
                ?? throw new InvalidOperationException("The crafting recipe viewport does not have a RectTransform parent.");

            viewportOffsetMin = recipeViewport.offsetMin;
            viewportOffsetMax = recipeViewport.offsetMax;
            var viewportCorners = new Vector3[4];
            recipeViewport.GetWorldCorners(viewportCorners);
            Vector3 searchTopCenter = (viewportCorners[1] + viewportCorners[2]) * 0.5f;
            Vector3 localTopLeft = parent.InverseTransformPoint(viewportCorners[1]);
            Vector3 localTopRight = parent.InverseTransformPoint(viewportCorners[2]);
            float searchWidth = Mathf.Abs(localTopRight.x - localTopLeft.x);

            var searchObject = new GameObject(FixedPrefix + "Search", typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            searchObject.transform.SetParent(parent, false);
            searchObject.transform.SetAsLastSibling();
            var searchRect = (RectTransform)searchObject.transform;
            searchRect.anchorMin = new Vector2(0.5f, 0.5f);
            searchRect.anchorMax = new Vector2(0.5f, 0.5f);
            searchRect.pivot = new Vector2(0.5f, 1f);
            searchRect.sizeDelta = new Vector2(searchWidth, SearchFieldHeight);
            searchRect.rotation = recipeViewport.rotation;
            searchRect.position = searchTopCenter + recipeViewport.TransformVector(new Vector3(0f, -2f, 0f));

            Image background = searchObject.GetComponent<Image>();
            ApplyVanillaButtonSprite(gui, background);
            background.color = SearchBrown;
            AddSearchBorder(searchRect);

            var viewportObject = new GameObject(FixedPrefix + "SearchViewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(searchRect, false);
            var viewport = (RectTransform)viewportObject.transform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(8f, 2f);
            viewport.offsetMax = new Vector2(-27f, -2f);

            var inputText = (TextMeshProUGUI)CreateText(gui, viewport, FixedPrefix + "SearchText", string.Empty,
                Vector2.zero, Vector2.zero, 12f, new Color(0.92f, 0.9f, 0.84f), TextAlignmentOptions.MidlineLeft, true);
            inputText.fontStyle = FontStyles.Normal;

            var placeholder = (TextMeshProUGUI)CreateText(gui, viewport, FixedPrefix + "SearchPlaceholder", "Search recipes",
                Vector2.zero, Vector2.zero, 12f, new Color(0.66f, 0.63f, 0.57f), TextAlignmentOptions.MidlineLeft, true);
            placeholder.fontStyle = FontStyles.Normal;

            input = searchObject.GetComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.textViewport = viewport;
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 64;
            input.caretColor = Gold;
            input.selectionColor = new Color(ActiveBlue.r, ActiveBlue.g, ActiveBlue.b, 0.55f);
            TMP_InputField searchInput = input;
            searchInput.onSelect.AddListener(_ => _searchHasFocus = true);
            searchInput.onDeselect.AddListener(_ => _searchHasFocus = false);
            searchInput.onEndEdit.AddListener(_ => _searchHasFocus = false);
            searchInput.onValueChanged.AddListener(value =>
            {
                string nextValue = value ?? string.Empty;
                if (string.Equals(_searchText, nextValue, StringComparison.Ordinal)) return;

                _searchText = nextValue;
                ApplyCachedRecipeView(gui);
                _searchHasFocus = searchInput != null && searchInput.isFocused;
            });

            CreateClearSearchButton(gui, searchRect, input);
            return searchObject;
        }

        private static void AddSearchBorder(RectTransform parent)
        {
            AddBorderLine(parent, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(0f, 1f));
            AddBorderLine(parent, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(0f, 1f));
            AddBorderLine(parent, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            AddBorderLine(parent, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-1f, 0f), new Vector2(1f, 0f));
        }

        private static void AddBorderLine(
            RectTransform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 position,
            Vector2 size)
        {
            var lineObject = new GameObject(FixedPrefix + "SearchBorder" + name, typeof(RectTransform), typeof(Image));
            lineObject.transform.SetParent(parent, false);
            var rect = (RectTransform)lineObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image line = lineObject.GetComponent<Image>();
            line.color = new Color(Gold.r, Gold.g, Gold.b, 0.62f);
            line.raycastTarget = false;
        }

        private static void CreateClearSearchButton(InventoryGui gui, RectTransform parent, TMP_InputField input)
        {
            var buttonObject = new GameObject(FixedPrefix + "SearchClear", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(25f, 0f);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0.01f);
            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() =>
            {
                _searchText = string.Empty;
                input.SetTextWithoutNotify(string.Empty);
                ApplyCachedRecipeView(gui);
            });

            CreateText(gui, rect, FixedPrefix + "SearchClearLabel", "×", Vector2.zero, Vector2.zero,
                16f, new Color(0.84f, 0.80f, 0.70f), TextAlignmentOptions.Center, true);
        }

        private static void SetFixedControlsVisible(bool visible)
        {
            if (_fixedControls == null) return;
            if (visible)
            {
                _fixedControls.ReserveSearchSpace();
            }
            else
            {
                _searchHasFocus = false;
                _fixedControls.SearchInput.DeactivateInputField();
                _fixedControls.RestoreRecipeViewport();
            }
            _fixedControls.ModeRoot.SetActive(visible);
            _fixedControls.SearchRoot.SetActive(visible);
            if (visible) _fixedControls.SearchRoot.transform.SetAsLastSibling();
        }

        private static void HideFixedControls(InventoryGui gui)
        {
            if (_fixedControls != null && _fixedControls.Owner == gui)
            {
                SetFixedControlsVisible(false);
            }
        }

        private static void DestroyFixedControls()
        {
            if (_fixedControls == null) return;

            FixedControls controls = _fixedControls;
            _fixedControls = null;
            _searchHasFocus = false;
            Exception? failure = null;
            try
            {
                controls.RestoreRecipeViewport();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                DestroyControlRoot(controls.ModeRoot);
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }

            try
            {
                DestroyControlRoot(controls.SearchRoot);
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }

            if (failure != null) throw new InvalidOperationException("Could not fully destroy CraftIndex recipe controls.", failure);
        }

        private static void DestroyControlRoot(GameObject root)
        {
            if (root == null) return;

            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].onClick.RemoveAllListeners();
            }

            TMP_InputField[] inputs = root.GetComponentsInChildren<TMP_InputField>(true);
            for (int i = 0; i < inputs.Length; i++)
            {
                inputs[i].onValueChanged.RemoveAllListeners();
                inputs[i].onSelect.RemoveAllListeners();
                inputs[i].onDeselect.RemoveAllListeners();
                inputs[i].onEndEdit.RemoveAllListeners();
            }

            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }

        private static void ResetTransientState()
        {
            _searchText = string.Empty;
            _activeStationKey = string.Empty;
            _searchHasFocus = false;
            ClearRecipeCache();
        }

        private static void UpdateModeAppearance()
        {
            if (_fixedControls == null) return;
            DisplayedMode active;
            if (_fixedControls.Context == StationRecipeContext.Food)
            {
                active = ModConfig.FoodMode == FoodGroupingMode.Default
                    ? DisplayedMode.Default
                    : ModConfig.FoodMode == FoodGroupingMode.Stat ? DisplayedMode.Primary : DisplayedMode.Biome;
            }
            else
            {
                active = ModConfig.EquipmentMode == EquipmentGroupingMode.Default
                    ? DisplayedMode.Default
                    : ModConfig.EquipmentMode == EquipmentGroupingMode.Type ? DisplayedMode.Primary : DisplayedMode.Biome;
            }

            for (int i = 0; i < _fixedControls.ModeButtons.Length; i++)
            {
                ModeButtonState state = _fixedControls.ModeButtons[i];
                bool selected = state.Mode == active;
                state.Background.color = selected ? ActiveBlue : InactiveBrown;
                state.Label.color = selected ? Color.white : new Color(0.78f, 0.75f, 0.69f);
            }
        }

        private static void EnsureRecipePanelLayout(InventoryGui gui)
        {
            if (_recipePanelLayout != null
                && ReferenceEquals(_recipePanelLayout.Owner, gui)
                && _recipePanelLayout.CraftingRoot != null)
            {
                return;
            }

            RestoreRecipePanelLayout(null);
            if (gui == null || gui.m_crafting == null || gui.m_recipeListRoot == null) return;
            RectTransform? viewport = gui.m_recipeListRoot.parent as RectTransform;
            if (viewport == null) return;
            RectTransform listContainer = viewport.parent as RectTransform ?? viewport;
            if (listContainer == gui.m_crafting) listContainer = viewport;
            float listWidth = listContainer.rect.width;
            if (listWidth <= 1f) listWidth = Mathf.Abs(listContainer.sizeDelta.x);
            if (listWidth <= 1f) return;

            float widthIncrease = listWidth * (RecipeListWidthScale - 1f);
            var detailRoots = CollectDetailRoots(gui, listContainer);
            if (detailRoots.Count == 0) return;
            _recipePanelLayout = new RecipePanelLayoutState(
                gui,
                gui.m_crafting,
                listContainer,
                viewport,
                gui.m_recipeListRoot,
                detailRoots);

            float craftingWidth = gui.m_crafting.rect.width;
            if (craftingWidth <= 1f) craftingWidth = Mathf.Abs(gui.m_crafting.sizeDelta.x);
            if (craftingWidth > 1f)
            {
                SetWidthKeepingLeft(gui.m_crafting, craftingWidth + widthIncrease);
            }
            SetWidthKeepingLeft(listContainer, listWidth + widthIncrease);
            float viewportWidth = viewport.rect.width;
            if (viewportWidth <= 1f) viewportWidth = Mathf.Abs(viewport.sizeDelta.x);
            if (viewportWidth > 1f)
            {
                SetWidthKeepingLeft(viewport, viewportWidth + widthIncrease);
            }
            float contentWidth = gui.m_recipeListRoot.rect.width;
            if (contentWidth <= 1f) contentWidth = Mathf.Abs(gui.m_recipeListRoot.sizeDelta.x);
            if (contentWidth > 1f)
            {
                SetWidthKeepingLeft(gui.m_recipeListRoot, contentWidth + widthIncrease);
            }
            Vector3 worldShift = gui.m_crafting.TransformVector(new Vector3(widthIncrease, 0f, 0f));
            for (int i = 0; i < _recipePanelLayout.DetailRoots.Count; i++)
            {
                ShiftedRectState shifted = _recipePanelLayout.DetailRoots[i];
                if (shifted.Rect != null) shifted.Rect.position = shifted.WorldPosition + worldShift;
            }
            Canvas.ForceUpdateCanvases();
        }

        private static void CenterNativeRecipeTabs(InventoryGui gui)
        {
            if (_recipePanelLayout == null || _recipePanelLayout.TabsCentered
                || gui.m_tabCraft == null || gui.m_tabUpgrade == null
                || gui.m_recipeListRoot == null)
            {
                return;
            }

            RectTransform? viewport = gui.m_recipeListRoot.parent as RectTransform;
            RectTransform? craft = gui.m_tabCraft.transform as RectTransform;
            RectTransform? upgrade = gui.m_tabUpgrade.transform as RectTransform;
            if (viewport == null || craft == null || upgrade == null) return;

            var corners = new Vector3[4];
            viewport.GetWorldCorners(corners);
            Vector3 targetCenter = (corners[1] + corners[2]) * 0.5f;
            craft.GetWorldCorners(corners);
            Vector3 groupLeft = (corners[0] + corners[1]) * 0.5f;
            upgrade.GetWorldCorners(corners);
            Vector3 groupRight = (corners[2] + corners[3]) * 0.5f;
            Vector3 groupCenter = (groupLeft + groupRight) * 0.5f;

            RectTransform? parent = craft.parent as RectTransform;
            if (parent == null || upgrade.parent != parent) return;
            Vector3 localDelta = parent.InverseTransformVector(targetCenter - groupCenter);
            localDelta.y = 0f;
            localDelta.z = 0f;
            Vector3 worldDelta = parent.TransformVector(localDelta);
            craft.position += worldDelta;
            upgrade.position += worldDelta;
            _recipePanelLayout.TabsCentered = true;
        }

        private static List<RectTransform> CollectDetailRoots(InventoryGui gui, RectTransform listContainer)
        {
            var roots = new HashSet<RectTransform>();
            AddDetailRoot(roots, gui, listContainer, gui.m_recipeName != null ? gui.m_recipeName.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_recipeDecription != null ? gui.m_recipeDecription.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_recipeIcon != null ? gui.m_recipeIcon.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_variantButton != null ? gui.m_variantButton.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_craftButton != null ? gui.m_craftButton.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_craftCancelButton != null ? gui.m_craftCancelButton.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_craftProgressPanel);
            AddDetailRoot(roots, gui, listContainer, gui.m_qualityPanel);
            AddDetailRoot(roots, gui, listContainer, gui.m_itemCraftType != null ? gui.m_itemCraftType.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_minStationLevelIcon != null ? gui.m_minStationLevelIcon.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_minStationLevelText != null ? gui.m_minStationLevelText.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_upgradeItemIcon != null ? gui.m_upgradeItemIcon.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_upgradeItemDurability != null ? gui.m_upgradeItemDurability.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_upgradeItemName != null ? gui.m_upgradeItemName.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_upgradeItemQuality != null ? gui.m_upgradeItemQuality.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_upgradeItemQualityArrow != null ? gui.m_upgradeItemQualityArrow.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_upgradeItemNextQuality != null ? gui.m_upgradeItemNextQuality.transform : null);
            AddDetailRoot(roots, gui, listContainer, gui.m_upgradeItemIndex != null ? gui.m_upgradeItemIndex.transform : null);
            if (gui.m_recipeRequirementList != null)
            {
                for (int i = 0; i < gui.m_recipeRequirementList.Length; i++)
                {
                    GameObject requirement = gui.m_recipeRequirementList[i];
                    AddDetailRoot(roots, gui, listContainer, requirement != null ? requirement.transform : null);
                }
            }
            return new List<RectTransform>(roots);
        }

        private static void AddDetailRoot(
            HashSet<RectTransform> roots,
            InventoryGui gui,
            RectTransform listContainer,
            Transform? candidate)
        {
            if (candidate == null) return;
            Transform current = candidate;
            while (current.parent != null && current.parent != gui.m_crafting)
            {
                current = current.parent;
            }
            if (current.parent != gui.m_crafting || !(current is RectTransform rect)) return;
            if (rect == listContainer || rect.IsChildOf(listContainer)) return;
            roots.Add(rect);
        }

        private static void SetWidthKeepingLeft(RectTransform rect, float width)
        {
            if (rect == null || width <= 1f) return;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 oldLeft = (corners[0] + corners[1]) * 0.5f;
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            rect.GetWorldCorners(corners);
            Vector3 newLeft = (corners[0] + corners[1]) * 0.5f;
            rect.position += oldLeft - newLeft;
        }

        private static void RestoreRecipePanelLayout(InventoryGui? gui)
        {
            if (_recipePanelLayout == null) return;
            RecipePanelLayoutState state = _recipePanelLayout;
            _recipePanelLayout = null;
            state.Restore();
            Canvas.ForceUpdateCanvases();
        }

        private static void RestoreRecipeRowLayouts()
        {
            foreach (RecipeRowLayoutState state in RecipeRowLayouts.Values)
            {
                state.Restore();
            }
            RecipeRowLayouts.Clear();
        }

        private static void ApplyVanillaButtonSprite(InventoryGui gui, Image image)
        {
            Image? vanillaImage = gui.m_tabCraft != null ? gui.m_tabCraft.GetComponent<Image>() : null;
            image.sprite = vanillaImage != null ? vanillaImage.sprite : null;
            image.type = vanillaImage != null ? vanillaImage.type : Image.Type.Simple;
        }

        private static void CreateHeader(InventoryGui gui, RectTransform root, string label, float y)
        {
            TMP_Text text = AcquireContentText(gui, root, ContentPrefix + "Header_" + label);
            ConfigureText(text, gui, label.ToUpperInvariant(), new Vector2(4f, -y),
                new Vector2(0f, HeaderHeight), 13f, Gold, TextAlignmentOptions.MidlineLeft, false);
            RectTransform rect = (RectTransform)text.transform;
            rect.anchorMax = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(-8f, HeaderHeight);

            if (!ModConfig.ShowSeparators.Value) return;
            Image line = AcquireContentLine(root, ContentPrefix + "HeaderLine");
            var lineRect = (RectTransform)line.transform;
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0f, 1f);
            lineRect.anchoredPosition = new Vector2(4f, -(y + HeaderHeight - 2f));
            lineRect.sizeDelta = new Vector2(-8f, 1f);
            line.color = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
            line.raycastTarget = false;
        }

        private static void CreateStatus(InventoryGui gui, RectTransform root, string label, float y)
        {
            TMP_Text text = AcquireContentText(gui, root, ContentPrefix + "Status");
            ConfigureText(text, gui, label, new Vector2(4f, -y), new Vector2(0f, HeaderHeight), 12f,
                new Color(0.72f, 0.69f, 0.62f), TextAlignmentOptions.MidlineLeft, false);
            RectTransform rect = (RectTransform)text.transform;
            rect.anchorMax = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(-8f, HeaderHeight);
        }

        private static float ConfigureRecipeRowLayout(InventoryGui gui, RecipePairView view)
        {
            RectTransform elementRect = (RectTransform)view.Element.transform;
            Transform? nameTransform = view.Element.transform.Find("name");
            if (!(nameTransform is RectTransform nameRect)) return gui.m_recipeListSpace;

            TMP_Text name = nameTransform.GetComponent<TMP_Text>();
            if (name == null) return gui.m_recipeListSpace;

            int elementId = view.Element.GetInstanceID();
            if (!RecipeRowLayouts.TryGetValue(elementId, out RecipeRowLayoutState? state))
            {
                state = new RecipeRowLayoutState(elementRect, nameRect, name);
                RecipeRowLayouts[elementId] = state;
            }

            RectTransform? viewport = gui.m_recipeListRoot.parent as RectTransform;
            float targetWidth = viewport != null ? viewport.rect.width : gui.m_recipeListRoot.rect.width;
            if (targetWidth > 1f)
            {
                elementRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, targetWidth);
            }

            var nameCorners = new Vector3[4];
            nameRect.GetWorldCorners(nameCorners);
            float nameLeft = elementRect.InverseTransformPoint(nameCorners[0]).x - elementRect.rect.xMin;
            if (nameLeft < 1f || nameLeft >= elementRect.rect.width - 10f) nameLeft = 42f;

            Vector2 anchorMin = nameRect.anchorMin;
            Vector2 anchorMax = nameRect.anchorMax;
            anchorMin.x = 0f;
            anchorMax.x = 1f;
            nameRect.anchorMin = anchorMin;
            nameRect.anchorMax = anchorMax;
            Vector2 offsetMin = nameRect.offsetMin;
            Vector2 offsetMax = nameRect.offsetMax;
            offsetMin.x = nameLeft;
            offsetMax.x = -RecipeNameRightPadding;
            nameRect.offsetMin = offsetMin;
            nameRect.offsetMax = offsetMax;

            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.ForceMeshUpdate();
            float availableWidth = Mathf.Max(20f, nameRect.rect.width - 2f);
            float preferredWidth = name.GetPreferredValues(name.text).x;
            bool wrap = preferredWidth > availableWidth;
            float rowHeight = gui.m_recipeListSpace;
            view.NameWrapped = wrap;
            view.RequiredNameHeight = state.NameHeight;
            if (wrap)
            {
                name.textWrappingMode = TextWrappingModes.Normal;
                name.overflowMode = TextOverflowModes.Overflow;
                float preferredHeight = name.GetPreferredValues(name.text, availableWidth, 1000f).y;
                float baseline = view.Facts.IsFood || view.Facts.IsFeast
                    ? Mathf.Min(18f, state.NameHeight)
                    : state.NameHeight;
                float extraHeight = Mathf.Max(0f, preferredHeight - baseline);
                view.RequiredNameHeight = Mathf.Max(state.NameHeight, preferredHeight + 1f);
                rowHeight += extraHeight;
                elementRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    Mathf.Max(state.ElementHeight, rowHeight));
                nameRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, view.RequiredNameHeight);
            }

            UITooltip? tooltip = nameTransform.GetComponent<UITooltip>();
            if (tooltip == null)
            {
                tooltip = nameTransform.gameObject.AddComponent<UITooltip>();
                state.AddedTooltip = tooltip;
            }
            else if (!state.CapturedTooltip)
            {
                state.CapturedTooltip = true;
                state.TooltipText = tooltip.m_text;
            }
            tooltip.m_text = FullRecipeName(view);
            name.raycastTarget = true;
            return rowHeight;
        }

        private static string FullRecipeName(RecipePairView view)
        {
            string value = view.Facts.DisplayName;
            if (view.Recipe != null && view.Recipe.m_amount > 1)
            {
                value += " x" + view.Recipe.m_amount;
            }
            return value;
        }

        private static void AddMealStats(InventoryGui gui, RecipePairView view)
        {
            const string statsName = ContentPrefix + "FoodStats";
            Transform row = view.Element.transform;
            Transform? existing = row.Find(statsName);
            TMP_Text stats;
            if (existing != null)
            {
                stats = existing.GetComponent<TMP_Text>();
            }
            else
            {
                float statsLeft = 42f;
                Transform? nameTransform = row.Find("name");
                if (nameTransform is RectTransform nameRect)
                {
                    var corners = new Vector3[4];
                    nameRect.GetWorldCorners(corners);
                    RectTransform rowRect = (RectTransform)row;
                    float measuredLeft = rowRect.InverseTransformPoint(corners[0]).x - rowRect.rect.xMin;
                    if (measuredLeft > 0f && measuredLeft < rowRect.rect.width)
                    {
                        statsLeft = measuredLeft;
                    }

                    float currentHeight = nameRect.rect.height;
                    if (currentHeight > 20f)
                    {
                        nameRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 20f);
                    }
                    nameRect.anchoredPosition += new Vector2(0f, 7f);
                }

                stats = CreateText(gui, row, statsName, string.Empty, Vector2.zero, Vector2.zero,
                    11f, Color.white, TextAlignmentOptions.MidlineLeft, true);
                RectTransform statsRect = (RectTransform)stats.transform;
                statsRect.anchorMin = new Vector2(0f, 0f);
                statsRect.anchorMax = new Vector2(1f, 0f);
                statsRect.pivot = new Vector2(0f, 0f);
                statsRect.offsetMin = new Vector2(statsLeft, 0f);
                statsRect.offsetMax = new Vector2(-4f, 17f);
                stats.fontStyle = FontStyles.Bold;
                stats.richText = true;
                stats.outlineWidth = 0.1f;
                stats.outlineColor = new Color32(25, 15, 10, 230);
            }

            if (view.NameWrapped
                && row.Find("name") is RectTransform wrappedName
                && RecipeRowLayouts.TryGetValue(view.Element.GetInstanceID(), out RecipeRowLayoutState? layoutState))
            {
                wrappedName.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, view.RequiredNameHeight);
                wrappedName.anchoredPosition = layoutState.NameAnchoredPosition
                    + new Vector2(0f, 7f + Mathf.Max(0f, view.RequiredNameHeight - layoutState.NameHeight) * 0.5f);
            }

            string parts = string.Empty;
            if (view.Facts.Health > 0f)
            {
                parts = "<color=#FF9C7A>HP " + Mathf.RoundToInt(view.Facts.Health) + "</color>";
            }
            if (view.Facts.Stamina > 0f)
            {
                if (parts.Length > 0) parts += "   ";
                parts += "<color=#FFE071>STAM " + Mathf.RoundToInt(view.Facts.Stamina) + "</color>";
            }
            if (view.Facts.Eitr > 0f)
            {
                if (parts.Length > 0) parts += "   ";
                parts += "<color=#D7A5FF>EITR " + Mathf.RoundToInt(view.Facts.Eitr) + "</color>";
            }
            stats.text = parts;
        }

        private static void AddRecipeFavoriteButton(InventoryGui gui, RecipePairView view)
        {
            const string controlName = FixedPrefix + "RecipeFavorite";
            Transform row = view.Element.transform;
            Transform host = row.Find("icon") ?? row;
            Transform? existing = FindNamedDescendant(row, controlName);
            GameObject control;
            if (existing != null)
            {
                control = existing.gameObject;
                control.SetActive(true);
            }
            else
            {
                control = new GameObject(controlName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(UITooltip));
                control.transform.SetParent(host, false);
                var rect = (RectTransform)control.transform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(-2f, -2f);
                rect.sizeDelta = new Vector2(18f, 18f);

                Image image = control.GetComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.01f);
                Button createdButton = control.GetComponent<Button>();
                createdButton.targetGraphic = image;

                TMP_Text label = CreateText(gui, rect, FixedPrefix + "RecipeFavoriteLabel", "★",
                    Vector2.zero, Vector2.zero, 14f, Gold, TextAlignmentOptions.Center, true);
                label.outlineWidth = 0.16f;
                label.outlineColor = Color.black;
            }

            if (control.transform.parent != host)
            {
                control.transform.SetParent(host, false);
            }
            RectTransform controlRect = (RectTransform)control.transform;
            controlRect.anchorMin = new Vector2(1f, 1f);
            controlRect.anchorMax = new Vector2(1f, 1f);
            controlRect.pivot = new Vector2(0.5f, 0.5f);
            controlRect.anchoredPosition = new Vector2(-2f, -2f);
            controlRect.sizeDelta = new Vector2(18f, 18f);

            Button button = control.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                ModConfig.ToggleFavoriteRecipe(GetRecipeKey(view.Recipe));
                _orderedRecipeViews = null;
                _cachedOrderingKey = int.MinValue;
                ApplyCachedRecipeView(gui);
            });

            TMP_Text star = control.transform.Find(FixedPrefix + "RecipeFavoriteLabel").GetComponent<TMP_Text>();
            star.color = view.IsFavorite ? Gold : new Color(0.75f, 0.72f, 0.65f, 0.48f);
            UITooltip tooltip = control.GetComponent<UITooltip>();
            tooltip.m_text = view.IsFavorite ? "Remove from Favorites" : "Pin to Favorites";
            control.transform.SetAsLastSibling();
        }

        private static Transform? FindNamedDescendant(Transform root, string name)
        {
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < descendants.Length; i++)
            {
                if (descendants[i] != null && descendants[i].name == name) return descendants[i];
            }
            return null;
        }

        private static string GetRecipeKey(Recipe recipe)
        {
            string recipeName = recipe != null ? recipe.name : string.Empty;
            string itemName = recipe != null && recipe.m_item != null ? recipe.m_item.gameObject.name : string.Empty;
            return string.IsNullOrWhiteSpace(recipeName) ? itemName : recipeName + ":" + itemName;
        }

        private static void RemoveRecipeFavoriteButtons(RectTransform root)
        {
            if (root == null) return;
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            for (int i = descendants.Length - 1; i >= 0; i--)
            {
                Transform child = descendants[i];
                if (child == null || child.name != FixedPrefix + "RecipeFavorite") continue;
                Button? button = child.GetComponent<Button>();
                if (button != null) button.onClick.RemoveAllListeners();
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private static TMP_Text CreateText(
            InventoryGui gui,
            Transform parent,
            string name,
            string content,
            Vector2 position,
            Vector2 size,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            bool stretch = false)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = stretch ? Vector2.zero : new Vector2(0f, 1f);
            rect.anchorMax = stretch ? Vector2.one : new Vector2(0f, 1f);
            rect.pivot = stretch ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.font = gui.m_recipeName != null ? gui.m_recipeName.font : null;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            return text;
        }

        private static void ConfigureText(
            TMP_Text text,
            InventoryGui gui,
            string content,
            Vector2 position,
            Vector2 size,
            float fontSize,
            Color color,
            TextAlignmentOptions alignment,
            bool stretch)
        {
            RectTransform rect = (RectTransform)text.transform;
            rect.anchorMin = stretch ? Vector2.zero : new Vector2(0f, 1f);
            rect.anchorMax = stretch ? Vector2.one : new Vector2(0f, 1f);
            rect.pivot = stretch ? new Vector2(0.5f, 0.5f) : new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            text.text = content;
            text.font = gui.m_recipeName != null ? gui.m_recipeName.font : null;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            text.color = color;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
        }

        private static RecipeFacts ReadFacts(
            Recipe recipe,
            int originalIndex,
            FoodStationKind foodStationKind,
            bool resolveFoodStats)
        {
            ItemDrop.ItemData.SharedData shared = recipe.m_item.m_itemData.m_shared;
            var ingredients = new List<string>();
            if (recipe.m_resources != null)
            {
                for (int i = 0; i < recipe.m_resources.Length; i++)
                {
                    ItemDrop? item = recipe.m_resources[i]?.m_resItem;
                    if (item != null)
                    {
                        ingredients.Add(item.gameObject.name);
                        ingredients.Add(item.m_itemData.m_shared.m_name);
                    }
                }
            }

            string displayName = Localization.instance != null ? Localization.instance.Localize(shared.m_name) : shared.m_name;
            ResolvedFoodStats resolved = resolveFoodStats && foodStationKind != FoodStationKind.Mead
                ? FoodStatsResolver.Resolve(recipe.m_item)
                : default;
            float health = resolved.Resolved ? resolved.Health : shared.m_food;
            float stamina = resolved.Resolved ? resolved.Stamina : shared.m_foodStamina;
            float eitr = resolved.Resolved ? resolved.Eitr : shared.m_foodEitr;
            return new RecipeFacts(recipe.m_item.gameObject.name, displayName, shared.m_itemType.ToString(),
                shared.m_skillType.ToString(), ingredients, health, stamina, eitr, originalIndex, resolved.IsFeast);
        }

        private static void EnsurePairAccessors(object pair)
        {
            Type type = pair.GetType();
            if (_pairType == type) return;
            _pairType = type;
            _recipeProperty = type.GetProperty("Recipe", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            _elementProperty = type.GetProperty("InterfaceElement", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_recipeProperty == null || _elementProperty == null)
            {
                throw new MissingMemberException(type.FullName, "Recipe/InterfaceElement");
            }
        }

        private static void BeginContentDecorationPass(RectTransform root)
        {
            EnsureContentPoolOwner(root);
            DeactivateContentPool();
            _usedContentTexts = 0;
            _usedContentLines = 0;
        }

        private static TMP_Text AcquireContentText(InventoryGui gui, RectTransform root, string name)
        {
            EnsureContentPoolOwner(root);
            TMP_Text? text = _usedContentTexts < ContentTextPool.Count
                ? ContentTextPool[_usedContentTexts]
                : null;
            if (text == null)
            {
                text = CreateText(gui, root, name, string.Empty, Vector2.zero, Vector2.zero,
                    12f, Color.white, TextAlignmentOptions.MidlineLeft);
                if (_usedContentTexts < ContentTextPool.Count)
                    ContentTextPool[_usedContentTexts] = text;
                else
                    ContentTextPool.Add(text);
            }
            text.gameObject.name = name;
            text.gameObject.SetActive(true);
            text.transform.SetAsLastSibling();
            _usedContentTexts++;
            return text;
        }

        private static Image AcquireContentLine(RectTransform root, string name)
        {
            EnsureContentPoolOwner(root);
            Image? image = _usedContentLines < ContentLinePool.Count
                ? ContentLinePool[_usedContentLines]
                : null;
            if (image == null)
            {
                var lineObject = new GameObject(name, typeof(RectTransform), typeof(Image));
                lineObject.transform.SetParent(root, false);
                image = lineObject.GetComponent<Image>();
                if (_usedContentLines < ContentLinePool.Count)
                    ContentLinePool[_usedContentLines] = image;
                else
                    ContentLinePool.Add(image);
            }
            image.gameObject.name = name;
            image.gameObject.SetActive(true);
            image.transform.SetAsLastSibling();
            _usedContentLines++;
            return image;
        }

        private static void EnsureContentPoolOwner(RectTransform root)
        {
            if (_contentPoolOwner == root) return;
            DestroyContentPool();
            _contentPoolOwner = root;
        }

        private static void DeactivateContentPool()
        {
            for (int i = 0; i < ContentTextPool.Count; i++)
            {
                if (ContentTextPool[i] != null) ContentTextPool[i].gameObject.SetActive(false);
            }
            for (int i = 0; i < ContentLinePool.Count; i++)
            {
                if (ContentLinePool[i] != null) ContentLinePool[i].gameObject.SetActive(false);
            }
        }

        private static void DestroyContentPool()
        {
            for (int i = 0; i < ContentTextPool.Count; i++)
            {
                if (ContentTextPool[i] != null) UnityEngine.Object.Destroy(ContentTextPool[i].gameObject);
            }
            for (int i = 0; i < ContentLinePool.Count; i++)
            {
                if (ContentLinePool[i] != null) UnityEngine.Object.Destroy(ContentLinePool[i].gameObject);
            }
            ContentTextPool.Clear();
            ContentLinePool.Clear();
            _contentPoolOwner = null;
            _usedContentTexts = 0;
            _usedContentLines = 0;
        }

        private static bool IsPooledContentObject(GameObject gameObject)
        {
            for (int i = 0; i < ContentTextPool.Count; i++)
            {
                if (ContentTextPool[i] != null && ContentTextPool[i].gameObject == gameObject) return true;
            }
            for (int i = 0; i < ContentLinePool.Count; i++)
            {
                if (ContentLinePool[i] != null && ContentLinePool[i].gameObject == gameObject) return true;
            }
            return false;
        }

        private static void ClearContentDecorations(RectTransform root)
        {
            if (_contentPoolOwner == root) DeactivateContentPool();
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.name.StartsWith(ContentPrefix, StringComparison.Ordinal))
                {
                    if (IsPooledContentObject(child.gameObject)) continue;
                    child.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static void UpdateSelectedRecipe(InventoryGui gui, IReadOnlyList<RecipePairView> visibleViews)
        {
            object? selectedRecipe = SelectedRecipeField.GetValue(gui);
            if (selectedRecipe != null)
            {
                for (int i = 0; i < visibleViews.Count; i++)
                {
                    if (ReferenceEquals(visibleViews[i].Pair, selectedRecipe)) return;
                }
            }

            int selectedIndex = visibleViews.Count == 0
                ? -1
                : (int)GetSelectedRecipeIndexMethod.Invoke(gui, new object[] { true });
            SetRecipeMethod.Invoke(gui, new object[] { selectedIndex, false });
        }

        private static void RestoreCachedRecipeList(InventoryGui gui)
        {
            if (_cachedRecipeViews == null || !ReferenceEquals(_cachedRecipeOwner, gui)) return;

            ClearContentDecorations(gui.m_recipeListRoot);
            IList pairs = (IList)AvailableRecipesField.GetValue(gui);
            pairs.Clear();
            int restoredCount = 0;
            for (int i = 0; i < _cachedRecipeViews.Count; i++)
            {
                RecipePairView view = _cachedRecipeViews[i];
                if (view.Element == null) continue;

                view.Element.SetActive(true);
                var rect = (RectTransform)view.Element.transform;
                rect.anchoredPosition = new Vector2(0f, -restoredCount * gui.m_recipeListSpace);
                pairs.Add(view.Pair);
                restoredCount++;
            }

            float baseSize = (float)RecipeListBaseSizeField.GetValue(gui);
            gui.m_recipeListRoot.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(baseSize, restoredCount * gui.m_recipeListSpace));
            ClearRecipeCache();
        }

        private static void ClearRecipeCache()
        {
            _cachedRecipeOwner = null;
            _cachedRecipeViews = null;
            _orderedRecipeViews = null;
            VisibleRecipeScratch.Clear();
            _cachedOrderingKey = int.MinValue;
            _cachedRecipeContext = default;
            _cachedFoodStationKind = default;
        }

        private sealed class RectTransformSnapshot
        {
            internal RectTransformSnapshot(RectTransform rect)
            {
                Rect = rect;
                AnchoredPosition = rect.anchoredPosition;
                SizeDelta = rect.sizeDelta;
            }

            internal RectTransform Rect { get; }
            private Vector2 AnchoredPosition { get; }
            private Vector2 SizeDelta { get; }

            internal void Restore()
            {
                if (Rect == null) return;
                Rect.sizeDelta = SizeDelta;
                Rect.anchoredPosition = AnchoredPosition;
            }
        }

        private sealed class ShiftedRectState
        {
            internal ShiftedRectState(RectTransform rect)
            {
                Rect = rect;
                Snapshot = new RectTransformSnapshot(rect);
                WorldPosition = rect.position;
            }

            internal RectTransform Rect { get; }
            internal RectTransformSnapshot Snapshot { get; }
            internal Vector3 WorldPosition { get; }
        }

        private sealed class RecipePanelLayoutState
        {
            internal RecipePanelLayoutState(
                InventoryGui owner,
                RectTransform craftingRoot,
                RectTransform listContainer,
                RectTransform viewport,
                RectTransform contentRoot,
                IReadOnlyList<RectTransform> detailRoots)
            {
                Owner = owner;
                CraftingRoot = craftingRoot;
                CraftingSnapshot = new RectTransformSnapshot(craftingRoot);
                ListContainerSnapshot = new RectTransformSnapshot(listContainer);
                ViewportSnapshot = new RectTransformSnapshot(viewport);
                ContentRootSnapshot = new RectTransformSnapshot(contentRoot);
                RectTransform? craftTab = owner.m_tabCraft != null ? owner.m_tabCraft.transform as RectTransform : null;
                RectTransform? upgradeTab = owner.m_tabUpgrade != null ? owner.m_tabUpgrade.transform as RectTransform : null;
                CraftTabSnapshot = craftTab != null ? new RectTransformSnapshot(craftTab) : null;
                UpgradeTabSnapshot = upgradeTab != null ? new RectTransformSnapshot(upgradeTab) : null;
                ModeRootWorldPosition = upgradeTab != null
                    ? upgradeTab.TransformPoint(new Vector3(upgradeTab.rect.xMax + 5f, 0f, 0f))
                    : Vector3.zero;
                DetailRoots = new List<ShiftedRectState>(detailRoots.Count);
                for (int i = 0; i < detailRoots.Count; i++)
                {
                    DetailRoots.Add(new ShiftedRectState(detailRoots[i]));
                }
            }

            internal InventoryGui Owner { get; }
            internal RectTransform CraftingRoot { get; }
            internal List<ShiftedRectState> DetailRoots { get; }
            internal Vector3 ModeRootWorldPosition { get; }
            internal bool TabsCentered { get; set; }
            private RectTransformSnapshot CraftingSnapshot { get; }
            private RectTransformSnapshot ListContainerSnapshot { get; }
            private RectTransformSnapshot ViewportSnapshot { get; }
            private RectTransformSnapshot ContentRootSnapshot { get; }
            private RectTransformSnapshot? CraftTabSnapshot { get; }
            private RectTransformSnapshot? UpgradeTabSnapshot { get; }

            internal void Restore()
            {
                CraftingSnapshot.Restore();
                ListContainerSnapshot.Restore();
                ViewportSnapshot.Restore();
                ContentRootSnapshot.Restore();
                CraftTabSnapshot?.Restore();
                UpgradeTabSnapshot?.Restore();
                for (int i = 0; i < DetailRoots.Count; i++)
                {
                    DetailRoots[i].Snapshot.Restore();
                }
            }
        }

        private sealed class RecipeRowLayoutState
        {
            internal RecipeRowLayoutState(RectTransform elementRect, RectTransform nameRect, TMP_Text name)
            {
                ElementRect = elementRect;
                ElementSizeDelta = elementRect.sizeDelta;
                ElementHeight = elementRect.rect.height;
                NameRect = nameRect;
                NameAnchorMin = nameRect.anchorMin;
                NameAnchorMax = nameRect.anchorMax;
                NamePivot = nameRect.pivot;
                NameAnchoredPosition = nameRect.anchoredPosition;
                NameSizeDelta = nameRect.sizeDelta;
                NameHeight = nameRect.rect.height;
                Name = name;
                WrappingMode = name.textWrappingMode;
                OverflowMode = name.overflowMode;
                RaycastTarget = name.raycastTarget;
            }

            internal RectTransform ElementRect { get; }
            internal float ElementHeight { get; }
            internal RectTransform NameRect { get; }
            internal float NameHeight { get; }
            internal Vector2 NameAnchoredPosition { get; }
            internal TMP_Text Name { get; }
            internal UITooltip? AddedTooltip { get; set; }
            internal bool CapturedTooltip { get; set; }
            internal string TooltipText { get; set; } = string.Empty;
            private Vector2 ElementSizeDelta { get; }
            private Vector2 NameAnchorMin { get; }
            private Vector2 NameAnchorMax { get; }
            private Vector2 NamePivot { get; }
            private Vector2 NameSizeDelta { get; }
            private TextWrappingModes WrappingMode { get; }
            private TextOverflowModes OverflowMode { get; }
            private bool RaycastTarget { get; }

            internal void Restore()
            {
                if (ElementRect != null) ElementRect.sizeDelta = ElementSizeDelta;
                if (NameRect != null)
                {
                    NameRect.anchorMin = NameAnchorMin;
                    NameRect.anchorMax = NameAnchorMax;
                    NameRect.pivot = NamePivot;
                    NameRect.anchoredPosition = NameAnchoredPosition;
                    NameRect.sizeDelta = NameSizeDelta;
                }
                if (Name != null)
                {
                    Name.textWrappingMode = WrappingMode;
                    Name.overflowMode = OverflowMode;
                    Name.raycastTarget = RaycastTarget;
                }
                if (AddedTooltip != null)
                {
                    UnityEngine.Object.Destroy(AddedTooltip);
                }
                else if (CapturedTooltip && Name != null)
                {
                    UITooltip? tooltip = Name.GetComponent<UITooltip>();
                    if (tooltip != null) tooltip.m_text = TooltipText;
                }
                if (ElementRect != null)
                {
                    Transform? stats = ElementRect.Find(ContentPrefix + "FoodStats");
                    if (stats != null)
                    {
                        stats.gameObject.SetActive(false);
                        UnityEngine.Object.Destroy(stats.gameObject);
                    }
                }
            }
        }

        private sealed class RecipePairView
        {
            internal RecipePairView(object pair, GameObject element, Recipe recipe, RecipeFacts facts)
            {
                Pair = pair;
                Element = element;
                Recipe = recipe;
                Facts = facts;
                Group = new RecipeGroup("Other", 999);
            }

            internal object Pair { get; }
            internal GameObject Element { get; }
            internal Recipe Recipe { get; }
            internal RecipeFacts Facts { get; set; }
            internal RecipeGroup Group { get; set; }
            internal int Suborder { get; set; }
            internal float Strength { get; set; }
            internal bool IsFavorite { get; set; }
            internal bool NameWrapped { get; set; }
            internal float RequiredNameHeight { get; set; }
        }

        private sealed class FixedControls
        {
            private readonly Vector2 _viewportOffsetMin;
            private readonly Vector2 _viewportOffsetMax;
            private bool _searchSpaceReserved;

            internal FixedControls(InventoryGui owner, StationRecipeContext context, GameObject modeRoot, GameObject searchRoot,
                TMP_InputField searchInput, ModeButtonState[] modeButtons, RectTransform recipeViewport,
                Vector2 viewportOffsetMin, Vector2 viewportOffsetMax)
            {
                Owner = owner;
                Context = context;
                ModeRoot = modeRoot;
                SearchRoot = searchRoot;
                SearchInput = searchInput;
                ModeButtons = modeButtons;
                RecipeViewport = recipeViewport;
                _viewportOffsetMin = viewportOffsetMin;
                _viewportOffsetMax = viewportOffsetMax;
            }

            internal InventoryGui Owner { get; }
            internal StationRecipeContext Context { get; }
            internal GameObject ModeRoot { get; }
            internal GameObject SearchRoot { get; }
            internal TMP_InputField SearchInput { get; }
            internal ModeButtonState[] ModeButtons { get; }
            internal RectTransform RecipeViewport { get; }

            internal void ReserveSearchSpace()
            {
                if (_searchSpaceReserved || RecipeViewport == null) return;
                RecipeViewport.offsetMin = _viewportOffsetMin;
                RecipeViewport.offsetMax = new Vector2(_viewportOffsetMax.x, _viewportOffsetMax.y - SearchStripHeight);
                _searchSpaceReserved = true;
            }

            internal void RestoreRecipeViewport()
            {
                if (!_searchSpaceReserved || RecipeViewport == null) return;
                RecipeViewport.offsetMin = _viewportOffsetMin;
                RecipeViewport.offsetMax = _viewportOffsetMax;
                _searchSpaceReserved = false;
            }
        }

        private sealed class ModeButtonState
        {
            internal ModeButtonState(DisplayedMode mode, Image background, TMP_Text label)
            {
                Mode = mode;
                Background = background;
                Label = label;
            }

            internal DisplayedMode Mode { get; }
            internal Image Background { get; }
            internal TMP_Text Label { get; }
        }

        private enum StationRecipeContext
        {
            Equipment,
            Food
        }

        private enum FoodStationKind
        {
            Meals,
            FoodPrep,
            Mead
        }

        private enum DisplayedMode
        {
            Default,
            Primary,
            Biome
        }
    }
}
