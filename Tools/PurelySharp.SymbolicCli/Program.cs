using System.Text.Json;
using PurelySharp.Symbolic;

var options = SymbolicCliOptions.Parse(args);
if (options.ShowHelp || options.FilePath == null)
{
    Console.Error.WriteLine(SymbolicCliOptions.Usage);
    return options.ShowHelp ? 0 : 64;
}

try
{
    var result = new SymbolicSourceQueryService().QueryFile(
        options.FilePath,
        options.Line,
        options.Column);

    if (options.Json)
    {
        Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    else
    {
        Console.WriteLine($"{result.FilePath}:{result.Line}:{result.Column}");
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
