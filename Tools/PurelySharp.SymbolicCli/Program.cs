using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

try
{
    var options = SymbolicCliOptions.Parse(args);
    if (options.ShowHelp || options.FilePath == null)
    {
        Console.Error.WriteLine(SymbolicCliOptions.Usage);
        return options.ShowHelp ? 0 : 64;
    }

    var smtAnalysis = options.RequiresSmt
        ? new SmtAnalysisService(options.CreateSmtOptions())
        : null;

    var queryService = new SymbolicSourceQueryService();
    object result = options.AllLines
            ? queryService.QueryFileAllLines(
            options.FilePath,
            options.CreateReferences(),
            smtAnalysis: smtAnalysis,
            impliedConditions: options.ImpliedConditions,
            includeExpressionProgramPoints: options.LineExpressions)
        : options.LineInvariants
        ? queryService.QueryFileLine(
            options.FilePath,
            options.Line,
            options.CreateReferences(),
            smtAnalysis: smtAnalysis,
            impliedConditions: options.ImpliedConditions,
            includeExpressionProgramPoints: options.LineExpressions)
        : options.IsSpanQuery
        ? queryService.QueryFileSpan(
            options.FilePath,
            options.SpanStart!.Value,
            options.SpanEnd!.Value,
            options.CreateReferences(),
            smtAnalysis: smtAnalysis,
            impliedConditions: options.ImpliedConditions,
            includeExpressionProgramPoints: options.LineExpressions)
        : options.Position.HasValue
            ? queryService.QueryFileAtPosition(
            options.FilePath,
            options.Position.Value,
            options.CreateReferences(),
            smtAnalysis: smtAnalysis,
            impliedConditions: options.ImpliedConditions)
            : queryService.QueryFile(
            new SymbolicFileQuery(
                options.FilePath,
                options.Line,
                options.Column,
                options.CreateReferences(),
                options.ImpliedConditions),
            smtAnalysis: smtAnalysis);

    if (options.HasResultFilter)
    {
        var filter = options.CreateResultFilter();
        result = result switch
        {
            SymbolicFileQueryResult fileResult => fileResult.Filter(filter),
            SymbolicLineQueryResult lineResult => lineResult.Filter(filter),
            SymbolicSpanQueryResult spanResult => spanResult.Filter(filter),
            _ => result,
        };
    }

    if (options.CompactJson)
    {
        var compactResult = result switch
        {
            SymbolicFileQueryResult fileResult => fileResult.ToCompactResult(options.CreateCompactOptions()),
            SymbolicLineQueryResult lineResult => lineResult.ToCompactResult(options.CreateCompactOptions()),
            SymbolicSpanQueryResult spanResult => spanResult.ToCompactResult(options.CreateCompactOptions()),
            SymbolicSourceQueryResult pointResult => pointResult.ToCompactResult(options.CreateCompactOptions()),
            _ => throw new InvalidOperationException("Unexpected query result type."),
        };
        Console.WriteLine(JsonSerializer.Serialize(
            compactResult,
            CreateCompactJsonOptions()));
    }
    else if (options.Json)
    {
        var json = result switch
        {
            SymbolicFileQueryResult fileResult => JsonSerializer.Serialize(fileResult, CreateFullJsonOptions()),
            SymbolicLineQueryResult lineResult => JsonSerializer.Serialize(lineResult, CreateFullJsonOptions()),
            SymbolicSpanQueryResult spanResult => JsonSerializer.Serialize(spanResult, CreateFullJsonOptions()),
            SymbolicSourceQueryResult pointResult => JsonSerializer.Serialize(pointResult, CreateFullJsonOptions()),
            _ => throw new InvalidOperationException("Unexpected query result type."),
        };
        Console.WriteLine(json);
    }
    else if (result is SymbolicFileQueryResult fileResult)
    {
        PrintFileResult(fileResult, options);
    }
    else if (result is SymbolicLineQueryResult lineResult)
    {
        PrintLineResult(lineResult, options);
    }
    else if (result is SymbolicSpanQueryResult spanResult)
    {
        PrintSpanResult(spanResult, options);
    }
    else
    {
        PrintPointResult((SymbolicSourceQueryResult)result, options, includeLocation: true);
    }

    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(SymbolicCliOptions.Usage);
    return 64;
}

