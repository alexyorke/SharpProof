namespace SharpProof.Contracts;

public sealed class ContractBinder
{
    private readonly IrFactory _factory;
    private readonly ContractApiSymbols? _api;
    private readonly ContractIntrinsicValidator _intrinsics;
    private readonly ContractCanonicalization _canonicalization;
    private readonly ContractClauseInventoryBuilder _clauseInventory;
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult> _bindings =
        new(SymbolEqualityComparer.IncludeNullability);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult> _requiresBindings =
        new(SymbolEqualityComparer.IncludeNullability);
    private readonly EffectiveContractSourceResolver _contractSources;

    public ContractBinder(
        Compilation compilation,
        IrFactory factory,
        ContractClauseInventoryBuilder? clauseInventory = null)
        : this(
            compilation,
            factory,
            clauseInventory,
            contractSources: null)
    {
    }

    internal static ContractBinder CreateWithContractSources(
        Compilation compilation,
        IrFactory factory,
        ContractClauseInventoryBuilder clauseInventory,
        EffectiveContractSourceResolver contractSources)
    {
        return new ContractBinder(
            compilation,
            factory,
            clauseInventory,
            ArgumentNullGuard.NotNull(
                contractSources,
                nameof(contractSources)));
    }

    private ContractBinder(
        Compilation compilation,
        IrFactory factory,
        ContractClauseInventoryBuilder? clauseInventory,
        EffectiveContractSourceResolver? contractSources)
    {
        compilation = ArgumentNullGuard.NotNull(
            compilation,
            nameof(compilation));
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        _api = ContractApiSymbols.TryCreate(compilation);
        _intrinsics = new ContractIntrinsicValidator(compilation);
        _canonicalization = new ContractCanonicalization(
            compilation,
            _factory);
        _clauseInventory = clauseInventory ??
            ContractClauseInventoryBuilder.ForCompilation(compilation);
        _contractSources = contractSources ??
            (clauseInventory == null
                ? EffectiveContractSourceResolver.ForCompilation(compilation)
                : new EffectiveContractSourceResolver(
                    compilation,
                    clauseInventory));
    }

