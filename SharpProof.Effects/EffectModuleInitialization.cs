namespace SharpProof.Effects;

internal sealed class EffectModuleInitialization
{
    private readonly INamedTypeSymbol? _attribute;
    private readonly Compilation _compilation;

    internal EffectModuleInitialization(Compilation compilation)
    {
        _compilation = ArgumentNullGuard.NotNull(
            compilation,
            nameof(compilation));
        var attribute = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.ModuleInitializerAttribute);
        _attribute = attribute != null &&
            (SymbolEqualityComparer.Default.Equals(
                attribute.ContainingAssembly,
                compilation.Assembly)
                ? IsValidSourceDefinition(attribute)
                : true)
                ? attribute.OriginalDefinition
                : null;
    }

    internal ImmutableArray<IMethodSymbol> Discover(
        CancellationToken cancellationToken)
    {
        if (_attribute == null)
        {
            return [];
        }

        var initializers = new HashSet<IMethodSymbol>(
            SymbolEqualityComparer.Default);
        var pending = new Queue<INamespaceOrTypeSymbol>();
        pending.Enqueue(_compilation.Assembly.GlobalNamespace);
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var container = pending.Dequeue();
            if (container is INamespaceSymbol @namespace)
            {
                foreach (var member in @namespace.GetMembers())
                {
                    pending.Enqueue(member);
                }

                continue;
            }

            var type = (INamedTypeSymbol)container;
            foreach (var nestedType in type.GetTypeMembers())
            {
                pending.Enqueue(nestedType);
            }

            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.GetAttributes().Any(IsModuleInitializerAttribute))
                {
                    initializers.Add(
                        EffectAnalysisSession.NormalizeMethod(method));
                }
            }
        }

        return [.. initializers.OrderBy(
            static method => method,
            EffectSymbolComparer<IMethodSymbol>.Instance)];
    }

    internal static EffectSummary SummarizeBeforeEntry(
        IMethodSymbol method,
        ImmutableArray<IMethodSymbol> initializers,
        IReadOnlyDictionary<IMethodSymbol, EffectSummary> summaries)
    {
        var result = EffectSummary.Bottom;
        foreach (var initializer in initializers)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    method,
                    initializer))
            {
                continue;
            }

            result = EffectSummaryDomain.Instance.Join(
                result,
                summaries.TryGetValue(initializer, out var summary)
                    ? summary
                    : EffectSummaryOperations.UnknownBoundary(
                        EffectUncertainty.UnsupportedOperation));
        }

        return result;
    }

    internal static bool CanPreventBodyEntry(EffectSummary summary)
    {
        return !summary.IsBottom &&
            (summary.Completeness != EffectCompleteness.Complete ||
             summary.Termination != EffectTermination.Terminates ||
             !summary.Throws.IsEmpty);
    }

    private bool IsModuleInitializerAttribute(AttributeData attribute)
    {
        return SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass?.OriginalDefinition,
            _attribute);
    }

    private bool IsValidSourceDefinition(INamedTypeSymbol attribute)
    {
        var systemAttribute = _compilation.GetTypeByMetadataName(
            typeof(Attribute).FullName!);
        var attributeBase = attribute.BaseType;
        if (attributeBase == null ||
            !SymbolEqualityComparer.Default.Equals(
                attributeBase.OriginalDefinition,
                systemAttribute?.OriginalDefinition) ||
            !attribute.IsSealed ||
            !attribute.InstanceConstructors.Any(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.IsEmpty))
        {
            return false;
        }

        var attributeUsage = _compilation.GetTypeByMetadataName(
            typeof(AttributeUsageAttribute).FullName!);
        var usage = attribute.GetAttributes().FirstOrDefault(candidate =>
            SymbolEqualityComparer.Default.Equals(
                candidate.AttributeClass?.OriginalDefinition,
                attributeUsage?.OriginalDefinition));
        if (usage == null || usage.ConstructorArguments.Length != 1 ||
            usage.ConstructorArguments[0].Value is not int targets)
        {
            return false;
        }

        return ((AttributeTargets)targets & AttributeTargets.Method) != 0;
    }
}
