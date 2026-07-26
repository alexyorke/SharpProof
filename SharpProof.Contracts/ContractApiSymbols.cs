namespace SharpProof.Contracts;

internal sealed class ContractApiSymbols {
    private ContractApiSymbols(
        INamedTypeSymbol contractType,
        IMethodSymbol requires,
        IMethodSymbol ensures,
        IMethodSymbol assume,
        IMethodSymbol result,
        IMethodSymbol old,
        INamedTypeSymbol contractFor,
        INamedTypeSymbol notNull,
        INamedTypeSymbol positive,
        INamedTypeSymbol inRange,
        INamedTypeSymbol pure) {
        ContractType = contractType;
        Requires = requires;
        Ensures = ensures;
        Assume = assume;
        Result = result;
        Old = old;
        ContractFor = contractFor;
        NotNull = notNull;
        Positive = positive;
        InRange = inRange;
        Pure = pure;
    }

    internal INamedTypeSymbol ContractType { get; }
    internal IMethodSymbol Requires { get; }
    internal IMethodSymbol Ensures { get; }
    internal IMethodSymbol Assume { get; }
    internal IMethodSymbol Result { get; }
    internal IMethodSymbol Old { get; }
    internal INamedTypeSymbol ContractFor { get; }
    internal INamedTypeSymbol NotNull { get; }
    internal INamedTypeSymbol Positive { get; }
    internal INamedTypeSymbol InRange { get; }
    internal INamedTypeSymbol Pure { get; }

    internal static ContractApiSymbols? TryCreate(Compilation compilation) {
        var contract = compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract");
        var contractFor = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.ContractForAttribute");
        var notNull = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.NotNullAttribute");
        var positive = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.PositiveAttribute");
        var inRange = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.InRangeAttribute");
        var pure = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.PureAttribute");
        if (contract == null || contractFor == null || notNull == null ||
            positive == null || inRange == null || pure == null)
            return null;

        var requires = FindBooleanClause(contract, "Requires");
        var ensures = FindBooleanClause(contract, "Ensures");
        var assume = FindBooleanClause(contract, "Assume");
        var result = FindGenericIntrinsic(contract, "Result", 0);
        var old = FindGenericIntrinsic(contract, "Old", 1);
        if (requires == null || ensures == null || assume == null ||
            result == null || old == null)
            return null;
        return new ContractApiSymbols(
            contract,
            requires,
            ensures,
            assume,
            result,
            old,
            contractFor,
            notNull,
            positive,
            inRange,
            pure);
    }

    internal BoundContractKind? GetClauseKind(IMethodSymbol method) {
        var definition = method.OriginalDefinition;
        if (SymbolEqualityComparer.Default.Equals(definition, Requires))
            return BoundContractKind.Requires;
        if (SymbolEqualityComparer.Default.Equals(definition, Ensures))
            return BoundContractKind.Ensures;
        if (SymbolEqualityComparer.Default.Equals(definition, Assume))
            return BoundContractKind.Assume;
        return null;
    }

    internal bool IsResult(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Result);

    internal bool IsOld(IMethodSymbol method) =>
        SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, Old);

    internal bool IsAttribute(AttributeData attribute, INamedTypeSymbol expected) =>
        attribute.AttributeClass != null &&
        SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass.OriginalDefinition,
            expected);

    private static IMethodSymbol? FindBooleanClause(
        INamedTypeSymbol contract,
        string name) =>
        contract.GetMembers(name)
            .OfType<IMethodSymbol>()
            .SingleOrDefault(static method =>
                method.IsStatic &&
                method.Arity == 0 &&
                method.ReturnsVoid &&
                method.Parameters.Length == 1 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_Boolean);

    private static IMethodSymbol? FindGenericIntrinsic(
        INamedTypeSymbol contract,
        string name,
        int parameterCount) =>
        contract.GetMembers(name)
            .OfType<IMethodSymbol>()
            .SingleOrDefault(method =>
                method.IsStatic &&
                method.Arity == 1 &&
                method.Parameters.Length == parameterCount);
}