    public ContractBindingResult Bind(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));

        return implementationBody == null
            ? _bindings.GetOrAdd(
                target,
                value => BindUncached(value, requiresOnly: false))
            : BindCore(
                target,
                implementationBody,
                requiresOnly: false,
                cancellationToken: CancellationToken.None);
    }

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));

        return implementationBody == null
            ? _requiresBindings.GetOrAdd(
                target,
                value => BindUncached(value, requiresOnly: true))
            : BindCore(
                target,
                implementationBody,
                requiresOnly: true,
                cancellationToken: CancellationToken.None);
    }

    internal ContractBindingResult BindRequires(
        IMethodSymbol target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return _requiresBindings.GetOrAdd(
            target,
            value => BindCore(
                value,
                implementationBody: null,
                requiresOnly: true,
                cancellationToken: cancellationToken));
    }

    public ContractClauseInventory GetClauseInventory(IMethodSymbol target)
    {
        return _clauseInventory.Create(target);
    }

    private ContractBindingResult BindUncached(
        IMethodSymbol target,
        bool requiresOnly)
    {
        return BindCore(
            target,
            implementationBody: null,
            requiresOnly,
            cancellationToken: CancellationToken.None);
    }

    private ContractBindingResult BindCore(
        IMethodSymbol target,
        IOperation? implementationBody,
        bool requiresOnly,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_api == null)
        {
            return ContractBindingResult.Fail(ContractBindingFailure.ContractApiUnavailable);
        }

        if (target.MethodKind is not (
                MethodKind.Ordinary or
                MethodKind.Constructor or
                MethodKind.StaticConstructor or
                MethodKind.PropertyGet or
                MethodKind.PropertySet or
                MethodKind.EventAdd or
                MethodKind.EventRemove or
                MethodKind.ExplicitInterfaceImplementation or
                MethodKind.UserDefinedOperator or
                MethodKind.Conversion))
        {
            return ContractBindingResult.Fail(ContractBindingFailure.UnsupportedTarget);
        }

        var resolution = _contractSources.Resolve(
            target,
            implementationBody,
            cancellationToken);
        if (resolution.DirectInventory.HasRejectedContractApiUsage &&
            resolution.DirectInventory.ImplementationBody == null &&
            resolution.DirectInventory.Clauses.IsEmpty)
        {
            return ContractBindingResult.Fail(
                ContractBindingFailure.UnsupportedTarget);
        }
        var directIntrinsicsValidated = false;
        if (!resolution.HasValidDirectClause &&
            target.MethodKind == MethodKind.Ordinary)
        {
            var directFailure = ValidateIntrinsics(
                resolution.DirectInventory.Callable,
                resolution.DirectInventory.ImplementationBody,
                requiresOnly);
            if (directFailure != ContractBindingFailure.None)
            {
                return ContractBindingResult.Fail(directFailure);
            }

            directIntrinsicsValidated = true;
        }
        if (resolution.Failure != ContractBindingFailure.None &&
            (!requiresOnly ||
             resolution.Failure != ContractBindingFailure.InvalidClausePlacement ||
             HasRequiresPlacementErrors(resolution.Inventory)))
        {
            return ContractBindingResult.Fail(resolution.Failure);
        }

        var source = resolution.Source;
        var inventory = resolution.Inventory;
        var usesCompanion = resolution.UsesCompanion;
        var expressionBinder = new ContractExpressionBinder(
            _factory,
            _api,
            source,
            _canonicalization.CreateTypeSpecializer(source));
        var invocationResult = BindInvocations(
            expressionBinder,
            inventory,
            usesCompanion,
            requiresOnly,
            directIntrinsicsValidated && !usesCompanion);
        if (invocationResult.Failure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(invocationResult.Failure);
        }

        var canonical = _canonicalization.CreateVariables(
            target,
            includeResult: !requiresOnly);
        var substitutions = _canonicalization.CreateSubstitutions(
            source,
            usesCompanion,
            expressionBinder,
            canonical);
        if (substitutions == null)
        {
            return ContractBindingResult.Fail(ContractBindingFailure.UnsupportedExpression);
        }

        var clauses = ImmutableArray.CreateBuilder<BoundContractClause>();
        foreach (var clause in invocationResult.Clauses)
        {
            IrTerm condition;
            try
            {
                condition = IrSubstitution.Substitute(_factory, clause.Condition, substitutions);
            }
            catch (ArgumentException)
            {
                return ContractBindingResult.Fail(ContractBindingFailure.UnsupportedExpression);
            }
            clauses.Add(new BoundContractClause(
                clause.Kind, condition, clause.SourceOperation, clause.Evidence));
        }

        var attributeResult = BindClosedAttributes(target, canonical, requiresOnly);
        if (attributeResult.Failure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(attributeResult.Failure);
        }

        clauses.AddRange(attributeResult.Clauses);

        return ContractBindingResult.Success(new BoundMethodContracts(
            target, source, clauses.ToImmutable(), canonical.ToBoundVariables(), usesCompanion));
    }

    private static bool HasRequiresPlacementErrors(
        ContractClauseInventory inventory)
    {
        return inventory.Clauses.Any(static clause =>
            clause.Kind == BoundContractKind.Requires &&
            !clause.IsValid &&
            clause.Placement != ContractClausePlacement.NestedCallable);
    }

    private ClauseBindingResult BindInvocations(
        ContractExpressionBinder expressionBinder,
        ContractClauseInventory inventory,
        bool usesCompanion,
        bool requiresOnly,
        bool intrinsicsAlreadyValidated)
    {
        var body = inventory.ImplementationBody;
        if (body == null)
        {
            return ClauseBindingResult.Empty;
        }

        var failure = intrinsicsAlreadyValidated
            ? ContractBindingFailure.None
            : ValidateIntrinsics(inventory.Callable, body, requiresOnly);
        if (failure != ContractBindingFailure.None)
        {
            return new ClauseBindingResult([], failure);
        }

        var clauses = ImmutableArray.CreateBuilder<BoundContractClause>();
        foreach (var occurrence in inventory.Clauses)
        {
            if (!occurrence.IsValid)
            {
                continue;
            }

            if (requiresOnly && occurrence.Kind != BoundContractKind.Requires)
            {
                continue;
            }

            var invocation = occurrence.Invocation;
            if (invocation.Arguments.Length != 1)
            {
                return new ClauseBindingResult([], ContractBindingFailure.InvalidIntrinsicSignature);
            }

            var expression = expressionBinder.Bind(invocation.Arguments[0].Value);
            if (!expression.IsSuccess)
            {
                return new ClauseBindingResult([], expression.Failure);
            }

            if (expression.Term!.Type != _factory.BooleanType)
            {
                return new ClauseBindingResult([], ContractBindingFailure.NonBooleanCondition);
            }

            clauses.Add(new BoundContractClause(
                occurrence.Kind, expression.Term,
                _factory.CreateOperation("contract@" + invocation.Syntax.SpanStart.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)),
                usesCompanion
                    ? BoundContractEvidence.Companion
                    : BoundContractEvidence.CompilerBoundInvocation));
        }
        return new ClauseBindingResult(clauses.ToImmutable(), ContractBindingFailure.None);
    }

    private ContractBindingFailure ValidateIntrinsics(
        IMethodSymbol source,
        IOperation? body,
        bool requiresOnly)
    {
        foreach (var violation in _intrinsics.Validate(source, body))
        {
            if (!requiresOnly || violation.EnclosingClauseKind == BoundContractKind.Requires)
            {
                return violation.Failure;
            }
        }

        return ContractBindingFailure.None;
    }

    private ClauseBindingResult BindClosedAttributes(
        IMethodSymbol target,
        ContractCanonicalVariables variables,
        bool requiresOnly)
    {
        var clauses = ImmutableArray.CreateBuilder<BoundContractClause>();
        for (var index = 0; index < target.Parameters.Length; index++)
        {
            var result = BindValueAttributes(
                target.Parameters[index].GetAttributes(), target.Parameters[index].Type, target.Parameters[index].RefKind,
                _factory.Variable(variables.Parameters[index]),
                BoundContractKind.Requires, clauses);
            if (result != ContractBindingFailure.None)
            {
                return ClauseBindingResult.Fail(result);
            }
        }
        if (!requiresOnly)
        {
            var result = BindValueAttributes(
                target.GetReturnTypeAttributes(), target.ReturnType, RefKind.None,
                variables.Result.HasValue
                    ? _factory.Variable(variables.Result.Value)
                    : null,
                BoundContractKind.Ensures, clauses);
            if (result != ContractBindingFailure.None)
            {
                return ClauseBindingResult.Fail(result);
            }
        }
        return new ClauseBindingResult(clauses.ToImmutable(), ContractBindingFailure.None);
    }

    private ContractBindingFailure BindValueAttributes(
        ImmutableArray<AttributeData> attributes,
        ITypeSymbol sourceType,
        RefKind refKind,
        IrTerm? value,
        BoundContractKind kind,
        ImmutableArray<BoundContractClause>.Builder clauses)
    {
        foreach (var attribute in attributes)
        {
            if (_api!.Selections.IsRejectedClosedContract(attribute))
            {
                return ContractBindingFailure.InvalidClosedAttribute;
            }

            var validation = ClosedContractAttributeValidator.Validate(
                attribute,
                sourceType,
                refKind,
                _api.Selections);
            if (!validation.IsRecognized)
            {
                continue;
            }

            if (!validation.IsValid)
            {
                return ContractBindingFailure.InvalidClosedAttribute;
            }
            if (value == null)
            {
                return ContractBindingFailure.InvalidClosedAttribute;
            }

            IrTerm condition;
            switch (validation.Kind)
            {
                case ClosedContractAttributeKind.NotNull:
                    var type = _factory.GetTypeInfo(value.Type);
                    if (type.Kind is not (
                            IrTypeKind.Reference or
                            IrTypeKind.String or
                            IrTypeKind.Sequence))
                    {
                        return ContractBindingFailure.InvalidClosedAttribute;
                    }

                    condition = _factory.Binary(
                        IrBinaryOperator.NotEqual,
                        value,
                        _factory.Null(value.Type));
                    break;
                case ClosedContractAttributeKind.Positive:
                    if (value.Type != _factory.IntegerType)
                    {
                        return ContractBindingFailure.InvalidClosedAttribute;
                    }

                    condition = _factory.Binary(
                        IrBinaryOperator.GreaterThan,
                        value,
                        _factory.Integer(0));
                    break;
                case ClosedContractAttributeKind.InRange:
                    if (value.Type != _factory.IntegerType)
                    {
                        return ContractBindingFailure.InvalidClosedAttribute;
                    }

                    condition = _factory.Binary(
                        IrBinaryOperator.AndAlso,
                        _factory.Binary(
                            IrBinaryOperator.GreaterThanOrEqual,
                            value,
                            _factory.Integer(validation.Minimum)),
                        _factory.Binary(
                            IrBinaryOperator.LessThanOrEqual,
                            value,
                            _factory.Integer(validation.Maximum)));
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unknown closed contract attribute kind: " +
                        validation.Kind);
            }

            clauses.Add(new BoundContractClause(
                kind, condition, _factory.CreateOperation("closed-attribute"), BoundContractEvidence.ClosedAttribute));
        }
        return ContractBindingFailure.None;
    }

    private sealed class ClauseBindingResult(
        ImmutableArray<BoundContractClause> clauses, ContractBindingFailure failure)
    {
        internal ImmutableArray<BoundContractClause> Clauses { get; } = clauses;
        internal ContractBindingFailure Failure { get; } = failure;
        internal static ClauseBindingResult Empty { get; } = new([], ContractBindingFailure.None);
        internal static ClauseBindingResult Fail(ContractBindingFailure failure)
        {
            return new([], failure);
        }
    }

}
