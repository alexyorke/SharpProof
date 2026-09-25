using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SharpProof.Effects;

internal interface IEffectCallPreconditionPolicy
{
    bool IsNotProven(
        IMethodSymbol method);

    bool IsNotProven(
        EffectCallPreconditionContext context);
}

internal sealed class ConservativeEffectCallPreconditionPolicy
    : IEffectCallPreconditionPolicy
{
    private static readonly ConditionalWeakTable<
        Compilation,
        ImmutableHashSet<INamedTypeSymbol>>
        CompanionTypes = new();
    private readonly Compilation _compilation;
    private readonly bool _includeSourceCompanions;
    private readonly CancellationToken _cancellationToken;
    private readonly INamedTypeSymbol? _contract;
    private readonly INamedTypeSymbol? _inRange;
    private readonly INamedTypeSymbol? _notNull;
    private readonly INamedTypeSymbol? _positive;
    private readonly Lazy<
        ImmutableHashSet<INamedTypeSymbol>>
        _typesWithCompanions;
    private readonly ConcurrentDictionary<IMethodSymbol, bool>
        _directOrClosedPreconditions =
            new(SymbolEqualityComparer.Default);
    private readonly ConcurrentDictionary<IMethodSymbol, bool>
        _potentialPreconditions =
            new(SymbolEqualityComparer.Default);

    internal ConservativeEffectCallPreconditionPolicy(
        Compilation compilation,
        bool includeSourceCompanions = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _compilation = ArgumentNullGuard.NotNull(
            compilation, nameof(compilation));
        _includeSourceCompanions = includeSourceCompanions;
        _cancellationToken = cancellationToken;
        var identity =
            ContractApiIdentityResolver.ForCompilation(compilation);
        _contract = identity.Contract;
        var contractFor = identity.ResolveAttribute(
            ContractApiMetadata.ContractFor);
        _notNull = identity.ResolveAttribute(
            ContractApiMetadata.NotNull);
        _positive = identity.ResolveAttribute(
            ContractApiMetadata.Positive);
        _inRange = identity.ResolveAttribute(
            ContractApiMetadata.InRange);
        _typesWithCompanions =
            new(() => CompanionTypes.GetValue(
                    compilation,
                    value => FindTypesWithCompanions(
                        value,
                        contractFor,
                        cancellationToken)),
                LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsNotProven(
        EffectCallPreconditionContext context)
    {
        return HasPotentialPreconditions(context.Target);
    }

    public bool IsNotProven(
        IMethodSymbol method)
    {
        return HasPotentialPreconditions(method);
    }

    internal bool HasPotentialPreconditions(
        IMethodSymbol method)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        method = EffectAnalysisSession.NormalizeMethodConstruction(method);
        return _potentialPreconditions.GetOrAdd(
            method,
            HasPotentialPreconditionsCore);
    }

    internal bool HasPotentialDirectOrClosedPreconditions(
        IMethodSymbol method)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        method = EffectAnalysisSession.NormalizeMethodConstruction(method);
        return _directOrClosedPreconditions.GetOrAdd(
            method,
            HasPotentialDirectOrClosedPreconditionsCore);
    }

    private bool HasPotentialPreconditionsCore(
        IMethodSymbol method)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_directOrClosedPreconditions.GetOrAdd(
                method,
                HasPotentialDirectOrClosedPreconditionsCore))
        {
            return true;
        }
        if (!_includeSourceCompanions &&
            !method.DeclaringSyntaxReferences.IsEmpty)
        {
            return false;
        }

        var containingType = method.ContainingType;
        var companionTypes = _typesWithCompanions.Value;
        return companionTypes.Contains(containingType) ||
            containingType.IsGenericType &&
            companionTypes.Contains(containingType.OriginalDefinition);
    }

    private bool HasPotentialDirectOrClosedPreconditionsCore(
        IMethodSymbol method)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        return method.Parameters.Any(parameter =>
                parameter.RefKind != RefKind.Out &&
                parameter.GetAttributes().Any(
                    IsClosedPrecondition)) ||
            HasDirectRequires(method);
    }

    private bool HasDirectRequires(
        IMethodSymbol method)
    {
        if (_contract == null)
        {
            return false;
        }

        method = EffectAnalysisSession.NormalizeMethod(method);

        foreach (var syntaxReference in
                 method.DeclaringSyntaxReferences)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var syntax = syntaxReference.GetSyntax(
                _cancellationToken);
            var model =
                SharpProof.Frontend.Host
                    .CompilationModelProvider
                    .GetSemanticModel(
                        _compilation,
                        syntax.SyntaxTree);
            foreach (var invocationSyntax in
                     syntax.DescendantNodesAndSelf()
                         .OfType<
                             Microsoft.CodeAnalysis.CSharp.Syntax
                                 .InvocationExpressionSyntax>())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var enclosing = model.GetEnclosingSymbol(
                    invocationSyntax.SpanStart,
                    _cancellationToken);
                if (enclosing is not IMethodSymbol owner ||
                    !SymbolEqualityComparer.Default.Equals(
                        EffectAnalysisSession.NormalizeMethod(owner),
                        EffectAnalysisSession.NormalizeMethod(method)))
                {
                    continue;
                }

                if (model.GetOperation(
                        invocationSyntax,
                        _cancellationToken) is
                    IInvocationOperation invocation &&
                    invocation.TargetMethod.OriginalDefinition is
                    {
                        Name: ContractApiCatalog.RequiresMethodName,
                        ContainingType: { } containingType
                    } &&
                    SymbolEqualityComparer.Default.Equals(
                        containingType,
                        _contract))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsClosedPrecondition(
        AttributeData attribute)
    {
        return ContractApiMetadata.IsAttribute(attribute, _notNull) ||
            ContractApiMetadata.IsAttribute(attribute, _positive) ||
            ContractApiMetadata.IsAttribute(attribute, _inRange);
    }

    private static ImmutableHashSet<INamedTypeSymbol>
        FindTypesWithCompanions(
            Compilation compilation,
            INamedTypeSymbol? contractFor,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = ImmutableHashSet.CreateBuilder<
            INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        if (contractFor == null)
        {
            return result.ToImmutable();
        }

        foreach (var type in SharpProof.Frontend.ReferencedTypeSymbols
                     .GetAllCached(compilation, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var attribute in type.GetAttributes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass
                            ?.OriginalDefinition,
                        contractFor.OriginalDefinition) ||
                    attribute.ConstructorArguments.Length !=
                        1 ||
                    attribute.ConstructorArguments[0]
                        .Value is not INamedTypeSymbol
                        target ||
                    target.TypeKind is not (
                        TypeKind.Class or
                        TypeKind.Interface))
                {
                    continue;
                }

                result.Add(target.IsUnboundGenericType
                    ? target.OriginalDefinition
                    : target);
            }
        }

        return result.ToImmutable();
    }

}
