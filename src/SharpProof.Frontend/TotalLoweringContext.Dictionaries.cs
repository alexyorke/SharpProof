namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsDictionaryStateDeclaration(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var method = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod;
        return method != null && IsFrameworkCollectionMember(method) && symbol.OriginalDefinition.GetDocumentationCommentId() is
            "M:System.Collections.Generic.Dictionary`2.#ctor" or "M:System.Collections.Generic.Dictionary`2.Add(`0,`1)" or
            "M:System.Collections.Generic.Dictionary`2.ContainsKey(`0)" or "P:System.Collections.Generic.Dictionary`2.Count" or
            "P:System.Collections.Generic.Dictionary`2.Item(`0)";
    }

    internal static bool IsDefaultDictionaryStateMember(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return IsDictionaryStateDeclaration(method.AssociatedSymbol ?? method) && !method.IsStatic &&
            method.ContainingType is { TypeArguments.Length: 2 } type &&
            type.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.Dictionary`2" &&
            (IsComparisonIntegerType(type.TypeArguments[0]) || method.MethodKind == MethodKind.Constructor ||
                method.AssociatedSymbol is IPropertySymbol { Name: "Count" }) &&
            // Empty construction, Count and key membership do not inspect values.
            // Contents still require the supported scalar/reference value domain.
            (method.MethodKind == MethodKind.Constructor || method.Name == "ContainsKey" ||
                method.AssociatedSymbol is IPropertySymbol { Name: "Count" } ||
                IsScalar(type.TypeArguments[1]) || IsReferenceDomain(type.TypeArguments[1]) ||
                type.TypeArguments[1] is ITypeParameterSymbol { IsReferenceType: true });
    }

    internal static bool IsEmptyDefaultDictionaryCreation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IObjectCreationOperation { Constructor: { Parameters.IsEmpty: true } constructor, Initializer: null } &&
            IsDefaultDictionaryStateMember(constructor);
    }
}

internal sealed partial class TotalLoweringContext
{
    private readonly HashSet<ISymbol> _defaultDictionaries = new(SymbolEqualityComparer.Default);
    private readonly HashSet<CaptureId> _defaultDictionaryCaptures = [];

    // The comparer is a constructor-established invariant. A dictionary-typed
    // incoming value, callback, ref escape or cyclic definition supplies no proof.
    internal void TrackDefaultDictionaries(ControlFlowGraph graph)
    { TrackFreshCollections(RepositoryParameterGuard.NotNull(graph, nameof(graph)), false); }

    internal bool IsDefaultDictionary(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return CSharpOperationSemantics.IsEmptyDefaultDictionaryCreation(operation) ||
            Symbol(operation) is { } symbol && _defaultDictionaries.Contains(symbol) ||
            operation is IFlowCaptureReferenceOperation capture && _defaultDictionaryCaptures.Contains(capture.Id);
    }
}
