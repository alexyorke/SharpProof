using System.Text.Json;
using NUnit.Framework;
using SharpProof.Host;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class RefutedContractDiagnosticTests
{
    [Test]
    public void RefutedContractEmitsLocatedStructuredDiagnosticAndExitFive()
    {
        using var temporary = new TempDirectory(
            "sharpproof-refuted-diagnostic-",
            TestContext.CurrentContext.WorkDirectory);
        var resultPath = Path.Combine(temporary.FullName, "result.json");
        var response = CreateRefutedResponse();
        File.WriteAllText(resultPath, WorkerProtocolJson.SerializeResponse(response));

        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);

            var exitCode = Program.ValidateAndReport(
                resultPath,
                new WorkerVerifyRequest(),
                null,
                null,
                null,
                out var validResponse,
                out _);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(validResponse, Is.True, error.ToString());
                Assert.That(exitCode, Is.EqualTo(5));
                Assert.That(
                    output.ToString(),
                    Does.Contain("SharpProof Refuted C.M Postcondition claim claim-1"));
            }

            var diagnosticLine = error.ToString()
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .Single(static line => line.StartsWith(
                    VerifierDiagnosticTransport.Prefix,
                    StringComparison.Ordinal));
            Assert.That(
                VerifierDiagnosticTransport.TryDeserialize(
                    diagnosticLine,
                    out var transported),
                Is.True);
            Assert.That(transported.Code, Is.EqualTo("SP0051"));
            using var diagnostic = JsonDocument.Parse(
                diagnosticLine[VerifierDiagnosticTransport.Prefix.Length..]);
            var root = diagnostic.RootElement;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(root.GetProperty("schema").GetInt32(), Is.EqualTo(1));
                Assert.That(root.GetProperty("severity").GetString(), Is.EqualTo("error"));
                Assert.That(root.GetProperty("code").GetString(), Is.EqualTo("SP0051"));
                Assert.That(root.GetProperty("file").GetString(), Is.EqualTo("Subject.cs"));
                Assert.That(root.GetProperty("line").GetInt32(), Is.EqualTo(12));
                Assert.That(root.GetProperty("column").GetInt32(), Is.EqualTo(7));
                Assert.That(
                    root.GetProperty("message").GetString(),
                    Does.Contain("claim-1").And.Contain("value = -1"));
            }
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    private static WorkerVerifyResponse CreateRefutedResponse()
    {
        var location = new WorkerSourceLocation
        {
            Path = "Subject.cs",
            Start = 100,
            Length = 4,
            Line = 12,
            Column = 7
        };
        var manifest = new WorkerClaimManifest
        {
            Callables = [
                new WorkerCallableManifestEntry
                {
                    CallableId = "C.M",
                    SelectedFeatures = [WorkerSelectedFeature.Contracts],
                    SelectionReasons = [WorkerSelectionReason.DiscoveredPostcondition],
                    Location = location,
                    ClaimIds = ["claim-1"]
                }
            ],
            Claims = [
                new WorkerClaimManifestEntry
                {
                    ClaimId = "claim-1",
                    CallableId = "C.M",
                    Ordinal = 0,
                    Kind = WorkerClaimKind.Postcondition,
                    Evidence = WorkerClaimEvidence.DirectClause,
                    Location = location
                }
            ]
        };
        WorkerProtocolJson.SealManifest(manifest);
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
                    CallableId = "C.M",
                    Coverage = WorkerCallableCoverage.Complete,
                    Reason = WorkerCallableCoverageReason.None
                }
            ],
            ClaimResults = [
                new WorkerClaimResult
                {
                    ClaimId = "claim-1",
                    Outcome = WorkerClaimOutcome.Refuted,
                    Reason = WorkerClaimReason.None,
                    Model = [
                        new WorkerModelValue
                        {
                            Variable = "value",
                            Kind = "Int64",
                            Value = "-1"
                        }
                    ]
                }
            ],
            Summary = new WorkerVerificationSummary
            {
                CallableCount = 1,
                ClaimCount = 1,
                OutcomeCounts = [
                    new WorkerClaimOutcomeCount
                    {
                        Outcome = WorkerClaimOutcome.Refuted,
                        Count = 1
                    }
                ],
                ReasonCounts = [
                    new WorkerClaimReasonCount
                    {
                        Reason = WorkerClaimReason.None,
                        Count = 1
                    }
                ],
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "launcher-test",
                    ApiSpecVersion = "launcher-test"
                },
                Budgets = new WorkerBudgets()
            }
        };
    }
}
