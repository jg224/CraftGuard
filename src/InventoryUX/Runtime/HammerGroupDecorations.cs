using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using InventoryUX.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventoryUX.Runtime
{
    internal static class HammerGroupDecorations
    {
        private const string Prefix = "InventoryUX_";
        private const int GridWidth = HammerGridDimensions.ExpandedWidth;
        private const int GridHeight = HammerGridDimensions.ExpandedHeight;
        private const int CategoryColumns = 3;
        private const int ShelfColumns = 15;
        private const int ShelfRows = 7;
        private const float CraftingColumnPitchScale = 0.95f;
        private const float ShelfColumnPitchScale = 0.93f;
        private const float CategoryRailScale = 0.90f;
        private const float CategoryToItemsGap = 14f;
        private const float SubgroupSpacing = 26.4f;
        private const float SubgroupLineInset = 19.8f;
        private const float TileInset = 3f;
        private const float LabelHeight = 14f;
        private const float ViewControlsHeight = 76f;
        private const float ViewButtonGap = 6f;
        private const float HammerSearchHeight = 30f;

        private static readonly FieldInfo PieceIconsField = AccessTools.Field(typeof(Hud), "m_pieceIcons");
        private static readonly FieldInfo PlayerBuildPiecesField = AccessTools.Field(typeof(Player), "m_buildPieces");
        private static readonly MethodInfo UpdateAvailablePiecesMethod =
            AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");
        private static readonly MethodInfo HudUpdatePieceListMethod = AccessTools.Method(
            typeof(Hud),
            "UpdatePieceList",
            new[] { typeof(Player), typeof(Vector2Int), typeof(Piece.PieceCategory), typeof(bool) });
        private static readonly Dictionary<int, NativeBackgroundState> NativeBackgrounds =
            new Dictionary<int, NativeBackgroundState>();
        private static readonly Dictionary<int, UIInputHandler> HoverInputs =
            new Dictionary<int, UIInputHandler>();
        private static readonly Dictionary<int, Piece> FavoritePiecesByInput =
            new Dictionary<int, Piece>();
        private static readonly List<CategoryCardView> CategoryCardPool = new List<CategoryCardView>();
        private static readonly List<Image> DecorationLinePool = new List<Image>();
        private static readonly List<GameObject> IconDecorationPool = new List<GameObject>();
        private static readonly HashSet<int> IconDecorationIds = new HashSet<int>();
        private static int _decorationPoolHudId = int.MinValue;
        private static int _usedCategoryCards;
        private static int _usedDecorationLines;
        private static Image? _categoryHeaderBackgroundForPass;

        private static Type? _iconType;
        private static FieldInfo? _iconGoField;
        private static int[] _visualSlots = Array.Empty<int>();
        private static int _visualWidth = GridWidth;
        private static int _visualHeight = GridHeight;
        private static bool _stateValid;
        private static int _appliedHudId = int.MinValue;
        private static readonly int[] PieceGenerations = new int[(int)Piece.PieceCategory.Max + 1];
        private static int _appliedPiecesGeneration = -1;
        private static int _appliedPieceCount = -1;
        private static bool _appliedShowSeparators;
        private static bool _appliedShowPieceNames;
        private static Piece.PieceCategory _appliedCategory = Piece.PieceCategory.Max;
        private static int _repairLogicalIndex = -1;
        private static Piece? _repairPiece;
        private static GameObject? _persistentRepair;
        private static GameObject? _hiddenRepairRoot;
        private static int _repairHudId = int.MinValue;
        private static GameObject? _persistentViewControls;
        private static Image? _defaultViewBackground;
        private static Image? _modViewBackground;
        private static int _viewControlsHudId = int.MinValue;
        private static int _cleanedGeneratedBackgroundHudId = int.MinValue;
        private static GameObject? _persistentHammerSearch;
        private static TMP_InputField? _hammerSearchInput;
        private static int _hammerSearchHudId = int.MinValue;
        private static string _hammerSearchText = string.Empty;
        private static string _hammerSearchQuery = string.Empty;
        private static bool _hammerSearchHasFocus;
        private static Piece.PieceCategory _hammerSearchCategory = Piece.PieceCategory.Max;
        private static GameObject? _hammerNoResults;
        private static bool _refreshingHammerList;

        internal static bool UseModView { get; private set; } = true;
        internal static bool IsSearchFocused => _hammerSearchHasFocus;

        internal static bool ShouldUseModView(PieceTable table)
        {
            return table != null
                && table.gameObject != null
                && ModConfig.GetToolModView(table.gameObject.name);
        }

        internal static void NotifyPiecesChanged(Piece.PieceCategory category)
        {
            int index = (int)category;
            if (index < 0 || index >= PieceGenerations.Length) return;
            PieceGenerations[index] = unchecked(PieceGenerations[index] + 1);
        }

        private static int GetPiecesGeneration(Piece.PieceCategory category)
        {
            int index = (int)category;
            return index >= 0 && index < PieceGenerations.Length ? PieceGenerations[index] : 0;
        }

        private static readonly GroupPalette[] FallbackPalettes =
        {
            Palette(0.88f, 0.69f, 0.39f, 0.50f, 0.34f, 0.14f),
            Palette(0.84f, 0.52f, 0.40f, 0.46f, 0.25f, 0.20f),
            Palette(0.62f, 0.76f, 0.53f, 0.28f, 0.42f, 0.24f),
            Palette(0.59f, 0.72f, 0.82f, 0.25f, 0.37f, 0.48f),
            Palette(0.71f, 0.63f, 0.81f, 0.35f, 0.29f, 0.44f),
            Palette(0.49f, 0.76f, 0.73f, 0.20f, 0.40f, 0.38f),
            Palette(0.82f, 0.68f, 0.48f, 0.45f, 0.34f, 0.21f),
            Palette(0.77f, 0.60f, 0.68f, 0.42f, 0.27f, 0.35f)
        };

        internal static void Apply(Hud hud, IReadOnlyList<Piece> pieces, Piece.PieceCategory category)
        {
            int hudInstanceId = hud.GetInstanceID();
            RemoveStaleRepairForDifferentHud(hudInstanceId);
            Player? activePlayer = Player.m_localPlayer;
            PieceTable? activeTable = activePlayer == null
                ? null
                : PlayerBuildPiecesField.GetValue(activePlayer) as PieceTable;
            if (activeTable != null)
            {
                UseModView = ShouldUseModView(activeTable);
            }
            EnsurePersistentViewControls(hud, hudInstanceId);
            PrepareHammerSearchCategory(category);
            EnsurePersistentHammerSearch(hud, hudInstanceId);
            UpdateHammerSearchVisibility(category);
            if (_cleanedGeneratedBackgroundHudId != hudInstanceId)
            {
                RemoveGeneratedWarmBackgrounds(hud);
                _cleanedGeneratedBackgroundHudId = hudInstanceId;
            }

            if (!IsEnabled(category))
            {
                Clear(hud, category == Piece.PieceCategory.Crafting);
                return;
            }

            bool showSeparators = ModConfig.ShowSeparators.Value;
            bool showPieceNames = ModConfig.ShowHammerPieceNames.Value;
            if (_stateValid
                && _appliedHudId == hudInstanceId
                && _appliedCategory == category
                && _appliedPiecesGeneration == GetPiecesGeneration(category)
                && _appliedPieceCount == pieces.Count
                && _appliedShowSeparators == showSeparators
                && _appliedShowPieceNames == showPieceNames)
            {
                return;
            }

            IList icons = (IList)PieceIconsField.GetValue(hud);
            int count = Math.Min(pieces.Count, icons.Count);
            Clear(hud);
            SetHammerNoResultsVisible(hud, false);
            _categoryHeaderBackgroundForPass = FindVanillaCategoryHeaderBackground(hud);
            try
            {
                int visiblePieceCount = CountVisiblePieces(pieces, count, category);
                if (visiblePieceCount == 0 && !string.IsNullOrWhiteSpace(_hammerSearchText))
                {
                    var emptySlots = new int[count];
                    for (int i = 0; i < emptySlots.Length; i++) emptySlots[i] = -1;
                    int repairIndex = FindRepairIndex(pieces, count);
                    if (repairIndex >= 0) EnsurePersistentRepair(hud, pieces, icons, repairIndex);
                    ConfigureNativePieceCells(icons, pieces, count, emptySlots, repairIndex);
                    _visualSlots = emptySlots;
                    _visualWidth = GridWidth;
                    _visualHeight = GridHeight;
                    SetHammerNoResultsVisible(hud, true);
                    RememberState(hudInstanceId, category, pieces.Count, showSeparators, showPieceNames);
                    return;
                }

                if (category == Piece.PieceCategory.Crafting && CanUseReferenceCraftingLayout(pieces, count))
                {
                    CraftingLayoutResult layout = BuildCraftingLayout(pieces, count);
                    _visualSlots = layout.Slots;
                    _repairLogicalIndex = layout.RepairIndex;
                    EnsurePersistentRepair(hud, pieces, icons, layout.RepairIndex);
                    ApplyGridPermutation(hud, icons, layout, count);
                    ConfigureNativePieceCells(icons, pieces, count, layout.Slots, layout.RepairIndex);
                    AddReferenceCraftingRows(hud, pieces, icons, count, layout);
                    _visualWidth = GridWidth;
                    _visualHeight = layout.VisibleRows;
                    RememberState(hudInstanceId, category, pieces.Count, showSeparators, showPieceNames);
                    return;
                }

                if (category != Piece.PieceCategory.Crafting && CanUseShelfLayout(pieces, count, category))
                {
                    ShelfLayoutResult layout = BuildShelfLayout(hud, pieces, count, category);
                    _visualSlots = layout.Slots;
                    _visualWidth = ShelfColumns;
                    _visualHeight = layout.VisibleRows;
                    _repairLogicalIndex = layout.RepairIndex;
                    if (layout.RepairIndex >= 0)
                    {
                        EnsurePersistentRepair(hud, pieces, icons, layout.RepairIndex);
                    }
                    ConfigureNativePieceCells(icons, pieces, count, layout.Slots, layout.RepairIndex);
                    ApplyShelfPositions(hud, icons, layout, count);
                    AddShelfRows(hud, pieces, icons, count, layout);
                    RememberState(hudInstanceId, category, pieces.Count, showSeparators, showPieceNames);
                    return;
                }

                string? previous = null;
                for (int i = 0; i < count; i++)
                {
                    string label = HammerOrganizer.GetLabel(pieces[i], category) ?? "Other";
                    GroupPalette palette = GetPalette(category, label);
                    bool groupStart = !string.Equals(previous, label, StringComparison.Ordinal);
                    GameObject iconRoot = GetIconGameObject(icons[i]!);
                    AddTileTint(iconRoot, palette, groupStart);
                    if (ModConfig.IsFavorite(HammerOrganizer.GetPieceKey(pieces[i])))
                    {
                        AddFavoriteMarker(hud, iconRoot);
                    }
                    if (groupStart && !IsStandaloneAction(category, label))
                    {
                        AddLabel(hud, iconRoot, CompactLabel(label), palette);
                    }
                    previous = label;
                }
                RememberState(hudInstanceId, category, pieces.Count, showSeparators, showPieceNames);
            }
            catch
            {
                RemoveDecorations(hud, icons);
                ResetState();
                throw;
            }
        }

        private static void RememberState(
            int hudInstanceId,
            Piece.PieceCategory category,
            int pieceCount,
            bool showSeparators,
            bool showPieceNames)
        {
            _appliedHudId = hudInstanceId;
            _appliedCategory = category;
            _appliedPiecesGeneration = GetPiecesGeneration(category);
            _appliedPieceCount = pieceCount;
            _appliedShowSeparators = showSeparators;
            _appliedShowPieceNames = showPieceNames;
            _stateValid = true;
        }

        private static void ResetState()
        {
            _stateValid = false;
            _appliedHudId = int.MinValue;
            _appliedPiecesGeneration = -1;
            _appliedPieceCount = -1;
            _appliedCategory = Piece.PieceCategory.Max;
            _visualSlots = Array.Empty<int>();
            _visualWidth = GridWidth;
            _visualHeight = GridHeight;
            _repairLogicalIndex = -1;
            _categoryHeaderBackgroundForPass = null;
        }

        internal static bool TryNavigate(PieceTable table, int horizontal, int vertical)
        {
            if (_visualSlots.Length < 2 || table.GetSelectedCategory() != _appliedCategory)
            {
                return false;
            }

            Vector2Int selected = table.GetSelectedIndex();
            int currentIndex = selected.y * GridWidth + selected.x;
            if (currentIndex < 0 || currentIndex >= _visualSlots.Length) return false;

            int currentSlot = _visualSlots[currentIndex];
            if (currentSlot < 0)
            {
                int entry = horizontal < 0 || vertical < 0
                    ? FindLastVisualPiece()
                    : FindFirstVisualPiece();
                if (entry >= 0) SelectLogicalIndex(table, entry);
                return true;
            }

            int currentX = currentSlot % _visualWidth;
            int currentY = currentSlot / _visualWidth;
            int candidate = -1;

            if (horizontal != 0)
            {
                int nearestDistance = int.MaxValue;
                for (int i = 0; i < _visualSlots.Length; i++)
                {
                    int slot = _visualSlots[i];
                    if (slot < 0 || slot / _visualWidth != currentY) continue;
                    int delta = slot % _visualWidth - currentX;
                    if ((horizontal < 0 && delta >= 0) || (horizontal > 0 && delta <= 0)) continue;
                    int distance = Math.Abs(delta);
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        candidate = i;
                    }
                }

                if (candidate < 0 && horizontal < 0 && _repairLogicalIndex >= 0)
                {
                    candidate = _repairLogicalIndex;
                }
                else if (candidate < 0)
                {
                    int wrapX = horizontal < 0 ? int.MinValue : int.MaxValue;
                    for (int i = 0; i < _visualSlots.Length; i++)
                    {
                        int slot = _visualSlots[i];
                        if (slot < 0 || slot / _visualWidth != currentY) continue;
                        int x = slot % _visualWidth;
                        if ((horizontal < 0 && x > wrapX) || (horizontal > 0 && x < wrapX))
                        {
                            wrapX = x;
                            candidate = i;
                        }
                    }
                }
            }
            else if (vertical != 0)
            {
                for (int step = 1; step < _visualHeight && candidate < 0; step++)
                {
                    int row = (currentY + vertical * step + _visualHeight * 2) % _visualHeight;
                    int nearestDistance = int.MaxValue;
                    for (int i = 0; i < _visualSlots.Length; i++)
                    {
                        int slot = _visualSlots[i];
                        if (slot < 0 || slot / _visualWidth != row) continue;
                        int distance = Math.Abs(slot % _visualWidth - currentX);
                        if (distance < nearestDistance)
                        {
                            nearestDistance = distance;
                            candidate = i;
                        }
                    }
                }
            }

            if (candidate >= 0 && candidate != currentIndex)
            {
                SelectLogicalIndex(table, candidate);
            }
            return true;
        }

        private static void SelectLogicalIndex(PieceTable table, int logicalIndex)
            => table.SetSelected(new Vector2Int(logicalIndex % GridWidth, logicalIndex / GridWidth));

        private static int FindFirstVisualPiece()
        {
            int bestIndex = -1;
            int bestSlot = int.MaxValue;
            for (int i = 0; i < _visualSlots.Length; i++)
            {
                int slot = _visualSlots[i];
                if (slot >= 0 && slot < bestSlot)
                {
                    bestSlot = slot;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static int FindLastVisualPiece()
        {
            int bestIndex = -1;
            int bestSlot = int.MinValue;
            for (int i = 0; i < _visualSlots.Length; i++)
            {
                int slot = _visualSlots[i];
                if (slot > bestSlot)
                {
                    bestSlot = slot;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static CraftingLayoutResult BuildCraftingLayout(IReadOnlyList<Piece> pieces, int count)
        {
            var grouped = new List<CraftingEntry>[GridHeight];
            for (int i = 0; i < grouped.Length; i++) grouped[i] = new List<CraftingEntry>();
            var favorites = new List<CraftingEntry>();

            int repairIndex = -1;
            for (int pieceIndex = 0; pieceIndex < count; pieceIndex++)
            {
                Piece piece = pieces[pieceIndex];
                if (CraftingLayoutMetadata.IsRepair(piece))
                {
                    repairIndex = pieceIndex;
                    continue;
                }
                if (!HammerOrganizer.MatchesPreparedSearch(piece, Piece.PieceCategory.Crafting, _hammerSearchQuery)) continue;

                CraftingPieceLayout metadata = CraftingLayoutMetadata.Resolve(piece);
                var entry = new CraftingEntry(pieceIndex, metadata, Localize(piece.m_name));
                if (ModConfig.IsFavorite(HammerOrganizer.GetPieceKey(piece))) favorites.Add(entry);
                else grouped[(int)metadata.Section].Add(entry);
            }

            favorites.Sort(CompareCraftingEntries);
            for (int i = 0; i < grouped.Length; i++)
            {
                grouped[i].Sort(CompareCraftingEntries);
            }

            int layoutRows = GridHeight + (favorites.Count > 0 ? 1 : 0);
            var slots = new int[count];
            for (int i = 0; i < slots.Length; i++) slots[i] = -1;
            var xOffsets = new float[count];
            var used = new bool[GridWidth * layoutRows];
            var rowLabels = new string[layoutRows];
            var representatives = new int[layoutRows];
            for (int i = 0; i < representatives.Length; i++) representatives[i] = -1;
            var subgroupBreaks = new List<SubgroupDivider>();
            var overflow = new List<int>();

            int visibleRow = 0;
            if (favorites.Count > 0)
            {
                rowLabels[visibleRow] = "FAVORITES";
                representatives[visibleRow] = favorites[0].PieceIndex;
                int column = CategoryColumns;
                for (int itemIndex = 0; itemIndex < favorites.Count; itemIndex++)
                {
                    CraftingEntry entry = favorites[itemIndex];
                    if (column >= GridWidth) overflow.Add(entry.PieceIndex);
                    else
                    {
                        int slot = visibleRow * GridWidth + column++;
                        slots[entry.PieceIndex] = slot;
                        used[slot] = true;
                    }
                }
                visibleRow++;
            }

            for (int sectionIndex = 0; sectionIndex < grouped.Length && visibleRow < layoutRows; sectionIndex++)
            {
                List<CraftingEntry> entries = grouped[sectionIndex];
                if (entries.Count == 0) continue;

                rowLabels[visibleRow] = CraftingLayoutMetadata.Label((CraftingSection)sectionIndex);
                representatives[visibleRow] = FindCategoryRepresentative(
                    entries,
                    (CraftingSection)sectionIndex);
                int column = CategoryColumns;
                int previousSubgroup = entries[0].Metadata.Subgroup;
                int subgroupGapCount = 0;
                for (int itemIndex = 0; itemIndex < entries.Count; itemIndex++)
                {
                    CraftingEntry entry = entries[itemIndex];
                    bool subgroupChanged = itemIndex > 0 && entry.Metadata.Subgroup != previousSubgroup;
                    if (subgroupChanged)
                    {
                        subgroupGapCount++;
                        subgroupBreaks.Add(new SubgroupDivider(
                            visibleRow,
                            column,
                            subgroupGapCount));
                    }

                    if (column >= GridWidth)
                    {
                        overflow.Add(entry.PieceIndex);
                    }
                    else
                    {
                        int slot = visibleRow * GridWidth + column;
                        slots[entry.PieceIndex] = slot;
                        xOffsets[entry.PieceIndex] = subgroupGapCount * SubgroupSpacing;
                        used[slot] = true;
                        column++;
                    }
                    previousSubgroup = entry.Metadata.Subgroup;
                }
                visibleRow++;
            }

            for (int i = 0; i < overflow.Count; i++)
            {
                int slot = FirstFreePieceSlot(used, layoutRows);
                if (slot < 0) break;
                slots[overflow[i]] = slot;
                used[slot] = true;
            }

            return new CraftingLayoutResult(
                slots,
                xOffsets,
                repairIndex,
                rowLabels,
                representatives,
                subgroupBreaks,
                visibleRow);
        }

        private static int CompareCraftingEntries(CraftingEntry left, CraftingEntry right)
        {
            int comparison = left.Metadata.SortOrder.CompareTo(right.Metadata.SortOrder);
            if (comparison != 0) return comparison;
            comparison = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            return comparison != 0 ? comparison : left.PieceIndex.CompareTo(right.PieceIndex);
        }

        private static bool CanUseShelfLayout(
            IReadOnlyList<Piece> pieces,
            int count,
            Piece.PieceCategory category)
        {
            int ordinary = 0;
            for (int i = 0; i < count; i++)
            {
                if (!CraftingLayoutMetadata.IsRepair(pieces[i])
                    && HammerOrganizer.MatchesPreparedSearch(pieces[i], category, _hammerSearchQuery)) ordinary++;
            }
            return ordinary > 0 && ordinary <= ShelfColumns * ShelfRows;
        }

        private static ShelfLayoutResult BuildShelfLayout(
            Hud hud,
            IReadOnlyList<Piece> pieces,
            int count,
            Piece.PieceCategory category)
        {
            var sourceIndices = new List<int>(count);
            int repairIndex = -1;
            for (int i = 0; i < count; i++)
            {
                if (CraftingLayoutMetadata.IsRepair(pieces[i]))
                {
                    repairIndex = i;
                    continue;
                }
            }

            for (int favoritePass = 0; favoritePass < 2; favoritePass++)
            {
                bool favorites = favoritePass == 0;
                for (int i = 0; i < count; i++)
                {
                    Piece piece = pieces[i];
                    if (CraftingLayoutMetadata.IsRepair(piece)
                        || !HammerOrganizer.MatchesPreparedSearch(piece, category, _hammerSearchQuery)) continue;
                    bool favorite = ModConfig.IsFavorite(HammerOrganizer.GetPieceKey(piece));
                    if (favorite == favorites) sourceIndices.Add(i);
                }
            }

            var labels = new string[sourceIndices.Count];
            var subgroups = new int[sourceIndices.Count];
            int idealGroupRows = 0;
            int groupLength = 0;
            string? previousLabel = null;
            for (int i = 0; i < sourceIndices.Count; i++)
            {
                Piece piece = pieces[sourceIndices[i]];
                PieceGroup group = HammerOrganizer.Classify(piece, category);
                string label = ModConfig.IsFavorite(HammerOrganizer.GetPieceKey(piece))
                    ? "Favorites"
                    : HammerOrganizer.GetLabel(piece, category) ?? group.Label;
                labels[i] = label;
                subgroups[i] = group.Suborder;
                if (i == 0 || string.Equals(previousLabel, label, StringComparison.Ordinal))
                {
                    groupLength++;
                }
                else
                {
                    idealGroupRows += Mathf.CeilToInt(groupLength / (float)ShelfColumns);
                    groupLength = 1;
                }
                previousLabel = label;
            }
            if (groupLength > 0)
            {
                idealGroupRows += Mathf.CeilToInt(groupLength / (float)ShelfColumns);
            }

            int minimumRows = Mathf.CeilToInt(sourceIndices.Count / (float)ShelfColumns);
            int visibleRows = Mathf.Clamp(Mathf.Max(minimumRows, idealGroupRows), 1, ShelfRows);
            List<int>? materialRowSizes = category == Piece.PieceCategory.BuildingWorkbench
                ? BuildMaterialRowSizes(labels)
                : null;
            if (materialRowSizes != null) visibleRows = materialRowSizes.Count;
            float spacing = hud.m_pieceIconSpacing;
            float rowPitch = ShelfRowPitch(hud, visibleRows);
            float itemStart = CategoryRailWidth(spacing) + CategoryToItemsGap;
            float maximumAnchorX = (GridWidth - 1) * spacing;

            var slots = new int[count];
            for (int i = 0; i < slots.Length; i++) slots[i] = -1;
            var xPositions = new float[count];
            var rowLabels = new string[visibleRows];
            var representatives = new int[visibleRows];
            var dividers = new List<ShelfDivider>();
            int cursor = 0;
            for (int row = 0; row < visibleRows; row++)
            {
                int take = materialRowSizes != null
                    ? materialRowSizes[row]
                    : ShelfRowPlanner.ChooseGroupRowSize(
                        labels,
                        cursor,
                        ShelfColumns,
                        visibleRows - row);
                rowLabels[row] = labels[cursor];
                representatives[row] = sourceIndices[cursor];

                int breakCount = 0;
                for (int column = 1; column < take; column++)
                {
                    if (IsShelfBreak(labels, subgroups, cursor + column, category))
                    {
                        breakCount++;
                    }
                }

                float pitch = spacing * ShelfColumnPitchScale;
                if (take > 1)
                {
                    float fittedPitch = (maximumAnchorX - itemStart - breakCount * SubgroupSpacing) / (take - 1);
                    pitch = Mathf.Min(pitch, fittedPitch);
                }

                int gaps = 0;
                for (int column = 0; column < take; column++)
                {
                    int sourceIndex = cursor + column;
                    int pieceIndex = sourceIndices[sourceIndex];
                    if (column > 0
                        && IsShelfBreak(labels, subgroups, sourceIndex, category))
                    {
                        gaps++;
                    }

                    float x = itemStart + column * pitch + gaps * SubgroupSpacing;
                    if (column > 0
                        && IsShelfBreak(labels, subgroups, sourceIndex, category))
                    {
                        dividers.Add(new ShelfDivider(row, x - SubgroupLineInset));
                    }
                    slots[pieceIndex] = row * ShelfColumns + column;
                    xPositions[pieceIndex] = x;
                }
                cursor += take;
            }

            List<ShelfCard> cards = BuildShelfCards(
                pieces,
                sourceIndices,
                labels,
                rowLabels,
                representatives,
                category);

            return new ShelfLayoutResult(
                slots,
                xPositions,
                cards,
                dividers,
                visibleRows,
                rowPitch,
                repairIndex);
        }

        private static bool IsShelfBreak(
            string[] labels,
            int[] subgroups,
            int index,
            Piece.PieceCategory category)
        {
            if (index <= 0 || index >= labels.Length) return false;
            if (!string.Equals(labels[index - 1], labels[index], StringComparison.Ordinal)) return true;
            if (category == Piece.PieceCategory.BuildingWorkbench
                && string.Equals(Normalize(labels[index]), "corewood", StringComparison.Ordinal))
            {
                return false;
            }
            return category == Piece.PieceCategory.BuildingWorkbench
                && subgroups[index - 1] != subgroups[index];
        }

        private static List<int> BuildMaterialRowSizes(string[] labels)
        {
            var sizes = new List<int>();
            int cursor = 0;
            while (cursor < labels.Length)
            {
                int groupEnd = cursor + 1;
                while (groupEnd < labels.Length
                    && string.Equals(labels[cursor], labels[groupEnd], StringComparison.Ordinal))
                {
                    groupEnd++;
                }

                int remaining = groupEnd - cursor;
                int rows = Mathf.CeilToInt(remaining / (float)ShelfColumns);
                for (int row = 0; row < rows; row++)
                {
                    int take = Mathf.CeilToInt(remaining / (float)(rows - row));
                    sizes.Add(take);
                    remaining -= take;
                }
                cursor = groupEnd;
            }
            return sizes;
        }

        private static List<ShelfCard> BuildShelfCards(
            IReadOnlyList<Piece> pieces,
            List<int> sourceIndices,
            string[] labels,
            string[] rowLabels,
            int[] representatives,
            Piece.PieceCategory category)
        {
            var cards = new List<ShelfCard>();
            for (int row = 0; row < rowLabels.Length; row++)
            {
                string label = rowLabels[row];
                if (cards.Count > 0
                    && string.Equals(cards[cards.Count - 1].Label, label, StringComparison.Ordinal))
                {
                    ShelfCard previous = cards[cards.Count - 1];
                    cards[cards.Count - 1] = new ShelfCard(
                        previous.StartRow,
                        previous.RowSpan + 1,
                        previous.Label,
                        previous.Representative);
                    continue;
                }

                int representative = representatives[row];
                if (category == Piece.PieceCategory.BuildingWorkbench)
                {
                    representative = FindBuildingBeamRepresentative(pieces, sourceIndices, labels, label, representative);
                }
                else if (category == Piece.PieceCategory.Misc
                    && string.Equals(Normalize(label), "utility", StringComparison.Ordinal))
                {
                    representative = FindCartographyRepresentative(pieces, sourceIndices, labels, label, representative);
                }
                cards.Add(new ShelfCard(row, 1, label, representative));
            }
            return cards;
        }

        private static int FindBuildingBeamRepresentative(
            IReadOnlyList<Piece> pieces,
            List<int> sourceIndices,
            string[] labels,
            string label,
            int fallback)
        {
            string normalizedLabel = Normalize(label);
            for (int i = 0; i < sourceIndices.Count; i++)
            {
                if (!string.Equals(labels[i], label, StringComparison.Ordinal)) continue;
                Piece piece = pieces[sourceIndices[i]];
                string id = Normalize(piece.gameObject.name + " " + piece.m_name);
                if (!id.Contains("beam")) continue;
                if (normalizedLabel == "darkwood" && !id.Contains("darkwood")) continue;
                if (normalizedLabel == "ashwood"
                    && !id.Contains("grausten")
                    && !id.Contains("ashwood")
                    && !id.Contains("blackwood")) continue;
                if (normalizedLabel == "corewood" && !id.Contains("corewood") && !id.Contains("log")) continue;
                if (normalizedLabel == "wood"
                    && (id.Contains("darkwood")
                        || id.Contains("grausten")
                        || id.Contains("ashwood")
                        || id.Contains("corewood")
                        || id.Contains("log"))) continue;
                return sourceIndices[i];
            }
            return fallback;
        }

        private static int FindCartographyRepresentative(
            IReadOnlyList<Piece> pieces,
            List<int> sourceIndices,
            string[] labels,
            string label,
            int fallback)
        {
            for (int i = 0; i < sourceIndices.Count; i++)
            {
                if (!string.Equals(labels[i], label, StringComparison.Ordinal)) continue;
                Piece piece = pieces[sourceIndices[i]];
                string id = Normalize(piece.gameObject.name + " " + piece.m_name);
                if (id.Contains("cartography") || id.Contains("maptable"))
                {
                    return sourceIndices[i];
                }
            }
            return fallback;
        }

        private static float CategoryRailWidth(float spacing)
            => (CategoryColumns * spacing - 28f) * CategoryRailScale;

        private static float CraftingContentShift(float spacing)
            => CategoryRailWidth(spacing) + CategoryToItemsGap
                - CategoryColumns * spacing * CraftingColumnPitchScale;

        private static float ShelfRowPitch(Hud hud, int visibleRows)
        {
            if (visibleRows <= GridHeight)
            {
                return hud.m_pieceIconSpacing * HammerGridDimensions.ReferenceCraftingRowScale;
            }
            return hud.m_pieceIconSpacing * ShelfRows / visibleRows;
        }

        private static int FindCategoryRepresentative(
            List<CraftingEntry> entries,
            CraftingSection section)
        {
            int preferredOrder = 100;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Metadata.SortOrder == preferredOrder)
                {
                    return entries[i].PieceIndex;
                }
            }
            return entries[0].PieceIndex;
        }

        private static int CountVisiblePieces(
            IReadOnlyList<Piece> pieces,
            int count,
            Piece.PieceCategory category)
        {
            int visible = 0;
            for (int i = 0; i < count; i++)
            {
                if (CraftingLayoutMetadata.IsRepair(pieces[i])) continue;
                if (HammerOrganizer.MatchesPreparedSearch(pieces[i], category, _hammerSearchQuery)) visible++;
            }
            return visible;
        }

        private static int FindRepairIndex(IReadOnlyList<Piece> pieces, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (CraftingLayoutMetadata.IsRepair(pieces[i])) return i;
            }
            return -1;
        }

        private static bool CanUseReferenceCraftingLayout(IReadOnlyList<Piece> pieces, int count)
        {
            int ordinaryPieces = 0;
            bool hasFavorite = false;
            for (int i = 0; i < count; i++)
            {
                Piece piece = pieces[i];
                if (CraftingLayoutMetadata.IsRepair(piece)
                    || !HammerOrganizer.MatchesPreparedSearch(piece, Piece.PieceCategory.Crafting, _hammerSearchQuery)) continue;
                ordinaryPieces++;
                if (ModConfig.IsFavorite(HammerOrganizer.GetPieceKey(piece))) hasFavorite = true;
            }
            int rows = GridHeight + (hasFavorite ? 1 : 0);
            return ordinaryPieces > 0 && ordinaryPieces <= (GridWidth - CategoryColumns) * rows;
        }

        private static int FirstFreePieceSlot(bool[] used, int rows)
        {
            for (int row = 0; row < rows; row++)
            {
                for (int column = CategoryColumns; column < GridWidth; column++)
                {
                    int slot = row * GridWidth + column;
                    if (!used[slot]) return slot;
                }
            }
            return -1;
        }

        private static void AddReferenceCraftingRows(
            Hud hud,
            IReadOnlyList<Piece> pieces,
            IList icons,
            int count,
            CraftingLayoutResult layout)
        {
            RectTransform templateRect = (RectTransform)GetIconGameObject(icons[0]!).transform;

            for (int pieceIndex = 0; pieceIndex < count; pieceIndex++)
            {
                if (pieceIndex == layout.RepairIndex || layout.Slots[pieceIndex] < 0) continue;
                GameObject iconRoot = GetIconGameObject(icons[pieceIndex]!);
                if (ModConfig.IsFavorite(HammerOrganizer.GetPieceKey(pieces[pieceIndex])))
                {
                    AddFavoriteMarker(hud, iconRoot);
                }
                if (ModConfig.ShowHammerPieceNames.Value)
                {
                    AddReferenceTile(hud, iconRoot, pieces[pieceIndex]);
                }
            }

            for (int row = 0; row < layout.VisibleRows; row++)
            {
                if (row < layout.VisibleRows - 1 && ModConfig.ShowSeparators.Value)
                {
                    AddRowSeparator(hud, templateRect, row, CraftingRowPitch(hud, layout.VisibleRows));
                }

                int representative = layout.Representatives[row];
                if (!string.IsNullOrEmpty(layout.RowLabels[row]) && representative >= 0)
                {
                    AddCategoryCard(
                        hud,
                        templateRect,
                        row,
                        CraftingRowPitch(hud, layout.VisibleRows),
                        layout.RowLabels[row],
                        pieces[representative]);
                }
            }

            if (ModConfig.ShowSeparators.Value)
            {
                for (int i = 0; i < layout.SubgroupBreaks.Count; i++)
                {
                    AddSubgroupSeparator(
                        hud,
                        templateRect,
                        layout.SubgroupBreaks[i],
                        CraftingRowPitch(hud, layout.VisibleRows));
                }
            }
        }

        private static void AddShelfRows(
            Hud hud,
            IReadOnlyList<Piece> pieces,
            IList icons,
            int count,
            ShelfLayoutResult layout)
        {
            RectTransform templateRect = (RectTransform)GetIconGameObject(icons[0]!).transform;
            for (int pieceIndex = 0; pieceIndex < count; pieceIndex++)
            {
                if (layout.Slots[pieceIndex] < 0) continue;
                GameObject iconRoot = GetIconGameObject(icons[pieceIndex]!);
                if (ModConfig.IsFavorite(HammerOrganizer.GetPieceKey(pieces[pieceIndex])))
                {
                    AddFavoriteMarker(hud, iconRoot);
                }
                if (ModConfig.ShowHammerPieceNames.Value)
                {
                    AddReferenceTile(hud, iconRoot, pieces[pieceIndex]);
                }
            }

            for (int row = 0; row < layout.VisibleRows; row++)
            {
                if (row < layout.VisibleRows - 1 && ModConfig.ShowSeparators.Value)
                {
                    bool cardBoundary = false;
                    for (int cardIndex = 0; cardIndex < layout.Cards.Count; cardIndex++)
                    {
                        if (layout.Cards[cardIndex].StartRow == row + 1)
                        {
                            cardBoundary = true;
                            break;
                        }
                    }

                    // Tall Building material cards intentionally cover multiple
                    // packed icon rows. Only close the shelf when the major
                    // material changes; internal lines make those rows look like
                    // unrelated groups and visually cut through the pieces.
                    if (cardBoundary)
                    {
                        AddRowSeparator(hud, templateRect, row, layout.RowPitch, 0f);
                    }
                }
            }

            for (int cardIndex = 0; cardIndex < layout.Cards.Count; cardIndex++)
            {
                ShelfCard card = layout.Cards[cardIndex];
                AddCategoryCard(
                    hud,
                    templateRect,
                    card.StartRow,
                    layout.RowPitch,
                    card.Label,
                    pieces[card.Representative],
                    card.RowSpan);
            }

            if (ModConfig.ShowSeparators.Value)
            {
                for (int i = 0; i < layout.Dividers.Count; i++)
                {
                    AddShelfDivider(hud, templateRect, layout.Dividers[i], layout.RowPitch);
                }
            }
        }

        private static void AddReferenceTile(Hud hud, GameObject iconRoot, Piece piece)
        {
            Transform? existing = iconRoot.transform.Find(Prefix + "CraftingPieceName");
            GameObject nameObject;
            if (existing != null)
            {
                nameObject = existing.gameObject;
                nameObject.SetActive(true);
            }
            else
            {
                nameObject = new GameObject(
                    Prefix + "CraftingPieceName",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));
                nameObject.transform.SetParent(iconRoot.transform, false);
            }
            TrackIconDecoration(nameObject);
            nameObject.transform.SetAsLastSibling();
            var nameRect = (RectTransform)nameObject.transform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.anchoredPosition = new Vector2(0f, TileInset);
            nameRect.sizeDelta = new Vector2(-TileInset * 2f - 2f, 27f);

            TextMeshProUGUI name = nameObject.GetComponent<TextMeshProUGUI>();
            name.text = Localize(piece.m_name);
            name.font = hud.m_pieceDescription != null ? hud.m_pieceDescription.font : null;
            name.fontSize = 10.6f;
            name.fontSizeMin = 8.6f;
            name.fontSizeMax = 10.6f;
            name.enableAutoSizing = true;
            name.fontStyle = FontStyles.Bold;
            name.color = new Color(0.96f, 0.88f, 0.72f, 1f);
            name.alignment = TextAlignmentOptions.Bottom;
            name.textWrappingMode = TextWrappingModes.Normal;
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.maxVisibleLines = 2;
            name.outlineWidth = 0.22f;
            name.outlineColor = new Color32(0, 0, 0, 255);
            name.raycastTarget = false;
        }

        private static void AddFavoriteMarker(Hud hud, GameObject iconRoot)
        {
            Transform? existing = iconRoot.transform.Find(Prefix + "FavoriteMarker");
            GameObject markerObject;
            if (existing != null)
            {
                markerObject = existing.gameObject;
                markerObject.SetActive(true);
            }
            else
            {
                markerObject = new GameObject(
                    Prefix + "FavoriteMarker",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));
                markerObject.transform.SetParent(iconRoot.transform, false);
            }

            TrackIconDecoration(markerObject);
            markerObject.transform.SetAsLastSibling();
            var rect = (RectTransform)markerObject.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-2f, -1f);
            rect.sizeDelta = new Vector2(22f, 22f);

            TextMeshProUGUI marker = markerObject.GetComponent<TextMeshProUGUI>();
            marker.text = "★";
            marker.font = hud.m_pieceDescription != null ? hud.m_pieceDescription.font : null;
            marker.fontSize = 17f;
            marker.fontStyle = FontStyles.Bold;
            marker.color = new Color(0.93f, 0.63f, 0.25f, 1f);
            marker.alignment = TextAlignmentOptions.TopRight;
            marker.outlineWidth = 0.22f;
            marker.outlineColor = Color.black;
            marker.raycastTarget = false;
        }

        private static void AddCategoryCard(
            Hud hud,
            RectTransform templateRect,
            int row,
            CraftingSection section,
            Piece representative)
            => AddCategoryCard(
                hud,
                templateRect,
                row,
                HammerGridDimensions.CraftingRowPitch(hud),
                CraftingLayoutMetadata.Label(section),
                representative);

        private static void AddCategoryCard(
            Hud hud,
            RectTransform templateRect,
            int row,
            float rowPitch,
            string text,
            Piece representative,
            int rowSpan = 1)
        {
            float spacing = hud.m_pieceIconSpacing;
            CategoryCardView cardView = AcquireCategoryCard(hud);
            GameObject cardObject = cardView.Root;
            cardObject.name = Prefix + "CraftingCategory_" + row;
            cardObject.transform.SetAsLastSibling();
            var cardRect = cardView.Rect;
            cardRect.anchorMin = templateRect.anchorMin;
            cardRect.anchorMax = templateRect.anchorMax;
            cardRect.pivot = templateRect.pivot;
            cardRect.anchoredPosition = new Vector2(0f, -row * rowPitch);
            cardRect.sizeDelta = new Vector2(CategoryRailWidth(spacing), rowPitch * rowSpan - 7f);

            Image card = cardView.Background;
            Image? nativeBackground = _categoryHeaderBackgroundForPass;
            if (nativeBackground != null)
            {
                card.sprite = nativeBackground.sprite;
                card.overrideSprite = nativeBackground.overrideSprite;
                card.material = nativeBackground.material;
                card.type = nativeBackground.type;
                card.preserveAspect = nativeBackground.preserveAspect;
                card.fillCenter = nativeBackground.fillCenter;
                card.pixelsPerUnitMultiplier = nativeBackground.pixelsPerUnitMultiplier;
                card.color = nativeBackground.canvasRenderer.GetColor();
            }
            else
            {
                card.sprite = null;
                card.overrideSprite = null;
                card.material = null;
                card.type = Image.Type.Simple;
                card.color = new Color(0.20f, 0.20f, 0.20f, 0.70f);
            }
            card.raycastTarget = false;

            var iconRect = cardView.IconRect;
            iconRect.anchorMin = new Vector2(0f, 0.5f);
            iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.pivot = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(8f, 0f);
            iconRect.sizeDelta = new Vector2(56f, 56f);
            Image icon = cardView.Icon;
            icon.sprite = representative.m_icon;
            icon.preserveAspect = true;
            icon.color = Color.white;
            icon.raycastTarget = false;

            var labelRect = cardView.LabelRect;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(66f, 6f);
            labelRect.offsetMax = new Vector2(-7f, -6f);

            TextMeshProUGUI label = cardView.Label;
            label.text = text.ToUpperInvariant();
            label.font = hud.m_pieceDescription != null ? hud.m_pieceDescription.font : null;
            label.fontSize = 12.8f;
            label.fontSizeMin = 9.5f;
            label.fontSizeMax = 12.8f;
            label.enableAutoSizing = true;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.84f, 0.69f, 0.43f, 1f);
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.outlineWidth = 0.12f;
            label.outlineColor = new Color32(18, 12, 8, 245);
            label.raycastTarget = false;
        }

        private static CategoryCardView AcquireCategoryCard(Hud hud)
        {
            EnsureDecorationPoolOwner(hud);
            CategoryCardView? card = _usedCategoryCards < CategoryCardPool.Count
                ? CategoryCardPool[_usedCategoryCards]
                : null;
            if (card == null || card.Root == null)
            {
                var cardObject = new GameObject(
                    Prefix + "CraftingCategoryPool",
                    typeof(RectTransform),
                    typeof(Image));
                cardObject.transform.SetParent(hud.m_pieceListRoot, false);
                var cardRect = (RectTransform)cardObject.transform;
                Image background = cardObject.GetComponent<Image>();
                AddRectBorder(cardRect, new Color(0.68f, 0.50f, 0.25f, 0.68f));

                var iconObject = new GameObject(
                    Prefix + "CraftingCategoryIcon",
                    typeof(RectTransform),
                    typeof(Image));
                iconObject.transform.SetParent(cardRect, false);
                var iconRect = (RectTransform)iconObject.transform;
                Image icon = iconObject.GetComponent<Image>();

                var labelObject = new GameObject(
                    Prefix + "CraftingCategoryLabel",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(cardRect, false);
                var labelRect = (RectTransform)labelObject.transform;
                TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();

                card = new CategoryCardView(
                    cardObject,
                    cardRect,
                    background,
                    iconRect,
                    icon,
                    labelRect,
                    label);
                if (_usedCategoryCards < CategoryCardPool.Count)
                    CategoryCardPool[_usedCategoryCards] = card;
                else
                    CategoryCardPool.Add(card);
            }

            card.Root.SetActive(true);
            _usedCategoryCards++;
            return card;
        }

        private static Image? FindVanillaCategoryHeaderBackground(Hud hud)
        {
            Image? best = null;
            float darkestLuminance = float.MaxValue;
            GameObject[] tabs = hud.m_pieceCategoryTabs;
            for (int i = 0; i < tabs.Length; i++)
            {
                GameObject tab = tabs[i];
                if (tab == null || !tab.activeInHierarchy) continue;

                Image? image = tab.GetComponent<Image>()
                    ?? tab.GetComponentInChildren<Image>(true);
                if (image == null) continue;

                // Selectable transitions tint tab headers through the
                // CanvasRenderer; Image.color remains white underneath.
                Color color = image.canvasRenderer.GetColor();
                if (color.a <= 0.05f) continue;
                float luminance = color.r * 0.2126f
                    + color.g * 0.7152f
                    + color.b * 0.0722f;
                if (luminance < darkestLuminance)
                {
                    best = image;
                    darkestLuminance = luminance;
                }
            }

            return best;
        }

        private static void AddRowSeparator(Hud hud, RectTransform templateRect, int row)
            => AddRowSeparator(
                hud,
                templateRect,
                row,
                HammerGridDimensions.CraftingRowPitch(hud));

        private static void AddRowSeparator(
            Hud hud,
            RectTransform templateRect,
            int row,
            float rowPitch)
            => AddRowSeparator(hud, templateRect, row, rowPitch, 0f);

        private static void AddRowSeparator(
            Hud hud,
            RectTransform templateRect,
            int row,
            float rowPitch,
            float startX)
        {
            float spacing = hud.m_pieceIconSpacing;
            Image line = AcquireDecorationLine(hud, Prefix + "CraftingRowSeparator_" + row);
            line.transform.SetAsLastSibling();
            var rect = (RectTransform)line.transform;
            rect.anchorMin = templateRect.anchorMin;
            rect.anchorMax = templateRect.anchorMax;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(startX, -(row + 1f) * rowPitch + 3.5f);
            rect.sizeDelta = new Vector2(GridWidth * spacing - 9f - startX, 1f);
            line.color = new Color(0.57f, 0.40f, 0.19f, 0.34f);
            line.raycastTarget = false;
        }

        private static void AddSubgroupSeparator(
            Hud hud,
            RectTransform templateRect,
            SubgroupDivider divider,
            float rowPitch)
        {
            float nativeSpacing = hud.m_pieceIconSpacing;
            float spacing = nativeSpacing * CraftingColumnPitchScale;
            float nextItemX = divider.Column * spacing
                + CraftingContentShift(nativeSpacing)
                + divider.GapCount * SubgroupSpacing;
            Image line = AcquireDecorationLine(
                hud,
                Prefix + "CraftingSubgroupSeparator_" + divider.Row + "_" + divider.Column);
            line.transform.SetAsLastSibling();
            var rect = (RectTransform)line.transform;
            rect.anchorMin = templateRect.anchorMin;
            rect.anchorMax = templateRect.anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(
                nextItemX - SubgroupLineInset,
                -divider.Row * rowPitch - rowPitch * 0.5f);
            rect.sizeDelta = new Vector2(1f, rowPitch - 20f);
            line.color = new Color(0.62f, 0.47f, 0.25f, 0.42f);
            line.raycastTarget = false;
        }

        private static void AddShelfDivider(
            Hud hud,
            RectTransform templateRect,
            ShelfDivider divider,
            float rowPitch)
        {
            Image line = AcquireDecorationLine(
                hud,
                Prefix + "CraftingShelfDivider_" + divider.Row + "_" + divider.X);
            line.transform.SetAsLastSibling();
            var rect = (RectTransform)line.transform;
            rect.anchorMin = templateRect.anchorMin;
            rect.anchorMax = templateRect.anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(
                divider.X,
                -divider.Row * rowPitch - rowPitch * 0.5f);
            rect.sizeDelta = new Vector2(1f, rowPitch - 20f);
            line.color = new Color(0.62f, 0.47f, 0.25f, 0.42f);
            line.raycastTarget = false;
        }

        private static Image AcquireDecorationLine(Hud hud, string name)
        {
            EnsureDecorationPoolOwner(hud);
            Image? line = _usedDecorationLines < DecorationLinePool.Count
                ? DecorationLinePool[_usedDecorationLines]
                : null;
            if (line == null)
            {
                var lineObject = new GameObject(name, typeof(RectTransform), typeof(Image));
                lineObject.transform.SetParent(hud.m_pieceListRoot, false);
                line = lineObject.GetComponent<Image>();
                if (_usedDecorationLines < DecorationLinePool.Count)
                    DecorationLinePool[_usedDecorationLines] = line;
                else
                    DecorationLinePool.Add(line);
            }

            line.gameObject.name = name;
            line.gameObject.SetActive(true);
            _usedDecorationLines++;
            return line;
        }

        private static void EnsureDecorationPoolOwner(Hud hud)
        {
            int hudId = hud.GetInstanceID();
            if (_decorationPoolHudId == hudId) return;
            DestroyDecorationPool();
            _decorationPoolHudId = hudId;
        }

        private static void DeactivateDecorationPool()
        {
            for (int i = 0; i < CategoryCardPool.Count; i++)
            {
                CategoryCardView card = CategoryCardPool[i];
                if (card != null && card.Root != null) card.Root.SetActive(false);
            }
            for (int i = 0; i < DecorationLinePool.Count; i++)
            {
                if (DecorationLinePool[i] != null) DecorationLinePool[i].gameObject.SetActive(false);
            }
            _usedCategoryCards = 0;
            _usedDecorationLines = 0;
        }

        private static void DestroyDecorationPool()
        {
            for (int i = 0; i < CategoryCardPool.Count; i++)
            {
                CategoryCardView card = CategoryCardPool[i];
                if (card != null && card.Root != null) UnityEngine.Object.Destroy(card.Root);
            }
            for (int i = 0; i < DecorationLinePool.Count; i++)
            {
                if (DecorationLinePool[i] != null) UnityEngine.Object.Destroy(DecorationLinePool[i].gameObject);
            }
            CategoryCardPool.Clear();
            DecorationLinePool.Clear();
            _usedCategoryCards = 0;
            _usedDecorationLines = 0;
            _decorationPoolHudId = int.MinValue;
        }

        private static void PrepareHammerSearchCategory(Piece.PieceCategory category)
        {
            if (_hammerSearchCategory == category) return;

            _hammerSearchCategory = category;
            _hammerSearchText = string.Empty;
            _hammerSearchQuery = string.Empty;
            _hammerSearchHasFocus = false;
            if (_hammerSearchInput != null)
            {
                _hammerSearchInput.DeactivateInputField();
                _hammerSearchInput.SetTextWithoutNotify(string.Empty);
            }
        }

        private static void EnsurePersistentHammerSearch(Hud hud, int hudInstanceId)
        {
            if (_persistentHammerSearch != null && _hammerSearchHudId == hudInstanceId)
            {
                if (_hammerSearchInput != null
                    && !string.Equals(_hammerSearchInput.text, _hammerSearchText, StringComparison.Ordinal))
                {
                    _hammerSearchInput.SetTextWithoutNotify(_hammerSearchText);
                }
                _persistentHammerSearch.transform.SetAsLastSibling();
                return;
            }

            DestroyPersistentHammerSearch();
            var searchObject = new GameObject(
                Prefix + "HammerSearch",
                typeof(RectTransform),
                typeof(Image),
                typeof(TMP_InputField),
                typeof(UITooltip));
            searchObject.transform.SetParent(hud.m_pieceSelectionWindow.transform, false);
            searchObject.transform.SetAsLastSibling();
            var searchRect = (RectTransform)searchObject.transform;
            searchRect.anchorMin = new Vector2(0f, 1f);
            searchRect.anchorMax = new Vector2(0f, 1f);
            searchRect.pivot = new Vector2(0f, 1f);
            searchRect.anchoredPosition = new Vector2(
                18f,
                -(hud.m_pieceIconSpacing + 49f + ViewControlsHeight + 8f));
            searchRect.sizeDelta = new Vector2(hud.m_pieceIconSpacing + 8f, HammerSearchHeight);

            Image background = searchObject.GetComponent<Image>();
            background.color = new Color(0.20f, 0.20f, 0.20f, 0.70f);
            background.raycastTarget = true;
            AddRectBorder(searchRect, new Color(0.68f, 0.50f, 0.25f, 0.68f));

            UITooltip tooltip = searchObject.GetComponent<UITooltip>();
            tooltip.m_text = "Search pieces. Middle-click a piece to add or remove a favorite.";

            var viewportObject = new GameObject(
                Prefix + "HammerSearchViewport",
                typeof(RectTransform),
                typeof(RectMask2D));
            viewportObject.transform.SetParent(searchRect, false);
            var viewport = (RectTransform)viewportObject.transform;
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(6f, 2f);
            viewport.offsetMax = new Vector2(-21f, -2f);

            TextMeshProUGUI inputText = CreateHammerSearchText(
                hud,
                viewport,
                Prefix + "HammerSearchText",
                string.Empty,
                new Color(0.92f, 0.90f, 0.84f, 1f));
            TextMeshProUGUI placeholder = CreateHammerSearchText(
                hud,
                viewport,
                Prefix + "HammerSearchPlaceholder",
                "SEARCH",
                new Color(0.66f, 0.63f, 0.57f, 1f));

            TMP_InputField input = searchObject.GetComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.textViewport = viewport;
            input.textComponent = inputText;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.characterLimit = 64;
            input.caretColor = new Color(0.84f, 0.61f, 0.26f, 1f);
            input.selectionColor = new Color(0.25f, 0.55f, 0.82f, 0.55f);
            input.SetTextWithoutNotify(_hammerSearchText);
            input.onSelect.AddListener(_ => _hammerSearchHasFocus = true);
            input.onDeselect.AddListener(_ => _hammerSearchHasFocus = false);
            input.onEndEdit.AddListener(_ => _hammerSearchHasFocus = false);
            input.onValueChanged.AddListener(value =>
            {
                string nextValue = value ?? string.Empty;
                if (string.Equals(_hammerSearchText, nextValue, StringComparison.Ordinal)) return;
                _hammerSearchText = nextValue;
                _hammerSearchQuery = HammerPieceSearch.Normalize(nextValue);
                RefreshHammerList(hud);
                _hammerSearchHasFocus = input != null && input.isFocused;
            });

            var clearObject = new GameObject(
                Prefix + "HammerSearchClear",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            clearObject.transform.SetParent(searchRect, false);
            var clearRect = (RectTransform)clearObject.transform;
            clearRect.anchorMin = new Vector2(1f, 0f);
            clearRect.anchorMax = new Vector2(1f, 1f);
            clearRect.pivot = new Vector2(1f, 0.5f);
            clearRect.anchoredPosition = Vector2.zero;
            clearRect.sizeDelta = new Vector2(20f, 0f);
            Image clearImage = clearObject.GetComponent<Image>();
            clearImage.color = new Color(0f, 0f, 0f, 0.01f);
            Button clearButton = clearObject.GetComponent<Button>();
            clearButton.targetGraphic = clearImage;
            clearButton.onClick.AddListener(() =>
            {
                if (string.IsNullOrEmpty(_hammerSearchText)) return;
                _hammerSearchText = string.Empty;
                _hammerSearchQuery = string.Empty;
                input.SetTextWithoutNotify(string.Empty);
                RefreshHammerList(hud);
            });
            TextMeshProUGUI clearLabel = CreateHammerSearchText(
                hud,
                clearRect,
                Prefix + "HammerSearchClearLabel",
                "×",
                new Color(0.84f, 0.80f, 0.70f, 1f));
            clearLabel.fontSize = 15f;
            clearLabel.alignment = TextAlignmentOptions.Center;

            _persistentHammerSearch = searchObject;
            _hammerSearchInput = input;
            _hammerSearchHudId = hudInstanceId;
        }

        private static TextMeshProUGUI CreateHammerSearchText(
            Hud hud,
            RectTransform parent,
            string name,
            string value,
            Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            var rect = (RectTransform)textObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.font = hud.m_pieceDescription != null ? hud.m_pieceDescription.font : null;
            text.fontSize = 10.5f;
            text.fontStyle = FontStyles.Normal;
            text.color = color;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            return text;
        }

        private static void UpdateHammerSearchVisibility(Piece.PieceCategory category)
        {
            if (_persistentHammerSearch == null) return;
            bool visible = IsEnabled(category);
            if (!visible && _hammerSearchInput != null)
            {
                _hammerSearchHasFocus = false;
                _hammerSearchInput.DeactivateInputField();
            }
            _persistentHammerSearch.SetActive(visible);
            if (visible) _persistentHammerSearch.transform.SetAsLastSibling();
        }

        private static void RefreshHammerList(Hud hud)
        {
            if (_refreshingHammerList) return;
            Player? player = Player.m_localPlayer;
            PieceTable? table = player == null
                ? null
                : PlayerBuildPiecesField.GetValue(player) as PieceTable;
            if (player == null || table == null) return;

            _refreshingHammerList = true;
            try
            {
                List<Piece>? pieces = player.GetBuildPieces();
                if (pieces == null) return;
                Clear(hud);
                Apply(hud, pieces, table.GetSelectedCategory());
            }
            catch (Exception exception)
            {
                Plugin.LogInstance.LogWarning($"Hammer search refresh failed: {exception}");
            }
            finally
            {
                _refreshingHammerList = false;
            }
        }

        private static void SetHammerNoResultsVisible(Hud hud, bool visible)
        {
            if (!visible)
            {
                if (_hammerNoResults != null) _hammerNoResults.SetActive(false);
                return;
            }

            if (_hammerNoResults == null)
            {
                _hammerNoResults = new GameObject(
                    Prefix + "CraftingNoResults",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));
                _hammerNoResults.transform.SetParent(hud.m_pieceListRoot, false);
            }

            _hammerNoResults.SetActive(true);
            _hammerNoResults.transform.SetAsLastSibling();
            var rect = (RectTransform)_hammerNoResults.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(
                (CategoryColumns + (GridWidth - CategoryColumns) * 0.5f) * hud.m_pieceIconSpacing,
                -GridHeight * hud.m_pieceIconSpacing * 0.45f);
            rect.sizeDelta = new Vector2((GridWidth - CategoryColumns) * hud.m_pieceIconSpacing, 42f);

            TextMeshProUGUI text = _hammerNoResults.GetComponent<TextMeshProUGUI>();
            text.text = "No matching unlocked pieces";
            text.font = hud.m_pieceDescription != null ? hud.m_pieceDescription.font : null;
            text.fontSize = 18f;
            text.fontStyle = FontStyles.Bold;
            text.color = new Color(0.90f, 0.79f, 0.61f, 1f);
            text.alignment = TextAlignmentOptions.Center;
            text.outlineWidth = 0.2f;
            text.outlineColor = Color.black;
            text.raycastTarget = false;
        }

        private static void ConfigureNativePieceCells(
            IList icons,
            IReadOnlyList<Piece> pieces,
            int occupiedCount,
            int[] slots,
            int repairIndex)
        {
            for (int i = 0; i < icons.Count; i++)
            {
                GameObject root = GetIconGameObject(icons[i]!);
                Image? background = root.GetComponent<Image>();
                if (background == null) continue;
                UIInputHandler? input = root.GetComponent<UIInputHandler>();
                int id = background.GetInstanceID();
                if (!NativeBackgrounds.ContainsKey(id))
                {
                    NativeBackgrounds[id] = new NativeBackgroundState(
                        root,
                        i == repairIndex || root.activeSelf,
                        background,
                        background.color,
                        background.raycastTarget,
                        (RectTransform)root.transform,
                        ((RectTransform)root.transform).sizeDelta,
                        input,
                        input != null && input.enabled);
                }

                bool interactive = i < occupiedCount
                    && i < slots.Length
                    && slots[i] >= 0
                    && i != repairIndex;
                if (i < occupiedCount && i < slots.Length)
                {
                    root.SetActive(interactive);
                }
                Color transparent = background.color;
                transparent.a = 0f;
                background.color = transparent;
                background.raycastTarget = interactive;
                if (input != null)
                {
                    input.enabled = interactive;
                    int inputId = input.GetInstanceID();
                    if (interactive && !HoverInputs.ContainsKey(inputId))
                    {
                        input.m_onPointerEnter += OnPieceHoverEnter;
                        input.m_onPointerExit += OnPieceHoverExit;
                        input.m_onMiddleDown += OnFavoriteToggle;
                        HoverInputs[inputId] = input;
                    }
                    if (interactive && i < pieces.Count)
                    {
                        FavoritePiecesByInput[inputId] = pieces[i];
                    }
                }
            }
        }

        private static void OnPieceHoverEnter(UIInputHandler input)
        {
            Image? background = input != null ? input.GetComponent<Image>() : null;
            if (background == null) return;
            if (NativeBackgrounds.TryGetValue(background.GetInstanceID(), out NativeBackgroundState state))
            {
                state.SetHovered(true);
            }
        }

        private static void OnFavoriteToggle(UIInputHandler input)
        {
            if (input == null
                || !FavoritePiecesByInput.TryGetValue(input.GetInstanceID(), out Piece piece)
                || piece == null)
            {
                return;
            }

            ModConfig.ToggleFavorite(HammerOrganizer.GetPieceKey(piece));
            NotifyPiecesChanged(_appliedCategory);
            Hud? hud = Hud.instance;
            if (hud != null) RefreshHammerList(hud);
        }

        private static void OnPieceHoverExit(UIInputHandler input)
        {
            Image? background = input != null ? input.GetComponent<Image>() : null;
            if (background == null) return;
            if (NativeBackgrounds.TryGetValue(background.GetInstanceID(), out NativeBackgroundState state))
            {
                state.SetHovered(false);
            }
        }

        private static void RestoreNativeCraftingCells()
        {
            foreach (KeyValuePair<int, UIInputHandler> pair in HoverInputs)
            {
                UIInputHandler input = pair.Value;
                if (input == null) continue;
                input.m_onPointerEnter -= OnPieceHoverEnter;
                input.m_onPointerExit -= OnPieceHoverExit;
                input.m_onMiddleDown -= OnFavoriteToggle;
            }
            HoverInputs.Clear();
            FavoritePiecesByInput.Clear();

            foreach (KeyValuePair<int, NativeBackgroundState> pair in NativeBackgrounds)
            {
                pair.Value.Restore();
            }
            NativeBackgrounds.Clear();
        }

        private static void EnsurePersistentRepair(
            Hud hud,
            IReadOnlyList<Piece> pieces,
            IList icons,
            int repairIndex)
        {
            if (repairIndex < 0 || repairIndex >= pieces.Count || repairIndex >= icons.Count)
            {
                DestroyPersistentRepair();
                return;
            }

            Piece repairPiece = pieces[repairIndex];
            if (_persistentRepair == null
                || _repairHudId != hud.GetInstanceID()
                || _repairPiece == null
                || _repairPiece.GetInstanceID() != repairPiece.GetInstanceID())
            {
                DestroyPersistentRepair();
                GameObject source = GetIconGameObject(icons[repairIndex]!);
                _persistentRepair = UnityEngine.Object.Instantiate(source, hud.m_pieceSelectionWindow.transform);
                _persistentRepair.name = Prefix + "PersistentRepair";
                _repairHudId = hud.GetInstanceID();
                _repairPiece = repairPiece;
                ConfigurePersistentRepair(hud, _persistentRepair, repairPiece);
            }

            _hiddenRepairRoot = GetIconGameObject(icons[repairIndex]!);
            _hiddenRepairRoot.SetActive(false);
            _persistentRepair.SetActive(true);
        }

        private static void EnsurePersistentViewControls(Hud hud, int hudInstanceId)
        {
            if (_persistentViewControls != null && _viewControlsHudId == hudInstanceId) return;

            DestroyPersistentViewControls();
            var rootObject = new GameObject(Prefix + "PersistentViewControls", typeof(RectTransform));
            rootObject.transform.SetParent(hud.m_pieceSelectionWindow.transform, false);
            rootObject.transform.SetAsLastSibling();
            var root = (RectTransform)rootObject.transform;
            root.anchorMin = new Vector2(0f, 1f);
            root.anchorMax = new Vector2(0f, 1f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = new Vector2(18f, -(hud.m_pieceIconSpacing + 49f));
            root.sizeDelta = new Vector2(hud.m_pieceIconSpacing + 8f, ViewControlsHeight);

            _defaultViewBackground = CreateViewButton(hud, root, "DEFAULT VIEW", 0, false);
            _modViewBackground = CreateViewButton(hud, root, "MOD VIEW", 1, true);
            _persistentViewControls = rootObject;
            _viewControlsHudId = hudInstanceId;
            UpdateViewButtonState();
        }

        private static Image CreateViewButton(
            Hud hud,
            RectTransform parent,
            string caption,
            int index,
            bool useModView)
        {
            float buttonHeight = (ViewControlsHeight - ViewButtonGap) * 0.5f;
            var buttonObject = new GameObject(
                Prefix + (useModView ? "ModView" : "DefaultView"),
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = (RectTransform)buttonObject.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -index * (buttonHeight + ViewButtonGap));
            rect.sizeDelta = new Vector2(0f, buttonHeight);

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.20f, 0.20f, 0.20f, 0.50f);
            background.raycastTarget = true;
            AddRectBorder(rect,
                new Color(0.68f, 0.50f, 0.25f, 0.68f));

            Button button = buttonObject.GetComponent<Button>();
            button.targetGraphic = background;
            // Unity's ColorTint transition writes directly to Image.color and
            // would erase the persistent blue active-view state on pointer exit.
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SetHammerView(hud, useModView));

            var labelObject = new GameObject(Prefix + "ViewButtonLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(rect, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(4f, 2f);
            labelRect.offsetMax = new Vector2(-4f, -2f);

            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = caption;
            label.font = hud.m_pieceDescription != null ? hud.m_pieceDescription.font : null;
            label.fontSize = 9.5f;
            label.fontSizeMin = 7f;
            label.fontSizeMax = 9.5f;
            label.enableAutoSizing = true;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.94f, 0.83f, 0.64f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Ellipsis;
            label.outlineWidth = 0.16f;
            label.outlineColor = Color.black;
            label.raycastTarget = false;
            return background;
        }

        private static void SetHammerView(Hud hud, bool useModView)
        {
            Player? player = Player.m_localPlayer;
            if (player == null) return;
            PieceTable? table = PlayerBuildPiecesField.GetValue(player) as PieceTable;
            if (table == null || table.gameObject == null) return;
            if (ShouldUseModView(table) == useModView) return;

            ModConfig.SetToolModView(table.gameObject.name, useModView);
            UseModView = useModView;
            Clear(hud, true);
            UpdateViewButtonState();

            try
            {
                UpdateAvailablePiecesMethod.Invoke(player, null);
                HudUpdatePieceListMethod.Invoke(hud, new object[]
                {
                    player,
                    table.GetSelectedIndex(),
                    table.GetSelectedCategory(),
                    true
                });
            }
            catch (Exception exception)
            {
                Plugin.LogInstance.LogWarning($"Hammer view switch failed: {exception}");
            }
        }

        private static void UpdateViewButtonState()
        {
            Color active = new Color(0.42f, 0.42f, 0.42f, 0.50f);
            Color inactive = new Color(0.20f, 0.20f, 0.20f, 0.50f);
            if (_defaultViewBackground != null)
            {
                _defaultViewBackground.color = UseModView ? inactive : active;
            }
            if (_modViewBackground != null)
            {
                _modViewBackground.color = UseModView ? active : inactive;
            }
        }

        private static void ConfigurePersistentRepair(Hud hud, GameObject button, Piece repairPiece)
        {
            float spacing = hud.m_pieceIconSpacing;
            var rect = (RectTransform)button.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(18f, -18f);
            rect.sizeDelta = new Vector2(spacing + 8f, spacing + 13f);

            Image? background = button.GetComponent<Image>();
            if (background != null)
            {
                background.color = new Color(0.20f, 0.20f, 0.20f, 0.70f);
                background.raycastTarget = true;
                AddRectBorder(rect,
                    new Color(0.68f, 0.50f, 0.25f, 0.68f));
            }

            Transform? selected = button.transform.Find("selected");
            if (selected != null) selected.gameObject.SetActive(false);
            Transform? upgrade = button.transform.Find("upgrade");
            if (upgrade != null) upgrade.gameObject.SetActive(false);

            Transform? iconTransform = button.transform.Find("icon");
            if (iconTransform != null)
            {
                var iconRect = (RectTransform)iconTransform;
                iconRect.anchorMin = new Vector2(0.12f, 0.25f);
                iconRect.anchorMax = new Vector2(0.88f, 0.96f);
                iconRect.offsetMin = Vector2.zero;
                iconRect.offsetMax = Vector2.zero;
                Image? icon = iconTransform.GetComponent<Image>();
                if (icon != null)
                {
                    icon.sprite = repairPiece.m_icon;
                    icon.preserveAspect = true;
                    icon.color = Color.white;
                }
            }

            UIInputHandler? input = button.GetComponent<UIInputHandler>();
            if (input != null)
            {
                input.m_onLeftClick = null;
                input.m_onLeftDown = OnPersistentRepairSelected;
                input.m_onLeftUp = null;
                input.m_onRightClick = null;
                input.m_onRightDown = OnPersistentRepairSelected;
                input.m_onRightUp = null;
                input.m_onMiddleClick = null;
                input.m_onMiddleDown = null;
                input.m_onMiddleUp = null;
                input.m_onPointerEnter = null;
                input.m_onPointerExit = null;
            }

            UITooltip? tooltip = button.GetComponent<UITooltip>();
            if (tooltip != null) tooltip.m_text = repairPiece.m_name;

            var labelObject = new GameObject(Prefix + "RepairLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(button.transform, false);
            var labelRect = (RectTransform)labelObject.transform;
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0f);
            labelRect.pivot = new Vector2(0.5f, 0f);
            labelRect.anchoredPosition = new Vector2(0f, 3f);
            labelRect.sizeDelta = new Vector2(-6f, 18f);
            TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
            label.text = "Repair";
            label.font = hud.m_pieceDescription != null ? hud.m_pieceDescription.font : null;
            label.fontSize = 10.5f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.91f, 0.75f, 0.47f, 1f);
            label.alignment = TextAlignmentOptions.Center;
            label.outlineWidth = 0.2f;
            label.outlineColor = Color.black;
            label.raycastTarget = false;
        }

        private static void OnPersistentRepairSelected(UIInputHandler _)
        {
            Player? player = Player.m_localPlayer;
            if (player == null) return;

            PieceTable? table = PlayerBuildPiecesField.GetValue(player) as PieceTable;
            List<Piece>? pieces = player.GetBuildPieces();
            if (table == null || pieces == null) return;

            // Repair is an All-category piece. Player.SetSelectedPiece(Piece)
            // attempts to resolve a single owning category and can therefore
            // fail (or jump to the one category where the lookup happened to
            // succeed). Select the Repair entry in the active category by its
            // logical grid index instead. This preserves the open tab and
            // matches the way Valheim handles a normal repair-cell click.
            int repairIndex = FindRepairIndexInCurrentCategory(table, pieces);
            if (repairIndex < 0)
            {
                Plugin.LogInstance.LogWarning(
                    $"Static Repair selection failed: no Repair action was found in {table.GetSelectedCategory()}.");
                return;
            }

            player.SetSelectedPiece(new Vector2Int(
                repairIndex % GridWidth,
                repairIndex / GridWidth));
            Hud.HidePieceSelection();
        }

        private static int FindRepairIndexInCurrentCategory(
            PieceTable table,
            IReadOnlyList<Piece> pieces)
        {
            if (_stateValid
                && table.GetSelectedCategory() == _appliedCategory
                && _repairLogicalIndex >= 0
                && _repairLogicalIndex < pieces.Count
                && CraftingLayoutMetadata.IsRepair(pieces[_repairLogicalIndex]))
            {
                return _repairLogicalIndex;
            }

            for (int i = 0; i < pieces.Count; i++)
            {
                if (CraftingLayoutMetadata.IsRepair(pieces[i])) return i;
            }
            return -1;
        }

        private static void RemoveStaleRepairForDifferentHud(int hudInstanceId)
        {
            if (_persistentRepair != null && _repairHudId != hudInstanceId)
            {
                DestroyPersistentRepair();
            }
            if (_persistentViewControls != null && _viewControlsHudId != hudInstanceId)
            {
                DestroyPersistentViewControls();
            }
            if (_persistentHammerSearch != null && _hammerSearchHudId != hudInstanceId)
            {
                DestroyPersistentHammerSearch();
            }
        }

        private static void RemoveGeneratedWarmBackgrounds(Hud hud)
        {
            if (hud.m_pieceSelectionWindow == null) return;

            Transform? parent = hud.m_pieceSelectionWindow.transform.parent;
            if (parent != null)
            {
                Transform? legacy = parent.Find(Prefix + "WarmPanelBackdrop");
                if (legacy != null)
                {
                    legacy.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(legacy.gameObject);
                }
            }

            Transform[] descendants =
                hud.m_pieceSelectionWindow.GetComponentsInChildren<Transform>(true);
            foreach (Transform descendant in descendants)
            {
                if (descendant == null || descendant.gameObject.name != Prefix + "WarmPanelOverlay")
                {
                    continue;
                }

                descendant.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(descendant.gameObject);
            }
        }

        private static void DestroyPersistentRepair()
        {
            if (_persistentRepair != null)
            {
                _persistentRepair.SetActive(false);
                UnityEngine.Object.Destroy(_persistentRepair);
            }
            _persistentRepair = null;
            _repairPiece = null;
            _repairHudId = int.MinValue;
        }

        private static void DestroyPersistentViewControls()
        {
            if (_persistentViewControls != null)
            {
                _persistentViewControls.SetActive(false);
                UnityEngine.Object.Destroy(_persistentViewControls);
            }
            _persistentViewControls = null;
            _defaultViewBackground = null;
            _modViewBackground = null;
            _viewControlsHudId = int.MinValue;
        }

        private static void DestroyPersistentHammerSearch()
        {
            _hammerSearchHasFocus = false;
            if (_hammerSearchInput != null)
            {
                _hammerSearchInput.onValueChanged.RemoveAllListeners();
                _hammerSearchInput.onSelect.RemoveAllListeners();
                _hammerSearchInput.onDeselect.RemoveAllListeners();
                _hammerSearchInput.onEndEdit.RemoveAllListeners();
                _hammerSearchInput.DeactivateInputField();
            }
            if (_persistentHammerSearch != null)
            {
                Button[] buttons = _persistentHammerSearch.GetComponentsInChildren<Button>(true);
                for (int i = 0; i < buttons.Length; i++) buttons[i].onClick.RemoveAllListeners();
                _persistentHammerSearch.SetActive(false);
                UnityEngine.Object.Destroy(_persistentHammerSearch);
            }
            if (_hammerNoResults != null)
            {
                _hammerNoResults.SetActive(false);
                UnityEngine.Object.Destroy(_hammerNoResults);
            }
            _persistentHammerSearch = null;
            _hammerSearchInput = null;
            _hammerSearchHudId = int.MinValue;
            _hammerSearchText = string.Empty;
            _hammerSearchQuery = string.Empty;
            _hammerSearchCategory = Piece.PieceCategory.Max;
            _hammerNoResults = null;
            _refreshingHammerList = false;
        }

        private static void AddOutline(Transform parent, float inset, Color color)
        {
            var outline = parent.gameObject.AddComponent<Outline>();
            float distance = Mathf.Max(1f, inset);
            outline.effectColor = color;
            outline.effectDistance = new Vector2(distance, -distance);
            outline.useGraphicAlpha = true;
        }

        private static void AddRectBorder(RectTransform parent, Color color)
        {
            AddRectBorderLine(parent, "Top",
                new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -0.5f), new Vector2(0f, 1f), color);
            AddRectBorderLine(parent, "Bottom",
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 0.5f), new Vector2(0f, 1f), color);
            AddRectBorderLine(parent, "Left",
                new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(0.5f, 0f), new Vector2(1f, 0f), color);
            AddRectBorderLine(parent, "Right",
                new Vector2(1f, 0f), new Vector2(1f, 1f),
                new Vector2(-0.5f, 0f), new Vector2(1f, 0f), color);
        }

        private static void AddRectBorderLine(
            RectTransform parent,
            string side,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            Color color)
        {
            var lineObject = new GameObject(
                Prefix + "ViewButtonBorder" + side,
                typeof(RectTransform),
                typeof(Image));
            lineObject.transform.SetParent(parent, false);
            var rect = (RectTransform)lineObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Image line = lineObject.GetComponent<Image>();
            line.color = color;
            line.raycastTarget = false;
        }

        private static void ApplyGridPermutation(
            Hud hud,
            IList icons,
            CraftingLayoutResult layout,
            int occupiedCount)
        {
            int total = Math.Min(icons.Count, GridWidth * GridHeight);
            int visualCapacity = GridWidth * Mathf.Max(GridHeight, layout.VisibleRows);
            var used = new bool[visualCapacity];
            for (int i = 0; i < layout.Slots.Length && i < occupiedCount; i++)
            {
                if (layout.Slots[i] >= 0 && layout.Slots[i] < visualCapacity) used[layout.Slots[i]] = true;
            }

            int nextEmptySlot = 0;
            for (int i = 0; i < total; i++)
            {
                if (i < occupiedCount && layout.Slots[i] < 0) continue;

                int slot;
                if (i < occupiedCount)
                {
                    slot = layout.Slots[i];
                }
                else
                {
                    while (nextEmptySlot < visualCapacity && used[nextEmptySlot]) nextEmptySlot++;
                    slot = nextEmptySlot < visualCapacity ? nextEmptySlot++ : i;
                }
                float xOffset = i < occupiedCount ? layout.XOffsets[i] : 0f;
                PositionCraftingIcon(hud, GetIconGameObject(icons[i]!), slot, xOffset, layout.VisibleRows);
            }
        }

        private static void ApplyShelfPositions(
            Hud hud,
            IList icons,
            ShelfLayoutResult layout,
            int occupiedCount)
        {
            int count = Math.Min(occupiedCount, icons.Count);
            float cellScale = Mathf.Min(1f, layout.RowPitch / hud.m_pieceIconSpacing);
            for (int i = 0; i < count; i++)
            {
                if (layout.Slots[i] < 0) continue;
                var rect = (RectTransform)GetIconGameObject(icons[i]!).transform;
                int row = layout.Slots[i] / ShelfColumns;
                if (cellScale < 0.999f)
                {
                    // Building can require eight material rows inside the
                    // seven-row presentation. Keep each hit target and artwork
                    // square inside its assigned row instead of letting the
                    // native-size cell cross a material boundary.
                    rect.sizeDelta *= cellScale;
                }
                rect.anchoredPosition = new Vector2(
                    layout.XPositions[i],
                    -row * layout.RowPitch);
            }
        }

        private static void RestoreNativeGrid(Hud hud, IList icons)
        {
            int width = HammerGridDimensions.Width;
            int height = HammerGridDimensions.Height;
            int total = Math.Min(icons.Count, width * height);
            for (int i = 0; i < total; i++)
            {
                PositionIcon(hud, GetIconGameObject(icons[i]!), i, width);
            }
        }

        private static void PositionIcon(Hud hud, GameObject iconRoot, int slot)
            => PositionIcon(hud, iconRoot, slot, GridWidth);

        private static void PositionCraftingIcon(
            Hud hud,
            GameObject iconRoot,
            int slot,
            float xOffset,
            int visibleRows)
        {
            var rect = (RectTransform)iconRoot.transform;
            float spacing = hud.m_pieceIconSpacing;
            rect.anchoredPosition = new Vector2(
                slot % GridWidth * spacing * CraftingColumnPitchScale
                    + CraftingContentShift(spacing)
                    + xOffset,
                -(slot / GridWidth) * CraftingRowPitch(hud, visibleRows));
        }

        private static float CraftingRowPitch(Hud hud, int visibleRows)
            => visibleRows > GridHeight
                ? hud.m_pieceIconSpacing
                : HammerGridDimensions.CraftingRowPitch(hud);

        private static void PositionIcon(Hud hud, GameObject iconRoot, int slot, int width)
        {
            var rect = (RectTransform)iconRoot.transform;
            rect.anchoredPosition = new Vector2(
                slot % width * hud.m_pieceIconSpacing,
                -(slot / width) * hud.m_pieceIconSpacing);
        }

        private static bool IsEnabled(Piece.PieceCategory category)
        {
            if (!UseModView) return false;
            switch (category)
            {
                case Piece.PieceCategory.Misc: return true;
                case Piece.PieceCategory.Crafting: return ModConfig.OrganizeCrafting.Value;
                case Piece.PieceCategory.BuildingWorkbench: return ModConfig.OrganizeBuilding.Value;
                case Piece.PieceCategory.BuildingStonecutter: return ModConfig.OrganizeHeavyBuilding.Value;
                case Piece.PieceCategory.Furniture: return ModConfig.OrganizeFurniture.Value;
                default: return false;
            }
        }

        internal static void Clear(Hud hud, bool removePersistentRepair = false)
        {
            if (_stateValid)
            {
                IList icons = (IList)PieceIconsField.GetValue(hud);
                RemoveDecorations(hud, icons);
                ResetState();
            }
            if (removePersistentRepair) DestroyPersistentRepair();
        }

        internal static void Shutdown()
        {
            Release(Hud.instance);
            Array.Clear(PieceGenerations, 0, PieceGenerations.Length);
            UseModView = true;
        }

        internal static void Release(Hud? hud)
        {
            Exception? failure = null;
            try
            {
                if (hud != null)
                {
                    IList icons = (IList)PieceIconsField.GetValue(hud);
                    RemoveDecorations(hud, icons, true);
                }
                else
                {
                    DestroyDecorationPool();
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                if (_hiddenRepairRoot != null) _hiddenRepairRoot.SetActive(true);
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }
            finally
            {
                _hiddenRepairRoot = null;
            }

            try
            {
                RestoreNativeCraftingCells();
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }

            try
            {
                DestroyPersistentRepair();
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }

            try
            {
                DestroyPersistentViewControls();
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }

            try
            {
                DestroyPersistentHammerSearch();
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }

            try
            {
                if (hud != null) RemoveGeneratedWarmBackgrounds(hud);
                _cleanedGeneratedBackgroundHudId = int.MinValue;
            }
            catch (Exception exception)
            {
                failure = failure == null ? exception : new AggregateException(failure, exception);
            }

            ResetState();

            if (failure != null) throw new InvalidOperationException("Could not fully release CraftIndex Hammer UI.", failure);
        }

        private static void RemoveDecorations(Hud hud, IList icons, bool destroy = false)
        {
            if (_hiddenRepairRoot != null)
            {
                _hiddenRepairRoot.SetActive(true);
                _hiddenRepairRoot = null;
            }
            RestoreNativeCraftingCells();
            DeactivateDecorationPool();

            for (int childIndex = hud.m_pieceListRoot.childCount - 1; childIndex >= 0; childIndex--)
            {
                Transform child = hud.m_pieceListRoot.GetChild(childIndex);
                if (!child.name.StartsWith(Prefix + "Crafting", StringComparison.Ordinal)) continue;
                if (IsPooledDecorationRoot(child.gameObject)) continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }

            RestoreNativeGrid(hud, icons);
            for (int i = 0; i < IconDecorationPool.Count; i++)
            {
                GameObject decoration = IconDecorationPool[i];
                if (decoration == null) continue;
                decoration.SetActive(false);
                if (destroy) UnityEngine.Object.Destroy(decoration);
            }
            if (destroy)
            {
                IconDecorationPool.Clear();
                IconDecorationIds.Clear();
            }

            if (destroy) DestroyDecorationPool();
        }

        private static void TrackIconDecoration(GameObject decoration)
        {
            if (IconDecorationIds.Add(decoration.GetInstanceID())) IconDecorationPool.Add(decoration);
        }

        private static bool IsPooledDecorationRoot(GameObject root)
        {
            for (int i = 0; i < CategoryCardPool.Count; i++)
            {
                CategoryCardView card = CategoryCardPool[i];
                if (card != null && card.Root == root) return true;
            }
            for (int i = 0; i < DecorationLinePool.Count; i++)
            {
                if (DecorationLinePool[i] != null && DecorationLinePool[i].gameObject == root) return true;
            }
            return false;
        }

        private static void AddTileTint(GameObject iconRoot, GroupPalette palette, bool groupStart)
        {
            Transform? existing = iconRoot.transform.Find(Prefix + "TileTint");
            GameObject tintObject;
            if (existing != null)
            {
                tintObject = existing.gameObject;
                tintObject.SetActive(true);
            }
            else
            {
                tintObject = new GameObject(Prefix + "TileTint", typeof(RectTransform), typeof(Image));
                tintObject.transform.SetParent(iconRoot.transform, false);
            }
            TrackIconDecoration(tintObject);
            tintObject.transform.SetAsFirstSibling();
            var rect = (RectTransform)tintObject.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(TileInset, TileInset);
            rect.offsetMax = new Vector2(-TileInset, -TileInset);

            Image image = tintObject.GetComponent<Image>();
            Image? nativeBackground = iconRoot.GetComponent<Image>();
            if (nativeBackground != null)
            {
                image.sprite = nativeBackground.sprite;
                image.type = nativeBackground.type;
            }
            Color color = palette.Background;
            if (groupStart) color.a *= 1.25f;
            image.color = color;
            image.raycastTarget = false;
        }

        private static void AddLabel(Hud hud, GameObject iconRoot, string label, GroupPalette palette)
        {
            Transform? existing = iconRoot.transform.Find(Prefix + "GroupLabel");
            GameObject labelObject;
            if (existing != null)
            {
                labelObject = existing.gameObject;
                labelObject.SetActive(true);
            }
            else
            {
                labelObject = new GameObject(
                    Prefix + "GroupLabel",
                    typeof(RectTransform),
                    typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(iconRoot.transform, false);
            }
            TrackIconDecoration(labelObject);
            labelObject.transform.SetAsLastSibling();
            var rect = (RectTransform)labelObject.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 2f);
            rect.sizeDelta = new Vector2(-7f, LabelHeight);

            TextMeshProUGUI text = labelObject.GetComponent<TextMeshProUGUI>();
            text.text = label.ToUpperInvariant();
            text.font = hud.m_pieceDescription != null ? hud.m_pieceDescription.font : null;
            text.fontSize = 8.5f;
            text.fontStyle = FontStyles.Bold;
            text.color = palette.Text;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.BottomLeft;
            text.outlineWidth = 0.1f;
            text.outlineColor = new Color32(18, 14, 11, 225);
        }

        private static bool IsStandaloneAction(Piece.PieceCategory category, string label)
            => category == Piece.PieceCategory.Crafting
                && string.Equals(Normalize(label), "actions", StringComparison.Ordinal);

        private static GroupPalette GetPalette(Piece.PieceCategory category, string label)
        {
            string id = Normalize(label);
            if (category == Piece.PieceCategory.Crafting)
            {
                if (id.Contains("workbench")) return FallbackPalettes[0];
                if (id.Contains("blackforge")) return FallbackPalettes[4];
                if (id.Contains("forge")) return FallbackPalettes[1];
                if (id.Contains("cauldron")) return FallbackPalettes[2];
                if (id.Contains("stonecutter")) return FallbackPalettes[3];
                if (id.Contains("artisan")) return FallbackPalettes[6];
                if (id.Contains("galdr")) return FallbackPalettes[5];
                if (id.Contains("foodpreparation") || id.Contains("preptable")) return FallbackPalettes[2];
                if (id.Contains("mead") || id.Contains("ketill") || id.Contains("kettle")) return FallbackPalettes[7];
                if (id.Contains("cooking")) return FallbackPalettes[6];
            }

            int hash = 17;
            for (int i = 0; i < id.Length; i++) hash = unchecked(hash * 31 + id[i]);
            return FallbackPalettes[(hash & int.MaxValue) % FallbackPalettes.Length];
        }

        private static string CompactLabel(string label)
        {
            string id = Normalize(label);
            if (id.Contains("foodpreparation") || id.Contains("preptable")) return "Food Prep";
            if (id.Contains("stonecutter")) return "Stonecut";
            if (id.Contains("artisantable")) return "Artisan";
            if (id.Contains("galdrtable")) return "Galdr";
            if (id.Contains("mead") && (id.Contains("ketill") || id.Contains("kettle"))) return "Mead Ketill";
            if (id.Contains("othercrafting")) return "Other";
            return label;
        }

        private static string Localize(string value)
            => Localization.instance != null ? Localization.instance.Localize(value) : value;

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

        private static GroupPalette Palette(
            float textR,
            float textG,
            float textB,
            float backgroundR,
            float backgroundG,
            float backgroundB)
            => new GroupPalette(
                new Color(textR, textG, textB, 0.96f),
                new Color(backgroundR, backgroundG, backgroundB, 0.085f));

        private static GameObject GetIconGameObject(object icon)
        {
            Type type = icon.GetType();
            if (_iconType != type)
            {
                _iconType = type;
                _iconGoField = type.GetField("m_go", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (_iconGoField == null) throw new MissingFieldException(type.FullName, "m_go");
            }
            return (GameObject)_iconGoField!.GetValue(icon);
        }

        private readonly struct CraftingEntry
        {
            internal CraftingEntry(int pieceIndex, CraftingPieceLayout metadata, string name)
            {
                PieceIndex = pieceIndex;
                Metadata = metadata;
                Name = name;
            }
            internal int PieceIndex { get; }
            internal CraftingPieceLayout Metadata { get; }
            internal string Name { get; }
        }

        private sealed class CraftingLayoutResult
        {
            internal CraftingLayoutResult(
                int[] slots,
                float[] xOffsets,
                int repairIndex,
                string[] rowLabels,
                int[] representatives,
                List<SubgroupDivider> subgroupBreaks,
                int visibleRows)
            {
                Slots = slots;
                XOffsets = xOffsets;
                RepairIndex = repairIndex;
                RowLabels = rowLabels;
                Representatives = representatives;
                SubgroupBreaks = subgroupBreaks;
                VisibleRows = visibleRows;
            }

            internal int[] Slots { get; }
            internal float[] XOffsets { get; }
            internal int RepairIndex { get; }
            internal string[] RowLabels { get; }
            internal int[] Representatives { get; }
            internal List<SubgroupDivider> SubgroupBreaks { get; }
            internal int VisibleRows { get; }
        }

        private readonly struct SubgroupDivider
        {
            internal SubgroupDivider(int row, int column, int gapCount)
            {
                Row = row;
                Column = column;
                GapCount = gapCount;
            }

            internal int Row { get; }
            internal int Column { get; }
            internal int GapCount { get; }
        }

        private sealed class ShelfLayoutResult
        {
            internal ShelfLayoutResult(
                int[] slots,
                float[] xPositions,
                List<ShelfCard> cards,
                List<ShelfDivider> dividers,
                int visibleRows,
                float rowPitch,
                int repairIndex)
            {
                Slots = slots;
                XPositions = xPositions;
                Cards = cards;
                Dividers = dividers;
                VisibleRows = visibleRows;
                RowPitch = rowPitch;
                RepairIndex = repairIndex;
            }

            internal int[] Slots { get; }
            internal float[] XPositions { get; }
            internal List<ShelfCard> Cards { get; }
            internal List<ShelfDivider> Dividers { get; }
            internal int VisibleRows { get; }
            internal float RowPitch { get; }
            internal int RepairIndex { get; }
        }

        private readonly struct ShelfCard
        {
            internal ShelfCard(int startRow, int rowSpan, string label, int representative)
            {
                StartRow = startRow;
                RowSpan = rowSpan;
                Label = label;
                Representative = representative;
            }

            internal int StartRow { get; }
            internal int RowSpan { get; }
            internal string Label { get; }
            internal int Representative { get; }
        }

        private readonly struct ShelfDivider
        {
            internal ShelfDivider(int row, float x)
            {
                Row = row;
                X = x;
            }

            internal int Row { get; }
            internal float X { get; }
        }

        private readonly struct NativeBackgroundState
        {
            internal NativeBackgroundState(
                GameObject root,
                bool active,
                Image image,
                Color color,
                bool raycastTarget,
                RectTransform rect,
                Vector2 sizeDelta,
                UIInputHandler? input,
                bool inputEnabled)
            {
                Root = root;
                Active = active;
                Image = image;
                Color = color;
                RaycastTarget = raycastTarget;
                Rect = rect;
                SizeDelta = sizeDelta;
                Input = input;
                InputEnabled = inputEnabled;
            }
            private GameObject Root { get; }
            private bool Active { get; }
            private Image Image { get; }
            private Color Color { get; }
            private bool RaycastTarget { get; }
            private RectTransform Rect { get; }
            private Vector2 SizeDelta { get; }
            private UIInputHandler? Input { get; }
            private bool InputEnabled { get; }

            internal void SetHovered(bool hovered)
            {
                if (Image == null) return;
                Color color = Color;
                color.a = hovered ? Mathf.Max(Color.a, 0.62f) : 0f;
                Image.color = color;
            }

            internal void Restore()
            {
                if (Root != null) Root.SetActive(Active);
                if (Image != null)
                {
                    Image.color = Color;
                    Image.raycastTarget = RaycastTarget;
                }
                if (Rect != null) Rect.sizeDelta = SizeDelta;
                if (Input != null) Input.enabled = InputEnabled;
            }
        }

        private readonly struct GroupPalette
        {
            internal GroupPalette(Color text, Color background)
            {
                Text = text;
                Background = background;
            }
            internal Color Text { get; }
            internal Color Background { get; }
        }

        private sealed class CategoryCardView
        {
            internal CategoryCardView(
                GameObject root,
                RectTransform rect,
                Image background,
                RectTransform iconRect,
                Image icon,
                RectTransform labelRect,
                TextMeshProUGUI label)
            {
                Root = root;
                Rect = rect;
                Background = background;
                IconRect = iconRect;
                Icon = icon;
                LabelRect = labelRect;
                Label = label;
            }

            internal GameObject Root { get; }
            internal RectTransform Rect { get; }
            internal Image Background { get; }
            internal RectTransform IconRect { get; }
            internal Image Icon { get; }
            internal RectTransform LabelRect { get; }
            internal TextMeshProUGUI Label { get; }
        }
    }
}
