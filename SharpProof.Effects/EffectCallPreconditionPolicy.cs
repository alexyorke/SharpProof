using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SharpProof.Effects;

internal interface IEffectCallPreconditionPolicy
{
    EffectCallPreconditionStatus AssessEntry(
        IMethodSymbol method);

    EffectCallPreconditionStatus Assess(
        EffectCallPreconditionContext context);
}

internal sealed class ConservativeEffectCallPreconditionPolicy
    : IEffectCallPreconditionPolicy
{
    private static readonly ConditionalWeakTable<
        Compilation,
        Lazy<ImmutableHashSet<INamedTypeSymbol>>>
        CompanionTypes = new();
    private readonly Compilation _compilation;
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
        Compilation compilation)
    {
        _compilation = ArgumentNullGuard.NotNull(
            compilation, nameof(compilation));
        var identity =
            ContractApiIdentityResolver.ForCompilation(compilation);
        _contract = identity.Contract;
        _notNull = identity.ResolveAttribute(
            ContractApiMetadata.NotNull);
        _positive = identity.ResolveAttribute(
            ContractApiMetadata.Positive);
        _inRange = identity.ResolveAttribute(
            ContractApiMetadata.InRange);
        _typesWithCompanions =
            CompanionTypes.GetValue(
                compilation,
                static value => new(
                    () => FindTypesWithCompanions(
                        value,
                        ContractApiIdentityResolver
                            .ForCompilation(value)
                            .ResolveAttribute(
                                ContractApiMetadata
                                    .ContractFor)),
                    LazyThreadSafetyMode
                        .ExecutionAndPublication));
    }

    public EffectCallPreconditionStatus Assess(
        EffectCallPreconditionContext context)
    {
        return AssessEntry(context.Target);
    }

    public EffectCallPreconditionStatus AssessEntry(
        IMethodSymbol method)
    {
        return HasPotentialPreconditions(method)
            ? EffectCallPreconditionStatus.NotProven
            : EffectCallPreconditionStatus.None;
    }

    internal bool HasPotentialPreconditions(
        IMethodSymbol method)
    {
        method = EffectAnalysisSession.NormalizeMethod(method);
        return _potentialPreconditions.GetOrAdd(
            method,
            HasPotentialPreconditionsCore);
    }

    internal bool HasPotentialDirectOrClosedPreconditions(
        IMethodSymbol method)
    {
        method = EffectAnalysisSession.NormalizeMethod(method);
        return _directOrClosedPreconditions.GetOrAdd(
            method,
            HasPotentialDirectOrClosedPreconditionsCore);
    }

    private bool HasPotentialPreconditionsCore(
        IMethodSymbol method)
    {
        return HasPotentialDirectOrClosedPreconditions(method) ||
            _typesWithCompanions.Value.Contains(
                method.ContainingType.OriginalDefinition);
    }

    private bool HasPotentialDirectOrClosedPreconditionsCore(
        IMethodSymbol method)
    {
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

        foreach (var syntaxReference in
                 method.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();
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
                var enclosing = model.GetEnclosingSymbol(
                    invocationSyntax.SpanStart);
                if (enclosing is not IMethodSymbol owner ||
                    !SymbolEqualityComparer.Default.Equals(
                        EffectAnalysisSession.NormalizeMethod(owner),
                        method))
                {
                    continue;
                }

                if (model.GetOperation(invocationSyntax) is
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
        var type = attribute.AttributeClass?.OriginalDefinition;
        return type != null &&
            (SymbolEqualityComparer.Default.Equals(
                 type,
                 _notNull?.OriginalDefinition) ||
             SymbolEqualityComparer.Default.Equals(
                 type,
                 _positive?.OriginalDefinition) ||
             SymbolEqualityComparer.Default.Equals(
                 type,
                 _inRange?.OriginalDefinition));
    }

    private static ImmutableHashSet<INamedTypeSymbol>
        FindTypesWithCompanions(
            Compilation compilation,
            INamedTypeSymbol? contractFor)
    {
        var result = ImmutableHashSet.CreateBuilder<
            INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        if (contractFor == null)
        {
            return result.ToImmutable();
        }

        foreach (var type in GetAllTypes(
                     compilation.Assembly.GlobalNamespace))
        {
            foreach (var attribute in type.GetAttributes())
            {
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

                result.Add(target.OriginalDefinition);
            }
        }

        return result.ToImmutable();
    }

    private static IEnumerable<INamedTypeSymbol>
        GetAllTypes(
            INamespaceOrTypeSymbol container)
    {
        foreach (var type in
                 container.GetTypeMembers())
        {
            yield return type;
            foreach (var nested in
                     GetAllTypes(type))
            {
                yield return nested;
            }
        }

        if (container is not INamespaceSymbol
            @namespace)
        {
            yield break;
        }

        foreach (var child in
                 @namespace.GetNamespaceMembers())
        {
            foreach (var type in
                     GetAllTypes(child))
            {
                yield return type;
            }
        }
    }
}
