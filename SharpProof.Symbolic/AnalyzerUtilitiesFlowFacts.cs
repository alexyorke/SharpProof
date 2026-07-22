namespace SharpProof.Symbolic;

internal sealed class AnalyzerUtilitiesFlowFacts {
    private readonly PointsToAnalysisResult _pointsTo;
    private readonly ImmutableDictionary<(OperationKind Kind, TextSpan Span), BasicBlock> _operationBlocks;

    internal AnalyzerUtilitiesFlowFacts(PointsToAnalysisResult pointsTo, ControlFlowGraph graph) {
        _pointsTo = pointsTo;
        var blocks = ImmutableDictionary.CreateBuilder<(OperationKind Kind, TextSpan Span), BasicBlock>();
        foreach (var block in graph.Blocks)
            foreach (var root in block.Operations.Append(block.BranchValue).Where(static operation => operation != null))
                foreach (var operation in root!.DescendantsAndSelf())
                    blocks[(operation.Kind, operation.Syntax.Span)] = block;
        _operationBlocks = blocks.ToImmutable();
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
}
