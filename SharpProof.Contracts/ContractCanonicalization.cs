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
        private readonly Dictionary<ITypeParameterSymbol, ITypeSymbol>
            _substitutions = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ITypeSymbol, ITypeSymbol> _signatureTypes =
            new(SymbolEqualityComparer.IncludeNullability);

        internal TypeSpecializer(
            Compilation compilation,
            IMethodSymbol source)
        {
            if (compilation is null)
            {
                throw new System.ArgumentNullException(nameof(compilation));
            }
            if (source is null)
            {
                throw new System.ArgumentNullException(nameof(source));
            }
            _compilation = compilation;
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
            AddMethodSignature(source.OriginalDefinition, source);
            var partialCounterpart =
                source.OriginalDefinition.PartialImplementationPart ??
                source.OriginalDefinition.PartialDefinitionPart;
            if (partialCounterpart != null &&
                partialCounterpart.TypeParameters.Length == source.TypeArguments.Length &&
                partialCounterpart.Parameters.Length == source.Parameters.Length)
            {
                AddParameters(
                    partialCounterpart.TypeParameters,
                    source.TypeArguments);
                AddMethodSignature(partialCounterpart, source,
                    includeReturn: ContractForSymbolMatcher.TypesMatch(
                        source.OriginalDefinition.ReturnType,
                        partialCounterpart.ReturnType,
                        source.OriginalDefinition,
                        partialCounterpart,
                        normalizeMappedTypeParameters: true,
                        compareRuntimeShape: true,
                        mapTypeParameter: MapTypeParameter));
            }
        }

        private ITypeSymbol MapTypeParameter(ITypeSymbol type)
        {
            if (type is null)
            {
                throw new System.ArgumentNullException(nameof(type));
            }
            return type is ITypeParameterSymbol parameter &&
                _substitutions.TryGetValue(parameter, out var replacement)
                ? replacement : type;
        }

        private void AddMethodSignature(
            IMethodSymbol definition,
            IMethodSymbol constructed,
            bool includeReturn = true)
        {
            if (constructed is null)
            {
                throw new System.ArgumentNullException(nameof(constructed));
            }
            if (definition is null)
            {
                throw new System.ArgumentNullException(nameof(definition));
            }
            if (includeReturn)
            {
                AddSignatureType(definition.ReturnType, constructed.ReturnType);
            }
            for (var index = 0; index < constructed.Parameters.Length; index++)
            {
                AddSignatureType(
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
                var parameterTypes = ImmutableArray.CreateBuilder<ITypeSymbol>(
                    signature.Parameters.Length);
                var parameterRefKinds = ImmutableArray.CreateBuilder<RefKind>(
                    signature.Parameters.Length);
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

            return ContractSpecializationPolicy.TrySpecialize(
                (named, containing, arguments),
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

        private void AddParameters(
            ImmutableArray<ITypeParameterSymbol> parameters,
            ImmutableArray<ITypeSymbol> arguments)
        {


            if (arguments.IsDefault)
            {
                throw new System.ArgumentException("Array must be initialized.", nameof(arguments));
            }
            if (parameters.IsDefault)
            {
                throw new System.ArgumentException("Array must be initialized.", nameof(parameters));
            }
            for (var index = 0; index < parameters.Length; index++)
            {
                _substitutions[parameters[index]] = arguments[index];
            }
        }

        private void AddSignatureType(
            ITypeSymbol original,
            ITypeSymbol constructed)
        {
            if (constructed is null)
            {
                throw new System.ArgumentNullException(nameof(constructed));
            }
            if (_signatureTypes.ContainsKey(original))
            {
                return;
            }
            _signatureTypes.Add(original, constructed);

            switch (original, constructed)
            {
                case (IArrayTypeSymbol originalArray,
                      IArrayTypeSymbol constructedArray):
                    AddSignatureType(
                        originalArray.ElementType,
                        constructedArray.ElementType);
                    break;
                case (IPointerTypeSymbol originalPointer,
                      IPointerTypeSymbol constructedPointer):
                    AddSignatureType(
                        originalPointer.PointedAtType,
                        constructedPointer.PointedAtType);
                    break;
                case (IFunctionPointerTypeSymbol originalFunction,
                      IFunctionPointerTypeSymbol constructedFunction):
                    AddSignatureType(
                        originalFunction.Signature.ReturnType,
                        constructedFunction.Signature.ReturnType);
                    for (var index = 0;
                         index < originalFunction.Signature.Parameters.Length;
                         index++)
                    {
                        AddSignatureType(
                            originalFunction.Signature.Parameters[index].Type,
                            constructedFunction.Signature.Parameters[index].Type);
                    }
                    break;
                case (INamedTypeSymbol originalNamed,
                      INamedTypeSymbol constructedNamed):
                    if (originalNamed.ContainingType is { } originalContaining &&
                        constructedNamed.ContainingType is { } constructedContaining)
                    {
                        AddSignatureType(
                            originalContaining,
                            constructedContaining);
                    }
                    for (var index = 0;
                         index < originalNamed.TypeArguments.Length;
                         index++)
                    {
                        AddSignatureType(
                            originalNamed.TypeArguments[index],
                            constructedNamed.TypeArguments[index]);
                    }
                    break;
            }
        }
    }

    internal ContractCanonicalVariables CreateVariables(
        IMethodSymbol target,
        bool includeResult)
    {
        ArgumentNullGuard.NotNull(target, nameof(target));
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

    internal IReadOnlyDictionary<IrVarId, IrTerm>? CreateSubstitutions(
        IMethodSymbol source,
        bool usesCompanion,
        ContractExpressionBinder expressionBinder,
        ContractCanonicalVariables canonical)
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
            [.. expressionBinder.PreStateVariables],
            canonical.ToBoundVariables(),
            canonical.CreatePreState);
    }
}

internal sealed class ContractCanonicalVariables(IrFactory factory)
{
    private readonly List<BoundContractVariable> _variables = (factory is null ? throw new System.ArgumentNullException(nameof(factory)) : []);

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
        if (type.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(type));
        }
        if (name is null)
        {
            throw new System.ArgumentNullException(nameof(name));
        }
        var variable = factory.CreateVariable(name, type);
        _variables.Add(new BoundContractVariable(
            symbol,
            role,
            ordinal,
            variable,
            null));
        return variable;
    }

    internal IrVarId CreatePreState(IrVarId current)
    {
        if (current.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(current));
        }
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
        return [.. _variables];
    }
}
