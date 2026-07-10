using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static bool IsSystemRangeType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsSystemRangeType(typeSymbol);
    }

    private static bool IsNullableValueAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicTypeFacts.IsNullableValueAccess(memberAccess, semanticModel, cancellationToken);
    }

    private static bool IsNullableType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsNullableType(typeSymbol);
    }

    private static bool IsNonNullableValueType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol?.IsValueType == true &&
               !IsNullableType(typeSymbol);
    }

    private static bool IsReferenceType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsReferenceType(typeSymbol);
    }

    private static bool IsReferenceLikeType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsReferenceLikeType(typeSymbol);
    }

    private static bool IsDynamicExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return SymbolicTypeFacts.IsDynamicExpression(
            expression,
            semanticModel,
            cancellationToken,
            UnwrapFactExpression);
    }
}