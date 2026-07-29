using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ProtocolJsonTests
{
    private const string InputHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string[] s_requestProperties = [
        "protocolVersion", "compilerManifest", "budgets", "cache", "verifyPolicy", "assumptionPolicy"
    ];
    private static readonly WorkerAssumptionKind[] s_assumptionKinds = [
        WorkerAssumptionKind.UserAssume,
        WorkerAssumptionKind.TrustedBoundary
    ];

    [Test]
    public void VersionEightRequestCarriesOnlyArtifactAndRuntimeControls()
    {
        var request = CreateRequest();
        var json = WorkerProtocolJson.SerializeRequest(request);
        var roundTrip = WorkerProtocolJson.DeserializeRequest(json)!;
        using var document = JsonDocument.Parse(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolVersions.Current, Is.EqualTo("8"));
            Assert.That(WorkerCacheVersions.Current, Is.EqualTo(9));
            Assert.That(WorkerManifestVersions.Current, Is.EqualTo(4));
            Assert.That(
                document.RootElement.EnumerateObject()
                    .Select(static property => property.Name),
                Is.EqualTo(s_requestProperties));
            Assert.That(roundTrip.CompilerManifest.Path, Is.EqualTo("compiler.manifest.json"));
            Assert.That(roundTrip.CompilerManifest.Sha256, Is.EqualTo(InputHash));
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
        request.CompilerManifest.Sha256 = InputHash.ToUpperInvariant();
        Assert.That(
            WorkerProtocolJson.Validate(request).Errors
                .Select(static error => error.Code),
            Does.Contain("policy.verify")
                .And.Contain("policy.assumption")
                .And.Contain("project.compiler_manifest"));
    }

    [Test]
    public void DeserializationRejectsDuplicatePropertiesAtEveryDepth()
    {
        var requestJson = WorkerProtocolJson.SerializeRequest(CreateRequest());
        var nestedRequestDuplicate = requestJson.Replace(
            "\"path\":\"compiler.manifest.json\"",
            "\"path\":\"compiler.manifest.json\"," +
            "\"path\":\"compiler.manifest.json\"",
            StringComparison.Ordinal);

        var responseJson = WorkerProtocolJson.SerializeResponse(
            CreateResponse(CreateManifest()));
        var nestedArrayObjectDuplicate = responseJson.Replace(
            "\"path\":\"Subject.cs\"",
            "\"path\":\"Subject.cs\",\"path\":\"Subject.cs\"",
            StringComparison.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<JsonException>((Action)(() =>
                WorkerProtocolJson.DeserializeRequest(
                    nestedRequestDuplicate)));
            Assert.Throws<JsonException>((Action)(() =>
                WorkerProtocolJson.DeserializeResponse(
                    nestedArrayObjectDuplicate)));
        }
    }

    [Test]
    public void CompilerManifestArtifactIsCanonicalAndCarriesAssumptions()
    {
        var compilation = CreateCompilation();
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var manifest = discovery.Manifest;
        var artifact = CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net9.0",
            WorkerFeatureSet.All,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);
        var json = CompilerManifestArtifactJson.Serialize(artifact);
        var roundTrip = CompilerManifestArtifactJson.Deserialize(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(roundTrip.SchemaVersion, Is.EqualTo(6));
            Assert.That(roundTrip.ProtocolVersion, Is.EqualTo("8"));
            Assert.That(roundTrip.Manifest.Hash, Is.EqualTo(manifest.Hash));
            Assert.That(roundTrip.Manifest.Callables[0].Assumptions, Has.Length.EqualTo(2));
            Assert.That(
                roundTrip.Manifest.Callables[0].Assumptions
                    .Select(static assumption => assumption.Kind),
                Is.EqualTo(s_assumptionKinds));
            Assert.That(roundTrip.Compilation.SyntaxTrees, Has.Length.EqualTo(1));
            Assert.That(
                roundTrip.Compilation.SyntaxTrees[0].Sha256,
                Does.Match("^[0-9a-f]{64}$"));
            Assert.That(roundTrip.CompilationSha256, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(WorkerProtocolJson.ManifestsEqual(
                roundTrip.Manifest, manifest), Is.True);
        }
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(json.Replace(
                "\"schemaVersion\":4",
                "\"schemaVersion\":4,\"schemaVersion\":4",
                StringComparison.Ordinal))));
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(json.Replace(
                "\"features\":\"All\"",
                "\"features\":1",
                StringComparison.Ordinal))));

        roundTrip.Manifest.Claims[0].Location.Column++;
        Assert.That(WorkerProtocolJson.ManifestsEqual(
            roundTrip.Manifest, manifest), Is.False);
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(roundTrip))));
    }

    [Test]
    public void ManifestHashIsCanonicalAndCoversEveryField()
    {
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
    public void ManifestHashUsesStableNamedEnumIdentities()
    {
        var forward = CreateManifest();
        forward.Callables[0].SelectedFeatures = [
            WorkerSelectedFeature.Effects,
            WorkerSelectedFeature.Contracts
        ];
        forward.Callables[0].SelectionReasons = [
            WorkerSelectionReason.ExplicitAnnotation,
            WorkerSelectionReason.DiscoveredPostcondition
        ];
        WorkerProtocolJson.SealManifest(forward);

        var reverse = CreateManifest();
        reverse.Callables[0].SelectedFeatures = [
            WorkerSelectedFeature.Contracts,
            WorkerSelectedFeature.Effects
        ];
        reverse.Callables[0].SelectionReasons = [
            WorkerSelectionReason.DiscoveredPostcondition,
            WorkerSelectionReason.ExplicitAnnotation
        ];
        reverse.Callables[0].Assumptions =
            [.. reverse.Callables[0].Assumptions.Reverse()];
        WorkerProtocolJson.SealManifest(reverse);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reverse.Hash, Is.EqualTo(forward.Hash));
            Assert.That(
                forward.Hash,
                Is.EqualTo(
                    "5ac4df9ec5bec9ba006ab877dda2ea3c" +
                    "185ef76eea7743f2322b353399599b59"));
        }
    }

    [Test]
    public void ManifestHashIsSensitiveToEveryManifestEnum()
    {
        var hashes = new HashSet<string>(StringComparer.Ordinal) {
            CreateManifest().Hash,
            ManifestHashAfter(static manifest =>
                manifest.Callables[0].SelectedFeatures =
                    [WorkerSelectedFeature.Effects]),
            ManifestHashAfter(static manifest =>
                manifest.Callables[0].SelectionReasons =
                    [WorkerSelectionReason.ExplicitAnnotation]),
            ManifestHashAfter(static manifest =>
                manifest.Callables[0].Assumptions[0].Kind =
                    WorkerAssumptionKind.Precondition),
            ManifestHashAfter(static manifest =>
                manifest.Claims[0].Kind = WorkerClaimKind.Effect),
            ManifestHashAfter(static manifest =>
                manifest.Claims[0].Evidence =
                    WorkerClaimEvidence.CompanionClause),
            ManifestHashAfter(static manifest =>
                manifest.Claims[0].EffectContractKind =
                    WorkerEffectContractKind.DoesNotThrow)
        };

        Assert.That(hashes, Has.Count.EqualTo(7));
    }

    [Test]
    public void ManifestHashSeparatesFormerlyAmbiguousCollectionBoundaries()
    {
        var responseManifest = CreateBoundaryManifest(expandedFirst: false);
        var expectedManifest = CreateBoundaryManifest(expandedFirst: true);
        var response = CreateResponse(responseManifest);

        using (Assert.EnterMultipleScope())
        {
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
    public void EffectClaimShapeIsClosedAndPartOfManifestIdentity()
    {
        var manifest = CreateManifest();
        var postconditionHash = manifest.Hash;
        manifest.Callables[0].SelectedFeatures = [WorkerSelectedFeature.Effects];
        manifest.Callables[0].SelectionReasons = [
            WorkerSelectionReason.ExplicitAnnotation
        ];
        manifest.Claims[0].Kind = WorkerClaimKind.Effect;
        manifest.Claims[0].Evidence = WorkerClaimEvidence.Attribute;
        manifest.Claims[0].EffectContractKind =
            WorkerEffectContractKind.DoesNotThrow;
        WorkerProtocolJson.SealManifest(manifest);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolJson.ValidateManifest(manifest).IsValid,
                Is.True);
            Assert.That(manifest.Hash, Is.Not.EqualTo(postconditionHash));
        }
        manifest.Claims[0].Evidence = WorkerClaimEvidence.DirectClause;
        WorkerProtocolJson.SealManifest(manifest);
        Assert.That(
            WorkerProtocolJson.ValidateManifest(manifest).Errors
                .Select(static error => error.Code),
            Does.Contain("manifest.claim_shape"));

        manifest.Claims[0].Kind = WorkerClaimKind.Postcondition;
        manifest.Claims[0].Evidence = WorkerClaimEvidence.DirectClause;
        WorkerProtocolJson.SealManifest(manifest);
        Assert.That(
            WorkerProtocolJson.ValidateManifest(manifest).Errors
                .Select(static error => error.Code),
            Does.Contain("manifest.claim_shape"));
    }

    [Test]
    public void EffectCertaintyMustAgreeWithOutcomeAndUnknownReason()
    {
        var manifest = CreateManifest();
        manifest.Callables[0].SelectedFeatures = [WorkerSelectedFeature.Effects];
        manifest.Callables[0].SelectionReasons = [
            WorkerSelectionReason.ExplicitAnnotation
        ];
        manifest.Claims[0].Kind = WorkerClaimKind.Effect;
        manifest.Claims[0].Evidence = WorkerClaimEvidence.Attribute;
        manifest.Claims[0].EffectContractKind =
            WorkerEffectContractKind.DoesNotThrow;
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);

        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Refuted;
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.DefiniteViolation;
        response.ClaimResults[0].EffectWitness =
            CreateEffectWitness(manifest.Claims[0].Location);
        response.Summary = CreateSummary(response);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        response.ClaimResults[0].EffectWitness!.Effects =
            WorkerEffectSet.Allocates;
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.effect_witness"));
        response.ClaimResults[0].EffectWitness = null;
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.effect_witness"));
        response.ClaimResults[0].EffectWitness =
            CreateEffectWitness(manifest.Claims[0].Location);
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.effect_certainty"));
        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Proven;
        response.ClaimResults[0].EffectWitness = null;
        response.Summary = CreateSummary(response);

        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary;
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.effect_certainty"));

        SetUnknown(response, WorkerClaimReason.EffectSummaryIncomplete);
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary;
        response.CallableResults[0].Coverage =
            WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        response.Summary = CreateSummary(response);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        response.ClaimResults[0].Reason =
            WorkerClaimReason.EffectContractNotEstablished;
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.effect_certainty"));
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;
        response.Summary = CreateSummary(response);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void VacuityEvidenceIsLimitedToProvenPostconditions()
    {
        var response = CreateResponse(CreateManifest());
        response.ClaimResults[0].Vacuity =
            WorkerVacuityKind.ContradictoryPreconditions;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        SetUnknown(response, WorkerClaimReason.UnsupportedBody);
        response.CallableResults[0].Coverage =
            WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        response.Summary = CreateSummary(response);
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.vacuity"));
    }

    [Test]
    public void StrictResponseValidationRequiresExactManifestAndResultSets()
    {
        var expected = CreateManifest();
        var response = CreateResponse(expected);
        var request = CreateRequest();
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
        Assert.That(
            WorkerProtocolJson.Validate(response, InputHash, expected).IsValid,
            Is.True);
        Assert.That(WorkerProtocolJson.ValidateForRequest(
            response, response.RequestHash, InputHash, expected,
            request.Budgets).IsValid, Is.True);
        request.VerifyPolicy = WorkerVerifyPolicy.WarnOnUnknown;
        Assert.That(WorkerProtocolJson.ValidateForRequest(
                response, WorkerProtocolJson.ComputeRequestHash(request),
                InputHash, expected, request.Budgets)
            .Errors.Select(static error => error.Code),
            Does.Contain("response.request_mismatch"));

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

    [TestCase(nameof(WorkerBudgets.QueryRlimit))]
    [TestCase(nameof(WorkerBudgets.MethodRlimit))]
    [TestCase(nameof(WorkerBudgets.MethodWallTimeMilliseconds))]
    [TestCase(nameof(WorkerBudgets.ProjectWallTimeMilliseconds))]
    [TestCase(nameof(WorkerBudgets.MaxParallelism))]
    [TestCase(nameof(WorkerBudgets.MaximumExpressionDepth))]
    [TestCase(nameof(WorkerBudgets.ProcessMemoryLimitBytes))]
    [TestCase(nameof(WorkerBudgets.MaxWorkerProcesses))]
    public void RequestValidationBindsEverySummaryBudget(string propertyName)
    {
        var request = CreateRequest();
        var response = CreateResponse(CreateManifest());
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
        var property = typeof(WorkerBudgets).GetProperty(propertyName)!;
        var value = Convert.ToInt64(
            property.GetValue(response.Summary.Budgets),
            CultureInfo.InvariantCulture);
        property.SetValue(
            response.Summary.Budgets,
            Convert.ChangeType(
                value + 1, property.PropertyType,
                CultureInfo.InvariantCulture));

        Assert.That(
            WorkerProtocolJson.ValidateForRequest(
                    response, response.RequestHash, InputHash,
                    response.Manifest, request.Budgets)
                .Errors.Select(static error => error.Code),
            Does.Contain("response.budgets_mismatch"));
    }

    [Test]
    public void OmittedOrNumericClaimOutcomeCannotBecomeProven()
    {
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
    public void UnknownClaimsRequireIncompleteCallableCoverage()
    {
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
    public void FatalClaimReasonsRequireFailedRun()
    {
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
    public void CallableFailureAndTimeoutReasonsConstrainRunStatus()
    {
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
    public void AssumptionSummaryUnionsDeclarationsAndUsageById()
    {
        var response = CreateResponse(CreateManifest());
        var used = response.ClaimResults[0].Assumptions.Single(
            static assumption =>
                assumption.Kind == WorkerAssumptionKind.UserAssume);
        used.Used = true;
        response.Summary = CreateSummary(response);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        used.Kind = WorkerAssumptionKind.TrustedBoundary;
        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("summary.assumption_conflict"));
    }

    [Test]
    public void ClaimResultsRequireOwningCallableAssumptionDeclarations()
    {
        var response = CreateResponse(CreateManifest());
        response.ClaimResults[0].Assumptions =
            [.. response.ClaimResults[0].Assumptions.Skip(1)];
        response.Summary = CreateSummary(response);

        Assert.That(
            WorkerProtocolJson.Validate(response).Errors
                .Select(static error => error.Code),
            Does.Contain("response.claim_assumption_set"));
    }

    [Test]
    public void NullPayloadElementsAreRejectedWithoutCanonicalizationCrashes()
    {
        var response = CreateResponse(CreateManifest());
        response.ClaimResults = [null!];
        response.CallableResults = [null!];
        response.Errors = [null!];

        Assert.DoesNotThrow(
            (Action)(() => WorkerProtocolJson.Canonicalize(response)));
        var codes = WorkerProtocolJson.Validate(response).Errors
            .Select(static error => error.Code).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(codes, Does.Contain("response.claim_results"));
            Assert.That(codes, Does.Contain("response.callable_results"));
            Assert.That(codes, Does.Contain("response.errors"));
        }
    }

    [Test]
    public void SummaryAndOutcomePayloadMustMatchClaimResults()
    {
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
    public void ManifestRequiresDenseOrdinalsAndExactCallableMembership()
    {
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

    private static WorkerVerifyRequest CreateRequest()
    {
        return new()
        {
            CompilerManifest = new WorkerFileReference
            {
                Path = "compiler.manifest.json",
                Sha256 = InputHash
            }
        };
    }

    private static CSharpCompilation CreateCompilation()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "ProtocolSubject.cs");
        var tree = CSharpSyntaxTree.ParseText(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [SharpProofTrusted("reviewed boundary")]
                public static long Identity(long value) {
                    Contract.Assume(value >= 0);
                    Contract.Ensures(Contract.Result<long>() >= 0);
                    return value;
                }
            }
            """,
            new CSharpParseOptions(
                LanguageVersion.CSharp12,
                preprocessorSymbols: [Contract.ConditionalSymbol]),
            path);
        var trusted = ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);
        var required = new HashSet<string>(
            [
                "System.Private.CoreLib.dll",
                "System.Linq.dll",
                "System.Runtime.dll",
                "netstandard.dll"
            ],
            StringComparer.OrdinalIgnoreCase);
        var references = trusted
            .Where(item => required.Contains(Path.GetFileName(item)))
            .Append(typeof(Contract).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static item => MetadataReference.CreateFromFile(item));
        return CSharpCompilation.Create(
            "ProtocolTest",
            [tree],
            references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                deterministic: true,
                concurrentBuild: false));
    }

    private static WorkerClaimManifest CreateManifest()
    {
        var location = new WorkerSourceLocation
        {
            Path = "Subject.cs",
            Start = 10,
            Length = 20,
            Line = 2,
            Column = 5
        };
        var manifest = new WorkerClaimManifest
        {
            Callables = [
                new WorkerCallableManifestEntry {
                    CallableId = "M:Subject.Identity(System.Int64)",
                    SelectedFeatures = [WorkerSelectedFeature.Contracts],
                    SelectionReasons = [
                        WorkerSelectionReason.DiscoveredPostcondition
                    ],
                    Location = location,
                    ClaimIds = ["claim.identity.0"],
                    Assumptions = [
                        new WorkerAssumptionEvidence {
                            Id = "spa1:1",
                            Kind = WorkerAssumptionKind.UserAssume
                        },
                        new WorkerAssumptionEvidence {
                            Id = "spa1:2",
                            Kind = WorkerAssumptionKind.TrustedBoundary
                        }
                    ]
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

    private static string ManifestHashAfter(
        Action<WorkerClaimManifest> mutation)
    {
        var manifest = CreateManifest();
        mutation(manifest);
        WorkerProtocolJson.SealManifest(manifest);
        return manifest.Hash;
    }

    private static WorkerClaimManifest CreateBoundaryManifest(
        bool expandedFirst)
    {
        var manifest = new WorkerClaimManifest
        {
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
        WorkerClaimManifest manifest)
    {
        var response = new WorkerVerifyResponse
        {
            InputHash = InputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [.. manifest.Callables.Select(
                static callable => new WorkerCallableResult {
                    CallableId = callable.CallableId,
                    Coverage = WorkerCallableCoverage.Complete,
                    Reason = WorkerCallableCoverageReason.None,
                    Assumptions = CopyAssumptions(callable.Assumptions)
                })],
            ClaimResults = [.. manifest.Claims.Select(
                claim => new WorkerClaimResult {
                    ClaimId = claim.ClaimId,
                    Outcome = WorkerClaimOutcome.Proven,
                    Reason = WorkerClaimReason.None,
                    EffectCertainty = claim.Kind == WorkerClaimKind.Effect
                        ? WorkerEffectEvidenceCertainty.CompleteMayEffectSummary
                        : WorkerEffectEvidenceCertainty.Unspecified,
                    Assumptions = CopyAssumptions(
                        manifest.Callables.Single(callable =>
                            callable.CallableId == claim.CallableId)
                            .Assumptions)
                })]
        };
        response.Summary = CreateSummary(response);
        return response;
    }

    private static WorkerAssumptionEvidence[] CopyAssumptions(
        IEnumerable<WorkerAssumptionEvidence> assumptions)
    {
        return [.. assumptions.Select(static assumption =>
            new WorkerAssumptionEvidence {
                Id = assumption.Id,
                Kind = assumption.Kind,
                Used = assumption.Used
            })];
    }

    private static WorkerVerificationSummary CreateSummary(
        WorkerVerifyResponse response)
    {
        var assumptions = response.ClaimResults
            .Where(static claim => claim != null)
            .SelectMany(static claim => claim.Assumptions ?? [])
            .Concat(response.CallableResults
                .Where(static callable => callable != null)
                .SelectMany(static callable => callable.Assumptions ?? []))
            .Where(static assumption => assumption != null)
            .GroupBy(static assumption => assumption.Id, StringComparer.Ordinal)
            .ToArray();
        return new WorkerVerificationSummary
        {
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
            Assumptions = new WorkerAssumptionSummary
            {
                Total = assumptions.Length,
                Used = assumptions.Count(static group =>
                    group.Any(static value => value.Used)),
                User = assumptions.Count(static group =>
                    group.First().Kind == WorkerAssumptionKind.UserAssume),
                Trusted = assumptions.Count(static group =>
                    group.First().Kind == WorkerAssumptionKind.TrustedBoundary)
            },
            CacheStatus = WorkerCacheStatus.Disabled,
            Versions = new WorkerVersionSummary
            {
                WorkerVersion = "test",
                ApiSpecVersion = "test"
            }
        };
    }

    private static void SetUnknown(
        WorkerVerifyResponse response,
        WorkerClaimReason reason)
    {
        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Unknown;
        response.ClaimResults[0].Reason = reason;
    }

    private static WorkerEffectViolationWitness CreateEffectWitness(
        WorkerSourceLocation location)
    {
        return new()
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
                Path = location.Path,
                Start = location.Start,
                Length = location.Length,
                Line = location.Line,
                Column = location.Column
            }
        };
    }
}
