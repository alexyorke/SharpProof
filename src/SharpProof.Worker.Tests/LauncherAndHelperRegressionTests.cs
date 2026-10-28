using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class LauncherAndHelperRegressionTests
{
    [Test]
    public void MissingReferenceCompilationCannotResolveTheOfficialContractType()
    {
        const string source = "internal sealed class Subject { }";
        var absent = TestCompilation.Create("WithoutAttributes", source, includeSharpProofReference: false);
        var present = TestCompilation.Create("WithAttributes", source, includeSharpProofReference: true);
        Assert.That(absent.GetTypeByMetadataName("SharpProof.Attributes.Contract"), Is.Null);
        Assert.That(present.GetTypeByMetadataName("SharpProof.Attributes.Contract"), Is.Not.Null);
    }

    [Test]
    public void AlreadyCanceledProcessDoesNotAttemptToStartAnExecutable()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var start = new ProcessStartInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")))
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () =>
            await ProcessRunner.RunCapturedAsync(start, cancellation.Token)));
    }

    [Test]
    [NonParallelizable]
    public void CapturedConsoleHandlerIsHarmlessAfterScopeDisposal()
    {
        using var scope = new ConsoleCancellationScope(handleTermination: true, TestContext.CurrentContext.CancellationToken);
        var handler = (ConsoleCancelEventHandler)typeof(ConsoleCancellationScope)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(scope)!;
        var args = (ConsoleCancelEventArgs)Activator.CreateInstance(typeof(ConsoleCancelEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
            args: [ConsoleSpecialKey.ControlC], culture: CultureInfo.InvariantCulture)!;
        scope.Dispose();
        Assert.DoesNotThrow((Action)(() => handler(null, args)));
        Assert.That(args.Cancel, Is.True);
        Assert.DoesNotThrow((Action)scope.Dispose);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PrivateInputAliasesAreRejectedBeforeAnyFileIsWritten(bool requestAliasesInput)
    {
        TestRepository.RequireCanonicalContainer();
        using var directory = new TempDirectory("sharpproof-private-alias-");
        var real = Directory.CreateDirectory(Path.Combine(directory.FullName, "real")).FullName;
        var alias = Path.Combine(directory.FullName, "alias");
        Directory.CreateSymbolicLink(alias, real);
        var manifest = Path.Combine(real, "compiler.json");
        const string original = "original compiler input";
        await File.WriteAllTextAsync(manifest, original);
        var aliased = Path.Combine(alias, "compiler.json");
        var request = requestAliasesInput ? aliased : Path.Combine(directory.FullName, "request.json");
        var result = requestAliasesInput ? Path.Combine(directory.FullName, "result.json") : aliased;
        ImmutableArray<string> arguments = ["verify", "--request", request, "--result", result,
            "--compiler-manifest", manifest, "--verify-policy", "advisory", "--assumption-policy", "allow"];
        Assert.That(Launcher.LauncherArguments.TryParse(arguments, out var parsed), Is.True);
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            parsed.CreateRequest(out _, out _, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Does.Contain("I/O paths must be distinct"));
        Assert.That(await File.ReadAllTextAsync(manifest), Is.EqualTo(original));
        Assert.That(File.Exists(requestAliasesInput ? result : request), Is.False);
    }

    [TestCase("manifest")]
    [TestCase("request")]
    [TestCase("result")]
    public void CacheCannotContainAnyPrivateInputOrOutput(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        using var directory = new TempDirectory("sharpproof-cache-containment-");
        var cache = Directory.CreateDirectory(Path.Combine(directory.FullName, "cache")).FullName;
        var request = Path.Combine(member == "request" ? cache : directory.FullName, "request.json");
        var result = Path.Combine(member == "result" ? cache : directory.FullName, "result.json");
        var manifest = Path.Combine(member == "manifest" ? cache : directory.FullName, "manifest.sharp-proof-cache.json");
        Assert.That(Launcher.LauncherArguments.TryParse(["verify", "--request", request, "--result", result,
            "--compiler-manifest", manifest, "--verify-policy", "advisory", "--assumption-policy", "allow"], out var arguments), Is.True);
        Assert.Throws<ArgumentException>((Action)(() => arguments.ValidateDistinctPaths(cache)));
        Assert.DoesNotThrow((Action)(() => arguments.ValidateDistinctPaths(Path.Combine(directory.FullName, "separate-cache"))));
    }

    [Test]
    public async Task NullTimeoutErrorIsLeftForMalformedResponseValidation()
    {
        using var project = new ShadowTestProject(ShadowTestProject.IdentitySource);
        var inputHash = project.Bind().InputHash;
        var versions = Launcher.Program.ExpectedVersions();
        var response = WorkerResultAssembler.CreateIncomplete(inputHash,
            WorkerProtocolJson.ComputeRequestHash(project.Request), project.Artifact.Manifest, project.Request.Budgets,
            WorkerRunStatus.TimedOut, WorkerRunFailureReason.None,
            WorkerCallableCoverageReason.ProjectTimeout, WorkerClaimReason.ProjectTimeout,
            [], versions, 0);
        var json = JsonNode.Parse(WorkerProtocolJson.SerializeCanonicalResponse(response))!.AsObject();
        json["callableResults"] = new JsonArray();
        json["claimResults"] = new JsonArray();
        json["errors"] = new JsonArray((JsonNode?)null);
        var text = json.ToJsonString();
        var path = Path.Combine(project.Artifact.Compilation.ProjectDirectory, "null-timeout.json");
        await File.WriteAllTextAsync(path, text);
        await Launcher.Program.PromotePreManifestProjectTimeoutAsync(path, project.Request,
            project.Artifact, inputHash, versions, TestContext.CurrentContext.CancellationToken);
        Assert.That(await File.ReadAllTextAsync(path), Is.EqualTo(text));
        Assert.That(WorkerProtocolJson.Validate(WorkerProtocolJson.DeserializeResponse(text)!).IsValid, Is.False);
    }
}
