using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static bool TryLowerIntegralMathMinMaxInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (!method.IsStatic ||
            method.Parameters.Length != 2 ||
            !IsIntegerSmtType(method.ReturnType) ||
            method.Parameters.Any(parameter => !IsIntegerSmtType(parameter.Type)) ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation operation ||
            !TryLowerIntegralMathArgument(operation, 0, context, out var left) ||
            !TryLowerIntegralMathArgument(operation, 1, context, out var right))
            return false;

        var comparisonOperator = method.Name == nameof(Math.Min)
            ? SymbolicRelationOperator.LessThanOrEqual
            : SymbolicRelationOperator.GreaterThanOrEqual;
        var comparison = CreateRelationCondition(
            comparisonOperator,
            left,
            right,
            invocation,
            "ir.known-api.math." + method.Name.ToLowerInvariant());
        term = new SymbolicConditionalTerm(comparison, left, right);
        return true;
    }

    private static bool TryLowerIntegralMathArgument(
        IInvocationOperation operation,
        int parameterIndex,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        return parameterIndex >= 0 &&
               parameterIndex < operation.TargetMethod.Parameters.Length &&
               IsIntegerSmtType(operation.TargetMethod.Parameters[parameterIndex].Type) &&
               SymbolicValueFacts.TryGetInvocationArgumentExpression(
                   operation,
                   parameterIndex,
                   out var argumentExpression) &&
               TryLowerTerm(argumentExpression, context, out term) &&
               term.Kind == SearchLib.Smt.SmtValueKind.Int;
    }

    private static bool TryLowerBigIntegerStaticValueMember(ISymbol? memberSymbol, out SymbolicTerm term)
    {
        if (memberSymbol is IPropertySymbol property &&
            IsBigIntegerType(property.Type))
        {
            if (string.Equals(property.Name, "Zero", StringComparison.Ordinal))
            {
                term = new SymbolicIntegerConstantTerm(0);
                return true;
            }

            if (string.Equals(property.Name, "One", StringComparison.Ordinal))
            {
                term = new SymbolicIntegerConstantTerm(1);
                return true;
            }

            if (string.Equals(property.Name, "MinusOne", StringComparison.Ordinal))
            {
                term = new SymbolicIntegerConstantTerm(-1);
                return true;
            }
        }

        term = null!;
        return false;
    }

    private static bool IsBigIntegerType(ITypeSymbol type)
    {
        return string.Equals(type.ContainingNamespace?.ToDisplayString(), "System.Numerics",
                   StringComparison.Ordinal) &&
               string.Equals(type.Name, "BigInteger", StringComparison.Ordinal);
    }
}
