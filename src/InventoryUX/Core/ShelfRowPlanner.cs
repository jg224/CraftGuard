using System;
using System.Collections.Generic;

namespace InventoryUX.Core
{
    internal static class ShelfRowPlanner
    {
        internal static int ChooseGroupRowSize(
            IReadOnlyList<string> labels,
            int cursor,
            int columns,
            int remainingRows)
        {
            if (labels == null) throw new ArgumentNullException(nameof(labels));
            if (cursor < 0 || cursor >= labels.Count) throw new ArgumentOutOfRangeException(nameof(cursor));
            if (columns < 1) throw new ArgumentOutOfRangeException(nameof(columns));
            if (remainingRows < 1) throw new ArgumentOutOfRangeException(nameof(remainingRows));

            int remaining = labels.Count - cursor;
            if (remainingRows == 1)
            {
                if (remaining > columns)
                {
                    throw new InvalidOperationException(
                        $"The final shelf row cannot hold {remaining} entries in {columns} columns.");
                }
                return remaining;
            }

            int minimum = Math.Max(1, remaining - (remainingRows - 1) * columns);
            int maximum = Math.Min(columns, remaining - (remainingRows - 1));
            if (maximum < minimum)
            {
                throw new InvalidOperationException(
                    $"Cannot distribute {remaining} entries across {remainingRows} shelf rows.");
            }

            string label = labels[cursor];
            int groupRemaining = 1;
            while (cursor + groupRemaining < labels.Count
                && string.Equals(labels[cursor + groupRemaining], label, StringComparison.Ordinal))
            {
                groupRemaining++;
            }

            int rowsForGroup = (groupRemaining + columns - 1) / columns;
            int target = (groupRemaining + rowsForGroup - 1) / rowsForGroup;
            return Math.Max(minimum, Math.Min(maximum, target));
        }
    }
}
