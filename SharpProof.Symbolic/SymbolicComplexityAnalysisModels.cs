namespace SharpProof.Symbolic;

internal sealed record MethodAnalysisSummary(
    SymbolicCostExpression Cost,
    ImmutableArray<SymbolicComplexityDriverInfo> Drivers,
    ImmutableArray<SymbolicComplexityUnknownReason> UnknownReasons,
    ImmutableArray<SymbolicComplexityCalleeInfo> CalleeSummaries);

internal sealed record ComplexityArtifacts(
    SymbolicCostExpression Cost,
    IReadOnlyList<SymbolicComplexityDriverInfo> Drivers,
    IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons,
    IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries)
{
    public static readonly ComplexityArtifacts Constant = new(
        SymbolicCostExpression.Constant(),
        Array.Empty<SymbolicComplexityDriverInfo>(),
        Array.Empty<SymbolicComplexityUnknownReason>(),
        Array.Empty<SymbolicComplexityCalleeInfo>());

    public static ComplexityArtifacts FromCost(
        SymbolicCostExpression cost,
        IEnumerable<SymbolicComplexityDriverInfo>? drivers = null,
        IEnumerable<SymbolicComplexityUnknownReason>? unknownReasons = null,
        IEnumerable<SymbolicComplexityCalleeInfo>? calleeSummaries = null)
    {
        return new ComplexityArtifacts(
            cost,
            drivers?.ToArray() ?? Array.Empty<SymbolicComplexityDriverInfo>(),
            unknownReasons?.ToArray() ?? Array.Empty<SymbolicComplexityUnknownReason>(),
            calleeSummaries?.ToArray() ?? Array.Empty<SymbolicComplexityCalleeInfo>());
    }

    public static ComplexityArtifacts Unknown(
        SymbolicComplexityUnknownReason reason,
        SyntaxNode syntax,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken,
        params ComplexityArtifacts[] parts)
    {
        return Unknown(reason, syntax, syntaxTree, cancellationToken, parts.AsEnumerable());
    }

    public static ComplexityArtifacts Unknown(
        SymbolicComplexityUnknownReason reason,
        SyntaxNode syntax,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken,
        IEnumerable<ComplexityArtifacts>? parts = null,
        IEnumerable<SymbolicComplexityCalleeInfo>? calleeSummaries = null)
    {
        var drivers = new List<SymbolicComplexityDriverInfo>();
        var reasons = new List<SymbolicComplexityUnknownReason> { reason };
        var callees = new List<SymbolicComplexityCalleeInfo>();
        if (parts != null)
            foreach (var part in parts)
            {
                drivers.AddRange(part.Drivers);
                reasons.AddRange(part.UnknownReasons);
                callees.AddRange(part.CalleeSummaries);
            }

        if (calleeSummaries != null) callees.AddRange(calleeSummaries);

        drivers.Add(CreateUnknownDriver(reason, syntax, syntaxTree, cancellationToken));
        return FromCost(SymbolicCostExpression.Unknown(reason), drivers, reasons, callees);
    }

    public ComplexityArtifacts WithDriver(SymbolicComplexityDriverInfo driver)
    {
        var drivers = Drivers.ToList();
        drivers.Add(driver);
        return new ComplexityArtifacts(Cost, drivers, UnknownReasons, CalleeSummaries);
    }

    private static SymbolicComplexityDriverInfo CreateUnknownDriver(
        SymbolicComplexityUnknownReason reason,
        SyntaxNode syntax,
        SyntaxTree syntaxTree,
        CancellationToken cancellationToken)
    {
        var lineColumn = SymbolicSourceLocation.GetLineAndColumn(
            syntaxTree,
            syntax.SpanStart,
            cancellationToken,
            true);
        return new SymbolicComplexityDriverInfo(
            "Unknown",
            reason.ToString(),
            syntax.SpanStart,
            syntax.Span.Length,
            lineColumn.Line,
            lineColumn.Column);
    }
}

internal sealed record SubstitutionResult(
    SymbolicCostExpression Cost,
    IReadOnlyList<SymbolicComplexityDriverInfo> Drivers,
    IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons);

internal readonly record struct LoopBoundInfo(SymbolicCostExpression Cost, string Description);

internal enum StepDirection
{
    None,
    Up,
    Down
}

internal enum CostProjection
{
    Value,
    LengthOrCount
}
