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

        // Input patches run many times per frame. Event handlers keep this flag
        // authoritative so those patches never need to query Unity hierarchy state.
        internal static bool IsSearchFocused => _searchHasFocus;

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
                HideFixedControls(gui);
                return;
            }

            PrepareStationSearch(station);
            FoodStationKind foodStationKind = GetFoodStationKind(station);
            bool resolveFoodStats = GuessContextFromStation(station) == StationRecipeContext.Food;

            IList pairs = (IList)AvailableRecipesField.GetValue(gui);
            if (pairs.Count == 0)
            {
                StationRecipeContext emptyContext = GuessContextFromStation(station);
                EnsureFixedControls(gui, emptyContext);
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
                    HideFixedControls(gui);
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
            SetFixedControlsVisible(true);
            UpdateModeAppearance();

            CacheRecipeViews(gui, allViews, context, foodStationKind);
            ApplyCachedRecipeView(gui);
        }

        internal static void PrepareForVanillaRecipeRefresh(InventoryGui gui)
        {
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

            if (grouped) ordered.Sort(CompareViews);
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
            if (gui.m_recipeListRoot != null)
            {
                RestoreCachedRecipeList(gui);
                ClearContentDecorations(gui.m_recipeListRoot);
            }

            HideFixedControls(gui);
        }

        internal static void Release(InventoryGui gui)
        {
            Exception? failure = null;
            try
            {
                if (gui != null && gui.m_recipeListRoot != null)
                {
                    RestoreCachedRecipeList(gui);
                    ClearContentDecorations(gui.m_recipeListRoot);
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
            finally
            {
                ResetTransientState();
            }

            if (failure != null) throw new InvalidOperationException("Could not fully release CraftGuard recipe UI.", failure);
        }

        internal static void Shutdown()
        {
            try
            {
                DestroyFixedControls();
                DestroyContentPool();
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

            if (views.Count == 0 && !string.IsNullOrWhiteSpace(_searchText))
            {
                CreateStatus(gui, root, "No matching unlocked recipes", cursor);
                cursor += HeaderHeight;
            }

            for (int i = 0; i < views.Count; i++)
            {
                RecipePairView view = views[i];
                if (grouped && !string.Equals(previousGroup, view.Group.Label, StringComparison.Ordinal))
                {
                    CreateHeader(gui, root, view.Group.Label, cursor);
                    cursor += HeaderHeight;
                    previousGroup = view.Group.Label;
                }

                RectTransform elementRect = (RectTransform)view.Element.transform;
                elementRect.anchoredPosition = new Vector2(0f, -cursor);
                if (context == StationRecipeContext.Food
                    && foodStationKind != FoodStationKind.Mead
                    && (view.Facts.IsFood || view.Facts.IsFeast))
                {
                    AddMealStats(gui, view);
                }
                cursor += gui.m_recipeListSpace;
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
            root.position = upgradeRect.TransformPoint(new Vector3(upgradeRect.rect.xMax + 5f, 0f, 0f));

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

            if (failure != null) throw new InvalidOperationException("Could not fully destroy CraftGuard recipe controls.", failure);
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
