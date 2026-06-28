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
    var smtAnalysis = options.CheckReachability || options.ImpliedConditions.Count != 0
        ? new SmtAnalysisService(SmtAnalysisOptions.Default)
        : null;

    var queryService = new SymbolicSourceQueryService();
    var result = queryService.QueryFile(
        options.FilePath,
        options.Line,
        options.Column,
        options.CreateReferences(),
        smtAnalysis: smtAnalysis,
        impliedConditions: options.ImpliedConditions);

    if (options.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine($"{result.FilePath}:{result.Line}:{result.Column}");
        Console.WriteLine($"Node: {result.NodeKind}");
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
Usage: PurelySharp.SymbolicCli --file <path> --line <n> [--column <n>] [--json]

Options:
  --file <path>       C# source file to query.
  --line <n>          1-based source line to query.
  --column <n>        1-based source column to query. Default: 1.
  --reference <path>  Metadata reference path. Can be repeated.
  --check-reachability
                      Use bounded SMT to classify whether the queried program point is reachable.
  --implies <expr>    Use bounded SMT to prove whether invariants at the queried point imply expr. Can be repeated.
  --json              Emit JSON instead of text.
""";

    public string? FilePath { get; private set; }

    public int Line { get; private set; }

    public int Column { get; private set; } = 1;

    public List<string> ReferencePaths { get; } = new();

    public bool Json { get; private set; }

    public bool CheckReachability { get; private set; }

    public List<string> ImpliedConditions { get; } = new();

    public bool ShowHelp { get; private set; }

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

            if (options.Line == 0)
            {
                throw new ArgumentException("--line is required.");
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
}
