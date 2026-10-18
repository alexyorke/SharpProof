namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsListStateDeclaration(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var method = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod;
        return method != null && IsFrameworkCollectionMember(method) && symbol.OriginalDefinition.GetDocumentationCommentId() is
            "M:System.Collections.Generic.List`1.#ctor" or "M:System.Collections.Generic.List`1.Add(`0)" or
            "P:System.Collections.Generic.List`1.Count" or "P:System.Collections.Generic.List`1.Item(System.Int32)" or
            "M:System.Collections.Generic.ICollection`1.Add(`0)" or "P:System.Collections.Generic.ICollection`1.Count" or
            "P:System.Collections.Generic.IList`1.Item(System.Int32)";
    }

    internal static bool IsListStateMember(IMethodSymbol method)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        return IsListStateDeclaration(method.AssociatedSymbol ?? method) && !method.IsStatic &&
            method.ContainingType is { TypeArguments.Length: 1 } type &&
            (method.MethodKind == MethodKind.Constructor || method.AssociatedSymbol is IPropertySymbol { Name: "Count" } ||
                IsScalar(type.TypeArguments[0]) || IsReferenceDomain(type.TypeArguments[0]) ||
                type.TypeArguments[0] is ITypeParameterSymbol { IsReferenceType: true });
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
    private readonly HashSet<ISymbol> _freshLists = new(SymbolEqualityComparer.Default);
    private readonly HashSet<CaptureId> _freshListCaptures = [];

    internal void TrackFreshLists(ControlFlowGraph graph)
    { TrackFreshCollections(RepositoryParameterGuard.NotNull(graph, nameof(graph)), true); }

    internal bool IsFreshList(IOperation operation)
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
        return CSharpOperationSemantics.IsEmptyListCreation(operation) ||
            Symbol(operation) is { } symbol && _freshLists.Contains(symbol) ||
            operation is IFlowCaptureReferenceOperation capture && _freshListCaptures.Contains(capture.Id);
    }
}
