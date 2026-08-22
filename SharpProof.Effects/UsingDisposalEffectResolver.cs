using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

/// <summary>
/// Resolves the conditional synchronous disposal that Roslyn lowers outside
/// the source operation tree. The lowered CFG invocation targets
/// <see cref="IDisposable.Dispose"/> and loses the concrete receiver type, so
/// effect discovery must bind the source resource before that lowering.
/// </summary>
internal sealed class UsingDisposalEffectResolver
{
    private readonly IMethodSymbol _caller;
    private readonly EffectCallSiteResolver _calls;
    private readonly Compilation _compilation;
    private readonly ManagedFlowResult? _flow;

    internal UsingDisposalEffectResolver(
        Compilation compilation,
        IMethodSymbol caller,
        EffectCallSiteResolver calls,
        ManagedFlowResult? flow)
    {
        _compilation = ArgumentNullGuard.NotNull(
            compilation,
            nameof(compilation));
        _caller = ArgumentNullGuard.NotNull(caller, nameof(caller));
        _calls = ArgumentNullGuard.NotNull(calls, nameof(calls));
        _flow = flow;
    }

    internal EffectSummary Scan(
        IOperation root,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion,
        Func<IOperation?, bool> canCompleteNormally,
        Func<IMethodSymbol, bool> canMethodCompleteNormally,
        Func<IMethodSymbol, bool> canMethodThrow,
        Func<IOperation, IOperation, bool> canExitAbruptly)
    {
        var summary = EffectSummary.Empty;
        foreach (var operation in root.DescendantsAndSelf()
                     .Where(static operation =>
                         operation is IUsingOperation or
                             IUsingDeclarationOperation))
        {
            if (IsInsideNestedCallable(operation, root) ||
                _flow != null && !_flow.IsReachable(operation))
            {
                continue;
            }

            var disposal = operation switch
            {
                IUsingOperation { IsAsynchronous: true } or
                    IUsingDeclarationOperation { IsAsynchronous: true } =>
                    EffectSummaryOperations.Unsupported(),
                IUsingOperation @using =>
                    ResolveResources(
                        @using.Resources,
                        @using,
                        classifyRegion,
                        canCompleteNormally,
                        canMethodCompleteNormally,
                        canMethodThrow,
                        canCompleteNormally(@using.Body) ||
                        canExitAbruptly(@using.Body, @using.Body)),
                IUsingDeclarationOperation declaration =>
                    ResolveResources(
                        declaration.DeclarationGroup,
                        declaration,
                        classifyRegion,
                        canCompleteNormally,
                        canMethodCompleteNormally,
                        canMethodThrow,
                        CanReachDeclarationDisposal(
                            declaration,
                            canCompleteNormally,
                            canMethodCompleteNormally,
                            canExitAbruptly)),
                _ => EffectSummary.Empty
            };
            summary = EffectSummaryDomain.Instance.Join(summary, disposal);
        }

        return summary;
    }

    private bool CanReachDeclarationDisposal(
        IUsingDeclarationOperation declaration,
        Func<IOperation?, bool> canCompleteNormally,
        Func<IMethodSymbol, bool> canMethodCompleteNormally,
        Func<IOperation, IOperation, bool> canExitAbruptly)
    {
        if (declaration.Parent is not IBlockOperation block)
        {
            return true;
        }
        var index = block.Operations.IndexOf(declaration);
        if (index < 0)
        {
            return true;
        }
        var pending = new Queue<int>();
        var visited = new HashSet<int>();
        pending.Enqueue(index + 1);
        while (pending.Count != 0)
        {
            var operationIndex = pending.Dequeue();
            if (operationIndex >= block.Operations.Length)
            {
                return true;
            }
            if (!visited.Add(operationIndex))
            {
                continue;
            }
            var operation = block.Operations[operationIndex];
            var internalBranches = GetInternalGotoTargets(
                operation,
                block,
                branch => _flow == null || _flow.IsReachable(branch),
                index + 1);
            if (internalBranches.LeavesActiveLifetime)
            {
                return true;
            }
            foreach (var target in internalBranches.Targets)
            {
                pending.Enqueue(target);
            }
            if (canExitAbruptly(operation, block))
            {
                return true;
            }
            if (operation is IUsingDeclarationOperation laterUsing &&
                !CanDisposalsCompleteNormally(
                    laterUsing,
                    canMethodCompleteNormally))
            {
                continue;
            }
            if (canCompleteNormally(operation) &&
                !internalBranches.HasUnconditionalGoto)
            {
                pending.Enqueue(operationIndex + 1);
            }
        }
        return false;
    }

