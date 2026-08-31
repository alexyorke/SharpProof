using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Effects;

internal static class ConditionalTruthOperatorFacts
{
    internal static IMethodSymbol? Resolve(IBinaryOperation binary)
    {
        if (binary.OperatorMethod is not { } binaryOperator ||
            binary.OperatorKind is not (
                BinaryOperatorKind.ConditionalAnd or
                BinaryOperatorKind.ConditionalOr) ||
            binaryOperator.Parameters.Length == 0)
        {
            return null;
        }

        var name = binary.OperatorKind == BinaryOperatorKind.ConditionalAnd
            ? "op_False"
            : "op_True";
        var operandType = binaryOperator.Parameters[0].Type;
        var candidates = binaryOperator.ContainingType
            .GetMembers(name)
            .OfType<IMethodSymbol>()
            .Where(method =>
                method.MethodKind == MethodKind.UserDefinedOperator &&
                method.IsStatic &&
                method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                method.Parameters.Length == 1 &&
                SymbolEqualityComparer.Default.Equals(
                    method.Parameters[0].Type,
                    operandType))
            .Take(2)
            .ToImmutableArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }
}
