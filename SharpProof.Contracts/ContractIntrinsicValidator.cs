namespace SharpProof.Contracts;

internal sealed class ContractIntrinsicValidator
{
    private readonly Compilation _compilation;
    private readonly ContractApiSymbols? _api;
    internal ContractIntrinsicValidator(Compilation compilation)
    {
        _compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        _api = ContractApiSymbols.TryCreate(_compilation);
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
        foreach (var root in GetRoots(callable, body))
        {
            foreach (var invocation in root.DescendantsAndSelf()
                         .OfType<IInvocationOperation>()
                         .OrderBy(static value => value.Syntax.SpanStart))
            {
                var isResult = _api.IsResult(invocation.TargetMethod);
                if (!isResult && !_api.IsOld(invocation.TargetMethod))
                {
                    continue;
                }

                var owner = GetOwner(invocation);
                if (owner == null || !includeNestedCallables &&
                    !SameCallable(owner, callable))
                {
                    continue;
                }

                var context = GetContext(invocation, owner);
                var failure = Classify(invocation, owner, context, isResult);
                if (failure != ContractBindingFailure.None)
                {
                    violations.Add(new(invocation, context.Clause, failure));
                }
            }
        }
        return violations.ToImmutable();
    }

    private IEnumerable<IOperation> GetRoots(IMethodSymbol callable, IOperation body)
    {
        yield return body;

        if (callable.MethodKind != MethodKind.Constructor)
        {
            yield break;
        }

        foreach (var reference in callable.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not ConstructorDeclarationSyntax
                {
                    Initializer: { } initializer
                })
            {
                continue;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, initializer.SyntaxTree);
            var operation = model.GetOperation(initializer.ArgumentList) ??
                model.GetOperation(initializer);
            if (operation != null &&
                (operation.Syntax.SyntaxTree != body.Syntax.SyntaxTree ||
                 operation.Syntax.Span != body.Syntax.Span))
            {
                yield return operation;
            }
        }
    }

    private static ContractBindingFailure Classify(
        IInvocationOperation invocation, IMethodSymbol owner, IntrinsicContext context,
        bool isResult)
    {
        if (context.Clause != BoundContractKind.Ensures)
        {
            return isResult
                ? ContractBindingFailure.ResultOutsideEnsures
                : ContractBindingFailure.OldOutsideEnsures;
        }

        if (isResult)
        {
            return !context.InsideOld &&
                   invocation.Arguments.Length == 0 && !owner.ReturnsVoid &&
                   owner.MethodKind != MethodKind.Constructor &&
                   invocation.TargetMethod.ReturnType != null &&
                   SymbolEqualityComparer.IncludeNullability.Equals(
                       invocation.TargetMethod.ReturnType, owner.ReturnType)
                ? ContractBindingFailure.None
                : ContractBindingFailure.InvalidIntrinsicSignature;
        }

        if (invocation.Arguments.Length != 1)
        {
            return ContractBindingFailure.InvalidIntrinsicSignature;
        }

        return context.InsideOld
            ? ContractBindingFailure.NestedOld
            : ContractBindingFailure.None;
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

                if (_api.GetClauseKind(invocation.TargetMethod) is { } kind)
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

internal readonly struct ContractIntrinsicViolation(IInvocationOperation invocation,
    BoundContractKind? enclosingClauseKind, ContractBindingFailure failure)
{
    internal IInvocationOperation Invocation { get; } = invocation;
    internal BoundContractKind? EnclosingClauseKind { get; } = enclosingClauseKind;
    internal ContractBindingFailure Failure { get; } = failure;
}
