namespace SharpProof.Compiler.Semantics;

internal sealed class BoundTotalMetadataRequires(int parameterOrdinal, AttributeData attribute,
    ClosedContractAttributeValidation validation, IrTerm value, IrTerm safeCondition)
{
    internal int ParameterOrdinal { get; } = parameterOrdinal;
    internal AttributeData Attribute { get; } = (attribute ?? throw new System.ArgumentNullException(nameof(attribute)));
    internal ClosedContractAttributeValidation Validation { get; } = validation.IsRecognized ? validation : throw new ArgumentException("A recognized contract is required.", nameof(validation));
    internal IrTerm Value { get; } = (value ?? throw new System.ArgumentNullException(nameof(value)));
    internal IrTerm SafeCondition { get; } = (safeCondition ?? throw new System.ArgumentNullException(nameof(safeCondition)));
}

internal sealed class TotalMetadataRequiresBindingResult(ImmutableArray<BoundTotalMetadataRequires> clauses,
    ContractBindingFailure failure)
{
    internal ImmutableArray<BoundTotalMetadataRequires> Clauses { get; } = (clauses.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(clauses)) : clauses);
    internal ContractBindingFailure Failure { get; } = failure;
    internal bool IsSuccess => Failure == ContractBindingFailure.None;
}

internal sealed class BoundTotalContractClause
{
    internal BoundTotalContractClause(BoundContractKind kind, GuardedExpression expression,
        OperationId sourceOperation, string diagnosticText, BoundContractEvidence evidence = BoundContractEvidence.CompilerBoundInvocation,
        SyntaxReference? sourceSyntax = null)
    {
        RepositoryParameterGuard.ThrowIfInvalid(sourceOperation.IsDefault, nameof(sourceOperation));
        diagnosticText = RepositoryParameterGuard.NotNull(diagnosticText, nameof(diagnosticText));
        expression = RepositoryParameterGuard.NotNull(expression, nameof(expression));
        Kind = kind;
        Value = expression.Value;
        SafeCondition = expression.SafeCondition;
        SourceOperation = sourceOperation;
        DiagnosticText = diagnosticText;
        Evidence = evidence;
        SourceSyntax = sourceSyntax;
    }
    public BoundContractKind Kind { get; }
    public IrTerm Value { get; }
    public IrTerm SafeCondition { get; }
    public OperationId SourceOperation { get; }
    public string DiagnosticText { get; }
    public BoundContractEvidence Evidence { get; }
    public SyntaxReference? SourceSyntax { get; }
}

internal sealed class TotalContractBindingResult
{
    internal TotalContractBindingResult(ImmutableArray<BoundTotalContractClause> clauses, ContractBindingFailure failure, object origin)
    {

        RepositoryParameterGuard.RequireInitialized(clauses.IsDefault, nameof(clauses), "Array must be initialized.");
        origin = RepositoryParameterGuard.NotNull(origin, nameof(origin));
        Clauses = clauses;
        Failure = failure;
        Origin = origin;
    }
    public ImmutableArray<BoundTotalContractClause> Clauses { get; }
    public ContractBindingFailure Failure { get; }
    public bool IsSuccess => Failure == ContractBindingFailure.None;
    internal object Origin { get; }
}

// Lowers contract clauses to IR terms in one factory. Clause discovery,
// source resolution and intrinsic validation are shared with ContractBinder.
internal sealed class TotalContractBinder
{
    private readonly ContractBinder _classifier;
    private readonly IrFactory _factory;

