using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Specs;
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
internal sealed class EvidenceSetCoverageTests
{
    [Test]
    public void ConstructorRequiresEveryEvidenceAuthority()
    {
        using var fixture = new StoredEvidenceFixture();
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new EvidenceSet(null!, fixture.Request, fixture.Artifact); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new EvidenceSet(fixture.Response, null!, fixture.Artifact); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new EvidenceSet(fixture.Response, fixture.Request, null!); }));
        Assert.That(fixture.Evidence.Response, Is.SameAs(fixture.Response));
        Assert.That(fixture.Evidence.Request, Is.SameAs(fixture.Request));
        Assert.That(fixture.Evidence.Artifact, Is.SameAs(fixture.Artifact));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void StoredEvidenceRoundTripsWithOptionalDescriptions(bool includeMetadata)
    {
        using var fixture = new StoredEvidenceFixture(includeMetadata);
        var loaded = EvidenceSet.Load(fixture.ResultPath, null, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(loaded.Response.InputHash, Is.EqualTo(fixture.Response.InputHash));
        Assert.That(loaded.Request.CompilerManifest.Sha256, Is.EqualTo(fixture.Request.CompilerManifest.Sha256));
        Assert.That(loaded.Artifact.Manifest.Hash, Is.EqualTo(fixture.Artifact.Manifest.Hash));
        Assert.That(loaded.Response.ClaimResults[0].Presentation is not null, Is.EqualTo(includeMetadata));
    }

    [Test]
    public void ExplicitEvidencePathsAndOmittedResponseDescriptionsAreAccepted()
    {
        using var fixture = new StoredEvidenceFixture();
        fixture.Response.ClaimResults = [fixture.Response.ClaimResults[0] with { Presentation = null, Diagnostic = null }];
        fixture.Save();
        var loaded = EvidenceSet.Load(fixture.ResultPath, fixture.RequestPath, fixture.ManifestPath, TestContext.CurrentContext.CancellationToken);
        Assert.That(loaded.Response.ClaimResults[0].Presentation, Is.Null);
        Assert.That(loaded.Artifact.Callables[0].Presentation, Is.Not.Null);
    }

    [Test]
    public void InvalidPathsAndCancellationFailBeforeEvidenceIo()
    {
        var token = TestContext.CurrentContext.CancellationToken;
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = EvidenceSet.Load(null!, null, null, token); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = EvidenceSet.Load("", null, null, token); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = EvidenceSet.Load(" ", null, null, token); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = EvidenceSet.Load("missing.json", " ", null, token); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = EvidenceSet.Load("missing.json", null, " ", token); }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => { _ = EvidenceSet.Load("missing.json", null, null, cancellation.Token); }));
    }

    [Test]
    public void ReadRequiresNonemptyBoundedRegularFilesAndStrictUtf8()
    {
        using var fixture = new StoredEvidenceFixture();
        var token = TestContext.CurrentContext.CancellationToken;
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = EvidenceSet.ReadBounded((string)null!, 4, token); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = EvidenceSet.ReadBounded("", 4, token); }));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = EvidenceSet.ReadBounded(fixture.ResultPath, 0, token); }));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = EvidenceSet.ReadBounded(fixture.ResultPath, -1, token); }));
        Assert.Throws<InvalidDataException>(new Action(() => { _ = EvidenceSet.ReadBounded(Path.Combine(fixture.Root, "missing"), 4, token); }));
        Assert.Throws<InvalidDataException>(new Action(() => { _ = EvidenceSet.ReadBounded(fixture.Root, 4, token); }));
        var path = Path.Combine(fixture.Root, "bytes");
        File.WriteAllBytes(path, []);
        Assert.Throws<InvalidDataException>(new Action(() => { _ = EvidenceSet.ReadBounded(path, 4, token); }));
        File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
        Assert.Throws<InvalidDataException>(new Action(() => { _ = EvidenceSet.ReadBounded(path, 4, token); }));
        File.WriteAllBytes(path, [0xc3, 0x28]);
        Assert.Throws<DecoderFallbackException>(new Action(() => { _ = EvidenceSet.ReadBounded(path, 4, token); }));
    }

    [Test]
    public void SymlinkEvidenceIsRejectedBeforeOpeningItsTarget()
    {
        using var fixture = new StoredEvidenceFixture();
        var path = Path.Combine(fixture.Root, "linked.json");
        File.CreateSymbolicLink(path, fixture.ResultPath);
        Assert.Throws<InvalidDataException>(new Action(() => { _ = EvidenceSet.ReadBounded(path, 16 * 1024 * 1024, TestContext.CurrentContext.CancellationToken); }));
    }

    [Test]
    public void ExactByteLimitAndMultipleReadChunksPreserveContent()
    {
        using var fixture = new StoredEvidenceFixture();
        var text = new string('a', 65_537) + "\u00e9";
        var path = Path.Combine(fixture.Root, "large.txt");
        File.WriteAllText(path, text, new UTF8Encoding(false));
        var bytes = Encoding.UTF8.GetByteCount(text);
        Assert.That(EvidenceSet.ReadBounded(path, bytes, TestContext.CurrentContext.CancellationToken), Is.EqualTo(text));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void JsonNullIsRejectedAsMissingEvidence(bool result)
    {
        using var fixture = new StoredEvidenceFixture();
        File.WriteAllText(result ? fixture.ResultPath : fixture.RequestPath, "null");
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain(result ? "result is empty" : "request is empty"));
    }

    [Test]
    public void TypedRequestValidationPrecedesProvenanceUse()
    {
        using var fixture = new StoredEvidenceFixture();
        fixture.WriteRequest(fixture.Request with { VerifyPolicy = WorkerVerifyPolicy.Unspecified });
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("policy.verify"));
    }

    [Test]
    public void MissingSummaryIsValidationFailureInsteadOfNullDereference()
    {
        using var fixture = new StoredEvidenceFixture();
        fixture.WriteResponse(fixture.Response with { Summary = null! });
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("response.summary"));
    }

    [Test]
    public void ChangedArtifactBytesCannotReuseRecordedDigest()
    {
        using var fixture = new StoredEvidenceFixture();
        File.AppendAllText(fixture.ManifestPath, " ");
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("request digest"));
    }

    [Test]
    public void MatchingDigestCannotMakeMalformedArtifactAuthoritative()
    {
        using var fixture = new StoredEvidenceFixture();
        var bytes = Encoding.UTF8.GetBytes("{}").ToImmutableArray();
        File.WriteAllBytes(fixture.ManifestPath, bytes.ToArray());
        fixture.WriteRequest(fixture.Request with { CompilerManifest = fixture.Request.CompilerManifest with { Sha256 = ArtifactDigest.Compute(bytes) } });
        Assert.Throws<JsonException>(new Action(() => { _ = fixture.Load(); }));
    }

    [Test]
    public void RequestPolicyMutationCannotReuseOldResponseHash()
    {
        using var fixture = new StoredEvidenceFixture();
        fixture.WriteRequest(fixture.Request with { VerifyPolicy = WorkerVerifyPolicy.RequireProven });
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("response.request_mismatch"));
    }

    [Test]
    public void ValidlyShapedButWrongInputHashIsRejected()
    {
        using var fixture = new StoredEvidenceFixture();
        fixture.WriteResponse(fixture.Response with { InputHash = new string('a', 64) });
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("response.input_mismatch"));
    }

    [Test]
    public void ResponseManifestMustMatchCapturedArtifact()
    {
        using var fixture = new StoredEvidenceFixture();
        var original = fixture.Response;
        var originalCallable = fixture.Artifact.Manifest.Callables[0];
        fixture.Artifact = fixture.Artifact with
        {
            Manifest = fixture.Artifact.Manifest with
            {
                Callables = [originalCallable with { ClaimIds = ["claim:changed"] }],
                Claims = [fixture.Artifact.Manifest.Claims[0] with { ClaimId = "claim:changed" }]
            }
        };
        fixture.Save();
        fixture.WriteResponse(original with { InputHash = fixture.Response.InputHash, RequestHash = fixture.Response.RequestHash });
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("response.manifest_mismatch"));
    }

    [TestCase("name")]
    [TestCase("parameters")]
    [TestCase("absent")]
    public void ResponsePresentationMustMatchCapturedDescription(string mutation)
    {
        ArgumentException.ThrowIfNullOrEmpty(mutation);
        using var fixture = new StoredEvidenceFixture();
        if (mutation == "absent")
        {
            fixture.Artifact = fixture.Artifact with { Callables = [fixture.Artifact.Callables[0] with { Presentation = null }] };
        }
        else
        {
            var presentation = fixture.Response.ClaimResults[0].Presentation!;
            fixture.Response.ClaimResults = [fixture.Response.ClaimResults[0] with
            {
                Presentation = mutation == "name" ? presentation with { Name = "Other.Sample" } : presentation with { ParameterNames = ["other"] }
            }];
        }
        fixture.Save();
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("presentation does not match"));
    }

    [Test]
    public void ResponseDiagnosticMustMatchCapturedOrigin()
    {
        using var fixture = new StoredEvidenceFixture();
        fixture.Response.ClaimResults = [fixture.Response.ClaimResults[0] with { Diagnostic = fixture.Response.ClaimResults[0].Diagnostic! with { Code = "lowering.Other" } }];
        fixture.Save();
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("diagnostic context does not match"));
    }

    [Test]
    public void DepthMismatchRejectsResultsThatDidNotReportTheMismatch()
    {
        using var fixture = new StoredEvidenceFixture(artifactDepth: 63);
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("expression-depth limit"));
    }

    [Test]
    public void StreamAdmissionRejectsInvalidBoundsAndStaleObservedLength()
    {
        var token = TestContext.CurrentContext.CancellationToken;
        using var stream = new MemoryStream([1, 2]);
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = EvidenceSet.ReadBounded((Stream)null!, 2, 4, token); }));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = EvidenceSet.ReadBounded(stream, 2, 0, token); }));
        Assert.Throws<InvalidDataException>(new Action(() => { _ = EvidenceSet.ReadBounded(stream, 0, 4, token); }));
        Assert.Throws<InvalidDataException>(new Action(() => { _ = EvidenceSet.ReadBounded(stream, 5, 4, token); }));
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = EvidenceSet.ReadBounded(stream, 3, 4, token); }));
        Assert.That(error!.Message, Does.Contain("changed while opening"));
    }

    [TestCase(1, 2)]
    [TestCase(3, 2)]
    public void ChangedBytesDuringReadFailClosed(int actualLength, int observedLength)
    {
        using var stream = new ObservedLengthStream(new byte[actualLength], observedLength);
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = EvidenceSet.ReadBounded(stream, observedLength, 4, TestContext.CurrentContext.CancellationToken); }));
        Assert.That(error!.Message, Does.Contain("changed while reading"));
    }

    [Test]
    public void CancellationBetweenReadAndPublicationPreventsReturningEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new ObservedLengthStream([65, 66], 2, cancellation.Cancel);
        Assert.Throws<OperationCanceledException>(new Action(() => { _ = EvidenceSet.ReadBounded(stream, 2, 4, cancellation.Token); }));
    }

    [Test]
    public void StreamReaderReturnsExactEvidenceWithoutOwningTheStream()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        Assert.That(EvidenceSet.ReadBounded(stream, 3, 3, TestContext.CurrentContext.CancellationToken), Is.EqualTo("abc"));
        Assert.That(stream.CanRead, Is.True);
    }

    private sealed class ObservedLengthStream(byte[] bytes, int observedLength, Action? afterRead = null) : MemoryStream(bytes ?? throw new ArgumentNullException(nameof(bytes)), writable: false)
    {
        public override long Length => observedLength;
        public override int Read(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            var read = base.Read(buffer, offset, count);
            afterRead?.Invoke();
            return read;
        }
    }
    [Test]
    public void DifferentFailureReasonDoesNotExcuseDepthMismatch()
    {
        using var fixture = new StoredEvidenceFixture(artifactDepth: 63, failedDepthMismatch: true);
        fixture.Response = fixture.Response with { FailureReason = WorkerRunFailureReason.InfrastructureFailure, Errors = [new() { Code = "worker.infrastructure", Message = "Other failure" }] };
        fixture.Save();
        Assert.That(WorkerProtocolJson.Validate(fixture.Response).IsValid, Is.True);
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("expression-depth limit"));
    }

    [Test]
    public void DepthMismatchFailureCannotCarryAnAcceptedProof()
    {
        using var fixture = new StoredEvidenceFixture(artifactDepth: 63, failedDepthMismatch: true);
        fixture.Response.ClaimResults = [fixture.Response.ClaimResults[0] with { Outcome = WorkerClaimOutcome.Proven, Reason = WorkerClaimReason.None, Diagnostic = null }];
        fixture.Save();
        Assert.That(WorkerProtocolJson.Validate(fixture.Response).IsValid, Is.True);
        var error = Assert.Throws<InvalidDataException>(new Action(() => { _ = fixture.Load(); }));
        Assert.That(error!.Message, Does.Contain("expression-depth limit"));
    }
    [Test]
    public void RecordedDepthMismatchFailureRemainsExplainable()
    {
        using var fixture = new StoredEvidenceFixture(artifactDepth: 63, failedDepthMismatch: true);
        var evidence = fixture.Load();
        Assert.That(evidence.Response.FailureReason, Is.EqualTo(WorkerRunFailureReason.CompilerManifestMismatch));
        Assert.That(evidence.Response.ClaimResults[0].Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }
}