static void PrintFileResult(
    SymbolicFileQueryResult result,
    SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}");
    Console.WriteLine($"Total lines: {result.LineCount}");
    Console.WriteLine($"Lines with program points: {result.LinesWithProgramPoints}");
    Console.WriteLine($"Program points: {result.ProgramPointCount}");
    PrintProgramPointSummary(result.ProgramPointSummary, options);
    Console.WriteLine($"Merged invariant: {result.MergedInvariantText}");
    Console.WriteLine($"Merged invariant merge: {result.MergedInvariant.MergeKind}");
    Console.WriteLine($"Merged invariant conditions: {result.MergedInvariant.ConditionCount}");
    PrintInvariantQuery("Merged invariant query", result.InvariantQuery);
    PrintMergedPathFacts("Merged path facts", result.MergedPathFacts);
    Console.WriteLine($"Observed distinct facts: {result.ObservedFactCount}");
    Console.WriteLine($"Observed invariant merge: {result.ObservedInvariant.MergeKind}");
    Console.WriteLine($"Observed invariant conditions: {result.ObservedInvariant.ConditionCount}");
    if (options.CheckReachability)
    {
        Console.WriteLine(
            "Reachability summary: " +
            $"Reachable={result.Reachability.ReachableCount}, " +
            $"Unreachable={result.Reachability.UnreachableCount}, " +
            $"Unknown={result.Reachability.UnknownCount}, " +
            $"NotChecked={result.Reachability.NotCheckedCount}");
    }

    foreach (var proof in result.ConditionProofs)
    {
        Console.WriteLine(
            $"Implies '{proof.Condition}' summary: " +
            $"ProvenTrue={proof.ProvenTrueCount}, " +
            $"ProvenFalse={proof.ProvenFalseCount}, " +
            $"Unreachable={proof.UnreachableCount}, " +
            $"Unknown={proof.UnknownCount}");
    }

    foreach (var lineResult in result.Lines)
    {
        Console.WriteLine();
        PrintLineResult(lineResult, options);
    }

    if (result.SmtDiagnostics.IsConfigured && result.Lines.Count == 0)
    {
        PrintSmtDiagnostics(result.SmtDiagnostics);
    }
}

static void PrintLineResult(SymbolicLineQueryResult result, SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}:{result.Line}");
    Console.WriteLine($"Program points: {result.ProgramPoints.Count}");
    PrintProgramPointSummary(result.ProgramPointSummary, options);
    Console.WriteLine($"Observed distinct facts: {result.ObservedFactCount}");
    Console.WriteLine($"Line merged invariant: {result.MergedInvariantText}");
    Console.WriteLine($"Line invariant merge: {result.MergedInvariant.MergeKind}");
    Console.WriteLine($"Line invariant conditions: {result.MergedInvariant.ConditionCount}");
    PrintInvariantQuery("Line invariant query", result.InvariantQuery);
    PrintMergedPathFacts("Line merged path facts", result.MergedPathFacts);
    foreach (var point in result.ProgramPoints)
    {
        Console.WriteLine();
        PrintPointResult(point, options, includeLocation: true);
    }

    if (result.SmtDiagnostics.IsConfigured && result.ProgramPoints.Count == 0)
    {
        PrintSmtDiagnostics(result.SmtDiagnostics);
    }
}

static void PrintSpanResult(SymbolicSpanQueryResult result, SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}:{result.SpanStart}-{result.SpanEnd}");
    Console.WriteLine($"Span lines: {result.StartLine}:{result.StartColumn}-{result.EndLine}:{result.EndColumn}");
    Console.WriteLine($"Program points: {result.ProgramPoints.Count}");
    PrintProgramPointSummary(result.ProgramPointSummary, options);
    Console.WriteLine($"Observed distinct facts: {result.ObservedFactCount}");
    Console.WriteLine($"Span merged invariant: {result.MergedInvariantText}");
    Console.WriteLine($"Span invariant merge: {result.MergedInvariant.MergeKind}");
    Console.WriteLine($"Span invariant conditions: {result.MergedInvariant.ConditionCount}");
    PrintInvariantQuery("Span invariant query", result.InvariantQuery);
    PrintMergedPathFacts("Span merged path facts", result.MergedPathFacts);
    foreach (var point in result.ProgramPoints)
    {
        Console.WriteLine();
        PrintPointResult(point, options, includeLocation: true);
    }

    if (result.SmtDiagnostics.IsConfigured && result.ProgramPoints.Count == 0)
    {
        PrintSmtDiagnostics(result.SmtDiagnostics);
    }
}

