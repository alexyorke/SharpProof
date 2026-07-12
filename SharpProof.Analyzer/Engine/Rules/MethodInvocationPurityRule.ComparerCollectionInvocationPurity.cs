using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Analysis;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    private static bool TryCheckEqualityComparerDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (!TryGetEqualityComparerElementType(methodSymbol, out var elementType)) return false;

        if (ComparerDispatchHelper.IsBuiltinValueComparerKey(elementType)) return true;

        if (methodSymbol.Name == nameof(object.Equals) && methodSymbol.Parameters.Length == 2)
        {
            if (DispatchedMemberResolution.TryGetIEquatableEqualsImplementation(elementType,
                    out var equalsImplementation))
            {
                result = CheckResolvedEqualityImplementation(
                    equalsImplementation,
                    invocationOperation,
                    context);
                return true;
            }

            if (DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(object.Equals), 1,
                    out var objectEqualsOverride))
            {
                result = CheckResolvedEqualityImplementation(
                    objectEqualsOverride,
                    invocationOperation,
                    context);
                return true;
            }
        }
        else if (methodSymbol.Name == nameof(GetHashCode) && methodSymbol.Parameters.Length == 1)
        {
            if (DispatchedMemberResolution.TryGetObjectOverride(elementType, nameof(GetHashCode), 0,
                    out var getHashCodeOverride))
            {
                result = CheckResolvedEqualityImplementation(
                    getHashCodeOverride,
                    invocationOperation,
                    context);
                return true;
            }
        }
        else
        {
            return false;
        }

        result = CreateUnknownExternalCallImpurity(invocationOperation, methodSymbol);
        return true;
    }

    private static bool TryCheckComparerDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (!TryGetComparerElementType(methodSymbol, out var elementType)) return false;

        result = CheckDefaultComparisonDispatchPurity(elementType, invocationOperation, context);
        return true;
    }

    private static bool TryCheckNullableComparisonDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        var definition = methodSymbol.OriginalDefinition;
        if (definition.ContainingType?.ToDisplayString() != "System.Nullable" ||
            definition.Name is not ("Compare" or "Equals") ||
            methodSymbol.TypeArguments.Length != 1)
            return false;

        var valueType = methodSymbol.TypeArguments[0];
        if (definition.Name == "Compare")
        {
            result = CheckDefaultComparisonDispatchPurity(valueType, invocationOperation, context);
            return true;
        }

        if (definition.Name == "Equals")
        {
            result = CheckDefaultEqualityDispatchPurity(valueType, invocationOperation, context);
            return true;
        }

        return false;
    }

    private static bool TryCheckCollectionEqualityDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (!TryGetDefaultEqualityCollectionElementType(methodSymbol, out var elementType, out var requiresHashCode))
            return false;

        var receiverComparerResult = CheckHashSetReceiverComparerPurity(invocationOperation, context);
        if (!receiverComparerResult.IsPure)
        {
            result = receiverComparerResult;
            return true;
        }

        if (IsHashSetRelationMethod(methodSymbol) &&
            invocationOperation.Arguments.Length > 0)
        {
            result = CheckLinqSourceEnumeratorPurity(invocationOperation.Arguments[0].Value, context, currentState);
            if (!result.IsPure) return true;
        }

        result = CheckDefaultEqualityDispatchPurity(elementType, invocationOperation, context, requiresHashCode);
        return true;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckHashSetReceiverComparerPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        var methodSymbol = invocationOperation.TargetMethod;
        if (methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString() !=
            "System.Collections.Generic.HashSet<T>") return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var receiverOperation = PurityAnalysisEngine.SkipImplicitConversions(invocationOperation.Instance) ??
                                invocationOperation.Instance;
        var constructionResult = ComparerDispatchHelper.CheckKnownConstructionComparerPurity(
            receiverOperation,
            context,
            IsConcreteHashSetType,
            IsEqualityComparerType,
            value => CheckComparerValuePurity(value, invocationOperation, context));
        if (!constructionResult.IsPure) return constructionResult;

        if (receiverOperation?.Type is INamedTypeSymbol receiverType)
            return CheckHashSetSubtypeConstructorComparerPurity(receiverType, invocationOperation, context);

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckHashSetSubtypeConstructorComparerPurity(
        INamedTypeSymbol receiverType,
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        if (receiverType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.HashSet<T>" ||
            !DerivesFromHashSet(receiverType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return ComparerDispatchHelper.CheckSubtypeConstructorComparerPurity(
            receiverType,
            context,
            value => CheckComparerValuePurity(value, invocationOperation, context));
    }

    private static bool DerivesFromHashSet(INamedTypeSymbol typeSymbol)
    {
        for (var baseType = typeSymbol.BaseType; baseType != null; baseType = baseType.BaseType)
            if (baseType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.HashSet<T>")
                return true;

        return false;
    }

    private static bool IsConcreteHashSetType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.HashSet<T>";
    }

    private static bool TryCheckCollectionComparisonDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (!TryGetDefaultComparisonCollectionKeyType(methodSymbol, out var keyType)) return false;

        var receiverComparerResult = CheckSortedCollectionReceiverComparerPurity(invocationOperation, context);
        if (!receiverComparerResult.IsPure)
        {
            result = receiverComparerResult;
            return true;
        }

        result = CheckDefaultComparisonDispatchPurity(keyType, invocationOperation, context);
        return true;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckSortedCollectionReceiverComparerPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        var methodSymbol = invocationOperation.TargetMethod;
        if (!IsConcreteSortedCollectionType(methodSymbol.ContainingType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var receiverOperation = PurityAnalysisEngine.SkipImplicitConversions(invocationOperation.Instance) ??
                                invocationOperation.Instance;
        var constructionResult = ComparerDispatchHelper.CheckKnownConstructionComparerPurity(
            receiverOperation,
            context,
            IsConcreteSortedCollectionType,
            IsComparerType,
            value => CheckComparerValuePurity(value, invocationOperation, context));
        if (!constructionResult.IsPure) return constructionResult;

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static bool IsConcreteSortedCollectionType(ITypeSymbol? typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType) return false;

        return namedType.OriginalDefinition.ToDisplayString() is
            "System.Collections.Generic.SortedDictionary<TKey, TValue>" or
            "System.Collections.Generic.SortedList<TKey, TValue>" or
            "System.Collections.Generic.SortedSet<T>";
    }

    private static bool TryCheckLinqDefaultEqualityDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (!TryGetLinqDefaultEqualityDispatchType(methodSymbol, out var equalityType)) return false;

        if (!IsLinqDefaultEqualityOverload(invocationOperation)) return false;

        result = CheckDefaultEqualityDispatchPurity(equalityType, invocationOperation, context);
        return true;
    }

    private static bool TryCheckLinqDefaultComparisonDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (!TryGetLinqDefaultComparisonDispatchType(methodSymbol, out var comparisonType)) return false;

        if (!IsLinqDefaultComparisonOverload(invocationOperation)) return false;

        result = CheckDefaultComparisonDispatchPurity(comparisonType, invocationOperation, context);
        return true;
    }

    private static bool TryGetLinqDefaultComparisonDispatchType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol comparisonType)
    {
        comparisonType = null!;

        var definition = GetExtensionDefinition(methodSymbol);
        if (definition.ContainingType?.OriginalDefinition.ToDisplayString() != "System.Linq.Enumerable" ||
            definition.Name is not ("OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or "Min"
                or "Max"))
            return false;

        if (definition.Name is "Min" or "Max")
        {
            if (methodSymbol.TypeArguments.Length != 1) return false;

            comparisonType = methodSymbol.TypeArguments[0];
            return true;
        }

        if (methodSymbol.TypeArguments.Length < 2) return false;

        comparisonType = methodSymbol.TypeArguments[1];
        return true;
    }

    private static bool IsLinqDefaultComparisonOverload(IInvocationOperation invocationOperation)
    {
        if (TryGetComparerArgument(invocationOperation, out var comparerArgument))
            return ComparerDispatchHelper.IsNullOrDefaultComparerArgument(comparerArgument);

        return true;
    }

    private static bool TryGetLinqDefaultEqualityDispatchType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol equalityType)
    {
        equalityType = null!;

        var definition = GetExtensionDefinition(methodSymbol);
        if (definition.ContainingType?.OriginalDefinition.ToDisplayString() != "System.Linq.Enumerable") return false;

        if (definition.Name is "GroupBy" or "ToLookup")
        {
            if (methodSymbol.TypeArguments.Length < 2) return false;

            equalityType = methodSymbol.TypeArguments[1];
            return true;
        }

        if (definition.Name is "Join" or "GroupJoin")
        {
            if (methodSymbol.TypeArguments.Length < 3) return false;

            equalityType = methodSymbol.TypeArguments[2];
            return true;
        }

        if (definition.Name is not ("Contains" or "SequenceEqual" or "Distinct" or "Except" or "Intersect"
                or "Union") ||
            methodSymbol.TypeArguments.Length != 1)
            return false;

        equalityType = methodSymbol.TypeArguments[0];
        return true;
    }

    private static bool IsLinqDefaultEqualityOverload(IInvocationOperation invocationOperation)
    {
        if (TryGetEqualityComparerArgument(invocationOperation, out var comparerArgument))
            return ComparerDispatchHelper.IsNullOrDefaultComparerArgument(comparerArgument);

        return true;
    }

    private static bool TryGetComparerArgument(
        IInvocationOperation invocationOperation,
        out IArgumentOperation comparerArgument)
    {
        return TryGetArgumentByParameterType(invocationOperation, IsComparerType, out comparerArgument);
    }

    private static bool TryGetEqualityComparerArgument(
        IInvocationOperation invocationOperation,
        out IArgumentOperation comparerArgument)
    {
        return TryGetArgumentByParameterType(invocationOperation, IsEqualityComparerType, out comparerArgument);
    }

    private static bool TryGetArgumentByParameterType(
        IInvocationOperation invocationOperation,
        Func<ITypeSymbol?, bool> matchesParameterType,
        out IArgumentOperation matchingArgument)
    {
        foreach (var argument in invocationOperation.Arguments)
            if (matchesParameterType(argument.Parameter?.Type))
            {
                matchingArgument = argument;
                return true;
            }

        matchingArgument = null!;
        return false;
    }

    private static void AddKnownInterfaceImplementation(
        INamedTypeSymbol type,
        IMethodSymbol target,
        ISet<IMethodSymbol> targets,
        CancellationToken cancellationToken)
    {
        if (!TypeHierarchyEnumeration.ImplementsInterface(type, target.ContainingType, true)) return;

        if (type.Kind == SymbolKind.NamedType &&
            (type.TypeKind == TypeKind.Interface ||
             type.TypeKind == TypeKind.Struct ||
             type.TypeKind == TypeKind.Class))
        {
            var implementation = ResolveKnownInterfaceImplementation(type, target, cancellationToken);
            if (implementation != null) targets.Add(implementation.OriginalDefinition);
        }
    }

    private static bool IsComparerType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IComparer<T>";
    }

    private static bool IsEqualityComparerType(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEqualityComparer<T>";
    }


    private static bool TryGetEqualityComparerElementType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol elementType)
    {
        elementType = null!;

        if (methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
            containingType.TypeArguments.Length != 1 ||
            containingType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.EqualityComparer<T>")
            return false;

        if ((methodSymbol.Name == nameof(object.Equals) && methodSymbol.Parameters.Length == 2) ||
            (methodSymbol.Name == nameof(GetHashCode) && methodSymbol.Parameters.Length == 1))
        {
            elementType = containingType.TypeArguments[0];
            return true;
        }

        return false;
    }

    private static bool TryGetComparerElementType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol elementType)
    {
        elementType = null!;

        if (methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
            containingType.TypeArguments.Length != 1 ||
            containingType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.Comparer<T>")
            return false;

        if (methodSymbol.Name == "Compare" && methodSymbol.Parameters.Length == 2)
        {
            elementType = containingType.TypeArguments[0];
            return true;
        }

        return false;
    }

    private static bool TryGetDefaultEqualityCollectionElementType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol elementType,
        out bool requiresHashCode)
    {
        elementType = null!;
        requiresHashCode = false;

        if (methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
            methodSymbol.Parameters.Length < 1)
            return false;

        if (containingType.SpecialType == SpecialType.System_Array &&
            methodSymbol.IsGenericMethod &&
            methodSymbol.TypeArguments.Length == 1 &&
            methodSymbol.Parameters.Length >= 2 &&
            methodSymbol.Name is "IndexOf" or "LastIndexOf")
        {
            elementType = methodSymbol.TypeArguments[0];
            return true;
        }

        var typeDefinition = containingType.OriginalDefinition.ToDisplayString();
        if (containingType.TypeArguments.Length == 2 &&
            typeDefinition == "System.Collections.Generic.Dictionary<TKey, TValue>" &&
            methodSymbol.Name is "ContainsKey" or "TryGetValue")
        {
            elementType = containingType.TypeArguments[0];
            requiresHashCode = true;
            return true;
        }

        if (containingType.TypeArguments.Length == 2 &&
            typeDefinition == "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>" &&
            methodSymbol.Name is "ContainsKey" or "TryGetValue" or "Add" or "Remove" or "SetItem")
        {
            elementType = containingType.TypeArguments[0];
            requiresHashCode = true;
            return true;
        }

        if (containingType.TypeArguments.Length == 2 &&
            (typeDefinition == "System.Collections.Generic.Dictionary<TKey, TValue>" ||
             typeDefinition == "System.Collections.Generic.SortedDictionary<TKey, TValue>") &&
            methodSymbol.Name == "ContainsValue")
        {
            elementType = containingType.TypeArguments[1];
            return true;
        }

        if (containingType.TypeArguments.Length != 1) return false;

        var usesDefaultEquality =
            typeDefinition == "System.Collections.Generic.List<T>" ||
            typeDefinition == "System.Collections.Immutable.ImmutableList<T>" ||
            typeDefinition == "System.Collections.Generic.Queue<T>" ||
            typeDefinition == "System.Collections.Generic.Stack<T>" ||
            typeDefinition == "System.Collections.Generic.HashSet<T>" ||
            typeDefinition == "System.Collections.Immutable.ImmutableHashSet<T>";
        if (!usesDefaultEquality) return false;

        var isDefaultEqualityLookup =
            methodSymbol.Name == "Contains" ||
            methodSymbol.Name == "IndexOf" ||
            methodSymbol.Name == "LastIndexOf" ||
            methodSymbol.Name == "TryGetValue";
        var isImmutableHashSetUpdate =
            typeDefinition == "System.Collections.Immutable.ImmutableHashSet<T>" &&
            methodSymbol.Name is "Add" or "Remove";
        var isImmutableListRemove =
            typeDefinition == "System.Collections.Immutable.ImmutableList<T>" &&
            methodSymbol.Name == "Remove";
        var isHashSetRelation = IsHashSetRelationMethod(methodSymbol);
        if (!isDefaultEqualityLookup && !isImmutableHashSetUpdate && !isImmutableListRemove &&
            !isHashSetRelation) return false;

        elementType = containingType.TypeArguments[0];
        requiresHashCode =
            typeDefinition == "System.Collections.Generic.HashSet<T>" ||
            typeDefinition == "System.Collections.Immutable.ImmutableHashSet<T>";
        return true;
    }

    private static bool IsHashSetRelationMethod(IMethodSymbol methodSymbol)
    {
        var typeDefinition = methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        return (typeDefinition == "System.Collections.Generic.HashSet<T>" ||
                typeDefinition == "System.Collections.Immutable.ImmutableHashSet<T>") &&
               methodSymbol.Name is "SetEquals" or "Overlaps" or "IsSubsetOf" or "IsSupersetOf" or "IsProperSubsetOf"
                   or "IsProperSupersetOf";
    }

    private static bool TryGetDefaultComparisonCollectionKeyType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol keyType)
    {
        keyType = null!;

        if (methodSymbol.ContainingType is not INamedTypeSymbol containingType ||
            methodSymbol.Name is not ("ContainsKey" or "TryGetValue" or "BinarySearch" or "SequenceCompareTo"
                or "Contains" or "Add" or "Remove" or "SetItem" or "IndexOfKey"))
            return false;

        var typeDefinition = containingType.OriginalDefinition.ToDisplayString();
        if (containingType.SpecialType == SpecialType.System_Array &&
            methodSymbol.IsGenericMethod &&
            methodSymbol.Name == "BinarySearch" &&
            methodSymbol.TypeArguments.Length == 1 &&
            methodSymbol.Parameters.Length >= 2)
        {
            keyType = methodSymbol.TypeArguments[0];
            return true;
        }

        if (typeDefinition == "System.MemoryExtensions" &&
            methodSymbol.IsGenericMethod &&
            methodSymbol.Name is "BinarySearch" or "SequenceCompareTo" &&
            methodSymbol.Parameters.Length == 2)
        {
            keyType = methodSymbol.Name == "BinarySearch"
                ? methodSymbol.Parameters[1].Type
                : methodSymbol.TypeArguments[0];
            return true;
        }

        if (containingType.TypeArguments.Length == 2 &&
            (typeDefinition == "System.Collections.Generic.SortedDictionary<TKey, TValue>" ||
             typeDefinition == "System.Collections.Generic.SortedList<TKey, TValue>") &&
            methodSymbol.Name is "ContainsKey" or "TryGetValue" or "IndexOfKey")
        {
            keyType = containingType.TypeArguments[0];
            return true;
        }

        if (containingType.TypeArguments.Length == 2 &&
            typeDefinition == "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>" &&
            methodSymbol.Name is "ContainsKey" or "TryGetValue" or "Add" or "Remove" or "SetItem")
        {
            keyType = containingType.TypeArguments[0];
            return true;
        }

        if (containingType.TypeArguments.Length == 1 &&
            typeDefinition == "System.Collections.Generic.SortedSet<T>" &&
            methodSymbol.Name is "Contains" or "TryGetValue")
        {
            keyType = containingType.TypeArguments[0];
            return true;
        }

        if (containingType.TypeArguments.Length == 1 &&
            typeDefinition == "System.Collections.Immutable.ImmutableSortedSet<T>" &&
            methodSymbol.Name is "Contains" or "TryGetValue" or "Add" or "Remove")
        {
            keyType = containingType.TypeArguments[0];
            return true;
        }

        if (containingType.TypeArguments.Length == 1 &&
            typeDefinition == "System.Collections.Generic.List<T>" &&
            methodSymbol.Name == "BinarySearch" &&
            methodSymbol.Parameters.Length == 1)
        {
            keyType = containingType.TypeArguments[0];
            return true;
        }

        return false;
    }


    private static PurityAnalysisEngine.PurityAnalysisResult CheckComparerValuePurity(
        IOperation value,
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        return ComparerDispatchHelper.CheckComparerValuePurity(
            value,
            context,
            invocationOperation.Syntax,
            invocationOperation,
            nameof(MethodInvocationPurityRule),
            invocationOperation.TargetMethod);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckLinqComparerArgumentPurity(
        IArgumentOperation argument,
        PurityAnalysisContext context)
    {
        var value = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
        return ComparerDispatchHelper.CheckComparerValuePurity(
            value,
            context,
            value.Syntax,
            argument,
            nameof(MethodInvocationPurityRule),
            argument.Parameter);
    }
}
