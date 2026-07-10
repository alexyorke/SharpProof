using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Rules;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static PurityEvidence CreateUnsupportedOperationEvidence(IOperation operation)
    {
        return IsUnsafePointerOperation(operation)
            ? PurityEvidence.Create("unsafe_pointer", "UnsupportedOperation", operation)
            : PurityEvidence.Create("unsupported_operation", "UnsupportedOperation", operation);
    }

    private static bool IsUnsafePointerOperation(IOperation operation)
    {
        var operationKind = operation.Kind.ToString();
        var typeKind = operation.Type?.TypeKind.ToString() ?? string.Empty;

        return operationKind.IndexOf("Pointer", StringComparison.OrdinalIgnoreCase) >= 0 ||
               operationKind.Equals("AddressOf", StringComparison.Ordinal) ||
               operationKind.Equals("Fixed", StringComparison.Ordinal) ||
               operationKind.Equals("SizeOf", StringComparison.Ordinal) ||
               operationKind.Equals("StackAlloc", StringComparison.Ordinal) ||
               typeKind.IndexOf("Pointer", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static PurityAnalysisResult ImpureResult(SyntaxNode? syntaxNode, PurityEvidence evidence = default)
    {
        if (syntaxNode != null)
            return evidence.IsEmpty
                ? PurityAnalysisResult.Impure(syntaxNode)
                : PurityAnalysisResult.Impure(syntaxNode, evidence);

        return evidence.IsEmpty
            ? PurityAnalysisResult.ImpureUnknownLocation
            : PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(evidence);
    }

    internal static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeSymbol)
    {
        if (attributeSymbol == null) return false;
        return GetAttributesIncludingAssociatedSymbol(symbol).Any(ad =>
            SymbolEqualityComparer.Default.Equals(ad.AttributeClass?.OriginalDefinition,
                attributeSymbol.OriginalDefinition));
    }


    internal static PurityAnalysisResult CheckStaticConstructorPurity(ITypeSymbol? typeSymbol,
        PurityAnalysisContext context, PurityAnalysisState currentState)
    {
        if (typeSymbol == null) return PurityAnalysisResult.Pure;


        var staticConstructor = typeSymbol.GetMembers(".cctor").OfType<IMethodSymbol>().FirstOrDefault();

        if (staticConstructor == null) return PurityAnalysisResult.Pure;


        var cctorResult = GetCalleePurity(staticConstructor, context);


        return cctorResult.IsPure
            ? PurityAnalysisResult.Pure
            : PurityAnalysisResult.Impure(
                cctorResult.ImpureSyntaxNode ??
                typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken) ??
                context.ContainingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                    ?.GetSyntax(context.CancellationToken) ??
                throw new InvalidOperationException("Cannot find syntax node for static constructor impurity"),
                cctorResult.Evidence);
    }
}