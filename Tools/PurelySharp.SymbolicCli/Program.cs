using System.Text.Json;
using Microsoft.CodeAnalysis;
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
    var result = options.Position.HasValue
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
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine($"{result.FilePath}:{result.Line}:{result.Column}");
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
            Console.WriteLine("SMT:");
            Console.WriteLine($"  Mode: {result.SmtDiagnostics.Mode}");
            Console.WriteLine($"  Enabled: {result.SmtDiagnostics.IsEnabled}");
            Console.WriteLine($"  Query timeout ms: {result.SmtDiagnostics.QueryTimeoutMs}");
            Console.WriteLine($"  Method budget ms: {result.SmtDiagnostics.MethodBudgetMs}");
            Console.WriteLine($"  Max path conditions: {result.SmtDiagnostics.MaxPathConditions}");
            Console.WriteLine($"  Max expression nodes: {result.SmtDiagnostics.MaxExpressionNodes}");
            Console.WriteLine($"  Executed queries: {result.SmtDiagnostics.ExecutedQueryCount}");
            Console.WriteLine($"  Cache entries: {result.SmtDiagnostics.CacheEntryCount}");
        }

        Console.WriteLine("Facts:");
        if (result.Facts.Count == 0)
        {
            Console.WriteLine("  <none>");
        }
        else
        {
            foreach (var fact in result.Facts)
            {
                Console.WriteLine("  " + fact);
            }
        }
    }

    return 0;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(SymbolicCliOptions.Usage);
    return 64;
}

internal sealed class SymbolicCliOptions
{
    public const string Usage = """
Usage: PurelySharp.SymbolicCli --file <path> (--line <n> [--column <n>] | --position <n>) [--json]

Options:
  --file <path>       C# source file to query.
  --line <n>          1-based source line to query.
  --column <n>        1-based source column to query. Default: 1.
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

            if (!options.Position.HasValue && options.Line == 0)
            {
                throw new ArgumentException("--line or --position is required.");
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
