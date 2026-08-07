using System;
using System.Collections.Generic;
using System.Globalization;

namespace InventoryUX.Runtime
{
    internal enum CraftingSection
    {
        Workbench,
        Forge,
        Cooking,
        Advanced,
        Processing,
        Utility
    }

    internal readonly struct CraftingPieceLayout
    {
        internal CraftingPieceLayout(
            CraftingSection section,
            int sortOrder,
            int subgroup,
            bool isPrimaryStation = false)
        {
            Section = section;
            SortOrder = sortOrder;
            Subgroup = subgroup;
            IsPrimaryStation = isPrimaryStation;
        }

        internal CraftingSection Section { get; }
        internal int SortOrder { get; }
        internal int Subgroup { get; }
        internal bool IsPrimaryStation { get; }
    }

    /// <summary>
    /// Structured metadata for the approved Crafting-tab layout. The rules only
    /// classify pieces already exposed by Valheim's available-piece list, so this
    /// layer cannot reveal locked or undiscovered content.
    /// </summary>
    internal static class CraftingLayoutMetadata
    {
        private static readonly PieceRule[] Rules =
        {
            // Workbench family.
            Rule(CraftingSection.Workbench, 110, 0, "choppingblock"),
            Rule(CraftingSection.Workbench, 120, 0, "tanningrack"),
            Rule(CraftingSection.Workbench, 130, 0, "adze"),
            Rule(CraftingSection.Workbench, 140, 0, "toolshelf"),
            Rule(CraftingSection.Workbench, 100, 0, true, "workbench"),

            // Forge family.
            Rule(CraftingSection.Forge, 210, 0, "forgebellows", "bellows"),
            Rule(CraftingSection.Forge, 220, 0, "anvils"),
            Rule(CraftingSection.Forge, 230, 0, "grindingwheel"),
            Rule(CraftingSection.Forge, 240, 0, "smithsanvil"),
            Rule(CraftingSection.Forge, 250, 0, "forgecooler"),
            Rule(CraftingSection.Forge, 260, 0, "forgetoolrack", "toolrack"),
            Rule(CraftingSection.Forge, 100, 0, true, "forge"),

            // Cooking workflow.
            Rule(CraftingSection.Cooking, 110, 0, "spicerack"),
            Rule(CraftingSection.Cooking, 120, 0, "butcherstable", "butchertable"),
            Rule(CraftingSection.Cooking, 130, 0, "potsandpans"),
            Rule(CraftingSection.Cooking, 140, 0, "mortarandpestle", "mortar"),
            Rule(CraftingSection.Cooking, 150, 0, "rollingpin", "cuttingboard"),
            // Mead Ketill is associated with the Cauldron in live data, so these
            // specific rules must precede the broad Cauldron station fallback.
            Rule(CraftingSection.Cooking, 400, 3, true, "meadketill", "meadkettle", "ketill", "kettle"),
            Rule(CraftingSection.Cooking, 410, 3, "fermenter", "fermentationbarrel"),
            Rule(CraftingSection.Cooking, 100, 0, true, "cauldron"),
            Rule(CraftingSection.Cooking, 210, 1, "ironcookingstation", "cookingstationiron"),
            Rule(CraftingSection.Cooking, 200, 1, true, "cookingstation"),
            Rule(CraftingSection.Cooking, 300, 2, true, "foodpreparation", "foodprep", "preptable"),
            Rule(CraftingSection.Cooking, 310, 2, "stoneoven", "oven"),

            // Advanced non-magic crafting.
            Rule(CraftingSection.Advanced, 110, 0, "artisanpress"),
            Rule(CraftingSection.Advanced, 100, 0, true, "artisantable", "artisan"),
            Rule(CraftingSection.Advanced, 210, 1, "blackforgecooler"),
            Rule(CraftingSection.Advanced, 200, 1, true, "blackforge"),
            Rule(CraftingSection.Advanced, 300, 2, "gemcutter"),
            Rule(CraftingSection.Advanced, 310, 2, "metalcutter"),
            Rule(CraftingSection.Advanced, 320, 2, "vice", "vise"),

            // Galdr crafting forms the final divided workflow inside Advanced.
            Rule(CraftingSection.Advanced, 410, 3, "unfadingcandles"),
            Rule(CraftingSection.Advanced, 420, 3, "featherywreath", "wreath"),
            Rule(CraftingSection.Advanced, 430, 3, "runetable"),
            Rule(CraftingSection.Advanced, 400, 3, true, "galdrtable", "galdr"),

            // Resource processing.
            Rule(CraftingSection.Processing, 100, 0, "charcoalkiln", "kiln"),
            Rule(CraftingSection.Processing, 110, 0, "smelter"),
            Rule(CraftingSection.Processing, 120, 0, "blastfurnace"),
            Rule(CraftingSection.Processing, 130, 0, "windmill"),
            Rule(CraftingSection.Processing, 140, 0, "spinningwheel"),
            Rule(CraftingSection.Processing, 150, 0, "sapextractor", "sapcollector"),
            Rule(CraftingSection.Processing, 160, 0, "obliterator", "incinerator"),
            Rule(CraftingSection.Processing, 170, 0, "eitrrefinery", "refineryeitr"),

            // Utility / safe fallback row.
            Rule(CraftingSection.Utility, 100, 0, "stonecutter"),
            Rule(CraftingSection.Utility, 110, 0, "beehive"),
            Rule(CraftingSection.Utility, 120, 0, "wispfountain")
        };

