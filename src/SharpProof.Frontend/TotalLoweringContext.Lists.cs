namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsListStateDeclaration(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var method = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod;
        return method != null && IsFrameworkCollectionMember(method) && symbol.OriginalDefinition.GetDocumentationCommentId() is
            "M:System.Collections.Generic.List`1.#ctor" or "M:System.Collections.Generic.List`1.Add(`0)" or
            "M:System.Collections.Generic.List`1.Insert(System.Int32,`0)" or "M:System.Collections.Generic.IList`1.Insert(System.Int32,`0)" or
            "M:System.Collections.Generic.List`1.GetRange(System.Int32,System.Int32)" or
            "P:System.Collections.Generic.List`1.Count" or "P:System.Collections.Generic.List`1.Item(System.Int32)" or
            "M:System.Collections.Generic.ICollection`1.Add(`0)" or "P:System.Collections.Generic.ICollection`1.Count" or
            "P:System.Collections.Generic.IList`1.Item(System.Int32)";
    }

    internal static bool IsListStateMember(IMethodSymbol method, ITypeSymbol? knownElement = null)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return IsListStateDeclaration(method.AssociatedSymbol ?? method) && !method.IsStatic &&
            method.ContainingType is { TypeArguments.Length: 1 } type &&
            (method.MethodKind == MethodKind.Constructor || method.AssociatedSymbol is IPropertySymbol { Name: "Count" } ||
                IsValueDomain(knownElement ?? type.TypeArguments[0]));
    }

    internal static bool IsConcreteListStateMember(IMethodSymbol method, ITypeSymbol? knownElement = null)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        // A subclass can replace interface dispatch or hide a public member,
        // but a bound nonvirtual List member still uses the base List storage.
        return IsListStateMember(method, knownElement) && !method.IsVirtual && method.MethodKind != MethodKind.Constructor &&
            method.ContainingType.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.List`1";
    }

    internal static bool IsEmptyListCreation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IObjectCreationOperation { Constructor: { Parameters.IsEmpty: true } constructor, Initializer: null } &&
            IsListStateMember(constructor);
    }
}

public sealed partial class TotalLoweringContext
{
    private readonly HashSet<ISymbol> _exactLists = new(SymbolEqualityComparer.Default);
    private readonly HashSet<CaptureId> _exactListCaptures = [];

    internal void TrackFreshLists(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        TrackFreshCollections(graph, true);
    }

    internal bool IsExactList(IOperation operation)
    { return IsKnownList(RepositoryParameterGuard.NotNull(operation, nameof(operation)), _exactLists, _exactListCaptures); }

    internal static bool IsListReceiverType(ITypeSymbol? type)
    {
        return IsArrayInterface(type) || type is INamedTypeSymbol named &&
            named.OriginalDefinition.GetDocumentationCommentId() == "T:System.Collections.Generic.List`1" &&
            named.GetMembers().Any(CSharpOperationSemantics.IsListStateDeclaration);
    }

    internal void SpecializeListParameter(IParameterSymbol parameter)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        _exactLists.Add(parameter);
    }

    private static bool IsKnownList(IOperation operation, HashSet<ISymbol> symbols, HashSet<CaptureId> captures)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        symbols = RepositoryParameterGuard.NotNull(symbols, nameof(symbols));
        captures = RepositoryParameterGuard.NotNull(captures, nameof(captures));
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
        return CSharpOperationSemantics.IsEmptyListCreation(operation) || IsListRangeAllocation(operation) ||
            Symbol(operation) is { } symbol && symbols.Contains(symbol) ||
            operation is IFlowCaptureReferenceOperation capture && captures.Contains(capture.Id);
    }

    private static bool IsListRangeAllocation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IInvocationOperation { TargetMethod: { Name: "GetRange" } method } &&
            CSharpOperationSemantics.IsConcreteListStateMember(method);
    }
}
