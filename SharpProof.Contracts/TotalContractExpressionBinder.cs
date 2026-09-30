namespace SharpProof.Contracts;

public sealed class BoundTotalContractClause
{
    internal BoundTotalContractClause(BoundContractKind kind, GuardedExpression expression,
        OperationId sourceOperation, string diagnosticText)
    {
        Kind = kind;
        Value = expression.Value;
        SafeCondition = expression.SafeCondition;
        SourceOperation = sourceOperation;
        DiagnosticText = diagnosticText;
    }
    public BoundContractKind Kind { get; }
    public IrTerm Value { get; }
    public IrTerm SafeCondition { get; }
    public OperationId SourceOperation { get; }
    public string DiagnosticText { get; }
    public BoundContractEvidence Evidence { get; } = BoundContractEvidence.CompilerBoundInvocation;
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
    // authoritative; companion and closed-attribute typing is not enabled here.
    public TotalContractBindingResult BindTotal(TotalLoweringContext context, IOperation? implementationBody = null)
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
        if (resolution.Failure != ContractBindingFailure.None)
        { return Fail(resolution.Failure); }
        if (resolution.UsesCompanion || resolution.Inventory.ImplementationBody == null ||
            !context.OwnsBody(resolution.Inventory.ImplementationBody))
        {
            return Fail(ContractBindingFailure.UnsupportedTarget);
        }
        if (ClosedContractAttributeValidator.EnumerateValueSites(context.Target, includeReturn: true)
            .Any(site => site.Attributes.Any(attribute => _api.Selections.GetClosedContractKind(attribute) != ClosedContractAttributeKind.None)))
        {
            return Fail(ContractBindingFailure.UnsupportedExpression);
        }
        var intrinsicFailure = ValidateIntrinsics(context.Target, resolution.Inventory.ImplementationBody, requiresOnly: false);
        if (intrinsicFailure != ContractBindingFailure.None)
        { return Fail(intrinsicFailure); }
        RoslynTotalExpressionLowerer lowerer = null!;
        lowerer = new(context)
        {
            Intrinsic = (invocation, state) => _api.IsResult(invocation.TargetMethod)
                ? context.Result is { } result
                    ? new GuardedExpression(_factory.Variable(result), _factory.Boolean(true), FrontendSubsetClassification.Exact)
                    : null
                : _api.IsOld(invocation.TargetMethod) && invocation.Arguments.Length == 1
                    ? lowerer.LowerClause(invocation.Arguments[0].Value, TotalParameterState.PreState)
                    : null
        };
        var allowed = new HashSet<IrVarId>(context.Parameters.SelectMany(binding => new[] { binding.Entry, binding.Current, binding.PreState }));
        if (context.Result is { } resultVariable)
        { allowed.Add(resultVariable); }
        var clauses = ImmutableArray.CreateBuilder<BoundTotalContractClause>();
        foreach (var occurrence in resolution.Inventory.Clauses.Where(occurrence => occurrence.IsValid))
        {
            var invocation = occurrence.Invocation;
            var state = occurrence.Kind == BoundContractKind.Ensures ? TotalParameterState.Current : TotalParameterState.Entry;
            var expression = lowerer.LowerClause(invocation.Arguments[0].Value, state);
            if (!expression.Classification.IsExact ||
                IrTraversal.CollectVariables(expression.Value).Any(variable => !allowed.Contains(variable)) ||
                IrTraversal.CollectVariables(expression.SafeCondition).Any(variable => !allowed.Contains(variable)))
            {
                return Fail(ContractBindingFailure.UnsupportedExpression);
            }
            if (expression.Value.Type != _factory.BooleanType)
            { return Fail(ContractBindingFailure.NonBooleanCondition); }
            clauses.Add(new(occurrence.Kind, expression, context.Site(invocation), FormatDiagnosticSourceText(invocation.Arguments[0].Value.Syntax)));
        }
        foreach (var occurrence in resolution.Inventory.Clauses.Where(occurrence => occurrence.IsValid))
        {
            context.ExcludeSpecificationCall(occurrence.Invocation);
        }
        return new(clauses.ToImmutable(), ContractBindingFailure.None, context.Origin);

        TotalContractBindingResult Fail(ContractBindingFailure failure)
        {
            return new([], failure, context.Origin);
        }
    }
}
