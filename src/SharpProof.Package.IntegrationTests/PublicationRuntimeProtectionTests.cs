using Microsoft.Build.Utilities;
using NUnit.Framework;
using SharpProof.BuildTasks;
using SharpProof.Host;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class PublicationRuntimeProtectionTests
{
    [TestCase("managed")]
    [TestCase("native")]
    [TestCase("staging")]
    public void CleanRejectsRuntimeCollisionsBeforeDeletingAnyPublication(string collision)
    {
        ArgumentNullException.ThrowIfNull(collision);
        using var fixture = new Fixture();
        var protectedPath = collision == "managed" ? fixture.Worker : fixture.Native;
        var task = new ResetPublishedVerification
        {
            BuildEngine = fixture.Engine,
            ProjectDirectory = fixture.Root,
            RequestPath = "request.json",
            ResultPath = collision == "staging" ? "result.json" : protectedPath,
            ManifestPath = "manifest.json",
            WorkerPath = fixture.Worker,
            CompilerManifestStagingPath = collision == "staging" ? protectedPath : null
        };

        Assert.That(task.Execute(), Is.False);
        Assert.That(File.ReadAllText(protectedPath), Is.EqualTo("runtime"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Root, "request.json")), Is.EqualTo("request"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Root, "result.json")), Is.EqualTo("result"));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void InvalidationProtectsManagedAndNativeCaptureAndResultPaths(bool capture, bool native)
    {
        using var fixture = new Fixture();
        var task = fixture.Invalidation();
        var protectedPath = native ? fixture.Native : fixture.Worker;
        if (capture)
        { task.InvocationManifestPath = protectedPath; }
        else
        { task.ResultPath = protectedPath; }

        Assert.That(task.Execute(), Is.False);
        Assert.That(File.ReadAllText(protectedPath), Is.EqualTo("runtime"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Root, "result.json")), Is.EqualTo("result"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingStandaloneWorkerDoesNotClaimItsParentDirectory(bool clean)
    {
        using var fixture = new Fixture();
        var worker = Path.Combine(fixture.Root, "missing-worker.dll");
        var invalidation = fixture.Invalidation();
        invalidation.WorkerPath = worker;
        var succeeded = clean
            ? new ResetPublishedVerification
            {
                BuildEngine = fixture.Engine,
                ProjectDirectory = fixture.Root,
                RequestPath = "request.json",
                ResultPath = "result.json",
                ManifestPath = "manifest.json",
                WorkerPath = worker
            }.Execute()
            : invalidation.Execute();

        Assert.That(succeeded, Is.True);
        Assert.That(fixture.Engine.Errors, Is.Empty);
        Assert.That(File.Exists(Path.Combine(fixture.Root, "result.json")), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingPackageWorkerStillProtectsNativePayloads(bool clean)
    {
        using var fixture = new Fixture();
        File.Delete(fixture.Worker);
        var invalidation = fixture.Invalidation();
        invalidation.InvocationManifestPath = fixture.Native;
        var succeeded = clean
            ? new ResetPublishedVerification
            {
                BuildEngine = fixture.Engine,
                ProjectDirectory = fixture.Root,
                RequestPath = "request.json",
                ResultPath = fixture.Native,
                ManifestPath = "manifest.json",
                WorkerPath = fixture.Worker
            }.Execute()
            : invalidation.Execute();

        Assert.That(succeeded, Is.False);
        Assert.That(fixture.Engine.Errors.Single().Message, Does.Contain("inside the worker runtime"));
        Assert.That(File.ReadAllText(fixture.Native), Is.EqualTo("runtime"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Root, "request.json")), Is.EqualTo("request"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Root, "result.json")), Is.EqualTo("result"));
    }

    [Test]
    public void ExistingStandaloneWorkerStillProtectsItsParentDirectory()
    {
        using var fixture = new Fixture();
        var worker = Path.Combine(fixture.Root, "standalone-worker.dll");
        File.WriteAllText(worker, "runtime");
        var task = fixture.Invalidation();
        task.WorkerPath = worker;

        Assert.That(task.Execute(), Is.False);
        Assert.That(fixture.Engine.Errors.Single().Message, Does.Contain("inside the worker runtime"));
        Assert.That(File.ReadAllText(worker), Is.EqualTo("runtime"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Root, "result.json")), Is.EqualTo("result"));
    }

    [TestCase("compiler")]
    [TestCase("source")]
    [TestCase("publication")]
    [TestCase("nonce")]
    [TestCase("receipt")]
    [TestCase("invocation")]
    public void CaptureManifestAliasesAreRejectedBeforeInvalidation(string collision)
    {
        ArgumentNullException.ThrowIfNull(collision);
        using var fixture = new Fixture();
        var task = fixture.Invalidation();
        task.CompilerCaptureRequestPath = "capture.request";
        task.CompilerCaptureNonce = Guid.NewGuid().ToString("N");
        task.InvocationRequestPath = "invocation-request.json";
        task.InvocationManifestPath = collision switch
        {
            "compiler" => "consumer.dll",
            "source" => "Subject.cs",
            "publication" => "manifest.json",
            "nonce" => "capture.request",
            "receipt" => "capture.request.completed",
            "invocation" => "invocation-request.json",
            _ => throw new ArgumentOutOfRangeException(nameof(collision))
        };
        task.CompilerOutputPaths = [new TaskItem("consumer.dll")];
        task.CompilerInputPaths = [new TaskItem("Subject.cs")];
        var protectedPath = Path.Combine(fixture.Root, task.InvocationManifestPath);
        File.WriteAllText(protectedPath, "protected");

        Assert.That(task.Execute(), Is.False);
        Assert.That(File.ReadAllText(protectedPath), Is.EqualTo("protected"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Root, "result.json")), Is.EqualTo("result"));
    }

    [TestCase("Subject.cs")]
    [TestCase("Dependency.dll")]
    [Platform("Linux")]
    public void CaptureManifestCannotReplaceTheTargetOfACompilerInputSymlink(string inputName)
    {
        ArgumentNullException.ThrowIfNull(inputName);
        using var fixture = new Fixture();
        var target = Path.Combine(fixture.Root, "capture.json");
        var input = Path.Combine(fixture.Root, inputName);
        var original = new byte[] { 0, 1, 2, 127, 255 };
        File.WriteAllBytes(target, original);
        File.CreateSymbolicLink(input, "capture.json");
        var task = fixture.Invalidation();
        task.CompilerInputPaths = [new TaskItem(inputName)];

        Assert.That(task.Execute(), Is.False);
        Assert.That(fixture.Engine.Errors.Single().Message,
            Is.EqualTo("SharpProof publication and capture paths must not alias compiler inputs."));
        Assert.That(File.ReadAllBytes(target), Is.EqualTo(original));
        Assert.That(File.ReadAllBytes(input), Is.EqualTo(original));
        Assert.That(new FileInfo(input).LinkTarget, Is.EqualTo("capture.json"));
        Assert.That(File.ReadAllText(Path.Combine(fixture.Root, "result.json")), Is.EqualTo("result"));
    }

    [Test]
    [Platform("Linux")]
    public void CaptureNonceReplacesLeafSymlinkWithoutChangingCompilerSource()
    {
        using var fixture = new Fixture();
        var source = Path.Combine(fixture.Root, "Subject.cs");
        var capture = Path.Combine(fixture.Root, "capture.request");
        File.WriteAllText(source, "public class Subject { }");
        File.CreateSymbolicLink(capture, source);
        var task = fixture.Invalidation();
        task.CompilerCaptureRequestPath = capture;
        task.CompilerCaptureNonce = Guid.NewGuid().ToString("N");
        task.CompilerInputPaths = [new TaskItem(source)];

        Assert.That(task.Execute(), Is.True);
        Assert.That(File.ReadAllText(source), Is.EqualTo("public class Subject { }"));
        Assert.That(File.ReadAllText(capture), Is.EqualTo(task.CompilerCaptureNonce));
        Assert.That(new FileInfo(capture).LinkTarget, Is.Null);
    }

    [Test]
    [Platform("Linux")]
    public void FilesystemInvalidationDoesNotRequireAVerifierHostContract()
    {
        using var fixture = new Fixture();
        var previousContainer = Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER");
        var previousContract = Environment.GetEnvironmentVariable("SHARPPROOF_CONTAINER_CONTRACT");
        try
        {
            Environment.SetEnvironmentVariable("SHARPPROOF_CONTAINER", "1");
            Environment.SetEnvironmentVariable("SHARPPROOF_CONTAINER_CONTRACT", Path.Combine(fixture.Root, "missing-contract.json"));
            Assert.That(fixture.Invalidation().Execute(), Is.True);
            Assert.That(File.Exists(Path.Combine(fixture.Root, "result.json")), Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SHARPPROOF_CONTAINER", previousContainer);
            Environment.SetEnvironmentVariable("SHARPPROOF_CONTAINER_CONTRACT", previousContract);
        }
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void CaseInsensitivePublicationPoliciesShareContainmentAndLeaseIdentity(bool windows, bool macOs)
    {
        var comparison = PublicationPaths.PathComparison(windows, macOs);
        var root = Path.Combine(Path.GetTempPath(), "SharpProofRuntime");
        var first = Path.Combine(root, "Report.json");
        var alias = first.ToUpperInvariant();

        Assert.That(PublicationPaths.IsWithin(alias, root, comparison), Is.True);
        Assert.That(PublicationLease.LockFileName(alias, comparison),
            Is.EqualTo(PublicationLease.LockFileName(first, comparison)));
        Assert.That(PublicationPaths.IsWithin(root + "-other", root, comparison), Is.False);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDirectory directory = new("publication-runtime-protection-");
        internal string Root => directory.FullName;
        internal string Worker { get; }
        internal string Native { get; }
        internal BuildTaskTests.RecordingBuildEngine Engine { get; } = new();

        internal Fixture()
        {
            Worker = Path.Combine(Root, "tools", "net10", "SharpProof.Worker.dll");
            Native = Path.Combine(Root, "tools", "native", "linux-x64", "libz3.so");
            Directory.CreateDirectory(Path.GetDirectoryName(Worker)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Native)!);
            File.WriteAllText(Worker, "runtime");
            File.WriteAllText(Native, "runtime");
            File.WriteAllText(Path.Combine(Root, "request.json"), "request");
            File.WriteAllText(Path.Combine(Root, "result.json"), "result");
        }

        internal InvalidatePublishedResult Invalidation()
        {
            return new()
            {
                BuildEngine = Engine,
                ProjectDirectory = Root,
                RequestPath = "request.json",
                ResultPath = "result.json",
                ManifestPath = "manifest.json",
                InvocationManifestPath = "capture.json",
                WorkerPath = Worker,
                LauncherPath = Worker,
                WorkerProtocolPath = Path.Combine(Root, "tools", "net10", "SharpProof.Worker.Protocol.dll")
            };
        }

        public void Dispose()
        {
            directory.Dispose();
        }
    }
}
