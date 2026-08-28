namespace SharpProof.Contracts;

public sealed class ContractBinder(
    Compilation compilation,
    IrFactory factory,
    ContractClauseInventoryBuilder? clauseInventory = null)
{
    private readonly IrFactory _factory =
        ArgumentNullGuard.NotNull(factory, nameof(factory));
    private readonly ContractApiSymbols? _api = ContractApiSymbols.TryCreate(compilation);
    private readonly ContractIntrinsicValidator _intrinsics = new(compilation);
    private readonly ContractCanonicalization _canonicalization =
        new(compilation, factory);
    private readonly ContractClauseInventoryBuilder _clauseInventory =
        clauseInventory ?? ContractClauseInventoryBuilder.ForCompilation(compilation);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult> _bindings =
        new(SymbolEqualityComparer.IncludeNullability);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult> _requiresBindings =
        new(SymbolEqualityComparer.IncludeNullability);
    private readonly EffectiveContractSourceResolver _contractSources =
        clauseInventory == null
            ? EffectiveContractSourceResolver.ForCompilation(compilation)
            : new EffectiveContractSourceResolver(compilation, clauseInventory);

    public ContractBindingResult Bind(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));

        return implementationBody == null
            ? _bindings.GetOrAdd(target, BindUncached)
            : BindCore(target, implementationBody, requiresOnly: false);
    }

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));

        return implementationBody == null
            ? _requiresBindings.GetOrAdd(target, BindRequiresUncached)
            : BindCore(target, implementationBody, requiresOnly: true);
    }

    public ContractClauseInventory GetClauseInventory(IMethodSymbol target)
    {
        return _clauseInventory.Create(
            ContractClauseInventoryBuilder.NormalizeCallable(
                ArgumentNullGuard.NotNull(target, nameof(target))));
    }

    private ContractBindingResult BindUncached(IMethodSymbol target)
    {
        return BindCore(target, null, requiresOnly: false);
    }

    private ContractBindingResult BindRequiresUncached(IMethodSymbol target)
    {
        return BindCore(target, null, requiresOnly: true);
    }

    private ContractBindingResult BindCore(
        IMethodSymbol target,
        IOperation? implementationBody,
        bool requiresOnly)
    {
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
                MethodKind.ExplicitInterfaceImplementation))
        {
            return ContractBindingResult.Fail(ContractBindingFailure.UnsupportedTarget);
        }

        var resolution = _contractSources.Resolve(target, implementationBody);
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
        }
        var resolutionFailure = requiresOnly
            ? GetRequiresFailure(resolution)
            : resolution.Failure;
        if (resolutionFailure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(resolutionFailure);
        }

        var source = resolution.Source;
        var inventory = resolution.Inventory;
        var usesCompanion = resolution.UsesCompanion;
        var typeSpecializer = _canonicalization.CreateTypeSpecializer(source);
        if (typeSpecializer == null)
        {
            return ContractBindingResult.Fail(
                ContractBindingFailure.UnsupportedExpression);
        }
        var expressionBinder = new ContractExpressionBinder(
            _factory,
            _api,
            source,
            typeSpecializer);
        var invocationResult = BindInvocations(expressionBinder, inventory, usesCompanion, requiresOnly);
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

        var attributeResult = BindClosedAttributes(
            target,
            source,
            usesCompanion,
            canonical,
            requiresOnly);
        if (attributeResult.Failure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(attributeResult.Failure);
        }

        if (target.MethodKind == MethodKind.ExplicitInterfaceImplementation &&
            (invocationResult.Clauses.Any(static clause =>
                 clause.Kind == BoundContractKind.Requires) ||
             attributeResult.Clauses.Any(static clause =>
                 clause.Kind == BoundContractKind.Requires)) &&
            !HasInterfacePreconditions(target))
        {
            return ContractBindingResult.Fail(
                ContractBindingFailure.UnsupportedTarget);
        }

        clauses.AddRange(attributeResult.Clauses);

        return ContractBindingResult.Success(new BoundMethodContracts(
            target, source, clauses.ToImmutable(), canonical.ToBoundVariables(), usesCompanion));
    }

    private ClauseBindingResult BindInvocations(
        ContractExpressionBinder expressionBinder,
        ContractClauseInventory inventory,
        bool usesCompanion,
        bool requiresOnly)
    {
        var body = inventory.ImplementationBody;
        if (body == null)
        {
            return ClauseBindingResult.Empty;
        }

        var failure = ValidateIntrinsics(inventory.Callable, body, requiresOnly);
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
            if (!requiresOnly ||
                violation.EnclosingClauseKind is null ||
                violation.EnclosingClauseKind == BoundContractKind.Requires)
            {
                return violation.Failure;
            }
        }

        return ContractBindingFailure.None;
    }

    private static ContractBindingFailure GetRequiresFailure(
        EffectiveContractSourceResolution resolution)
    {
        if (resolution.Failure != ContractBindingFailure.InvalidClausePlacement)
        {
            return resolution.Failure;
        }

        var direct = resolution.DirectInventory;
        if (direct.HasPlacementErrors &&
            !direct.Clauses.Any(static clause => clause.IsValid))
        {
            // A malformed direct contract must not expose a valid companion
            // merely because the caller requested only entry requirements.
            return ContractBindingFailure.InvalidClausePlacement;
        }

        return resolution.Inventory.Clauses.Any(clause =>
                clause.Kind == BoundContractKind.Requires &&
                !clause.IsValid &&
                clause.Placement != ContractClausePlacement.NestedCallable)
            ? ContractBindingFailure.InvalidClausePlacement
            : ContractBindingFailure.None;
    }

    private bool HasInterfacePreconditions(IMethodSymbol implementation)
    {
        if (implementation.ExplicitInterfaceImplementations.IsDefaultOrEmpty)
        {
            return false;
        }

        return implementation.ExplicitInterfaceImplementations.All(
            interfaceMember =>
            {
                var binding = BindRequires(interfaceMember);
                return binding is { IsSuccess: true, Contracts: not null } &&
                    binding.Contracts.Clauses.Any(static clause =>
                        clause.Kind == BoundContractKind.Requires);
            });
    }

    private ClauseBindingResult BindClosedAttributes(
        IMethodSymbol target,
        IMethodSymbol source,
        bool usesCompanion,
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

        if (usesCompanion)
        {
            var offset = variables.Receiver.HasValue ? 1 : 0;
            if (variables.Receiver is { } receiver &&
                source.Parameters.Length > 0)
            {
                var result = BindValueAttributes(
                    source.Parameters[0].GetAttributes(),
                    source.Parameters[0].Type,
                    source.Parameters[0].RefKind,
                    _factory.Variable(receiver),
                    BoundContractKind.Requires,
                    clauses);
                if (result != ContractBindingFailure.None)
                {
                    return ClauseBindingResult.Fail(result);
                }
            }

            for (var index = 0; index < target.Parameters.Length; index++)
            {
                var sourceIndex = index + offset;
                if (sourceIndex >= source.Parameters.Length)
                {
                    return ClauseBindingResult.Fail(
                        ContractBindingFailure.CompanionSignatureMismatch);
                }

                var parameter = source.Parameters[sourceIndex];
                var result = BindValueAttributes(
                    parameter.GetAttributes(),
                    parameter.Type,
                    parameter.RefKind,
                    _factory.Variable(variables.Parameters[index]),
                    BoundContractKind.Requires,
                    clauses);
                if (result != ContractBindingFailure.None)
                {
                    return ClauseBindingResult.Fail(result);
                }
            }
        }

        if (!requiresOnly && !variables.Result.HasValue)
        {
            var returnResult = ValidateValueAttributes(
                target.GetReturnTypeAttributes(), target.ReturnType, RefKind.None);
            if (returnResult != ContractBindingFailure.None)
            {
                return ClauseBindingResult.Fail(returnResult);
            }

            if (usesCompanion)
            {
                var companionResult = ValidateValueAttributes(
                    source.GetReturnTypeAttributes(),
                    source.ReturnType,
                    RefKind.None);
                if (companionResult != ContractBindingFailure.None)
                {
                    return ClauseBindingResult.Fail(companionResult);
                }
            }
        }

        if (!requiresOnly && variables.Result.HasValue)
        {
            var result = BindValueAttributes(
                target.GetReturnTypeAttributes(), target.ReturnType, RefKind.None,
                _factory.Variable(variables.Result.Value),
                BoundContractKind.Ensures, clauses);
            if (result != ContractBindingFailure.None)
            {
                return ClauseBindingResult.Fail(result);
            }

            if (usesCompanion)
            {
                var companionResult = BindValueAttributes(
                    source.GetReturnTypeAttributes(),
                    source.ReturnType,
                    RefKind.None,
                    _factory.Variable(variables.Result.Value),
                    BoundContractKind.Ensures,
                    clauses);
                if (companionResult != ContractBindingFailure.None)
                {
                    return ClauseBindingResult.Fail(companionResult);
                }
            }
        }
        return new ClauseBindingResult(clauses.ToImmutable(), ContractBindingFailure.None);
    }

    private ContractBindingFailure ValidateValueAttributes(
        ImmutableArray<AttributeData> attributes,
        ITypeSymbol sourceType,
        RefKind refKind)
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
            if (validation.IsRecognized && !validation.IsValid)
            {
                return ContractBindingFailure.InvalidClosedAttribute;
            }
        }

        return ContractBindingFailure.None;
    }

    private ContractBindingFailure BindValueAttributes(
        ImmutableArray<AttributeData> attributes,
        ITypeSymbol sourceType,
        RefKind refKind,
        IrTerm value,
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
