using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
[Platform("Linux")]
public sealed class OrdinaryReadOnlyTransactionWorkflowTests
{
    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void PublicationLosingDirectoryWriteAccessPreservesRecoveryUntilAccessReturns()
    {
        using var temporary = new TempDirectory("SharpProof-read-only-publication-");
        var first = Path.Combine(temporary.FullName, "first.txt");
        var second = Path.Combine(temporary.FullName, "second.txt");
        var marker = Path.Combine(temporary.FullName, ".sharpproof-corpus-transaction.json");
        var originalMode = File.GetUnixFileMode(temporary.FullName);
        try
        {
            Assert.ThrowsAsync<UnauthorizedAccessException>((Func<Task>)(async () =>
                await CorpusFileTransaction.WriteAllAsync(temporary.FullName,
                    [new CorpusFileUpdate(first, "first\n"), new CorpusFileUpdate(second, "second\n")], CancellationToken.None,
                    beforePublish: index =>
                    {
                        if (index == 1)
                        {
                            Assert.That(File.ReadAllText(first), Is.EqualTo("first\n"));
                            Assert.That(File.Exists(marker), Is.True);
                            File.SetUnixFileMode(temporary.FullName,
                                originalMode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
                        }
                    })));
            Assert.That(File.ReadAllText(first), Is.EqualTo("first\n"));
            Assert.That(File.Exists(second), Is.False);
            Assert.That(File.Exists(marker), Is.True);
            Assert.That(Directory.EnumerateFiles(temporary.FullName, "*.new").Count(), Is.EqualTo(1));
        }
        finally
        {
            File.SetUnixFileMode(temporary.FullName, originalMode);
        }

        CorpusFileTransaction.Recover(temporary.FullName);

        Assert.That(File.Exists(first), Is.False);
        Assert.That(File.Exists(second), Is.False);
        Assert.That(Directory.EnumerateFiles(temporary.FullName), Is.Empty);
    }
}
