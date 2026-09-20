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
    private readonly Func<IOperation?, bool, EffectRegionSet> _classifyRegion;
    private readonly Func<IOperation?, bool> _canCompleteNormally;
    private readonly Func<IMethodSymbol, bool> _canMethodCompleteNormally;
    private readonly Func<IMethodSymbol, bool> _canMethodThrow;
    private readonly Func<IOperation, IOperation, bool> _canExitAbruptly;
    private readonly Dictionary<IOperation, ResourceDisposalFacts>
        _declarationDisposalFacts = new();

    internal UsingDisposalEffectResolver(
        Compilation compilation,
        IMethodSymbol caller,
        EffectCallSiteResolver calls,
        ManagedFlowResult? flow,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion,
        Func<IOperation?, bool> canCompleteNormally,
        Func<IMethodSymbol, bool> canMethodCompleteNormally,
        Func<IMethodSymbol, bool> canMethodThrow,
        Func<IOperation, IOperation, bool> canExitAbruptly)
    {
        _compilation = ArgumentNullGuard.NotNull(
            compilation,
            nameof(compilation));
        _caller = ArgumentNullGuard.NotNull(caller, nameof(caller));
        _calls = ArgumentNullGuard.NotNull(calls, nameof(calls));
        _flow = flow;
        _classifyRegion = classifyRegion;
        _canCompleteNormally = canCompleteNormally;
        _canMethodCompleteNormally = canMethodCompleteNormally;
        _canMethodThrow = canMethodThrow;
        _canExitAbruptly = canExitAbruptly;
    }

    internal EffectSummary Scan(
        IOperation root,
        ImmutableArray<IOperation> operations = default)
    {
        var summary = EffectSummary.Empty;
        // Managed flow does not record handler paths (or code reached after a
        // normally completing handler). Let the scanner's semantic fallback
        // distinguish those paths from genuinely unreachable operations.
        OperationEffectScanner? semanticReachability = null;
        IEnumerable<IOperation> candidates = operations.IsDefault
            ? root.DescendantsAndSelf()
            : operations;
        foreach (var operation in candidates
                     .Where(static operation =>
                         operation is IUsingOperation or
                             IUsingDeclarationOperation))
        {
            if (ConversionOwnershipClassifier.IsInsideNestedCallable(operation, root))
            {
                continue;
            }
            if (_flow != null && !_flow.IsReachable(operation) &&
                !(semanticReachability ??= OperationEffectScanner
                    .CreateReachabilityProbe(
                        _compilation,
                        _caller,
                        root,
                        _flow))
                    .IsReachable(operation))
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
                        false,
                        _canCompleteNormally(@using.Body) ||
                        _canExitAbruptly(@using.Body, @using.Body)),
                IUsingDeclarationOperation declaration =>
                    ResolveResources(
                        declaration.DeclarationGroup,
                        declaration,
                        true,
                        UsingDisposalGraph.CanReachDeclarationDisposal(
                            declaration,
                            _canCompleteNormally,
                            _canExitAbruptly,
                            later => CanDisposalsCompleteNormally(later))),
                _ => EffectSummary.Empty
            };
            summary = EffectSummaryDomain.Instance.Join(summary, disposal);
        }

        return summary;
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
            IsUsingCleanupSyntax(invocation.Syntax);
    }

    private static bool IsUsingCleanupSyntax(SyntaxNode syntax)
    {
        foreach (var ancestor in syntax.AncestorsAndSelf())
        {
            switch (ancestor)
            {
                case ForEachStatementSyntax:
                case ForEachVariableStatementSyntax:
                    return false;
                case UsingStatementSyntax:
                    return true;
                case LocalDeclarationStatementSyntax
                {
                    UsingKeyword.RawKind: not 0
                }:
                    return true;
            }
        }

        return false;
    }

    private EffectSummary ResolveResources(
        IOperation resources,
        IOperation origin,
        bool cacheDeclarationFacts,
        bool scopeExitReachable)
    {
        if (resources is not IVariableDeclarationGroupOperation group)
        {
            if (!_canCompleteNormally(resources))
            {
                return EffectSummary.Empty;
            }
            if (!scopeExitReachable)
            {
                return EffectSummary.Empty;
            }
            return ResolveResource(
                    ResolveResourceFacts(resources.Type, resources, origin))
                .Summary;
        }

        var (acquired, reachableDisposalCount) = UsingDisposalGraph.AcquireResources(
            group,
            _canCompleteNormally,
            _canExitAbruptly,
            scopeExitReachable);
        if (reachableDisposalCount == 0)
        {
            return EffectSummary.Empty;
        }
        var summary = EffectSummary.Empty;
        foreach (var item in acquired.Take(reachableDisposalCount).Reverse())
        {
            var facts = cacheDeclarationFacts &&
                item.Origin is IVariableDeclaratorOperation declarator
                    ? ResolveDeclarationResourceFacts(declarator)
                    : ResolveResourceFacts(
                        item.Type,
                        item.Resource,
                        item.Origin);
            var disposal = ResolveResource(
                facts);
            summary = EffectSummaryDomain.Instance.Join(
                summary,
                disposal.Summary);
            if (!disposal.CanUnwind)
            {
                break;
            }
        }
        return summary;
    }

    private ResourceDisposalFacts ResolveDeclarationResourceFacts(
        IVariableDeclaratorOperation declarator)
    {
        if (_declarationDisposalFacts.TryGetValue(declarator, out var facts))
        {
            return facts;
        }

        facts = ResolveResourceFacts(
            declarator.Symbol.Type,
            declarator.Initializer?.Value,
            declarator);
        _declarationDisposalFacts.Add(declarator, facts);
        return facts;
    }

    private bool CanDisposalsCompleteNormally(IUsingDeclarationOperation declaration)
    {
        return UsingDisposalGraph.ReverseDeclarators(declaration.DeclarationGroup)
            .All(declarator => CanDisposalCompleteNormally(
                ResolveDeclarationResourceFacts(declarator)));
    }

    private bool CanDisposalCompleteNormally(
        ResourceDisposalFacts facts)
    {
        if (facts.ResourceType == null || facts.Resource == null ||
            facts.IsDefinitelyNull)
        {
            return true;
        }
        return facts.Dispose == null || facts.IsDispatchUncertain ||
            _canMethodCompleteNormally(facts.Dispose);
    }

    private bool IsDefinitelyNull(IOperation resource, IOperation origin)
    {
        return resource.ConstantValue is { HasValue: true, Value: null } ||
            _flow?.TryEvaluate(origin, resource, out var value) == true &&
            value.IsDefinitelyNull;
    }

    private (EffectSummary Summary, bool CanUnwind) ResolveResource(
        ResourceDisposalFacts facts)
    {
        if (facts.ResourceType == null || facts.Resource == null)
        {
            return (EffectSummaryOperations.Unsupported(), true);
        }

        if (facts.IsDefinitelyNull)
        {
            return (EffectSummary.Empty, true);
        }

        var dispose = facts.Dispose;
        if (dispose == null)
        {
            return (EffectSummaryOperations.Unsupported(), true);
        }

        var canComplete = !facts.IsDispatchUncertain &&
            _canMethodCompleteNormally(dispose);
        var canThrow = !facts.IsDispatchUncertain &&
            _canMethodThrow(dispose);
        var canUnwind = facts.IsDispatchUncertain || canComplete || canThrow;

        // A definitely diverging Dispose still runs its own effects before it
        // stops. Keep the call summary, while CanUnwind prevents an earlier
        // resource from being treated as reachable after this one.
        var receiver = dispose.ContainingType?.IsValueType == true &&
            !dispose.ContainingType.IsRefLikeType
                ? EffectRegionSet.Empty
                : _classifyRegion(facts.Resource, true);
        var summary = _calls.Resolve(
                dispose,
                receiver,
                ImmutableArray<EffectRegionSet>.Empty,
                ImmutableArray<IOperation?>.Empty,
                facts.IsDispatchUncertain,
                facts.Origin,
                facts.Resource,
                isDivergingDispose: !canUnwind);
        if (!canUnwind && summary.Completeness == EffectCompleteness.Incomplete)
        {
            // A recursive/unsupported disposal can produce an unknown call
            // boundary even though its own control flow is definitely
            // non-unwinding. Keep the fail-closed incompleteness marker and
            // divergence, but do not remap unknown receiver/parameter regions
            // into unrelated outer resources.
            summary = EffectSummaryOperations.IncompleteDivergence(summary);
        }

        return (
            summary,
            canUnwind);
    }

    private ResourceDisposalFacts ResolveResourceFacts(
        ITypeSymbol? resourceType,
        IOperation? resource,
        IOperation origin)
    {
        if (resourceType == null || resource == null)
        {
            return new(resourceType, resource, origin, null, false, false);
        }

        var isDefinitelyNull = IsDefinitelyNull(resource, origin);
        if (isDefinitelyNull)
        {
            return new(resourceType, resource, origin, null, true, false);
        }

        var dispose = ResolveDispose(
            _compilation,
            _caller,
            UsingDisposalGraph.GetConcreteResourceType(resourceType, resource));
        return new(
            resourceType,
            resource,
            origin,
            dispose,
            false,
            dispose != null && IsDispatchUncertain(dispose));
    }

    internal static IMethodSymbol? ResolveDispose(
        Compilation compilation,
        IMethodSymbol caller,
        ITypeSymbol resourceType)
    {
        resourceType = CompilerIdentityBridge.GetNullableUnderlyingType(
            resourceType) ?? resourceType;

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
        // A using statement invokes this method through IDisposable. Even when
        // the current class implementation is nonvirtual, a derived type can
        // list IDisposable again and install a different interface mapping.
        return PropertyDispatchFacts.HasOpenVirtualDispatch(
            method,
            allowClassReimplementation: true);
    }

    private readonly record struct ResourceDisposalFacts(
        ITypeSymbol? ResourceType,
        IOperation? Resource,
        IOperation Origin,
        IMethodSymbol? Dispose,
        bool IsDefinitelyNull,
        bool IsDispatchUncertain);

}
