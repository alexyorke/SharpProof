namespace SharpProof.Compiler.Semantics;

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
            if (IntrinsicOf(use.Target) is not { } intrinsic)
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
                ? Classify(invocation, owner, context, intrinsic)
                : ClassifyMethodReference(context, intrinsic);
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

            if (IntrinsicOf(GetReferencedMethod(operation)) is not { } intrinsic)
            {
                continue;
            }

            violations.Add(new(
                operation,
                null,
                ContractIntrinsicViolationClassification.OutsideEnsures(intrinsic)));
        }
        return violations.ToImmutable();
    }

    private ContractIntrinsic? IntrinsicOf(IMethodSymbol? target)
    {
        return target == null ? null
            : _api!.IsResult(target) ? ContractIntrinsic.Result
            : _api.IsOld(target) ? ContractIntrinsic.Old
            : _api.IsValueAtReturn(target) ? ContractIntrinsic.ValueAtReturn
            : null;
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
        var pending = ImmutableStack<IOperation>.Empty;
        pending = pending.Push(root);
        while (!pending.IsEmpty)
        {
            var operation = pending.Peek();
            pending = pending.Pop();
            yield return operation;
            if (operation != root &&
                (operation is ILocalFunctionOperation or IAnonymousFunctionOperation))
            {
                continue;
            }

            foreach (var child in operation.ChildOperations.Reverse())
            {
                pending = pending.Push(child);
            }
        }
    }

    private static ContractIntrinsicViolationClassification? Classify(
        IInvocationOperation invocation, IMethodSymbol owner, IntrinsicContext context,
        ContractIntrinsic intrinsic)
    {
        var contextViolation = ClassifyContext(context, intrinsic);
        if (contextViolation != null &&
            (intrinsic != ContractIntrinsic.Old || context.Clause != BoundContractKind.Ensures))
        {
            return contextViolation;
        }

        if (intrinsic == ContractIntrinsic.ValueAtReturn)
        {
            // The argument must name an out parameter of the callable whose
            // postcondition this is; its value at return is the parameter's
            // final value.
            return invocation.Arguments.Length == 1 &&
                   invocation.Arguments[0].Value is IParameterReferenceOperation
                   {
                       Parameter: { RefKind: RefKind.Out, ContainingSymbol: IMethodSymbol declaring } parameter
                   } &&
                   SameCallable(declaring, owner) &&
                   invocation.Type != null &&
                   SymbolEqualityComparer.Default.Equals(invocation.Type, parameter.Type)
                ? null
                : ContractIntrinsicViolationClassification.InvalidValueAtReturnSignature;
        }

        if (intrinsic == ContractIntrinsic.Result)
        {
            return invocation.Arguments.Length == 0 && !owner.ReturnsVoid &&
                   owner.MethodKind != MethodKind.Constructor &&
                   invocation.Type != null &&
                   SymbolEqualityComparer.IncludeNullability.Equals(
                       invocation.Type, owner.ReturnType)
                ? null
                : ContractIntrinsicViolationClassification.InvalidResultSignature;
        }

        return ContractInvocationShape.ApplySingleArgument(
            invocation, contextViolation,
            static (_, violation) => violation,
            (ContractIntrinsicViolationClassification?)
                ContractIntrinsicViolationClassification.InvalidOldSignature);
    }

    private static ContractIntrinsicViolationClassification ClassifyMethodReference(
        IntrinsicContext context,
        ContractIntrinsic intrinsic)
    {
        return ClassifyContext(context, intrinsic) ?? ContractIntrinsicViolationClassification.InvalidSignature(intrinsic);
    }

    private static ContractIntrinsicViolationClassification? ClassifyContext(
        IntrinsicContext context,
        ContractIntrinsic intrinsic)
    {
        if (context.Clause != BoundContractKind.Ensures)
        {
            return ContractIntrinsicViolationClassification.OutsideEnsures(intrinsic);
        }

        if (context.InsideOld)
        {
            return intrinsic switch
            {
                ContractIntrinsic.Result => ContractIntrinsicViolationClassification.ResultInsideOld,
                ContractIntrinsic.Old => ContractIntrinsicViolationClassification.OldInsideOld,
                _ => ContractIntrinsicViolationClassification.ValueAtReturnInsideOld
            };
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
    InvalidOldSignature,
    ValueAtReturnOutsideEnsures,
    ValueAtReturnInsideOld,
    InvalidValueAtReturnSignature
}

internal enum ContractIntrinsic
{
    Result,
    Old,
    ValueAtReturn
}

internal sealed class ContractIntrinsicViolationClassification
{
    internal static readonly ContractIntrinsicViolationClassification ValueAtReturnOutsideEnsures =
        new(ContractIntrinsicViolationKind.ValueAtReturnOutsideEnsures, ContractBindingFailure.OldOutsideEnsures);
    internal static readonly ContractIntrinsicViolationClassification ValueAtReturnInsideOld =
        new(ContractIntrinsicViolationKind.ValueAtReturnInsideOld, ContractBindingFailure.NestedOld);
    internal static readonly ContractIntrinsicViolationClassification InvalidValueAtReturnSignature =
        new(ContractIntrinsicViolationKind.InvalidValueAtReturnSignature, ContractBindingFailure.InvalidIntrinsicSignature);

    internal static ContractIntrinsicViolationClassification OutsideEnsures(ContractIntrinsic intrinsic)
    {
        return intrinsic switch
        {
            ContractIntrinsic.Result => ResultOutsideEnsures,
            ContractIntrinsic.Old => OldOutsideEnsures,
            _ => ValueAtReturnOutsideEnsures
        };
    }

    internal static ContractIntrinsicViolationClassification InvalidSignature(ContractIntrinsic intrinsic)
    {
        return intrinsic switch
        {
            ContractIntrinsic.Result => InvalidResultSignature,
            ContractIntrinsic.Old => InvalidOldSignature,
            _ => InvalidValueAtReturnSignature
        };
    }

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
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        Operation = operation;
        EnclosingClauseKind = enclosingClauseKind;
        _classification = ArgumentNullGuard.NotNull(classification, nameof(classification));
    }

    internal IOperation Operation { get; }
    internal BoundContractKind? EnclosingClauseKind { get; }
    internal ContractIntrinsicViolationKind Kind => _classification.Kind;
    internal ContractIntrinsic Intrinsic => Kind switch
    {
        ContractIntrinsicViolationKind.OldOutsideEnsures or
        ContractIntrinsicViolationKind.OldInsideOld or
        ContractIntrinsicViolationKind.InvalidOldSignature => ContractIntrinsic.Old,
        ContractIntrinsicViolationKind.ValueAtReturnOutsideEnsures or
        ContractIntrinsicViolationKind.ValueAtReturnInsideOld or
        ContractIntrinsicViolationKind.InvalidValueAtReturnSignature => ContractIntrinsic.ValueAtReturn,
        _ => ContractIntrinsic.Result
    };
    internal ContractBindingFailure Failure => _classification.Failure;
}
