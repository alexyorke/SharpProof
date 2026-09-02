namespace SharpProof.Effects;

internal readonly record struct EffectModuleInitializer(
    IMethodSymbol Method,
    bool CompletesNormally);

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
        // A framework-compatible ModuleInitializerAttribute is commonly
        // source-defined as a polyfill when targeting older frameworks.
        // The compiler-bound attribute is still the authority; its assembly
        // must not be restricted to a referenced framework assembly.
        _attribute = attribute?.OriginalDefinition;
    }

    internal ImmutableArray<EffectModuleInitializer> Discover(
        CancellationToken cancellationToken)
    {
        if (_attribute == null)
        {
            return [];
        }

        var syntaxTreeOrdinals = _compilation.SyntaxTrees
            .Select(static (tree, ordinal) => (tree, ordinal))
            .ToDictionary(
                static item => item.tree,
                static item => item.ordinal);
        var initializers = new Dictionary<IMethodSymbol, SyntaxReference>(
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
                    var normalized =
                        EffectAnalysisSession.NormalizeMethod(method);
                    var syntaxReference = method.DeclaringSyntaxReferences[0];
                    if (!initializers.TryGetValue(
                            normalized,
                            out var existingReference) ||
                        CompareSourceOrder(
                            syntaxReference,
                            existingReference,
                            syntaxTreeOrdinals) < 0)
                    {
                        initializers[normalized] = syntaxReference;
                    }
                }
            }
        }

        var completionFacts = new DefiniteOperationFacts(
            _compilation,
            cancellationToken);
        // Roslyn emits the calls in lexical symbol order. For source methods,
        // that key is the syntax-tree ordinal followed by declaration position.
        return [.. initializers
            .OrderBy(pair => GetSyntaxTreeOrdinal(
                pair.Value,
                syntaxTreeOrdinals))
            .ThenBy(static pair => pair.Value.Span.Start)
            .ThenBy(
                static pair => pair.Key.Name,
                StringComparer.Ordinal)
            .Select(pair => new EffectModuleInitializer(
                pair.Key,
                completionFacts.MethodCanCompleteNormally(pair.Key)))];
    }

    internal static EffectStep SummarizeBeforeEntry(
        IMethodSymbol method,
        ImmutableArray<EffectModuleInitializer> initializers,
        IReadOnlyDictionary<IMethodSymbol, EffectSummary> summaries)
    {
        var result = new EffectStep(EffectSummary.Bottom, true);
        foreach (var initializer in initializers)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    method,
                    initializer.Method))
            {
                break;
            }

            result = result.Then(new EffectStep(
                summaries.TryGetValue(initializer.Method, out var summary)
                    ? summary
                    : EffectSummaryOperations.UnknownBoundary(
                        EffectUncertainty.UnsupportedOperation),
                initializer.CompletesNormally));
            if (!result.CompletesNormally)
            {
                break;
            }
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

    private static int CompareSourceOrder(
        SyntaxReference left,
        SyntaxReference right,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrdinals)
    {
        var result = GetSyntaxTreeOrdinal(left, syntaxTreeOrdinals).CompareTo(
            GetSyntaxTreeOrdinal(right, syntaxTreeOrdinals));
        return result != 0
            ? result
            : left.Span.Start.CompareTo(right.Span.Start);
    }

    private static int GetSyntaxTreeOrdinal(
        SyntaxReference syntaxReference,
        IReadOnlyDictionary<SyntaxTree, int> syntaxTreeOrdinals)
    {
        return syntaxTreeOrdinals.TryGetValue(
            syntaxReference.SyntaxTree,
            out var ordinal)
            ? ordinal
            : int.MaxValue;
    }
}
