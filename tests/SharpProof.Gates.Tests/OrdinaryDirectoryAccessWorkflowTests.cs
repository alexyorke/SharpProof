using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
[Platform("Linux")]
public sealed class OrdinaryDirectoryAccessWorkflowTests
{
    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void UnavailableOwnedDirectoryLookupKeepsTheContainedLexicalPath()
    {
        using var temporary = new TempDirectory("SharpProof-directory-access-");
        var directory = Path.Combine(temporary.FullName, "ordinary-source");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "ordinary.cs");
        const string source = "public static class OrdinarySource { public static int Value() => 37; }\n";
        File.WriteAllText(path, source);
        var fullPath = Path.GetFullPath(path);
        Assert.That(OpenSourceCorpusCatalog.ResolvePath(path), Is.EqualTo(fullPath));
        var originalMode = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory,
                originalMode & ~(UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute));
            Assert.Throws<UnauthorizedAccessException>((Action)(() => File.ReadAllText(path)));

            Assert.That(OpenSourceCorpusCatalog.ResolvePath(path), Is.EqualTo(fullPath));
            Assert.DoesNotThrow((Action)(() => OpenSourceCorpusCatalog.EnsureContained(temporary.FullName, path)));
        }
        finally
        {
            File.SetUnixFileMode(directory, originalMode);
        }
        Assert.That(File.ReadAllText(path), Is.EqualTo(source));
        Assert.That(OpenSourceCorpusCatalog.ResolvePath(path), Is.EqualTo(fullPath));
    }
}
