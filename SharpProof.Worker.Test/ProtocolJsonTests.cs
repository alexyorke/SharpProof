using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private static IEnumerable<TestCaseData> ProtocolScalingCases()
    {
        yield return new TestCaseData(
            (Func<WorkerVerifyResponse, TimeSpan>)MeasureValidation,
            1024,
            8192).SetName("ValidResponseValidationDoesNotRescanManifestRows");
        yield return new TestCaseData(
            (Func<WorkerVerifyResponse, TimeSpan>)MeasureCanonicalization,
            512,
            4096).SetName("ProtocolCanonicalizationDoesNotRescanManifestRows");
    }

    [TestCaseSource(nameof(ProtocolScalingCases))]
    public void ProtocolOperationDoesNotRescanManifestRows(
        Func<WorkerVerifyResponse, TimeSpan> measure,
        int smallSize,
        int largeSize)
    {
        ArgumentNullException.ThrowIfNull(measure);
        _ = measure(CreateValidationScalingResponse(4));
        var small = measure(
            CreateValidationScalingResponse(smallSize));
        var large = measure(
            CreateValidationScalingResponse(largeSize));
        var maximumLarge = small * 16 + TimeSpan.FromMilliseconds(250);

        Assert.That(
            large,
            Is.LessThanOrEqualTo(maximumLarge),
            $"small={small.TotalMilliseconds:F0} ms, " +
            $"large={large.TotalMilliseconds:F0} ms");
    }

    [Test]
    public void ProtocolSerializersRejectDocumentsBeyondReaderLimit()
    {
        var oversizedValue = new string(
            'x',
            WorkerProtocolJson.MaximumJsonBytes);
        var request = CreateRequest();
        request.CompilerManifest.Path = oversizedValue;
        var response = CreateResponse(CreateManifest());
        response.ClaimResults[0].ProofCore = [oversizedValue];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolJson.Validate(request).IsValid, Is.True);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
            Assert.That(
                (Action)(() => WorkerProtocolJson.SerializeRequest(request)),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                (Action)(() => WorkerProtocolJson.SerializeResponse(response)),
                Throws.TypeOf<InvalidDataException>());
        }
    }

    [Test]
    public void ProtocolDeserializersRejectLoneUtf16Surrogates()
    {
        var json = WorkerProtocolJson.SerializeRequest(CreateRequest())
            .Replace("compiler.manifest.json", "\\uD800", StringComparison.Ordinal);

        Assert.That(
            (Action)(() => WorkerProtocolJson.DeserializeRequest(json)),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void BoundedUtf8FileReaderRejectsOversizedAndInvalidFiles()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "protocol-json-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllBytes(path, new byte[WorkerProtocolJson.MaximumJsonBytes + 1]);

            Assert.Throws<InvalidDataException>(
                (Action)(() => WorkerProtocolJson.ReadUtf8File(path)));
            Func<Task> readAsync = () => WorkerProtocolJson.ReadUtf8FileAsync(path);
            Assert.ThrowsAsync<InvalidDataException>(readAsync);

            File.WriteAllBytes(path, [0xff]);
            Assert.Throws<DecoderFallbackException>(
                (Action)(() => WorkerProtocolJson.ReadUtf8File(path)));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Test]
    [Platform("Linux")]
    public void BoundedUtf8FileReaderRejectsGrowthAfterOpen()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "protocol-json-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllBytes(
                path,
                new byte[WorkerProtocolJson.MaximumJsonBytes]);

            using (var reader = OpenReader())
            {
                AppendByte();
                Assert.Throws<InvalidDataException>(
                    (Action)(() => reader.ReadToEnd()));
            }

            File.WriteAllBytes(
                path,
                new byte[WorkerProtocolJson.MaximumJsonBytes]);
            using (var reader = OpenReader())
            {
                AppendByte();
                Func<Task> readAsync = async () =>
                    await reader.ReadToEndAsync();
                Assert.ThrowsAsync<InvalidDataException>(
                    readAsync);
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        StreamReader OpenReader()
        {
            var method = typeof(WorkerProtocolJson).GetMethod(
                "OpenJsonReader",
                System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Static)!;
            return (StreamReader)method.Invoke(null, [path])!;
        }

        void AppendByte()
        {
            using var writer = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite);
            writer.WriteByte((byte)' ');
            writer.Flush(flushToDisk: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void BoundedUtf8FileReaderRejectsFifoBeforeBlockingOpen()
    {
        var path = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "protocol-json-" + Guid.NewGuid().ToString("N") + ".fifo");
        try
        {
            using (var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "mkfifo",
                    UseShellExecute = false,
                    ArgumentList = { path }
                })!)
            {
                process.WaitForExit();
                Assert.That(process.ExitCode, Is.Zero);
            }

            var read = Task.Run(() => WorkerProtocolJson.ReadUtf8File(path));
            var completed = Task.WhenAny(read, Task.Delay(500))
                .GetAwaiter()
                .GetResult();
            if (!ReferenceEquals(completed, read))
            {
                using (var writer = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.ReadWrite))
                {
                    writer.WriteByte((byte)'{');
                }

                var unblocked = Task.WhenAny(read, Task.Delay(5000))
                    .GetAwaiter()
                    .GetResult();
                Assert.That(unblocked, Is.SameAs(read));
                _ = read.Exception;
            }

            Assert.That(
                completed,
                Is.SameAs(read),
                "Opening a FIFO must not wait for a writer.");
            Assert.That(
                (Action)(() => _ = read.GetAwaiter().GetResult()),
                Throws.TypeOf<InvalidDataException>());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void VersionNineRequestCarriesOnlyArtifactAndRuntimeControls()
    {
        var request = CreateRequest();
        var json = WorkerProtocolJson.SerializeRequest(request);
        var roundTrip = WorkerProtocolJson.DeserializeRequest(json)!;
        using var document = JsonDocument.Parse(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolVersions.Current, Is.EqualTo("11"));
            Assert.That(WorkerCacheVersions.Current, Is.EqualTo(13));
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
        AssertErrorCode(
            WorkerProtocolJson.Validate(request),
            "policy.verify",
            "policy.assumption",
            "project.compiler_manifest");
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
    public void DeserializationRejectsDocumentsBeyondTheDeclaredDepth()
    {
        const int expectedMaximumDepth = 32;
        var prefix = string.Concat(Enumerable.Repeat(
            "{\"nested\":",
            expectedMaximumDepth + 1));
        var json = prefix + "0" + new string(
            '}',
            expectedMaximumDepth + 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WorkerProtocolJson.Options.MaxDepth,
                Is.EqualTo(expectedMaximumDepth));
            Assert.That(
                (Action)(() => WorkerProtocolJson.DeserializeRequest(json)),
                Throws.InstanceOf<JsonException>());
            Assert.That(
                (Action)(() =>
                    CompilerManifestArtifactJson.Deserialize(json)),
                Throws.InstanceOf<JsonException>());
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
            Assert.That(
                roundTrip.SchemaVersion,
                Is.EqualTo(CompilerManifestArtifactVersions.Current));
            Assert.That(roundTrip.ProtocolVersion, Is.EqualTo("11"));
            Assert.That(roundTrip.Manifest.Hash, Is.EqualTo(manifest.Hash));
            Assert.That(roundTrip.Manifest.Callables[0].Assumptions, Has.Length.EqualTo(2));
            Assert.That(
                roundTrip.Manifest.Callables[0].Assumptions
                    .Select(static assumption => assumption.Kind),
                Is.EqualTo(WorkerTestData.UserAndTrustedAssumptions));
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
        AssertErrorCode(
            WorkerProtocolJson.Validate(CreateResponse(manifest)),
            "manifest.hash");
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
            AssertErrorCode(WorkerProtocolJson.Validate(
                        response,
                        InputHash,
                        expectedManifest)
                    , "response.manifest_mismatch");
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
        AssertErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            "manifest.claim_shape");

        manifest.Claims[0].Kind = WorkerClaimKind.Postcondition;
        manifest.Claims[0].Evidence = WorkerClaimEvidence.DirectClause;
        WorkerProtocolJson.SealManifest(manifest);
        AssertErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            "manifest.claim_shape");
    }

    [Test]
    public void EffectCertaintyMustAgreeWithOutcomeAndUnknownReason()
    {
        var manifest = CreateEffectManifest();
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
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.effect_witness");
        response.ClaimResults[0].EffectWitness = null;
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.effect_witness");
        response.ClaimResults[0].EffectWitness =
            CreateEffectWitness(manifest.Claims[0].Location);
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.effect_certainty");
        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Proven;
        response.ClaimResults[0].EffectWitness = null;
        response.Summary = CreateSummary(response);

        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary;
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.effect_certainty");

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
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.effect_certainty");
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;
        response.Summary = CreateSummary(response);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void ResourceLimitIncompleteEffectTupleIsAProtocolState()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WorkerProtocolJson.HasValidEffectCertainty(
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimReason.ResourceLimit,
                    WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary),
                Is.True);
            Assert.That(
                WorkerProtocolJson.HasValidEffectCertainty(
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimReason.ResourceLimit,
                    WorkerEffectEvidenceCertainty.TrustedCompleteBoundary),
                Is.True);
            Assert.That(
                WorkerProtocolJson.HasValidEffectCertainty(
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimReason.ResourceLimit,
                    WorkerEffectEvidenceCertainty.CompleteMayEffectSummary),
                Is.False);
            Assert.That(
                WorkerProtocolJson.HasValidEffectCertainty(
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimReason.ResourceLimit,
                    WorkerEffectEvidenceCertainty.DefiniteViolation),
                Is.False);
        }
    }

    [TestCase(
        WorkerClaimReason.EffectSummaryIncomplete,
        WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)]
    [TestCase(
        WorkerClaimReason.EffectContractNotEstablished,
        WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)]
    [TestCase(
        WorkerClaimReason.ResourceLimit,
        WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary)]
    [TestCase(
        WorkerClaimReason.ResourceLimit,
        WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)]
    [TestCase(
        WorkerClaimReason.UnsupportedBody,
        WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary)]
    [TestCase(
        WorkerClaimReason.UnsupportedBody,
        WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)]
    public void AcceptedUnknownEffectTuplePassesFullResponseValidation(
        WorkerClaimReason reason,
        WorkerEffectEvidenceCertainty certainty)
    {
        var response = CreateResponse(CreateEffectManifest());
        SetUnknown(response, reason);
        response.ClaimResults[0].EffectCertainty = certainty;
        response.CallableResults[0].Coverage =
            WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        if (certainty == WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)
        {
            response.ClaimResults[0].Assumptions.Single(static assumption =>
                assumption.Kind == WorkerAssumptionKind.TrustedBoundary).Used = true;
        }
        response.Summary = CreateSummary(response);

        var validation = WorkerProtocolJson.Validate(response);

        Assert.That(
            validation.Errors.Select(static error => error.Code),
            Is.Empty);
    }

    [Test]
    public void VacuityEvidenceIsClosedAndRequiresProofCore()
    {
        var response = CreateResponse(CreateManifest());
        response.ClaimResults[0].Vacuity =
            WorkerVacuityKind.ContradictoryPreconditions;
        response.ClaimResults[0].ProofCore = ["requires:0"];
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        SetUnknown(response, WorkerClaimReason.UnsupportedBody);
        response.ClaimResults[0].ProofCore = [];
        response.CallableResults[0].Coverage =
            WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        response.Summary = CreateSummary(response);
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.vacuity");

        var effectManifest = CreateEffectManifest();
        response = CreateResponse(effectManifest);
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.VacuousEntry;
        response.ClaimResults[0].Vacuity =
            WorkerVacuityKind.ContradictoryPreconditions;
        response.ClaimResults[0].ProofCore = ["requires:0"];
        Assert.That(
            WorkerProtocolJson.Validate(response).IsValid,
            Is.True);

        response.ClaimResults[0].ProofCore = [];
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.vacuity_evidence");

        response.ClaimResults[0].ProofCore = [
            "body:normal-completion"
        ];
        response.ClaimResults[0].Vacuity =
            WorkerVacuityKind.NoModeledNormalReturn;
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.vacuity");
    }

    [Test]
    public void EffectEvidenceTupleRequiresVacuousEntryContradictionAndCore()
    {
        var manifest = CreateEffectManifest();
        var response = CreateResponse(manifest);
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.VacuousEntry;
        response.ClaimResults[0].Vacuity = WorkerVacuityKind.None;
        response.ClaimResults[0].ProofCore = [];
        response.Summary = CreateSummary(response);

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.effect_evidence");

        response.ClaimResults[0].Vacuity =
            WorkerVacuityKind.ContradictoryPreconditions;
        response.ClaimResults[0].ProofCore = ["requires:0"];
        response.Summary = CreateSummary(response);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void EffectEvidenceTupleRejectsNonVacuousContradiction()
    {
        var response = CreateResponse(CreateEffectManifest());
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;
        response.ClaimResults[0].Vacuity =
            WorkerVacuityKind.ContradictoryPreconditions;
        response.ClaimResults[0].ProofCore = ["requires:0"];
        response.Summary = CreateSummary(response);

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.effect_evidence");
    }

    [Test]
    public void TrustedEffectEvidenceRequiresUsedTrustedBoundary()
    {
        var response = CreateResponse(CreateEffectManifest());
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.TrustedCompleteBoundary;
        response.Summary = CreateSummary(response);

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.effect_evidence");

        foreach (var assumption in response.ClaimResults
                     .SelectMany(static result => result.Assumptions)
                     .Where(static value =>
                         value.Kind == WorkerAssumptionKind.TrustedBoundary))
        {
            assumption.Used = true;
        }
        response.Summary = CreateSummary(response);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void TrustedEffectEvidenceRequiresTrustedBoundaryDeclaration()
    {
        var manifest = CreateEffectManifest();
        manifest.Callables[0].Assumptions = [
            new WorkerAssumptionEvidence {
                Id = "spa1:1",
                Kind = WorkerAssumptionKind.UserAssume
            }
        ];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.TrustedCompleteBoundary;
        response.Summary = CreateSummary(response);

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.effect_evidence");
    }

    [Test]
    public void CallableAssumptionUsageMustRemainClaimAuthoritative()
    {
        var response = CreateResponse(CreateManifest());
        response.CallableResults[0].Assumptions[0].Used = true;
        response.Summary = CreateSummary(response);

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.callable_assumption_usage");
    }

    [Test]
    public void AssumptionIdentityIsGlobalAcrossCallables()
    {
        var manifest = CreateTwoCallableManifest();
        manifest.Callables[1].Assumptions[0].Id =
            manifest.Callables[0].Assumptions[0].Id;
        WorkerProtocolJson.SealManifest(manifest);

        AssertErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            "manifest.assumption_identity");
    }

    [Test]
    public void AssumptionIdentityRejectsKindMutationAcrossCallables()
    {
        var manifest = CreateTwoCallableManifest();
        manifest.Callables[1].Assumptions[0].Id =
            manifest.Callables[0].Assumptions[0].Id;
        manifest.Callables[1].Assumptions[0].Kind =
            WorkerAssumptionKind.TrustedBoundary;
        WorkerProtocolJson.SealManifest(manifest);

        AssertErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            "manifest.assumption_identity");
    }

    [Test]
    public void ManifestAssumptionKindMustRemainProducerClosed()
    {
        var manifest = CreateManifest();
        manifest.Callables[0].Assumptions[0].Kind =
            WorkerAssumptionKind.ApiSpecification;
        WorkerProtocolJson.SealManifest(manifest);

        AssertContainsErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            "manifest.assumption_kind");
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
            request, CreateExpectedVersions()).IsValid, Is.True);
        request.VerifyPolicy = WorkerVerifyPolicy.WarnOnUnknown;
        AssertErrorCode(WorkerProtocolJson.ValidateForRequest(
                response, WorkerProtocolJson.ComputeRequestHash(request),
                InputHash, expected, request,
                CreateExpectedVersions())
            , "response.request_mismatch");

        response.ClaimResults = [];
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response, InputHash, expected),
            "response.claim_set");

        response = CreateResponse(expected);
        response.ClaimResults = [
            response.ClaimResults[0],
            response.ClaimResults[0]
        ];
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response, InputHash, expected),
            "response.result_claim_id");

        response = CreateResponse(expected);
        var other = CreateManifest();
        other.Claims[0].Location.Start++;
        WorkerProtocolJson.SealManifest(other);
        AssertErrorCode(
            WorkerProtocolJson.Validate(response, InputHash, other),
            "response.manifest_mismatch");
        AssertErrorCode(WorkerProtocolJson.Validate(
                    response,
                    new string('b', InputHash.Length),
                    expected)
                , "response.input_mismatch");
    }

    [TestCase(nameof(WorkerVersionSummary.WorkerVersion))]
    [TestCase(nameof(WorkerVersionSummary.ApiSpecVersion))]
    [TestCase(nameof(WorkerVersionSummary.WorkerBinarySha256))]
    [TestCase(nameof(WorkerVersionSummary.ApiSpecContentSha256))]
    public void RequestBoundValidationAuthenticatesRuntimeProvenance(
        string propertyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        var response = CreateResponse(CreateManifest());
        var expected = CreateExpectedVersions();
        var property = typeof(WorkerVersionSummary).GetProperty(propertyName)!;
        property.SetValue(
            response.Summary.Versions,
            propertyName.EndsWith("Sha256", StringComparison.Ordinal)
                ? new string('c', 64)
                : "FABRICATED-version");

        AssertErrorCode(ValidateForRequest(response), "response.versions_mismatch");

        property.SetValue(
            response.Summary.Versions,
            property.GetValue(expected));
        Assert.That(ValidateForRequest(response).IsValid, Is.True);
    }

    [Test]
    public void RequestBoundValidationRejectsCrossSwappedRuntimeDigests()
    {
        var response = CreateResponse(CreateManifest());
        (response.Summary.Versions.WorkerBinarySha256,
            response.Summary.Versions.ApiSpecContentSha256) =
            (response.Summary.Versions.ApiSpecContentSha256,
                response.Summary.Versions.WorkerBinarySha256);

        AssertErrorCode(ValidateForRequest(response), "response.versions_mismatch");
    }

    [TestCase(nameof(WorkerBudgets.QueryRlimit))]
    [TestCase(nameof(WorkerBudgets.MethodRlimit))]
    [TestCase(nameof(WorkerBudgets.MethodWallTimeMilliseconds))]
    [TestCase(nameof(WorkerBudgets.ProjectWallTimeMilliseconds))]
    [TestCase(nameof(WorkerBudgets.MaxParallelism))]
    [TestCase(nameof(WorkerBudgets.MaximumExpressionDepth))]
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

        AssertContainsErrorCode(WorkerProtocolJson.ValidateForRequest(
                    response, response.RequestHash, InputHash,
                    response.Manifest, request,
                    CreateExpectedVersions())
                , "response.budgets_mismatch");
    }

    [Test]
    public void OmittedOrNumericClaimOutcomeIsRejectedDuringDeserialization()
    {
        var json = WorkerProtocolJson.SerializeResponse(
            CreateResponse(CreateManifest()));
        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<JsonException>((Action)(() =>
                WorkerProtocolJson.DeserializeResponse(
                    json.Replace(
                        "\"outcome\":\"Proven\",",
                        string.Empty,
                        StringComparison.Ordinal))));
            Assert.Throws<JsonException>((Action)(() =>
                WorkerProtocolJson.DeserializeResponse(
                    json.Replace(
                        "\"outcome\":\"Proven\"",
                        "\"outcome\":1",
                        StringComparison.Ordinal))));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void OversizedNumericEnumStringsAreRejectedAsMalformedJson(
        bool request)
    {
        const string oversized = "9999999999999999999999999999999999999999";
        var json = request
            ? WorkerProtocolJson.SerializeRequest(CreateRequest()).Replace(
                "\"verifyPolicy\":\"Advisory\"",
                $"\"verifyPolicy\":\"{oversized}\"",
                StringComparison.Ordinal)
            : WorkerProtocolJson.SerializeResponse(
                    CreateResponse(CreateManifest()))
                .Replace(
                    "\"outcome\":\"Proven\"",
                    $"\"outcome\":\"{oversized}\"",
                    StringComparison.Ordinal);

        Assert.That(
            (Action)(() => DeserializeByRoot(json)),
            Throws.TypeOf<JsonException>());
    }

    [Test]
    public void OmittedNestedManifestSchemaVersionIsRejectedDuringDeserialization()
    {
        var json = WorkerProtocolJson.SerializeResponse(
            CreateResponse(CreateManifest()));

        Assert.Throws<JsonException>((Action)(() =>
            WorkerProtocolJson.DeserializeResponse(
                json.Replace(
                    $"\"schemaVersion\":{WorkerManifestVersions.Current},",
                    string.Empty,
                    StringComparison.Ordinal))));
    }

    [Test]
    public void EveryReachableNestedProtocolModelRequiresItsExactPropertySet()
    {
        var requestJson = WorkerProtocolJson.SerializeRequest(CreateRequest());
        var response = CreateShapeCoverageResponse();
        var responseJson = WorkerProtocolJson.SerializeResponse(response);
        var cases = new (string Json, Func<JsonObject, JsonObject> Select, string Property)[]
        {
            (requestJson, static root => root["compilerManifest"]!.AsObject(), "path"),
            (requestJson, static root => root["budgets"]!.AsObject(), "queryRlimit"),
            (requestJson, static root => root["cache"]!.AsObject(), "enabled"),
            (responseJson, static root => root["manifest"]!.AsObject(), "schemaVersion"),
            (responseJson, static root => root["manifest"]!["callables"]![0]!.AsObject(), "callableId"),
            (responseJson, static root => root["manifest"]!["callables"]![0]!["location"]!.AsObject(), "path"),
            (responseJson, static root => root["manifest"]!["callables"]![0]!["assumptions"]![0]!.AsObject(), "id"),
            (responseJson, static root => root["manifest"]!["claims"]![0]!.AsObject(), "claimId"),
            (responseJson, static root => root["callableResults"]![0]!.AsObject(), "coverage"),
            (responseJson, static root => root["claimResults"]![0]!.AsObject(), "outcome"),
            (responseJson, static root => root["claimResults"]![0]!["effectWitness"]!.AsObject(), "kind"),
            (responseJson, static root => root["claimResults"]![0]!["model"]![0]!.AsObject(), "variable"),
            (responseJson, static root => root["summary"]!.AsObject(), "callableCount"),
            (responseJson, static root => root["summary"]!["outcomeCounts"]![0]!.AsObject(), "outcome"),
            (responseJson, static root => root["summary"]!["reasonCounts"]![0]!.AsObject(), "reason"),
            (responseJson, static root => root["summary"]!["assumptions"]!.AsObject(), "total"),
            (responseJson, static root => root["summary"]!["versions"]!.AsObject(), "protocolVersion"),
            (responseJson, static root => root["summary"]!["budgets"]!.AsObject(), "queryRlimit"),
            (responseJson, static root => root["errors"]![0]!.AsObject(), "code")
        };

        using (Assert.EnterMultipleScope())
        {
            foreach (var item in cases)
            {
                var root = JsonNode.Parse(item.Json)!.AsObject();
                Assert.That(item.Select(root).Remove(item.Property), Is.True);
                Assert.Throws<JsonException>((Action)(() =>
                    DeserializeByRoot(root.ToJsonString())));
            }
        }
    }

    [Test]
    public void StrictProtocolShapeRejectsNoncanonicalNestedTokensAndOrdering()
    {
        var responseJson = WorkerProtocolJson.SerializeResponse(
            CreateShapeCoverageResponse());
        var caseVariant = responseJson.Replace(
            "\"schemaVersion\"",
            "\"SchemaVersion\"",
            StringComparison.Ordinal);
        var numericString = responseJson.Replace(
            $"\"schemaVersion\":{WorkerManifestVersions.Current}",
            $"\"schemaVersion\":\"{WorkerManifestVersions.Current}\"",
            StringComparison.Ordinal);
        var enumCaseVariant = responseJson.Replace(
            "\"outcome\":\"Proven\"",
            "\"outcome\":\"proven\"",
            StringComparison.Ordinal);

        var extra = JsonNode.Parse(responseJson)!.AsObject();
        extra["manifest"]!.AsObject()["futureField"] = true;
        var nullElement = JsonNode.Parse(responseJson)!.AsObject();
        nullElement["manifest"]!["callables"]!.AsArray().Insert(0, null);
        var arraySwap = JsonNode.Parse(responseJson)!.AsObject();
        arraySwap["summary"]!.AsObject()["budgets"] = new JsonArray();
        var reordered = JsonNode.Parse(responseJson)!.AsObject();
        var manifest = reordered["manifest"]!.AsObject();
        var schemaVersion = manifest["schemaVersion"]!.DeepClone();
        Assert.That(manifest.Remove("schemaVersion"), Is.True);
        manifest["schemaVersion"] = schemaVersion;

        using (Assert.EnterMultipleScope())
        {
            foreach (var invalid in new[]
            {
                caseVariant,
                numericString,
                enumCaseVariant,
                extra.ToJsonString(),
                nullElement.ToJsonString(),
                arraySwap.ToJsonString(),
                reordered.ToJsonString()
            })
            {
                Assert.Throws<JsonException>((Action)(() =>
                    WorkerProtocolJson.DeserializeResponse(invalid)));
            }
        }
    }

    [Test]
    public void CanonicalProtocolDocumentsStrictlyRoundTrip()
    {
        var requestJson = WorkerProtocolJson.SerializeRequest(CreateRequest());
        var responseJson = WorkerProtocolJson.SerializeResponse(
            CreateShapeCoverageResponse());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                WorkerProtocolJson.SerializeRequest(
                    WorkerProtocolJson.DeserializeRequest(requestJson)!),
                Is.EqualTo(requestJson));
            Assert.That(
                WorkerProtocolJson.SerializeResponse(
                    WorkerProtocolJson.DeserializeResponse(responseJson)!),
                Is.EqualTo(responseJson));
        }
    }

    [Test]
    public void UnknownClaimsRequireIncompleteCallableCoverage()
    {
        var response = CreateResponse(CreateManifest());
        SetUnknown(response, WorkerClaimReason.UnsupportedBody);
        response.Summary = CreateSummary(response);

        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.unknown_coverage");

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

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.run_projection");

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

        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.run_projection");

        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.MethodTimeout;
        SetUnknown(response, WorkerClaimReason.MethodTimeout);
        response.Summary = CreateSummary(response);
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.run_projection");

        response.RunStatus = WorkerRunStatus.TimedOut;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        response.RunStatus = WorkerRunStatus.Complete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.Canceled;
        SetUnknown(response, WorkerClaimReason.Canceled);
        response.Summary = CreateSummary(response);
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.run_projection");

        response.RunStatus = WorkerRunStatus.Canceled;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [TestCase(WorkerRunStatus.TimedOut)]
    [TestCase(WorkerRunStatus.Canceled)]
    public void AllProvenEvidenceRejectsFabricatedInterruptedStatus(
        WorkerRunStatus status)
    {
        var response = CreateResponse(CreateManifest());
        response.RunStatus = status;

        AssertErrorCode(ValidateForRequest(response), "response.run_projection");
    }

    [Test]
    public void AllProvenEvidenceRejectsFabricatedFailureStatus()
    {
        var response = CreateResponse(CreateManifest());
        response.RunStatus = WorkerRunStatus.Failed;
        response.FailureReason = WorkerRunFailureReason.BackendUnavailable;

        AssertErrorCode(ValidateForRequest(response), "response.run_projection");
    }

    [Test]
    public void CallableCoverageIsAnExactProjectionOfOwnedClaims()
    {
        var response = CreateResponse(CreateManifest());
        response.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;

        AssertErrorCode(ValidateForRequest(response), "response.callable_projection");

        SetUnknown(response, WorkerClaimReason.UnsupportedBody);
        response.Summary = CreateSummary(response);
        Assert.That(ValidateForRequest(response).IsValid, Is.True);

        var emptyManifest = CreateManifest();
        emptyManifest.Callables[0].ClaimIds = [];
        emptyManifest.Claims = [];
        WorkerProtocolJson.SealManifest(emptyManifest);
        response = CreateResponse(emptyManifest);
        Assert.That(ValidateForRequest(response).IsValid, Is.True);
    }

    [Test]
    public void SchemaValidIncompleteUnsupportedContractProjectionIsAccepted()
    {
        var response = CreateResponse(CreateManifest());
        SetUnknown(response, WorkerClaimReason.UnsupportedContract);
        response.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.UnsupportedContract;
        response.Summary = CreateSummary(response);

        Assert.That(ValidateForRequest(response).IsValid, Is.True);
    }

    [TestCase(WorkerCallableCoverageReason.UnsupportedCallable)]
    [TestCase(WorkerCallableCoverageReason.UnsupportedContract)]
    [TestCase(WorkerCallableCoverageReason.SemanticUnknown)]
    [TestCase(WorkerCallableCoverageReason.InfrastructureFailure)]
    public void SchemaValidIncompleteClaimlessProjectionIsAccepted(
        WorkerCallableCoverageReason reason)
    {
        var manifest = CreateManifest();
        manifest.Callables[0].ClaimIds = [];
        manifest.Claims = [];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);
        response.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason = reason;
        if (reason == WorkerCallableCoverageReason.InfrastructureFailure)
        {
            response.RunStatus = WorkerRunStatus.Failed;
            response.FailureReason =
                WorkerRunFailureReason.InfrastructureFailure;
        }

        Assert.That(ValidateForRequest(response).IsValid, Is.True);
    }

    [Test]
    public void ClaimReasonsAreBoundToClaimKind()
    {
        var response = CreateResponse(CreateManifest());
        SetUnknown(response, WorkerClaimReason.EffectSummaryIncomplete);
        response.Summary = CreateSummary(response);
        AssertContainsErrorCode(ValidateForRequest(response), "response.claim_reason");

        var effectManifest = CreateManifest();
        effectManifest.Callables[0].SelectedFeatures = [WorkerSelectedFeature.Effects];
        effectManifest.Callables[0].SelectionReasons = [
            WorkerSelectionReason.ExplicitAnnotation
        ];
        effectManifest.Claims[0].Kind = WorkerClaimKind.Effect;
        effectManifest.Claims[0].Evidence = WorkerClaimEvidence.Attribute;
        effectManifest.Claims[0].EffectContractKind =
            WorkerEffectContractKind.EnforcePure;
        WorkerProtocolJson.SealManifest(effectManifest);
        response = CreateResponse(effectManifest);
        SetUnknown(response, WorkerClaimReason.DeepPostcondition);
        response.ClaimResults[0].EffectCertainty =
            WorkerEffectEvidenceCertainty.Unavailable;
        response.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        response.Summary = CreateSummary(response);
        AssertContainsErrorCode(ValidateForRequest(response), "response.claim_reason");
    }

    [TestCase("containment.unavailable", WorkerRunFailureReason.BackendUnavailable)]
    [TestCase("backend.unavailable", WorkerRunFailureReason.ContainmentFailure)]
    [TestCase("compiler_manifest.invalid", WorkerRunFailureReason.CompilationFailure)]
    [TestCase("compiler.CS1001", WorkerRunFailureReason.MalformedResult)]
    [TestCase("response.claim_set", WorkerRunFailureReason.InfrastructureFailure)]
    public void FailureReasonMustMatchProtocolErrorIdentity(
        string code,
        WorkerRunFailureReason reason)
    {
        var response = CreateResponse(CreateManifest());
        response.RunStatus = WorkerRunStatus.Failed;
        response.FailureReason = reason;
        response.Errors = [new WorkerProtocolError { Code = code, Message = "failure" }];

        AssertContainsErrorCode(ValidateForRequest(response), "response.run_projection");
    }

    [Test]
    public void ExactRunProjectionAcceptsSemanticProducerStatesAndCacheHits()
    {
        var complete = CreateResponse(CreateManifest());
        Assert.That(ValidateForRequest(complete).IsValid, Is.True);

        complete.ClaimResults[0].Outcome = WorkerClaimOutcome.Refuted;
        complete.Summary = CreateSummary(complete);
        complete.Summary.CacheStatus = WorkerCacheStatus.Written;
        Assert.That(ValidateForRequest(complete).IsValid, Is.True);

        var unknown = CreateResponse(CreateManifest());
        SetUnknown(unknown, WorkerClaimReason.UnsupportedBody);
        unknown.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        unknown.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        unknown.Summary = CreateSummary(unknown);
        Assert.That(ValidateForRequest(unknown).IsValid, Is.True);

        var timedOut = CreateResponse(CreateManifest());
        SetUnknown(timedOut, WorkerClaimReason.MethodTimeout);
        timedOut.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        timedOut.CallableResults[0].Reason =
            WorkerCallableCoverageReason.MethodTimeout;
        timedOut.RunStatus = WorkerRunStatus.TimedOut;
        timedOut.Summary = CreateSummary(timedOut);
        Assert.That(ValidateForRequest(timedOut).IsValid, Is.True);

        var canceled = CreateResponse(CreateManifest());
        SetUnknown(canceled, WorkerClaimReason.Canceled);
        canceled.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        canceled.CallableResults[0].Reason =
            WorkerCallableCoverageReason.Canceled;
        canceled.RunStatus = WorkerRunStatus.Canceled;
        canceled.Summary = CreateSummary(canceled);
        Assert.That(ValidateForRequest(canceled).IsValid, Is.True);

        var backend = CreateResponse(CreateManifest());
        SetUnknown(backend, WorkerClaimReason.BackendUnavailable);
        backend.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        backend.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        backend.RunStatus = WorkerRunStatus.Failed;
        backend.FailureReason = WorkerRunFailureReason.BackendUnavailable;
        backend.Summary = CreateSummary(backend);
        Assert.That(ValidateForRequest(backend).IsValid, Is.True);

        var replay = CreateResponse(CreateManifest());
        SetUnknown(replay, WorkerClaimReason.CounterexampleReplayFailed);
        replay.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        replay.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        replay.RunStatus = WorkerRunStatus.Failed;
        replay.FailureReason =
            WorkerRunFailureReason.CounterexampleReplayFailed;
        replay.Summary = CreateSummary(replay);
        Assert.That(ValidateForRequest(replay).IsValid, Is.True);

        complete.Summary.CacheStatus = WorkerCacheStatus.Hit;
        complete.Summary.CacheHit = true;
        Assert.That(ValidateForRequest(complete).IsValid, Is.True);
    }

    [Test]
    public void RequestBoundValidationRequiresProducerCompatibleCacheStates()
    {
        var activeRequest = CreateRequest();
        var proven = CreateResponse(CreateManifest());
        AssertCacheState(activeRequest, proven, WorkerCacheStatus.Miss, true);
        AssertCacheState(activeRequest, proven, WorkerCacheStatus.Disabled, false);
        AssertCacheState(activeRequest, proven, WorkerCacheStatus.Hit, false);
        AssertCacheState(activeRequest, proven, WorkerCacheStatus.Written, false);

        var inactiveRequest = CreateRequest();
        inactiveRequest.Cache.Enabled = false;
        AssertCacheState(inactiveRequest, proven, WorkerCacheStatus.Disabled, true);
        AssertCacheState(inactiveRequest, proven, WorkerCacheStatus.Miss, false);
        AssertCacheState(inactiveRequest, proven, WorkerCacheStatus.Unavailable, false);

        var provenOnlyRequest = CreateRequest();
        provenOnlyRequest.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        AssertCacheState(provenOnlyRequest, proven, WorkerCacheStatus.Disabled, true);
        AssertCacheState(provenOnlyRequest, proven, WorkerCacheStatus.Miss, false);

        var refuted = CreateResponse(CreateManifest());
        refuted.ClaimResults[0].Outcome = WorkerClaimOutcome.Refuted;
        refuted.Summary = CreateSummary(refuted);
        AssertCacheState(activeRequest, refuted, WorkerCacheStatus.Hit, true);
        AssertCacheState(activeRequest, refuted, WorkerCacheStatus.Written, true);
        AssertCacheState(activeRequest, refuted, WorkerCacheStatus.Unavailable, true);
        AssertCacheState(activeRequest, refuted, WorkerCacheStatus.Miss, false);

        var unknown = CreateResponse(CreateManifest());
        SetUnknown(unknown, WorkerClaimReason.UnsupportedBody);
        unknown.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        unknown.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        unknown.Summary = CreateSummary(unknown);
        AssertCacheState(activeRequest, unknown, WorkerCacheStatus.Miss, true);
        AssertCacheState(activeRequest, unknown, WorkerCacheStatus.Unavailable, true);
        AssertCacheState(activeRequest, unknown, WorkerCacheStatus.Hit, false);
        AssertCacheState(activeRequest, unknown, WorkerCacheStatus.Written, false);

        var earlyFailureManifest = CreateManifest();
        earlyFailureManifest.Callables = [];
        earlyFailureManifest.Claims = [];
        WorkerProtocolJson.SealManifest(earlyFailureManifest);
        var earlyFailure = CreateResponse(earlyFailureManifest);
        earlyFailure.RunStatus = WorkerRunStatus.Failed;
        earlyFailure.FailureReason = WorkerRunFailureReason.InputUnavailable;
        earlyFailure.Errors = [new WorkerProtocolError {
            Code = "input.unavailable",
            Message = "failure"
        }];
        AssertCacheState(activeRequest, earlyFailure, WorkerCacheStatus.Disabled, true);

        var malformed = CreateResponse(earlyFailureManifest);
        malformed.RunStatus = WorkerRunStatus.Failed;
        malformed.FailureReason = WorkerRunFailureReason.MalformedResult;
        malformed.Errors = [new WorkerProtocolError {
            Code = "response.claim_set",
            Message = "failure"
        }];
        AssertCacheState(activeRequest, malformed, WorkerCacheStatus.Rejected, true);
        malformed.FailureReason = WorkerRunFailureReason.InfrastructureFailure;
        malformed.Errors[0].Code = "worker.infrastructure";
        AssertCacheState(activeRequest, malformed, WorkerCacheStatus.Rejected, false);
    }

    [Test]
    public void MixedClaimOutcomesProjectOneIncompleteCallable()
    {
        var manifest = CreateManifest();
        var first = manifest.Claims[0];
        manifest.Callables[0].ClaimIds = [first.ClaimId, "claim.identity.1"];
        manifest.Claims = [
            first,
            new WorkerClaimManifestEntry {
                ClaimId = "claim.identity.1",
                CallableId = first.CallableId,
                Ordinal = 1,
                Kind = WorkerClaimKind.Postcondition,
                Evidence = WorkerClaimEvidence.DirectClause,
                Location = first.Location
            }
        ];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);
        SetUnknown(response, WorkerClaimReason.UnsupportedExpression, 1);
        response.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason =
            WorkerCallableCoverageReason.SemanticUnknown;
        response.Summary = CreateSummary(response);

        Assert.That(ValidateForRequest(response).IsValid, Is.True);

        response.CallableResults[0].Coverage = WorkerCallableCoverage.Complete;
        response.CallableResults[0].Reason = WorkerCallableCoverageReason.None;
        AssertContainsErrorCode(ValidateForRequest(response), "response.callable_projection");
    }

    [TestCase("request.malformed", WorkerRunFailureReason.InvalidRequest)]
    [TestCase("input.unavailable", WorkerRunFailureReason.InputUnavailable)]
    [TestCase("compiler.CS1001", WorkerRunFailureReason.CompilationFailure)]
    [TestCase("compiler_manifest.invalid", WorkerRunFailureReason.CompilerManifestMismatch)]
    [TestCase("backend.unavailable", WorkerRunFailureReason.BackendUnavailable)]
    [TestCase("worker.infrastructure", WorkerRunFailureReason.InfrastructureFailure)]
    [TestCase("response.claim_set", WorkerRunFailureReason.MalformedResult)]
    [TestCase("containment.unavailable", WorkerRunFailureReason.ContainmentFailure)]
    public void ExactRunProjectionAcceptsKnownFailureEvidence(
        string code,
        WorkerRunFailureReason reason)
    {
        var manifest = CreateManifest();
        manifest.Callables = [];
        manifest.Claims = [];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);
        response.RunStatus = WorkerRunStatus.Failed;
        response.FailureReason = reason;
        response.Errors = [new WorkerProtocolError { Code = code, Message = "failure" }];

        Assert.That(ValidateForRequest(response).IsValid, Is.True);
    }

    [Test]
    public void ProtocolErrorsRejectControlAndLineSeparatorCharacters()
    {
        var manifest = CreateManifest();
        manifest.Callables = [];
        manifest.Claims = [];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);
        response.RunStatus = WorkerRunStatus.Failed;
        response.FailureReason = WorkerRunFailureReason.InfrastructureFailure;
        response.Errors = [new WorkerProtocolError {
            Code = "worker.infrastructure",
            Message = "failure"
        }];
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        foreach (var separator in new[]
        {
            "\n", "\r", "\t", "\u001b", "\u2028", "\u2029"
        })
        {
            response.Errors[0].Code = "worker" + separator + "infrastructure";
            AssertContainsErrorCode(
                WorkerProtocolJson.Validate(response),
                "response.errors");

            response.Errors[0].Code = "worker.infrastructure";
            response.Errors[0].Message = "failure" + separator + "forged";
            AssertContainsErrorCode(
                WorkerProtocolJson.Validate(response),
                "response.errors");
            response.Errors[0].Message = "failure";
        }
    }

    [TestCase("worker.timeout", WorkerRunStatus.TimedOut)]
    [TestCase("worker.canceled", WorkerRunStatus.Canceled)]
    public void EmptyManifestInterruptionRequiresExactEvidence(
        string code,
        WorkerRunStatus status)
    {
        var manifest = CreateManifest();
        manifest.Callables = [];
        manifest.Claims = [];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);
        response.RunStatus = status;
        response.Errors = [new WorkerProtocolError { Code = code, Message = "interrupted" }];

        Assert.That(ValidateForRequest(response).IsValid, Is.True);
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
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "summary.assumption_conflict");
    }

    [Test]
    public void ClaimResultsRequireOwningCallableAssumptionDeclarations()
    {
        var response = CreateResponse(CreateManifest());
        response.ClaimResults[0].Assumptions =
            [.. response.ClaimResults[0].Assumptions.Skip(1)];
        response.Summary = CreateSummary(response);

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.claim_assumption_set");
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

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            "summary.totals",
            "response.claim_payload");
    }

    [Test]
    public void SummaryCountBucketsMustHaveUniqueKinds()
    {
        var manifest = CreateManifest();
        var first = manifest.Claims[0];
        manifest.Callables[0].ClaimIds = [.. manifest.Callables[0].ClaimIds, "claim.identity.1"];
        manifest.Claims = [.. manifest.Claims, new WorkerClaimManifestEntry {
            ClaimId = "claim.identity.1",
            CallableId = first.CallableId,
            Ordinal = 1,
            Kind = first.Kind,
            Evidence = first.Evidence,
            Location = new WorkerSourceLocation {
                Path = first.Location.Path,
                Start = first.Location.Start,
                Length = first.Location.Length,
                Line = first.Location.Line,
                Column = first.Location.Column
            }
        }];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);
        SetUnknown(response, WorkerClaimReason.UnsupportedBody, index: 1);
        response.CallableResults[0].Coverage = WorkerCallableCoverage.Incomplete;
        response.CallableResults[0].Reason = WorkerCallableCoverageReason.SemanticUnknown;
        response.Summary = CreateSummary(response);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        response.Summary.OutcomeCounts = [
            new WorkerClaimOutcomeCount { Outcome = WorkerClaimOutcome.Proven, Count = 1 },
            new WorkerClaimOutcomeCount { Outcome = WorkerClaimOutcome.Proven, Count = 1 }
        ];
        AssertErrorCode(WorkerProtocolJson.Validate(response), "summary.outcomes");

        response.Summary = CreateSummary(response);
        response.Summary.ReasonCounts = [
            new WorkerClaimReasonCount { Reason = WorkerClaimReason.None, Count = 1 },
            new WorkerClaimReasonCount { Reason = WorkerClaimReason.None, Count = 1 }
        ];
        AssertErrorCode(WorkerProtocolJson.Validate(response), "summary.reasons");
    }

    [Test]
    public void ManifestRequiresDenseOrdinalsAndExactCallableMembership()
    {
        var manifest = CreateManifest();
        manifest.Claims[0].Ordinal = 2;
        manifest.Callables[0].ClaimIds = [];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            "manifest.dense_ordinals",
            "manifest.claim_membership");
    }

    [Test]
    public void ManifestRejectsEffectClaimsBeforePostconditions()
    {
        var manifest = CreateManifest();
        var postcondition = manifest.Claims[0];
        postcondition.Ordinal = 1;
        manifest.Claims = [
            new WorkerClaimManifestEntry {
                ClaimId = "claim.identity.effect.0",
                CallableId = postcondition.CallableId,
                Ordinal = 0,
                Kind = WorkerClaimKind.Effect,
                Evidence = WorkerClaimEvidence.Attribute,
                EffectContractKind = WorkerEffectContractKind.DoesNotThrow,
                Location = postcondition.Location
            },
            postcondition
        ];
        manifest.Callables[0].SelectedFeatures = [
            WorkerSelectedFeature.Contracts,
            WorkerSelectedFeature.Effects
        ];
        manifest.Callables[0].SelectionReasons = [
            WorkerSelectionReason.DiscoveredPostcondition,
            WorkerSelectionReason.ExplicitAnnotation
        ];
        manifest.Callables[0].ClaimIds = [
            "claim.identity.effect.0",
            postcondition.ClaimId
        ];
        WorkerProtocolJson.SealManifest(manifest);

        Assert.That(
            WorkerProtocolJson.ValidateManifest(manifest).Errors
                .Select(static error => error.Code),
            Is.EqualTo(["manifest.claim_order"]));
    }

    [Test]
    public void NullProtocolRootsAndArrayRequestsAreRejected()
    {
        var request = WorkerProtocolJson.Validate((WorkerVerifyRequest?)null);
        var response = WorkerProtocolJson.Validate((WorkerVerifyResponse?)null);

        using (Assert.EnterMultipleScope())
        {
            AssertErrorCode(request, "request.null");
            AssertErrorCode(response, "response.null");
            Assert.Throws<JsonException>((Action)(() =>
                WorkerProtocolJson.DeserializeRequest("[]")));
        }
    }

    [Test]
    public void MissingResponseSubdocumentsAndInvalidExpectedManifestAreRejected()
    {
        var missingManifest = CreateResponse(CreateManifest());
        missingManifest.Manifest = null!;
        var missingSummary = CreateResponse(CreateManifest());
        missingSummary.Summary = null!;
        var missingAssumptions = CreateResponse(CreateManifest());
        missingAssumptions.Summary.Assumptions = null!;
        var expectedManifest = CreateManifest();
        expectedManifest.SchemaVersion = int.MaxValue;

        using (Assert.EnterMultipleScope())
        {
            AssertContainsErrorCode(
                WorkerProtocolJson.Validate(missingManifest),
                "manifest.null");
            AssertContainsErrorCode(
                WorkerProtocolJson.Validate(missingSummary),
                "response.summary");
            AssertContainsErrorCode(
                WorkerProtocolJson.Validate(missingAssumptions),
                "summary.assumptions");
            AssertContainsErrorCode(WorkerProtocolJson.Validate(
                        CreateResponse(CreateManifest()),
                        InputHash,
                        expectedManifest)
                    , "response.expected_manifest");
        }
    }

    [Test]
    public void RequestBoundValidationRequiresAndTotallyComparesManifests()
    {
        var request = CreateRequest();
        var expected = CreateManifest();
        var response = CreateResponse(expected);
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);

        var nullError = Assert.Throws<ArgumentNullException>((Action)(() =>
            WorkerProtocolJson.ValidateForRequest(
                response,
                response.RequestHash,
                InputHash,
                null!,
                request,
                CreateExpectedVersions())));

        response.Manifest.Claims[0].Kind =
            (WorkerClaimKind)int.MaxValue;
        var validation = WorkerProtocolJson.ValidateForRequest(
            response,
            response.RequestHash,
            InputHash,
            expected,
            request,
            CreateExpectedVersions());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullError!.ParamName, Is.EqualTo("expectedManifest"));
            Assert.That(validation.IsValid, Is.False);
            AssertContainsErrorCode(validation, "manifest.claim_kind");
        }
    }

    [TestCase(0L, true)]
    [TestCase(922337203685477L, true)]
    [TestCase(922337203685478L, false)]
    [TestCase(long.MaxValue, false)]
    [TestCase(-1L, false)]
    public void ResponseElapsedTimeUsesTheProducerRepresentableEnvelope(
        long elapsedMilliseconds,
        bool expectedValid)
    {
        var response = CreateResponse(CreateManifest());
        response.Summary.ElapsedMilliseconds = elapsedMilliseconds;

        var validation = WorkerProtocolJson.Validate(response);
        var elapsedErrors = validation.Errors
            .Where(static error => error.Code is
                "response.elapsed_unrepresentable" or "summary.elapsed")
            .ToArray();

        Assert.That(elapsedErrors.Length == 0, Is.EqualTo(expectedValid));
    }

    [TestCase(WorkerRunStatus.Complete)]
    [TestCase(WorkerRunStatus.TimedOut)]
    [TestCase(WorkerRunStatus.Canceled)]
    [TestCase(WorkerRunStatus.Failed)]
    public void ProducerElapsedEnvelopeAppliesToEveryRunStatus(
        WorkerRunStatus status)
    {
        var response = CreateResponse(CreateManifest());
        response.RunStatus = status;
        response.Summary.ElapsedMilliseconds =
            WorkerExecutionEnvelope.MaximumProducerElapsedMilliseconds + 1;

        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            "response.elapsed_unrepresentable");
    }

    [TestCase(101, 300001L)]
    [TestCase(200, 300100L)]
    [TestCase(1000, 300900L)]
    public void RequestBoundElapsedTimeUsesTheActualLauncherGrace(
        int terminationGraceMilliseconds,
        long exactMaximum)
    {
        var request = CreateRequest();
        var manifest = CreateManifest();
        var response = CreateResponse(manifest);
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
        response.Summary.ElapsedMilliseconds = exactMaximum;

        var exact = WorkerProtocolJson.ValidateForRequest(
            response, response.RequestHash, InputHash, manifest, request,
            CreateExpectedVersions(), terminationGraceMilliseconds);
        Assert.That(exact.IsValid, Is.True,
            string.Join(Environment.NewLine,
                exact.Errors.Select(static error => error.Code)));

        response.Summary.ElapsedMilliseconds++;
        var over = WorkerProtocolJson.ValidateForRequest(
            response, response.RequestHash, InputHash, manifest, request,
            CreateExpectedVersions(), terminationGraceMilliseconds);
        AssertErrorCode(over, "response.elapsed_request_envelope");
    }

    [Test]
    public void RequestElapsedEnvelopeRejectsInvalidAuthority()
    {
        var request = CreateRequest();
        request.Budgets.ProjectWallTimeMilliseconds = int.MaxValue;
        request.Budgets.MethodWallTimeMilliseconds = int.MaxValue;

        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            WorkerExecutionEnvelope.MaximumElapsedMilliseconds(request, 0)));
        Assert.That(
            WorkerExecutionEnvelope.MaximumElapsedMilliseconds(
                request, WorkerExecutionEnvelope.CleanupReserveMilliseconds),
            Is.EqualTo((long)int.MaxValue + 1));
        Assert.That(
            WorkerExecutionEnvelope.MaximumElapsedMilliseconds(
                request, WorkerLauncherDefaults.MaximumTerminationGraceMilliseconds),
            Is.EqualTo((long)int.MaxValue +
                WorkerLauncherDefaults.MaximumTerminationGraceMilliseconds -
                WorkerExecutionEnvelope.CleanupReserveMilliseconds));
    }

    [Test]
    public void ManifestHashAndExpectedInputHashValidateBoundaryValues()
    {
        var nullIdentity = CreateManifest();
        nullIdentity.Callables[0].CallableId = null!;
        var nullIdentityHash = WorkerProtocolJson.ComputeManifestHash(nullIdentity);
        var unknownEnum = CreateManifest();
        unknownEnum.Claims[0].Kind = (WorkerClaimKind)int.MaxValue;

        var enumError = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            WorkerProtocolJson.ComputeManifestHash(unknownEnum)));
        var hashError = Assert.Throws<ArgumentException>((Action)(() =>
            WorkerProtocolJson.Validate(
                CreateResponse(CreateManifest()),
                "not-a-hash")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullIdentityHash, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(enumError!.ParamName, Is.EqualTo("value"));
            Assert.That(hashError!.ParamName, Is.EqualTo("expectedInputHash"));
        }
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
                LanguageVersion.CSharp12),
            path);
        var references = TestMetadataReferences.ForFileNames(
            [
                "System.Private.CoreLib.dll",
                "System.Linq.dll",
                "System.Runtime.dll",
                "netstandard.dll"
            ],
            includeSharpProof: true,
            sort: false);
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

    private static WorkerClaimManifest CreateEffectManifest()
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
        return manifest;
    }

    private static WorkerClaimManifest CreateTwoCallableManifest()
    {
        var manifest = CreateManifest();
        var location = manifest.Callables[0].Location;
        manifest.Callables = [
            manifest.Callables[0],
            new WorkerCallableManifestEntry {
                CallableId = "M:Subject.Other(System.Int64)",
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [
                    WorkerSelectionReason.DiscoveredPostcondition
                ],
                Location = new WorkerSourceLocation {
                    Path = location.Path,
                    Start = location.Start + 1,
                    Length = location.Length,
                    Line = location.Line,
                    Column = location.Column
                },
                ClaimIds = [],
                Assumptions = [
                    new WorkerAssumptionEvidence {
                        Id = "spa1:other",
                        Kind = WorkerAssumptionKind.UserAssume
                    }
                ]
            }
        ];
        WorkerProtocolJson.SealManifest(manifest);
        return manifest;
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

    private static TimeSpan MeasureValidation(WorkerVerifyResponse response)
    {
        var stopwatch = Stopwatch.StartNew();
        var validation = WorkerProtocolJson.Validate(response);
        stopwatch.Stop();

        Assert.That(
            validation.Errors,
            Is.Empty,
            string.Join(
                ", ",
                validation.Errors.Select(static error => error.Code)));
        return stopwatch.Elapsed;
    }

    private static void AssertErrorCode(
        WorkerProtocolValidationResult validation,
        params string[] expected)
    {
        var actual = validation.Errors
            .Select(static error => error.Code)
            .ToArray();
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    private static void AssertContainsErrorCode(
        WorkerProtocolValidationResult validation,
        params string[] expected)
    {
        var actual = validation.Errors
            .Select(static error => error.Code)
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            foreach (var code in expected)
            {
                Assert.That(actual, Does.Contain(code));
            }
        }
    }

    private static TimeSpan MeasureCanonicalization(
        WorkerVerifyResponse response)
    {
        var stopwatch = Stopwatch.StartNew();
        WorkerProtocolJson.Canonicalize(response);
        stopwatch.Stop();

        Assert.That(response.ClaimResults, Has.Length.EqualTo(
            response.Manifest.Claims.Length));
        return stopwatch.Elapsed;
    }

    private static WorkerVerifyResponse CreateValidationScalingResponse(
        int size)
    {
        var callables = new WorkerCallableManifestEntry[size];
        var claims = new WorkerClaimManifestEntry[size];
        var callableResults = new WorkerCallableResult[size];
        var claimResults = new WorkerClaimResult[size];
        var idPrefix = new string('x', 32);
        var location = new WorkerSourceLocation
        {
            Path = "Scaling.cs",
            Length = 1,
            Line = 1,
            Column = 1
        };
        for (var index = 0; index < size; index++)
        {
            var suffix = index.ToString("D6", CultureInfo.InvariantCulture);
            var callableId = "M:" + idPrefix + suffix;
            var claimId = "claim:" + idPrefix + suffix;
            callables[index] = new WorkerCallableManifestEntry
            {
                CallableId = callableId,
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [
                    WorkerSelectionReason.DiscoveredPostcondition
                ],
                Location = location,
                ClaimIds = [claimId]
            };
            claims[index] = new WorkerClaimManifestEntry
            {
                ClaimId = claimId,
                CallableId = callableId,
                Kind = WorkerClaimKind.Postcondition,
                Evidence = WorkerClaimEvidence.DirectClause,
                Location = location
            };
            callableResults[index] = new WorkerCallableResult
            {
                CallableId = callableId,
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None
            };
            claimResults[index] = new WorkerClaimResult
            {
                ClaimId = claimId,
                Outcome = WorkerClaimOutcome.Proven,
                Reason = WorkerClaimReason.None
            };
        }

        var manifest = new WorkerClaimManifest
        {
            Callables = callables,
            Claims = claims
        };
        manifest.Hash = WorkerProtocolJson.ComputeManifestHash(manifest);
        return new WorkerVerifyResponse
        {
            InputHash = InputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = callableResults,
            ClaimResults = claimResults,
            Summary = new WorkerVerificationSummary
            {
                CallableCount = size,
                ClaimCount = size,
                OutcomeCounts = [
                    new WorkerClaimOutcomeCount
                    {
                        Outcome = WorkerClaimOutcome.Proven,
                        Count = size
                    }
                ],
                ReasonCounts = [
                    new WorkerClaimReasonCount
                    {
                        Reason = WorkerClaimReason.None,
                        Count = size
                    }
                ],
                CacheStatus = WorkerCacheStatus.Miss,
                Versions = CreateExpectedVersions()
            }
        };
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
            CacheStatus = WorkerCacheStatus.Miss,
            Versions = CreateExpectedVersions()
        };
    }

    private static WorkerVersionSummary CreateExpectedVersions()
    {
        return new WorkerVersionSummary
        {
            WorkerVersion = "test-worker",
            ApiSpecVersion = "test-spec",
            WorkerBinarySha256 = new string('a', 64),
            ApiSpecContentSha256 = new string('b', 64)
        };
    }

    private static WorkerVerifyResponse CreateShapeCoverageResponse()
    {
        var response = CreateResponse(CreateManifest());
        var claim = response.ClaimResults[0];
        claim.EffectWitness = CreateEffectWitness(
            response.Manifest.Claims[0].Location);
        claim.Model = [
            new WorkerModelValue
            {
                Variable = "result",
                Kind = "integer",
                Value = "0"
            }
        ];
        response.Errors = [
            new WorkerProtocolError
            {
                Code = "fixture.error",
                Message = "Shape coverage fixture."
            }
        ];
        return response;
    }

    private static void DeserializeByRoot(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty(
                "compilerManifest",
                out _))
        {
            _ = WorkerProtocolJson.DeserializeRequest(json);
        }
        else
        {
            _ = WorkerProtocolJson.DeserializeResponse(json);
        }
    }

    private static void SetUnknown(
        WorkerVerifyResponse response,
        WorkerClaimReason reason,
        int index = 0)
    {
        response.ClaimResults[index].Outcome = WorkerClaimOutcome.Unknown;
        response.ClaimResults[index].Reason = reason;
    }

    private static WorkerProtocolValidationResult ValidateForRequest(
        WorkerVerifyResponse response)
    {
        var request = CreateRequest();
        return ValidateForRequest(response, request);
    }

    private static WorkerProtocolValidationResult ValidateForRequest(
        WorkerVerifyResponse response,
        WorkerVerifyRequest request)
    {
        response.RequestHash = WorkerProtocolJson.ComputeRequestHash(request);
        return WorkerProtocolJson.ValidateForRequest(
            response,
            response.RequestHash,
            InputHash,
            response.Manifest,
            request,
            CreateExpectedVersions());
    }

    private static void AssertCacheState(
        WorkerVerifyRequest request,
        WorkerVerifyResponse response,
        WorkerCacheStatus status,
        bool expectedValid)
    {
        response.Summary.CacheStatus = status;
        response.Summary.CacheHit = status == WorkerCacheStatus.Hit;
        var validation = ValidateForRequest(response, request);
        Assert.That(
            validation.IsValid,
            Is.EqualTo(expectedValid),
            string.Join(", ", validation.Errors.Select(static error => error.Code)));
        if (!expectedValid)
        {
            AssertErrorCode(validation, "response.cache_request_mismatch");
        }
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
