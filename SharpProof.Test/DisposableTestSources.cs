namespace SharpProof.Test;

internal static class DisposableTestSources {
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

    internal const string NonSealedPureDisposable = """


public class PureDisposable : IDisposable
{
    public void Dispose() { }
}
""";

    internal const string NonSealedImpureDisposable = """


public class ImpureDisposable : IDisposable
{
    private int _disposeCount;

    public void Dispose()
    {
        _disposeCount++;
    }
}
""";

    internal const string ImpureFileOpenUsing = """

using System;
using SharpProof.Attributes;
using System.IO;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using (var file = File.OpenRead("test.txt"))
        {
            // Some operation
        }
    }
}
""";
}
