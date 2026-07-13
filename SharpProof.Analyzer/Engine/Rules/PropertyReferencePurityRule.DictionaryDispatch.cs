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

        if (!TryMatchIndexerContainer(
                propertyReferenceOperation,
                out var keyType,
                "System.Collections.Generic.Dictionary<TKey, TValue>",
                "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>"))
            return false;

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

        if (!TryMatchIndexerContainer(
                propertyReferenceOperation,
                out var keyType,
                "System.Collections.Generic.SortedDictionary<TKey, TValue>",
                "System.Collections.Generic.SortedList<TKey, TValue>",
                "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>"))
            return false;

        result = CheckSortedDictionaryKeyDispatchPurity(keyType, propertyReferenceOperation, context);
        return true;
    }

    private static bool TryMatchIndexerContainer(
        IPropertyReferenceOperation propertyReferenceOperation,
        out ITypeSymbol keyType,
        params string[] typeDefinitions)
    {
        keyType = null!;
        var propertySymbol = propertyReferenceOperation.Property;
        if (!propertySymbol.IsIndexer ||
            propertySymbol.ContainingType is not INamedTypeSymbol { TypeArguments.Length: 2 } containingType ||
            propertyReferenceOperation.Arguments.IsEmpty ||
            !typeDefinitions.Contains(
                containingType.OriginalDefinition.ToDisplayString(),
                StringComparer.Ordinal))
            return false;

        keyType = containingType.TypeArguments[0];
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
        PurityAnalysisEngine.PurityAnalysisResult CheckComparerValue(IOperation value)
        {
            return ComparerDispatchHelper.CheckComparerValuePurity(
                value,
                context,
                propertyReferenceOperation.Syntax,
                propertyReferenceOperation,
                nameof(PropertyReferencePurityRule),
                null);
        }

        var knownConstructionComparerResult = ComparerDispatchHelper.CheckKnownConstructionComparerPurity(
            receiverOperation,
            context,
            IsConcreteDictionaryType,
            ComparerDispatchHelper.IsComparerOrDerivedInterface,
            CheckComparerValue);
        if (!knownConstructionComparerResult.IsPure) return knownConstructionComparerResult;

        if (receiverOperation?.Type is not INamedTypeSymbol receiverType ||
            receiverType.DeclaringSyntaxReferences.Length == 0)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return ComparerDispatchHelper.CheckSubtypeConstructorComparerPurity(
            receiverType,
            context,
            CheckComparerValue);
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
        var implementationPurity = PurityCalleeResolver.GetCalleePurity(implementation.OriginalDefinition, context);
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