static void PrintMergedPathFacts(
    string label,
    SymbolicMergedPathFacts facts)
{
    Console.WriteLine(
        $"{label}: " +
        $"Always={facts.AlwaysFacts.Count}, " +
        $"Maybe={facts.MaybeFacts.Count}, " +
        $"Unknown={facts.ConservativeUnknownCount}, " +
        $"CandidatePoints={facts.CandidateProgramPointCount}, " +
        $"UnreachablePoints={facts.UnreachableProgramPointCount}");
    if (facts.ConservativeUnknownCount != 0)
    {
        Console.WriteLine(label + " unknowns: " + string.Join("; ", facts.ConservativeUnknowns));
        foreach (var diagnostic in facts.ConservativeUnknownDiagnostics)
        {
            Console.WriteLine(
                label + " unknown diagnostic: " +
                $"{diagnostic.UnknownText} target={diagnostic.Target} reason={diagnostic.Reason} " +
                $"maybe={string.Join("; ", diagnostic.MaybeFacts)}");
        }
    }
}

static void PrintInvariantQuery(
    string label,
    SymbolicInvariantQueryView query)
{
    Console.WriteLine(
        $"{label}: " +
        $"Must={query.MustFactCount}, " +
        $"Maybe={query.MaybeFactCount}, " +
        $"Unknown={query.UnknownFactCount}, " +
        $"CandidatePoints={query.CandidateProgramPointCount}, " +
        $"UnreachablePoints={query.UnreachableProgramPointCount}");
    if (query.MustFacts.Count != 0)
    {
        Console.WriteLine(label + " must facts: " + string.Join("; ", query.MustFacts));
    }

    if (query.MaybeFacts.Count != 0)
    {
        Console.WriteLine(label + " maybe facts: " + string.Join("; ", query.MaybeFacts));
    }

    if (query.UnknownFacts.Count != 0)
    {
        Console.WriteLine(label + " unknowns: " + string.Join("; ", query.UnknownFacts));
    }
}

static void PrintPointResult(
    SymbolicSourceQueryResult result,
    SymbolicCliOptions options,
    bool includeLocation)
{
    if (includeLocation)
    {
        Console.WriteLine($"{result.FilePath}:{result.Line}:{result.Column}");
    }

    Console.WriteLine($"Node: {result.NodeKind}");
    Console.WriteLine($"Program point kind: {result.ProgramPointKind}");
    if (!string.IsNullOrWhiteSpace(result.MethodName))
    {
        Console.WriteLine($"Method: {result.MethodName}");
    }

    Console.WriteLine($"Merged invariant: {result.MergedInvariantText}");
    Console.WriteLine($"Invariant merge: {result.Invariant.MergeKind}");
    Console.WriteLine($"Path conditions: {result.PathConditionCount}");
    Console.WriteLine($"Conservative unknown conditions: {result.Invariant.ConservativeUnknownCount}");
    PrintInvariantQuery("Invariant query", result.InvariantQuery);
    if (result.Invariant.ConditionCount != 0)
    {
        Console.WriteLine("Invariant conditions:");
        foreach (var condition in result.Invariant.Conditions)
        {
            var unknown = condition.IsConservativeUnknown ? " conservative-unknown" : string.Empty;
            Console.WriteLine(
                $"  [{condition.Index}] {condition.Text} " +
                $"target={condition.Target} kind={condition.FormulaKind}{unknown}");
        }
    }

    if (options.CheckReachability)
    {
        Console.WriteLine($"Reachability: {result.Reachability}");
        Console.WriteLine($"Reachability reason: {result.ReachabilityReason}");
    }

    foreach (var proof in result.ConditionProofs)
    {
        Console.WriteLine($"Implies '{proof.Condition}': {proof.TruthValue}");
        Console.WriteLine($"Implication reason: {proof.Reason}");
    }

    PrintProofOutcomeSummary(result.ProofOutcomes, indent: "");

    if (result.SmtDiagnostics.IsConfigured)
    {
        PrintSmtDiagnostics(result.SmtDiagnostics);
    }

    Console.WriteLine("Facts:");
    if (result.Facts.Count == 0)
    {
        Console.WriteLine("  <none>");
        return;
    }

    foreach (var fact in result.Facts)
    {
        Console.WriteLine("  " + fact);
    }
}

