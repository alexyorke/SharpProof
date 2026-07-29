using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerBinaryIdentityTests
{
    [Test]
    public void IdentityCoversTheCompleteTrustedRuntimeClosure()
    {
        Assert.That(
            WorkerBinaryIdentity.ComputeSha256(
                typeof(SharpProofWorker).Assembly.Location),
            Is.EqualTo(WorkerCacheIdentity.Current.WorkerBinarySha256));

        var sourceDirectory = Path.GetDirectoryName(
            typeof(SharpProofWorker).Assembly.Location)!;
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.WorkerBinaryIdentity." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            foreach (var source in Directory.GetFiles(
                         sourceDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                var name = Path.GetRelativePath(sourceDirectory, source);
                if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                    name is
                        "SharpProof.Worker.deps.json" or
                        "SharpProof.Worker.runtimeconfig.json")
                {
                    var destination = Path.Combine(temporaryDirectory, name);
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(destination)!);
                    File.Copy(
                        source,
                        destination);
                }
            }

            var worker = Path.Combine(
                temporaryDirectory,
                "SharpProof.Worker.dll");
            var baseline = WorkerBinaryIdentity.ComputeSha256(worker);
            File.AppendAllText(
                Path.Combine(temporaryDirectory, "SharpProof.Smt.dll"),
                "mutated");
            var dependencyChanged =
                WorkerBinaryIdentity.ComputeSha256(worker);
            Assert.That(dependencyChanged, Is.Not.EqualTo(baseline));

            File.WriteAllText(
                Path.Combine(temporaryDirectory, "unrelated.dll"),
                "ignored");
            Assert.That(
                WorkerBinaryIdentity.ComputeSha256(worker),
                Is.EqualTo(dependencyChanged));

            var nativeZ3 = Path.Combine(
                temporaryDirectory,
                "runtimes",
                "win-x64",
                "native",
                "libz3.dll");
            Assert.That(File.Exists(nativeZ3), Is.True);
            File.AppendAllText(nativeZ3, "mutated");
            var nativeChanged = WorkerBinaryIdentity.ComputeSha256(worker);
            Assert.That(nativeChanged, Is.Not.EqualTo(dependencyChanged));

            File.Delete(
                Path.Combine(temporaryDirectory, "SharpProof.Verify.dll"));
            Action missingDependency =
                () => _ = WorkerBinaryIdentity.ComputeSha256(worker);
            Assert.Throws<FileNotFoundException>(missingDependency);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
