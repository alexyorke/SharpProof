using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryTransactionRollbackWorkflowTests
{
    [Test]
    public void InterruptedSecondPublicationRemovesBothNewDestinationsAndStaging()
    {
        using var temporary = new TempDirectory("SharpProof-new-corpus-rollback-");
        var first = Path.Combine(temporary.FullName, "first.txt");
        var second = Path.Combine(temporary.FullName, "second.txt");
        var error = Assert.ThrowsAsync<IOException>((Func<Task>)(async () =>
            await CorpusFileTransaction.WriteAllAsync(temporary.FullName,
                [new CorpusFileUpdate(first, "first\n"), new CorpusFileUpdate(second, "second\n")],
                TestContext.CurrentContext.CancellationToken, beforePublish: index =>
                {
                    if (index == 1)
                    {
                        Assert.That(File.ReadAllText(first), Is.EqualTo("first\n"));
                        throw new IOException("Ordinary interrupted publication.");
                    }
                })));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Ordinary interrupted publication."));
            Assert.That(File.Exists(first), Is.False);
            Assert.That(File.Exists(second), Is.False);
            Assert.That(Directory.EnumerateFiles(temporary.FullName), Is.Empty);
        }
    }

    [Test]
    public void UnavailableActualBackupPreservesDestinationAndRecoveryMarker()
    {
        using var temporary = new TempDirectory("SharpProof-unavailable-corpus-backup-");
        var destination = Path.Combine(temporary.FullName, "snapshot.txt");
        File.WriteAllText(destination, "reviewed snapshot\n");
        var markerPath = Path.Combine(temporary.FullName, ".sharpproof-corpus-transaction.json");
        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await CorpusFileTransaction.WriteAllAsync(temporary.FullName,
                [new CorpusFileUpdate(destination, "replacement\n")], TestContext.CurrentContext.CancellationToken,
                beforePublish: index =>
                {
                    Assert.That(index, Is.Zero);
                    var backup = Directory.EnumerateFiles(temporary.FullName, "*.old").Single();
                    Assert.That(File.ReadAllText(backup), Is.EqualTo("reviewed snapshot\n"));
                    Assert.That(File.Exists(markerPath), Is.True);
                    File.Delete(backup);
                    throw new IOException("Ordinary interrupted publication.");
                })));
        var marker = File.ReadAllText(markerPath);
        var recoveryError = Assert.Throws<InvalidDataException>((Action)(() =>
            CorpusFileTransaction.Recover(temporary.FullName)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("A corpus transaction backup is unavailable."));
            Assert.That(recoveryError!.Message, Is.EqualTo("A corpus transaction backup is unavailable."));
            Assert.That(File.ReadAllText(destination), Is.EqualTo("reviewed snapshot\n"));
            Assert.That(File.ReadAllText(markerPath), Is.EqualTo(marker));
            Assert.That(Directory.EnumerateFiles(temporary.FullName, "*.new").Count(), Is.EqualTo(1));
            Assert.That(Directory.EnumerateFiles(temporary.FullName, "*.restore"), Is.Empty);
        }
    }
}
