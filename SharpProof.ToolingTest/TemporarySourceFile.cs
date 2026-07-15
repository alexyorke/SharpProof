using NUnit.Framework;

namespace SharpProof.Test;

internal sealed class TemporarySourceFile(string path) : IDisposable
{
    internal string Path { get; } = path;

    internal static TemporarySourceFile Create(string fileNamePrefix, string source)
    {
        var path = System.IO.Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            fileNamePrefix + Guid.NewGuid().ToString("N") + ".cs");
        File.WriteAllText(path, source);
        return new TemporarySourceFile(path);
    }

    public void Dispose()
    {
        File.Delete(Path);
    }
}