static void PrintProgramPointSummary(
    SymbolicProgramPointSummary summary,
    SymbolicCliOptions options)
{
    Console.WriteLine("Program point summary:");
    Console.WriteLine($"  Points: {summary.ProgramPointCount}");
    Console.WriteLine(
        "  Path conditions: " +
        $"Total={summary.TotalPathConditionCount}, " +
        $"MaxPerPoint={summary.MaxPathConditionCount}");
    if (options.CheckReachability)
    {
        Console.WriteLine(
            "  Reachability: " +
            $"Reachable={summary.Reachability.ReachableCount}, " +
            $"Unreachable={summary.Reachability.UnreachableCount}, " +
            $"Unknown={summary.Reachability.UnknownCount}, " +
            $"NotChecked={summary.Reachability.NotCheckedCount}");
    }

    PrintProofOutcomeSummary(summary.ProofOutcomes, indent: "  ");
}

static void PrintProofOutcomeSummary(
    SymbolicProofOutcomeSummary summary,
    string indent)
{
    Console.WriteLine(
        indent +
        "Proof outcomes: " +
        $"Total={summary.TotalCount}, " +
        $"ProvenTrue={summary.ProvenTrueCount}, " +
        $"ProvenFalse={summary.ProvenFalseCount}, " +
        $"Unreachable={summary.UnreachableCount}, " +
        $"Unknown={summary.UnknownCount}");
}

static void PrintSmtDiagnostics(SymbolicSmtDiagnostics diagnostics)
{
    Console.WriteLine("SMT:");
    Console.WriteLine($"  Mode: {diagnostics.Mode}");
    Console.WriteLine($"  Enabled: {diagnostics.IsEnabled}");
    Console.WriteLine($"  Query timeout ms: {diagnostics.QueryTimeoutMs}");
    Console.WriteLine($"  Method budget ms: {diagnostics.MethodBudgetMs}");
    Console.WriteLine($"  Max path conditions: {diagnostics.MaxPathConditions}");
    Console.WriteLine($"  Max expression nodes: {diagnostics.MaxExpressionNodes}");
    Console.WriteLine($"  Executed queries: {diagnostics.ExecutedQueryCount}");
    Console.WriteLine($"  Cache entries: {diagnostics.CacheEntryCount}");
}

static JsonSerializerOptions CreateCompactJsonOptions()
{
    var options = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
}

static JsonSerializerOptions CreateFullJsonOptions()
{
    var options = new JsonSerializerOptions
    {
        WriteIndented = true,
    };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
}

internal sealed class SymbolicCliOptions
{
    public const string Usage = """
Usage: PurelySharp.SymbolicCli --file <path> (--line <n> [--column <n>] [--line-invariants] | --position <n> | --span-start <n> --span-end <n>) [--json|--compact-json]

Options:
  --file <path>       C# source file to query.
  --line <n>          1-based source line to query.
  --column <n>        1-based source column to query. Default: 1.
  --line-invariants   Query every statement/expression program point on the line.
  --span-start <n>    0-based inclusive source span start to query.
  --span-end <n>      0-based exclusive source span end to query.
  --all-lines         Query every line that contains statement/expression program points.
  --line-expressions  Include expression program points in --line-invariants, --span-start/--span-end, or --all-lines.
  --position <n>      0-based absolute source position to query.
  --reference <path>  Metadata reference path. Can be repeated.
  --node-kind <kind>  Keep only matching Roslyn node kinds in --line-invariants or --all-lines output. Can be repeated.
  --program-point-kind <kind>
                      Keep only Statement, Expression, or Other program points. Can be repeated.
  --filter-line <n>   Keep only program points on this 1-based line in aggregate output. Can be repeated.
  --line-start <n>    Keep only program points at or after this 1-based line.
  --line-end <n>      Keep only program points at or before this 1-based line.
  --with-facts        Keep only program points that have at least one reported fact.
  --with-conditions   Keep only program points that have at least one path condition.
  --method <name>     Keep only program points inside a matching method/local function. Can be repeated.
  --method-contains <text>
                      Keep only program points inside a method/local function containing text. Can be repeated.
  --condition-target <target>
                      Keep only program points with a path condition for the target. Can be repeated.
  --condition <expr>  Keep only program points with an exact source-like path condition. Can be repeated.
  --condition-contains <text>
                      Keep only program points with a path condition containing text. Can be repeated.
  --reachability <r>  Keep only program points with reachability NotChecked, Unknown, Reachable, or Unreachable. Can be repeated.
  --with-proofs       Keep only program points with at least one implication proof result.
  --proof-outcome <v> Keep only program points with proof outcome Unknown, ProvenTrue, ProvenFalse, or Unreachable. Can be repeated.
  --proof-condition <expr>
                      Keep only program points with an exact implication condition. Can be repeated.
  --proof-condition-contains <text>
                      Keep only program points with an implication condition containing text. Can be repeated.
  --check-reachability
                      Use bounded SMT to classify whether the queried program point is reachable.
  --implies <expr>    Use bounded SMT to prove whether invariants at the queried point imply expr. Can be repeated.
  --smt-mode <mode>   SMT mode: off, bounded, or deep. Default: bounded.
  --smt-timeout-ms <n>
                      Per-query SMT timeout in milliseconds.
  --smt-method-budget-ms <n>
                      Total SMT budget for this CLI query in milliseconds.
  --smt-max-path-conditions <n>
                      Maximum path conditions before conservative fallback.
  --smt-max-expression-nodes <n>
                      Maximum formula nodes before conservative fallback.
  --json              Emit JSON instead of text.
  --compact-json      Emit compact bounded JSON with observed and conservative invariant summaries.
  --max-lines <n>     Maximum lines included in --compact-json output. Default: 100.
  --max-points <n>    Maximum program points included in --compact-json output. Default: 250.
  --max-facts <n>     Maximum raw SMT facts included in --compact-json output. Default: 50.
  --max-conditions <n>
                      Maximum condition strings included in --compact-json output. Default: 50.
  --max-proofs <n>    Maximum proof summaries/results included in --compact-json output. Default: 50.
  --summary-only      Shorthand for --compact-json with --max-lines 0 and --max-points 0.
""";