        internal static CraftingPieceLayout Resolve(Piece piece)
        {
            string label = Normalize(HammerOrganizer.GetLabel(piece));
            string localizedName = Localization.instance != null
                ? Localization.instance.Localize(piece.m_name)
                : piece.m_name;
            string id = Normalize(label + " " + piece.gameObject.name + " " + piece.m_name + " " + localizedName);

            // Eitr Refinery is resource-processing equipment even if another mod
            // exposes it through a Galdr-related station association.
            if (ContainsAny(id, "eitrrefinery", "refineryeitr"))
                return ResolveWithin(id, CraftingSection.Processing, 170, 0);

            // A real station/extension relationship is stronger evidence than an
            // English-looking prefab fragment. This also keeps modded upgrades
            // beside the station they extend.
            if (label.Contains("workbench"))
                return ResolveWithin(id, CraftingSection.Workbench, 900, 1);
            if (label.Contains("artisan"))
                return ResolveWithin(id, CraftingSection.Advanced, 900, 0);
            if (label.Contains("blackforge"))
                return ResolveWithin(id, CraftingSection.Advanced, 900, 1);
            if (label.Contains("galdr"))
                return ResolveWithin(id, CraftingSection.Advanced, 900, 3);
            if (label.Contains("forge"))
                return ResolveWithin(id, CraftingSection.Forge, 900, 1);
            if (ContainsAny(label, "cauldron", "foodpreparation", "foodprep", "preptable", "cooking"))
                return ResolveWithin(id, CraftingSection.Cooking, 900, 3);
            if (ContainsAny(label, "mead", "ketill", "kettle", "brewing"))
                return ResolveWithin(id, CraftingSection.Cooking, 900, 3);

            for (int i = 0; i < Rules.Length; i++)
            {
                if (Rules[i].Matches(id)) return Rules[i].Layout;
            }

            // Unknown unlocked pieces remain accessible. The approved Utility
            // row doubles as the safe catch-all without creating a spoiler row.
            return new CraftingPieceLayout(CraftingSection.Utility, 900, 9);
        }

        internal static string Label(CraftingSection section)
        {
            switch (section)
            {
                case CraftingSection.Workbench: return "WORKBENCH";
                case CraftingSection.Forge: return "FORGE";
                case CraftingSection.Cooking: return "COOKING";
                case CraftingSection.Advanced: return "ADVANCED";
                case CraftingSection.Processing: return "PROCESSING";
                default: return "UTILITY";
            }
        }

        internal static bool IsRepair(Piece piece)
        {
            if (piece.m_repairPiece) return true;

            // A few Hammer-table mods clone Valheim's Repair entry without
            // preserving m_repairPiece. Keep the static Repair rail reliable by
            // accepting only the exact Repair action identifiers as a fallback.
            string prefabId = Normalize(piece.gameObject.name);
            string nameId = Normalize(piece.m_name);
            return prefabId == "repair"
                || prefabId == "piecerepair"
                || prefabId == "hammerrepair"
                || prefabId == "repairpiece"
                || nameId == "repair"
                || nameId == "piecerepair";
        }

        private static CraftingPieceLayout ResolveWithin(
            string id,
            CraftingSection section,
            int fallbackOrder,
            int fallbackSubgroup)
        {
            for (int i = 0; i < Rules.Length; i++)
            {
                PieceRule rule = Rules[i];
                if (rule.Layout.Section == section && rule.Matches(id)) return rule.Layout;
            }
            return new CraftingPieceLayout(section, fallbackOrder, fallbackSubgroup);
        }

        private static PieceRule Rule(
            CraftingSection section,
            int order,
            int subgroup,
            params string[] markers)
            => new PieceRule(new CraftingPieceLayout(section, order, subgroup), markers);

        private static PieceRule Rule(
            CraftingSection section,
            int order,
            int subgroup,
            bool primary,
            params string[] markers)
            => new PieceRule(new CraftingPieceLayout(section, order, subgroup, primary), markers);

        private static bool ContainsAny(string value, params string[] markers)
        {
            for (int i = 0; i < markers.Length; i++)
            {
                if (value.Contains(markers[i])) return true;
            }
            return false;
        }

        private static string Normalize(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string source = value!;
            char[] buffer = new char[source.Length];
            int cursor = 0;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (char.IsLetterOrDigit(c)) buffer[cursor++] = char.ToLower(c, CultureInfo.InvariantCulture);
            }
            return new string(buffer, 0, cursor);
        }

        private readonly struct PieceRule
        {
            internal PieceRule(CraftingPieceLayout layout, string[] markers)
            {
                Layout = layout;
                Markers = markers;
            }

            internal CraftingPieceLayout Layout { get; }
            private string[] Markers { get; }

            internal bool Matches(string id)
            {
                for (int i = 0; i < Markers.Length; i++)
                {
                    if (id.Contains(Markers[i])) return true;
                }
                return false;
            }
        }
    }
}
