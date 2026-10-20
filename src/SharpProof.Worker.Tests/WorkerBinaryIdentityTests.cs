using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerBinaryIdentityTests
{
    [Test]
    public void WorkerIdentityIncludesThePinnedNativeLibraryDigest()
    {
        var worker = typeof(SharpProofWorker).Assembly.Location;
        var pinnedZ3 = ContainerContract.GetZ3LibrarySha256Required();
        var withoutZ3 = WorkerBinaryIdentity.ComputeSha256(worker);
        var withZ3 = WorkerBinaryIdentity.ComputeSha256(worker, pinnedZ3);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withZ3, Is.Not.EqualTo(withoutZ3));
            Assert.That(
                WorkerBinaryIdentity.ComputeSha256(worker, new string('0', 64)),
                Is.Not.EqualTo(withZ3));
        }
    }

    [Test]
    public void WorkerIdentityRejectsMissingOrNonDllBinaries()
    {
        using var temporaryWorkspace = new TempDirectory(
            "SharpProof.InvalidWorkerBinary.");
        var missing = Path.Combine(temporaryWorkspace.FullName, "missing.dll");
        var wrongExtension = Path.Combine(temporaryWorkspace.FullName, "worker.exe");
        File.WriteAllBytes(wrongExtension, [1, 2, 3]);

        foreach (var path in ImmutableArray.Create(missing, wrongExtension))
        {
            var failure = Assert.Throws<FileNotFoundException>((Action)(() =>
                WorkerBinaryIdentity.ComputeSha256(path)));
            Assert.That(failure!.FileName, Is.EqualTo(Path.GetFullPath(path)));
        }
    }

    [Test]
    public void CompilerManifestReaderRejectsEmptyOpenedFile()
    {
        using var temporaryWorkspace = new TempDirectory(
            "SharpProof.EmptyManifest.");
        var path = Path.Combine(temporaryWorkspace.FullName, "manifest.json");
        File.WriteAllBytes(path, []);
        Assert.That(
            (Action)(() => CompilerManifestArtifactFile.ReadAllBytes(path, CompilerManifestArtifactFile.MaximumBytes, TestContext.CurrentContext.CancellationToken)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void CompilerManifestReaderRejectsChangedLengths()
    {
        using var temporaryWorkspace = new TempDirectory(
            "SharpProof.ChangedManifest.");
        var path = Path.Combine(temporaryWorkspace.FullName, "manifest.json");
        File.WriteAllBytes(path, [1, 2, 3]);
        const string changedMessage = "The compiler manifest changed while it was read.";

        using (var exact = File.OpenRead(path))
        {
            Assert.That(CompilerManifestArtifactFile.ReadExact(
                exact, 3, changedMessage, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.EqualTo(ImmutableArray.Create<byte>(1, 2, 3)));
        }
        using (var shortened = File.OpenRead(path))
        {
            var failure = Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactFile.ReadExact(
                    shortened, 4, changedMessage, cancellationToken: TestContext.CurrentContext.CancellationToken)));
            Assert.That(failure!.Message, Is.EqualTo(changedMessage));
        }
        using (var lengthened = File.OpenRead(path))
        {
            var failure = Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactFile.ReadExact(
                    lengthened, 2, changedMessage, cancellationToken: TestContext.CurrentContext.CancellationToken)));
            Assert.That(failure!.Message, Is.EqualTo(changedMessage));
        }
    }

    [TestCase(1)]
    [TestCase(5)]
    public void CompilerManifestReaderRejectsChangedDirectoryEntryLength(int replacementLength)
    {
        using var temporaryWorkspace = new TempDirectory("SharpProof.ManifestSnapshot.");
        var path = Path.Combine(temporaryWorkspace.FullName, "manifest.json");
        File.WriteAllBytes(path, [1, 2, 3]);
        var observed = new FileInfo(path);
        observed.Refresh();
        Assert.That(observed.Length, Is.EqualTo(3));
        File.WriteAllBytes(path, new byte[replacementLength]);

        var failure = Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactFile.ReadAllBytes(observed, CompilerManifestArtifactFile.MaximumBytes, TestContext.CurrentContext.CancellationToken)));

        Assert.That(failure!.Message,
            Is.EqualTo("The compiler manifest changed while it was opened."));
        using var reopened = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.That(reopened.Length, Is.EqualTo(replacementLength));
    }

    [Test]
    public void CompilerManifestReaderRetainsUnchangedDirectoryEntry()
    {
        using var temporaryWorkspace = new TempDirectory("SharpProof.ManifestSnapshot.");
        var path = Path.Combine(temporaryWorkspace.FullName, "manifest.json");
        File.WriteAllBytes(path, [1, 2, 3]);
        var observed = new FileInfo(path);
        observed.Refresh();

        Assert.That(CompilerManifestArtifactFile.ReadAllBytes(observed, CompilerManifestArtifactFile.MaximumBytes, TestContext.CurrentContext.CancellationToken),
            Is.EqualTo(ImmutableArray.Create<byte>(1, 2, 3)));
    }

    [Test]
    public void ExactZeroLengthReadReturnsInitializedEmptyBytes()
    {
        using var temporaryWorkspace = new TempDirectory("SharpProof.ManifestEmpty.");
        var path = Path.Combine(temporaryWorkspace.FullName, "manifest.json");
        File.WriteAllBytes(path, []);
        using var stream = File.OpenRead(path);
        var bytes = CompilerManifestArtifactFile.ReadExact(stream, 0, "changed",
            TestContext.CurrentContext.CancellationToken);
        Assert.That(bytes.IsDefault, Is.False);
        Assert.That(bytes.IsEmpty, Is.True);
        Assert.That(stream.Position, Is.Zero);
    }
}