    public string? FilePath { get; private set; }

    public int Line { get; private set; }

    public int Column { get; private set; } = 1;

    public int? Position { get; private set; }

    public int? SpanStart { get; private set; }

    public int? SpanEnd { get; private set; }

    public bool LineInvariants { get; private set; }

    public bool AllLines { get; private set; }

    public bool LineExpressions { get; private set; }

    public List<string> ReferencePaths { get; } = new();

    public List<string> NodeKinds { get; } = new();

    public List<string> ProgramPointKinds { get; } = new();

    public List<int> FilterLines { get; } = new();

    public int? FilterLineStart { get; private set; }

    public int? FilterLineEnd { get; private set; }

    public bool WithFacts { get; private set; }

    public bool WithConditions { get; private set; }

    public List<string> MethodNames { get; } = new();

    public List<string> MethodNameContains { get; } = new();

    public List<string> ConditionTargets { get; } = new();

    public List<string> Conditions { get; } = new();

    public List<string> ConditionContains { get; } = new();

    public List<SymbolicReachability> ReachabilityFilters { get; } = new();

    public bool WithProofs { get; private set; }

    public List<SymbolicTruthValue> ProofOutcomes { get; } = new();

    public List<string> ProofConditions { get; } = new();

    public List<string> ProofConditionContains { get; } = new();

    public bool Json { get; private set; }

    public bool CompactJson { get; private set; }

    public bool CheckReachability { get; private set; }

    public List<string> ImpliedConditions { get; } = new();

    public bool ShowHelp { get; private set; }

    public SmtAnalysisMode SmtMode { get; private set; } = SmtAnalysisOptions.Default.Mode;

    public int? SmtTimeoutMs { get; private set; }

    public int? SmtMethodBudgetMs { get; private set; }

    public int? SmtMaxPathConditions { get; private set; }

    public int? SmtMaxExpressionNodes { get; private set; }

