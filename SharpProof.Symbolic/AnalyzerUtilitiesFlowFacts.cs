namespace SharpProof.Symbolic;

internal sealed class AnalyzerUtilitiesFlowFacts {
    private readonly PointsToAnalysisResult _pointsTo;
    private readonly ImmutableDictionary<(OperationKind Kind, TextSpan Span), BasicBlock> _operationBlocks;

    private AnalyzerUtilitiesFlowFacts(PointsToAnalysisResult pointsTo, ControlFlowGraph graph) {
        _pointsTo = pointsTo;
        var blocks = ImmutableDictionary.CreateBuilder<(OperationKind Kind, TextSpan Span), BasicBlock>();
        foreach (var block in graph.Blocks)
            foreach (var root in block.Operations.Append(block.BranchValue).Where(static operation => operation != null))
                AddOperations(root!, block, blocks);
        _operationBlocks = blocks.ToImmutable();
    }

    internal static AnalyzerUtilitiesFlowFacts? TryCreate(
        ControlFlowGraph graph,
        ISymbol owningSymbol,
        Compilation compilation) {
        try {
            var options = new AnalyzerOptions([]);
            var interprocedural = InterproceduralAnalysisConfiguration.Create(
                options,
                [],
                graph,
                compilation,
                InterproceduralAnalysisKind.None,
                0,
                0);
            var result = PointsToAnalysis.TryGetOrComputeResult(
                graph,
                owningSymbol,
                options,
                Analyzer.Utilities.WellKnownTypeProvider.GetOrCreate(compilation),
                PointsToAnalysisKind.Complete,
                interprocedural,
                interproceduralAnalysisPredicate: null,
                pessimisticAnalysis: true,
                performCopyAnalysis: true,
                exceptionPathsAnalysis: true);
            return result == null ? null : new(result, graph);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException) {
            return null;
        }
    }

    internal EffectFlowValue RefineNullState(IOperation operation, ISymbol? symbol, EffectFlowValue value) {
        PointsToAbstractValue pointsTo;
        try { pointsTo = _pointsTo[operation]; }
        catch (KeyNotFoundException) { return value; }
        var nullState = pointsTo.NullState;
        if (nullState is not (NullAbstractValue.Null or NullAbstractValue.NotNull) && symbol != null &&
            _operationBlocks.TryGetValue((operation.Kind, operation.Syntax.Span), out var block)) {
            var symbolValues = _pointsTo[block].Data.Where(pair =>
                SymbolEqualityComparer.Default.Equals(pair.Key.Symbol, symbol)).Select(static pair => pair.Value.NullState).ToArray();
            if (symbolValues.Length != 0 && symbolValues.All(static state => state == NullAbstractValue.NotNull))
                nullState = NullAbstractValue.NotNull;
            else if (symbolValues.Length != 0 && symbolValues.All(static state => state == NullAbstractValue.Null))
                nullState = NullAbstractValue.Null;
        }
        return nullState switch {
            NullAbstractValue.Null => value.AsDefinitelyNull(),
            NullAbstractValue.NotNull => value.AsDefinitelyNonNull(),
            _ => value
        };
    }

    private static void AddOperations(
        IOperation operation,
        BasicBlock block,
        ImmutableDictionary<(OperationKind Kind, TextSpan Span), BasicBlock>.Builder blocks) {
        blocks[(operation.Kind, operation.Syntax.Span)] = block;
        foreach (var child in operation.ChildOperations) AddOperations(child, block, blocks);
    }
}
