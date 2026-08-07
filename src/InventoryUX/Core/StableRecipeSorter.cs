using System;
using System.Collections.Generic;

namespace InventoryUX.Core
{
    internal static class StableRecipeSorter
    {
        internal static List<T> Sort<T>(IReadOnlyList<T> source, Func<T, RecipeGroup> group, Func<T, RecipeFacts> facts)
        {
            var indexed = new List<(T Item, int Index)>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                indexed.Add((source[i], i));
            }

            indexed.Sort((left, right) =>
            {
                RecipeGroup leftGroup = group(left.Item);
                RecipeGroup rightGroup = group(right.Item);
                int comparison = leftGroup.Order.CompareTo(rightGroup.Order);
                if (comparison != 0)
                {
                    return comparison;
                }

                RecipeFacts leftFacts = facts(left.Item);
                RecipeFacts rightFacts = facts(right.Item);
                comparison = string.Compare(leftFacts.DisplayName, rightFacts.DisplayName, StringComparison.CurrentCultureIgnoreCase);
                if (comparison != 0)
                {
                    return comparison;
                }

                comparison = string.Compare(leftFacts.Id, rightFacts.Id, StringComparison.Ordinal);
                return comparison != 0 ? comparison : left.Index.CompareTo(right.Index);
            });

            var result = new List<T>(indexed.Count);
            for (int i = 0; i < indexed.Count; i++)
            {
                result.Add(indexed[i].Item);
            }

            return result;
        }

        internal static IReadOnlyList<RecipeGroup> VisibleGroups<T>(IReadOnlyList<T> source, Func<T, RecipeGroup> classifier)
        {
            var groups = new List<RecipeGroup>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < source.Count; i++)
            {
                RecipeGroup group = classifier(source[i]);
                if (seen.Add(group.Label))
                {
                    groups.Add(group);
                }
            }

            groups.Sort((left, right) => left.Order.CompareTo(right.Order));
            return groups;
        }
    }
}
