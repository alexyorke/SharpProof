using System.Threading;

internal sealed class CliHost
{
    private static readonly AsyncLocal<CliHost?> AmbientHost = new();
    private static readonly CliHost SystemHost = new(
        System.Console.In,
        System.Console.Out,
        System.Console.Error,
        Directory.GetCurrentDirectory());

    private CliHost(TextReader input, TextWriter output, TextWriter error, string baseDirectory)
    {
        Input = input ?? throw new ArgumentNullException(nameof(input));
        Output = output ?? throw new ArgumentNullException(nameof(output));
        Error = error ?? throw new ArgumentNullException(nameof(error));
        BaseDirectory = Path.GetFullPath(baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory)));
    }

    public static CliHost Current => AmbientHost.Value ?? SystemHost;

    public TextReader Input { get; }

    public TextWriter Output { get; }

    public TextWriter Error { get; }

    public string BaseDirectory { get; }

    public static string GetFullPath(string path)
    {
        return Path.GetFullPath(path, Current.BaseDirectory);
    }

    internal static IDisposable BeginScope(
        TextReader input,
        TextWriter output,
        TextWriter error,
        string baseDirectory)
    {
        var previous = AmbientHost.Value;
        AmbientHost.Value = new CliHost(input, output, error, baseDirectory);
        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly CliHost? _previous;
        private bool _disposed;

        public Scope(CliHost? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            AmbientHost.Value = _previous;
            _disposed = true;
        }
    }
}

internal static class Console
{
    public static TextReader In => CliHost.Current.Input;

    public static TextWriter Out => CliHost.Current.Output;

    public static TextWriter Error => CliHost.Current.Error;

    public static void Write(string? value)
    {
        Out.Write(value);
    }

    public static void WriteLine()
    {
        Out.WriteLine();
    }

    public static void WriteLine(string? value)
    {
        Out.WriteLine(value);
    }
}
