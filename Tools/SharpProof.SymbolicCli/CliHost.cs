using System.Threading;

internal sealed class CliHost(
    TextReader input,
    TextWriter output,
    TextWriter error,
    string baseDirectory)
{
    private static readonly AsyncLocal<CliHost?> AmbientHost = new();
    private static readonly CliHost SystemHost = new(
        System.Console.In,
        System.Console.Out,
        System.Console.Error,
        Directory.GetCurrentDirectory());

    public static CliHost Current => AmbientHost.Value ?? SystemHost;

    public TextReader Input { get; } = input ?? throw new ArgumentNullException(nameof(input));

    public TextWriter Output { get; } = output ?? throw new ArgumentNullException(nameof(output));

    public TextWriter Error { get; } = error ?? throw new ArgumentNullException(nameof(error));

    public string BaseDirectory { get; } =
        Path.GetFullPath(baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory)));

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

    private sealed class Scope(CliHost? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            AmbientHost.Value = previous;
            _disposed = true;
        }
    }

}

internal static class Console
{
    public static TextReader In => CliHost.Current.Input;

    public static TextWriter Out => CliHost.Current.Output;

    public static TextWriter Error => CliHost.Current.Error;


    public static void WriteLine() => Out.WriteLine();

    public static void WriteLine(string? value) => Out.WriteLine(value);
}
