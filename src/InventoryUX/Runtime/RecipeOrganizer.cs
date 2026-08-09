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
        private static readonly FieldInfo RecipeListBaseSizeField = AccessTools.Field(typeof(InventoryGui), "m_recipeListBaseSize");
        private static readonly MethodInfo RefreshMethod = AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel");

        private static Type? _pairType;
        private static PropertyInfo? _recipeProperty;
        private static PropertyInfo? _elementProperty;
        private static FixedControls? _fixedControls;
        private static string _searchText = string.Empty;
        private static string _activeStationKey = string.Empty;
        private static bool _searchHasFocus;

        internal static bool IsSearchFocused
            => _searchHasFocus
                && _fixedControls != null
                && _fixedControls.SearchRoot != null
                && _fixedControls.SearchRoot.activeInHierarchy;

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

            IList pairs = (IList)AvailableRecipesField.GetValue(gui);
            if (pairs.Count == 0)
            {
                StationRecipeContext emptyContext = GuessContextFromStation(station);
                EnsureFixedControls(gui, emptyContext);
                SetFixedControlsVisible(true);
                UpdateModeAppearance();
                Render(gui, Array.Empty<RecipePairView>(), emptyContext, foodStationKind, false);
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

                allViews.Add(new RecipePairView(pair, element, ReadFacts(recipe, i, foodStationKind)));
            }

            StationRecipeContext context = ClassifyContext(station, allViews);
            EnsureFixedControls(gui, context);
            SetFixedControlsVisible(true);
            UpdateModeAppearance();

            var visibleViews = new List<RecipePairView>(allViews.Count);
            for (int i = 0; i < allViews.Count; i++)
            {
                RecipePairView view = allViews[i];
                if (RecipeSearch.Matches(view.Facts, _searchText))
                {
                    visibleViews.Add(view);
                }
                else
                {
                    view.Element.SetActive(false);
                    UnityEngine.Object.Destroy(view.Element);
                }
            }

            bool grouped;
            if (context == StationRecipeContext.Food)
            {
                FoodGroupingMode mode = ModConfig.FoodMode;
                grouped = mode != FoodGroupingMode.Default;
                for (int i = 0; i < visibleViews.Count; i++)
                {
                    RecipePairView view = visibleViews[i];
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
            else
            {
                EquipmentGroupingMode mode = ModConfig.EquipmentMode;
                grouped = mode != EquipmentGroupingMode.Default;
                for (int i = 0; i < visibleViews.Count; i++)
                {
                    RecipePairView view = visibleViews[i];
                    RecipeGroup type = RecipeClassifier.GetTypeGroup(view.Facts);
                    RecipeGroup biome = BiomeClassifier.GetGroup(view.Facts);
                    view.Group = mode == EquipmentGroupingMode.Biome ? biome : type;
                    view.Suborder = mode == EquipmentGroupingMode.Type ? biome.Order : type.Order;
                }
            }

            if (grouped) visibleViews.Sort(CompareViews);

            pairs.Clear();
            for (int i = 0; i < visibleViews.Count; i++)
            {
                pairs.Add(visibleViews[i].Pair);
            }

            Render(gui, visibleViews, context, foodStationKind, grouped);
        }

        internal static void Cleanup(InventoryGui gui)
        {
            if (gui.m_recipeListRoot != null)
            {
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
            }
            finally
            {
                ResetTransientState();
                _pairType = null;
                _recipeProperty = null;
                _elementProperty = null;
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
                _fixedControls.SearchInput.SetTextWithoutNotify(_searchText);
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
                Refresh(gui);
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
                bool retainFocus = _searchHasFocus || searchInput.isFocused;
                int caretPosition = searchInput.caretPosition;
                int anchorPosition = searchInput.selectionAnchorPosition;
                int focusPosition = searchInput.selectionFocusPosition;
                _searchText = value ?? string.Empty;
                Refresh(gui);

                // Updating Valheim's recipe panel can replace its selected
                // recipe button and make the EventSystem drop the input field.
                // Reclaim selection synchronously so the next key cannot leak
                // through as Use, Guardian Power, movement, or another action.
                if (retainFocus
                    && searchInput != null
                    && searchInput.gameObject.activeInHierarchy
                    && _fixedControls != null
                    && ReferenceEquals(_fixedControls.SearchInput, searchInput))
                {
                    searchInput.Select();
                    searchInput.ActivateInputField();
                    int textLength = searchInput.text != null ? searchInput.text.Length : 0;
                    searchInput.caretPosition = Mathf.Clamp(caretPosition, 0, textLength);
                    searchInput.selectionAnchorPosition = Mathf.Clamp(anchorPosition, 0, textLength);
                    searchInput.selectionFocusPosition = Mathf.Clamp(focusPosition, 0, textLength);
                    _searchHasFocus = true;
                }
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
                Refresh(gui);
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
            TMP_Text text = CreateText(gui, root, ContentPrefix + "Header_" + label, label.ToUpperInvariant(),
                new Vector2(4f, -y), new Vector2(0f, HeaderHeight), 13f, Gold, TextAlignmentOptions.MidlineLeft);
            RectTransform rect = (RectTransform)text.transform;
            rect.anchorMax = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(-8f, HeaderHeight);

            if (!ModConfig.ShowSeparators.Value) return;
            var lineObject = new GameObject(ContentPrefix + "HeaderLine", typeof(RectTransform), typeof(Image));
            lineObject.transform.SetParent(root, false);
            var lineRect = (RectTransform)lineObject.transform;
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0f, 1f);
            lineRect.anchoredPosition = new Vector2(4f, -(y + HeaderHeight - 2f));
            lineRect.sizeDelta = new Vector2(-8f, 1f);
            Image line = lineObject.GetComponent<Image>();
            line.color = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
            line.raycastTarget = false;
        }

        private static void CreateStatus(InventoryGui gui, RectTransform root, string label, float y)
        {
            TMP_Text text = CreateText(gui, root, ContentPrefix + "Status", label,
                new Vector2(4f, -y), new Vector2(0f, HeaderHeight), 12f,
                new Color(0.72f, 0.69f, 0.62f), TextAlignmentOptions.MidlineLeft);
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

            var parts = new List<string>(3);
            if (view.Facts.Health > 0f)
            {
                parts.Add("<color=#FF9C7A>HP " + Mathf.RoundToInt(view.Facts.Health) + "</color>");
            }
            if (view.Facts.Stamina > 0f)
            {
                parts.Add("<color=#FFE071>STAM " + Mathf.RoundToInt(view.Facts.Stamina) + "</color>");
            }
            if (view.Facts.Eitr > 0f)
            {
                parts.Add("<color=#D7A5FF>EITR " + Mathf.RoundToInt(view.Facts.Eitr) + "</color>");
            }
            stats.text = string.Join("   ", parts);
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

        private static RecipeFacts ReadFacts(Recipe recipe, int originalIndex, FoodStationKind foodStationKind)
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
            ResolvedFoodStats resolved = foodStationKind != FoodStationKind.Mead
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

        private static void ClearContentDecorations(RectTransform root)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);
                if (child.name.StartsWith(ContentPrefix, StringComparison.Ordinal))
                {
                    child.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private static void Refresh(InventoryGui gui)
        {
            RefreshMethod.Invoke(gui, new object[] { false });
        }

        private sealed class RecipePairView
        {
            internal RecipePairView(object pair, GameObject element, RecipeFacts facts)
            {
                Pair = pair;
                Element = element;
                Facts = facts;
                Group = new RecipeGroup("Other", 999);
            }

            internal object Pair { get; }
            internal GameObject Element { get; }
            internal RecipeFacts Facts { get; }
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
