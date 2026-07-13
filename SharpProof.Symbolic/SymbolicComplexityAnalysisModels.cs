using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic;

internal sealed class ResolvedComplexityTarget
{
    public ResolvedComplexityTarget(
        SyntaxTree syntaxTree,
        SemanticModel semanticModel,
        SyntaxNode declaration,
        SyntaxNode bodyNode,
        IMethodSymbol symbol,
        string filePath,
        string methodName,
        string methodDisplayName,
        string declarationKind,
        int spanStart,
        int spanEnd,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn)
    {
        SyntaxTree = syntaxTree;
        SemanticModel = semanticModel;
        Declaration = declaration;
        BodyNode = bodyNode;
        Symbol = symbol;
        FilePath = filePath;
        MethodName = methodName;
        MethodDisplayName = methodDisplayName;
        DeclarationKind = declarationKind;
        SpanStart = spanStart;
        SpanEnd = spanEnd;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
    }

    public SyntaxTree SyntaxTree { get; }

    public SemanticModel SemanticModel { get; }

    public SyntaxNode Declaration { get; }

    public SyntaxNode BodyNode { get; }

    public IMethodSymbol Symbol { get; }

    public string FilePath { get; }

    public string MethodName { get; }

    public string MethodDisplayName { get; }

    public string DeclarationKind { get; }

    public int SpanStart { get; }

    public int SpanEnd { get; }

    public int StartLine { get; }

    public int StartColumn { get; }

    public int EndLine { get; }

    public int EndColumn { get; }
}

internal sealed class MethodAnalysisSummary
{
    public MethodAnalysisSummary(
        SymbolicCostExpression cost,
        ImmutableArray<SymbolicComplexityDriverInfo> drivers,
        ImmutableArray<SymbolicComplexityUnknownReason> unknownReasons,
        ImmutableArray<SymbolicComplexityCalleeInfo> calleeSummaries)
    {
        Cost = cost;
        Drivers = drivers;
        UnknownReasons = unknownReasons;
        CalleeSummaries = calleeSummaries;
    }

    public SymbolicCostExpression Cost { get; }

    public ImmutableArray<SymbolicComplexityDriverInfo> Drivers { get; }

    public ImmutableArray<SymbolicComplexityUnknownReason> UnknownReasons { get; }

    public ImmutableArray<SymbolicComplexityCalleeInfo> CalleeSummaries { get; }
}

internal sealed class ComplexityArtifacts
{
    public static readonly ComplexityArtifacts Constant = new(
        SymbolicCostExpression.Constant(),
        Array.Empty<SymbolicComplexityDriverInfo>(),
        Array.Empty<SymbolicComplexityUnknownReason>(),
        Array.Empty<SymbolicComplexityCalleeInfo>());

    private ComplexityArtifacts(
        SymbolicCostExpression cost,
        IReadOnlyList<SymbolicComplexityDriverInfo> drivers,
        IReadOnlyList<SymbolicComplexityUnknownReason> unknownReasons,
        IReadOnlyList<SymbolicComplexityCalleeInfo> calleeSummaries)
    {
        Cost = cost;
        Drivers = drivers;
        UnknownReasons = unknownReasons;
        CalleeSummaries = calleeSummaries;
    }

    public SymbolicCostExpression Cost { get; }

    public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers { get; }

    public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons { get; }

    public IReadOnlyList<SymbolicComplexityCalleeInfo> CalleeSummaries { get; }

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

internal sealed class SubstitutionResult
{
    public SubstitutionResult(
        SymbolicCostExpression cost,
        IReadOnlyList<SymbolicComplexityDriverInfo> drivers,
        IReadOnlyList<SymbolicComplexityUnknownReason> unknownReasons)
    {
        Cost = cost;
        Drivers = drivers;
        UnknownReasons = unknownReasons;
    }

    public SymbolicCostExpression Cost { get; }

    public IReadOnlyList<SymbolicComplexityDriverInfo> Drivers { get; }

    public IReadOnlyList<SymbolicComplexityUnknownReason> UnknownReasons { get; }
}

internal readonly struct LoopBoundInfo
{
    public LoopBoundInfo(SymbolicCostExpression cost, string description)
    {
        Cost = cost;
        Description = description;
    }

    public SymbolicCostExpression Cost { get; }

    public string Description { get; }
}

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
