using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Analysis;
using static SharpProof.Analyzer.Engine.Rules.MethodInvocationPurityRule;

namespace SharpProof.Analyzer.Engine.Rules;

internal static partial class ComparerInvocationPurity
{
    private sealed record CollectionDispatchRule(
        string TypeDefinition,
        int TypeArgumentIndex,
        bool RequiresHashCode,
        int? ParameterCount,
        bool IncludesHashSetRelations,
        params string[] MethodNames);

    private sealed record GenericDispatchRule(
        int TypeArgumentIndex,
        int RequiredTypeArgumentCount,
        bool RequiresExactTypeArgumentCount,
        params string[] MethodNames);

    private static readonly CollectionDispatchRule[] EqualityCollectionDispatchRules =
    [
        new("System.Collections.Generic.Dictionary<TKey, TValue>", 0, true, null, false, "ContainsKey", "TryGetValue"),
        new("System.Collections.Immutable.ImmutableDictionary<TKey, TValue>", 0, true, null, false, "ContainsKey", "TryGetValue", "Add", "Remove", "SetItem"),
        new("System.Collections.Generic.Dictionary<TKey, TValue>", 1, false, null, false, "ContainsValue"),
        new("System.Collections.Generic.SortedDictionary<TKey, TValue>", 1, false, null, false, "ContainsValue"),
        new("System.Collections.Generic.List<T>", 0, false, null, false, "Contains", "IndexOf", "LastIndexOf"),
        new("System.Collections.Immutable.ImmutableList<T>", 0, false, null, false, "Contains", "IndexOf", "LastIndexOf", "Remove"),
        new("System.Collections.Generic.Queue<T>", 0, false, null, false, "Contains"),
        new("System.Collections.Generic.Stack<T>", 0, false, null, false, "Contains"),
        new("System.Collections.Generic.HashSet<T>", 0, true, null, true, "Contains", "TryGetValue"),
        new("System.Collections.Immutable.ImmutableHashSet<T>", 0, true, null, true, "Contains", "TryGetValue", "Add", "Remove")
    ];

    private static readonly CollectionDispatchRule[] ComparisonCollectionDispatchRules =
    [
        new("System.Collections.Generic.SortedDictionary<TKey, TValue>", 0, false, null, false, "ContainsKey", "TryGetValue"),
        new("System.Collections.Generic.SortedList<TKey, TValue>", 0, false, null, false, "ContainsKey", "TryGetValue", "IndexOfKey"),
        new("System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>", 0, false, null, false, "ContainsKey", "TryGetValue", "Add", "Remove", "SetItem"),
        new("System.Collections.Generic.SortedSet<T>", 0, false, null, false, "Contains", "TryGetValue"),
        new("System.Collections.Immutable.ImmutableSortedSet<T>", 0, false, null, false, "Contains", "TryGetValue", "Add", "Remove"),
        new("System.Collections.Generic.List<T>", 0, false, 1, false, "BinarySearch")
    ];

    private static readonly GenericDispatchRule[] LinqEqualityDispatchRules =
    [
        new(0, 1, true, "Contains", "SequenceEqual", "Distinct", "Except", "Intersect", "Union"),
        new(1, 2, false, "GroupBy", "ToLookup"),
        new(2, 3, false, "Join", "GroupJoin")
    ];

    private static readonly GenericDispatchRule[] LinqComparisonDispatchRules =
    [
        new(0, 1, true, "Min", "Max"),
        new(1, 2, false, "OrderBy", "OrderByDescending", "ThenBy", "ThenByDescending")
    ];

    internal static bool TryCheckEqualityComparerDispatchPurity(
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

    internal static bool TryCheckComparerDispatchPurity(
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

    internal static bool TryCheckNullableComparisonDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!TryGetNullableDefaultDispatchType(
                invocationOperation.TargetMethod,
                out var valueType,
                out var isComparison))
            return false;

        result = isComparison
            ? CheckDefaultComparisonDispatchPurity(valueType, invocationOperation, context)
            : CheckDefaultEqualityDispatchPurity(valueType, invocationOperation, context);
        return true;
    }

