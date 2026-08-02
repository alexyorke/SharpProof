using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerBinaryIdentityTests
{
    [Test]
    public void RuntimeClosureLimitsFailClosedAtEveryBoundary()
    {
        const long expectedMaximumComponentBytes = 32L * 1024 * 1024;
        long total = 0;
        WorkerBinaryIdentity.ValidateComponentCount(
            WorkerBinaryIdentity.MaximumRuntimeComponents);
        WorkerBinaryIdentity.ValidateComponentLength(
            "worker",
            WorkerBinaryIdentity.MaximumComponentBytes,
            ref total);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WorkerBinaryIdentity.MaximumComponentBytes,
                Is.EqualTo(expectedMaximumComponentBytes));
            Assert.That(
                (Action)(() => WorkerBinaryIdentity.ValidateComponentCount(
                    WorkerBinaryIdentity.MaximumRuntimeComponents + 1)),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                (Action)(() => ValidateLength(
                    "dependencies",
                    WorkerBinaryIdentity.MaximumDependenciesBytes + 1)),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                (Action)(() => ValidateLength(
                    "runtimeConfig",
                    WorkerBinaryIdentity.MaximumRuntimeConfigBytes + 1)),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                (Action)(() => ValidateLength(
                    "runtime/large.dll",
                    expectedMaximumComponentBytes + 1)),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                (Action)(() => ValidateLength(new('a',
                    WorkerBinaryIdentity.MaximumComponentKeyCharacters + 1),
                    0)),
                Throws.TypeOf<InvalidDataException>());
        }

        total = WorkerBinaryIdentity.MaximumClosureBytes;
        Assert.That(
            (Action)(() => WorkerBinaryIdentity.ValidateComponentLength(
                "runtime/extra.dll", 1, ref total)),
            Throws.TypeOf<InvalidDataException>());
    }

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
            var dependency = Path.Combine(
                temporaryDirectory,
                "SharpProof.Worker.deps.json");
            var runtimeConfig = Path.Combine(
                temporaryDirectory,
                "SharpProof.Worker.runtimeconfig.json");
            var baseline = WorkerBinaryIdentity.ComputeSha256(worker);
            var heldComponent = Path.Combine(
                temporaryDirectory,
                "SharpProof.Verify.dll");
            var appLocalAsset = Path.Combine(
                temporaryDirectory,
                "System.Collections.Immutable.dll");
            var nativeZ3 = Path.Combine(
                temporaryDirectory,
                "runtimes",
                "win-x64",
                "native",
                "libz3.dll");
            var lockedComponents = new[] {
                worker, dependency, runtimeConfig, heldComponent,
                Path.Combine(temporaryDirectory, "SharpProof.Smt.dll"),
                appLocalAsset, nativeZ3
            };
            using (var oversized = new FileStream(
                       heldComponent,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.Read))
            {
                oversized.SetLength(WorkerBinaryIdentity.MaximumComponentBytes + 1);
            }
            Assert.That(
                (Action)(() => WorkerBinaryIdentity.CreateSnapshot(worker)),
                Throws.TypeOf<InvalidDataException>());
            File.Copy(
                Path.Combine(sourceDirectory, "SharpProof.Verify.dll"),
                heldComponent,
                overwrite: true);
            using (var snapshot = WorkerBinaryIdentity.CreateSnapshot(worker))
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(snapshot.WorkerPath, Is.EqualTo(worker));
                    Assert.That(snapshot.Sha256, Is.EqualTo(baseline));
                    foreach (var component in lockedComponents)
                    {
                        Assert.That(
                            (Action)(() => File.AppendAllText(component, "blocked")),
                            Throws.InstanceOf<IOException>());
                    }
                }
            }
            File.AppendAllText(heldComponent, "released");
            Assert.That(
                WorkerBinaryIdentity.ComputeSha256(worker),
                Is.Not.EqualTo(baseline));
            File.AppendAllText(
                Path.Combine(temporaryDirectory, "SharpProof.Smt.dll"),
                "mutated");
            var dependencyChanged =
                WorkerBinaryIdentity.ComputeSha256(worker);
            Assert.That(dependencyChanged, Is.Not.EqualTo(baseline));

            File.AppendAllText(appLocalAsset, "mutated");
            var appLocalChanged =
                WorkerBinaryIdentity.ComputeSha256(worker);
            Assert.That(appLocalChanged, Is.Not.EqualTo(dependencyChanged));

            File.WriteAllText(
                Path.Combine(temporaryDirectory, "unrelated.dll"),
                "ignored");
            Assert.That(
                WorkerBinaryIdentity.ComputeSha256(worker),
                Is.EqualTo(appLocalChanged));

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

    private static void ValidateLength(string key, long length)
    {
        long total = 0;
        WorkerBinaryIdentity.ValidateComponentLength(key, length, ref total);
    }
}
