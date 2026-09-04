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
        Func<IOperation, IOperation, bool> canExitAbruptly,
        ImmutableArray<IOperation> operations = default)
    {
        var summary = EffectSummary.Empty;
        IEnumerable<IOperation> candidates = operations.IsDefault
            ? root.DescendantsAndSelf()
            : operations;
        foreach (var operation in candidates
                     .Where(static operation =>
                         operation is IUsingOperation or
                             IUsingDeclarationOperation))
        {
            if (ConversionOwnershipClassifier.IsInsideNestedCallable(operation, root) ||
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
                        canExitAbruptly,
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
                        canExitAbruptly,
                        UsingDisposalGraph.CanReachDeclarationDisposal(
                            declaration,
                            canCompleteNormally,
                            canExitAbruptly,
                            later => CanDisposalsCompleteNormally(
                                later,
                                canMethodCompleteNormally))),
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
        Func<IOperation, IOperation, bool> canExitAbruptly,
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
                ResolveResourceFacts(resources.Type, resources, origin),
                classifyRegion,
                canMethodCompleteNormally,
                canMethodThrow);
        }

        var (acquired, reachableDisposalCount) = UsingDisposalGraph.AcquireResources(
            group,
            canCompleteNormally,
            canExitAbruptly,
            scopeExitReachable);
        if (reachableDisposalCount == 0)
        {
            return EffectSummary.Empty;
        }
        var summary = EffectSummary.Empty;
        foreach (var item in acquired.Take(reachableDisposalCount).Reverse())
        {
            var facts = ResolveResourceFacts(
                item.Type,
                item.Resource,
                item.Origin);
            var disposal = ResolveResource(
                facts,
                classifyRegion,
                canMethodCompleteNormally,
                canMethodThrow);
            summary = EffectSummaryDomain.Instance.Join(summary, disposal);
            if (!CanDisposalUnwind(
                    facts,
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
        var dispose = ResolveDispose(
            _compilation,
            _caller,
            UsingDisposalGraph.GetConcreteResourceType(resourceType, resource));
        return dispose == null || IsDispatchUncertain(dispose) ||
            canMethodCompleteNormally(dispose);
    }

    private static bool CanDisposalUnwind(
        ResourceDisposalFacts facts,
        Func<IMethodSymbol, bool> canMethodCompleteNormally,
        Func<IMethodSymbol, bool> canMethodThrow)
    {
        if (facts.IsDefinitelyNull)
        {
            return true;
        }
        var dispose = facts.Dispose;
        var complete = dispose != null && canMethodCompleteNormally(dispose);
        var throws = dispose != null && canMethodThrow(dispose);
        return dispose == null || facts.IsDispatchUncertain || complete || throws;
    }

    private bool IsDefinitelyNull(IOperation resource, IOperation origin)
    {
        return resource.ConstantValue is { HasValue: true, Value: null } ||
            _flow?.TryEvaluate(origin, resource, out var value) == true &&
            value.IsDefinitelyNull;
    }

    private EffectSummary ResolveResource(
        ResourceDisposalFacts facts,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion,
        Func<IMethodSymbol, bool> canMethodCompleteNormally,
        Func<IMethodSymbol, bool> canMethodThrow)
    {
        if (facts.ResourceType == null || facts.Resource == null)
        {
            return EffectSummaryOperations.Unsupported();
        }

        if (facts.IsDefinitelyNull)
        {
            return EffectSummary.Empty;
        }

        var dispose = facts.Dispose;
        if (dispose == null)
        {
            return EffectSummaryOperations.Unsupported();
        }
        if (!facts.IsDispatchUncertain &&
            !canMethodCompleteNormally(dispose) &&
            !canMethodThrow(dispose))
        {
            return EffectSummary.Empty;
        }

        var receiver = dispose.ContainingType?.IsValueType == true &&
            !dispose.ContainingType.IsRefLikeType
                ? EffectRegionSet.Empty
                : classifyRegion(facts.Resource, true);
        return _calls.Resolve(
            dispose,
            receiver,
            ImmutableArray<EffectRegionSet>.Empty,
            ImmutableArray<IOperation?>.Empty,
            facts.IsDispatchUncertain,
            facts.Origin,
            facts.Resource);
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
        var canReimplementInterface =
            method.ContainingType?.TypeKind == TypeKind.Class;
        return !method.IsStatic &&
            (canReimplementInterface ||
             method.IsVirtual ||
             method.IsAbstract ||
             method.IsOverride ||
             method.ContainingType?.TypeKind == TypeKind.Interface) &&
            method.ContainingType?.IsSealed != true &&
            (canReimplementInterface || !method.IsSealed);
    }

    private readonly record struct ResourceDisposalFacts(
        ITypeSymbol? ResourceType,
        IOperation? Resource,
        IOperation Origin,
        IMethodSymbol? Dispose,
        bool IsDefinitelyNull,
        bool IsDispatchUncertain);

}