    private static InternalGotoTargets GetInternalGotoTargets(
        IOperation operation,
        IBlockOperation scope,
        Func<IBranchOperation, bool> isReachable,
        int firstActiveOperation)
    {
        var branches = operation.DescendantsAndSelf()
            .OfType<IBranchOperation>()
            .Where(branch =>
                branch.Syntax is GotoStatementSyntax &&
                isReachable(branch))
            .ToArray();
        var allTargets = branches
            .SelectMany(static branch =>
                branch.Target.DeclaringSyntaxReferences)
            .Select(static reference => reference.GetSyntax())
            .Where(target =>
                target.SyntaxTree == scope.Syntax.SyntaxTree &&
                scope.Syntax.Span.Contains(target.Span))
            .Select(target => scope.Operations.IndexOf(
                scope.Operations.First(candidate =>
                    candidate.Syntax.Span.Contains(target.Span))))
            .Distinct()
            .ToArray();
        return new InternalGotoTargets(
            allTargets.Where(target =>
                target >= firstActiveOperation).ToArray(),
            branches.Any(branch =>
                IsUnconditionalAtOperationLevel(branch, operation)),
            allTargets.Any(target => target < firstActiveOperation));
    }

    private static bool IsUnconditionalAtOperationLevel(
        IBranchOperation branch,
        IOperation operation)
    {
        if (ReferenceEquals(branch, operation))
        {
            return true;
        }
        for (var parent = branch.Parent;
             parent != null;
             parent = parent.Parent)
        {
            if (ReferenceEquals(parent, operation))
            {
                return true;
            }
            if (parent is not ILabeledOperation)
            {
                return false;
            }
        }
        return false;
    }

    internal static bool IsSynthesizedSynchronousDispose(
        IInvocationOperation invocation)
    {
        return invocation.IsImplicit &&
            invocation.TargetMethod is
            {
                Name: "Dispose",
                IsStatic: false,
                Arity: 0,
                Parameters.IsEmpty: true,
                ReturnsVoid: true
            } &&
            invocation.Syntax.AncestorsAndSelf().Any(static syntax =>
                syntax is UsingStatementSyntax ||
                syntax is LocalDeclarationStatementSyntax
                {
                    UsingKeyword.RawKind: not 0
                });
    }

    private EffectSummary ResolveResources(
        IOperation resources,
        IOperation origin,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion,
        Func<IOperation?, bool> canCompleteNormally,
        Func<IMethodSymbol, bool> canMethodCompleteNormally,
        Func<IMethodSymbol, bool> canMethodThrow,
        bool scopeExitReachable)
    {
        if (resources is not IVariableDeclarationGroupOperation group)
        {
            if (!canCompleteNormally(resources))
            {
                return EffectSummary.Empty;
            }
            if (!scopeExitReachable)
            {
                return EffectSummary.Empty;
            }
            return ResolveResource(
                resources.Type,
                resources,
                origin,
                classifyRegion);
        }

        var acquired = new List<(
            ITypeSymbol Type,
            IOperation Resource,
            IOperation Origin)>();
        var acquisitionFailed = false;
        foreach (var declarator in group.Declarations
                     .SelectMany(static declaration => declaration.Declarators))
        {
            var resource = declarator.Initializer?.Value;
            if (!canCompleteNormally(resource))
            {
                acquisitionFailed = true;
                break;
            }
            if (resource != null)
            {
                acquired.Add((
                    declarator.Symbol.Type,
                    resource,
                    declarator));
            }
        }
        if (!scopeExitReachable && !acquisitionFailed)
        {
            return EffectSummary.Empty;
        }
        var summary = EffectSummary.Empty;
        foreach (var item in acquired.AsEnumerable().Reverse())
        {
            var disposal = ResolveResource(
                item.Type,
                item.Resource,
                item.Origin,
                classifyRegion);
            summary = EffectSummaryDomain.Instance.Join(summary, disposal);
            if (!CanDisposalUnwind(
                    item.Type,
                    item.Resource,
                    item.Origin,
                    canMethodCompleteNormally,
                    canMethodThrow))
            {
                break;
            }
        }
        return summary;
    }

    private bool CanDisposalsCompleteNormally(
        IUsingDeclarationOperation declaration,
        Func<IMethodSymbol, bool> canMethodCompleteNormally)
    {
        return declaration.DeclarationGroup.Declarations
            .SelectMany(static item => item.Declarators)
            .Reverse()
            .All(declarator => CanDisposalCompleteNormally(
                declarator.Symbol.Type,
                declarator.Initializer?.Value,
                declarator,
                canMethodCompleteNormally));
    }

