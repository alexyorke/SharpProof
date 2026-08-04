using System.Collections.Immutable;
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
    public void MalformedRuntimeDependencyManifestsFailClosed()
    {
        var sourceWorker = typeof(SharpProofWorker).Assembly.Location;
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.MalformedWorkerDeps." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var worker = Path.Combine(temporaryDirectory, "SharpProof.Worker.dll");
            var dependency = Path.ChangeExtension(worker, ".deps.json");
            File.Copy(sourceWorker, worker);
            foreach (var json in new[] {
                         "{}",
                         "{\"runtimeTarget\":1,\"targets\":{}}",
                         "{\"runtimeTarget\":{\"name\":\"missing\"},\"targets\":{}}",
                         "{\"runtimeTarget\":{\"name\":\"app\"},\"targets\":{\"app\":{\"lib\":{\"runtimeTargets\":{\"asset.dll\":{}}}}}}"
                         ,
                         "{\"runtimes/win/../outside.dll\":{}}"
                     })
            {
                File.WriteAllText(dependency, json);
                Exception? exception = null;
                try
                {
                    using var snapshot = WorkerBinaryIdentity.CreateSnapshot(worker);
                }
                catch (InvalidDataException observed)
                {
                    exception = observed;
                }
                catch (KeyNotFoundException observed)
                {
                    exception = observed;
                }
                catch (InvalidOperationException observed)
                {
                    exception = observed;
                }
                catch (FileNotFoundException observed)
                {
                    exception = observed;
                }

                Assert.That(
                    exception?.GetType(),
                    Is.AnyOf(
                        typeof(InvalidDataException),
                        typeof(KeyNotFoundException),
                        typeof(InvalidOperationException),
                        typeof(FileNotFoundException)));
            }
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Test]
    public void RuntimeClosureComponentPathsAreImmutable()
    {
        using var snapshot = WorkerBinaryIdentity.CreateSnapshot(
            typeof(SharpProofWorker).Assembly.Location);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                snapshot.ComponentPaths.GetType(),
                Is.EqualTo(typeof(ImmutableArray<string>)));
            Assert.That(
                snapshot.ExecutionWorkerPath,
                Is.Not.EqualTo(snapshot.WorkerPath));
        }

        if (OperatingSystem.IsWindows())
        {
            Assert.That(
                (Action)(() => File.Delete(snapshot.ExecutionWorkerPath)),
                Throws.TypeOf<IOException>());
        }
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
            var appLocalAsset = Path.Combine(
                temporaryDirectory,
                "System.Collections.Immutable.dll");
            File.Copy(
                typeof(ImmutableArray<>).Assembly.Location,
                appLocalAsset,
                overwrite: true);
            var baseline = WorkerBinaryIdentity.ComputeSha256(worker);
            var nestedAppLocalAsset = Path.Combine(
                temporaryDirectory,
                "runtimes",
                "win",
                "lib",
                "net9.0",
                "System.Collections.Immutable.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(nestedAppLocalAsset)!);
            File.Copy(
                appLocalAsset,
                nestedAppLocalAsset,
                overwrite: true);
            Assert.That(
                WorkerBinaryIdentity.ComputeSha256(worker),
                Is.EqualTo(baseline));
            var heldComponent = Path.Combine(
                temporaryDirectory,
                "SharpProof.Verify.dll");
            var nativeZ3 = Path.Combine(
                temporaryDirectory,
                "runtimes",
                "win-x64",
                "native",
                "libz3.dll");
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
            string stagedWorker;
            using (var snapshot = WorkerBinaryIdentity.CreateSnapshot(worker))
            {
                stagedWorker = snapshot.ExecutionWorkerPath;
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(snapshot.WorkerPath, Is.EqualTo(worker));
                    Assert.That(snapshot.ExecutionWorkerPath, Is.Not.EqualTo(worker));
                    Assert.That(File.Exists(snapshot.ExecutionWorkerPath), Is.True);
                    Assert.That(snapshot.Sha256, Is.EqualTo(baseline));
                    Assert.That(
                        new HashSet<string>(
                            snapshot.ComponentPaths,
                            StringComparer.OrdinalIgnoreCase).Count,
                        Is.EqualTo(snapshot.ComponentPaths.Count));
                    Assert.That(
                        snapshot.ComponentPaths,
                        Does.Contain(worker));
                    Assert.That(
                        snapshot.ComponentPaths,
                        Does.Contain(appLocalAsset));
                    Assert.That(
                        snapshot.ComponentPaths,
                        Does.Not.Contain(Path.Combine(
                            temporaryDirectory, "libz3.dll")));
                    Assert.That(
                        snapshot.ComponentPaths,
                        Does.Not.Contain(Path.Combine(
                            temporaryDirectory,
                            "runtimes", "browser", "lib", "net8.0",
                            "System.Text.Encodings.Web.dll")));
                    Assert.That(
                        snapshot.ComponentPaths,
                        Does.Contain(Path.Combine(
                            temporaryDirectory,
                            "runtimes", "win-x64", "native", "libz3.dll")));
                    Assert.That(
                        snapshot.ComponentPaths,
                        Does.Contain(Path.Combine(
                            temporaryDirectory,
                            "runtimes", "win", "lib", "net9.0",
                            "System.Text.Encodings.Web.dll")));

                    foreach (var componentPath in snapshot.ComponentPaths)
                    {
                        var relativePath = Path.GetRelativePath(
                            Path.GetDirectoryName(worker)!,
                            componentPath);
                        var stagedPath = Path.Combine(
                            Path.GetDirectoryName(snapshot.ExecutionWorkerPath)!,
                            relativePath);
                        Assert.That(
                            File.Exists(stagedPath),
                            Is.True,
                            relativePath);
                        Assert.That(
                            File.ReadAllBytes(stagedPath),
                            Is.EqualTo(File.ReadAllBytes(componentPath)),
                            relativePath);
                    }

                    Assert.That(
                        WorkerBinaryIdentity.ComputeSha256(
                            snapshot.ExecutionWorkerPath),
                        Is.EqualTo(snapshot.Sha256));
                }

                var stagedBytes = File.ReadAllBytes(snapshot.ExecutionWorkerPath);
                var stagedHeldComponent = Path.Combine(
                    Path.GetDirectoryName(snapshot.ExecutionWorkerPath)!,
                    Path.GetRelativePath(
                        Path.GetDirectoryName(worker)!,
                        heldComponent));
                var stagedHeldBytes = File.ReadAllBytes(stagedHeldComponent);
                File.AppendAllText(heldComponent, "source-mutated");
                Assert.That(File.ReadAllBytes(snapshot.ExecutionWorkerPath),
                    Is.EqualTo(stagedBytes));
                Assert.That(
                    File.ReadAllBytes(stagedHeldComponent),
                    Is.EqualTo(stagedHeldBytes));
            }
            Assert.That(File.Exists(stagedWorker), Is.False);
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
