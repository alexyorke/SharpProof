namespace SharpProof.Contracts;

public sealed class ContractBinder(
    Compilation compilation,
    IrFactory factory,
    ContractClauseInventoryBuilder? clauseInventory = null)
{
    private readonly IrFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly ContractApiSymbols? _api = ContractApiSymbols.TryCreate(compilation);
    private readonly ContractIntrinsicValidator _intrinsics = new(compilation);
    private readonly ContractCanonicalization _canonicalization =
        new(compilation, factory);
    private readonly ContractClauseInventoryBuilder _clauseInventory =
        clauseInventory ?? ContractClauseInventoryBuilder.ForCompilation(compilation);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult> _bindings =
        new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, ContractBindingResult> _requiresBindings =
        new(SymbolEqualityComparer.Default);
    private readonly ImmutableArray<ContractForSymbolMatcher.CompanionDescriptor> _companions =
        compilation == null ? [] : ContractForSymbolMatcher.DiscoverCompanions(compilation);

    public ContractBindingResult Bind(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        return implementationBody == null
            ? _bindings.GetOrAdd(target, BindUncached)
            : BindCore(target, implementationBody, requiresOnly: false);
    }

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        return implementationBody == null
            ? _requiresBindings.GetOrAdd(target, BindRequiresUncached)
            : BindCore(target, implementationBody, requiresOnly: true);
    }

    public ContractClauseInventory GetClauseInventory(IMethodSymbol target)
    {
        return _clauseInventory.Create(
            ContractClauseInventoryBuilder.NormalizeCallable(target ?? throw new ArgumentNullException(nameof(target))));
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
                MethodKind.Ordinary or MethodKind.Constructor))
        {
            return ContractBindingResult.Fail(ContractBindingFailure.UnsupportedTarget);
        }

        var source = ContractClauseInventoryBuilder.NormalizeCallable(target);
        var usesCompanion = false;
        var inventory = _clauseInventory.Create(source, implementationBody);
        var sourceBody = inventory.ImplementationBody;
        var hasValidDirectClause = inventory.Clauses.Any(static clause => clause.IsValid);
        if (!hasValidDirectClause && target.MethodKind == MethodKind.Ordinary)
        {
            var directFailure = ValidateIntrinsics(source, sourceBody, requiresOnly);
            if (directFailure != ContractBindingFailure.None)
            {
                return ContractBindingResult.Fail(directFailure);
            }

            var companion = ContractForSymbolMatcher.ResolveCompanion(_companions, target);
            if (companion.Failure != ContractBindingFailure.None)
            {
                return ContractBindingResult.Fail(companion.Failure);
            }

            if (companion.Method != null)
            {
                source = companion.Method;
                inventory = _clauseInventory.Create(source);
                sourceBody = inventory.ImplementationBody;
                if (sourceBody == null)
                {
                    return ContractBindingResult.Fail(ContractBindingFailure.CompanionBodyUnavailable);
                }

                usesCompanion = true;
                if (inventory.HasPlacementErrors)
                {
                    return ContractBindingResult.Fail(ContractBindingFailure.InvalidClausePlacement);
                }
            }
        }
        if (!usesCompanion && inventory.HasPlacementErrors)
        {
            return ContractBindingResult.Fail(ContractBindingFailure.InvalidClausePlacement);
        }

        var expressionBinder = new ContractExpressionBinder(
            _factory,
            _api,
            source,
            _canonicalization.CreateTypeSpecializer(source));
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

        var attributeResult = BindClosedAttributes(target, canonical, requiresOnly);
        if (attributeResult.Failure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(attributeResult.Failure);
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
        }
        return new ClauseBindingResult(clauses.ToImmutable(), ContractBindingFailure.None);
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
            var isNotNull = ContractSelectionInventory.Is(attribute, _api!.Selections.NotNull);
            var isPositive = ContractSelectionInventory.Is(attribute, _api.Selections.Positive);
            var isInRange = ContractSelectionInventory.Is(attribute, _api.Selections.InRange);
            if (!isNotNull && !isPositive && !isInRange)
            {
                continue;
            }

            if (refKind == RefKind.Out)
            {
                return ContractBindingFailure.InvalidClosedAttribute;
            }

            IrTerm? condition = null;
            if (isNotNull)
            {
                var type = _factory.GetTypeInfo(value.Type);
                if (!sourceType.IsReferenceType ||
                    type.Kind is not (IrTypeKind.Reference or IrTypeKind.String or IrTypeKind.Sequence))
                {
                    return ContractBindingFailure.InvalidClosedAttribute;
                }

                condition = _factory.Binary(IrBinaryOperator.NotEqual, value, _factory.Null(value.Type));
            }
            else if (isPositive)
            {
                if (!IsSupportedInteger(sourceType) || value.Type != _factory.IntegerType)
                {
                    return ContractBindingFailure.InvalidClosedAttribute;
                }

                condition = _factory.Binary(IrBinaryOperator.GreaterThan, value, _factory.Integer(0));
            }
            else
            {
                if (!IsSupportedInteger(sourceType) ||
                    value.Type != _factory.IntegerType ||
                    attribute.ConstructorArguments.Length != 2 ||
                    !TryGetInt64(attribute.ConstructorArguments[0], out var minimum) ||
                    !TryGetInt64(attribute.ConstructorArguments[1], out var maximum) ||
                    minimum > maximum)
                {
                    return ContractBindingFailure.InvalidClosedAttribute;
                }

                condition = _factory.Binary(
                    IrBinaryOperator.AndAlso,
                    _factory.Binary(IrBinaryOperator.GreaterThanOrEqual, value, _factory.Integer(minimum)),
                    _factory.Binary(IrBinaryOperator.LessThanOrEqual, value, _factory.Integer(maximum)));
            }
            if (condition == null)
            {
                continue;
            }

            clauses.Add(new BoundContractClause(
                kind, condition, _factory.CreateOperation("closed-attribute"), BoundContractEvidence.ClosedAttribute));
        }
        return ContractBindingFailure.None;
    }

    private static bool IsSupportedInteger(ITypeSymbol type)
    {
        return CSharpScalarSemantics.IsSupportedInteger(type.SpecialType);
    }

    private static bool TryGetInt64(TypedConstant value, out long result)
    {
        if (value.Value is long number)
        {
            result = number;
            return true;
        }
        result = 0;
        return false;
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
