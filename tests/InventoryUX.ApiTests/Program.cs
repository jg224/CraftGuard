using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.IO;
using System.Linq;

namespace InventoryUX.ApiTests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string assemblyPath = args.Length > 0
                ? args[0]
                : @"C:\ValheimServer\server\valheim_server_Data\Managed\assembly_valheim.dll";

            try
            {
                if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Valheim assembly not found", assemblyPath);
                using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
                ModuleDefinition module = assembly.MainModule;

                RequireField(module, "PieceTable", "m_availablePieces");
                RequireMethod(module, "PieceTable", "UpdateAvailable", 4);
                RequireMethod(module, "PieceTable", "GetPiece", 2);
                RequireMethod(module, "PieceTable", "GetPieceIndex", 3);
                RequireMethod(module, "PieceTable", "LeftPiece", 0);
                RequireMethod(module, "PieceTable", "RightPiece", 0);
                RequireMethod(module, "PieceTable", "UpPiece", 0);
                RequireMethod(module, "PieceTable", "DownPiece", 0);
                RequireField(module, "Hud", "m_pieceIcons");
                RequireField(module, "Hud", "m_pieceListRoot");
                RequireField(module, "Hud", "m_pieceIconSpacing");
                RequireMethod(module, "Hud", "UpdatePieceList", 4);
                RequireMethod(module, "Hud", "GetSelectedGrid", 1);
                RequireMethod(module, "Hud", "OnDestroy", 0);
                RequireIntConstant(module, "Hud", "UpdatePieceList", 4, 15);
                RequireIntConstant(module, "Hud", "UpdatePieceList", 4, 6);
                RequireIntConstant(module, "Hud", "GetSelectedGrid", 1, 15);
                RequireIntConstant(module, "Hud", "GetSelectedGrid", 1, 6);
                RequireIntConstant(module, "PieceTable", "GetPiece", 2, 15);
                RequireIntConstant(module, "PieceTable", "GetPieceIndex", 3, 15);
                RequireIntConstant(module, "PieceTable", "LeftPiece", 0, 14);
                RequireIntConstant(module, "PieceTable", "RightPiece", 0, 15);
                RequireIntConstant(module, "PieceTable", "UpPiece", 0, 5);
                RequireIntConstant(module, "PieceTable", "DownPiece", 0, 6);
                RequireField(module, "InventoryGui", "m_availableRecipes");
                RequireField(module, "InventoryGui", "m_recipeListScroll");
                RequireField(module, "InventoryGui", "m_recipeEnsureVisible");
                RequireFieldType(module, "InventoryGui", "m_selectedRecipe", "RecipeDataPair");
                RequireMethod(module, "InventoryGui", "UpdateRecipeList", 1);
                RequireMethod(module, "InventoryGui", "GetSelectedRecipeIndex", 1);
                RequireMethod(module, "InventoryGui", "SetRecipe", 2);
                RequireMethod(module, "InventoryGui", "OnTabCraftPressed", 0);
                RequireMethod(module, "InventoryGui", "OnTabUpgradePressed", 0);
                RequireMethod(module, "InventoryGui", "OnDestroy", 0);
                RequireProperty(module, "InventoryGui/RecipeDataPair", "Recipe");
                RequireProperty(module, "InventoryGui/RecipeDataPair", "InterfaceElement");
                RequireMethod(module, "Player", "IsRecipeKnown", 1);
                RequireField(module, "Player", "m_buildPieces");
                RequireMethod(module, "Player", "UpdateAvailablePiecesList", 0);
                RequireMethod(module, "Player", "IsTeleporting", 0);
                RequireMethod(module, "Player", "TakeInput", 0);
                RequireMethod(module, "PlayerController", "TakeInput", 1);
                RequireMethod(module, "Game", "IsShuttingDown", 0);
                RequireField(module, "ItemDrop/ItemData/SharedData", "m_food");
                RequireField(module, "ItemDrop/ItemData/SharedData", "m_foodStamina");
                RequireField(module, "ItemDrop/ItemData/SharedData", "m_foodEitr");
                RequireField(module, "CookingStation", "m_conversion");
                RequireField(module, "CookingStation/ItemConversion", "m_from");
                RequireField(module, "CookingStation/ItemConversion", "m_to");
                RequireField(module, "Feast", "m_foodItem");
                RequireField(module, "Piece", "m_icon");
                RequireField(module, "Piece", "m_resources");
                RequireField(module, "ObjectDB", "m_recipes");
                RequireMethod(module, "ObjectDB", "Awake", 0);
                RequireField(module, "ZNetScene", "m_prefabs");
                RequireMethod(module, "ZNetScene", "Awake", 0);
                RequireMethod(module, "ZNetScene", "OnDestroy", 0);
                RequireEnumValue(module, "Piece/PieceCategory", "Misc", 0);
                RequireEnumValue(module, "Piece/PieceCategory", "Crafting", 1);
                RequireEnumValue(module, "Piece/PieceCategory", "BuildingWorkbench", 2);
                RequireEnumValue(module, "Piece/PieceCategory", "BuildingStonecutter", 3);
                RequireEnumValue(module, "Piece/PieceCategory", "Furniture", 4);
                RequireEnumValue(module, "Piece/PieceCategory", "Feasts", 5);

                Console.WriteLine("Valheim API compatibility checks passed.");
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(exception.Message);
                return 1;
            }
        }

        private static TypeDefinition RequireType(ModuleDefinition module, string name)
            => module.GetType(name) ?? throw new InvalidOperationException($"Missing Valheim type: {name}");

        private static void RequireField(ModuleDefinition module, string type, string name)
        {
            if (!RequireType(module, type).Fields.Any(field => field.Name == name))
                throw new InvalidOperationException($"Missing Valheim field: {type}.{name}");
        }

        private static void RequireFieldType(ModuleDefinition module, string type, string name, string fieldType)
        {
            FieldDefinition? field = RequireType(module, type).Fields.FirstOrDefault(candidate => candidate.Name == name);
            if (field == null)
                throw new InvalidOperationException($"Missing Valheim field: {type}.{name}");
            if (!string.Equals(field.FieldType.Name, fieldType, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Unexpected Valheim field type: {type}.{name} is {field.FieldType.Name}, expected {fieldType}");
        }

        private static void RequireMethod(ModuleDefinition module, string type, string name, int parameterCount)
        {
            if (!RequireType(module, type).Methods.Any(method => method.Name == name && method.Parameters.Count == parameterCount))
                throw new InvalidOperationException($"Missing Valheim method: {type}.{name}/{parameterCount}");
        }

        private static void RequireProperty(ModuleDefinition module, string type, string name)
        {
            if (!RequireType(module, type).Properties.Any(property => property.Name == name))
                throw new InvalidOperationException($"Missing Valheim property: {type}.{name}");
        }

        private static void RequireIntConstant(
            ModuleDefinition module,
            string type,
            string name,
            int parameterCount,
            int expected)
        {
            MethodDefinition? method = RequireType(module, type).Methods
                .FirstOrDefault(candidate => candidate.Name == name && candidate.Parameters.Count == parameterCount);
            if (method == null || !method.HasBody
                || !method.Body.Instructions.Any(instruction => LoadsInt32(instruction, expected)))
            {
                throw new InvalidOperationException(
                    $"Missing expected grid constant {expected}: {type}.{name}/{parameterCount}");
            }
        }

        private static bool LoadsInt32(Instruction instruction, int expected)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldc_I4_M1: return expected == -1;
                case Code.Ldc_I4_0: return expected == 0;
                case Code.Ldc_I4_1: return expected == 1;
                case Code.Ldc_I4_2: return expected == 2;
                case Code.Ldc_I4_3: return expected == 3;
                case Code.Ldc_I4_4: return expected == 4;
                case Code.Ldc_I4_5: return expected == 5;
                case Code.Ldc_I4_6: return expected == 6;
                case Code.Ldc_I4_7: return expected == 7;
                case Code.Ldc_I4_8: return expected == 8;
                case Code.Ldc_I4:
                case Code.Ldc_I4_S:
                    return instruction.Operand != null && Convert.ToInt32(instruction.Operand) == expected;
                default:
                    return false;
            }
        }

        private static void RequireEnumValue(ModuleDefinition module, string type, string name, int value)
        {
            FieldDefinition? field = RequireType(module, type).Fields.FirstOrDefault(candidate => candidate.Name == name);
            if (field == null || Convert.ToInt32(field.Constant) != value)
                throw new InvalidOperationException($"Unexpected Valheim enum value: {type}.{name}");
        }
    }
}