    public int CompactMaxLines { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxLines;

    public int CompactMaxProgramPoints { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxProgramPoints;

    public int CompactMaxFacts { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxFacts;

    public int CompactMaxConditions { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxConditions;

    public int CompactMaxProofs { get; private set; } = SymbolicCompactQueryOptions.DefaultMaxProofs;

    public bool HasCompactOutputLimit { get; private set; }

    public bool CompactSummaryOnly { get; private set; }

    public bool RequiresSmt => CheckReachability || ImpliedConditions.Count != 0;

    public bool IsSpanQuery => SpanStart.HasValue || SpanEnd.HasValue;

    public bool HasResultFilter =>
        NodeKinds.Count != 0 ||
        ProgramPointKinds.Count != 0 ||
        FilterLines.Count != 0 ||
        FilterLineStart.HasValue ||
        FilterLineEnd.HasValue ||
        WithFacts ||
        WithConditions ||
        MethodNames.Count != 0 ||
        MethodNameContains.Count != 0 ||
        ConditionTargets.Count != 0 ||
        Conditions.Count != 0 ||
        ConditionContains.Count != 0 ||
        ReachabilityFilters.Count != 0 ||
        WithProofs ||
        ProofOutcomes.Count != 0 ||
        ProofConditions.Count != 0 ||
        ProofConditionContains.Count != 0;

    public static SymbolicCliOptions Parse(string[] args)
    {
        var options = new SymbolicCliOptions();
        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            switch (arg)
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--file":
                    options.FilePath = ReadString(args, ref index, arg);
                    break;
                case "--line":
                    options.Line = ReadInt(args, ref index, arg);
                    break;
                case "--column":
                    options.Column = ReadInt(args, ref index, arg);
                    break;
                case "--position":
                    options.Position = ReadNonNegativeInt(args, ref index, arg);
                    break;
                case "--span-start":
                    options.SpanStart = ReadNonNegativeInt(args, ref index, arg);
                    break;
                case "--span-end":
                    options.SpanEnd = ReadNonNegativeInt(args, ref index, arg);
                    break;
                case "--line-invariants":
                case "--all-line-points":
                    options.LineInvariants = true;
                    break;
                case "--all-lines":
                case "--file-invariants":
                    options.AllLines = true;
                    break;
                case "--line-expressions":
                case "--include-expressions":
                    options.LineExpressions = true;
                    break;
                case "--reference":
                case "-r":
                    options.ReferencePaths.Add(ReadString(args, ref index, arg));
                    break;
                case "--node-kind":
                    options.NodeKinds.Add(ReadString(args, ref index, arg));
                    break;
                case "--program-point-kind":
                case "--point-kind":
                    options.ProgramPointKinds.Add(ReadProgramPointKind(args, ref index, arg));
                    break;
                case "--filter-line":
                    options.FilterLines.Add(ReadPositiveInt(args, ref index, arg));
                    break;
                case "--line-start":
                    options.FilterLineStart = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--line-end":
                    options.FilterLineEnd = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--with-facts":
                    options.WithFacts = true;
                    break;
                case "--with-conditions":
                    options.WithConditions = true;
                    break;
                case "--method":
                    options.MethodNames.Add(ReadString(args, ref index, arg));
                    break;
                case "--method-contains":
                    options.MethodNameContains.Add(ReadString(args, ref index, arg));
                    break;
                case "--condition-target":
                case "--target":
                    options.ConditionTargets.Add(ReadString(args, ref index, arg));
                    break;
                case "--condition":
                    options.Conditions.Add(ReadString(args, ref index, arg));
                    break;
                case "--condition-contains":
                    options.ConditionContains.Add(ReadString(args, ref index, arg));
                    break;
                case "--reachability":
                    options.ReachabilityFilters.Add(ReadReachability(args, ref index, arg));
                    break;
                case "--with-proofs":
                    options.WithProofs = true;
                    break;
                case "--proof-outcome":
                    options.ProofOutcomes.Add(ReadTruthValue(args, ref index, arg));
                    break;
                case "--proof-condition":
                    options.ProofConditions.Add(ReadString(args, ref index, arg));
                    break;
                case "--proof-condition-contains":
                    options.ProofConditionContains.Add(ReadString(args, ref index, arg));
                    break;
                case "--json":
                    options.Json = true;
                    break;
                case "--compact-json":
                case "--compact":
                    options.CompactJson = true;
                    break;
                case "--max-lines":
                    options.CompactMaxLines = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--max-points":
                    options.CompactMaxProgramPoints = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--max-facts":
                    options.CompactMaxFacts = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--max-conditions":
                    options.CompactMaxConditions = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--max-proofs":
                    options.CompactMaxProofs = ReadNonNegativeInt(args, ref index, arg);
                    options.HasCompactOutputLimit = true;
                    break;
                case "--summary-only":
                    options.CompactSummaryOnly = true;
                    options.CompactJson = true;
                    break;
                case "--check-reachability":
                    options.CheckReachability = true;
                    break;
                case "--implies":
                    options.ImpliedConditions.Add(ReadString(args, ref index, arg));
                    break;
                case "--smt-mode":
                    options.SmtMode = ReadSmtMode(args, ref index, arg);
                    break;
                case "--smt-timeout-ms":
                    options.SmtTimeoutMs = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--smt-method-budget-ms":
                    options.SmtMethodBudgetMs = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--smt-max-path-conditions":
                    options.SmtMaxPathConditions = ReadPositiveInt(args, ref index, arg);
                    break;
                case "--smt-max-expression-nodes":
                    options.SmtMaxExpressionNodes = ReadPositiveInt(args, ref index, arg);
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        if (!options.ShowHelp)
        {
            if (options.CompactSummaryOnly)
            {
                options.CompactMaxLines = SymbolicCompactQueryOptions.SummaryOnly.MaxLines;
                options.CompactMaxProgramPoints = SymbolicCompactQueryOptions.SummaryOnly.MaxProgramPoints;
            }

            if (options.Json && options.CompactJson)
            {
                throw new ArgumentException("--json cannot be combined with --compact-json.");
            }

            if (options.HasCompactOutputLimit && !options.CompactJson)
            {
                throw new ArgumentException("--max-lines, --max-points, --max-facts, --max-conditions, and --max-proofs require --compact-json.");
            }

            if (options.FilePath == null)
            {
                throw new ArgumentException("--file is required.");
            }

            if (!File.Exists(options.FilePath))
            {
                throw new ArgumentException("--file does not exist.");
            }

            if (options.Position.HasValue && options.Line != 0)
            {
                throw new ArgumentException("--position cannot be combined with --line.");
            }

            if (options.Position.HasValue && options.IsSpanQuery)
            {
                throw new ArgumentException("--position cannot be combined with --span-start or --span-end.");
            }

            if (options.IsSpanQuery && options.Line != 0)
            {
                throw new ArgumentException("--span-start and --span-end cannot be combined with --line.");
            }

            if (options.IsSpanQuery && options.LineInvariants)
            {
                throw new ArgumentException("--span-start and --span-end cannot be combined with --line-invariants.");
            }

            if (options.IsSpanQuery && options.Column != 1)
            {
                throw new ArgumentException("--span-start and --span-end cannot be combined with --column.");
            }

            if (options.IsSpanQuery && (!options.SpanStart.HasValue || !options.SpanEnd.HasValue))
            {
                throw new ArgumentException("--span-start and --span-end must be provided together.");
            }

            if (options.SpanEnd.HasValue &&
                options.SpanStart.HasValue &&
                options.SpanEnd.Value < options.SpanStart.Value)
            {
                throw new ArgumentException("--span-end cannot be less than --span-start.");
            }

            if (options.AllLines &&
                (options.Position.HasValue || options.IsSpanQuery || options.Line != 0 || options.Column != 1 || options.LineInvariants))
            {
                throw new ArgumentException("--all-lines cannot be combined with --line, --column, --position, --span-start, --span-end, or --line-invariants.");
            }

            if (options.Position.HasValue && options.LineInvariants)
            {
                throw new ArgumentException("--line-invariants cannot be combined with --position.");
            }

            if (options.LineInvariants && options.Column != 1)
            {
                throw new ArgumentException("--line-invariants cannot be combined with --column.");
            }

            if (options.LineExpressions && !options.LineInvariants && !options.AllLines && !options.IsSpanQuery)
            {
                throw new ArgumentException("--line-expressions requires --line-invariants, --span-start/--span-end, or --all-lines.");
            }

            if (options.FilterLineStart.HasValue &&
                options.FilterLineEnd.HasValue &&
                options.FilterLineStart.Value > options.FilterLineEnd.Value)
            {
                throw new ArgumentException("--line-start cannot be greater than --line-end.");
            }

            if (!options.AllLines && !options.Position.HasValue && !options.IsSpanQuery && options.Line == 0)
            {
                throw new ArgumentException("--line, --position, --span-start/--span-end, or --all-lines is required.");
            }

            if (options.HasResultFilter && !options.AllLines && !options.LineInvariants && !options.IsSpanQuery)
            {
                throw new ArgumentException("Result filters require --line-invariants, --span-start/--span-end, or --all-lines.");
            }

            foreach (var referencePath in options.ReferencePaths)
            {
                if (!File.Exists(referencePath))
                {
                    throw new ArgumentException("--reference does not exist: " + referencePath);
                }
            }
        }

        return options;
    }

    public SymbolicSourceQueryFilter CreateResultFilter()
    {
        return new SymbolicSourceQueryFilter(
            nodeKinds: NodeKinds,
            requireFacts: WithFacts,
            reachability: ReachabilityFilters,
            methodNames: MethodNames,
            requirePathConditions: WithConditions,
            conditionTargets: ConditionTargets,
            conditionTexts: Conditions,
            conditionTextContains: ConditionContains,
            methodNameContains: MethodNameContains,
            lines: FilterLines,
            lineStart: FilterLineStart,
            lineEnd: FilterLineEnd,
            programPointKinds: ProgramPointKinds,
            requireProofs: WithProofs,
            proofOutcomes: ProofOutcomes,
            proofConditions: ProofConditions,
            proofConditionContains: ProofConditionContains);
    }

    public SmtAnalysisOptions CreateSmtOptions()
    {
        return SmtAnalysisOptions.ForMode(SmtMode).WithOverrides(
            SmtTimeoutMs.HasValue ? TimeSpan.FromMilliseconds(SmtTimeoutMs.Value) : null,
            SmtMethodBudgetMs.HasValue ? TimeSpan.FromMilliseconds(SmtMethodBudgetMs.Value) : null,
            SmtMaxPathConditions,
            SmtMaxExpressionNodes);
    }

    public SymbolicCompactQueryOptions CreateCompactOptions()
    {
        return new SymbolicCompactQueryOptions(
            CompactMaxLines,
            CompactMaxProgramPoints,
            CompactMaxFacts,
            CompactMaxConditions,
            CompactMaxProofs);
    }

    public IEnumerable<MetadataReference>? CreateReferences()
    {
        if (ReferencePaths.Count == 0)
        {
            return null;
        }

        return ReferencePaths.Select(static path => MetadataReference.CreateFromFile(Path.GetFullPath(path)));
    }

    private static string ReadString(string[] args, ref int index, string optionName)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException(optionName + " requires a value.");
        }

        return args[++index];
    }

    private static int ReadInt(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName);
        if (!int.TryParse(value, out var parsed))
        {
            throw new ArgumentException(optionName + " requires an integer value.");
        }

        return parsed;
    }

    private static int ReadPositiveInt(string[] args, ref int index, string optionName)
    {
        var parsed = ReadInt(args, ref index, optionName);
        if (parsed <= 0)
        {
            throw new ArgumentException(optionName + " requires a positive integer value.");
        }

        return parsed;
    }

    private static int ReadNonNegativeInt(string[] args, ref int index, string optionName)
    {
        var parsed = ReadInt(args, ref index, optionName);
        if (parsed < 0)
        {
            throw new ArgumentException(optionName + " requires a non-negative integer value.");
        }

        return parsed;
    }

    private static SmtAnalysisMode ReadSmtMode(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName).Trim().ToLowerInvariant();
        switch (value)
        {
            case "off":
            case "false":
            case "disabled":
                return SmtAnalysisMode.Off;
            case "bounded":
            case "default":
            case "true":
                return SmtAnalysisMode.Bounded;
            case "deep":
            case "aggressive":
                return SmtAnalysisMode.Deep;
            default:
                throw new ArgumentException(optionName + " must be off, bounded, or deep.");
        }
    }

