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
    private readonly IMethodSymbol? _dispose;
    private readonly INamedTypeSymbol? _disposable;
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
        _disposable = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.IDisposable);
        _dispose = _disposable?.GetMembers("Dispose")
            .OfType<IMethodSymbol>()
            .SingleOrDefault(static method =>
                !method.IsStatic &&
                method.Arity == 0 &&
                method.Parameters.IsEmpty &&
                method.ReturnsVoid);
    }

    internal EffectSummary Scan(
        IOperation root,
        Func<IOperation?, bool, EffectRegionSet> classifyRegion)
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
                IUsingOperation @using => ResolveResources(
                    @using.Resources,
                    @using,
                    classifyRegion),
                IUsingDeclarationOperation declaration => ResolveResources(
                    declaration.DeclarationGroup,
                    declaration,
                    classifyRegion),
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
        Func<IOperation?, bool, EffectRegionSet> classifyRegion)
    {
        if (resources is not IVariableDeclarationGroupOperation group)
        {
            return ResolveResource(
                resources.Type,
                resources,
                origin,
                classifyRegion);
        }

        var summary = EffectSummary.Empty;
        foreach (var declarator in group.Declarations
                     .SelectMany(static declaration => declaration.Declarators))
        {
            summary = EffectSummaryDomain.Instance.Join(
                summary,
                ResolveResource(
                    declarator.Symbol.Type,
                    declarator.Initializer?.Value,
                    declarator,
                    classifyRegion));
        }

        return summary;
    }

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

        var dispose = ResolveDispose(resourceType);
        if (dispose == null)
        {
            return EffectSummaryOperations.Unsupported();
        }

        return _calls.Resolve(
            dispose,
            classifyRegion(resource, true),
            ImmutableArray<EffectRegionSet>.Empty,
            ImmutableArray<IOperation?>.Empty,
            IsDispatchUncertain(dispose),
            origin,
            resource);
    }

    private IMethodSymbol? ResolveDispose(ITypeSymbol resourceType)
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

        if (_disposable != null && _dispose != null &&
            (SymbolEqualityComparer.Default.Equals(
                 named.OriginalDefinition,
                 _disposable) ||
             named.AllInterfaces.Any(@interface =>
                 SymbolEqualityComparer.Default.Equals(
                     @interface.OriginalDefinition,
                     _disposable))))
        {
            return named.TypeKind == TypeKind.Interface
                ? _dispose
                : named.FindImplementationForInterfaceMember(_dispose) as
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
                    _compilation.IsSymbolAccessibleWithin(
                        method,
                        _caller.ContainingType))
            : null;
    }

    private static bool IsDispatchUncertain(IMethodSymbol method)
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
