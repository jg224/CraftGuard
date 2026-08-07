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

        private static readonly Dictionary<int, string> LabelsByPiece = new Dictionary<int, string>();

        internal static void ReorderAvailablePieces(PieceTable table)
        {
            var available = (List<List<Piece>>)AvailablePiecesField.GetValue(table);
            LabelsByPiece.Clear();

            Reorder(available, Piece.PieceCategory.Misc, true);
            Reorder(available, Piece.PieceCategory.Crafting, ModConfig.OrganizeCrafting.Value);
            Reorder(available, Piece.PieceCategory.BuildingWorkbench, ModConfig.OrganizeBuilding.Value);
            Reorder(available, Piece.PieceCategory.BuildingStonecutter, ModConfig.OrganizeHeavyBuilding.Value);
            Reorder(available, Piece.PieceCategory.Furniture, ModConfig.OrganizeFurniture.Value);
        }

        internal static string? GetLabel(Piece piece)
        {
            return piece != null && LabelsByPiece.TryGetValue(piece.GetInstanceID(), out string label) ? label : null;
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

        private static void Reorder(List<List<Piece>> all, Piece.PieceCategory category, bool enabled)
        {
            int index = (int)category;
            if (!enabled || index < 0 || index >= all.Count || all[index] == null)
            {
                return;
            }

            List<Piece> pieces = all[index];
            var originalIndex = new Dictionary<int, int>();
            for (int i = 0; i < pieces.Count; i++)
            {
                originalIndex[pieces[i].GetInstanceID()] = i;
                if (category == Piece.PieceCategory.Crafting)
                {
                    LabelsByPiece[pieces[i].GetInstanceID()] = ClassifyCrafting(pieces[i]).Label;
                }
            }

            pieces.Sort((left, right) =>
            {
                if (category == Piece.PieceCategory.Crafting)
                {
                    bool leftCraftAction = left.m_repairPiece || left.m_removePiece;
                    bool rightCraftAction = right.m_repairPiece || right.m_removePiece;
                    if (leftCraftAction != rightCraftAction) return leftCraftAction ? 1 : -1;
                    if (!leftCraftAction)
                    {
                        CraftingPieceLayout leftLayout = CraftingLayoutMetadata.Resolve(left);
                        CraftingPieceLayout rightLayout = CraftingLayoutMetadata.Resolve(right);
                        int layoutComparison = leftLayout.Section.CompareTo(rightLayout.Section);
                        if (layoutComparison != 0) return layoutComparison;
                        layoutComparison = leftLayout.SortOrder.CompareTo(rightLayout.SortOrder);
                        if (layoutComparison != 0) return layoutComparison;
                    }

                    int nameComparison = string.Compare(
                        Localize(left.m_name),
                        Localize(right.m_name),
                        StringComparison.CurrentCultureIgnoreCase);
                    if (nameComparison != 0) return nameComparison;
                    return originalIndex[left.GetInstanceID()].CompareTo(originalIndex[right.GetInstanceID()]);
                }

                bool leftRepair = CraftingLayoutMetadata.IsRepair(left) || left.m_removePiece;
                bool rightRepair = CraftingLayoutMetadata.IsRepair(right) || right.m_removePiece;
                if (leftRepair != rightRepair) return leftRepair ? 1 : -1;
                if (leftRepair) return originalIndex[left.GetInstanceID()].CompareTo(originalIndex[right.GetInstanceID()]);

                PieceGroup leftGroup = Classify(left, category);
                PieceGroup rightGroup = Classify(right, category);
                int comparison = leftGroup.Order.CompareTo(rightGroup.Order);
                if (comparison != 0) return comparison;

                if (category == Piece.PieceCategory.BuildingWorkbench
                    || category == Piece.PieceCategory.BuildingStonecutter
                    || category == Piece.PieceCategory.Furniture)
                {
                    comparison = leftGroup.Suborder.CompareTo(rightGroup.Suborder);
                    if (comparison != 0) return comparison;
                }

                HammerSortKey leftKey = HammerProgressionSorter.Create(
                    ToSortCategory(category),
                    SearchText(left),
                    ResourceIdentifiers(left));
                HammerSortKey rightKey = HammerProgressionSorter.Create(
                    ToSortCategory(category),
                    SearchText(right),
                    ResourceIdentifiers(right));
                comparison = leftKey.CompareTo(rightKey);
                if (comparison != 0) return comparison;
                return originalIndex[left.GetInstanceID()].CompareTo(originalIndex[right.GetInstanceID()]);
            });

            for (int i = 0; i < pieces.Count; i++)
            {
                LabelsByPiece[pieces[i].GetInstanceID()] = Classify(pieces[i], category).Label;
            }
        }

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
            if (ContainsAny(id,
                    "portal", "teleport", "cart", "karve", "longship",
                    "drakkar", "raft", "ship", "boat"))
                return new PieceGroup("Travel", 0, 0);
            if (ContainsAny(id, "campfire", "bonfire", "firepit", "hearth", "brazier")
                || components.Contains("fireplace"))
                return new PieceGroup("Fire / Comfort", 1, 0);
            if (ContainsAny(id,
                    "ballista", "catapult", "batteringram", "siege",
                    "turret", "cannon"))
                return new PieceGroup("Siege", 3, 0);
            if (ContainsAny(id,
                    "trap", "stake", "spike", "palisade", "barricade",
                    "roundpolefence", "shieldgenerator", "shieldgen",
                    "defence", "defense"))
                return new PieceGroup("Defense", 2, 0);
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
