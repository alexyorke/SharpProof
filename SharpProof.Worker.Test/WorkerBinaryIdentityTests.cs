using System.Diagnostics;
using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerBinaryIdentityTests
{
    [Test]
    public void StartupReclaimsDeadRuntimeOwnersButPreservesLiveAndUnownedDirectories()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.RuntimeReclaim." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var dead = Path.Combine(
                temporaryDirectory,
                "SharpProof.Worker.Runtime.dead");
            var live = Path.Combine(
                temporaryDirectory,
                "SharpProof.Worker.Runtime.live");
            var unowned = Path.Combine(
                temporaryDirectory,
                "SharpProof.Worker.Runtime.unowned");
            foreach (var directory in new[] { dead, live, unowned })
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, "marker"), "marker");
            }

            var currentProcess = Process.GetCurrentProcess();
            var liveTicks = currentProcess.StartTime.ToUniversalTime().Ticks;
            File.WriteAllText(
                Path.Combine(dead, WorkerBinaryIdentity.OwnerLeaseFileName),
                "{\"pid\":" + currentProcess.Id +
                ",\"startTimeUtcTicks\":" + (liveTicks + 1) + "}");
            File.WriteAllText(
                Path.Combine(live, WorkerBinaryIdentity.OwnerLeaseFileName),
                "{\"pid\":" + currentProcess.Id +
                ",\"startTimeUtcTicks\":" + liveTicks + "}");
            Directory.SetLastWriteTimeUtc(
                dead,
                DateTime.UtcNow.AddMinutes(-2));

            WorkerBinaryIdentity.ReclaimOrphanedStagingDirectories(
                temporaryDirectory);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Directory.Exists(dead), Is.False);
                Assert.That(Directory.Exists(live), Is.True);
                Assert.That(Directory.Exists(unowned), Is.True);
            }
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void StartupDoesNotReclaimARecentDeadOwnerDirectory()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.RuntimeReclaimRecent." +
                Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var recent = Path.Combine(
                temporaryDirectory,
                "SharpProof.Worker.Runtime.recent");
            Directory.CreateDirectory(recent);
            File.WriteAllText(
                Path.Combine(recent, WorkerBinaryIdentity.OwnerLeaseFileName),
                "{\"pid\":1,\"startTimeUtcTicks\":1}");

            WorkerBinaryIdentity.ReclaimOrphanedStagingDirectories(
                temporaryDirectory);

            Assert.That(Directory.Exists(recent), Is.True);
        }
        finally
        {
            if (Directory.Exists(temporaryDirectory))
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Test]
    public void StagedComponentConsistencyIsFailClosed()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.StagedComponent." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var source = Path.Combine(temporaryDirectory, "source.dll");
            var staged = Path.Combine(temporaryDirectory, "staged.dll");
            File.WriteAllBytes(source, [1, 2]);
            File.WriteAllBytes(staged, [1, 3]);
            Assert.That(
                (Action)(() => WorkerBinaryIdentity.EnsureStagedComponentConsistency(
                    source, staged)),
                Throws.TypeOf<InvalidDataException>());
            File.WriteAllBytes(staged, [1, 2]);
            Assert.DoesNotThrow((Action)(() =>
                WorkerBinaryIdentity.EnsureStagedComponentConsistency(
                    source, staged)));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Test]
    public void RuntimeComponentReadsRetainTheDeclaredSizeBoundary()
    {
        const int aboveManifestLimit = 16 * 1024 * 1024 + 1;
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.RuntimeComponentLimit." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var path = Path.Combine(temporaryDirectory, "runtime.dll");
            using (var stream = File.Create(path))
            {
                stream.SetLength(aboveManifestLimit);
            }

            Assert.That(
                (Action)(() => CompilerManifestArtifactFile.ReadAllBytes(path)),
                Throws.TypeOf<InvalidDataException>());
            var bytes = CompilerManifestArtifactFile.ReadAllBytes(
                path,
                WorkerBinaryIdentity.MaximumComponentBytes);
            Assert.That(bytes, Has.Length.EqualTo(aboveManifestLimit));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Test]
    public void CompilerManifestReaderRejectsEmptyOpenedFile()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.EmptyManifest." + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(path, []);
            Assert.That(
                (Action)(() => CompilerManifestArtifactFile.ReadAllBytes(path)),
                Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            File.Delete(path);
        }
    }

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
#pragma warning disable CA2000 // The alias mutant is deliberately not disposed through an unowned source path.
        var snapshot = WorkerBinaryIdentity.CreateSnapshot(
            typeof(SharpProofWorker).Assembly.Location);
