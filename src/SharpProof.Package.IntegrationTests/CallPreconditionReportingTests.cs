using System.Text.Json;
using NUnit.Framework;
using SharpProof.Host;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Package.Test;

// A replayed violation is an error at the call under every policy. An
// unprovable obligation is incomplete coverage, which only require-proven rejects.
[TestFixture]
internal sealed class CallPreconditionReportingTests
{
    private static readonly WorkerSourceLocation CallSite = new() { Path = "Subject.cs", Start = 120, Length = 5, Line = 9, Column = 16 };
    private static readonly WorkerSourceLocation Declaration = new() { Path = "Subject.cs", Start = 80, Length = 40, Line = 8, Column = 5 };
    private static readonly WorkerSourceLocation Clause = new() { Path = "Subject.cs", Start = 20, Length = 22, Line = 3, Column = 34 };

    private static WorkerVerifyResponse CreateResponse(WorkerClaimOutcome outcome)
    {
        var manifest = new WorkerClaimManifest
        {
            Callables = [
                new WorkerCallableManifestEntry
                {
                    CallableId = "M:Subject.Caller",
                    SelectedFeatures = [WorkerSelectedFeature.Contracts],
                    SelectionReasons = [WorkerSelectionReason.DiscoveredCallPrecondition],
                    Location = Declaration,
                    ClaimIds = ["claim-1"]
                }
            ],
            Claims = [
                new WorkerClaimManifestEntry
                {
                    ClaimId = "claim-1",
                    CallableId = "M:Subject.Caller",
                    Kind = WorkerClaimKind.CallPrecondition,
                    Evidence = WorkerClaimEvidence.CallSite,
                    CallPrecondition = new WorkerCallPreconditionTarget { Callee = "Consumer, Version=2.1.0.0, Culture=neutral, PublicKeyToken=null::M:Subject.F", ClauseOrdinal = 1, ClauseLocation = Clause },
                    Location = CallSite
                }
            ]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var unknown = outcome == WorkerClaimOutcome.Unknown;
        return new WorkerVerifyResponse
        {
            RequestHash = new('b', 64),
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [
                new WorkerCallableResult
                {
                    CallableId = "M:Subject.Caller",
                    Coverage = unknown ? WorkerCallableCoverage.Incomplete : WorkerCallableCoverage.Complete,
                    Reason = unknown ? WorkerCallableCoverageReason.SemanticUnknown : WorkerCallableCoverageReason.None
                }
            ],
            ClaimResults = [
                new WorkerClaimResult
                {
                    ClaimId = "claim-1",
                    Outcome = outcome,
                    Reason = unknown ? WorkerClaimReason.CounterexampleNotReplayable : WorkerClaimReason.None,
                    Model = outcome == WorkerClaimOutcome.Refuted
                        ? [new WorkerModelValue { Variable = "parameter:0", Kind = "Int32", Value = "0" }] : []
                }
            ],
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary { WorkerVersion = "launcher-test", ApiSpecVersion = "launcher-test" },
                Budgets = new WorkerBudgets()
            }
        };
    }

    private static (int ExitCode, string Output, string Error) Report(WorkerVerifyResponse response, WorkerVerifyPolicy policy)
    {
        System.ArgumentNullException.ThrowIfNull(response);
        using var temporary = new TempDirectory("sharpproof-call-precondition-", TestContext.CurrentContext.WorkDirectory);
        var resultPath = Path.Combine(temporary.FullName, "result.json");
        File.WriteAllText(resultPath, WorkerProtocolJson.SerializeResponse(response));
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exitCode = Program.ValidateAndReport(resultPath, new WorkerVerifyRequest { VerifyPolicy = policy }, null, null, null, out var valid, out _);
            Assert.That(valid, Is.True, error.ToString());
            return (exitCode, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    [TestCase(WorkerVerifyPolicy.Advisory)]
    [TestCase(WorkerVerifyPolicy.WarnOnUnknown)]
    [TestCase(WorkerVerifyPolicy.RequireProven)]
    public void ViolationIsAnErrorAtTheCallUnderEveryPolicy(WorkerVerifyPolicy policy)
    {
        var (exitCode, _, error) = Report(CreateResponse(WorkerClaimOutcome.Refuted), policy);
        var line = error.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Single(static line => line is null ? throw new System.ArgumentNullException(nameof(line)) : (line.StartsWith(VerifierDiagnosticTransport.Prefix, StringComparison.Ordinal)));
        using var diagnostic = JsonDocument.Parse(line[VerifierDiagnosticTransport.Prefix.Length..]);
        var root = diagnostic.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(5));
            Assert.That(root.GetProperty("code").GetString(), Is.EqualTo("SP0051"));
            Assert.That(root.GetProperty("line").GetInt32(), Is.EqualTo(9));
            Assert.That(root.GetProperty("column").GetInt32(), Is.EqualTo(16));
            Assert.That(root.GetProperty("message").GetString(), Does.Contain("Refuted CallPrecondition claim claim-1")
                .And.Contain("Call to M:Subject.F, clause 1 at Subject.cs(3,34)").And.Contain("= 0").And.Not.Contain("Version="));
        }
    }