    public TotalContractBinder(
        Compilation compilation,
        IrFactory factory,
        ContractClauseInventoryBuilder? clauseInventory = null)
        : this(new ContractBinder(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)), clauseInventory), factory)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
    }

    internal TotalContractBinder(ContractBinder classifier, IrFactory factory)
    {
        _classifier = ArgumentNullGuard.NotNull(classifier, nameof(classifier));
        _factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
    }

    public TotalContractBindingResult BindTotal(TotalLoweringContext context, IOperation? implementationBody = null)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
        return BindTotalCore(context, implementationBody, requiresOnly: false, cancellation.Token);
    }

    public TotalContractBindingResult BindTotal(TotalLoweringContext context, CancellationToken cancellationToken)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        return BindTotalCore(context, null, requiresOnly: false, cancellationToken);
    }

    public TotalContractBindingResult BindTotal(TotalLoweringContext context, IOperation? implementationBody,
        CancellationToken cancellationToken)
    { context = RepositoryParameterGuard.NotNull(context, nameof(context)); return BindTotalCore(context, implementationBody, requiresOnly: false, cancellationToken); }

    public TotalContractBindingResult BindTotalRequires(TotalLoweringContext context, IOperation? implementationBody = null)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(SharpProof.Ir.AnalysisDefaults.OperationTimeoutMinutes));
        return BindTotalCore(context, implementationBody, requiresOnly: true, cancellation.Token);
    }

    public TotalContractBindingResult BindTotalRequires(TotalLoweringContext context, CancellationToken cancellationToken)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        return BindTotalCore(context, null, requiresOnly: true, cancellationToken);
    }

    public TotalContractBindingResult BindTotalRequires(TotalLoweringContext context, IOperation? implementationBody,
        CancellationToken cancellationToken)
    { context = RepositoryParameterGuard.NotNull(context, nameof(context)); return BindTotalCore(context, implementationBody, requiresOnly: true, cancellationToken); }

    // Binding does not authenticate a reference image. The collector must attach
    // the selected attribute's metadata ownership before recording an obligation.
    internal TotalMetadataRequiresBindingResult BindTotalMetadataRequires(TotalLoweringContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(context, nameof(context));
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_factory, context.Factory))
        { throw new ArgumentException("The context belongs to another factory.", nameof(context)); }
        if (_classifier.Api == null)
        { return new([], ContractBindingFailure.ContractApiUnavailable); }
        if (!context.HasScalarSignature || context.Target.DeclaringSyntaxReferences.Length != 0)
        { return new([], ContractBindingFailure.UnsupportedTarget); }
        var sites = context.Target.Parameters.Select(parameter => new ContractMetadataValueSite(
            parameter, () => _factory.Variable(context.Parameters[parameter.Ordinal].Entry)));
        return ClosedContractAttributeBinding.BindMetadataValueSites(
            _factory, _classifier.Api.Selections, sites, cancellationToken);
    }

    // The Requires a companion in this compilation declares for a callee in
    // another assembly. The callee's own closed attributes are not bound here:
    // they come from its authenticated image through BindTotalMetadataRequires.
    internal TotalContractBindingResult BindTotalCompanionRequires(TotalLoweringContext context,
        CancellationToken cancellationToken)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        if (context.Target.DeclaringSyntaxReferences.Length != 0)
        { return new([], ContractBindingFailure.UnsupportedTarget, context.Origin); }
        if (_classifier.Api == null)
        { return new([], ContractBindingFailure.ContractApiUnavailable, context.Origin); }
        // A companion type that declares no member of this name does not
        // cover the callee, which then has no companion clause. A companion
        // that is ambiguous or does not match is a failure, so the call stays
        // unchecked.
        var resolution = _classifier.ContractSources.Resolve(context.Target, null, cancellationToken);
        if (!resolution.UsesCompanion)
        {
            return new([], resolution.Failure == ContractBindingFailure.MissingCompanion ? ContractBindingFailure.None : resolution.Failure,
                context.Origin);
        }
        return BindTotalCore(context, null, requiresOnly: true, cancellationToken, includeAttributes: false);
    }

    // The Requires written for a callable in this compilation, for its
    // contract export. Closed parameter attributes are left out: they travel
    // in the callable's binary and are checked from there.
    internal TotalContractBindingResult BindTotalSourceRequires(TotalLoweringContext context,
        CancellationToken cancellationToken)
    {
        context = RepositoryParameterGuard.NotNull(context, nameof(context));
        if (context.Target.DeclaringSyntaxReferences.Length == 0)
        { return new([], ContractBindingFailure.UnsupportedTarget, context.Origin); }
        return BindTotalCore(context, null, requiresOnly: true, cancellationToken, includeAttributes: false);
    }

    private TotalContractBindingResult BindTotalCore(TotalLoweringContext context, IOperation? implementationBody,
        bool requiresOnly, CancellationToken cancellationToken, bool includeAttributes = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullGuard.NotNull(context, nameof(context));
        if (!ReferenceEquals(_factory, context.Factory))
        {
            throw new ArgumentException("The context belongs to another factory.", nameof(context));
        }
        if (_classifier.Api == null)
        { return Fail(ContractBindingFailure.ContractApiUnavailable); }
        if (!context.HasScalarSignature)
        { return Fail(ContractBindingFailure.UnsupportedTarget); }
        var resolution = _classifier.ContractSources.Resolve(context.Target, implementationBody, cancellationToken);
        if (!resolution.HasValidDirectClause && context.Target.MethodKind == MethodKind.Ordinary)
        {
            var directFailure = _classifier.ValidateIntrinsics(resolution.DirectInventory.Callable,
                resolution.DirectInventory.ImplementationBody, requiresOnly);
            if (directFailure != ContractBindingFailure.None)
            { return Fail(directFailure); }
        }
        if (resolution.Failure != ContractBindingFailure.None &&
            (!requiresOnly || resolution.Failure != ContractBindingFailure.InvalidClausePlacement || ContractBinder.HasRequiresPlacementErrors(resolution.Inventory)))
        { return Fail(resolution.Failure); }
        var candidates = resolution.Inventory.Clauses
            .Where(static occurrence => occurrence.IsValid)
            .Select(static occurrence => new ContractInvocationCandidate(
                occurrence.Kind, occurrence.Invocation))
            .ToImmutableArray();
        var materialized = TotalContractInvocationMaterialization.Bind(
            context, _classifier.Api, _classifier.Intrinsics, resolution, candidates, requiresOnly);
        if (materialized.Failure != ContractBindingFailure.None)
        {
            return Fail(materialized.Failure);
        }
        var clauses = materialized.Clauses.ToBuilder();
        void AppendClause(BoundTotalContractClause clause)
        {
            clauses.Add(clause);
        }
        var attributeFailure = includeAttributes ? BindTotalAttributes(context, requiresOnly, AppendClause) : ContractBindingFailure.None;
        if (attributeFailure != ContractBindingFailure.None)
        { return Fail(attributeFailure); }
        var ordinal = 0;
        foreach (var occurrence in resolution.Inventory.Clauses.Where(occurrence => occurrence.IsValid))
        {
            context.ExcludeSpecificationCall(occurrence.Invocation);
            if (requiresOnly)
            { continue; }
            var clause = materialized.Clauses[ordinal++];
            if (clause.Kind == BoundContractKind.Assume)
            {
                context.RegisterSpecificationAssumption(occurrence.Invocation,
                    _factory.Binary(IrBinaryOperator.AndAlso, clause.SafeCondition, clause.Value), clause.SourceOperation, atEntry: resolution.UsesCompanion);
            }
        }
        return new(clauses.ToImmutable(), ContractBindingFailure.None, context.Origin);

        TotalContractBindingResult Fail(ContractBindingFailure failure)
        {
            return new([], failure, context.Origin);
        }
    }

    private ContractBindingFailure BindTotalAttributes(TotalLoweringContext context, bool requiresOnly,
        Action<BoundTotalContractClause> clauses)
    {
        foreach (var site in ClosedContractAttributeValidator.EnumerateValueSites(context.Target, includeReturn: !requiresOnly))
        {
            var name = site.IsReturn ? "result" : context.Target.Parameters[site.ParameterIndex].Name;
            var bound = ClosedContractAttributeBinding.BindValue(
                _factory, _classifier.Api!.Selections, site.Attributes, site.Type, site.RefKind,
                () => site.IsReturn
                    ? context.Result is { } result ? _factory.Variable(result) : null
                    : _factory.Variable(context.Parameters[site.ParameterIndex].Entry), name,
                site.IsReturn ? BoundContractKind.Ensures : BoundContractKind.Requires,
                syntax => context.AttributeSite(syntax!));
            if (bound.Failure != ContractBindingFailure.None)
            {
                return bound.Failure;
            }
            foreach (var clause in bound.Clauses)
            {
                clauses(new(clause.Kind,
                    new GuardedExpression(clause.Condition, clause.SafeCondition!, FrontendSubsetClassification.Exact),
                    clause.SourceOperation, clause.DiagnosticText, BoundContractEvidence.ClosedAttribute, clause.SourceSyntax));
            }
        }
        return ContractBindingFailure.None;
    }
}
