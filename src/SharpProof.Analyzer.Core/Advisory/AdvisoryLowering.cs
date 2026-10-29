namespace SharpProof.Analyzer;

// A callable's Total program as the advisory passes see it, with the source
// trees its operation spans name.
internal sealed record AdvisoryLoweredBody(FrontendProgramLoweringResult Lowering, ImmutableArray<BoundTotalContractClause> Clauses,
    ImmutableDictionary<string, SyntaxTree> Trees)
{
    internal IrProgram Program => Lowering.Program;

    internal Location? Locate(OperationId site)
    {
        RepositoryParameterGuard.ThrowIfInvalid(site.IsDefault, nameof(site));
        var factory = Program.Factory;
        return factory.GetOperationInfo(site).SourceSpan is { } span && span.Document is { } document &&
            Trees.TryGetValue(document, out var tree) && span.Start + span.Length <= tree.Length
            ? Location.Create(tree, new TextSpan(span.Start, span.Length))
            : null;
    }
    public FrontendProgramLoweringResult Lowering { get; init; } = Lowering ?? throw new System.ArgumentNullException(nameof(Lowering));
    public ImmutableDictionary<string, SyntaxTree> Trees { get; init; } = Trees ?? throw new System.ArgumentNullException(nameof(Trees));
    public ImmutableArray<BoundTotalContractClause> Clauses { get; init; } = Clauses.IsDefault ? throw new System.ArgumentException("Array must be initialized.", nameof(Clauses)) : Clauses;
}

internal static class AdvisoryLowering
{
    // Each tree's document path is its ordinal in the compilation; every
    // callable of a compilation shares the two maps.
    private sealed class Documents
    {
        internal Documents(CSharpCompilation compilation)
        {
            compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
            var documents = compilation.SyntaxTrees.Select((tree, ordinal) => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : ((Tree: tree, Path: ordinal.ToString(CultureInfo.InvariantCulture))))
                .ToImmutableArray();
            Paths = ImmutableDictionaryCapture.Create(documents, item => item.Tree, item => item.Path);
            Trees = documents.ToImmutableDictionary(item => item.Path, item => item.Tree, StringComparer.Ordinal);
        }

        internal ImmutableDictionary<SyntaxTree, string> Paths { get; }
        internal ImmutableDictionary<string, SyntaxTree> Trees { get; }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CSharpCompilation, Documents> DocumentMaps = new();

    // Lowers the body the worker would verify, or names why it cannot.
    internal static (AdvisoryLoweredBody? Body, string? Gap) Lower(CSharpCompilation compilation, IMethodSymbol method,
        SyntaxNode declaration, CancellationToken cancellationToken)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        declaration = ArgumentNullGuard.NotNull(declaration, nameof(declaration));
        cancellationToken.ThrowIfCancellationRequested();
        var documents = DocumentMaps.GetValue(compilation, static value => value is null ? throw new System.ArgumentNullException(nameof(value)) : new Documents(value));
        if (!documents.Paths.ContainsKey(declaration.SyntaxTree))
        { return (null, "SourceOwnership"); }
        if (!TotalSourceCallSession.IsBodyDeclaration(declaration))
        { return (null, "UnsupportedCallable"); }
        var paths = documents.Paths;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method,
            tree => paths.TryGetValue(tree, out var path) ? path : throw new InvalidOperationException("The syntax tree is not part of the compilation."));
        FrontendProgramLoweringResult? lowering;
        ImmutableArray<BoundTotalContractClause> clauses = [];
        if (TotalBodyLowering.AutoAccessor(declaration) is { } autoAccessor)
        { lowering = new RoslynProgramLowerer(context.Factory).LowerAutoAccessor(context, autoAccessor); }
        else
        {
            var binding = new ContractBinder(compilation, context.Factory).BindTotal(context,
                cancellationToken: cancellationToken);
            if (!binding.IsSuccess)
            { return (null, "ContractBinding:" + binding.Failure); }
            clauses = binding.Clauses;
            var graph = TotalSourceCallSession.CreateGraph(declaration, method,
                Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree), cancellationToken);
            lowering = graph == null ? null : TotalBodyLowering.Lower(compilation, graph, context, cancellationToken, opaqueCalls: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (lowering == null)
        { return (null, "UnsupportedCallable"); }
        if (!lowering.IsExact)
        { return (null, "Lowering:" + lowering.Classification.Abstention); }
        return (new(lowering, clauses, documents.Trees), null);
    }
}
