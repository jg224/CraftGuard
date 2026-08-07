using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace InventoryUX.Runtime
{
    internal static class HammerGridDimensions
    {
        internal const int NativeWidth = 15;
        internal const int NativeHeight = 6;
        internal const int ExpandedWidth = 15;
        internal const int ExpandedHeight = 6;
        // Six logical rows are presented across seven native row-heights. This
        // makes every section taller without adding selectable grid cells.
        internal const float ReferenceCraftingRowScale = 7f / 6f;

        internal static int Width => ModConfig.Enabled.Value ? ExpandedWidth : NativeWidth;
        internal static int Height => ModConfig.Enabled.Value ? ExpandedHeight : NativeHeight;
        internal static int MaxX => Width - 1;
        internal static int MaxY => Height - 1;
        internal static float CraftingRowPitch(Hud hud) => hud.m_pieceIconSpacing * ReferenceCraftingRowScale;
    }

    internal static class HammerGridSizer
    {
        private static int _hudInstanceId = int.MinValue;
        private static RectState? _windowState;
        private static RectState? _maskState;
        private static RectState? _categoryState;
        private static RectState? _previousCategoryControlState;

        internal static float RepairRailWidth { get; private set; }

        internal static void Apply(Hud hud)
        {
            int hudInstanceId = hud.GetInstanceID();
            if (_hudInstanceId == hudInstanceId) return;

            Restore();
            float spacing = hud.m_pieceIconSpacing;
            float contentWidthDelta = (HammerGridDimensions.ExpandedWidth - HammerGridDimensions.NativeWidth) * spacing;
            float contentHeightDelta = Mathf.Max(0f,
                (HammerGridDimensions.ExpandedHeight * HammerGridDimensions.ReferenceCraftingRowScale
                    - HammerGridDimensions.NativeHeight) * spacing);
            RepairRailWidth = spacing + 28f;
            RectTransform? window = hud.m_pieceSelectionWindow != null
                ? hud.m_pieceSelectionWindow.GetComponent<RectTransform>()
                : null;
            if (window != null)
            {
                _windowState = new RectState(window);
                ExpandCentered(window, contentWidthDelta + RepairRailWidth, contentHeightDelta);
            }

            RectTransform? mask = hud.m_pieceListMask;
            if (mask != null)
            {
                _maskState = new RectState(mask);
                float widthDelta = Mathf.Approximately(mask.anchorMin.x, mask.anchorMax.x) ? contentWidthDelta : 0f;
                float heightDelta = Mathf.Approximately(mask.anchorMin.y, mask.anchorMax.y) ? contentHeightDelta : 0f;
                ExpandKeepingTopLeft(mask, widthDelta, heightDelta);
                mask.anchoredPosition += new Vector2(RepairRailWidth, 0f);
            }

            RectTransform? categoryRoot = hud.m_pieceCategoryRoot != null
                ? hud.m_pieceCategoryRoot.GetComponent<RectTransform>()
                : null;
            if (categoryRoot != null)
            {
                _categoryState = new RectState(categoryRoot);
                categoryRoot.sizeDelta -= new Vector2(RepairRailWidth, 0f);
                categoryRoot.anchoredPosition += new Vector2(
                    RepairRailWidth * (1f - categoryRoot.pivot.x),
                    0f);
                MovePreviousCategoryControl(categoryRoot, RepairRailWidth);
            }

            _hudInstanceId = hudInstanceId;
        }

        internal static void Restore()
        {
            _windowState?.Restore();
            _maskState?.Restore();
            _categoryState?.Restore();
            _previousCategoryControlState?.Restore();
            _windowState = null;
            _maskState = null;
            _categoryState = null;
            _previousCategoryControlState = null;
            _hudInstanceId = int.MinValue;
            RepairRailWidth = 0f;
        }

        private static void MovePreviousCategoryControl(RectTransform categoryRoot, float offset)
        {
            RectTransform? control = FindTopLevelControl(categoryRoot, "Q");
            if (control == null) return;

            _previousCategoryControlState = new RectState(control);
            control.anchoredPosition += new Vector2(offset, 0f);
        }

        private static RectTransform? FindTopLevelControl(RectTransform root, string displayedText)
        {
            TextMeshProUGUI[] tmpLabels = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < tmpLabels.Length; i++)
            {
                if (string.Equals(tmpLabels[i].text?.Trim(), displayedText, System.StringComparison.OrdinalIgnoreCase))
                {
                    return TopLevelChild(root, tmpLabels[i].transform);
                }
            }

            Text[] labels = root.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                if (string.Equals(labels[i].text?.Trim(), displayedText, System.StringComparison.OrdinalIgnoreCase))
                {
                    return TopLevelChild(root, labels[i].transform);
                }
            }
            return null;
        }

        private static RectTransform? TopLevelChild(RectTransform root, Transform child)
        {
            Transform current = child;
            while (current.parent != null && current.parent != root)
            {
                current = current.parent;
            }
            return current.parent == root ? current as RectTransform : null;
        }

        private static void ExpandCentered(RectTransform rect, float widthDelta, float heightDelta)
        {
            rect.sizeDelta += new Vector2(widthDelta, heightDelta);
        }

        private static void ExpandKeepingTopLeft(RectTransform rect, float widthDelta, float heightDelta)
        {
            rect.sizeDelta += new Vector2(widthDelta, heightDelta);
            rect.anchoredPosition += new Vector2(
                widthDelta * rect.pivot.x,
                -heightDelta * (1f - rect.pivot.y));
        }

        private sealed class RectState
        {
            private readonly RectTransform _rect;
            private readonly Vector2 _sizeDelta;
            private readonly Vector2 _anchoredPosition;

            internal RectState(RectTransform rect)
            {
                _rect = rect;
                _sizeDelta = rect.sizeDelta;
                _anchoredPosition = rect.anchoredPosition;
            }

            internal void Restore()
            {
                if (_rect == null) return;
                _rect.sizeDelta = _sizeDelta;
                _rect.anchoredPosition = _anchoredPosition;
            }
        }
    }
}
