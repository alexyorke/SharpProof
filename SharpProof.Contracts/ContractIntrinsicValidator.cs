namespace SharpProof.Contracts;

internal sealed class ContractIntrinsicValidator
{
    private readonly ContractApiSymbols? _api;
    internal ContractIntrinsicValidator(Compilation compilation)
    {
        _api = ContractApiSymbols.TryCreate(
            ArgumentNullGuard.NotNull(compilation, nameof(compilation)));
    }

    internal ImmutableArray<ContractIntrinsicViolation> Validate(
        IMethodSymbol callable, IOperation? body, bool includeNestedCallables = false)
    {
        callable = ArgumentNullGuard.NotNull(callable, nameof(callable));

        if (_api == null || body == null)
        {
            return [];
        }

        var violations = ImmutableArray.CreateBuilder<ContractIntrinsicViolation>();
        foreach (var operation in body.DescendantsAndSelf()
                     .Where(static value => value is IInvocationOperation or
                         IMethodReferenceOperation)
                     .OrderBy(static value => value.Syntax.SpanStart))
        {
            var target = operation switch
            {
                IInvocationOperation directInvocation =>
                    directInvocation.TargetMethod,
                IMethodReferenceOperation methodReference =>
                    methodReference.Method,
                _ => null
            };
            var isResult = target != null && _api.IsResult(target);
            if (!isResult && (target == null || !_api.IsOld(target)))
            {
                continue;
            }

            var owner = GetOwner(operation);
            if (owner == null || !includeNestedCallables &&
                !SameCallable(owner, callable))
            {
                continue;
            }

            var context = GetContext(operation, owner);
            var violationKind = operation is IInvocationOperation invocation
                ? Classify(invocation, owner, context, isResult)
                : ClassifyMethodReference(context, isResult);
            if (violationKind.HasValue)
            {
                violations.Add(new(
                    operation,
                    context.Clause,
                    violationKind.Value));
            }
        }
        return violations.ToImmutable();
    }

    private static ContractIntrinsicViolationKind? Classify(
        IInvocationOperation invocation, IMethodSymbol owner, IntrinsicContext context,
        bool isResult)
    {
        var contextViolation = ClassifyContext(context, isResult);
        if (contextViolation.HasValue &&
            (isResult || context.Clause != BoundContractKind.Ensures))
        {
            return contextViolation;
        }

        if (isResult)
        {
            return invocation.Arguments.Length == 0 && !owner.ReturnsVoid &&
                   owner.MethodKind != MethodKind.Constructor &&
                   invocation.Type != null &&
                   SymbolEqualityComparer.IncludeNullability.Equals(
                       invocation.Type, owner.ReturnType)
                ? null
                : ContractIntrinsicViolationKind.InvalidResultSignature;
        }

        if (invocation.Arguments.Length != 1)
        {
            return ContractIntrinsicViolationKind.InvalidOldSignature;
        }

        return contextViolation;
    }

    private static ContractIntrinsicViolationKind ClassifyMethodReference(
        IntrinsicContext context,
        bool isResult)
    {
        return ClassifyContext(context, isResult) ??
            (isResult
                ? ContractIntrinsicViolationKind.InvalidResultSignature
                : ContractIntrinsicViolationKind.InvalidOldSignature);
    }

    private static ContractIntrinsicViolationKind? ClassifyContext(
        IntrinsicContext context,
        bool isResult)
    {
        if (context.Clause != BoundContractKind.Ensures)
        {
            return isResult
                ? ContractIntrinsicViolationKind.ResultOutsideEnsures
                : ContractIntrinsicViolationKind.OldOutsideEnsures;
        }

        if (context.InsideOld)
        {
            return isResult
                ? ContractIntrinsicViolationKind.ResultInsideOld
                : ContractIntrinsicViolationKind.OldInsideOld;
        }
        return null;
    }

    private IntrinsicContext GetContext(IOperation operation, IMethodSymbol owner)
    {
        var insideOld = false;
        for (var parent = operation.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is IInvocationOperation invocation &&
                SameCallable(GetOwner(invocation), owner))
            {
                if (_api!.IsOld(invocation.TargetMethod))
                {
                    insideOld = true;
                }

                if (_api.Clauses.GetClauseKind(invocation.TargetMethod) is { } kind)
                {
                    return new(kind, insideOld);
                }
            }
        }

        return new(null, insideOld);
    }

    private static IMethodSymbol? GetOwner(IOperation operation)
    {
        return operation.SemanticModel?.GetEnclosingSymbol(operation.Syntax.SpanStart) as
            IMethodSymbol;
    }

    private static bool SameCallable(IMethodSymbol? left, IMethodSymbol right)
    {
        return left != null &&
            ContractClauseInventoryBuilder.HaveSameDefinition(left, right);
    }

    private readonly struct IntrinsicContext(BoundContractKind? clause, bool insideOld)
    {
        internal BoundContractKind? Clause { get; } = clause;
        internal bool InsideOld { get; } = insideOld;
    }
}

internal enum ContractIntrinsicViolationKind
{
    ResultOutsideEnsures,
    OldOutsideEnsures,
    ResultInsideOld,
    OldInsideOld,
    InvalidResultSignature,
    InvalidOldSignature
}

internal readonly struct ContractIntrinsicViolation(
    IOperation operation,
    BoundContractKind? enclosingClauseKind,
    ContractIntrinsicViolationKind kind)
{
    internal IOperation Operation { get; } = operation;
    internal BoundContractKind? EnclosingClauseKind { get; } = enclosingClauseKind;
    internal ContractIntrinsicViolationKind Kind { get; } = kind;
    internal bool IsOld => Kind is
        ContractIntrinsicViolationKind.OldOutsideEnsures or
        ContractIntrinsicViolationKind.OldInsideOld or
        ContractIntrinsicViolationKind.InvalidOldSignature;
    internal ContractBindingFailure Failure => Kind switch
    {
        ContractIntrinsicViolationKind.ResultOutsideEnsures =>
            ContractBindingFailure.ResultOutsideEnsures,
        ContractIntrinsicViolationKind.OldOutsideEnsures =>
            ContractBindingFailure.OldOutsideEnsures,
        ContractIntrinsicViolationKind.ResultInsideOld or
        ContractIntrinsicViolationKind.OldInsideOld =>
            ContractBindingFailure.NestedOld,
        ContractIntrinsicViolationKind.InvalidResultSignature or
        ContractIntrinsicViolationKind.InvalidOldSignature =>
            ContractBindingFailure.InvalidIntrinsicSignature,
        _ => throw new InvalidOperationException(
            "Unknown contract intrinsic violation: " + Kind)
    };
}
