using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;

using static SharpProof.Analyzer.ExceptionFlowAnalyzer;

namespace SharpProof.Analyzer;

internal static partial class ExceptionSiteClassifier
{
    internal static bool IsReferenceType(ITypeSymbol? typeSymbol)
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
