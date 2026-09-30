using System.Text;
using NUnit.Framework;

internal static class GoldenTest
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static IEnumerable<string> Cases(string stage, string? repositoryRoot = null)
    {
        var directory = StageDirectory(repositoryRoot ?? TestRepository.FindRoot(), stage);
        var sources = Directory.GetFiles(directory, "*.cs", SearchOption.TopDirectoryOnly);
        foreach (var expected in Directory.EnumerateFiles(directory, "*.expected", SearchOption.TopDirectoryOnly))
        {
            if (!File.Exists(Path.ChangeExtension(expected, ".cs")))
            {
                throw new InvalidDataException("Orphan golden expectation: " + Path.GetFileName(expected));
            }
        }
        return sources
            .Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal).Select(name => name!);
    }

    internal static GoldenCase Load(string stage, string name, string? repositoryRoot = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or "..")
        {
            throw new ArgumentException("A golden case must be a file name.", nameof(name));
        }
        var root = Path.GetFullPath(repositoryRoot ?? TestRepository.FindRoot());
        var directory = StageDirectory(root, stage);
        var relative = Path.GetRelativePath(root, Path.Combine(directory, name + ".cs"));
        var source = ReadPlainFile(root, relative);
        var expected = ReadExpectation(root, Path.ChangeExtension(relative, ".expected"));
        if (expected == null && Environment.GetEnvironmentVariable("SHARPPROOF_UPDATE_GOLDEN") != "1")
        {
            throw new InvalidDataException("Missing paired golden expectation: " + relative.Replace('\\', '/'));
        }
        return new GoldenCase(root, relative, source, expected, Decode(source), expected == null ? "" : Decode(expected));
    }

    internal static void Compare(GoldenCase fixture, string actual)
    {
        actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
        if (actual.Contains('\r', StringComparison.Ordinal))
        {
            throw new InvalidDataException("Golden output contains a bare carriage return.");
        }
        if (string.Equals(actual, fixture.Expected, StringComparison.Ordinal))
        {
            return;
        }
        if (Environment.GetEnvironmentVariable("SHARPPROOF_UPDATE_GOLDEN") != "1")
        {
            Assert.That(actual, Is.EqualTo(fixture.Expected), fixture.RelativePath.Replace('\\', '/'));
            return;
        }
        Update(fixture, Utf8.GetBytes(actual));
    }

    private static void Update(GoldenCase fixture, byte[] actual)
    {
        var sourceRoot = Environment.GetEnvironmentVariable("SHARPPROOF_GOLDEN_SOURCE_ROOT");
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Path.IsPathFullyQualified(sourceRoot))
        {
            throw new InvalidOperationException("Golden updates require the original source root captured by the canonical test entrypoint.");
        }
        sourceRoot = Path.GetFullPath(sourceRoot);
        RequireRepository(sourceRoot);
        var expectedRelative = Path.ChangeExtension(fixture.RelativePath, ".expected");
        RequireSame(ReadPlainFile(fixture.Root, fixture.RelativePath), fixture.SourceBytes, "Private golden source changed.");
        RequireSame(ReadExpectation(fixture.Root, expectedRelative), fixture.ExpectedBytes, "Private golden expectation changed.");
        RequireSame(ReadPlainFile(sourceRoot, fixture.RelativePath), fixture.SourceBytes, "Original golden source differs from the tested copy.");
        RequireSame(ReadExpectation(sourceRoot, expectedRelative), fixture.ExpectedBytes, "Original golden expectation differs from the tested copy.");
        var expectedPath = PlainPath(sourceRoot, expectedRelative, allowMissingLeaf: true);
        var temporary = expectedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(actual);
                stream.Flush(flushToDisk: true);
            }
            RequireSame(ReadPlainFile(sourceRoot, fixture.RelativePath), fixture.SourceBytes, "Original golden source changed during update.");
            RequireSame(ReadExpectation(sourceRoot, expectedRelative), fixture.ExpectedBytes, "Original golden expectation changed during update.");
            File.Move(temporary, expectedPath, overwrite: true);
            RequireSame(ReadPlainFile(sourceRoot, fixture.RelativePath), fixture.SourceBytes, "Original golden source changed during update.");
            RequireSame(ReadPlainFile(sourceRoot, expectedRelative), actual, "Golden expectation did not persist at the original source root.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Cannot update the paired golden expectation at the original source; it may be read-only: " +
                expectedRelative.Replace('\\', '/'), exception);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string StageDirectory(string root, string stage)
    {
        if (stage is not ("lowering" or "worker" or "analyzer"))
        {
            throw new ArgumentException("Unknown golden stage.", nameof(stage));
        }
        RequireRepository(root);
        return PlainPath(root, Path.Combine("tests", "golden", stage));
    }

    private static void RequireRepository(string root)
    {
        if (!File.Exists(Path.Combine(root, "SharpProof.slnx")) || !File.Exists(Path.Combine(root, "SharpProof.Release.props")))
        {
            throw new DirectoryNotFoundException("The original golden source is not a SharpProof repository.");
        }
    }

    private static byte[] ReadPlainFile(string root, string relative)
    {
        return File.ReadAllBytes(PlainPath(root, relative));
    }

    private static byte[]? ReadExpectation(string root, string relative)
    {
        var path = PlainPath(root, relative, allowMissingLeaf: true);
        if (Directory.Exists(path))
        {
            throw new InvalidDataException("Golden expectation is a directory.");
        }
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static string PlainPath(string root, string relative, bool allowMissingLeaf = false)
    {
        root = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Golden path escapes its source root.");
        }
        var current = path;
        while (true)
        {
            if (new FileInfo(current).LinkTarget != null)
            {
                throw new InvalidDataException("Golden paths must not contain symbolic links.");
            }
            if (!(allowMissingLeaf && current == path && !File.Exists(current) && !Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("Golden paths must not contain symbolic links.");
            }
            if (string.Equals(current, root, StringComparison.Ordinal))
            {
                return path;
            }
            current = Path.GetDirectoryName(current)!;
        }
    }

    private static void RequireSame(byte[]? actual, byte[]? expected, string message)
    {
        if ((actual == null) != (expected == null) || actual != null && !actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidDataException(message);
        }
    }

    private static string Decode(byte[] bytes)
    {
        var text = Utf8.GetString(bytes);
        if (text.Contains('\r', StringComparison.Ordinal) || text.StartsWith('\uFEFF'))
        {
            throw new InvalidDataException("Golden files require UTF-8 without BOM and LF line endings.");
        }
        return text;
    }
}

internal sealed class GoldenCase(string root, string relativePath, byte[] sourceBytes, byte[]? expectedBytes, string source, string expected)
{
    internal string Root { get; } = root;
    internal string RelativePath { get; } = relativePath;
    internal byte[] SourceBytes { get; } = sourceBytes;
    internal byte[]? ExpectedBytes { get; } = expectedBytes;
    internal string Source { get; } = source;
    internal string Expected { get; } = expected;
}