#pragma warning restore CA2000
        try
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    snapshot.ComponentPaths.GetType(),
                    Is.EqualTo(typeof(ImmutableArray<string>)));
                Assert.That(
                    snapshot.ExecutionWorkerPath,
                    Is.Not.EqualTo(snapshot.WorkerPath));
            }
        }
        finally
        {
            // Disposal removes the directory containing ExecutionWorkerPath.
            // Do not let the deliberate alias mutation erase the test output.
            if (!string.Equals(
                    snapshot.ExecutionWorkerPath,
                    snapshot.WorkerPath,
                    StringComparison.Ordinal))
            {
                snapshot.Dispose();
            }
        }
    }

    [Test]
    [Platform("Linux")]
    public void IdentityDistinguishesLinuxComponentNameCase()
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.ComponentCase." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            static string WriteClosure(string directory, string workerName)
            {
                Directory.CreateDirectory(directory);
                var worker = Path.Combine(directory, workerName + ".dll");
                File.WriteAllBytes(worker, [1, 2, 3]);
                File.WriteAllText(
                    Path.Combine(directory, workerName + ".deps.json"),
                    "{}");
                File.WriteAllText(
                    Path.Combine(directory, workerName + ".runtimeconfig.json"),
                    "{}");
                return worker;
            }

            var upper = WriteClosure(
                Path.Combine(temporaryDirectory, "upper"),
                "Worker");
            var lower = WriteClosure(
                Path.Combine(temporaryDirectory, "lower"),
                "worker");

            Assert.That(
                WorkerBinaryIdentity.ComputeSha256(lower),
                Is.Not.EqualTo(WorkerBinaryIdentity.ComputeSha256(upper)));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
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
            var dependencyPath = Path.ChangeExtension(worker, ".deps.json");
            var dependencyText = File.ReadAllText(dependencyPath).Replace(
                "runtimes/browser/lib/net8.0/System.Text.Encodings.Web.dll",
                "runtimes/browser/lib/net8.0/OnlyBrowser.dll",
                StringComparison.Ordinal);
            File.WriteAllText(dependencyPath, dependencyText);
            var unsupportedRidLeaf = Path.Combine(
                temporaryDirectory,
                "OnlyBrowser.dll");
            File.Copy(
                typeof(System.Text.Encodings.Web.HtmlEncoder).Assembly.Location,
                unsupportedRidLeaf,
                overwrite: true);
            var unsupportedRidPath = Path.Combine(
                temporaryDirectory,
                "runtimes",
                "browser",
                "lib",
                "net8.0",
                "OnlyBrowser.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(unsupportedRidPath)!);
            File.Copy(
                unsupportedRidLeaf,
                unsupportedRidPath,
                overwrite: true);
            var baseline = WorkerBinaryIdentity.ComputeSha256(worker);
            var nestedAppLocalAsset = Path.Combine(
                temporaryDirectory,
                "runtimes",
                "linux",
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
                "linux-x64",
                "native",
                "libz3.so");
            Directory.CreateDirectory(Path.GetDirectoryName(nativeZ3)!);
            File.WriteAllText(nativeZ3, "not-the-container-owned-native-library");
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
                        Does.Not.Contain(unsupportedRidLeaf));
                    Assert.That(
                        snapshot.ComponentPaths,
                        Does.Not.Contain(Path.Combine(
                            temporaryDirectory,
                            "runtimes", "browser", "lib", "net8.0",
                            "OnlyBrowser.dll")));
                    Assert.That(
                        snapshot.ComponentPaths,
                        Does.Not.Contain(Path.Combine(
                            temporaryDirectory,
                            "runtimes", "linux-x64", "native", "libz3.so")));
                    Assert.That(
                        snapshot.ComponentPaths,
                        Does.Not.Contain(Path.Combine(
                            temporaryDirectory,
                            "runtimes", "linux", "lib", "net9.0",
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
            var beforeNativeMutation =
                WorkerBinaryIdentity.ComputeSha256(worker);
            File.AppendAllText(nativeZ3, "mutated");
            var nativeChanged = WorkerBinaryIdentity.ComputeSha256(worker);
            Assert.That(nativeChanged, Is.EqualTo(beforeNativeMutation));

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