    private static SymbolicReachability ReadReachability(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName);
        if (Enum.TryParse<SymbolicReachability>(value, ignoreCase: true, out var reachability))
        {
            return reachability;
        }

        throw new ArgumentException(optionName + " must be NotChecked, Unknown, Reachable, or Unreachable.");
    }

    private static SymbolicTruthValue ReadTruthValue(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName);
        if (Enum.TryParse<SymbolicTruthValue>(value, ignoreCase: true, out var truthValue))
        {
            return truthValue;
        }

        throw new ArgumentException(optionName + " must be Unknown, ProvenTrue, ProvenFalse, or Unreachable.");
    }

    private static string ReadProgramPointKind(string[] args, ref int index, string optionName)
    {
        var value = ReadString(args, ref index, optionName).Trim();
        if (string.Equals(value, SymbolicProgramPointKinds.Statement, StringComparison.OrdinalIgnoreCase))
        {
            return SymbolicProgramPointKinds.Statement;
        }

        if (string.Equals(value, SymbolicProgramPointKinds.Expression, StringComparison.OrdinalIgnoreCase))
        {
            return SymbolicProgramPointKinds.Expression;
        }

        if (string.Equals(value, SymbolicProgramPointKinds.Other, StringComparison.OrdinalIgnoreCase))
        {
            return SymbolicProgramPointKinds.Other;
        }

        throw new ArgumentException(optionName + " must be Statement, Expression, or Other.");
    }
}