    [Test]
    public void PassReportsTheCallsNoClaimChecked()
    {
        var response = CreateResponse(WorkerClaimOutcome.Proven);
        var manifest = response.Manifest with { UncheckedCallSites = 3 };
        WorkerProtocolJson.SealManifest(manifest);
        var (exitCode, output, _) = Report(response with { Manifest = manifest }, WorkerVerifyPolicy.RequireProven);
        using var document = JsonDocument.Parse(SarifProjection.Serialize(new WorkerVerifyRequest(), response with { Manifest = manifest }, "/workspace/consumer"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.Zero);
            Assert.That(output, Does.Contain("unchecked calls to methods with preconditions=3;"));
            Assert.That(document.RootElement.GetProperty("runs")[0].GetProperty("invocations")[0].GetProperty("properties").GetProperty("uncheckedCallSites").GetInt32(), Is.EqualTo(3));
        }
    }

    [TestCase(WorkerVerifyPolicy.Advisory, 0)]
    [TestCase(WorkerVerifyPolicy.WarnOnUnknown, 0)]
    [TestCase(WorkerVerifyPolicy.RequireProven, 6)]
    public void UnprovableObligationFailsOnlyRequireProven(WorkerVerifyPolicy policy, int expected)
    {
        var (exitCode, output, error) = Report(CreateResponse(WorkerClaimOutcome.Unknown), policy);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(expected));
            Assert.That(output, Does.Contain("Call to M:Subject.F, clause 1 at Subject.cs(3,34)"));
            Assert.That(error, Does.Not.Contain("SP0051"));
        }
    }

    [TestCase(WorkerClaimOutcome.Refuted, WorkerVerifyPolicy.Advisory, "SharpProof.Refuted", "fail", "error")]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.RequireProven, "SharpProof.Unknown", "fail", "error")]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.WarnOnUnknown, "SharpProof.Unknown", "fail", "warning")]
    [TestCase(WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.Advisory, "SharpProof.Unknown", "review", "none")]
    public void SarifReportsTheCall(WorkerClaimOutcome outcome, WorkerVerifyPolicy policy, string rule, string kind, string level)
    {
        System.ArgumentNullException.ThrowIfNull(kind);
        System.ArgumentNullException.ThrowIfNull(level);
        System.ArgumentNullException.ThrowIfNull(rule);
        using var document = JsonDocument.Parse(SarifProjection.Serialize(new WorkerVerifyRequest { VerifyPolicy = policy },
            CreateResponse(outcome), "/workspace/consumer"));
        var result = document.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        var region = result.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("region");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.GetProperty("ruleId").GetString(), Is.EqualTo(rule));
            Assert.That(result.GetProperty("kind").GetString(), Is.EqualTo(kind));
            Assert.That(result.GetProperty("level").GetString(), Is.EqualTo(level));
            Assert.That(region.GetProperty("startLine").GetInt32(), Is.EqualTo(9));
            Assert.That(region.GetProperty("startColumn").GetInt32(), Is.EqualTo(16));
            Assert.That(result.GetProperty("message").GetProperty("text").GetString(),
                Does.Contain("CallPrecondition").And.Contain("Call to M:Subject.F, clause 1 at Subject.cs(3,34)"));
        }
    }
}