// A small, admitted compiler failure isolates evidence transport from compiler or solver execution.
internal sealed class StoredEvidenceFixture : IDisposable
{
    internal StoredEvidenceFixture(bool includeMetadata = true, int artifactDepth = 64, int requestDepth = 64, bool failedDepthMismatch = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "sharpproof-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        ManifestPath = Path.Combine(Root, "compiler-manifest.json");
        RequestPath = Path.Combine(Root, "request.json");
        ResultPath = Path.Combine(Root, "result.json");
        var location = new WorkerSourceLocation { Path = "Evidence.cs", Start = 0, Length = 1, Line = 1, Column = 1 };
        var manifest = new WorkerClaimManifest
        {
            Callables = [new() { CallableId = "M:Evidence.Sample(System.Int32)", ClaimIds = ["claim:captured"], SelectedFeatures = [WorkerSelectedFeature.Contracts], SelectionReasons = [WorkerSelectionReason.DiscoveredPostcondition], Location = location }],
            Claims = [new() { ClaimId = "claim:captured", CallableId = "M:Evidence.Sample(System.Int32)", Kind = WorkerClaimKind.Postcondition, Evidence = WorkerClaimEvidence.DirectClause, Location = location }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var presentation = includeMetadata ? new WorkerCallablePresentation { Name = "Evidence.Sample", ParameterNames = ["value"] } : null;
        var diagnostic = includeMetadata ? new WorkerDiagnosticContext { Code = "lowering.Unsupported", Location = location } : null;
        Artifact = new CompilerManifestArtifact
        {
            Features = WorkerFeatureSet.Contracts,
            MaximumExpressionDepth = artifactDepth,
            Manifest = manifest,
            Compilation = new() { ProjectDirectory = Root, AssemblyName = "EvidenceFixture", AssemblyIdentity = "EvidenceFixture, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null", TargetFramework = "net10.0" },
            Callables = [new() { CallableId = manifest.Callables[0].CallableId, FailureReason = WorkerClaimReason.UnsupportedCallable, Presentation = presentation, Diagnostic = diagnostic }]
        };
        Request = new WorkerVerifyRequest
        {
            CompilerManifest = new() { Path = ManifestPath, Sha256 = WorkerProtocolVersions.EmptySha256 },
            Cache = new() { Enabled = false },
            Budgets = new() { MaximumExpressionDepth = requestDepth }
        };
        var versions = new WorkerVersionSummary { WorkerVersion = "fixture-worker", ApiSpecVersion = "fixture-api", WorkerBinarySha256 = new string('1', 64), ApiSpecContentSha256 = new string('2', 64) };
        Response = WorkerResultAssembler.CreateIncomplete(WorkerProtocolVersions.EmptySha256, WorkerProtocolVersions.EmptySha256, manifest, Request.Budgets,
            failedDepthMismatch ? WorkerRunStatus.Failed : WorkerRunStatus.Complete,
            failedDepthMismatch ? WorkerRunFailureReason.CompilerManifestMismatch : WorkerRunFailureReason.None,
            failedDepthMismatch ? WorkerCallableCoverageReason.InfrastructureFailure : WorkerCallableCoverageReason.UnsupportedCallable,
            failedDepthMismatch ? WorkerClaimReason.InfrastructureFailure : WorkerClaimReason.UnsupportedCallable,
            failedDepthMismatch ? [new WorkerProtocolError { Code = "compiler_manifest.options", Message = "Captured expression depth differs from the request." }] : [], versions);
        Response.ClaimResults = [Response.ClaimResults[0] with { Presentation = presentation, Diagnostic = diagnostic }];
        Save();
        var validation = WorkerProtocolJson.Validate(Response);
        Assert.That(validation.IsValid, Is.True, string.Join(", ", validation.Errors.Select(static error =>
        {
            ArgumentNullException.ThrowIfNull(error);
            return error.Code;
        })));
    }

    internal string Root { get; }
    internal string ManifestPath { get; }
    internal string RequestPath { get; }
    internal string ResultPath { get; }
    internal CompilerManifestArtifact Artifact { get; set; }
    internal WorkerVerifyRequest Request { get; set; }
    internal WorkerVerifyResponse Response { get; set; }
    internal EvidenceSet Evidence => new(Response, Request, Artifact);

    internal void Save()
    {
        var token = TestContext.CurrentContext.CancellationToken;
        WorkerProtocolJson.SealManifest(Artifact.Manifest);
        var json = CompilerManifestArtifactJson.Serialize(Artifact, token);
        File.WriteAllText(ManifestPath, json, new UTF8Encoding(false));
        Request = Request with { CompilerManifest = Request.CompilerManifest with { Path = ManifestPath, Sha256 = ArtifactDigest.Compute(Encoding.UTF8.GetBytes(json).ToImmutableArray()) } };
        var versions = Response.Summary.Versions;
        Response = Response with
        {
            Manifest = Artifact.Manifest,
            RequestHash = WorkerProtocolJson.ComputeRequestHash(Request),
            InputHash = CompilerArtifactInputHash.Compute(Request, Request.CompilerManifest.Sha256, "SharpProof.Worker", versions.WorkerVersion, versions.WorkerBinarySha256, ApiSpecTable.DefaultTableIdentity, versions.ApiSpecVersion, versions.ApiSpecContentSha256),
            Summary = Response.Summary with { Budgets = Request.Budgets }
        };
        WriteRequest(Request);
        WriteResponse(Response);
    }

    internal EvidenceSet Load()
    {
        return EvidenceSet.Load(ResultPath, RequestPath, ManifestPath, TestContext.CurrentContext.CancellationToken);
    }
    internal void WriteRequest(WorkerVerifyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        File.WriteAllText(RequestPath, WorkerProtocolJson.SerializeRequest(request), new UTF8Encoding(false));
    }
    internal void WriteResponse(WorkerVerifyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        File.WriteAllText(ResultPath, JsonSerializer.Serialize(response, WorkerProtocolJson.SharedOptions), new UTF8Encoding(false));
    }
    public void Dispose()
    {
        Directory.Delete(Root, recursive: true);
    }
}
