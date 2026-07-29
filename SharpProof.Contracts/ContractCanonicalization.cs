namespace SharpProof.Contracts;

internal sealed class ContractCanonicalization(
    Compilation compilation,
    IrFactory factory)
{
    private readonly Compilation _compilation =
        compilation ?? throw new ArgumentNullException(nameof(compilation));
    private readonly RoslynOperationLowerer _types = new(factory);

    internal Func<ITypeSymbol?, ITypeSymbol?> CreateTypeSpecializer(
        IMethodSymbol source)
    {
        var substitutions = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(
            SymbolEqualityComparer.Default);
        AddParameters(
            source.OriginalDefinition.TypeParameters,
            source.TypeArguments);
        for (var type = source.ContainingType;
             type != null;
             type = type.ContainingType)
        {
            AddParameters(
                type.OriginalDefinition.TypeParameters,
                type.TypeArguments);
        }

        return Specialize;

        ITypeSymbol? Specialize(ITypeSymbol? type)
        {
            if (type == null)
            {
                return null;
            }

            if (type is ITypeParameterSymbol parameter &&
                substitutions.TryGetValue(parameter, out var replacement))
            {
                return type.NullableAnnotation == NullableAnnotation.Annotated
                    ? replacement.WithNullableAnnotation(
                        NullableAnnotation.Annotated)
                    : replacement;
            }

            if (type is IArrayTypeSymbol array)
            {
                return Specialize(array.ElementType) is { } element
                    ? _compilation.CreateArrayTypeSymbol(
                            element,
                            array.Rank,
                            array.ElementNullableAnnotation)
                        .WithNullableAnnotation(array.NullableAnnotation)
                    : null;
            }

            if (type is IPointerTypeSymbol pointer)
            {
                return Specialize(pointer.PointedAtType) is { } pointedAt
                    ? _compilation.CreatePointerTypeSymbol(pointedAt)
                    : null;
            }

            if (type is not INamedTypeSymbol named ||
                named.IsUnboundGenericType)
            {
                return type;
            }

            var arguments =
                ImmutableArray.CreateBuilder<ITypeSymbol>(
                    named.TypeArguments.Length);
            foreach (var argument in named.TypeArguments)
            {
                var specialized = Specialize(argument);
                if (specialized == null)
                {
                    return null;
                }

                arguments.Add(specialized);
            }
            var containing =
                Specialize(named.ContainingType) as INamedTypeSymbol;
            if (named.ContainingType != null && containing == null)
            {
                return null;
            }

            var changed = !arguments.SequenceEqual(
                    named.TypeArguments,
                    SymbolEqualityComparer.IncludeNullability) ||
                !SymbolEqualityComparer.IncludeNullability.Equals(
                    named.ContainingType,
                    containing);
            if (!changed)
            {
                return named;
            }

            if (named.IsTupleType)
            {
                return null;
            }

            try
            {
                var definition = containing == null
                    ? named.OriginalDefinition
                    : containing.GetTypeMembers(
                        named.Name,
                        named.Arity).SingleOrDefault();
                if (definition == null)
                {
                    return null;
                }

                return (definition.Arity == 0
                        ? definition
                        : definition.Construct([.. arguments]))
                    .WithNullableAnnotation(named.NullableAnnotation);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        void AddParameters(
            ImmutableArray<ITypeParameterSymbol> parameters,
            ImmutableArray<ITypeSymbol> arguments)
        {
            for (var index = 0; index < parameters.Length; index++)
            {
                substitutions[parameters[index]] = arguments[index];
            }
        }
    }

    internal ContractCanonicalVariables CreateVariables(
        IMethodSymbol target,
        bool includeResult)
    {
        var result = new ContractCanonicalVariables(factory);
        if (!target.IsStatic &&
            target.MethodKind != MethodKind.Constructor)
        {
            result.Receiver = result.Add(
                target.ContainingType,
                BoundContractVariableRole.Receiver,
                -1,
                _types.GetTypeId(target.ContainingType),
                "receiver");
        }

        for (var index = 0; index < target.Parameters.Length; index++)
        {
            var parameter = target.Parameters[index];
            result.Parameters.Add(result.Add(
                parameter,
                BoundContractVariableRole.Parameter,
                index,
                _types.GetTypeId(parameter.Type),
                "parameter:" + index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (includeResult &&
            !target.ReturnsVoid &&
            target.MethodKind != MethodKind.Constructor)
        {
            result.Result = result.Add(
                null,
                BoundContractVariableRole.Result,
                -1,
                _types.GetTypeId(target.ReturnType),
                "result");
        }

        return result;
    }

    internal Dictionary<IrVarId, IrTerm>? CreateSubstitutions(
        IMethodSymbol source,
        bool usesCompanion,
        ContractExpressionBinder expressionBinder,
        ContractCanonicalVariables canonical)
    {
        var replacements = new Dictionary<IrVarId, IrTerm>();
        foreach (var binding in expressionBinder.VariableBindings)
        {
            IrVarId? canonicalVariable = null;
            if (binding.Symbol is IParameterSymbol parameter &&
                parameter.ContainingSymbol is IMethodSymbol owner &&
                SymbolEqualityComparer.Default.Equals(
                    owner.OriginalDefinition,
                    source.OriginalDefinition))
            {
                var ordinal = parameter.Ordinal -
                    (usesCompanion && canonical.Receiver.HasValue ? 1 : 0);
                canonicalVariable = ordinal < 0
                    ? canonical.Receiver
                    : ordinal < canonical.Parameters.Count
                        ? canonical.Parameters[ordinal]
                        : null;
            }
            if (!canonicalVariable.HasValue)
            {
                return null;
            }

            replacements[binding.Variable] =
                factory.Variable(canonicalVariable.Value);
        }

        if (expressionBinder.ResultVariable.HasValue)
        {
            if (!canonical.Result.HasValue)
            {
                return null;
            }

            replacements[expressionBinder.ResultVariable.Value] =
                factory.Variable(canonical.Result.Value);
        }

        foreach (var receiverVariable in
                 expressionBinder.ReceiverVariables)
        {
            if (usesCompanion || !canonical.Receiver.HasValue)
            {
                return null;
            }

            replacements[receiverVariable] =
                factory.Variable(canonical.Receiver.Value);
        }

        foreach (var preState in expressionBinder.PreStateVariables)
        {
            if (!replacements.TryGetValue(
                    preState.Key,
                    out var current) ||
                current is not IrVariableTerm currentVariable)
            {
                return null;
            }

            var canonicalPre = canonical.GetOrCreatePreState(
                currentVariable.Variable);
            replacements[preState.Value] =
                factory.Variable(canonicalPre);
        }

        return replacements;
    }
}

internal sealed class ContractCanonicalVariables(IrFactory factory)
{
    private readonly List<BoundContractVariable> _variables = [];
    private readonly Dictionary<IrVarId, IrVarId> _preState = [];

    internal IrVarId? Receiver { get; set; }
    internal List<IrVarId> Parameters { get; } = [];
    internal IrVarId? Result { get; set; }

    internal IrVarId Add(
        ISymbol? symbol,
        BoundContractVariableRole role,
        int ordinal,
        IrTypeId type,
        string name)
    {
        var variable = factory.CreateVariable(name, type);
        _variables.Add(new BoundContractVariable(
            symbol,
            role,
            ordinal,
            variable,
            null));
        return variable;
    }

    internal IrVarId GetOrCreatePreState(IrVarId current)
    {
        if (_preState.TryGetValue(current, out var existing))
        {
            return existing;
        }

        var info = factory.GetVariableInfo(current);
        var variable = factory.CreateVariable(
            "pre:" + current.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            info.Type);
        _preState.Add(current, variable);
        _variables.Add(new BoundContractVariable(
            null,
            BoundContractVariableRole.PreState,
            -1,
            variable,
            current));
        return variable;
    }

    internal ImmutableArray<BoundContractVariable> ToBoundVariables()
    {
        return [.. _variables];
    }
}
