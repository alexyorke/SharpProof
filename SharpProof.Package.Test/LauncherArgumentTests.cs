using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class LauncherArgumentTests {
    [Test]
    public void UnknownOptionIsRejected() {
        string[] arguments = [
            .. ValidArguments(),
            "--project-wall-milliseconds",
            "100"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out _),
            Is.False);
    }

    [Test]
    public void SarifRequiresTheAtomicPublicationTriple() {
        string[] arguments = [
            .. ValidArguments(),
            "--publish-sarif", "result.sarif"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out _),
            Is.False);
    }

    [Test]
    public void CompletePublicationAcceptsSarif() {
        string[] arguments = [
            .. ValidArguments(),
            "--publish-request", "published-request.json",
            "--publish-result", "published-result.json",
            "--publish-compiler-manifest", "published-manifest.json",
            "--publish-sarif", "result.sarif"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);
        Assert.That(
            parsed.PublishSarifPath,
            Is.EqualTo(Path.GetFullPath("result.sarif")));
    }

    [Test]
    public void CombinedTimeoutOverflowIsRejectedBeforeStartingWorker() {
        Action action = () => _ = Program.ComputeHardLimit(
            int.MaxValue,
            WorkerLauncherDefaults.TerminationGraceMilliseconds);

        Assert.That(action, Throws.TypeOf<OverflowException>());
    }

    [TestCase(1_000, 1_000, 1_900)]
    [TestCase(1_000, 100, 1_001)]
    public void CombinedTimeoutReservesCleanupTime(
        int projectMilliseconds, int graceMilliseconds, int expected) =>
        Assert.That(
            Program.ComputeHardLimit(projectMilliseconds, graceMilliseconds),
            Is.EqualTo(expected));

    [TestCase("input", "response.input_mismatch")]
    [TestCase("budgets", "response.budgets_mismatch")]
    public void BoundResultValidationRejectsMismatches(
        string mismatch, string expectedError) {
        var request = new WorkerVerifyRequest();
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        const string inputHash =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var response = new WorkerVerifyResponse {
            RequestHash = WorkerProtocolJson.ComputeRequestHash(request),
            InputHash = inputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Summary = new WorkerVerificationSummary {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary {
                    WorkerVersion = "test",
                    ApiSpecVersion = "test"
                },
                Budgets = new WorkerBudgets()
            }
        };
        if (mismatch == "input") response.InputHash = new('b', 64);
        else response.Summary.Budgets.QueryRlimit++;
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        var error = Console.Error;
        using var capture = new StringWriter();
        try {
            Console.SetError(capture);
            File.WriteAllText(
                path, WorkerProtocolJson.SerializeResponse(response));
            Assert.That(
                Program.ValidateAndReport(
                    path, request, inputHash, manifest, out var valid),
                Is.EqualTo(3));
            Assert.That(valid, Is.False);
            Assert.That(capture.ToString(), Does.Contain(expectedError));
        }
        finally {
            Console.SetError(error);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public void SarifProjectionIsDeterministicAndIncludesTypedResults() {
        var manifest = CreateSarifManifest();
        var response = new WorkerVerifyResponse {
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [
                new WorkerCallableResult {
                    CallableId = "C.M",
                    Coverage = WorkerCallableCoverage.Complete,
                    Reason = WorkerCallableCoverageReason.None,
                    Assumptions = [UsedUserAssumption()]
                },
                new WorkerCallableResult {
                    CallableId = "C.Unsupported",
                    Coverage = WorkerCallableCoverage.Incomplete,
                    Reason = WorkerCallableCoverageReason.UnsupportedCallable
                }
            ],
            ClaimResults = [
                new WorkerClaimResult {
                    ClaimId = "claim-1",
                    Outcome = WorkerClaimOutcome.Refuted,
                    Reason = WorkerClaimReason.None,
                    Model = [
                        new WorkerModelValue {
                            Variable = "value",
                            Kind = "Int64",
                            Value = "0"
                        }
                    ],
                    Assumptions = [UsedUserAssumption()]
                }
            ],
            Summary = new WorkerVerificationSummary {
                CallableCount = 2,
                ClaimCount = 1,
                CacheStatus = WorkerCacheStatus.Disabled,
                Assumptions = new WorkerAssumptionSummary {
                    Total = 1,
                    Used = 1,
                    User = 1
                },
                Versions = new WorkerVersionSummary {
                    WorkerVersion = "1.0.0-test",
                    ApiSpecVersion = "test"
                }
            }
        };
        var request = new WorkerVerifyRequest {
            VerifyPolicy = WorkerVerifyPolicy.RequireProven,
            AssumptionPolicy = WorkerAssumptionPolicy.Error
        };

        var first = SarifProjection.Serialize(request, response);
        var second = SarifProjection.Serialize(request, response);

        Assert.That(second, Is.EqualTo(first));
        using var document = JsonDocument.Parse(first);
        var root = document.RootElement;
        var run = root.GetProperty("runs")[0];
        var results = run.GetProperty("results");
        using (Assert.EnterMultipleScope()) {
            Assert.That(
                root.GetProperty("$schema").GetString(),
                Does.EndWith("sarif-2.1.0.json"));
            Assert.That(
                root.GetProperty("version").GetString(),
                Is.EqualTo("2.1.0"));
            Assert.That(
                run.GetProperty("invocations")[0]
                    .GetProperty("executionSuccessful").GetBoolean(),
                Is.True);
            Assert.That(results.GetArrayLength(), Is.EqualTo(2));
            Assert.That(
                results[0].GetProperty("ruleId").GetString(),
                Is.EqualTo("SharpProof.Refuted"));
            Assert.That(
                results[0].GetProperty("kind").GetString(),
                Is.EqualTo("fail"));
            Assert.That(
                results[0].GetProperty("level").GetString(),
                Is.EqualTo("error"));
            Assert.That(
                results[0].GetProperty("partialFingerprints")
                    .GetProperty("sharpProofSemanticId/v1").GetString(),
                Is.EqualTo("claim-1"));
            var physicalLocation = results[0].GetProperty("locations")[0]
                .GetProperty("physicalLocation");
            Assert.That(
                physicalLocation.GetProperty("artifactLocation")
                    .GetProperty("uri").GetString(),
                Is.EqualTo("file:///C:/source/Subject.cs"));
            Assert.That(
                physicalLocation.GetProperty("region")
                    .GetProperty("startLine").GetInt32(),
                Is.EqualTo(2));
            Assert.That(
                physicalLocation.GetProperty("region")
                    .GetProperty("startColumn").GetInt32(),
                Is.EqualTo(5));
            Assert.That(
                results[1].GetProperty("ruleId").GetString(),
                Is.EqualTo("SP0047"));
            Assert.That(
                results[1].GetProperty("level").GetString(),
                Is.EqualTo("error"));
            var assumption = run.GetProperty("invocations")[0]
                .GetProperty("toolExecutionNotifications")[0];
            Assert.That(
                assumption.GetProperty("descriptor")
                    .GetProperty("id").GetString(),
                Is.EqualTo("SP0048"));
            Assert.That(
                assumption.GetProperty("level").GetString(),
                Is.EqualTo("error"));
        }
    }

    [TestCase(
        WorkerClaimOutcome.Proven, WorkerVerifyPolicy.Advisory,
        "pass", "none")]
    [TestCase(
        WorkerClaimOutcome.Refuted, WorkerVerifyPolicy.Advisory,
        "fail", "error")]
    [TestCase(
        WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.Advisory,
        "review", "note")]
    [TestCase(
        WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.WarnOnUnknown,
        "review", "warning")]
    [TestCase(
        WorkerClaimOutcome.Unknown, WorkerVerifyPolicy.RequireProven,
        "review", "error")]
    public void SarifClaimPresentationFollowsOutcomeAndPolicy(
        WorkerClaimOutcome outcome, WorkerVerifyPolicy policy,
        string expectedKind, string expectedLevel) {
        var response = new WorkerVerifyResponse {
            Manifest = CreateSarifManifest(),
            ClaimResults = [
                new WorkerClaimResult {
                    ClaimId = "claim-1",
                    Outcome = outcome,
                    Reason = outcome == WorkerClaimOutcome.Unknown
                        ? WorkerClaimReason.UnsupportedExpression
                        : WorkerClaimReason.None
                }
            ],
            Summary = new WorkerVerificationSummary {
                Versions = new WorkerVersionSummary {
                    WorkerVersion = "1.0.0-test"
                }
            }
        };
        using var document = JsonDocument.Parse(
            SarifProjection.Serialize(
                new WorkerVerifyRequest { VerifyPolicy = policy },
                response));
        var result = document.RootElement.GetProperty("runs")[0]
            .GetProperty("results")[0];

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                result.GetProperty("ruleId").GetString(),
                Is.EqualTo("SharpProof." + outcome));
            Assert.That(
                result.GetProperty("kind").GetString(),
                Is.EqualTo(expectedKind));
            Assert.That(
                result.GetProperty("level").GetString(),
                Is.EqualTo(expectedLevel));
        }
    }

    [Test]
    public void SarifProjectionPreservesInfrastructureFailure() {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse {
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Failed,
            FailureReason = WorkerRunFailureReason.InfrastructureFailure,
            Summary = new WorkerVerificationSummary {
                CacheStatus = WorkerCacheStatus.Disabled,
                Assumptions = new WorkerAssumptionSummary {
                    Total = 1,
                    User = 1
                },
                Versions = new WorkerVersionSummary {
                    WorkerVersion = "launcher",
                    ApiSpecVersion = "unavailable"
                }
            },
            Errors = [
                new WorkerProtocolError {
                    Code = "infrastructure.test",
                    Message = "Deliberate failure."
                }
            ]
        };

        using var document = JsonDocument.Parse(
            SarifProjection.Serialize(
                new WorkerVerifyRequest {
                    AssumptionPolicy = WorkerAssumptionPolicy.Warn
                },
                response));
        var invocation = document.RootElement.GetProperty("runs")[0]
            .GetProperty("invocations")[0];

        using (Assert.EnterMultipleScope()) {
            Assert.That(
                invocation.GetProperty("executionSuccessful").GetBoolean(),
                Is.False);
            Assert.That(
                invocation.GetProperty("properties")
                    .GetProperty("runStatus").GetString(),
                Is.EqualTo("Failed"));
            Assert.That(
                invocation.GetProperty("toolExecutionNotifications")[0]
                    .GetProperty("descriptor").GetProperty("id").GetString(),
                Is.EqualTo("infrastructure.test"));
            Assert.That(
                invocation.GetProperty("toolExecutionNotifications")[1]
                    .GetProperty("descriptor").GetProperty("id").GetString(),
                Is.EqualTo("SP0048"));
        }
    }

    private static WorkerClaimManifest CreateSarifManifest() {
        var location = new WorkerSourceLocation {
            Path = @"C:\source\Subject.cs",
            Start = 10,
            Length = 4,
            Line = 2,
            Column = 5
        };
        var manifest = new WorkerClaimManifest {
            Callables = [
                new WorkerCallableManifestEntry {
                    CallableId = "C.M",
                    SelectedFeatures = [WorkerSelectedFeature.Contracts],
                    SelectionReasons = [
                        WorkerSelectionReason.DiscoveredPostcondition
                    ],
                    Location = location,
                    ClaimIds = ["claim-1"],
                    Assumptions = [
                        new WorkerAssumptionEvidence {
                            Id = "assumption-1",
                            Kind = WorkerAssumptionKind.UserAssume
                        }
                    ]
                },
                new WorkerCallableManifestEntry {
                    CallableId = "C.Unsupported",
                    SelectedFeatures = [WorkerSelectedFeature.Effects],
                    SelectionReasons = [
                        WorkerSelectionReason.ExplicitAnnotation
                    ],
                    Location = location
                }
            ],
            Claims = [
                new WorkerClaimManifestEntry {
                    ClaimId = "claim-1",
                    CallableId = "C.M",
                    Kind = WorkerClaimKind.Postcondition,
                    Evidence = WorkerClaimEvidence.DirectClause,
                    Location = location
                }
            ]
        };
        WorkerProtocolJson.SealManifest(manifest);
        return manifest;
    }

    private static WorkerAssumptionEvidence UsedUserAssumption() =>
        new() {
            Id = "assumption-1",
            Kind = WorkerAssumptionKind.UserAssume,
            Used = true
        };

    private static string[] ValidArguments() => [
        "verify",
        "--worker", "worker.dll",
        "--request", "request.json",
        "--result", "result.json",
        "--compiler-manifest", "compiler-manifest.json",
        "--verify-policy", "advisory",
        "--assumption-policy", "allow"
    ];
}
