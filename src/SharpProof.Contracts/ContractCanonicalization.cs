namespace SharpProof.Contracts;

internal sealed class ContractCanonicalization(
    Compilation compilation,
    IrFactory factory)
{
    private readonly Compilation _compilation =
        ArgumentNullGuard.NotNull(compilation, nameof(compilation));
    private readonly RoslynTypeMapper _types =
        new(ArgumentNullGuard.NotNull(factory, nameof(factory)));

    internal Func<ITypeSymbol?, ITypeSymbol?> CreateTypeSpecializer(
        IMethodSymbol source)
    {
        ArgumentNullGuard.NotNull(source, nameof(source));
        return new TypeSpecializer(_compilation, source).Specialize;
    }

    private sealed class TypeSpecializer
    {
        private readonly Compilation _compilation;
        private readonly ImmutableDictionary<ITypeParameterSymbol, ITypeSymbol> _substitutions;
        private readonly ImmutableDictionary<ITypeSymbol, ITypeSymbol> _signatureTypes;

        internal TypeSpecializer(
            Compilation compilation,
            IMethodSymbol source)
        {
            compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
            source = RepositoryParameterGuard.NotNull(source, nameof(source));
            _compilation = compilation;
            var substitutions = ImmutableDictionary.CreateBuilder<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
            var signatureTypes = ImmutableDictionary.CreateBuilder<ITypeSymbol, ITypeSymbol>(SymbolEqualityComparer.IncludeNullability);
            AddParameters(substitutions,
                source.OriginalDefinition.TypeParameters,
                source.TypeArguments);
            for (var type = source.ContainingType;
                 type != null;
                 type = type.ContainingType)
            {
                AddParameters(substitutions,
                    type.OriginalDefinition.TypeParameters,
                    type.TypeArguments);
            }
            AddMethodSignature(signatureTypes, source.OriginalDefinition, source);
            var partialCounterpart =
                source.OriginalDefinition.PartialImplementationPart ??
                source.OriginalDefinition.PartialDefinitionPart;
            if (partialCounterpart != null &&
                partialCounterpart.TypeParameters.Length == source.TypeArguments.Length &&
                partialCounterpart.Parameters.Length == source.Parameters.Length)
            {
                AddParameters(substitutions,
                    partialCounterpart.TypeParameters,
                    source.TypeArguments);
                AddMethodSignature(signatureTypes, partialCounterpart, source,
                    includeReturn: ContractForSymbolMatcher.TypesMatch(
                        source.OriginalDefinition.ReturnType,
                        partialCounterpart.ReturnType,
                        source.OriginalDefinition,
                        partialCounterpart,
                        normalizeMappedTypeParameters: true,
                        compareRuntimeShape: true,
                        mapTypeParameter: type => type is null ? throw new ArgumentNullException(nameof(type)) : MapTypeParameter(type, substitutions)));
            }
            _substitutions = substitutions.ToImmutable();
            _signatureTypes = signatureTypes.ToImmutable();
        }

        private static ITypeSymbol MapTypeParameter(
            ITypeSymbol type,
            ImmutableDictionary<ITypeParameterSymbol, ITypeSymbol>.Builder substitutions)
        {
            substitutions = RepositoryParameterGuard.NotNull(substitutions, nameof(substitutions));
            type = RepositoryParameterGuard.NotNull(type, nameof(type));
            return type is ITypeParameterSymbol parameter &&
                substitutions.TryGetValue(parameter, out var replacement)
                ? replacement : type;
        }

        private static void AddMethodSignature(
            ImmutableDictionary<ITypeSymbol, ITypeSymbol>.Builder signatureTypes,
            IMethodSymbol definition,
            IMethodSymbol constructed,
            bool includeReturn = true)
        {
            signatureTypes = RepositoryParameterGuard.NotNull(signatureTypes, nameof(signatureTypes));
            constructed = RepositoryParameterGuard.NotNull(constructed, nameof(constructed));
            definition = RepositoryParameterGuard.NotNull(definition, nameof(definition));
            if (includeReturn)
            {
                AddSignatureType(signatureTypes, definition.ReturnType, constructed.ReturnType);
            }
            for (var index = 0; index < constructed.Parameters.Length; index++)
            {
                AddSignatureType(signatureTypes,
                    definition.Parameters[index].Type,
                    constructed.Parameters[index].Type);
            }
        }

        internal ITypeSymbol? Specialize(ITypeSymbol? type)
        {
            if (type == null)
            {
                return null;
            }

            if (_signatureTypes.TryGetValue(type, out var signatureType))
            {
                return signatureType;
            }

            if (type is ITypeParameterSymbol parameter &&
                _substitutions.TryGetValue(parameter, out var replacement))
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

            if (type is IFunctionPointerTypeSymbol functionPointer)
            {
                var signature = functionPointer.Signature;
                var returnType = Specialize(signature.ReturnType);
                if (returnType == null)
                {
                    return null;
                }
                var parameterTypes = ImmutableArray.CreateBuilder<ITypeSymbol>();
                var parameterRefKinds = ImmutableArray.CreateBuilder<RefKind>();
                foreach (var functionParameter in signature.Parameters)
                {
                    var parameterType = Specialize(functionParameter.Type);
                    if (parameterType == null)
                    {
                        return null;
                    }
                    parameterTypes.Add(parameterType);
                    parameterRefKinds.Add(functionParameter.RefKind);
                }
                return _compilation.CreateFunctionPointerTypeSymbol(
                    returnType,
                    signature.RefKind,
                    parameterTypes.ToImmutable(),
                    parameterRefKinds.ToImmutable(),
                    signature.CallingConvention,
                    signature.UnmanagedCallingConventionTypes);
            }

            if (type is not INamedTypeSymbol named ||
                named.IsUnboundGenericType)
            {
                return type;
            }

            var arguments = ImmutableArray.CreateBuilder<ITypeSymbol>();
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

            return ContractSpecializationPolicy.TrySpecialize(
                (named, containing, arguments: arguments.ToImmutable()),
                static state =>
                {
                    var definition = state.containing == null
                        ? state.named.OriginalDefinition
                        : state.containing.GetTypeMembers(
                            state.named.Name,
                            state.named.Arity) is { Length: 1 } members
                            ? members[0]
                            : null;
                    if (definition == null)
                    {
                        return null;
                    }

                    return (definition.Arity == 0
                            ? definition
                            : definition.Construct([.. state.arguments]))
                        .WithNullableAnnotation(state.named.NullableAnnotation);
                }, out ITypeSymbol? specializedType)
                ? specializedType : null;
        }

        private static void AddParameters(
            ImmutableDictionary<ITypeParameterSymbol, ITypeSymbol>.Builder substitutions,
            ImmutableArray<ITypeParameterSymbol> parameters,
            ImmutableArray<ITypeSymbol> arguments)
        {


            substitutions = RepositoryParameterGuard.NotNull(substitutions, nameof(substitutions));
            RepositoryParameterGuard.RequireInitialized(arguments.IsDefault, nameof(arguments), "Array must be initialized.");
            RepositoryParameterGuard.RequireInitialized(parameters.IsDefault, nameof(parameters), "Array must be initialized.");
            for (var index = 0; index < parameters.Length; index++)
            {
                substitutions[parameters[index]] = arguments[index];
            }
        }

        private static void AddSignatureType(
            ImmutableDictionary<ITypeSymbol, ITypeSymbol>.Builder signatureTypes,
            ITypeSymbol original,
            ITypeSymbol constructed)
        {
            signatureTypes = RepositoryParameterGuard.NotNull(signatureTypes, nameof(signatureTypes));
            constructed = RepositoryParameterGuard.NotNull(constructed, nameof(constructed));
            if (signatureTypes.ContainsKey(original))
            {
                return;
            }
            signatureTypes.Add(original, constructed);

            switch (original, constructed)
            {
                case (IArrayTypeSymbol originalArray,
                      IArrayTypeSymbol constructedArray):
                    AddSignatureType(signatureTypes,
                        originalArray.ElementType,
                        constructedArray.ElementType);
                    break;
                case (IPointerTypeSymbol originalPointer,
                      IPointerTypeSymbol constructedPointer):
                    AddSignatureType(signatureTypes,
                        originalPointer.PointedAtType,
                        constructedPointer.PointedAtType);
                    break;
                case (IFunctionPointerTypeSymbol originalFunction,
                      IFunctionPointerTypeSymbol constructedFunction):
                    AddSignatureType(signatureTypes,
                        originalFunction.Signature.ReturnType,
                        constructedFunction.Signature.ReturnType);
                    for (var index = 0;
                         index < originalFunction.Signature.Parameters.Length;
                         index++)
                    {
                        AddSignatureType(signatureTypes,
                            originalFunction.Signature.Parameters[index].Type,
                            constructedFunction.Signature.Parameters[index].Type);
                    }
                    break;
                case (INamedTypeSymbol originalNamed,
                      INamedTypeSymbol constructedNamed):
                    if (originalNamed.ContainingType is { } originalContaining &&
                        constructedNamed.ContainingType is { } constructedContaining)
                    {
                        AddSignatureType(signatureTypes,
                            originalContaining,
                            constructedContaining);
                    }
                    for (var index = 0;
                         index < originalNamed.TypeArguments.Length;
                         index++)
                    {
                        AddSignatureType(signatureTypes,
                            originalNamed.TypeArguments[index],
                            constructedNamed.TypeArguments[index]);
                    }
                    break;
            }
        }
    }

    internal ContractCanonicalVariableBuilder CreateVariables(
        IMethodSymbol target,
        bool includeResult)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
        var result = new ContractCanonicalVariableBuilder(factory);
        if (!target.IsStatic &&
            target.MethodKind != MethodKind.Constructor)
        {
            result.Add(
                target.ContainingType,
                BoundContractVariableRole.Receiver,
                -1,
                _types.GetTypeId(target.ContainingType),
                "receiver");
        }

        for (var index = 0; index < target.Parameters.Length; index++)
        {
            var parameter = target.Parameters[index];
            result.Add(
                parameter,
                BoundContractVariableRole.Parameter,
                index,
                _types.GetTypeId(parameter.Type),
                "parameter:" + index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        if (includeResult &&
            !target.ReturnsVoid &&
            target.MethodKind != MethodKind.Constructor)
        {
            result.Add(
                null,
                BoundContractVariableRole.Result,
                -1,
                _types.GetTypeId(target.ReturnType),
                "result");
        }

        return result;
    }

    internal ImmutableDictionary<IrVarId, IrTerm>? CreateSubstitutions(
        IMethodSymbol source,
        bool usesCompanion,
        ContractExpressionBinder expressionBinder,
        ContractCanonicalVariableBuilder canonical)
    {
        ArgumentNullGuard.NotNull(source, nameof(source));
        ArgumentNullGuard.NotNull(expressionBinder, nameof(expressionBinder));
        ArgumentNullGuard.NotNull(canonical, nameof(canonical));
        return ContractVariableTransfer.CreateSubstitutions(
            factory,
            source,
            usesCompanion,
            expressionBinder.VariableBindings,
            expressionBinder.ResultVariable,
            expressionBinder.ReceiverVariables,
            expressionBinder.PreStateVariablePairs,
            canonical.ToBoundVariables(),
            canonical.CreatePreState);
    }
}

internal sealed class ContractCanonicalVariableBuilder(IrFactory factory)
{
    private readonly ImmutableArray<BoundContractVariable>.Builder _variables = factory is null
        ? throw new System.ArgumentNullException(nameof(factory))
        : ImmutableArray.CreateBuilder<BoundContractVariable>();
    private readonly ImmutableArray<IrVarId>.Builder _parameters = ImmutableArray.CreateBuilder<IrVarId>();

    internal ImmutableArray<IrVarId> Parameters => _parameters.ToImmutable();
    internal IrVarId? Result => _variables.FirstOrDefault(variable => variable is null
        ? throw new System.ArgumentNullException(nameof(variable))
        : variable.Role == BoundContractVariableRole.Result)?.Variable;

    internal IrVarId Add(
        ISymbol? symbol,
        BoundContractVariableRole role,
        int ordinal,
        IrTypeId type,
        string name)
    {
        RepositoryParameterGuard.ThrowIfInvalid(type.IsDefault, nameof(type));
        name = RepositoryParameterGuard.NotNull(name, nameof(name));
        var variable = factory.CreateVariable(name, type);
        _variables.Add(new BoundContractVariable(
            symbol,
            role,
            ordinal,
            variable,
            null));
        if (role == BoundContractVariableRole.Parameter)
        { _parameters.Add(variable); }
        return variable;
    }

    internal IrVarId CreatePreState(IrVarId current)
    {
        RepositoryParameterGuard.ThrowIfInvalid(current.IsDefault, nameof(current));
        var info = factory.GetVariableInfo(current);
        var variable = factory.CreateVariable(
            "pre:" + current.Value.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            info.Type);
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
        return _variables.ToImmutable();
    }
}
