using HarmonyLib;
using InventoryUX.Runtime;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace InventoryUX.Patches
{
    [HarmonyPatch]
    internal static class HammerGridWidthPatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return Require(typeof(Hud), "UpdatePieceList");
            yield return Require(typeof(Hud), "GetSelectedGrid");
            yield return Require(typeof(PieceTable), nameof(PieceTable.GetPiece),
                typeof(Piece.PieceCategory), typeof(Vector2Int));
            yield return Require(typeof(PieceTable), nameof(PieceTable.GetPieceIndex));
            yield return Require(typeof(PieceTable), nameof(PieceTable.LeftPiece));
            yield return Require(typeof(PieceTable), nameof(PieceTable.RightPiece));
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            bool leftWrap = original.DeclaringType == typeof(PieceTable)
                && original.Name == nameof(PieceTable.LeftPiece);
            int nativeValue = leftWrap
                ? HammerGridDimensions.NativeWidth - 1
                : HammerGridDimensions.NativeWidth;
            MethodInfo replacement = AccessTools.PropertyGetter(typeof(HammerGridDimensions),
                leftWrap ? nameof(HammerGridDimensions.MaxX) : nameof(HammerGridDimensions.Width));
            return GridConstantRewriter.Replace(instructions, original, nativeValue, replacement);
        }

        private static MethodBase Require(Type type, string name, params Type[]? parameters)
            => (parameters == null || parameters.Length == 0
                    ? AccessTools.Method(type, name)
                    : AccessTools.Method(type, name, parameters))
                ?? throw new MissingMethodException(type.FullName, name);
    }

    [HarmonyPatch]
    internal static class HammerGridHeightPatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return Require(typeof(Hud), "UpdatePieceList");
            yield return Require(typeof(Hud), "GetSelectedGrid");
            yield return Require(typeof(PieceTable), nameof(PieceTable.UpPiece));
            yield return Require(typeof(PieceTable), nameof(PieceTable.DownPiece));
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original)
        {
            bool upWrap = original.DeclaringType == typeof(PieceTable)
                && original.Name == nameof(PieceTable.UpPiece);
            int nativeValue = upWrap
                ? HammerGridDimensions.NativeHeight - 1
                : HammerGridDimensions.NativeHeight;
            MethodInfo replacement = AccessTools.PropertyGetter(typeof(HammerGridDimensions),
                upWrap ? nameof(HammerGridDimensions.MaxY) : nameof(HammerGridDimensions.Height));
            return GridConstantRewriter.Replace(instructions, original, nativeValue, replacement);
        }

        private static MethodBase Require(Type type, string name)
            => AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
    }

    internal static class GridConstantRewriter
    {
        internal static IEnumerable<CodeInstruction> Replace(
            IEnumerable<CodeInstruction> instructions,
            MethodBase original,
            int nativeValue,
            MethodInfo replacement)
        {
            var rewritten = new List<CodeInstruction>();
            int replacements = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                if (LoadsInt32(instruction, nativeValue))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                    replacements++;
                }
                rewritten.Add(instruction);
            }

            if (replacements == 0)
            {
                throw new InvalidOperationException(
                    $"No {nativeValue} grid constant found in {original.DeclaringType?.FullName}.{original.Name}.");
            }
            return rewritten;
        }

        private static bool LoadsInt32(CodeInstruction instruction, int expected)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_M1) return expected == -1;
            if (instruction.opcode == OpCodes.Ldc_I4_0) return expected == 0;
            if (instruction.opcode == OpCodes.Ldc_I4_1) return expected == 1;
            if (instruction.opcode == OpCodes.Ldc_I4_2) return expected == 2;
            if (instruction.opcode == OpCodes.Ldc_I4_3) return expected == 3;
            if (instruction.opcode == OpCodes.Ldc_I4_4) return expected == 4;
            if (instruction.opcode == OpCodes.Ldc_I4_5) return expected == 5;
            if (instruction.opcode == OpCodes.Ldc_I4_6) return expected == 6;
            if (instruction.opcode == OpCodes.Ldc_I4_7) return expected == 7;
            if (instruction.opcode == OpCodes.Ldc_I4_8) return expected == 8;
            if (instruction.opcode != OpCodes.Ldc_I4 && instruction.opcode != OpCodes.Ldc_I4_S) return false;
            return instruction.operand != null && Convert.ToInt32(instruction.operand) == expected;
        }
    }
}
