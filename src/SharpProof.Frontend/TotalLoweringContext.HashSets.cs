namespace SharpProof.Frontend;

internal static partial class CSharpOperationSemantics
{
    internal static bool IsHashSetInterfaceStateDeclaration(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var method = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod;
        return method != null && IsFrameworkCollectionMember(method) &&
            symbol.OriginalDefinition.GetDocumentationCommentId() is
                "M:System.Collections.Generic.ICollection`1.Add(`0)" or "M:System.Collections.Generic.ICollection`1.Contains(`0)" or
                "M:System.Collections.Generic.ICollection`1.Remove(`0)" or "P:System.Collections.Generic.ICollection`1.Count";
    }

    internal static bool IsHashSetStateDeclaration(ISymbol symbol)
    {
        symbol = RepositoryParameterGuard.NotNull(symbol, nameof(symbol));
        var method = symbol as IMethodSymbol ?? (symbol as IPropertySymbol)?.GetMethod;
        return method != null && IsFrameworkCollectionMember(method) &&
            symbol.OriginalDefinition.GetDocumentationCommentId() is
                "M:System.Collections.Generic.HashSet`1.#ctor" or "M:System.Collections.Generic.HashSet`1.#ctor(System.Int32)" or
                "M:System.Collections.Generic.HashSet`1.Add(`0)" or "M:System.Collections.Generic.HashSet`1.Contains(`0)" or
                "M:System.Collections.Generic.HashSet`1.Remove(`0)" or "P:System.Collections.Generic.HashSet`1.Count";
    }

    internal static bool IsHashSetStateMember(IMethodSymbol method, ITypeSymbol? knownElement = null)
    {
        method = RepositoryParameterGuard.NotNull(method, nameof(method));
        var declaration = method.AssociatedSymbol ?? (ISymbol)method;
        var throughInterface = IsHashSetInterfaceStateDeclaration(declaration);
        return (throughInterface || IsHashSetStateDeclaration(declaration)) && !method.IsStatic && (!method.IsVirtual || throughInterface) &&
            method.ContainingType is { TypeArguments.Length: 1 } type &&
            type.OriginalDefinition.GetDocumentationCommentId() == (throughInterface ? "T:System.Collections.Generic.ICollection`1" : "T:System.Collections.Generic.HashSet`1") &&
            (method.MethodKind == MethodKind.Constructor || method.AssociatedSymbol is IPropertySymbol { Name: "Count" } ||
                IsHashSetPrimitiveElement(knownElement ?? type.TypeArguments[0]));
    }

    internal static bool IsHashSetPrimitiveElement(ITypeSymbol? type)
    { return IsComparisonIntegerType(type) || type?.SpecialType == SpecialType.System_Boolean; }

    internal static bool IsEmptyDefaultHashSetCreation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation is IObjectCreationOperation { Constructor: { } constructor, Initializer: null } &&
            IsHashSetStateMember(constructor);
    }
}

public sealed partial class TotalLoweringContext
{
    private readonly HashSet<ISymbol> _defaultHashSetOrigins = new(SymbolEqualityComparer.Default);
    private readonly HashSet<CaptureId> _defaultHashSetOriginCaptures = [];

    internal static bool IsHashSetReceiverType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol named &&
            named.OriginalDefinition.GetDocumentationCommentId() is "T:System.Collections.Generic.HashSet`1" or "T:System.Collections.Generic.ICollection`1" &&
            named.GetMembers("Count").Any(member => member is null ? throw new ArgumentNullException(nameof(member)) :
                CSharpOperationSemantics.IsHashSetStateDeclaration(member) || CSharpOperationSemantics.IsHashSetInterfaceStateDeclaration(member));
    }

    internal void SpecializeHashSetOriginParameter(IParameterSymbol parameter)
    {
        parameter = RepositoryParameterGuard.NotNull(parameter, nameof(parameter));
        _defaultHashSetOrigins.Add(parameter);
    }

    internal void TrackDefaultHashSets(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        TrackFreshCollections(graph, false, hashSetOrigins: true);
    }

    internal bool IsDefaultHashSetOrigin(IOperation operation)
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
        return CSharpOperationSemantics.IsEmptyDefaultHashSetCreation(operation) ||
            Symbol(operation) is { } symbol && _defaultHashSetOrigins.Contains(symbol) ||
            operation is IFlowCaptureReferenceOperation capture && _defaultHashSetOriginCaptures.Contains(capture.Id);
    }
}
