namespace SharpProof.Contracts;

public sealed class BoundTotalContractClause
{
    internal BoundTotalContractClause(BoundContractKind kind, GuardedExpression expression,
        OperationId sourceOperation, string diagnosticText, BoundContractEvidence evidence = BoundContractEvidence.CompilerBoundInvocation)
    {
        Kind = kind;
        Value = expression.Value;
        SafeCondition = expression.SafeCondition;
        SourceOperation = sourceOperation;
        DiagnosticText = diagnosticText;
        Evidence = evidence;
    }
    public BoundContractKind Kind { get; }
    public IrTerm Value { get; }
    public IrTerm SafeCondition { get; }
    public OperationId SourceOperation { get; }
    public string DiagnosticText { get; }
    public BoundContractEvidence Evidence { get; }
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
        if (resolution.Failure != ContractBindingFailure.None &&
            (!requiresOnly || resolution.Failure != ContractBindingFailure.InvalidClausePlacement || HasRequiresPlacementErrors(resolution.Inventory)))
        { return Fail(resolution.Failure); }
        var omitReceiver = resolution.UsesCompanion && !context.Target.IsStatic;
        if (omitReceiver && (resolution.Source.Parameters.Length == 0 || resolution.Source.Parameters[0].RefKind != RefKind.None ||
            !SymbolEqualityComparer.Default.Equals(resolution.Source.Parameters[0].Type, context.Target.ContainingType)))
        { return Fail(ContractBindingFailure.UnsupportedTarget); }
        var clauseContext = resolution.UsesCompanion ? context.CreateContractFrame(resolution.Source, omitReceiver) : context;
        if (!clauseContext.HasScalarSignature || resolution.Inventory.ImplementationBody == null ||
            !clauseContext.OwnsBody(resolution.Inventory.ImplementationBody) ||
            clauseContext.Parameters.Length != context.Parameters.Length)
        {
            return Fail(ContractBindingFailure.UnsupportedTarget);
        }
        var replacements = new Dictionary<IrVarId, IrTerm>();
        if (resolution.UsesCompanion)
        {
            for (var index = 0; index < context.Parameters.Length; index++)
            {
                var source = clauseContext.Parameters[index];
                var target = context.Parameters[index];
                if (_factory.GetVariableInfo(source.Entry).Type != _factory.GetVariableInfo(target.Entry).Type)
                { return Fail(ContractBindingFailure.UnsupportedExpression); }
                replacements.Add(source.Entry, _factory.Variable(target.Entry));
                replacements.Add(source.Current, _factory.Variable(target.Current));
                replacements.Add(source.PreState, _factory.Variable(target.PreState));
            }
        }
        var intrinsicFailure = ValidateIntrinsics(resolution.Source, resolution.Inventory.ImplementationBody, requiresOnly);
        if (intrinsicFailure != ContractBindingFailure.None)
        { return Fail(intrinsicFailure); }
        RoslynTotalExpressionLowerer lowerer = null!;
        lowerer = new(clauseContext)
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
        foreach (var occurrence in resolution.Inventory.Clauses.Where(occurrence => occurrence.IsValid &&
            (!requiresOnly || occurrence.Kind == BoundContractKind.Requires)))
        {
            var invocation = occurrence.Invocation;
            var state = occurrence.Kind == BoundContractKind.Requires ? TotalParameterState.Entry : TotalParameterState.Current;
            var expression = lowerer.LowerClause(invocation.Arguments[0].Value, state);
            if (resolution.UsesCompanion && expression.Classification.IsExact)
            {
                expression = new GuardedExpression(IrSubstitution.Substitute(_factory, expression.Value, replacements),
                    IrSubstitution.Substitute(_factory, expression.SafeCondition, replacements), expression.Classification);
            }
            if (!expression.Classification.IsExact ||
                IrTraversal.CollectVariables(expression.Value).Any(variable => !allowed.Contains(variable)) ||
                IrTraversal.CollectVariables(expression.SafeCondition).Any(variable => !allowed.Contains(variable)))
            {
                return Fail(ContractBindingFailure.UnsupportedExpression);
            }
            if (expression.Value.Type != _factory.BooleanType)
            { return Fail(ContractBindingFailure.NonBooleanCondition); }
            clauses.Add(new(occurrence.Kind, expression, context.Site(invocation), FormatDiagnosticSourceText(invocation.Arguments[0].Value.Syntax),
                resolution.UsesCompanion ? BoundContractEvidence.Companion : BoundContractEvidence.CompilerBoundInvocation));
        }
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
                    _factory.Binary(IrBinaryOperator.AndAlso, clause.SafeCondition, clause.Value), clause.SourceOperation);
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
            foreach (var attribute in site.Attributes)
            {
                if (_api!.Selections.IsRejectedClosedContract(attribute))
                { return ContractBindingFailure.InvalidClosedAttribute; }
                var validation = ClosedContractAttributeValidator.Validate(attribute, site.Type, site.RefKind, _api.Selections, includeUnsigned64: true);
                if (!validation.IsRecognized)
                { continue; }
                if (!validation.IsValid || attribute.ApplicationSyntaxReference?.GetSyntax() is not { } syntax ||
                    site.IsReturn && context.Result == null)
                { return ContractBindingFailure.InvalidClosedAttribute; }
                var variable = site.IsReturn ? context.Result!.Value : context.Parameters[site.ParameterIndex].Entry;
                var value = _factory.Variable(variable);
                var type = _factory.GetTypeInfo(value.Type);
                IrTerm condition;
                if (validation.Kind == ClosedContractAttributeKind.NotNull &&
                    type.Kind is IrTypeKind.Reference or IrTypeKind.String or IrTypeKind.Sequence)
                { condition = _factory.Binary(IrBinaryOperator.NotEqual, value, _factory.Null(value.Type)); }
                else if (type.Kind == IrTypeKind.Integer && type.Width > 0)
                {
                    condition = validation.Kind switch
                    {
                        ClosedContractAttributeKind.Positive => Compare(IrBinaryOperator.GreaterThan, 0),
                        ClosedContractAttributeKind.InRange => _factory.Binary(IrBinaryOperator.AndAlso,
                            Compare(IrBinaryOperator.GreaterThanOrEqual, validation.Minimum),
                            Compare(IrBinaryOperator.LessThanOrEqual, validation.Maximum)),
                        _ => null!
                    };
                    if (condition == null)
                    { return ContractBindingFailure.UnsupportedExpression; }
                }
                else
                { return ContractBindingFailure.UnsupportedExpression; }
                var name = site.IsReturn ? "result" : context.Target.Parameters[site.ParameterIndex].Name;
                clauses.Add(new(site.IsReturn ? BoundContractKind.Ensures : BoundContractKind.Requires,
                    new GuardedExpression(condition, _factory.Boolean(true), FrontendSubsetClassification.Exact),
                    context.AttributeSite(syntax), FormatClosedAttributeDiagnosticText(attribute, validation, name),
                    BoundContractEvidence.ClosedAttribute));

                IrTerm Compare(IrBinaryOperator op, long bound)
                {
                    // Fold bounds outside the scalar domain; never truncate an attribute's long bound.
                    var minimum = type.Signed ? type.Width == 64 ? long.MinValue : -(1L << (type.Width - 1)) : 0L;
                    var maximum = type.Signed ? type.Width == 64 ? (ulong)long.MaxValue : (1UL << (type.Width - 1)) - 1 :
                        type.Width == 64 ? ulong.MaxValue : (1UL << type.Width) - 1;
                    if (bound < minimum)
                    { return _factory.Boolean(op != IrBinaryOperator.LessThanOrEqual); }
                    if (bound >= 0 && (ulong)bound > maximum)
                    { return _factory.Boolean(op == IrBinaryOperator.LessThanOrEqual); }
                    return _factory.Binary(op, value, _factory.Integer(value.Type, bound));
                }
            }
        }
        return ContractBindingFailure.None;
    }
}
