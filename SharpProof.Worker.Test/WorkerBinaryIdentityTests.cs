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
    public void CompilerManifestReaderRejectsEmptyOpenedFile()
    {
        using var temporaryWorkspace = new TempDirectory(
            "SharpProof.EmptyManifest.");
        var path = Path.Combine(temporaryWorkspace.FullName, "manifest.json");
        File.WriteAllBytes(path, []);
        Assert.That(
            (Action)(() => CompilerManifestArtifactFile.ReadAllBytes(path)),
            Throws.TypeOf<InvalidDataException>());
    }

}
