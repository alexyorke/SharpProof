namespace SharpProof.Contracts;

public sealed class ContractBinder
{
    private const int MaximumDiagnosticConditionLength = 512;

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
        _intrinsics = new ContractIntrinsicValidator(_api);
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
        return BindCached(
            target,
            implementationBody,
            requiresOnly: false,
            cache: _bindings,
            cancellationToken: CancellationToken.None);
    }

    public ContractBindingResult BindRequires(
        IMethodSymbol target,
        IOperation? implementationBody = null)
    {
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCached(
            target,
            implementationBody,
            requiresOnly: true,
            cache: _requiresBindings,
            cancellationToken: CancellationToken.None);
    }

    internal ContractBindingResult BindRequires(
        IMethodSymbol target,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target));
        return BindCached(
            target,
            implementationBody: null,
            requiresOnly: true,
            cache: _requiresBindings,
            cancellationToken: cancellationToken);
    }

    public ContractClauseInventory GetClauseInventory(IMethodSymbol target)
    {
        return _clauseInventory.Create(target);
    }

    private ContractBindingResult BindCached(
        IMethodSymbol target,
        IOperation? implementationBody,
        bool requiresOnly,
        ConcurrentDictionary<IMethodSymbol, ContractBindingResult> cache,
        CancellationToken cancellationToken)
    {
        return implementationBody == null
            ? cache.GetOrAdd(
                target,
                value => BindCore(value, null, requiresOnly, cancellationToken))
            : BindCore(target, implementationBody, requiresOnly, cancellationToken);
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
                clause.Kind,
                condition,
                clause.SourceOperation,
                clause.Evidence,
                clause.DiagnosticText));
        }

        var attributeFailure = BindClosedAttributes(
            target, canonical, requiresOnly, clauses);
        if (attributeFailure != ContractBindingFailure.None)
        {
            return ContractBindingResult.Fail(attributeFailure);
        }

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
                    : BoundContractEvidence.CompilerBoundInvocation,
                FormatDiagnosticSourceText(
                    invocation.Arguments[0].Value.Syntax)));
        }
        return new ClauseBindingResult(clauses.ToImmutable(), ContractBindingFailure.None);
    }

    private static string FormatClosedAttributeDiagnosticText(
        AttributeData attribute,
        ClosedContractAttributeValidation validation,
        string valueName)
    {
        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax();
        if (syntax != null)
        {
            if ((long)syntax.Span.Length + valueName.Length + 3 >
                MaximumDiagnosticConditionLength)
            {
                return "[condition exceeds the display limit]";
            }

            return "[" + syntax + "] " + valueName;
        }

        var attributeText = validation.Kind switch
        {
            ClosedContractAttributeKind.NotNull => "[NotNull]",
            ClosedContractAttributeKind.Positive => "[Positive]",
            ClosedContractAttributeKind.InRange =>
                "[InRange(" + validation.Minimum.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + ", " +
                validation.Maximum.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + ")]",
            _ => "[closed contract]"
        };
        return (long)attributeText.Length + valueName.Length + 1 >
            MaximumDiagnosticConditionLength
            ? "[condition exceeds the display limit]"
            : attributeText + " " + valueName;
    }

    private static string FormatDiagnosticSourceText(SyntaxNode syntax)
    {
        return syntax.Span.Length > MaximumDiagnosticConditionLength
            ? "[condition exceeds the display limit]"
            : syntax.ToString();
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

    private ContractBindingFailure BindClosedAttributes(
        IMethodSymbol target,
        ContractCanonicalVariables variables,
        bool requiresOnly,
        ImmutableArray<BoundContractClause>.Builder clauses)
    {
        foreach (var site in ClosedContractAttributeValidator.EnumerateValueSites(
                     target,
                     includeReturn: !requiresOnly))
        {
            var value = site.IsReturn
                ? variables.Result.HasValue
                    ? _factory.Variable(variables.Result.Value)
                    : null
                : _factory.Variable(variables.Parameters[site.ParameterIndex]);
            var result = BindValueAttributes(
                site.Attributes,
                site.Type,
                site.RefKind,
                value,
                site.IsReturn
                    ? "return value"
                    : target.Parameters[site.ParameterIndex].Name,
                site.IsReturn
                    ? BoundContractKind.Ensures
                    : BoundContractKind.Requires,
                clauses);
            if (result != ContractBindingFailure.None)
            {
                return result;
            }
        }
        return ContractBindingFailure.None;
    }

    private ContractBindingFailure BindValueAttributes(
        ImmutableArray<AttributeData> attributes,
        ITypeSymbol sourceType,
        RefKind refKind,
        IrTerm? value,
        string valueName,
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

            var diagnosticText = FormatClosedAttributeDiagnosticText(
                attribute,
                validation,
                valueName);

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
                kind,
                condition,
                _factory.CreateOperation("closed-attribute"),
                BoundContractEvidence.ClosedAttribute,
                diagnosticText));
        }
        return ContractBindingFailure.None;
    }

    private sealed class ClauseBindingResult(
        ImmutableArray<BoundContractClause> clauses, ContractBindingFailure failure)
    {
        internal ImmutableArray<BoundContractClause> Clauses { get; } = clauses;
        internal ContractBindingFailure Failure { get; } = failure;
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "SharpProof.Soundness",
            "SPMETA002",
            Justification = "Empty binding result is an immutable value sentinel.")]
        internal static ClauseBindingResult Empty { get; } = new([], ContractBindingFailure.None);
        internal static ClauseBindingResult Fail(ContractBindingFailure failure)
        {
            return new([], failure);
        }
    }

}
