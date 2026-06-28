using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Analyzer.Engine.Symbolic;

var options = SymbolicCliOptions.Parse(args);
if (options.ShowHelp || options.FilePath == null)
{
    Console.Error.WriteLine(SymbolicCliOptions.Usage);
    return options.ShowHelp ? 0 : 64;
}

try
{
    var source = File.ReadAllText(options.FilePath);
    var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview), options.FilePath);
    var compilation = CSharpCompilation.Create(
        "PurelySharp.SymbolicCli.Query",
        new[] { syntaxTree },
        GetTrustedPlatformReferences(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var semanticModel = compilation.GetSemanticModel(syntaxTree);
    var root = syntaxTree.GetRoot();
    var position = GetPosition(syntaxTree, options.Line, options.Column);
    var node = FindQueryNode(root, position);
    var service = new SymbolicInvariantService();
    var snapshot = node is ForStatementSyntax forStatement
        ? service.GetForInitialEntryInvariants(forStatement, semanticModel)
        : service.GetInvariantsAt(node, semanticModel);
    var result = new SymbolicCliResult(
        Path.GetFullPath(options.FilePath),
        options.Line,
        options.Column,
        node.Kind().ToString(),
        snapshot.Facts);

    if (options.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine($"{result.File}:{result.Line}:{result.Column}");
        Console.WriteLine($"Node: {result.NodeKind}");
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

static SyntaxNode FindQueryNode(SyntaxNode root, int position)
{
    var token = root.FindToken(position);
    var switchArm = token.Parent?
        .AncestorsAndSelf()
        .OfType<SwitchExpressionArmSyntax>()
        .FirstOrDefault(arm => arm.Expression.Span.Contains(position));
    if (switchArm != null)
    {
        return switchArm.Expression
            .DescendantNodesAndSelf()
            .Where(node => node.Span.Contains(position))
            .OfType<ExpressionSyntax>()
            .OrderBy(node => node.Span.Length)
            .FirstOrDefault()
            ?? switchArm.Expression;
    }

    return root
        .DescendantNodesAndSelf()
        .Where(node => node.Span.Contains(position))
        .OfType<StatementSyntax>()
        .OrderBy(node => node.Span.Length)
        .FirstOrDefault()
        ?? token.Parent
        ?? root;
}

static int GetPosition(SyntaxTree syntaxTree, int line, int column)
{
    if (line < 1)
    {
        throw new ArgumentException("--line must be 1 or greater.");
    }

    if (column < 1)
    {
        throw new ArgumentException("--column must be 1 or greater.");
    }

    var text = syntaxTree.GetText();
    if (line > text.Lines.Count)
    {
        throw new ArgumentException("--line exceeds the file line count.");
    }

    var textLine = text.Lines[line - 1];
    var zeroBasedColumn = column - 1;
    if (zeroBasedColumn > textLine.Span.Length)
    {
        throw new ArgumentException("--column exceeds the line length.");
    }

    return textLine.Start + zeroBasedColumn;
}

static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
{
    var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
    if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
    {
        throw new InvalidOperationException("TRUSTED_PLATFORM_ASSEMBLIES is not available.");
    }

    return trustedPlatformAssemblies
        .Split(Path.PathSeparator)
        .Where(static path => !string.IsNullOrWhiteSpace(path))
        .Select(static path => MetadataReference.CreateFromFile(path))
        .ToImmutableArray<MetadataReference>();
}

internal sealed class SymbolicCliOptions
{
    public const string Usage = """
Usage: PurelySharp.SymbolicCli --file <path> --line <n> [--column <n>] [--json]

Options:
  --file <path>    C# source file to query.
  --line <n>       1-based source line to query.
  --column <n>     1-based source column to query. Default: 1.
  --json           Emit JSON instead of text.
""";

    public string? FilePath { get; private set; }

    public int Line { get; private set; }

    public int Column { get; private set; } = 1;

    public bool Json { get; private set; }

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
                case "--json":
                    options.Json = true;
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
        }

        return options;
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

internal sealed class SymbolicCliResult
{
    public SymbolicCliResult(string file, int line, int column, string nodeKind, IReadOnlyList<string> facts)
    {
        File = file;
        Line = line;
        Column = column;
        NodeKind = nodeKind;
        Facts = facts;
    }

    public string File { get; }

    public int Line { get; }

    public int Column { get; }

    public string NodeKind { get; }

    public IReadOnlyList<string> Facts { get; }
}