    private bool CanDisposalCompleteNormally(
        ITypeSymbol? resourceType,
        IOperation? resource,
        IOperation origin,
        Func<IMethodSymbol, bool> canMethodCompleteNormally)
    {
        if (resourceType == null || resource == null ||
            IsDefinitelyNull(resource, origin))
        {
            return true;
        }
        var dispose = ResolveDispose(_compilation, _caller, resourceType);
        return dispose == null || IsDispatchUncertain(dispose) ||
            canMethodCompleteNormally(dispose);
    }

    private bool CanDisposalUnwind(
        ITypeSymbol? resourceType,
        IOperation resource,
        IOperation origin,
        Func<IMethodSymbol, bool> canMethodCompleteNormally,
        Func<IMethodSymbol, bool> canMethodThrow)
    {
        if (IsDefinitelyNull(resource, origin))
        {
            return true;
        }
        var dispose = resourceType == null
            ? null
            : ResolveDispose(_compilation, _caller, resourceType);
        return dispose == null || IsDispatchUncertain(dispose) ||
            canMethodCompleteNormally(dispose) ||
            canMethodThrow(dispose);
    }

    private bool IsDefinitelyNull(IOperation resource, IOperation origin)
    {
        return resource.ConstantValue is { HasValue: true, Value: null } ||
            _flow?.TryEvaluate(origin, resource, out var value) == true &&
            value.IsDefinitelyNull;
    }

    private sealed record InternalGotoTargets(
        IReadOnlyList<int> Targets,
        bool HasUnconditionalGoto,
        bool LeavesActiveLifetime);

    private EffectSummary ResolveResource(
        ITypeSymbol? resourceType,
        IOperation? resource,
        IOperation origin,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion)
    {
        if (resourceType == null || resource == null)
        {
            return EffectSummaryOperations.Unsupported();
        }

        if (resource.ConstantValue is { HasValue: true, Value: null } ||
            _flow?.TryEvaluate(origin, resource, out var value) == true &&
            value.IsDefinitelyNull)
        {
            return EffectSummary.Empty;
        }

        var dispose = ResolveDispose(
            _compilation,
            _caller,
            resourceType);
        if (dispose == null)
        {
            return EffectSummaryOperations.Unsupported();
        }

        return _calls.Resolve(
            dispose,
            resourceType.IsValueType && !resourceType.IsRefLikeType
                ? EffectRegionSet.Empty
                : classifyRegion(resource, true),
            ImmutableArray<EffectRegionSet>.Empty,
            ImmutableArray<IOperation?>.Empty,
            IsDispatchUncertain(dispose),
            origin,
            resource);
    }

    internal static IMethodSymbol? ResolveDispose(
        Compilation compilation,
        IMethodSymbol caller,
        ITypeSymbol resourceType)
    {
        if (resourceType is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType:
                    SpecialType.System_Nullable_T,
                TypeArguments: { Length: 1 } typeArguments
            })
        {
            resourceType = typeArguments[0];
        }

        if (resourceType is not INamedTypeSymbol named)
        {
            return null;
        }

        var disposable = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.IDisposable);
        var dispose = disposable?.GetMembers("Dispose")
            .OfType<IMethodSymbol>()
            .SingleOrDefault(static method =>
                !method.IsStatic &&
                method.Arity == 0 &&
                method.Parameters.IsEmpty &&
                method.ReturnsVoid);
        if (disposable != null && dispose != null &&
            (SymbolEqualityComparer.Default.Equals(
                 named.OriginalDefinition,
                 disposable) ||
             named.AllInterfaces.Any(@interface =>
                 SymbolEqualityComparer.Default.Equals(
                     @interface.OriginalDefinition,
                     disposable))))
        {
            return named.TypeKind == TypeKind.Interface
                ? dispose
                : named.FindImplementationForInterfaceMember(dispose) as
                    IMethodSymbol;
        }

        return named.IsRefLikeType
            ? named.GetMembers("Dispose")
                .OfType<IMethodSymbol>()
                .SingleOrDefault(method =>
                    method.MethodKind == MethodKind.Ordinary &&
                    !method.IsStatic &&
                    method.Arity == 0 &&
                    method.Parameters.IsEmpty &&
                    method.ReturnsVoid &&
                    compilation.IsSymbolAccessibleWithin(
                        method,
                        caller.ContainingType))
            : null;
    }

    internal static bool IsDispatchUncertain(IMethodSymbol method)
    {
        return !method.IsStatic &&
            (method.IsVirtual ||
             method.IsAbstract ||
             method.IsOverride ||
             method.ContainingType?.TypeKind == TypeKind.Interface) &&
            method.ContainingType?.IsSealed != true &&
            !method.IsSealed;
    }

    private static bool IsInsideNestedCallable(
        IOperation operation,
        IOperation root)
    {
        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, root);
             parent = parent.Parent)
        {
            if (parent is IAnonymousFunctionOperation or
                ILocalFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }
}
