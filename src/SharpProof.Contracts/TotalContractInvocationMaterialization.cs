namespace SharpProof.Contracts;

// Prepares a compiler-derived source frame and binds raw single-argument
// clause candidates. Recognition and enrollment remain with the caller.
// Positive candidates must be canonical when compiler-invocation evidence
// is published; ordinary raw candidates are useful rejection inputs.
internal static class TotalContractInvocationMaterialization
{
    internal static TotalContractInvocationMaterializationResult Bind(
        TotalLoweringContext context,
        ContractApiSymbols api,
        ContractIntrinsicValidator intrinsics,
        EffectiveContractSourceResolution resolution,
        ImmutableArray<ContractInvocationCandidate> candidates,
        bool requiresOnly)
    {

        RepositoryParameterGuard.RequireInitialized(candidates.IsDefault, nameof(candidates), "Array must be initialized.");
        context = ArgumentNullGuard.NotNull(context, nameof(context));
        api = ArgumentNullGuard.NotNull(api, nameof(api));
        intrinsics = ArgumentNullGuard.NotNull(intrinsics, nameof(intrinsics));
        resolution = ArgumentNullGuard.NotNull(resolution, nameof(resolution));
        var factory = context.Factory;
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
        // OwnsBody authenticates this semantic model against the clause callable.
        // Resolve actual interceptors while binding clauses, before body lowering
        // has populated the context's compilation (including companion frames).
        clauseContext.Compilation = resolution.Inventory.ImplementationBody.SemanticModel!.Compilation;
        var replacements = new Dictionary<IrVarId, IrTerm>();
        if (resolution.UsesCompanion)
        {
            for (var index = 0; index < context.Parameters.Length; index++)
            {
                var source = clauseContext.Parameters[index];
                var target = context.Parameters[index];
                if (factory.GetVariableInfo(source.Entry).Type != factory.GetVariableInfo(target.Entry).Type)
                { return Fail(ContractBindingFailure.UnsupportedExpression); }
                replacements.Add(source.Entry, factory.Variable(target.Entry));
                replacements.Add(source.Current, factory.Variable(target.Current));
                replacements.Add(source.PreState, factory.Variable(target.PreState));
            }
        }
        var intrinsicFailure = ValidateIntrinsics(intrinsics, resolution.Source, resolution.Inventory.ImplementationBody, requiresOnly);
        if (intrinsicFailure != ContractBindingFailure.None)
        { return Fail(intrinsicFailure); }
        RoslynTotalExpressionLowerer lowerer = null!;
        lowerer = new(clauseContext)
        {
            Intrinsic = (invocation, state) => invocation is null ? throw new System.ArgumentNullException(nameof(invocation)) : (api.IsResult(invocation.TargetMethod)
                ? context.Result is { } result
                    ? new GuardedExpression(factory.Variable(result), factory.Boolean(true), FrontendSubsetClassification.Exact)
                    : null
                : api.IsOld(invocation.TargetMethod) && invocation.Arguments.Length == 1
                    ? lowerer.LowerClause(invocation.Arguments[0].Value, TotalParameterState.PreState)
                    : null)
        };
        var allowed = new HashSet<IrVarId>(context.Inputs.SelectMany(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (new[] { binding.Entry, binding.Current, binding.PreState })));
        if (context.Result is { } resultVariable)
        { allowed.Add(resultVariable); }
        var clauses = ImmutableArray.CreateBuilder<BoundTotalContractClause>();
        foreach (var occurrence in candidates.Where(occurrence =>
            !requiresOnly || occurrence.Kind == BoundContractKind.Requires))
        {
            var invocation = occurrence.Invocation;
            if (invocation.Arguments.Length != 1)
            { return Fail(ContractBindingFailure.InvalidIntrinsicSignature); }
            var state = occurrence.Kind == BoundContractKind.Requires ? TotalParameterState.Entry : TotalParameterState.Current;
            var expression = lowerer.LowerClause(invocation.Arguments[0].Value, state);
            if (resolution.UsesCompanion && expression.Classification.IsExact)
            {
                expression = new GuardedExpression(IrSubstitution.Substitute(factory, expression.Value, replacements),
                    IrSubstitution.Substitute(factory, expression.SafeCondition, replacements), expression.Classification);
            }
            if (!expression.Classification.IsExact ||
                IrTraversal.CollectVariables(expression.Value).Any(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (!allowed.Contains(variable))) ||
                IrTraversal.CollectVariables(expression.SafeCondition).Any(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (!allowed.Contains(variable))))
            {
                return Fail(ContractBindingFailure.UnsupportedExpression);
            }
            if (expression.Value.Type != factory.BooleanType)
            { return Fail(ContractBindingFailure.NonBooleanCondition); }
            clauses.Add(new(occurrence.Kind, expression, context.Site(invocation), ContractInvocationMaterialization.FormatDiagnosticSourceText(invocation.Arguments[0].Value.Syntax),
                resolution.UsesCompanion ? BoundContractEvidence.Companion : BoundContractEvidence.CompilerBoundInvocation,
                invocation.Syntax.GetReference()));
        }
        return new(clauses.ToImmutable(), ContractBindingFailure.None);

        static TotalContractInvocationMaterializationResult Fail(ContractBindingFailure failure)
        {
            return new([], failure);
        }
    }

    private static ContractBindingFailure ValidateIntrinsics(
        ContractIntrinsicValidator intrinsics, IMethodSymbol source,
        IOperation? body, bool requiresOnly)
    {
        intrinsics = RepositoryParameterGuard.NotNull(intrinsics, nameof(intrinsics));
        foreach (var violation in intrinsics.Validate(source, body))
        {
            if (!requiresOnly || violation.EnclosingClauseKind == BoundContractKind.Requires)
            {
                return violation.Failure;
            }
        }
        return ContractBindingFailure.None;
    }
}

internal sealed class TotalContractInvocationMaterializationResult(
    ImmutableArray<BoundTotalContractClause> clauses,
    ContractBindingFailure failure)
{
    internal ImmutableArray<BoundTotalContractClause> Clauses { get; } = (clauses.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(clauses)) : clauses);
    internal ContractBindingFailure Failure { get; } = failure;
}
