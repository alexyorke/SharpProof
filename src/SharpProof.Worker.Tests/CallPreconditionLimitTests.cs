using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Callers selected only for their calls must not stop a build on their own:
// past a size cap they leave the artifact, and a call claim that runs out of
// time does not fail require-proven. Either way the call counts as unchecked.
// Not parallel: the timing test measures wall-clock budgets.
[TestFixture]
[NonParallelizable]
public sealed class CallPreconditionLimitTests
{
    // Each group has one declared claim and two callers selected only for
    // their calls, one of them with a loop.
    private static string Groups(int groups)
    {
        var text = new StringBuilder("using SharpProof.Attributes;\npublic static class Callee { public static int F(int x) { Contract.Requires(x > 0); return x; } }\n");
        for (var group = 0; group < groups; group++)
        {
            text.Append("public static class Group").Append(group.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" {\n")
                .Append("    public static int Declared(int x) { Contract.Ensures(Contract.Result<int>() == x); return x; }\n")
                .Append("    public static int Simple(int y) { return Callee.F(y > 0 ? y : 1); }\n")
                .Append("    public static int Looping(int n) { var t = 0; for (var i = 0; i < n; i++) { t = Callee.F(i + 1); } return t; }\n")
                .Append("}\n");
        }
        return text.ToString();
    }

    private static CompilerManifestArtifact Artifact(string source, long callerSelectionBytes)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.CreateExact("CallerLimits", [("Subject.cs", source)]);
        TestCompilation.AssertNoErrors(compilation);
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken, callerSelectionBytes: callerSelectionBytes);
    }

    private static int Count(CompilerManifestArtifact artifact, WorkerClaimKind kind)
    {
        System.ArgumentNullException.ThrowIfNull(artifact);
        return artifact.Manifest.Claims.Count(claim => claim.Kind == kind);
    }

    [Test]
    public void CallersPastTheCapAreCountedInsteadOfFailingTheBuild()
    {
        const int groups = 600;
        var source = Groups(groups);
        var uncapped = Artifact(source, long.MaxValue);
        var artifact = Artifact(source, CompilerManifestArtifactFile.CallerSelectionBytes);
        var bytes = Encoding.UTF8.GetByteCount(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken));
        var calls = Count(artifact, WorkerClaimKind.CallPrecondition);
        using (Assert.EnterMultipleScope())
        {
            // Without the cap this project fails with SP0049.
            Assert.That(JsonSerializer.SerializeToUtf8Bytes(uncapped, WorkerProtocolJson.SharedOptions).Length,
                Is.GreaterThan(CompilerManifestArtifactFile.MaximumBytes));
            Assert.That(Count(uncapped, WorkerClaimKind.CallPrecondition), Is.EqualTo(2 * groups));
            Assert.That(bytes, Is.LessThanOrEqualTo(CompilerManifestArtifactFile.CallerSelectionBytes));
            Assert.That(Count(artifact, WorkerClaimKind.Postcondition), Is.EqualTo(groups));
            Assert.That(calls, Is.Positive.And.LessThan(2 * groups));
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.EqualTo(2 * groups - calls));
            Assert.That(artifact.Manifest.Callables, Has.Length.EqualTo(uncapped.Manifest.Callables.Length - (2 * groups - calls)));
        }
    }

    [Test]
    public void DeclaredClaimsAreNeverDroppedByTheCap()
    {
        var artifact = Artifact(Groups(3), callerSelectionBytes: 1);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Count(artifact, WorkerClaimKind.Postcondition), Is.EqualTo(3));
            Assert.That(Count(artifact, WorkerClaimKind.CallPrecondition), Is.Zero);
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.EqualTo(6));
            Assert.That(artifact.Manifest.Callables.Select(static callable => callable.CallableId),
                Has.None.Contains("Simple").And.None.Contains("Looping").And.Exactly(3).Contains("Declared"));
        }
    }

    // A 100 ms method budget times out the looping callers. Those claims fail
    // require-proven only if a claim the source declares is also Unknown.
    [Test]
    public async Task TimedOutCallClaimsDoNotFailRequireProven()
    {
        using var project = new ShadowTestProject(Groups(50));
        var budgets = new WorkerBudgets { MethodWallTimeMilliseconds = 100 };
        using var worker = SharpProofWorker.CreateNative(budgets);
        var response = await worker.VerifyAsync(project.Request with { Budgets = budgets }, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        var claims = response.Manifest.Claims.ToImmutableDictionary(static claim => claim.ClaimId,
            StringComparer.Ordinal);
        var unknown = response.ClaimResults.Where(static result => result.Outcome == WorkerClaimOutcome.Unknown)
            .Select(result => (Claim: claims[result.ClaimId], Result: result)).ToImmutableArray();
        var timedOut = unknown.Where(static item => item.Claim.Kind == WorkerClaimKind.CallPrecondition &&
            item.Result.Reason is WorkerClaimReason.MethodTimeout or WorkerClaimReason.ProjectTimeout).ToImmutableArray();
        var (exitCode, output) = Report(response);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(timedOut, Is.Not.Empty);
            Assert.That(response.ClaimResults.Select(static result => result.Outcome),
                Has.None.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(exitCode, Is.EqualTo(unknown.Length == timedOut.Length ? 0 : 6), output);
            Assert.That(output, Does.Contain(FormattableString.Invariant(
                $"unchecked calls to methods with preconditions={response.Manifest.UncheckedCallSites + timedOut.Select(static item => item.Claim.Location).Distinct().Count()};")));
        }
    }

    private static (int ExitCode, string Output) Report(WorkerVerifyResponse response)
    {
        System.ArgumentNullException.ThrowIfNull(response);
        using var directory = new TempDirectory("sharpproof-call-limits-");
        var resultPath = Path.Combine(directory.FullName, "result.json");
        File.WriteAllText(resultPath, WorkerProtocolJson.SerializeResponse(response));
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exitCode = Launcher.Program.ValidateAndReport(resultPath, new WorkerVerifyRequest { VerifyPolicy = WorkerVerifyPolicy.RequireProven },
                null, null, null, out var valid, out _);
            Assert.That(valid, Is.True, error.ToString());
            return (exitCode, output.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }
}
