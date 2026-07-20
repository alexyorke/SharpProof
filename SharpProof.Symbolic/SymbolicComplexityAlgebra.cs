namespace SharpProof.Symbolic;

internal static class SymbolicComplexityAlgebra
{
    internal static ComplexityArtifacts CombineSequence(IEnumerable<ComplexityArtifacts> parts) =>
        Combine(parts);

    internal static ComplexityArtifacts CombineSequence(params ComplexityArtifacts[] parts) =>
        Combine(parts);

    internal static ComplexityArtifacts CombineBranch(IEnumerable<ComplexityArtifacts> parts) =>
        Combine(parts);

    internal static ComplexityArtifacts CombineBranch(params ComplexityArtifacts[] parts) =>
        Combine(parts);

    internal static ComplexityArtifacts Multiply(SymbolicCostExpression multiplier, ComplexityArtifacts body)
    {
        var cost = SymbolicCostExpression.Multiply(multiplier, body.Cost);
        var reasons = new List<SymbolicComplexityUnknownReason>(body.UnknownReasons);
        if (cost.IsUnknown && cost.UnknownReason != SymbolicComplexityUnknownReason.None)
            reasons.Add(cost.UnknownReason);

        return ComplexityArtifacts.FromCost(cost, body.Drivers, reasons, body.CalleeSummaries);
    }

    internal static MethodAnalysisSummary CreateSummary(
        SymbolicCostExpression cost,
        IEnumerable<SymbolicComplexityDriverInfo> drivers,
        IEnumerable<SymbolicComplexityUnknownReason> reasons,
        IEnumerable<SymbolicComplexityCalleeInfo> callees)
    {
        return new MethodAnalysisSummary(
            cost,
            drivers.ToImmutableArray(),
            reasons.ToImmutableArray(),
            callees.ToImmutableArray());
    }

    internal static SymbolicComplexityDriverInfo CreateDriver(
        string kind,
        string description,
        SyntaxNode node,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken)
    {
        var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            node.SpanStart,
            cancellationToken,
            true);
        return new SymbolicComplexityDriverInfo(
            kind,
            description,
            node.SpanStart,
            node.Span.Length,
            lineColumn.Line,
            lineColumn.Column);
    }

    internal static SymbolicComplexityCalleeInfo CreateCalleeInfo(
        string methodDisplayName,
        SymbolicCostExpression cost,
        IMethodSymbol contextMethod)
    {
        return new SymbolicComplexityCalleeInfo(
            methodDisplayName,
            cost.ToBigOText(contextMethod),
            cost.ToPublicKind(),
            cost.IsConservative,
            cost.UnknownReason);
    }

    private static ComplexityArtifacts Combine(IEnumerable<ComplexityArtifacts> parts)
    {
        var costExpressions = new List<SymbolicCostExpression>();
        var drivers = new List<SymbolicComplexityDriverInfo>();
        var reasons = new List<SymbolicComplexityUnknownReason>();
        var callees = new List<SymbolicComplexityCalleeInfo>();
        foreach (var part in parts.Where(static part => part != null))
        {
            costExpressions.Add(part.Cost);
            drivers.AddRange(part.Drivers);
            reasons.AddRange(part.UnknownReasons);
            callees.AddRange(part.CalleeSummaries);
        }

        var combinedCost = SymbolicCostExpression.Max(costExpressions);
        if (combinedCost.IsUnknown && combinedCost.UnknownReason != SymbolicComplexityUnknownReason.None)
            reasons.Add(combinedCost.UnknownReason);

        return ComplexityArtifacts.FromCost(combinedCost, drivers, reasons, callees);
    }
}
