namespace SharpProof.Contracts;

internal sealed class BoundTotalMetadataRequires(int parameterOrdinal, AttributeData attribute,
    ClosedContractAttributeValidation validation, IrTerm value, IrTerm safeCondition)
{
    internal int ParameterOrdinal { get; } = parameterOrdinal;
    internal AttributeData Attribute { get; } = attribute;
    internal ClosedContractAttributeValidation Validation { get; } = validation;
    internal IrTerm Value { get; } = value;
    internal IrTerm SafeCondition { get; } = safeCondition;
}

internal sealed class TotalMetadataRequiresBindingResult(ImmutableArray<BoundTotalMetadataRequires> clauses,
    ContractBindingFailure failure)
{
    internal ImmutableArray<BoundTotalMetadataRequires> Clauses { get; } = clauses;
    internal ContractBindingFailure Failure { get; } = failure;
    internal bool IsSuccess => Failure == ContractBindingFailure.None;
}

public sealed class BoundTotalContractClause
{
    internal BoundTotalContractClause(BoundContractKind kind, GuardedExpression expression,
        OperationId sourceOperation, string diagnosticText, BoundContractEvidence evidence = BoundContractEvidence.CompilerBoundInvocation,
        SyntaxReference? sourceSyntax = null)
    {
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

public sealed class TotalContractBindingResult
{
    internal TotalContractBindingResult(ImmutableArray<BoundTotalContractClause> clauses, ContractBindingFailure failure, object origin)
    {
        Clauses = clauses;
        Failure = failure;
        Origin = origin;
    }
    public ImmutableArray<BoundTotalContractClause> Clauses { get; }
    public ContractBindingFailure Failure { get; }
    public bool IsSuccess => Failure == ContractBindingFailure.None;
    internal object Origin { get; }
}

public sealed partial class ContractBinder
{
    // Explicit candidate adapter. Legacy Bind and its canonical model remain
    // authoritative while the native worker route is qualified.
    public TotalContractBindingResult BindTotal(TotalLoweringContext context, IOperation? implementationBody = null)
    { return BindTotalCore(context, implementationBody, requiresOnly: false); }

    public TotalContractBindingResult BindTotalRequires(TotalLoweringContext context, IOperation? implementationBody = null)
    { return BindTotalCore(context, implementationBody, requiresOnly: true); }

    // Binding does not authenticate a reference image. The collector must attach
    // the selected attribute's metadata ownership before recording an obligation.
    internal TotalMetadataRequiresBindingResult BindTotalMetadataRequires(TotalLoweringContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullGuard.NotNull(context, nameof(context));
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(_factory, context.Factory))
        { throw new ArgumentException("The context belongs to another factory.", nameof(context)); }
        if (_api == null)
        { return new([], ContractBindingFailure.ContractApiUnavailable); }
        if (!context.HasScalarSignature || context.Target.DeclaringSyntaxReferences.Length != 0)
        { return new([], ContractBindingFailure.UnsupportedTarget); }
        var clauses = ImmutableArray.CreateBuilder<BoundTotalMetadataRequires>();
        foreach (var site in ClosedContractAttributeValidator.EnumerateValueSites(context.Target, includeReturn: false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var kinds = new HashSet<ClosedContractAttributeKind>();
            foreach (var attribute in site.Attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_api.Selections.IsRejectedClosedContract(attribute))
                { return new([], ContractBindingFailure.InvalidClosedAttribute); }
                var validation = ClosedContractAttributeValidator.Validate(attribute, site.Type, site.RefKind,
                    _api.Selections, includeUnsigned64: true);
                if (!validation.IsRecognized)
                { continue; }
                var constructor = attribute.AttributeConstructor;
                var expectedArguments = validation.Kind == ClosedContractAttributeKind.InRange ? 2 : 0;
                if (!validation.IsValid || !kinds.Add(validation.Kind) || constructor == null ||
                    constructor.MethodKind != MethodKind.Constructor || constructor.IsStatic ||
                    !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, attribute.AttributeClass) ||
                    constructor.Parameters.Length != expectedArguments ||
                    constructor.Parameters.Any(static parameter => parameter.RefKind != RefKind.None ||
                        parameter.Type.SpecialType != SpecialType.System_Int64) ||
                    attribute.ConstructorArguments.Length != expectedArguments || attribute.NamedArguments.Length != 0 ||
                    attribute.ConstructorArguments.Any(static argument => argument.Kind == TypedConstantKind.Error))
                { return new([], ContractBindingFailure.InvalidClosedAttribute); }
                var value = _factory.Variable(context.Parameters[site.ParameterIndex].Entry);
                var failure = TryBindTotalAttributePredicate(value, validation, out var condition);
                if (failure != ContractBindingFailure.None)
                { return new([], failure); }
                clauses.Add(new(site.ParameterIndex, attribute, validation, condition!, _factory.Boolean(true)));
            }
        }
        return new(clauses.ToImmutable(), ContractBindingFailure.None);
    }

