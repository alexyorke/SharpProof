using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance", "CA1849", Justification = "These tests exercise the synchronous cleanup API.")]
public sealed class IrStagedCleanupFailureTests
{
    [Test]
    public void CleanupRetainsFailedStagingFileUntilDirectoryBecomesWritable()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("This lifecycle test requires the canonical Linux test environment.");
        }
        using var temporary = new TempDirectory("SharpProof.Ir.StagingCleanup.");
        var directory = Path.Combine(temporary.FullName, "staging");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "pending.tmp");
        File.WriteAllText(file, "pending");
        var originalMode = File.GetUnixFileMode(directory);
        try
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.DoesNotThrow((Action)(() => AtomicFile.TryDeleteStaged(file)));
            Assert.That(File.Exists(file), Is.True);
        }
        finally
        {
            File.SetUnixFileMode(directory, originalMode);
        }
        AtomicFile.TryDeleteStaged(file);
        Assert.That(File.Exists(file), Is.False);
    }
}
