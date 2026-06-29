using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Smt;

var options = SymbolicCliOptions.Parse(args);
if (options.ShowHelp || options.FilePath == null)
{
    Console.Error.WriteLine(SymbolicCliOptions.Usage);
    return options.ShowHelp ? 0 : 64;
}

try
{
    var smtAnalysis = options.RequiresSmt
        ? new SmtAnalysisService(options.CreateSmtOptions())
        : null;

    var queryService = new SymbolicSourceQueryService();
    object result = options.AllLines
        ? QueryFileAllLines(queryService, options, smtAnalysis)
        : options.LineInvariants
        ? queryService.QueryFileLine(
            options.FilePath,
            options.Line,
            options.CreateReferences(),
            smtAnalysis: smtAnalysis,
            impliedConditions: options.ImpliedConditions)
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

    if (options.Json)
    {
        var json = result switch
        {
            IReadOnlyList<SymbolicLineQueryResult> fileResult => JsonSerializer.Serialize(fileResult, new JsonSerializerOptions { WriteIndented = true }),
            SymbolicLineQueryResult lineResult => JsonSerializer.Serialize(lineResult, new JsonSerializerOptions { WriteIndented = true }),
            SymbolicSourceQueryResult pointResult => JsonSerializer.Serialize(pointResult, new JsonSerializerOptions { WriteIndented = true }),
            _ => throw new InvalidOperationException("Unexpected query result type."),
        };
        Console.WriteLine(json);
    }
    else if (result is IReadOnlyList<SymbolicLineQueryResult> fileResult)
    {
        PrintFileResult(fileResult, options, smtAnalysis);
    }
    else if (result is SymbolicLineQueryResult lineResult)
    {
        PrintLineResult(lineResult, options);
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

static IReadOnlyList<SymbolicLineQueryResult> QueryFileAllLines(
    SymbolicSourceQueryService queryService,
    SymbolicCliOptions options,
    SmtAnalysisService? smtAnalysis)
{
    var filePath = Path.GetFullPath(options.FilePath!);
    var sourceText = File.ReadAllText(filePath);
    var syntaxTree = CSharpSyntaxTree.ParseText(
        sourceText,
        new CSharpParseOptions(LanguageVersion.Preview),
        filePath);
    var references = options.CreateReferences()?.ToArray() ??
        SymbolicSourceQueryService.GetTrustedPlatformReferences().ToArray();
    var compilation = CSharpCompilation.Create(
        "PurelySharp.SymbolicCli.Query",
        new[] { syntaxTree },
        references,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var lineCount = syntaxTree.GetText().Lines.Count;
    var results = new List<SymbolicLineQueryResult>();
    for (var line = 1; line <= lineCount; line++)
    {
        var lineResult = queryService.QuerySyntaxTreeLine(
            syntaxTree,
            compilation,
            line,
            smtAnalysis: smtAnalysis,
            impliedConditions: options.ImpliedConditions);
        if (lineResult.ProgramPoints.Count != 0)
        {
            results.Add(lineResult);
        }
    }

    return results;
}

static void PrintFileResult(
    IReadOnlyList<SymbolicLineQueryResult> results,
    SymbolicCliOptions options,
    SmtAnalysisService? smtAnalysis)
{
    Console.WriteLine($"{options.FilePath}");
    Console.WriteLine($"Lines with program points: {results.Count}");
    foreach (var lineResult in results)
    {
        Console.WriteLine();
        PrintLineResult(lineResult, options);
    }

    if (results.Count == 0 && smtAnalysis != null)
    {
        PrintSmtDiagnostics(SymbolicSmtDiagnostics.FromService(smtAnalysis));
    }
}

static void PrintLineResult(SymbolicLineQueryResult result, SymbolicCliOptions options)
{
    Console.WriteLine($"{result.FilePath}:{result.Line}");
    Console.WriteLine($"Program points: {result.ProgramPoints.Count}");
    Console.WriteLine($"Line merged invariant: {result.MergedInvariantText}");
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
    Console.WriteLine($"Merged invariant: {result.MergedInvariantText}");
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

internal sealed class SymbolicCliOptions
{
    public const string Usage = """
Usage: PurelySharp.SymbolicCli --file <path> (--line <n> [--column <n>] [--line-invariants] | --position <n>) [--json]

Options:
  --file <path>       C# source file to query.
  --line <n>          1-based source line to query.
  --column <n>        1-based source column to query. Default: 1.
  --line-invariants   Query every statement/expression program point on the line.
  --all-lines         Query every line that contains statement/expression program points.
  --position <n>      0-based absolute source position to query.
  --reference <path>  Metadata reference path. Can be repeated.
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
""";

    public string? FilePath { get; private set; }

    public int Line { get; private set; }

    public int Column { get; private set; } = 1;

    public int? Position { get; private set; }

    public bool LineInvariants { get; private set; }

    public bool AllLines { get; private set; }

    public List<string> ReferencePaths { get; } = new();

    public bool Json { get; private set; }

    public bool CheckReachability { get; private set; }

    public List<string> ImpliedConditions { get; } = new();

    public bool ShowHelp { get; private set; }

    public SmtAnalysisMode SmtMode { get; private set; } = SmtAnalysisOptions.Default.Mode;

    public int? SmtTimeoutMs { get; private set; }

    public int? SmtMethodBudgetMs { get; private set; }

    public int? SmtMaxPathConditions { get; private set; }

    public int? SmtMaxExpressionNodes { get; private set; }

    public bool RequiresSmt => CheckReachability || ImpliedConditions.Count != 0;

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
                case "--line-invariants":
                case "--all-line-points":
                    options.LineInvariants = true;
                    break;
                case "--all-lines":
                case "--file-invariants":
                    options.AllLines = true;
                    break;
                case "--reference":
                case "-r":
                    options.ReferencePaths.Add(ReadString(args, ref index, arg));
                    break;
                case "--json":
                    options.Json = true;
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

            if (options.AllLines &&
                (options.Position.HasValue || options.Line != 0 || options.Column != 1 || options.LineInvariants))
            {
                throw new ArgumentException("--all-lines cannot be combined with --line, --column, --position, or --line-invariants.");
            }

            if (options.Position.HasValue && options.LineInvariants)
            {
                throw new ArgumentException("--line-invariants cannot be combined with --position.");
            }

            if (options.LineInvariants && options.Column != 1)
            {
                throw new ArgumentException("--line-invariants cannot be combined with --column.");
            }

            if (!options.AllLines && !options.Position.HasValue && options.Line == 0)
            {
                throw new ArgumentException("--line, --position, or --all-lines is required.");
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

    public SmtAnalysisOptions CreateSmtOptions()
    {
        return SmtAnalysisOptions.ForMode(SmtMode).WithOverrides(
            SmtTimeoutMs.HasValue ? TimeSpan.FromMilliseconds(SmtTimeoutMs.Value) : null,
            SmtMethodBudgetMs.HasValue ? TimeSpan.FromMilliseconds(SmtMethodBudgetMs.Value) : null,
            SmtMaxPathConditions,
            SmtMaxExpressionNodes);
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
}
