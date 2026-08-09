using HarmonyLib;
using InventoryUX.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace InventoryUX.Runtime
{
    internal static class HammerOrganizer
    {
        private static readonly System.Reflection.FieldInfo AvailablePiecesField =
            AccessTools.Field(typeof(PieceTable), "m_availablePieces");

        private static readonly Dictionary<long, string> LabelsByPieceCategory = new Dictionary<long, string>();
        private static readonly Dictionary<long, HammerPieceMetadata> MetadataByPieceCategory =
            new Dictionary<long, HammerPieceMetadata>();
        private static readonly Dictionary<long, CategorySortState> SortStateByTableCategory =
            new Dictionary<long, CategorySortState>();

        internal static bool ReorderAvailablePieces(PieceTable table)
        {
            var available = (List<List<Piece>>)AvailablePiecesField.GetValue(table);
            bool changed = false;
            changed |= Reorder(table, available, Piece.PieceCategory.Misc, true);
            changed |= Reorder(table, available, Piece.PieceCategory.Crafting, ModConfig.OrganizeCrafting.Value);
            changed |= Reorder(table, available, Piece.PieceCategory.BuildingWorkbench, ModConfig.OrganizeBuilding.Value);
            changed |= Reorder(table, available, Piece.PieceCategory.BuildingStonecutter, ModConfig.OrganizeHeavyBuilding.Value);
            changed |= Reorder(table, available, Piece.PieceCategory.Furniture, ModConfig.OrganizeFurniture.Value);
            return changed;
        }

        internal static void Reset()
        {
            LabelsByPieceCategory.Clear();
            MetadataByPieceCategory.Clear();
            SortStateByTableCategory.Clear();
        }

        internal static string? GetLabel(Piece piece, Piece.PieceCategory category)
        {
            return piece != null
                && LabelsByPieceCategory.TryGetValue(CacheKey(piece.GetInstanceID(), category), out string label)
                ? label
                : null;
        }

        internal static int GetExtensionOrder(Piece piece)
        {
            string id = Normalize(piece.gameObject.name);
            int extIndex = TrailingNumber(id);

            // Vanilla's ext suffixes are an authoritative progression hint for
            // the current data set. Named exceptions remain explicit and small.
            if (id.Contains("choppingblock")) return 10;
            if (id.Contains("tanningrack")) return 20;
            if (id.Contains("adze")) return 30;
            if (id.Contains("toolshelf")) return 40;
            if (id.Contains("forgecooler")) return 10;
            if (id.Contains("anvils")) return 20;
            if (id.Contains("smithsanvil")) return 30;
            if (id.Contains("grindingwheel")) return 40;
            if (id.Contains("forgebellows")) return 50;

            return extIndex >= 0 ? 10 + extIndex * 10 : 500;
        }

        internal static PieceGroup Classify(Piece piece, Piece.PieceCategory category)
            => GetMetadata(piece, category).Group;

        private static PieceGroup ClassifyUncached(Piece piece, Piece.PieceCategory category)
        {
            switch (category)
            {
                case Piece.PieceCategory.Misc:
                    return ClassifyMisc(piece);
                case Piece.PieceCategory.Crafting:
                    return ClassifyCrafting(piece);
                case Piece.PieceCategory.BuildingWorkbench:
                    return ClassifyBuilding(piece, false);
                case Piece.PieceCategory.BuildingStonecutter:
                    return ClassifyBuilding(piece, true);
                case Piece.PieceCategory.Furniture:
                    return ClassifyFurniture(piece);
                default:
                    return new PieceGroup("Other", 999, 999);
            }
        }

        private static bool Reorder(
            PieceTable table,
            List<List<Piece>> all,
            Piece.PieceCategory category,
            bool enabled)
        {
            int index = (int)category;
            if (!enabled || index < 0 || index >= all.Count || all[index] == null)
            {
                return false;
            }

            List<Piece> pieces = all[index];
            PieceSetSignature signature = PieceSetSignature.Create(pieces);
            long stateKey = CacheKey(table.GetInstanceID(), category);
            if (SortStateByTableCategory.TryGetValue(stateKey, out CategorySortState? cached)
                && cached.Signature.Equals(signature))
            {
                cached.Apply(pieces);
                CacheLabels(cached.SortedPieces, category);
                return false;
            }

            var entries = new List<HammerSortEntry>(pieces.Count);
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                HammerPieceMetadata metadata = GetMetadata(piece, category);
                entries.Add(metadata.ToSortEntry(piece, i));
                LabelsByPieceCategory[CacheKey(piece.GetInstanceID(), category)] = metadata.Group.Label;
            }

            entries.Sort((left, right) =>
            {
                if (category == Piece.PieceCategory.Crafting)
                {
                    if (left.Action != right.Action) return left.Action ? 1 : -1;
                    if (!left.Action)
                    {
                        int layoutComparison = left.CraftingLayout.Section.CompareTo(right.CraftingLayout.Section);
                        if (layoutComparison != 0) return layoutComparison;
                        layoutComparison = left.CraftingLayout.SortOrder.CompareTo(right.CraftingLayout.SortOrder);
                        if (layoutComparison != 0) return layoutComparison;
                    }

                    int nameComparison = string.Compare(
                        left.LocalizedName,
                        right.LocalizedName,
                        StringComparison.CurrentCultureIgnoreCase);
                    if (nameComparison != 0) return nameComparison;
                    return left.OriginalIndex.CompareTo(right.OriginalIndex);
                }

                if (left.Action != right.Action) return left.Action ? 1 : -1;
                if (left.Action) return left.OriginalIndex.CompareTo(right.OriginalIndex);

                int comparison = left.Group.Order.CompareTo(right.Group.Order);
                if (comparison != 0) return comparison;

                if (category == Piece.PieceCategory.BuildingWorkbench
                    || category == Piece.PieceCategory.BuildingStonecutter
                    || category == Piece.PieceCategory.Furniture
                    || category == Piece.PieceCategory.Misc)
                {
                    comparison = left.Group.Suborder.CompareTo(right.Group.Suborder);
                    if (comparison != 0) return comparison;
                }

                comparison = left.Progression.CompareTo(right.Progression);
                if (comparison != 0) return comparison;
                return left.OriginalIndex.CompareTo(right.OriginalIndex);
            });

            for (int i = 0; i < entries.Count; i++)
            {
                pieces[i] = entries[i].Piece;
            }

            var sortedPieces = new Piece[entries.Count];
            for (int i = 0; i < entries.Count; i++) sortedPieces[i] = entries[i].Piece;
            SortStateByTableCategory[stateKey] = new CategorySortState(signature, sortedPieces);
            HammerGroupDecorations.NotifyPiecesChanged(category);
            return true;
        }

        private static HammerPieceMetadata GetMetadata(Piece piece, Piece.PieceCategory category)
        {
            long key = CacheKey(piece.GetInstanceID(), category);
            if (MetadataByPieceCategory.TryGetValue(key, out HammerPieceMetadata cached)
                && ReferenceEquals(cached.Piece, piece))
            {
                return cached;
            }

            PieceGroup group = ClassifyUncached(piece, category);
            bool action = category == Piece.PieceCategory.Crafting
                ? piece.m_repairPiece || piece.m_removePiece
                : CraftingLayoutMetadata.IsRepair(piece) || piece.m_removePiece;
            CraftingPieceLayout craftingLayout = default;
            HammerSortKey progression = default;
            string localizedName = string.Empty;

            if (category == Piece.PieceCategory.Crafting)
            {
                LabelsByPieceCategory[key] = group.Label;
                if (!action) craftingLayout = CraftingLayoutMetadata.Resolve(piece);
                localizedName = Localize(piece.m_name);
            }
            else if (!action)
            {
                progression = HammerProgressionSorter.Create(
                    ToSortCategory(category),
                    SearchText(piece),
                    ResourceIdentifiers(piece));
            }

            var metadata = new HammerPieceMetadata(
                piece,
                action,
                group,
                craftingLayout,
                progression,
                localizedName);
            MetadataByPieceCategory[key] = metadata;
            return metadata;
        }

        private static void CacheLabels(IReadOnlyList<Piece> pieces, Piece.PieceCategory category)
        {
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                if (piece == null) continue;
                LabelsByPieceCategory[CacheKey(piece.GetInstanceID(), category)] =
                    GetMetadata(piece, category).Group.Label;
            }
        }

        private static long CacheKey(int ownerInstanceId, Piece.PieceCategory category)
            => ((long)ownerInstanceId << 32) ^ (uint)(int)category;

        private static PieceGroup ClassifyCrafting(Piece piece)
        {
            if (piece.m_repairPiece || piece.m_removePiece)
            {
                return new PieceGroup("Actions", 800, 0);
            }

            CraftingStation station = piece.GetComponent<CraftingStation>();
            StationExtension extension = piece.GetComponent<StationExtension>();
            string stationKey;
            int suborder;

            if (station != null)
            {
                stationKey = station.m_name;
                suborder = 0;
            }
            else if (extension != null && extension.m_craftingStation != null)
            {
                stationKey = extension.m_craftingStation.m_name;
                suborder = GetExtensionOrder(piece);
            }
            else
            {
                return new PieceGroup("Other Crafting", 900, 900);
            }

            return new PieceGroup(Localize(stationKey), StationOrder(stationKey), suborder);
        }

        private static PieceGroup ClassifyMisc(Piece piece)
        {
            if (piece.m_repairPiece || piece.m_removePiece)
                return new PieceGroup("Actions", 900, 0);

            string id = SearchText(piece);
            string components = ComponentNames(piece);
            if (ContainsAny(id, "cartography", "maptable"))
                return new PieceGroup("Utility", 5, 0);
            int travelOrder = HammerProgressionSorter.MiscTravelOrder(id);
            if (travelOrder >= 0)
                return new PieceGroup("Travel", 0, travelOrder);
            if (ContainsAny(id, "campfire", "bonfire", "firepit", "hearth", "brazier")
                || components.Contains("fireplace"))
                return new PieceGroup("Fire / Comfort", 1, 0);
            if (ContainsAny(id,
                    "trap", "stake", "spike", "palisade", "barricade",
                    "roundpolefence", "roundpole", "woodfence",
                    "shieldgenerator", "shieldgen",
                    "defence", "defense"))
                return new PieceGroup("Defense", 2, HammerProgressionSorter.MiscDefenseOrder(id));
            if (ContainsAny(id,
                    "ballista", "catapult", "batteringram", "siege",
                    "turret", "cannon"))
                return new PieceGroup("Siege", 3, 0);
            if (ContainsAny(id,
                    "resource", "stack", "pile", "woodpile", "stonepile",
                    "coalpile", "orepile", "scrappile", "coinpile",
                    "bonepile", "logstack", "haystack"))
                return new PieceGroup("Resources", 4, 0);
            return new PieceGroup("Utility", 5, 0);
        }

        private static PieceGroup ClassifyBuilding(Piece piece, bool heavy)
        {
            if (!heavy)
            {
                string text = SearchText(piece) + " " + ResourceText(piece);
                if (ContainsAny(text, "dvergr", "dverger"))
                    return new PieceGroup("Wood", 0, 5);
                if (ContainsAny(text, "grausten", "ashwood", "blackwood"))
                    return new PieceGroup("Ashwood", 3, StructuralOrder(piece));
                if (ContainsAny(text, "darkwood", "tar"))
                    return new PieceGroup("Darkwood", 2, StructuralOrder(piece));
                if (ContainsAny(text, "corewood", "roundlog", "logpole", "logbeam"))
                    return new PieceGroup("Core Wood", 1, StructuralOrder(piece));
                return new PieceGroup("Wood", 0, StructuralOrder(piece));
            }

            string material = MaterialGroup(piece, heavy, out int materialOrder);
            return new PieceGroup(material, materialOrder, StructuralOrder(piece));
        }

        private static PieceGroup ClassifyFurniture(Piece piece)
        {
            string id = SearchText(piece);
            string components = ComponentNames(piece);
            string label;
            int order;
            if (ContainsAny(id, "hottub", "bathtub"))
            {
                label = "Utility / Other";
                order = 6;
            }
            else if (components.Contains("container") || ContainsAny(id, "chest", "crate", "storage"))
            {
                label = "Storage";
                order = 0;
            }
            else if (ContainsAny(components, "chair", "seat") || ContainsAny(id, "chair", "bench", "stool", "throne"))
            {
                label = "Seating";
                order = 1;
            }
            else if (piece.m_comfortGroup == Piece.ComfortGroup.Table || ContainsAny(id, "table", "desk"))
            {
                label = "Tables";
                order = 2;
            }
            else if (components.Contains("bed") || piece.m_comfortGroup == Piece.ComfortGroup.Bed || id.Contains("bed"))
            {
                label = "Beds / Comfort";
                order = 3;
            }
            else if (components.Contains("fireplace") || ContainsAny(id, "torch", "sconce", "brazier", "fire", "lamp", "lantern"))
            {
                label = "Lighting";
                order = 4;
            }
            else if (components.Contains("itemstand")
                || ContainsAny(id, "itemstand", "armorstand")
                || piece.m_comfort > 0
                || ContainsAny(id, "banner", "rug", "carpet", "curtain", "decoration"))
            {
                label = "Display / Decor";
                order = 5;
            }
            else
            {
                label = "Utility / Other";
                order = 6;
            }

            int suborder = 0;
            if (string.Equals(label, "Display / Decor", StringComparison.Ordinal))
            {
                if (ContainsAny(id, "rug", "carpet")) suborder = 800;
                else if (id.Contains("banner")) suborder = 900;
            }
            return new PieceGroup(label, order, suborder);
        }

        private static string MaterialGroup(Piece piece, bool heavy, out int order)
        {
            string text = SearchText(piece) + " " + ResourceText(piece);
            if (ContainsAny(text, "grausten", "ashwood")) { order = 5; return "Grausten"; }
            if (ContainsAny(text, "blackmarble", "black marble")) { order = 4; return "Black Marble"; }
            if (ContainsAny(text, "darkwood", "tar")) { order = 2; return "Darkwood"; }
            if (ContainsAny(text, "corewood", "roundlog", "logpole", "logbeam")) { order = 1; return "Core Wood / Log"; }
            if (ContainsAny(text, "stone", "crystal")) { order = 3; return "Stone"; }
            if (ContainsAny(text, "iron", "copper", "bronze", "metal")) { order = 6; return "Metal / Structural"; }
            if (text.Contains("wood")) { order = 0; return "Wood"; }
            order = heavy ? 7 : 8;
            return heavy ? "Special / Other" : "Other";
        }

        private static int StructuralOrder(Piece piece)
        {
            string id = SearchText(piece);
            if (id.Contains("roofcross") || (id.Contains("roof") && id.Contains("cross"))) return 0;
            if (ContainsAny(id, "beam", "pole", "pillar", "column")) return 0;
            if (ContainsAny(id, "floor", "tile")) return 1;
            if (ContainsAny(id, "wall", "halfwall")) return 2;
            if (ContainsAny(id, "roof", "thatch")) return 3;
            return 4;
        }

        private static int StationOrder(string stationName)
        {
            string id = Normalize(stationName);
            if (id.Contains("workbench")) return 0;
            if (id.Contains("forge") && !id.Contains("black")) return 10;
            if (id.Contains("cauldron")) return 20;
            if (id.Contains("stonecutter")) return 30;
            if (id.Contains("artisan")) return 40;
            if (id.Contains("blackforge")) return 50;
            if (id.Contains("galdr")) return 60;
            if (id.Contains("foodpreparation") || id.Contains("preptable")) return 70;
            if (id.Contains("mead") || id.Contains("ketill") || id.Contains("kettle")) return 80;
            if (id.Contains("cooking")) return 90;
            return 100;
        }

        private static HammerSortCategory ToSortCategory(Piece.PieceCategory category)
        {
            switch (category)
            {
                case Piece.PieceCategory.Misc: return HammerSortCategory.Misc;
                case Piece.PieceCategory.BuildingWorkbench: return HammerSortCategory.Building;
                case Piece.PieceCategory.BuildingStonecutter: return HammerSortCategory.HeavyBuilding;
                case Piece.PieceCategory.Furniture: return HammerSortCategory.Furniture;
                default: return HammerSortCategory.Misc;
            }
        }

        private static string SearchText(Piece piece)
        {
            return Normalize(piece.gameObject.name + " " + piece.m_name + " " + piece.m_description);
        }

        private static string ResourceText(Piece piece)
        {
            if (piece.m_resources == null)
            {
                return string.Empty;
            }

            var values = new List<string>();
            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                ItemDrop? item = piece.m_resources[i]?.m_resItem;
                if (item != null)
                {
                    values.Add(item.gameObject.name);
                    values.Add(item.m_itemData.m_shared.m_name);
                }
            }

            return Normalize(string.Join(" ", values));
        }

        private static IReadOnlyList<string> ResourceIdentifiers(Piece piece)
        {
            var values = new List<string>();
            if (piece.m_resources == null) return values;
            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                ItemDrop? item = piece.m_resources[i]?.m_resItem;
                if (item == null) continue;
                values.Add(item.gameObject.name);
                values.Add(item.m_itemData.m_shared.m_name);
            }
            return values;
        }

        private static string ComponentNames(Piece piece)
        {
            Component[] components = piece.GetComponents<Component>();
            var names = new List<string>(components.Length);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                {
                    names.Add(components[i].GetType().Name.ToLowerInvariant());
                }
            }

            return string.Join(" ", names);
        }

        private static string Localize(string value)
        {
            return Localization.instance != null ? Localization.instance.Localize(value) : value;
        }

        private static bool ContainsAny(string value, params string[] markers)
        {
            for (int i = 0; i < markers.Length; i++)
            {
                if (value.IndexOf(markers[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string nonNullValue = value!;
            char[] buffer = new char[nonNullValue.Length];
            int cursor = 0;
            for (int i = 0; i < nonNullValue.Length; i++)
            {
                char c = nonNullValue[i];
                if (char.IsLetterOrDigit(c)) buffer[cursor++] = char.ToLower(c, CultureInfo.InvariantCulture);
            }

            return new string(buffer, 0, cursor);
        }

        private static int TrailingNumber(string value)
        {
            int end = value.Length - 1;
            while (end >= 0 && char.IsDigit(value[end])) end--;
            if (end == value.Length - 1) return -1;
            return int.TryParse(value.Substring(end + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : -1;
        }
    }

    internal readonly struct HammerPieceMetadata
    {
        internal HammerPieceMetadata(
            Piece piece,
            bool action,
            PieceGroup group,
            CraftingPieceLayout craftingLayout,
            HammerSortKey progression,
            string localizedName)
        {
            Piece = piece;
            Action = action;
            Group = group;
            CraftingLayout = craftingLayout;
            Progression = progression;
            LocalizedName = localizedName;
        }

        internal Piece Piece { get; }
        internal bool Action { get; }
        internal PieceGroup Group { get; }
        internal CraftingPieceLayout CraftingLayout { get; }
        internal HammerSortKey Progression { get; }
        internal string LocalizedName { get; }

        internal HammerSortEntry ToSortEntry(Piece piece, int originalIndex)
            => new HammerSortEntry(
                piece,
                originalIndex,
                Action,
                Group,
                CraftingLayout,
                Progression,
                LocalizedName);
    }

    internal sealed class CategorySortState
    {
        internal CategorySortState(PieceSetSignature signature, Piece[] sortedPieces)
        {
            Signature = signature;
            SortedPieces = sortedPieces;
        }

        internal PieceSetSignature Signature { get; }
        internal Piece[] SortedPieces { get; }

        internal void Apply(List<Piece> destination)
        {
            if (destination.Count != SortedPieces.Length) return;
            for (int i = 0; i < SortedPieces.Length; i++)
            {
                if (!ReferenceEquals(destination[i], SortedPieces[i]))
                {
                    for (int copy = 0; copy < SortedPieces.Length; copy++)
                    {
                        destination[copy] = SortedPieces[copy];
                    }
                    return;
                }
            }
        }
    }

    internal readonly struct PieceSetSignature : IEquatable<PieceSetSignature>
    {
        private PieceSetSignature(int count, ulong sum, ulong xor, ulong weighted)
        {
            Count = count;
            Sum = sum;
            Xor = xor;
            Weighted = weighted;
        }

        private int Count { get; }
        private ulong Sum { get; }
        private ulong Xor { get; }
        private ulong Weighted { get; }

        internal static PieceSetSignature Create(IReadOnlyList<Piece> pieces)
        {
            ulong sum = 0;
            ulong xor = 0;
            ulong weighted = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                uint value = piece == null ? 0u : unchecked((uint)piece.GetInstanceID());
                ulong mixed = Mix(value);
                sum = unchecked(sum + mixed);
                xor ^= mixed;
                weighted = unchecked(weighted + mixed * mixed);
            }
            return new PieceSetSignature(pieces.Count, sum, xor, weighted);
        }

        public bool Equals(PieceSetSignature other)
            => Count == other.Count
                && Sum == other.Sum
                && Xor == other.Xor
                && Weighted == other.Weighted;

        public override bool Equals(object? obj)
            => obj is PieceSetSignature other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Count;
                hash = hash * 397 ^ Sum.GetHashCode();
                hash = hash * 397 ^ Xor.GetHashCode();
                return hash * 397 ^ Weighted.GetHashCode();
            }
        }

        private static ulong Mix(uint value)
        {
            ulong mixed = value;
            mixed ^= mixed >> 16;
            mixed *= 0x7feb352dUL;
            mixed ^= mixed >> 15;
            mixed *= 0x846ca68bUL;
            mixed ^= mixed >> 16;
            return mixed;
        }
    }

    internal readonly struct HammerSortEntry
    {
        internal HammerSortEntry(
            Piece piece,
            int originalIndex,
            bool action,
            PieceGroup group,
            CraftingPieceLayout craftingLayout,
            HammerSortKey progression,
            string localizedName)
        {
            Piece = piece;
            OriginalIndex = originalIndex;
            Action = action;
            Group = group;
            CraftingLayout = craftingLayout;
            Progression = progression;
            LocalizedName = localizedName;
        }

        internal Piece Piece { get; }
        internal int OriginalIndex { get; }
        internal bool Action { get; }
        internal PieceGroup Group { get; }
        internal CraftingPieceLayout CraftingLayout { get; }
        internal HammerSortKey Progression { get; }
        internal string LocalizedName { get; }
    }

    internal readonly struct PieceGroup
    {
        internal PieceGroup(string label, int order, int suborder)
        {
            Label = label;
            Order = order;
            Suborder = suborder;
        }

        internal string Label { get; }
        internal int Order { get; }
        internal int Suborder { get; }
    }
}
