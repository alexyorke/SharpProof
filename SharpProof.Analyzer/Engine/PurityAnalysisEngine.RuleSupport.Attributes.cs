using Microsoft.CodeAnalysis;
using SharpProof.Analyzer.Engine.Rules;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    internal static string? GetKnownImpureMemberSource(ISymbol symbol)
    {
        return ImpurityCatalog.GetKnownImpureMemberSource(symbol);
    }

    internal static bool IsKnownMutableCollectionBoundaryType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType ||
            namedType.IsValueType ||
            namedType.TypeKind == TypeKind.Delegate ||
            namedType.SpecialType == SpecialType.System_String)
            return false;

        return namedType.OriginalDefinition.ToDisplayString() is
            "System.Collections.Generic.List<T>" or
            "System.Collections.Generic.HashSet<T>" or
            "System.Collections.Generic.Dictionary<TKey, TValue>";
    }

    internal static PurityAnalysisResult GetCalleePurity(
        IMethodSymbol methodSymbol,
        PurityAnalysisContext context)
    {
        PurityAnalysisResult result;
        if (context.PurityService != null)
            result = context.PurityService.GetPurity(
                methodSymbol.OriginalDefinition,
                context.SemanticModel,
                context.EnforcePureAttributeSymbol,
                context.AllowSynchronizationAttributeSymbol,
                context.CancellationToken);
        else
            result = DeterminePurityRecursiveInternal(
                methodSymbol.OriginalDefinition,
                context.SemanticModel,
                context.EnforcePureAttributeSymbol,
                context.AllowSynchronizationAttributeSymbol,
                context.VisitedMethods,
                context.PurityCache,
                context.SmtAnalysis,
                context.AttributePolicy,
                context.CancellationToken,
                context.PurityService);

        return IsRecursivePlaceholderImpurity(result)
            ? result.WithEvidence(
                result.Evidence.WithSymbol(context.ContainingMethodSymbol.ToDisplayString(_signatureFormat)))
            : result;
    }
}
