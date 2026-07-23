namespace SharpProof.Symbolic;

internal sealed class AnalyzerUtilitiesFlowFacts {
    private readonly PointsToAnalysisResult _pointsTo;

    internal AnalyzerUtilitiesFlowFacts(PointsToAnalysisResult pointsTo) => _pointsTo = pointsTo;

    internal EffectFlowValue RefineNullState(
        IOperation operation,
        ISymbol? symbol,
        BasicBlock block,
        EffectFlowValue value) {
        PointsToAbstractValue pointsTo;
        try { pointsTo = _pointsTo[operation]; }
        catch (KeyNotFoundException) { return value; }
        var nullState = pointsTo.NullState;
        if (nullState is not (NullAbstractValue.Null or NullAbstractValue.NotNull) && symbol != null) {
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
