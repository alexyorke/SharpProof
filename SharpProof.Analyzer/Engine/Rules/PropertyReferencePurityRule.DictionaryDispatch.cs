using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class PropertyReferencePurityRule
{
    private static bool TryCheckDictionaryIndexerKeyDispatchPurity(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var propertySymbol = propertyReferenceOperation.Property;
        var typeDefinition = propertySymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        if (!propertySymbol.IsIndexer ||
            propertySymbol.ContainingType is not INamedTypeSymbol containingType ||
            containingType.TypeArguments.Length != 2 ||
            (typeDefinition != "System.Collections.Generic.Dictionary<TKey, TValue>" &&
             typeDefinition != "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>") ||
            propertyReferenceOperation.Arguments.Length == 0)
            return false;

        var keyType = containingType.TypeArguments[0];
        var receiverComparerResult = CheckDictionaryReceiverComparerPurity(propertyReferenceOperation, context);
        if (!receiverComparerResult.IsPure)
        {
            result = receiverComparerResult;
            return true;
        }

        result = CheckDictionaryKeyDispatchPurity(keyType, propertyReferenceOperation, context);
        return true;
    }

    private static bool TryCheckSortedDictionaryIndexerComparisonDispatchPurity(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var propertySymbol = propertyReferenceOperation.Property;
        var typeDefinition = propertySymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        if (!propertySymbol.IsIndexer ||
            propertySymbol.ContainingType is not INamedTypeSymbol containingType ||
            containingType.TypeArguments.Length != 2 ||
            (typeDefinition != "System.Collections.Generic.SortedDictionary<TKey, TValue>" &&
             typeDefinition != "System.Collections.Generic.SortedList<TKey, TValue>" &&
             typeDefinition != "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>") ||
            propertyReferenceOperation.Arguments.Length == 0)
            return false;

        var keyType = containingType.TypeArguments[0];
        result = CheckSortedDictionaryKeyDispatchPurity(keyType, propertyReferenceOperation, context);
        return true;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDictionaryKeyDispatchPurity(
        ITypeSymbol keyType,
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context)
    {
        if (ComparerDispatchHelper.IsBuiltinValueComparerKey(keyType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!DispatchedMemberResolution.TryGetObjectOverride(keyType, nameof(GetHashCode), 0,
                out var getHashCodeOverride)) return UnknownKeyDispatch(propertyReferenceOperation);

        var hashPurity = CheckResolvedKeyImplementation(getHashCodeOverride, propertyReferenceOperation, context);
        if (!hashPurity.IsPure) return hashPurity;

        if (DispatchedMemberResolution.TryGetIEquatableEqualsImplementation(keyType, out var equalsImplementation))
            return CheckResolvedKeyImplementation(equalsImplementation, propertyReferenceOperation, context);

        if (DispatchedMemberResolution.TryGetObjectOverride(keyType, nameof(object.Equals), 1,
                out var objectEqualsOverride))
            return CheckResolvedKeyImplementation(objectEqualsOverride, propertyReferenceOperation, context);

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDictionaryReceiverComparerPurity(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context)
    {
        var receiverOperation = PurityAnalysisEngine.SkipImplicitConversions(propertyReferenceOperation.Instance) ??
                                propertyReferenceOperation.Instance;
        var knownConstructionComparerResult = ComparerDispatchHelper.CheckKnownConstructionComparerPurity(
            receiverOperation,
            context,
            IsConcreteDictionaryType,
            ComparerDispatchHelper.IsComparerOrDerivedInterface,
            value => ComparerDispatchHelper.CheckComparerValuePurity(
                value,
                context,
                propertyReferenceOperation.Syntax,
                propertyReferenceOperation,
                nameof(PropertyReferencePurityRule),
                null));
        if (!knownConstructionComparerResult.IsPure) return knownConstructionComparerResult;

        if (receiverOperation?.Type is not INamedTypeSymbol receiverType ||
            receiverType.DeclaringSyntaxReferences.Length == 0)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return ComparerDispatchHelper.CheckSubtypeConstructorComparerPurity(
            receiverType,
            context,
            value => ComparerDispatchHelper.CheckComparerValuePurity(
                value,
                context,
                propertyReferenceOperation.Syntax,
                propertyReferenceOperation,
                nameof(PropertyReferencePurityRule),
                null));
    }

    private static bool IsConcreteDictionaryType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>";
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckSortedDictionaryKeyDispatchPurity(
        ITypeSymbol keyType,
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context)
    {
        if (ComparerDispatchHelper.IsBuiltinValueComparerKey(keyType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (DispatchedMemberResolution.TryGetIComparableCompareToImplementation(keyType,
                out var compareToImplementation))
            return CheckResolvedKeyImplementation(compareToImplementation, propertyReferenceOperation, context);

        if (DispatchedMemberResolution.TryGetIComparableObjectCompareToImplementation(keyType,
                out var objectCompareToImplementation))
            return CheckResolvedKeyImplementation(objectCompareToImplementation, propertyReferenceOperation, context);

        return UnknownKeyDispatch(propertyReferenceOperation);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckResolvedKeyImplementation(
        IMethodSymbol implementation,
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context)
    {
        var implementationPurity = PurityAnalysisEngine.GetCalleePurity(implementation.OriginalDefinition, context);
        return implementationPurity.IsPure
            ? PurityAnalysisEngine.PurityAnalysisResult.Pure
            : implementationPurity.WithCallee(implementation.OriginalDefinition, propertyReferenceOperation.Syntax);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult UnknownKeyDispatch(
        IPropertyReferenceOperation propertyReferenceOperation,
        ISymbol? symbol = null)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
            propertyReferenceOperation.Syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                "unknown_external_call",
                nameof(PropertyReferencePurityRule),
                propertyReferenceOperation,
                symbol: symbol ?? propertyReferenceOperation.Property.GetMethod));
    }
}
