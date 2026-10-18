using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusTransactionWorkflowTests
{
    private static readonly ImmutableArray<string> ExpectedFiles = ["snapshot.txt"];

    [Test]
    public async Task EmptyUpdateLeavesExistingCorpusFilesUnchanged()
    {
        using var temporary = new TempDirectory("SharpProof-corpus-empty-update-");
        var path = Path.Combine(temporary.FullName, "snapshot.txt");
        await File.WriteAllTextAsync(path, "reviewed snapshot\n");
        await CorpusFileTransaction.WriteAllAsync(temporary.FullName, [], TestContext.CurrentContext.CancellationToken);
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo("reviewed snapshot\n"));
        Assert.That(Directory.EnumerateFiles(temporary.FullName).Select(Path.GetFileName),
            Is.EquivalentTo(ExpectedFiles));
    }

    [Test]
    public void DuplicateDestinationsRejectTheBatchBeforeAnyOutputChanges()
    {
        using var temporary = new TempDirectory("SharpProof-corpus-duplicate-update-");
        var path = Path.Combine(temporary.FullName, "snapshot.txt");
        File.WriteAllText(path, "reviewed snapshot\n");
        var error = Assert.ThrowsAsync<ArgumentException>((Func<Task>)(async () =>
            await CorpusFileTransaction.WriteAllAsync(temporary.FullName,
                [new CorpusFileUpdate(path, "first\n"), new CorpusFileUpdate(path, "second\n")], TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.ParamName, Is.EqualTo("updates"));
        Assert.That(error.Message, Does.StartWith("Corpus transaction destinations must be unique."));
        Assert.That(File.ReadAllText(path), Is.EqualTo("reviewed snapshot\n"));
        Assert.That(Directory.EnumerateFiles(temporary.FullName).Select(Path.GetFileName),
            Is.EquivalentTo(ExpectedFiles));
    }

    [Test]
    public void CanceledUpdateLeavesExistingCorpusFilesUnchanged()
    {
        using var temporary = new TempDirectory("SharpProof-corpus-canceled-update-");
        var path = Path.Combine(temporary.FullName, "snapshot.txt");
        File.WriteAllText(path, "reviewed snapshot\n");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>((Func<Task>)(async () =>
            await CorpusFileTransaction.WriteAllAsync(temporary.FullName,
                [new CorpusFileUpdate(path, "replacement\n")], cancellation.Token)));
        Assert.That(File.ReadAllText(path), Is.EqualTo("reviewed snapshot\n"));
        Assert.That(Directory.EnumerateFiles(temporary.FullName).Select(Path.GetFileName),
            Is.EquivalentTo(ExpectedFiles));
    }

    [TestCase("null", "The corpus transaction marker is empty.")]
    [TestCase("{\"SchemaVersion\":2,\"Entries\":[]}", "The corpus transaction marker is invalid.")]
    [TestCase("{\"SchemaVersion\":1,\"Entries\":[]}", "The corpus transaction marker is invalid.")]
    public void IncompleteTransactionMetadataFailsRecoveryBeforeChangingOutput(string marker, string message)
    {
        System.ArgumentNullException.ThrowIfNull(marker);
        System.ArgumentNullException.ThrowIfNull(message);
        using var temporary = new TempDirectory("SharpProof-corpus-recovery-metadata-");
        var path = Path.Combine(temporary.FullName, "snapshot.txt");
        File.WriteAllText(path, "reviewed snapshot\n");
        var markerPath = Path.Combine(temporary.FullName, ".sharpproof-corpus-transaction.json");
        File.WriteAllText(markerPath, marker);
        var error = Assert.Throws<InvalidDataException>((Action)(() => CorpusFileTransaction.Recover(temporary.FullName)));
        Assert.That(error!.Message, Is.EqualTo(message));
        Assert.That(File.ReadAllText(path), Is.EqualTo("reviewed snapshot\n"));
        Assert.That(File.ReadAllText(markerPath), Is.EqualTo(marker));
    }
}
