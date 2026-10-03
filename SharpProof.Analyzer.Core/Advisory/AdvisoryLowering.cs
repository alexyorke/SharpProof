namespace SharpProof.Analyzer;

// A callable's Total program as the advisory passes see it, with the source
// trees its operation spans name.
internal sealed record AdvisoryLoweredBody(FrontendProgramLoweringResult Lowering, ImmutableArray<BoundTotalContractClause> Clauses,
    ImmutableDictionary<string, SyntaxTree> Trees)
{
    internal IrProgram Program => Lowering.Program;

    internal Location? Locate(OperationId site)
    {
        var factory = Program.Factory;
        return factory.GetOperationInfo(site).SourceSpan is { } span && span.Document is { } document &&
            Trees.TryGetValue(document, out var tree) && span.Start + span.Length <= tree.Length
            ? Location.Create(tree, new TextSpan(span.Start, span.Length))
            : null;
    }
}

internal static class AdvisoryLowering
{
    // Lowers the body the worker would verify, or names why it cannot.
    internal static (AdvisoryLoweredBody? Body, string? Gap) Lower(CSharpCompilation compilation, IMethodSymbol method,
        SyntaxNode declaration, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!compilation.SyntaxTrees.Contains(declaration.SyntaxTree))
        { return (null, "SourceOwnership"); }
        if (!TotalBodyLowering.IsBodyDeclaration(declaration))
        { return (null, "UnsupportedCallable"); }
        var documents = compilation.SyntaxTrees.Select((tree, ordinal) => (Tree: tree, Path: ordinal.ToString(CultureInfo.InvariantCulture)))
            .ToArray();
        var paths = documents.ToDictionary(item => item.Tree, item => item.Path);
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), method, tree => paths[tree]);
        FrontendProgramLoweringResult? lowering;
        ImmutableArray<BoundTotalContractClause> clauses = [];
        if (TotalBodyLowering.AutoAccessor(declaration) is { } autoAccessor)
        { lowering = new RoslynProgramLowerer(context.Factory).LowerAutoAccessor(context, autoAccessor); }
        else
        {
            var binding = new ContractBinder(compilation, context.Factory).BindTotal(context);
            if (!binding.IsSuccess)
            { return (null, "ContractBinding:" + binding.Failure); }
            clauses = binding.Clauses;
            var graph = TotalBodyLowering.CreateGraph(declaration, method,
                Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, declaration.SyntaxTree), cancellationToken);
            lowering = graph == null ? null : TotalBodyLowering.Lower(compilation, graph, context, cancellationToken, opaqueCalls: true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (lowering == null)
        { return (null, "UnsupportedCallable"); }
        if (!lowering.IsExact)
        { return (null, "Lowering:" + lowering.Classification.Abstention); }
        if (lowering.Program.Blocks.Sum(block => block.Instructions.Length) > TotalBodyLowering.MaximumWork)
        { return (null, "GraphBudget"); }
        return (new(lowering, clauses, documents.ToImmutableDictionary(item => item.Path, item => item.Tree, StringComparer.Ordinal)), null);
    }
}
