using System.Text.Json;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ProtocolJsonTests {
    private const string InputHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public void VersionThreeRequestPoliciesAreExplicitStringEnums() {
        var request = CreateRequest();
        var json = WorkerProtocolJson.SerializeRequest(request);
        var roundTrip = WorkerProtocolJson.DeserializeRequest(json)!;

        using (Assert.EnterMultipleScope()) {
            Assert.That(WorkerProtocolVersions.Current, Is.EqualTo("3"));
            Assert.That(WorkerCacheVersions.Current, Is.EqualTo(3));
            Assert.That(roundTrip.Features, Is.EqualTo(WorkerFeatureSet.All));
            Assert.That(roundTrip.VerifyPolicy, Is.EqualTo(WorkerVerifyPolicy.Advisory));
            Assert.That(roundTrip.AssumptionPolicy, Is.EqualTo(WorkerAssumptionPolicy.Allow));
            Assert.That(WorkerProtocolJson.Validate(roundTrip).IsValid, Is.True);
        }
        Assert.Throws<JsonException>((Action)(() =>
            WorkerProtocolJson.DeserializeRequest(
                json.Replace(
                    "\"verifyPolicy\":\"Advisory\"",
                    "\"verifyPolicy\":1",
                    StringComparison.Ordinal))));
        Assert.Throws<JsonException>((Action)(() =>
            WorkerProtocolJson.DeserializeRequest(
                json.Replace(
                    "\"verifyPolicy\":\"Advisory\",",
                    string.Empty,
                    StringComparison.Ordinal))));

        request.VerifyPolicy = WorkerVerifyPolicy.Unspecified;
        request.AssumptionPolicy = (WorkerAssumptionPolicy)999;
        request.Features = WorkerFeatureSet.Unspecified;
        Assert.That(
            WorkerProtocolJson.Validate(request).Errors
                .Select(static error => error.Code),
            Does.Contain("policy.verify")
                .And.Contain("policy.assumption")
                .And.Contain("policy.features"));
    }

    [Test]
    public void ManifestHashIsCanonicalAndCoversEveryField() {
        var manifest = CreateManifest();
        var hash = manifest.Hash;

        manifest.Callables[0].SelectedFeatures = [
            WorkerSelectedFeature.Contracts
        ];
        WorkerProtocolJson.SealManifest(manifest);
        Assert.That(manifest.Hash, Is.EqualTo(hash));
        Assert.That(manifest.Hash, Does.Match("^[0-9a-f]{64}$"));

        manifest.Claims[0].Location.Column++;
        Assert.That(
            WorkerProtocolJson.ComputeManifestHash(manifest),
            Is.Not.EqualTo(hash));
        Assert.That(
            WorkerProtocolJson.Validate(CreateResponse(manifest)).Errors
                .Select(static error => error.Code),
            Does.Contain("manifest.hash"));
    }

    [Test]
    public void ManifestHashSeparatesFormerlyAmbiguousCollectionBoundaries() {
        var responseManifest = CreateBoundaryManifest(expandedFirst: false);
        var expectedManifest = CreateBoundaryManifest(expandedFirst: true);
        var response = CreateResponse(responseManifest);

        using (Assert.EnterMultipleScope()) {
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
            Assert.That(
                WorkerProtocolJson.Validate(CreateResponse(expectedManifest))
                    .IsValid,
                Is.True);
            Assert.That(responseManifest.Hash, Is.Not.EqualTo(expectedManifest.Hash));
            Assert.That(
                WorkerProtocolJson.Validate(
                        response,
                        InputHash,
                        expectedManifest)
                    .Errors.Select(static error => error.Code),
                Does.Contain("response.manifest_mismatch"));
        }
    }

    [Test]
    public void StrictResponseValidationRequiresExactManifestAndResultSets() {
        var expected = CreateManifest();
        var response = CreateResponse(expected);
        Assert.That(
            WorkerProtocolJson.Validate(response, InputHash, expected).IsValid,
            Is.True);

        response.ClaimResults = [];
        Assert.That(
            WorkerProtocolJson.Validate(response, InputHash, expected).Errors
                .Select(static error => error.Code),
            Does.Contain("response.claim_set"));

        response = CreateResponse(expected);
        response.ClaimResults = [
            response.ClaimResults[0],
            response.ClaimResults[0]
        ];
        Assert.That(
            WorkerProtocolJson.Validate(response, InputHash, expected).Errors
                .Select(static error => error.Code),
            Does.Contain("response.result_claim_id"));

        response = CreateResponse(expected);
        var other = CreateManifest();
        other.Claims[0].Location.Start++;
        WorkerProtocolJson.SealManifest(other);
        Assert.That(
            WorkerProtocolJson.Validate(response, InputHash, other).Errors
                .Select(static error => error.Code),
            Does.Contain("response.manifest_mismatch"));
        Assert.That(
            WorkerProtocolJson.Validate(
                    response,
                    new string('b', InputHash.Length),
                    expected)
                .Errors.Select(static error => error.Code),
            Does.Contain("response.input_mismatch"));
    }

    [Test]
    public void OmittedOrNumericClaimOutcomeCannotBecomeProven() {
        var json = WorkerProtocolJson.SerializeResponse(
            CreateResponse(CreateManifest()));
        var omitted = WorkerProtocolJson.DeserializeResponse(
            json.Replace(
                "\"outcome\":\"Proven\",",
                string.Empty,
                StringComparison.Ordinal))!;

        Assert.That(
            WorkerProtocolJson.Validate(omitted).Errors
                .Select(static error => error.Code),
            Does.Contain("response.claim_outcome"));
        Assert.Throws<JsonException>((Action)(() =>
            WorkerProtocolJson.DeserializeResponse(
                json.Replace(
                    "\"outcome\":\"Proven\"",
                    "\"outcome\":1",
                    StringComparison.Ordinal))));
    }

    [Test]
    public void UnknownClaimsRequireIncompleteCallableCoverage() {
        var response = CreateResponse(CreateManifest());
        SetUnknown(response, WorkerClaimReason.UnsupportedBody);
        response.Summary = CreateSummary(response);

        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.unknown_coverage"));

        response.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void FatalClaimReasonsRequireFailedRun() {
        var response = CreateResponse(CreateManifest());
        SetUnknown(response, WorkerClaimReason.BackendUnavailable);
        response.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        response.Summary = CreateSummary(response);

        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.fatal_claim"));

        response.RunStatus = WorkerRunStatus.Failed;
        response.FailureReason = WorkerRunFailureReason.BackendUnavailable;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void CallableFailureAndTimeoutReasonsConstrainRunStatus() {
        var response = CreateResponse(CreateManifest());
        response.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.InfrastructureFailure;

        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.fatal_callable"));

        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.MethodTimeout;
        SetUnknown(response, WorkerClaimReason.MethodTimeout);
        response.Summary = CreateSummary(response);
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.timeout_status"));

        response.RunStatus = WorkerRunStatus.TimedOut;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        response.RunStatus = WorkerRunStatus.Complete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        SetUnknown(response, WorkerClaimReason.Canceled);
        response.Summary = CreateSummary(response);
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.canceled_status"));

        response.RunStatus = WorkerRunStatus.Canceled;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void AssumptionSummaryUnionsDeclarationsAndUsageById() {
        var response = CreateResponse(CreateManifest());
        response.CallableResults[0].Assumptions = [
            new WorkerAssumptionEvidence {
                Id = "assume:0",
                Kind = WorkerAssumptionKind.UserAssume
            }
        ];
        response.ClaimResults[0].Assumptions = [
            new WorkerAssumptionEvidence {
                Id = "assume:0",
                Kind = WorkerAssumptionKind.UserAssume,
                Used = true
            }
        ];
        response.Summary = CreateSummary(response);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        response.ClaimResults[0].Assumptions[0].Kind =
            WorkerAssumptionKind.TrustedBoundary;
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("summary.assumption_conflict"));
    }

    [Test]
    public void NullPayloadElementsAreRejectedWithoutCanonicalizationCrashes() {
        var response = CreateResponse(CreateManifest());
        response.ClaimResults = [null!];
        response.CallableResults = [null!];
        response.Errors = [null!];

        Assert.DoesNotThrow(
            (Action)(() => WorkerProtocolJson.Canonicalize(response)));
        var codes = WorkerProtocolJson.Validate(response).Errors
            .Select(static error => error.Code).ToArray();
        using (Assert.EnterMultipleScope()) {
            Assert.That(codes, Does.Contain("response.claim_results"));
            Assert.That(codes, Does.Contain("response.callable_results"));
            Assert.That(codes, Does.Contain("response.errors"));
        }
    }

    [Test]
    public void SummaryAndOutcomePayloadMustMatchClaimResults() {
        var response = CreateResponse(CreateManifest());
        response.Summary.ClaimCount = 0;
        response.ClaimResults[0].Model = [
            new WorkerModelValue {
                Variable = "parameter:0",
                Kind = "Integer",
                Value = "1"
            }
        ];

        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("summary.totals")
                .And.Contain("response.claim_payload"));
    }

    [Test]
    public void ManifestRequiresDenseOrdinalsAndExactCallableMembership() {
        var manifest = CreateManifest();
        manifest.Claims[0].Ordinal = 2;
        manifest.Callables[0].ClaimIds = [];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);

        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("manifest.dense_ordinals")
                .And.Contain("manifest.claim_membership"));
    }

    private static WorkerVerifyRequest CreateRequest() =>
        new() {
            ProjectDirectory = "C:\\project",
            AssemblyName = "ProtocolTest",
            SourceFiles = ["Subject.cs"],
            ReferenceAssemblies = ["System.Runtime.dll"],
            DefineConstants = [],
            Compilation = new WorkerCompilationOptions {
                TargetFramework = "net8.0",
                LanguageVersion = "12.0",
                NullableContext = WorkerNullableContext.Enabled,
                Optimization = WorkerOptimizationLevel.Release,
                CheckOverflow = false,
                AllowUnsafe = false,
                Deterministic = true,
                OutputKind = WorkerOutputKind.DynamicallyLinkedLibrary,
                Platform = WorkerPlatform.AnyCpu
            }
        };

    private static WorkerClaimManifest CreateManifest() {
        var location = new WorkerSourceLocation {
            Path = "Subject.cs",
            Start = 10,
            Length = 20,
            Line = 2,
            Column = 5
        };
        var manifest = new WorkerClaimManifest {
            Callables = [
                new WorkerCallableManifestEntry {
                    CallableId = "M:Subject.Identity(System.Int64)",
                    SelectedFeatures = [WorkerSelectedFeature.Contracts],
                    SelectionReasons = [
                        WorkerSelectionReason.DiscoveredPostcondition
                    ],
                    Location = location,
                    ClaimIds = ["claim.identity.0"]
                }
            ],
            Claims = [
                new WorkerClaimManifestEntry {
                    ClaimId = "claim.identity.0",
                    CallableId = "M:Subject.Identity(System.Int64)",
                    Ordinal = 0,
                    Kind = WorkerClaimKind.Postcondition,
                    Evidence = WorkerClaimEvidence.DirectClause,
                    Location = new WorkerSourceLocation {
                        Path = location.Path,
                        Start = location.Start,
                        Length = location.Length,
                        Line = location.Line,
                        Column = location.Column
                    }
                }
            ]
        };
        WorkerProtocolJson.SealManifest(manifest);
        return manifest;
    }

    private static WorkerClaimManifest CreateBoundaryManifest(
        bool expandedFirst) {
        var manifest = new WorkerClaimManifest {
            Callables = [
                new WorkerCallableManifestEntry {
                    CallableId = "0",
                    SelectedFeatures = [WorkerSelectedFeature.Effects],
                    SelectionReasons = expandedFirst
                        ? [
                            WorkerSelectionReason.ExplicitAnnotation,
                            WorkerSelectionReason.DiscoveredPostcondition
                        ]
                        : [WorkerSelectionReason.ExplicitAnnotation],
                    Location = expandedFirst
                        ? new WorkerSourceLocation {
                            Path = "0",
                            Start = 0,
                            Length = 1,
                            Line = 1,
                            Column = 1
                        }
                        : new WorkerSourceLocation {
                            Path = "2",
                            Start = 0,
                            Length = 0,
                            Line = 1,
                            Column = 1
                        },
                    ClaimIds = ["1"]
                },
                new WorkerCallableManifestEntry {
                    CallableId = "1",
                    SelectedFeatures = [WorkerSelectedFeature.Effects],
                    SelectionReasons = expandedFirst
                        ? [WorkerSelectionReason.DiscoveredPostcondition]
                        : [
                            WorkerSelectionReason.ExplicitAnnotation,
                            WorkerSelectionReason.DiscoveredPostcondition
                        ],
                    Location = new WorkerSourceLocation {
                        Path = "p.cs",
                        Start = 0,
                        Length = 0,
                        Line = 1,
                        Column = 1
                    }
                }
            ],
            Claims = [
                new WorkerClaimManifestEntry {
                    ClaimId = "1",
                    CallableId = "0",
                    Ordinal = 0,
                    Kind = WorkerClaimKind.Postcondition,
                    Evidence = WorkerClaimEvidence.DirectClause,
                    Location = new WorkerSourceLocation {
                        Path = "q.cs",
                        Start = 0,
                        Length = 0,
                        Line = 1,
                        Column = 1
                    }
                }
            ]
        };
        WorkerProtocolJson.SealManifest(manifest);
        return manifest;
    }

    private static WorkerVerifyResponse CreateResponse(
        WorkerClaimManifest manifest) {
        var response = new WorkerVerifyResponse {
            InputHash = InputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [.. manifest.Callables.Select(
                static callable => new WorkerCallableResult {
                    CallableId = callable.CallableId,
                    Coverage = WorkerCallableCoverage.Complete,
                    Reason = WorkerCallableCoverageReason.None
                })],
            ClaimResults = [.. manifest.Claims.Select(
                static claim => new WorkerClaimResult {
                    ClaimId = claim.ClaimId,
                    Outcome = WorkerClaimOutcome.Proven,
                    Reason = WorkerClaimReason.None
                })]
        };
        response.Summary = CreateSummary(response);
        return response;
    }

    private static WorkerVerificationSummary CreateSummary(
        WorkerVerifyResponse response) {
        var assumptions = response.ClaimResults
            .Where(static claim => claim != null)
            .SelectMany(static claim => claim.Assumptions ?? [])
            .Concat(response.CallableResults
                .Where(static callable => callable != null)
                .SelectMany(static callable => callable.Assumptions ?? []))
            .Where(static assumption => assumption != null)
            .GroupBy(static assumption => assumption.Id, StringComparer.Ordinal)
            .ToArray();
        return new WorkerVerificationSummary {
            CallableCount = response.CallableResults.Count(
                static callable => callable != null),
            ClaimCount = response.ClaimResults.Count(
                static claim => claim != null),
            OutcomeCounts = [.. response.ClaimResults
                .Where(static claim => claim != null)
                .GroupBy(static claim => claim.Outcome)
                .OrderBy(static group => group.Key)
                .Select(static group => new WorkerClaimOutcomeCount {
                    Outcome = group.Key,
                    Count = group.Count()
                })],
            ReasonCounts = [.. response.ClaimResults
                .Where(static claim => claim != null)
                .GroupBy(static claim => claim.Reason)
                .OrderBy(static group => group.Key)
                .Select(static group => new WorkerClaimReasonCount {
                    Reason = group.Key,
                    Count = group.Count()
                })],
            Assumptions = new WorkerAssumptionSummary {
                Total = assumptions.Length,
                Used = assumptions.Count(static group =>
                    group.Any(static value => value.Used)),
                User = assumptions.Count(static group =>
                    group.First().Kind == WorkerAssumptionKind.UserAssume),
                Trusted = assumptions.Count(static group =>
                    group.First().Kind == WorkerAssumptionKind.TrustedBoundary)
            },
            CacheStatus = WorkerCacheStatus.Disabled,
            Versions = new WorkerVersionSummary {
                WorkerVersion = "test",
                ApiSpecVersion = "test"
            }
        };
    }

    private static void SetUnknown(
        WorkerVerifyResponse response,
        WorkerClaimReason reason) {
        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Unknown;
        response.ClaimResults[0].Reason = reason;
    }
}
