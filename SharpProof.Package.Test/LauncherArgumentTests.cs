using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Launcher;
using SharpProof.Worker.Protocol;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class LauncherArgumentTests
{
    [Test]
    public void UnknownOptionIsRejected()
    {
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
    public void SarifRequiresTheAtomicPublicationTriple()
    {
        string[] arguments = [
            .. ValidArguments(),
            "--publish-sarif", "result.sarif"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out _),
            Is.False);
    }

    [Test]
    public void CompletePublicationAcceptsSarif()
    {
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
    public void ParsedPathsAndTerminationGraceAreNormalized()
    {
        string[] arguments = [
            .. ValidArguments(),
            "--publish-request", "published-request.json",
            "--publish-result", "published-result.json",
            "--publish-compiler-manifest", "published-manifest.json",
            "--publish-sarif", "published-result.sarif",
            "--termination-grace-ms", "321"
        ];

        Assert.That(
            LauncherArguments.TryParse(arguments, out var parsed),
            Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(parsed.WorkerPath, Is.EqualTo(Path.GetFullPath("worker.dll")));
            Assert.That(parsed.RequestPath, Is.EqualTo(Path.GetFullPath("request.json")));
            Assert.That(parsed.ResultPath, Is.EqualTo(Path.GetFullPath("result.json")));
            Assert.That(
                parsed.CompilerManifestPath,
                Is.EqualTo(Path.GetFullPath("compiler-manifest.json")));
            Assert.That(
                parsed.PublishRequestPath,
                Is.EqualTo(Path.GetFullPath("published-request.json")));
            Assert.That(
                parsed.PublishResultPath,
                Is.EqualTo(Path.GetFullPath("published-result.json")));
            Assert.That(
                parsed.PublishCompilerManifestPath,
                Is.EqualTo(Path.GetFullPath("published-manifest.json")));
            Assert.That(
                parsed.PublishSarifPath,
                Is.EqualTo(Path.GetFullPath("published-result.sarif")));
            Assert.That(parsed.TerminationGraceMilliseconds, Is.EqualTo(321));
        }
    }

    [Test]
    public void CombinedTimeoutOverflowIsRejectedBeforeStartingWorker()
    {
        Action action = () => _ = Program.ComputeHardLimit(
            int.MaxValue,
            WorkerLauncherDefaults.TerminationGraceMilliseconds);

        Assert.That(action, Throws.TypeOf<OverflowException>());
    }

    [Test]
    public void CompilerManifestByteLimitIsEnforcedBeforeAllocation()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using (var stream = File.Create(path))
            {
                stream.SetLength(
                    LauncherArguments.MaximumCompilerManifestBytes + 1L);
            }

            Assert.That(
                (Action)(() => LauncherArguments.ReadCompilerManifest(path)),
                Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void DotNetHostMustBeAbsoluteInstalledAndOutsideProject()
    {
        var project = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N"));
        var fakeRoot = Path.Combine(project, "fake-sdk");
        var fakeHost = Path.Combine(fakeRoot, "dotnet.exe");
        Directory.CreateDirectory(Path.Combine(fakeRoot, "host", "fxr"));
        File.WriteAllBytes(fakeHost, []);
        var actualHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ??
            throw new InvalidOperationException(
                "The test host did not disclose its dotnet host path.");
        try
        {
            Assert.That(
                Program.ValidateDotNetHostPath(actualHost, project),
                Is.EqualTo(Path.GetFullPath(actualHost)));
            Assert.That(
                (Action)(() => _ = Program.ValidateDotNetHostPath(
                    "dotnet.exe", project)),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                (Action)(() => _ = Program.ValidateDotNetHostPath(
                    fakeHost, project)),
                Throws.TypeOf<InvalidOperationException>());
        }
        finally
        {
            Directory.Delete(project, recursive: true);
        }
    }

    [TestCase(1_000, 1_000, 1_900)]
    [TestCase(1_000, 100, 1_001)]
    public void CombinedTimeoutReservesCleanupTime(
        int projectMilliseconds, int graceMilliseconds, int expected)
    {
        Assert.That(
            Program.ComputeHardLimit(projectMilliseconds, graceMilliseconds),
            Is.EqualTo(expected));
    }

    [TestCase("input", "response.input_mismatch")]
    [TestCase("budgets", "response.budgets_mismatch")]
    public void BoundResultValidationRejectsMismatches(
        string mismatch, string expectedError)
    {
        var request = new WorkerVerifyRequest();
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        const string inputHash =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var response = new WorkerVerifyResponse
        {
            RequestHash = WorkerProtocolJson.ComputeRequestHash(request),
            InputHash = inputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "test",
                    ApiSpecVersion = "test"
                },
                Budgets = new WorkerBudgets()
            }
        };
        if (mismatch == "input")
        {
            response.InputHash = new('b', 64);
        }
        else
        {
            response.Summary.Budgets.QueryRlimit++;
        }

        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        var error = Console.Error;
        using var capture = new StringWriter();
        try
        {
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
        finally
        {
            Console.SetError(error);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void BoundResultReportsUnknownClaimsAndAssumptionsAccountably()
    {
        var request = new WorkerVerifyRequest
        {
            VerifyPolicy = WorkerVerifyPolicy.RequireProven,
            AssumptionPolicy = WorkerAssumptionPolicy.Error
        };
        var manifest = CreateSarifManifest();
        var usedAssumption = UsedUserAssumption();
        var response = new WorkerVerifyResponse
        {
            RequestHash = WorkerProtocolJson.ComputeRequestHash(request),
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [
                new WorkerCallableResult {
                    CallableId = "C.M",
                    Coverage = WorkerCallableCoverage.Incomplete,
                    Reason = WorkerCallableCoverageReason.UnsupportedCallable,
                    Assumptions = [usedAssumption]
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
                    Outcome = WorkerClaimOutcome.Unknown,
                    Reason = WorkerClaimReason.UnsupportedExpression,
                    Assumptions = [usedAssumption]
                }
            ],
            Summary = new WorkerVerificationSummary
            {
                CallableCount = 2,
                ClaimCount = 1,
                OutcomeCounts = [
                    new WorkerClaimOutcomeCount {
                        Outcome = WorkerClaimOutcome.Unknown,
                        Count = 1
                    }
                ],
                ReasonCounts = [
                    new WorkerClaimReasonCount {
                        Reason = WorkerClaimReason.UnsupportedExpression,
                        Count = 1
                    }
                ],
                Assumptions = new WorkerAssumptionSummary
                {
                    Total = 1,
                    Used = 1,
                    User = 1
                },
                CacheStatus = WorkerCacheStatus.Disabled,
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "launcher-test",
                    ApiSpecVersion = "launcher-test"
                },
                Budgets = new WorkerBudgets()
            }
        };
        const string inputHash =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            Guid.NewGuid().ToString("N") + ".json");
        var output = Console.Out;
        var error = Console.Error;
        using var outputCapture = new StringWriter();
        using var errorCapture = new StringWriter();
        try
        {
            Console.SetOut(outputCapture);
            Console.SetError(errorCapture);
            File.WriteAllText(
                path,
                WorkerProtocolJson.SerializeResponse(response));

            var exitCode = Program.ValidateAndReport(
                path,
                request,
                inputHash,
                manifest,
                out var valid);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(valid, Is.True, errorCapture.ToString());
                Assert.That(exitCode, Is.EqualTo(6));
                Assert.That(
                    outputCapture.ToString(),
                    Does.Contain(
                        "SharpProof Unknown C.M Postcondition claim claim-1 " +
                        "(UnsupportedExpression)"));
                Assert.That(errorCapture.ToString(), Does.Contain("SP0047"));
                Assert.That(errorCapture.ToString(), Does.Contain("SP0048"));
            }
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(error);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    public void SarifProjectionIsDeterministicAndIncludesTypedResults()
    {
        var manifest = CreateSarifManifest();
        var response = new WorkerVerifyResponse
        {
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
            Summary = new WorkerVerificationSummary
            {
                CallableCount = 2,
                ClaimCount = 1,
                CacheStatus = WorkerCacheStatus.Disabled,
                Assumptions = new WorkerAssumptionSummary
                {
                    Total = 1,
                    Used = 1,
                    User = 1
                },
                Versions = new WorkerVersionSummary
                {
                    WorkerVersion = "1.0.0-test",
                    ApiSpecVersion = "test"
                }
            }
        };
        var request = new WorkerVerifyRequest
        {
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
        using (Assert.EnterMultipleScope())
        {
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

    [Test]
    public void SarifProjectionPreservesVacuityAndEffectCertainty()
    {
        var manifest = CreateSarifManifest();
        manifest.Callables = [manifest.Callables[0]];
        manifest.Callables[0].Assumptions = [];
        var claim = manifest.Claims.Single();
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [
                new WorkerCallableResult {
                    CallableId = claim.CallableId,
                    Coverage = WorkerCallableCoverage.Complete,
                    Reason = WorkerCallableCoverageReason.None
                }
            ],
            ClaimResults = [
                new WorkerClaimResult {
                    ClaimId = claim.ClaimId,
                    Outcome = WorkerClaimOutcome.Proven,
                    Reason = WorkerClaimReason.None,
                    Vacuity = WorkerVacuityKind.NoModeledNormalReturn
                }
            ]
        };
        var request = new WorkerVerifyRequest();

        using var vacuity = JsonDocument.Parse(
            SarifProjection.Serialize(request, response));
        Assert.That(
            ResultEvidence(vacuity).GetProperty("vacuity").GetString(),
            Is.EqualTo("NoModeledNormalReturn"));

        manifest.Callables[0].SelectedFeatures = [
            WorkerSelectedFeature.Effects
        ];
        manifest.Callables[0].SelectionReasons = [
            WorkerSelectionReason.ExplicitAnnotation
        ];
        claim.Kind = WorkerClaimKind.Effect;
        claim.Evidence = WorkerClaimEvidence.Attribute;
        claim.EffectContractKind = WorkerEffectContractKind.DoesNotThrow;
        WorkerProtocolJson.SealManifest(manifest);
        response.ClaimResults[0].Vacuity = WorkerVacuityKind.None;
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;

        using var certainty = JsonDocument.Parse(
            SarifProjection.Serialize(request, response));
        Assert.That(
            ResultEvidence(certainty).GetProperty("effectCertainty").GetString(),
            Is.EqualTo("CompleteMayEffectSummary"));

        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Refuted;
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.DefiniteViolation;
        response.ClaimResults[0].EffectWitness =
            new WorkerEffectViolationWitness
            {
                Kind = "explicit-throw",
                Detail = "T:System.InvalidOperationException",
                Effects = WorkerEffectSet.Throws,
                ExactExceptionTypeHierarchy = [
                    "System.Private.CoreLib:T:System.InvalidOperationException",
                    "System.Private.CoreLib:T:System.Exception"
                ],
                Location = new WorkerSourceLocation
                {
                    Path = "witness.cs",
                    Start = 20,
                    Length = 5,
                    Line = 9,
                    Column = 7
                }
            };
        using var refuted = JsonDocument.Parse(
            SarifProjection.Serialize(request, response));
        var projected = refuted.RootElement.GetProperty("runs")[0]
            .GetProperty("results")[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                projected.GetProperty("ruleId").GetString(),
                Is.EqualTo("SharpProof.Refuted"));
            Assert.That(
                projected.GetProperty("message").GetProperty("text")
                    .GetString(),
                Does.Contain("concrete explicit-throw")
                    .And.Contain("witness.cs:9:7"));
            Assert.That(
                projected.GetProperty("locations")[0]
                    .GetProperty("physicalLocation")
                    .GetProperty("region")
                    .GetProperty("startLine").GetInt32(),
                Is.EqualTo(9));
            Assert.That(
                ResultEvidence(refuted).GetProperty("effectWitness")
                    .GetProperty("kind").GetString(),
                Is.EqualTo("explicit-throw"));
        }

        static JsonElement ResultEvidence(JsonDocument document)
        {
            return document.RootElement.GetProperty("runs")[0]
                .GetProperty("results")[0]
                .GetProperty("properties")
                .GetProperty("result");
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
        string expectedKind, string expectedLevel)
    {
        var response = new WorkerVerifyResponse
        {
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
            Summary = new WorkerVerificationSummary
            {
                Versions = new WorkerVersionSummary
                {
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

        using (Assert.EnterMultipleScope())
        {
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

    [TestCase(WorkerVerifyPolicy.Advisory, "info")]
    [TestCase(WorkerVerifyPolicy.WarnOnUnknown, "warning")]
    [TestCase(WorkerVerifyPolicy.RequireProven, "error")]
    public void VerifyPolicyPresentationUsesNamedMappings(
        WorkerVerifyPolicy policy, string expected)
    {
        Assert.That(LauncherPresentation.Level(policy, "info"), Is.EqualTo(expected));
    }

    [TestCase(WorkerAssumptionPolicy.Allow, "info")]
    [TestCase(WorkerAssumptionPolicy.Warn, "warning")]
    [TestCase(WorkerAssumptionPolicy.Error, "error")]
    public void AssumptionPolicyPresentationUsesNamedMappings(
        WorkerAssumptionPolicy policy, string expected)
    {
        Assert.That(LauncherPresentation.Level(policy, "info"), Is.EqualTo(expected));
    }

    [TestCase("advisory", WorkerVerifyPolicy.Advisory)]
    [TestCase("warn-on-unknown", WorkerVerifyPolicy.WarnOnUnknown)]
    [TestCase("require-proven", WorkerVerifyPolicy.RequireProven)]
    public void VerifyPolicyParsingUsesNames(
        string value, WorkerVerifyPolicy expected)
    {
        Assert.That(LauncherPresentation.ParseVerifyPolicy(value), Is.EqualTo(expected));
    }

    [TestCase("allow", WorkerAssumptionPolicy.Allow)]
    [TestCase("warn", WorkerAssumptionPolicy.Warn)]
    [TestCase("error", WorkerAssumptionPolicy.Error)]
    public void AssumptionPolicyParsingUsesNames(
        string value, WorkerAssumptionPolicy expected)
    {
        Assert.That(LauncherPresentation.ParseAssumptionPolicy(value), Is.EqualTo(expected));
    }

    [Test]
    public void NumericPolicyAliasesAreRejected()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(
                (Action)(() => LauncherPresentation.ParseVerifyPolicy("1")));
            Assert.Throws<ArgumentException>(
                (Action)(() => LauncherPresentation.ParseAssumptionPolicy("1")));
        }
    }

    [Test]
    public void UnknownClaimAndEffectKindsAreRejectedExhaustively()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => LauncherPresentation.ClaimKind(
                    new WorkerClaimManifestEntry
                    {
                        Kind = (WorkerClaimKind)int.MaxValue
                    })));
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => LauncherPresentation.ClaimKind(
                    new WorkerClaimManifestEntry
                    {
                        Kind = WorkerClaimKind.Effect,
                        EffectContractKind =
                            (WorkerEffectContractKind)int.MaxValue
                    })));
        }
    }

    [Test]
    public void UnknownPresentationPolicyIsRejectedExhaustively()
    {
        Assert.Throws<InvalidOperationException>(
            (Action)(() => LauncherPresentation.Level(
                (WorkerVerifyPolicy)int.MaxValue,
                "info")));
    }

    [Test]
    public void SarifProjectionPreservesInfrastructureFailure()
    {
        var manifest = new WorkerClaimManifest();
        WorkerProtocolJson.SealManifest(manifest);
        var response = new WorkerVerifyResponse
        {
            InputHash = new('a', 64),
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Failed,
            FailureReason = WorkerRunFailureReason.InfrastructureFailure,
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Disabled,
                Assumptions = new WorkerAssumptionSummary
                {
                    Total = 1,
                    User = 1
                },
                Versions = new WorkerVersionSummary
                {
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
                new WorkerVerifyRequest
                {
                    AssumptionPolicy = WorkerAssumptionPolicy.Warn
                },
                response));
        var invocation = document.RootElement.GetProperty("runs")[0]
            .GetProperty("invocations")[0];

        using (Assert.EnterMultipleScope())
        {
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

    private static WorkerClaimManifest CreateSarifManifest()
    {
        var location = new WorkerSourceLocation
        {
            Path = @"C:\source\Subject.cs",
            Start = 10,
            Length = 4,
            Line = 2,
            Column = 5
        };
        var manifest = new WorkerClaimManifest
        {
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

    private static WorkerAssumptionEvidence UsedUserAssumption()
    {
        return new()
        {
            Id = "assumption-1",
            Kind = WorkerAssumptionKind.UserAssume,
            Used = true
        };
    }

    private static string[] ValidArguments()
    {
        return [
        "verify",
        "--worker", "worker.dll",
        "--request", "request.json",
        "--result", "result.json",
        "--compiler-manifest", "compiler-manifest.json",
        "--verify-policy", "advisory",
        "--assumption-policy", "allow"
    ];
    }
}