    internal static bool TryGetNullableDefaultDispatchType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol valueType,
        out bool isComparison)
    {
        valueType = null!;
        var definition = methodSymbol.OriginalDefinition;
        isComparison = definition.Name == "Compare";
        if (definition.ContainingType?.ToDisplayString() != "System.Nullable" ||
            !isComparison && definition.Name != "Equals" ||
            methodSymbol.TypeArguments.Length != 1)
            return false;

        valueType = methodSymbol.TypeArguments[0];
        return true;
    }

    internal static bool TryCheckCollectionEqualityDispatchPurity(
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

    internal static bool TryCheckCollectionComparisonDispatchPurity(
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

    internal static bool TryCheckLinqDefaultEqualityDispatchPurity(
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

    internal static bool TryCheckLinqDefaultComparisonDispatchPurity(
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

    internal static bool TryGetLinqDefaultComparisonDispatchType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol comparisonType)
    {
        return TryGetLinqDispatchType(methodSymbol, LinqComparisonDispatchRules, out comparisonType);
    }

    private static bool IsLinqDefaultComparisonOverload(IInvocationOperation invocationOperation)
    {
        if (TryGetComparerArgument(invocationOperation, out var comparerArgument))
            return ComparerDispatchHelper.IsNullOrDefaultComparerArgument(comparerArgument);

        return true;
    }

    internal static bool TryGetLinqDefaultEqualityDispatchType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol equalityType)
    {
        return TryGetLinqDispatchType(methodSymbol, LinqEqualityDispatchRules, out equalityType);
    }

    private static bool TryGetLinqDispatchType(
        IMethodSymbol methodSymbol,
        IReadOnlyList<GenericDispatchRule> rules,
        out ITypeSymbol dispatchType)
    {
        dispatchType = null!;
        var definition = GetExtensionDefinition(methodSymbol);
        if (definition.ContainingType?.OriginalDefinition.ToDisplayString() != "System.Linq.Enumerable") return false;

        foreach (var rule in rules)
        {
            var typeArgumentCount = methodSymbol.TypeArguments.Length;
            if (!rule.MethodNames.Contains(definition.Name, StringComparer.Ordinal) ||
                typeArgumentCount < rule.RequiredTypeArgumentCount ||
                rule.RequiresExactTypeArgumentCount && typeArgumentCount != rule.RequiredTypeArgumentCount)
                continue;

            dispatchType = methodSymbol.TypeArguments[rule.TypeArgumentIndex];
            return true;
        }

        return false;
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

    internal static void AddKnownInterfaceImplementation(
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


    internal static bool TryGetEqualityComparerElementType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol elementType)
    {
        return TryGetComparerElementType(
            methodSymbol,
            "System.Collections.Generic.EqualityComparer<T>",
            static method =>
                (method.Name == nameof(object.Equals) && method.Parameters.Length == 2) ||
                (method.Name == nameof(GetHashCode) && method.Parameters.Length == 1),
            out elementType);
    }

    internal static bool TryGetComparerElementType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol elementType)
    {
        return TryGetComparerElementType(
            methodSymbol,
            "System.Collections.Generic.Comparer<T>",
            static method => method.Name == "Compare" && method.Parameters.Length == 2,
            out elementType);
    }

    private static bool TryGetComparerElementType(
        IMethodSymbol methodSymbol,
        string expectedTypeDefinition,
        Func<IMethodSymbol, bool> methodMatches,
        out ITypeSymbol elementType)
    {
        elementType = null!;

        if (methodSymbol.ContainingType is not INamedTypeSymbol { TypeArguments.Length: 1 } containingType ||
            containingType.OriginalDefinition.ToDisplayString() != expectedTypeDefinition ||
            !methodMatches(methodSymbol))
            return false;

        elementType = containingType.TypeArguments[0];
        return true;
    }

    internal static bool TryGetDefaultEqualityCollectionElementType(
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

        return TryGetCollectionDispatchType(
            methodSymbol,
            containingType,
            EqualityCollectionDispatchRules,
            out elementType,
            out requiresHashCode);
    }

    private static bool IsHashSetRelationMethod(IMethodSymbol methodSymbol)
    {
        var typeDefinition = methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        return IsHashSetTypeDefinition(typeDefinition) && IsHashSetRelationName(methodSymbol.Name);
    }

    internal static bool TryGetDefaultComparisonCollectionKeyType(
        IMethodSymbol methodSymbol,
        out ITypeSymbol keyType)
    {
        keyType = null!;

        if (methodSymbol.ContainingType is not INamedTypeSymbol containingType)
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

        return TryGetCollectionDispatchType(
            methodSymbol,
            containingType,
            ComparisonCollectionDispatchRules,
            out keyType,
            out _);
    }

    private static bool TryGetCollectionDispatchType(
        IMethodSymbol methodSymbol,
        INamedTypeSymbol containingType,
        IReadOnlyList<CollectionDispatchRule> rules,
        out ITypeSymbol dispatchType,
        out bool requiresHashCode)
    {
        dispatchType = null!;
        requiresHashCode = false;
        var typeDefinition = containingType.OriginalDefinition.ToDisplayString();

        foreach (var rule in rules)
        {
            if (rule.TypeDefinition != typeDefinition ||
                containingType.TypeArguments.Length <= rule.TypeArgumentIndex ||
                rule.ParameterCount is { } parameterCount && methodSymbol.Parameters.Length != parameterCount ||
                !rule.MethodNames.Contains(methodSymbol.Name, StringComparer.Ordinal) &&
                !(rule.IncludesHashSetRelations && IsHashSetRelationName(methodSymbol.Name)))
                continue;

            dispatchType = containingType.TypeArguments[rule.TypeArgumentIndex];
            requiresHashCode = rule.RequiresHashCode;
            return true;
        }

        return false;
    }

    private static bool IsHashSetTypeDefinition(string? typeDefinition)
    {
        return typeDefinition is "System.Collections.Generic.HashSet<T>" or
            "System.Collections.Immutable.ImmutableHashSet<T>";
    }

    private static bool IsHashSetRelationName(string methodName)
    {
        return methodName is "SetEquals" or "Overlaps" or "IsSubsetOf" or "IsSupersetOf" or "IsProperSubsetOf" or
            "IsProperSupersetOf";
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

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckLinqComparerArgumentPurity(
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
