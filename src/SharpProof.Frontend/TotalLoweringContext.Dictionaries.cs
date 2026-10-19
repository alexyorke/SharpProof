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

    internal static bool IsDefaultDictionaryStateMember(IMethodSymbol method, ITypeSymbol? knownKey = null, ITypeSymbol? knownValue = null)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return IsDictionaryStateDeclaration(method.AssociatedSymbol ?? method) && !method.IsStatic &&
            method.ContainingType is { TypeArguments.Length: 2 } type &&
            type.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.Dictionary`2" &&
            (IsComparisonIntegerType(knownKey ?? type.TypeArguments[0]) || method.MethodKind == MethodKind.Constructor ||
                method.AssociatedSymbol is IPropertySymbol { Name: "Count" }) &&
            // Empty construction, Count and key membership do not inspect values.
            // Contents are stored without interpreting opaque values.
            (method.MethodKind == MethodKind.Constructor || method.Name == "ContainsKey" ||
                method.AssociatedSymbol is IPropertySymbol { Name: "Count" } ||
                IsValueDomain(knownValue ?? type.TypeArguments[1]));
    }

    internal static bool IsEmptyDefaultDictionaryCreation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IObjectCreationOperation { Constructor: { Parameters.IsEmpty: true } constructor, Initializer: null } &&
            IsDefaultDictionaryStateMember(constructor);
    }
}

public sealed partial class TotalLoweringContext
{
    private readonly HashSet<ISymbol> _defaultDictionaries = new(SymbolEqualityComparer.Default);
    private readonly HashSet<CaptureId> _defaultDictionaryCaptures = [];
    private readonly HashSet<ISymbol> _defaultDictionaryOrigins = new(SymbolEqualityComparer.Default);
    private readonly HashSet<CaptureId> _defaultDictionaryOriginCaptures = [];

    // The comparer is a constructor-established invariant. A dictionary-typed
    // incoming value, callback, ref escape or cyclic definition supplies no proof.
    internal void TrackDefaultDictionaries(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        TrackFreshCollections(graph, false);
        TrackFreshCollections(graph, false, dictionaryOrigins: true);
    }

    internal bool IsDefaultDictionary(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return CSharpOperationSemantics.IsEmptyDefaultDictionaryCreation(operation) ||
            Symbol(operation) is { } symbol && _defaultDictionaries.Contains(symbol) ||
            operation is IFlowCaptureReferenceOperation capture && _defaultDictionaryCaptures.Contains(capture.Id);
    }

    internal static bool IsDictionaryReceiverType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol named &&
            named.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.Dictionary`2" &&
            named.GetMembers("Count").Any(CSharpOperationSemantics.IsDictionaryStateDeclaration);
    }

    internal void SpecializeDictionaryOriginParameter(IParameterSymbol parameter)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        _defaultDictionaryOrigins.Add(parameter);
    }

    // Constructor lineage identifies the storage, not its current comparer.
    // After publication, the modeled heap predicate gates primitive operations.
    internal bool IsDefaultDictionaryOrigin(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        while (true)
        {
            if (operation is IConversionOperation { OperatorMethod: null } conversion &&
                (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference && conversion.Conversion.IsImplicit))
            { operation = conversion.Operand; }
            else if (operation is ISimpleAssignmentOperation { IsRef: false } assignment)
            { operation = assignment.Value; }
            else
            { break; }
        }
        return CSharpOperationSemantics.IsEmptyDefaultDictionaryCreation(operation) ||
            Symbol(operation) is { } symbol && _defaultDictionaryOrigins.Contains(symbol) ||
            operation is IFlowCaptureReferenceOperation capture && _defaultDictionaryOriginCaptures.Contains(capture.Id);
    }
}