    private TotalContractBindingResult BindTotalCore(TotalLoweringContext context, IOperation? implementationBody, bool requiresOnly)
    {
        ArgumentNullGuard.NotNull(context, nameof(context));
        if (!ReferenceEquals(_factory, context.Factory))
        {
            throw new ArgumentException("The context belongs to another factory.", nameof(context));
        }
        if (_api == null)
        { return Fail(ContractBindingFailure.ContractApiUnavailable); }
        if (!context.HasScalarSignature)
        { return Fail(ContractBindingFailure.UnsupportedTarget); }
        var resolution = _contractSources.Resolve(context.Target, implementationBody, CancellationToken.None);
        if (!resolution.HasValidDirectClause && context.Target.MethodKind == MethodKind.Ordinary)
        {
            var directFailure = ValidateIntrinsics(resolution.DirectInventory.Callable,
                resolution.DirectInventory.ImplementationBody, requiresOnly);
            if (directFailure != ContractBindingFailure.None)
            { return Fail(directFailure); }
        }
        if (resolution.Failure != ContractBindingFailure.None &&
            (!requiresOnly || resolution.Failure != ContractBindingFailure.InvalidClausePlacement || HasRequiresPlacementErrors(resolution.Inventory)))
        { return Fail(resolution.Failure); }
        var candidates = resolution.Inventory.Clauses
            .Where(static occurrence => occurrence.IsValid)
            .Select(static occurrence => new ContractInvocationCandidate(
                occurrence.Kind, occurrence.Invocation))
            .ToImmutableArray();
        var materialized = TotalContractInvocationMaterialization.Bind(
            context, _api, _intrinsics, resolution, candidates, requiresOnly);
        if (materialized.Failure != ContractBindingFailure.None)
        {
            return Fail(materialized.Failure);
        }
        var clauses = materialized.Clauses.ToBuilder();
        var attributeFailure = BindTotalAttributes(context, requiresOnly, clauses);
        if (attributeFailure != ContractBindingFailure.None)
        { return Fail(attributeFailure); }
        var ordinal = 0;
        foreach (var occurrence in resolution.Inventory.Clauses.Where(occurrence => occurrence.IsValid))
        {
            context.ExcludeSpecificationCall(occurrence.Invocation);
            if (requiresOnly)
            { continue; }
            var clause = clauses[ordinal++];
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
        ImmutableArray<BoundTotalContractClause>.Builder clauses)
    {
        foreach (var site in ClosedContractAttributeValidator.EnumerateValueSites(context.Target, includeReturn: !requiresOnly))
        {
            var name = site.IsReturn ? "result" : context.Target.Parameters[site.ParameterIndex].Name;
            var bound = ClosedContractAttributeBinding.BindValue(
                _factory, _api!.Selections, site.Attributes, site.Type, site.RefKind,
                () => site.IsReturn
                    ? context.Result is { } result ? _factory.Variable(result) : null
                    : _factory.Variable(context.Parameters[site.ParameterIndex].Entry), name,
                site.IsReturn ? BoundContractKind.Ensures : BoundContractKind.Requires,
                total: true, syntax => context.AttributeSite(syntax!));
            if (bound.Failure != ContractBindingFailure.None)
            {
                return bound.Failure;
            }
            foreach (var clause in bound.Clauses)
            {
                clauses.Add(new(clause.Kind,
                    new GuardedExpression(clause.Condition, clause.SafeCondition!, FrontendSubsetClassification.Exact),
                    clause.SourceOperation, clause.DiagnosticText, BoundContractEvidence.ClosedAttribute, clause.SourceSyntax));
            }
        }
        return ContractBindingFailure.None;
    }
    private ContractBindingFailure TryBindTotalAttributePredicate(IrTerm value,
        ClosedContractAttributeValidation validation, out IrTerm? condition)
    {
        var type = _factory.GetTypeInfo(value.Type);
        condition = validation.CreateTotalPredicate(_factory, value, type);
        return condition == null ? ContractBindingFailure.UnsupportedExpression : ContractBindingFailure.None;
    }
}
