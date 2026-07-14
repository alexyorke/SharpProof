namespace SharpProof.Test;

internal static class DisposableTestSources
{
    internal const string CommonUsings = "\nusing System;\nusing SharpProof.Attributes;\n\n";
    internal const string AsyncUsings =
        "\nusing System;\nusing System.Threading.Tasks;\nusing SharpProof.Attributes;\n\n";

    internal const string ImpureDisposable = """
public sealed class ImpureDisposable : IDisposable
{
    public static int Count;
    public void Dispose() => Count++;
}
""" + "\n\n";

    internal const string PureDisposable = """
public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }
}
""" + "\n\n";

    internal const string PureDisposableWithUse = """
public sealed class PureDisposable : IDisposable
{
    [EnforcePure]
    public void Dispose()
    {
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}
""" + "\n\n";

    internal const string PureAsyncDisposable = """
public sealed class PureAsyncDisposable : IAsyncDisposable
{
    [EnforcePure]
    public ValueTask DisposeAsync()
    {
        return default;
    }
}
""" + "\n\n";

    internal const string PureAsyncDisposableWithUse = """
public sealed class PureAsyncDisposable : IAsyncDisposable
{
    [EnforcePure]
    public ValueTask DisposeAsync()
    {
        return default;
    }

    [EnforcePure]
    public int Use()
    {
        return 1;
    }
}
""" + "\n\n";
}
