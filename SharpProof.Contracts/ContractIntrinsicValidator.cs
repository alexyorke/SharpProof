namespace SharpProof.Contracts;

internal sealed class ContractIntrinsicValidator
{
    private readonly ContractApiSymbols? _api;

    internal ContractIntrinsicValidator(ContractApiSymbols? api)
    {
        _api = api;
    }

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
        var operations = includeNestedCallables
            ? body.DescendantsAndSelf()
            : WalkCallableBoundary(body);
        foreach (var use in operations
                     .Select(static operation => (Operation: operation, Target: GetReferencedMethod(operation)))
                     .Where(static use => use.Target != null)
                     .OrderBy(static use => use.Operation.Syntax.SpanStart))
        {
            var operation = use.Operation;
            var target = use.Target;
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
            if (violationKind != null)
            {
                violations.Add(new(
                    operation,
                    context.Clause,
                    violationKind));
            }
        }
        return violations.ToImmutable();
    }

    internal ImmutableArray<ContractIntrinsicViolation> ValidateMemberInitializer(
        IOperation initializer)
    {
        if (_api == null ||
            initializer is not (IFieldInitializerOperation or IPropertyInitializerOperation))
        {
            return [];
        }

        var violations = ImmutableArray.CreateBuilder<ContractIntrinsicViolation>();
        foreach (var operation in WalkCallableBoundary(initializer)
                     .OrderBy(static value => value.Syntax.SpanStart))
        {
            if (operation is IAnonymousFunctionOperation anonymous)
            {
                violations.AddRange(Validate(
                    anonymous.Symbol,
                    anonymous.Body,
                    includeNestedCallables: true));
                continue;
            }

            var target = GetReferencedMethod(operation);
            var isResult = target != null && _api.IsResult(target);
            if (!isResult && (target == null || !_api.IsOld(target)))
            {
                continue;
            }

            violations.Add(new(
                operation,
                null,
                isResult
                    ? ContractIntrinsicViolationClassification.ResultOutsideEnsures
                    : ContractIntrinsicViolationClassification.OldOutsideEnsures));
        }
        return violations.ToImmutable();
    }

    private static IMethodSymbol? GetReferencedMethod(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation reference => reference.Method,
            _ => null
        };
    }

    private static IEnumerable<IOperation> WalkCallableBoundary(
        IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var operation = pending.Pop();
            yield return operation;
            if (operation != root &&
                (operation is ILocalFunctionOperation or IAnonymousFunctionOperation))
            {
                continue;
            }

            foreach (var child in operation.ChildOperations.Reverse())
            {
                pending.Push(child);
            }
        }
    }

    private static ContractIntrinsicViolationClassification? Classify(
        IInvocationOperation invocation, IMethodSymbol owner, IntrinsicContext context,
        bool isResult)
    {
        var contextViolation = ClassifyContext(context, isResult);
        if (contextViolation != null &&
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
                : ContractIntrinsicViolationClassification.InvalidResultSignature;
        }

        if (invocation.Arguments.Length != 1)
        {
            return ContractIntrinsicViolationClassification.InvalidOldSignature;
        }

        return contextViolation;
    }

    private static ContractIntrinsicViolationClassification ClassifyMethodReference(
        IntrinsicContext context,
        bool isResult)
    {
        return ClassifyContext(context, isResult) ??
            (isResult
                ? ContractIntrinsicViolationClassification.InvalidResultSignature
                : ContractIntrinsicViolationClassification.InvalidOldSignature);
    }

    private static ContractIntrinsicViolationClassification? ClassifyContext(
        IntrinsicContext context,
        bool isResult)
    {
        if (context.Clause != BoundContractKind.Ensures)
        {
            return isResult
                ? ContractIntrinsicViolationClassification.ResultOutsideEnsures
                : ContractIntrinsicViolationClassification.OldOutsideEnsures;
        }

        if (context.InsideOld)
        {
            return isResult
                ? ContractIntrinsicViolationClassification.ResultInsideOld
                : ContractIntrinsicViolationClassification.OldInsideOld;
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

internal sealed class ContractIntrinsicViolationClassification
{
    internal static readonly ContractIntrinsicViolationClassification ResultOutsideEnsures =
        new(ContractIntrinsicViolationKind.ResultOutsideEnsures, ContractBindingFailure.ResultOutsideEnsures);
    internal static readonly ContractIntrinsicViolationClassification OldOutsideEnsures =
        new(ContractIntrinsicViolationKind.OldOutsideEnsures, ContractBindingFailure.OldOutsideEnsures);
    internal static readonly ContractIntrinsicViolationClassification ResultInsideOld =
        new(ContractIntrinsicViolationKind.ResultInsideOld, ContractBindingFailure.NestedOld);
    internal static readonly ContractIntrinsicViolationClassification OldInsideOld =
        new(ContractIntrinsicViolationKind.OldInsideOld, ContractBindingFailure.NestedOld);
    internal static readonly ContractIntrinsicViolationClassification InvalidResultSignature =
        new(ContractIntrinsicViolationKind.InvalidResultSignature, ContractBindingFailure.InvalidIntrinsicSignature);
    internal static readonly ContractIntrinsicViolationClassification InvalidOldSignature =
        new(ContractIntrinsicViolationKind.InvalidOldSignature, ContractBindingFailure.InvalidIntrinsicSignature);

    private ContractIntrinsicViolationClassification(ContractIntrinsicViolationKind kind, ContractBindingFailure failure)
    {
        Kind = kind;
        Failure = failure;
    }

    internal ContractIntrinsicViolationKind Kind { get; }
    internal ContractBindingFailure Failure { get; }
}

internal readonly struct ContractIntrinsicViolation
{
    private readonly ContractIntrinsicViolationClassification _classification;

    internal ContractIntrinsicViolation(IOperation operation, BoundContractKind? enclosingClauseKind,
        ContractIntrinsicViolationClassification classification)
    {
        Operation = operation;
        EnclosingClauseKind = enclosingClauseKind;
        _classification = ArgumentNullGuard.NotNull(classification, nameof(classification));
    }

    internal IOperation Operation { get; }
    internal BoundContractKind? EnclosingClauseKind { get; }
    internal ContractIntrinsicViolationKind Kind => _classification.Kind;
    internal bool IsOld => Kind is
        ContractIntrinsicViolationKind.OldOutsideEnsures or
        ContractIntrinsicViolationKind.OldInsideOld or
        ContractIntrinsicViolationKind.InvalidOldSignature;
    internal ContractBindingFailure Failure => _classification.Failure;
}
