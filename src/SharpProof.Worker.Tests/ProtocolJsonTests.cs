using System.Collections.Immutable;
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
internal sealed class ProtocolJsonTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ImmutableWireArrayRejectsNonArrayAndIncompleteReaderTokens(bool incomplete)
    {
        var options = WorkerProtocolJson.SharedOptions;
        var converter = (System.Text.Json.Serialization.JsonConverter<ImmutableArray<int>>)
            new ProtocolArrayJsonConverter().CreateConverter(typeof(ImmutableArray<int>), options);
        Assert.Throws<JsonException>((Action)(() =>
        {
            var input = incomplete ? "["u8 : "0"u8;
            var reader = new Utf8JsonReader(input, isFinalBlock: !incomplete, state: default);
            Assert.That(reader.Read(), Is.True);
            _ = converter.Read(ref reader, typeof(ImmutableArray<int>), options);
        }));
    }

    [Test]
    public void ImmutableIdentitySnapshotsRejectRepeatedNonblankValues()
    {
        Assert.That(WorkerProtocolJson.AreDistinctNonblank(["entry", "entry"]), Is.False);
        Assert.That(WorkerProtocolJson.AreDistinctNonblank(["entry", "other"]), Is.True);
    }

    [Test]
    public void ImmutableHashInputsRejectDefaultAndRetainTheEmptyPayloadDigest()
    {
        ImmutableArray<byte> bytes = default;
        var protocolException = Assert.Throws<ArgumentNullException>((Action)(() =>
            WorkerProtocolJson.ComputeSha256(bytes)));
        var sharedException = Assert.Throws<ArgumentNullException>((Action)(() =>
            ProtocolHashEncoding.ComputeSha256Hex(bytes)));
        var artifactException = Assert.Throws<ArgumentNullException>((Action)(() =>
            ArtifactDigest.Compute(bytes)));
        Assert.That(protocolException!.ParamName, Is.EqualTo(nameof(bytes)));
        Assert.That(sharedException!.ParamName, Is.EqualTo(nameof(bytes)));
        Assert.That(artifactException!.ParamName, Is.EqualTo("canonicalArtifactBytes"));
        const string emptyDigest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        Assert.That(WorkerProtocolJson.ComputeSha256(ImmutableArray<byte>.Empty), Is.EqualTo(emptyDigest));
        Assert.That(ArtifactDigest.Compute(ImmutableArray<byte>.Empty), Is.EqualTo(emptyDigest));
    }

    private const string InputHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly ImmutableArray<string> s_requestProperties = [
        "protocolVersion", "compilerManifest", "budgets", "cache", "verifyPolicy", "assumptionPolicy"
    ];

    [Test]
    public void SerializationCanonicalizesCopiesWithoutRewritingProducerOwners()
    {
        var manifest = CreateTwoCallableManifest();
        manifest.Callables = manifest.Callables.Reverse().ToImmutableArray();
        var response = CreateResponse(manifest);
        response = response with
        {
            ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with
            {
                ProofCore = ["z", "a"],
                EffectWitness = new WorkerEffectViolationWitness
                {
                    ExactExceptionTypeHierarchy = ["z", "a"]
                }
            })
        };
        var callables = manifest.Callables;
        var claim = response.ClaimResults[0];
        var core = claim.ProofCore;
        var witness = claim.EffectWitness!;
        var hierarchy = witness.ExactExceptionTypeHierarchy;
        var before = JsonSerializer.Serialize(response, WorkerProtocolJson.SharedOptions);

        var json = WorkerProtocolJson.SerializeResponse(response);
        using var canonical = JsonDocument.Parse(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(JsonSerializer.Serialize(response, WorkerProtocolJson.SharedOptions), Is.EqualTo(before));
            Assert.That(manifest.Callables.Equals(callables), Is.True);
            Assert.That(response.ClaimResults[0], Is.SameAs(claim));
            Assert.That(claim.ProofCore.Equals(core), Is.True);
            Assert.That(claim.EffectWitness, Is.SameAs(witness));
            Assert.That(witness.ExactExceptionTypeHierarchy.Equals(hierarchy), Is.True);
            Assert.That(canonical.RootElement.GetProperty("claimResults")[0].GetProperty("proofCore")[0].GetString(), Is.EqualTo("a"));
            Assert.That(canonical.RootElement.GetProperty("claimResults")[0].GetProperty("effectWitness").GetProperty("exactExceptionTypeHierarchy")[0].GetString(), Is.EqualTo("a"));
        }
    }
    [Test]
    public void RequiredWireArraysPreserveNullEmptyAndMalformedElementDistinctions()
    {
        var node = JsonSerializer.SerializeToNode(new WorkerClaimResult(), WorkerProtocolJson.SharedOptions)!.AsObject();
        node.Remove("model");
        Assert.Throws<JsonException>((Action)(() => JsonSerializer.Deserialize<WorkerClaimResult>(node.ToJsonString(), WorkerProtocolJson.SharedOptions)));

        node["model"] = null;
        var nullModel = JsonSerializer.Deserialize<WorkerClaimResult>(node.ToJsonString(), WorkerProtocolJson.SharedOptions)!;
        Assert.That(nullModel.Model.IsDefault, Is.True);
        Assert.That(JsonSerializer.SerializeToNode(nullModel, WorkerProtocolJson.SharedOptions)!["model"], Is.Null);
        Assert.That(WorkerProtocolJson.AreValidModel(nullModel.Model), Is.False);

        node["model"] = new JsonArray();
        var emptyModel = JsonSerializer.Deserialize<WorkerClaimResult>(node.ToJsonString(), WorkerProtocolJson.SharedOptions)!;
        Assert.That(emptyModel.Model.IsDefault, Is.False);
        Assert.That(emptyModel.Model, Is.Empty);
        Assert.That(WorkerProtocolJson.AreValidModel(emptyModel.Model), Is.True);

        node["model"] = new JsonArray((JsonNode?)null);
        var malformedModel = JsonSerializer.Deserialize<WorkerClaimResult>(node.ToJsonString(), WorkerProtocolJson.SharedOptions)!;
        Assert.That(malformedModel.Model, Has.Length.EqualTo(1));
        Assert.That(malformedModel.Model[0], Is.Null);
        Assert.That(WorkerProtocolJson.AreValidModel(malformedModel.Model), Is.False);
    }

    [Test]
    public void DefaultResponseArraysCannotBeCached()
    {
        var manifest = CreateTwoCallableManifest();
        var response = CreateResponse(manifest);
        Assert.That(VerificationCache.IsCacheable(response with { Errors = default }, InputHash, manifest), Is.False);
        Assert.That(VerificationCache.IsCacheable(response with { CallableResults = default }, InputHash, manifest), Is.False);
        Assert.That(VerificationCache.IsCacheable(response with { ClaimResults = default }, InputHash, manifest), Is.False);
    }

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
        if (measure is null)
        {
            throw CreateArgumentNullException(nameof(measure));
        }

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

    [TestCase(false)]
    [TestCase(true)]
    public void NullCallableIdentitiesProduceValidationErrors(bool nullResultIdentity)
    {
        var response = CreateResponse(CreateManifest());
        { response = response with { Manifest = response.Manifest with { Callables = response.Manifest.Callables.ReplaceAt(0, response.Manifest.Callables[0] with { CallableId = null! }) } }; }
        if (nullResultIdentity)
        {
            { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { CallableId = null! }) }; }
        }
        var validation = WorkerProtocolJson.Validate(response);
        Assert.That(validation.IsValid, Is.False);
        Assert.That(validation.Errors.Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)),
            Does.Contain("manifest.callable_id"));
    }

    [Test]
    public void ProtocolSerializersRejectDocumentsBeyondReaderLimit()
    {
        var oversizedValue = new string(
            'x',
            WorkerProtocolJson.MaximumJsonBytes);
        var request = CreateRequest();
        { request = request with { CompilerManifest = request.CompilerManifest with { Path = oversizedValue } }; }
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
                (Action)(() => WorkerProtocolJson.ComputeRequestHash(request)),
                Throws.TypeOf<InvalidDataException>());
            Assert.That(
                (Action)(() => WorkerProtocolJson.SerializeResponse(response)),
                Throws.TypeOf<InvalidDataException>());
        }
    }

    [Test]
    public void ProtocolDeserializersRejectDocumentsBeyondReaderLimit()
    {
        var json = WorkerProtocolJson.SerializeRequest(CreateRequest());
        var oversizedValue = new string(
            'x',
            WorkerProtocolJson.MaximumJsonBytes);
        var oversizedJson = json.Replace(
            "\"compiler.manifest.json\"",
            JsonSerializer.Serialize(oversizedValue),
            StringComparison.Ordinal);

        Assert.That(
            Encoding.UTF8.GetByteCount(oversizedJson),
            Is.GreaterThan(WorkerProtocolJson.MaximumJsonBytes));
        Assert.That(
            (Action)(() => WorkerProtocolJson.DeserializeRequest(oversizedJson)),
            Throws.TypeOf<JsonException>());
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
        using var temporary = new TempDirectory(
            "protocol-json-",
            TestContext.CurrentContext.WorkDirectory);
        var path = Path.Combine(
            temporary.FullName,
            "manifest.json");
        File.WriteAllBytes(path, new byte[WorkerProtocolJson.MaximumJsonBytes + 1]);

        Assert.Throws<InvalidDataException>(
            (Action)(() => WorkerProtocolJson.ReadUtf8File(path)));
        Func<Task> readAsync = () => WorkerProtocolJson.ReadUtf8FileAsync(path, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.ThrowsAsync<InvalidDataException>(readAsync);

        File.WriteAllBytes(path, [0xff]);
        Assert.Throws<DecoderFallbackException>(
            (Action)(() => WorkerProtocolJson.ReadUtf8File(path)));
    }

    [Test]
    public void BoundedReadStreamReturnsEofAtExactLimit()
    {
        using var exact = new BoundedReadStream(
            new MemoryStream([7]),
            1,
            "overflow");
        Assert.That(exact.ReadByte(), Is.EqualTo(7));
        Assert.That(exact.ReadByte(), Is.EqualTo(-1));

        using var overflow = new BoundedReadStream(
            new MemoryStream([7, 8]),
            1,
            "overflow");
        Assert.That(overflow.ReadByte(), Is.EqualTo(7));
        Assert.That(
            (Func<int>)overflow.ReadByte,
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    [Platform("Linux")]
    public void BoundedUtf8FileReaderRejectsGrowthAfterOpen()
    {
        using var temporary = new TempDirectory(
            "protocol-json-growth-",
            TestContext.CurrentContext.WorkDirectory);
        var path = Path.Combine(
            temporary.FullName,
            "manifest.json");
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
    public async Task BoundedUtf8FileReaderRejectsFifoBeforeBlockingOpen()
    {
        using var temporary = new TempDirectory(
            "protocol-json-fifo-",
            TestContext.CurrentContext.WorkDirectory);
        var path = Path.Combine(
            temporary.FullName,
            "manifest.fifo");
        using (var process = System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo
            {
                FileName = "mkfifo",
                UseShellExecute = false,
                ArgumentList = { path }
            })!)
        {
            await process.WaitForExitAsync(TestContext.CurrentContext.CancellationToken);
            Assert.That(process.ExitCode, Is.Zero);
        }

        var read = Task.Run(() =>
        {
            try
            {
                _ = WorkerProtocolJson.ReadUtf8File(path);
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        });
        var completedBeforeTimeout = false;
        try
        {
            try
            {
                await read.WaitAsync(TimeSpan.FromMilliseconds(500));
                completedBeforeTimeout = true;
            }
            catch (TimeoutException)
            {
                // Cleanup below releases a regressed blocking FIFO read.
            }
        }
        finally
        {
            try
            {
                if (!read.IsCompleted)
                {
                    using (var writer = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.ReadWrite,
                        FileShare.ReadWrite))
                    {
                        writer.WriteByte((byte)'{');
                    }
                    await read.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            finally
            {
                // Always join the operation before disposing the directory it reads.
                await read;
            }
        }

        Assert.That(
            completedBeforeTimeout,
            Is.True,
            "Opening a FIFO must not wait for a writer.");
        Assert.That(await read, Is.True,
            "The FIFO must be rejected with InvalidDataException.");
    }
    [Test]
    public void RequestCarriesOnlyArtifactAndRuntimeControls()
    {
        var request = CreateRequest();
        var json = WorkerProtocolJson.SerializeRequest(request);
        var roundTrip = WorkerProtocolJson.DeserializeRequest(json)!;
        using var document = JsonDocument.Parse(json);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolVersions.Current, Is.EqualTo("43"));
            Assert.That(WorkerCacheVersions.Current, Is.EqualTo(62));
            Assert.That(WorkerManifestVersions.Current, Is.EqualTo(6));
            Assert.That(WorkerResourceModels.Program, Is.EqualTo(SharpProof.Ir.ResourceProgram.ModelVersion));
            Assert.That(WorkerResourceModels.Expression, Is.EqualTo(SharpProof.Ir.ResourceExpression.ModelVersion));
            Assert.That(WorkerResourceModels.AllocationLayout, Is.EqualTo(SharpProof.Ir.ResourceRuntimeLayout.ModelId));
            Assert.That(WorkerResourceModels.LifetimeCostModel, Is.EqualTo(WorkerResourceModels.AllocationLayout + ":" + SharpProof.Ir.ResourceMemoryTrace.ModelId + ":normal-completion"));
            Assert.That(
                document.RootElement.EnumerateObject()
                    .Select(static property => ((property.Value.ValueKind == System.Text.Json.JsonValueKind.Undefined) ? throw new System.ArgumentException("Value must be initialized.", nameof(property)) : (property.Name))),
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

        { request = request with { VerifyPolicy = WorkerVerifyPolicy.Unspecified }; }
        { request = request with { AssumptionPolicy = (WorkerAssumptionPolicy)999 }; }
        { request = request with { CompilerManifest = request.CompilerManifest with { Sha256 = InputHash.ToUpperInvariant() } }; }
        AssertErrorCode(
            WorkerProtocolJson.Validate(request),
            ["policy.verify",
            "policy.assumption",
            "project.compiler_manifest"]);
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
                    CompilerManifestArtifactJson.Deserialize(json, cancellationToken: TestContext.CurrentContext.CancellationToken)),
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
            TestContext.CurrentContext.CancellationToken);
        var json = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var roundTrip = CompilerManifestArtifactJson.Deserialize(json, cancellationToken: TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                roundTrip.SchemaVersion,
                Is.EqualTo(CompilerManifestArtifactVersions.Current));
            Assert.That(roundTrip.ProtocolVersion, Is.EqualTo("43"));
            Assert.That(roundTrip.Manifest.Hash, Is.EqualTo(manifest.Hash));
            Assert.That(roundTrip.Manifest.Callables[0].Assumptions, Has.Length.EqualTo(2));
            Assert.That(
                roundTrip.Manifest.Callables[0].Assumptions
                    .Select(static assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind)),
                Is.EqualTo(WorkerTestData.UserAndTrustedAssumptions));
            Assert.That(roundTrip.Compilation.SyntaxTrees, Is.Empty);
            Assert.That(ArtifactDigest.Compute((Encoding.UTF8.GetBytes(json)).ToImmutableArray()), Does.Match("^[0-9a-f]{64}$"));
            Assert.That(WorkerProtocolJson.ManifestsEqual(
                roundTrip.Manifest, manifest), Is.True);
        }
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(json.Replace(
                $"\"schemaVersion\":{CompilerManifestArtifactVersions.Current}",
                $"\"schemaVersion\":{CompilerManifestArtifactVersions.Current},\"schemaVersion\":{CompilerManifestArtifactVersions.Current}",
                StringComparison.Ordinal), cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(json.Replace(
                "\"features\":\"All\"",
                "\"features\":1",
                StringComparison.Ordinal), cancellationToken: TestContext.CurrentContext.CancellationToken)));

        roundTrip = roundTrip with { Manifest = roundTrip.Manifest with { Claims = roundTrip.Manifest.Claims.ReplaceAt(0, roundTrip.Manifest.Claims[0] with { Location = roundTrip.Manifest.Claims[0].Location with { Column = roundTrip.Manifest.Claims[0].Location.Column + 1 } }) } };
        Assert.That(WorkerProtocolJson.ManifestsEqual(
            roundTrip.Manifest, manifest), Is.False);
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(roundTrip, cancellationToken: TestContext.CurrentContext.CancellationToken), cancellationToken: TestContext.CurrentContext.CancellationToken)));
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

        manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Location = manifest.Claims[0].Location with { Column = manifest.Claims[0].Location.Column + 1 } }) };
        Assert.That(
            WorkerProtocolJson.ComputeManifestHash(manifest),
            Is.Not.EqualTo(hash));
        AssertErrorCode(
            WorkerProtocolJson.Validate(CreateResponse(manifest)),
            ["manifest.hash"]);
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
                    "9fe651b6cd960c84008824d7bbc9d1ce4" +
                    "95088c6d9b0413a0a19763123a25886"));
        }
    }

    [Test]
    public void ManifestHashIsSensitiveToEveryManifestEnum()
    {
        var hashBuilder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        hashBuilder.Add(CreateManifest().Hash);
        hashBuilder.Add(ManifestHashAfter(static manifest =>
                { System.ArgumentNullException.ThrowIfNull(manifest); manifest = manifest with { Callables = manifest.Callables.ReplaceAt(0, manifest.Callables[0] with { SelectedFeatures = [WorkerSelectedFeature.Effects] }) }; return manifest; }));
        hashBuilder.Add(ManifestHashAfter(static manifest =>
                { System.ArgumentNullException.ThrowIfNull(manifest); manifest = manifest with { Callables = manifest.Callables.ReplaceAt(0, manifest.Callables[0] with { SelectionReasons = [WorkerSelectionReason.ExplicitAnnotation] }) }; return manifest; }));
        hashBuilder.Add(ManifestHashAfter(static manifest =>
                { System.ArgumentNullException.ThrowIfNull(manifest); manifest = manifest with { Callables = manifest.Callables.ReplaceAt(0, manifest.Callables[0] with { Assumptions = manifest.Callables[0].Assumptions.ReplaceAt(0, manifest.Callables[0].Assumptions[0] with { Kind = WorkerAssumptionKind.Precondition }) }) }; return manifest; }));
        hashBuilder.Add(ManifestHashAfter(static manifest =>
                { System.ArgumentNullException.ThrowIfNull(manifest); manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Kind = WorkerClaimKind.Effect }) }; return manifest; }));
        hashBuilder.Add(ManifestHashAfter(static manifest =>
                { System.ArgumentNullException.ThrowIfNull(manifest); manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Evidence = WorkerClaimEvidence.CompanionClause }) }; return manifest; }));
        hashBuilder.Add(ManifestHashAfter(static manifest =>
                { System.ArgumentNullException.ThrowIfNull(manifest); manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { EffectContractKind = WorkerEffectContractKind.DoesNotThrow }) }; return manifest; }));
        var hashes = hashBuilder.ToImmutable();

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
                    , ["response.manifest_mismatch"]);
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
        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Kind = WorkerClaimKind.Effect }) }; }
        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Evidence = WorkerClaimEvidence.Attribute }) }; }
        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { EffectContractKind = WorkerEffectContractKind.DoesNotThrow }) }; }
        WorkerProtocolJson.SealManifest(manifest);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolJson.ValidateManifest(manifest).IsValid,
                Is.True);
            Assert.That(manifest.Hash, Is.Not.EqualTo(postconditionHash));
        }
        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Evidence = WorkerClaimEvidence.DirectClause }) }; }
        WorkerProtocolJson.SealManifest(manifest);
        AssertErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            ["manifest.claim_shape"]);

        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Kind = WorkerClaimKind.Postcondition }) }; }
        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Evidence = WorkerClaimEvidence.DirectClause }) }; }
        WorkerProtocolJson.SealManifest(manifest);
        AssertErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            ["manifest.claim_shape"]);
    }

    [Test]
    public void EffectCertaintyMustAgreeWithOutcomeAndUnknownReason()
    {
        var manifest = CreateEffectManifest();
        var response = CreateResponse(manifest);

        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Outcome = WorkerClaimOutcome.Refuted }) }; }
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.DefiniteViolation }) }; }
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectWitness = CreateEffectWitness(manifest.Claims[0].Location) }) }; }
        { response = response with { Summary = CreateSummary(response) }; }
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectWitness = response.ClaimResults[0].EffectWitness! with { Effects = WorkerEffectSet.Allocates } }) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.effect_witness"]);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectWitness = null }) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.effect_witness"]);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectWitness = CreateEffectWitness(manifest.Claims[0].Location) }) }; }
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.CompleteMayEffectSummary }) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.effect_certainty"]);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Outcome = WorkerClaimOutcome.Proven }) }; }
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectWitness = null }) }; }
        { response = response with { Summary = CreateSummary(response) }; }

        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary }) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.effect_certainty"]);

        response = SetUnknown(response, WorkerClaimReason.EffectSummaryIncomplete);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        { response = response with { Summary = CreateSummary(response) }; }
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Reason = WorkerClaimReason.EffectContractNotEstablished }) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.effect_certainty"]);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.CompleteMayEffectSummary }) }; }
        { response = response with { Summary = CreateSummary(response) }; }
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
        response = SetUnknown(response, reason);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = certainty }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        if (certainty == WorkerEffectEvidenceCertainty.TrustedCompleteBoundary)
        {
            {
                response = response with
                {
                    ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with
                    {
                        Assumptions = response.ClaimResults[0].Assumptions.ReplaceReference(response.ClaimResults[0].Assumptions.Single(static assumption =>
                assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.TrustedBoundary)), response.ClaimResults[0].Assumptions.Single(static assumption =>
                assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.TrustedBoundary)) with
                { Used = true })
                    })
                };
            }
        }
        { response = response with { Summary = CreateSummary(response) }; }

        var validation = WorkerProtocolJson.Validate(response);

        Assert.That(
            validation.Errors.Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)),
            Is.Empty);
    }

    [Test]
    public void VacuityEvidenceIsClosedAndRequiresProofCore()
    {
        var response = CreateResponse(CreateManifest());
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Vacuity = WorkerVacuityKind.ContradictoryPreconditions }) }; }
        response.ClaimResults[0].ProofCore = ["requires:0"];
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        response = SetUnknown(response, WorkerClaimReason.UnsupportedBody);
        response.ClaimResults[0].ProofCore = [];
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        { response = response with { Summary = CreateSummary(response) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.vacuity"]);

        var effectManifest = CreateEffectManifest();
        response = CreateResponse(effectManifest);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.VacuousEntry }) }; }
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Vacuity = WorkerVacuityKind.ContradictoryPreconditions }) }; }
        response.ClaimResults[0].ProofCore = ["requires:0"];
        Assert.That(
            WorkerProtocolJson.Validate(response).IsValid,
            Is.True);

        response.ClaimResults[0].ProofCore = [];
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.vacuity_evidence"]);

        response.ClaimResults[0].ProofCore = [
            "body:normal-completion"
        ];
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Vacuity = WorkerVacuityKind.NoModeledNormalReturn }) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.vacuity"]);
    }

    [Test]
    public void EffectEvidenceTupleRequiresVacuousEntryContradictionAndCore()
    {
        var manifest = CreateEffectManifest();
        var response = CreateResponse(manifest);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.VacuousEntry }) }; }
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Vacuity = WorkerVacuityKind.None }) }; }
        response.ClaimResults[0].ProofCore = [];
        { response = response with { Summary = CreateSummary(response) }; }

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.effect_evidence"]);

        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Vacuity = WorkerVacuityKind.ContradictoryPreconditions }) }; }
        response.ClaimResults[0].ProofCore = ["requires:0"];
        { response = response with { Summary = CreateSummary(response) }; }
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void EffectEvidenceTupleRejectsNonVacuousContradiction()
    {
        var response = CreateResponse(CreateEffectManifest());
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.CompleteMayEffectSummary }) }; }
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Vacuity = WorkerVacuityKind.ContradictoryPreconditions }) }; }
        response.ClaimResults[0].ProofCore = ["requires:0"];
        { response = response with { Summary = CreateSummary(response) }; }

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.effect_evidence"]);
    }

    [Test]
    public void TrustedEffectEvidenceRequiresUsedTrustedBoundary()
    {
        var response = CreateResponse(CreateEffectManifest());
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.TrustedCompleteBoundary }) }; }
        { response = response with { Summary = CreateSummary(response) }; }

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.effect_evidence"]);

        response = response with
        {
            ClaimResults = [.. response.ClaimResults.Select(static result =>
                result is null ? throw new ArgumentNullException(nameof(result)) : result with
                {
                    Assumptions = [.. result.Assumptions.Select(static value =>
                        value is null ? throw new ArgumentNullException(nameof(value)) :
                            value.Kind == WorkerAssumptionKind.TrustedBoundary ? value with { Used = true } : value)]
                })]
        };
        { response = response with { Summary = CreateSummary(response) }; }
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
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.TrustedCompleteBoundary }) }; }
        { response = response with { Summary = CreateSummary(response) }; }

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.effect_evidence"]);
    }

    [Test]
    public void CallableAssumptionUsageMustRemainClaimAuthoritative()
    {
        var response = CreateResponse(CreateManifest());
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Assumptions = response.CallableResults[0].Assumptions.ReplaceAt(0, response.CallableResults[0].Assumptions[0] with { Used = true }) }) }; }
        { response = response with { Summary = CreateSummary(response) }; }

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.callable_assumption_usage"]);
    }

    [Test]
    public void AssumptionIdentityIsGlobalAcrossCallables()
    {
        var manifest = CreateTwoCallableManifest();
        { manifest = manifest with { Callables = manifest.Callables.ReplaceAt(1, manifest.Callables[1] with { Assumptions = manifest.Callables[1].Assumptions.ReplaceAt(0, manifest.Callables[1].Assumptions[0] with { Id = manifest.Callables[0].Assumptions[0].Id }) }) }; }
        WorkerProtocolJson.SealManifest(manifest);

        AssertErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            ["manifest.assumption_identity"]);
    }

    [Test]
    public void AssumptionIdentityRejectsKindMutationAcrossCallables()
    {
        var manifest = CreateTwoCallableManifest();
        { manifest = manifest with { Callables = manifest.Callables.ReplaceAt(1, manifest.Callables[1] with { Assumptions = manifest.Callables[1].Assumptions.ReplaceAt(0, manifest.Callables[1].Assumptions[0] with { Id = manifest.Callables[0].Assumptions[0].Id }) }) }; }
        { manifest = manifest with { Callables = manifest.Callables.ReplaceAt(1, manifest.Callables[1] with { Assumptions = manifest.Callables[1].Assumptions.ReplaceAt(0, manifest.Callables[1].Assumptions[0] with { Kind = WorkerAssumptionKind.TrustedBoundary }) }) }; }
        WorkerProtocolJson.SealManifest(manifest);

        AssertErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            ["manifest.assumption_identity"]);
    }

    [Test]
    public void ManifestAssumptionKindMustRemainProducerClosed()
    {
        var manifest = CreateManifest();
        { manifest = manifest with { Callables = manifest.Callables.ReplaceAt(0, manifest.Callables[0] with { Assumptions = manifest.Callables[0].Assumptions.ReplaceAt(0, manifest.Callables[0].Assumptions[0] with { Kind = WorkerAssumptionKind.ApiSpecification }) }) }; }
        WorkerProtocolJson.SealManifest(manifest);

        AssertContainsErrorCode(
            WorkerProtocolJson.ValidateManifest(manifest),
            ["manifest.assumption_kind"]);
    }

    [Test]
    public void StrictResponseValidationRequiresExactManifestAndResultSets()
    {
        var expected = CreateManifest();
        var response = CreateResponse(expected);
        var request = CreateRequest();
        { response = response with { RequestHash = WorkerProtocolJson.ComputeRequestHash(request) }; }
        Assert.That(
            WorkerProtocolJson.Validate(response, InputHash, expected).IsValid,
            Is.True);
        Assert.That(WorkerProtocolJson.ValidateForRequest(
            response, response.RequestHash, InputHash, expected,
            request, CreateExpectedVersions()).IsValid, Is.True);
        { request = request with { VerifyPolicy = WorkerVerifyPolicy.WarnOnUnknown }; }
        AssertErrorCode(WorkerProtocolJson.ValidateForRequest(
                response, WorkerProtocolJson.ComputeRequestHash(request),
                InputHash, expected, request,
                CreateExpectedVersions())
            , ["response.request_mismatch"]);

        response.ClaimResults = [];
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response, InputHash, expected),
            ["response.claim_set"]);

        response = CreateResponse(expected);
        response.ClaimResults = [
            response.ClaimResults[0],
            response.ClaimResults[0]
        ];
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response, InputHash, expected),
            ["response.result_claim_id"]);

        response = CreateResponse(expected);
        var other = CreateManifest();
        other = other with { Claims = other.Claims.ReplaceAt(0, other.Claims[0] with { Location = other.Claims[0].Location with { Start = other.Claims[0].Location.Start + 1 } }) };
        WorkerProtocolJson.SealManifest(other);
        AssertErrorCode(
            WorkerProtocolJson.Validate(response, InputHash, other),
            ["response.manifest_mismatch"]);
        AssertErrorCode(WorkerProtocolJson.Validate(
                    response,
                    new string('b', InputHash.Length),
                    expected)
                , ["response.input_mismatch"]);
    }

    [TestCase(nameof(WorkerVersionSummary.WorkerVersion))]
    [TestCase(nameof(WorkerVersionSummary.ApiSpecVersion))]
    [TestCase(nameof(WorkerVersionSummary.WorkerBinarySha256))]
    [TestCase(nameof(WorkerVersionSummary.ApiSpecContentSha256))]
    public void RequestBoundValidationAuthenticatesRuntimeProvenance(
        string propertyName)
    {
        if (propertyName is null)
        {
            throw CreateArgumentNullException(nameof(propertyName));
        }

        ArgumentException.ThrowIfNullOrEmpty(propertyName);
        var response = CreateResponse(CreateManifest());
        var expected = CreateExpectedVersions();
        var property = typeof(WorkerVersionSummary).GetProperty(propertyName)!;
        property.SetValue(
            response.Summary.Versions,
            propertyName.EndsWith("Sha256", StringComparison.Ordinal)
                ? new string('c', 64)
                : "FABRICATED-version");

        AssertErrorCode(ValidateForRequest(response), ["response.versions_mismatch"]);

        property.SetValue(
            response.Summary.Versions,
            property.GetValue(expected));
        Assert.That(ValidateForRequest(response).IsValid, Is.True);
    }

    [Test]
    public void RequestBoundValidationRejectsCrossSwappedRuntimeDigests()
    {
        var response = CreateResponse(CreateManifest());
        {
            response = response with
            {
                Summary = response.Summary with
                {
                    Versions = response.Summary.Versions with
                    {
                        WorkerBinarySha256 = response.Summary.Versions.ApiSpecContentSha256,
                        ApiSpecContentSha256 = response.Summary.Versions.WorkerBinarySha256
                    }
                }
            };
        }

        AssertErrorCode(ValidateForRequest(response), ["response.versions_mismatch"]);
    }

    [TestCase(nameof(WorkerBudgets.QueryRlimit))]
    [TestCase(nameof(WorkerBudgets.MethodRlimit))]
    [TestCase(nameof(WorkerBudgets.MethodWallTimeMilliseconds))]
    [TestCase(nameof(WorkerBudgets.ProjectWallTimeMilliseconds))]
    [TestCase(nameof(WorkerBudgets.MaxParallelism))]
    [TestCase(nameof(WorkerBudgets.MaximumExpressionDepth))]
    public void RequestValidationBindsEverySummaryBudget(string propertyName)
    {
        System.ArgumentNullException.ThrowIfNull(propertyName);
        var request = CreateRequest();
        var response = CreateResponse(CreateManifest());
        { response = response with { RequestHash = WorkerProtocolJson.ComputeRequestHash(request) }; }
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
                , ["response.budgets_mismatch"]);
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
        var cases = ImmutableArray.Create<(string Json, Func<JsonObject, JsonObject> Select, string Property)>(
            (requestJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["compilerManifest"]!.AsObject()), "path"),
            (requestJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["budgets"]!.AsObject()), "queryRlimit"),
            (requestJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["cache"]!.AsObject()), "enabled"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["manifest"]!.AsObject()), "schemaVersion"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["manifest"]!["callables"]![0]!.AsObject()), "callableId"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["manifest"]!["callables"]![0]!["location"]!.AsObject()), "path"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["manifest"]!["callables"]![0]!["assumptions"]![0]!.AsObject()), "id"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["manifest"]!["claims"]![0]!.AsObject()), "claimId"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["callableResults"]![0]!.AsObject()), "coverage"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["claimResults"]![0]!.AsObject()), "outcome"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["claimResults"]![0]!["effectWitness"]!.AsObject()), "kind"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["claimResults"]![0]!["model"]![0]!.AsObject()), "variable"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["summary"]!.AsObject()), "cacheStatus"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["summary"]!["versions"]!.AsObject()), "protocolVersion"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["summary"]!["budgets"]!.AsObject()), "queryRlimit"),
            (responseJson, static root => root is null ? throw new System.ArgumentNullException(nameof(root)) : (root["errors"]![0]!.AsObject()), "code"));

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
    public void ProtocolJsonRejectsMissingUnknownAndMistypedProperties()
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
        var extra = JsonNode.Parse(responseJson)!.AsObject();
        extra["manifest"]!.AsObject()["futureField"] = true;
        var arraySwap = JsonNode.Parse(responseJson)!.AsObject();
        arraySwap["summary"]!.AsObject()["budgets"] = new JsonArray();

        using (Assert.EnterMultipleScope())
        {
            foreach (var invalid in ImmutableArray.Create(caseVariant, numericString, extra.ToJsonString(), arraySwap.ToJsonString()))
            {
                Assert.Throws<JsonException>((Action)(() =>
                    WorkerProtocolJson.DeserializeResponse(invalid)));
            }
        }
    }

    [Test]
    public void ProtocolJsonAcceptsUnorderedObjectsAndValidatesNullElementsSemantically()
    {
        var responseJson = WorkerProtocolJson.SerializeResponse(CreateResponse(CreateManifest()));
        var root = JsonNode.Parse(responseJson)!.AsObject();
        var manifest = root["manifest"]!.AsObject();
        var schema = manifest["schemaVersion"]!.DeepClone();
        manifest.Remove("schemaVersion");
        manifest["schemaVersion"] = schema;
        var response = WorkerProtocolJson.DeserializeResponse(root.ToJsonString())!;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        root["manifest"]!["callables"]!.AsArray().Insert(0, null);
        response = WorkerProtocolJson.DeserializeResponse(root.ToJsonString())!;
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False);
        Assert.Throws<JsonException>((Action)(() => WorkerProtocolJson.DeserializeResponse(
            responseJson.Insert(1, $"\"protocolVersion\":\"{WorkerProtocolVersions.Current}\","))));
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
        response = SetUnknown(response, WorkerClaimReason.UnsupportedBody);
        { response = response with { Summary = CreateSummary(response) }; }

        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.unknown_coverage"]);

        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void FatalClaimReasonsRequireFailedRun()
    {
        var response = CreateResponse(CreateManifest());
        response = SetUnknown(response, WorkerClaimReason.BackendUnavailable);
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        { response = response with { Summary = CreateSummary(response) }; }

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.run_projection"]);

        { response = response with { RunStatus = WorkerRunStatus.Failed }; }
        { response = response with { FailureReason = WorkerRunFailureReason.BackendUnavailable }; }
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void CallableFailureAndTimeoutReasonsConstrainRunStatus()
    {
        var response = CreateResponse(CreateManifest());
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.InfrastructureFailure }) }; }

        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.run_projection"]);

        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.MethodTimeout }) }; }
        response = SetUnknown(response, WorkerClaimReason.MethodTimeout);
        { response = response with { Summary = CreateSummary(response) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.run_projection"]);

        { response = response with { RunStatus = WorkerRunStatus.TimedOut }; }
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        { response = response with { RunStatus = WorkerRunStatus.Complete }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.Canceled }) }; }
        response = SetUnknown(response, WorkerClaimReason.Canceled);
        { response = response with { Summary = CreateSummary(response) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.run_projection"]);

        { response = response with { RunStatus = WorkerRunStatus.Canceled }; }
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [TestCase(WorkerRunStatus.TimedOut)]
    [TestCase(WorkerRunStatus.Canceled)]
    public void AllProvenEvidenceRejectsFabricatedInterruptedStatus(
        WorkerRunStatus status)
    {
        var response = CreateResponse(CreateManifest());
        { response = response with { RunStatus = status }; }

        AssertErrorCode(ValidateForRequest(response), ["response.run_projection"]);
    }

    [Test]
    public void AllProvenEvidenceRejectsFabricatedFailureStatus()
    {
        var response = CreateResponse(CreateManifest());
        { response = response with { RunStatus = WorkerRunStatus.Failed }; }
        { response = response with { FailureReason = WorkerRunFailureReason.BackendUnavailable }; }

        AssertErrorCode(ValidateForRequest(response), ["response.run_projection"]);
    }

    [Test]
    public void CallableCoverageIsAnExactProjectionOfOwnedClaims()
    {
        var response = CreateResponse(CreateManifest());
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }

        AssertErrorCode(ValidateForRequest(response), ["response.callable_projection"]);

        response = SetUnknown(response, WorkerClaimReason.UnsupportedBody);
        { response = response with { Summary = CreateSummary(response) }; }
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
        response = SetUnknown(response, WorkerClaimReason.UnsupportedContract);
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.UnsupportedContract }) }; }
        { response = response with { Summary = CreateSummary(response) }; }

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
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = reason }) }; }
        if (reason == WorkerCallableCoverageReason.InfrastructureFailure)
        {
            { response = response with { RunStatus = WorkerRunStatus.Failed }; }
            { response = response with { FailureReason = WorkerRunFailureReason.InfrastructureFailure }; }
        }

        Assert.That(ValidateForRequest(response).IsValid, Is.True);
    }

    [Test]
    public void ClaimReasonsAreBoundToClaimKind()
    {
        var response = CreateResponse(CreateManifest());
        response = SetUnknown(response, WorkerClaimReason.EffectSummaryIncomplete);
        { response = response with { Summary = CreateSummary(response) }; }
        AssertContainsErrorCode(ValidateForRequest(response), ["response.claim_reason"]);

        var effectManifest = CreateManifest();
        effectManifest.Callables[0].SelectedFeatures = [WorkerSelectedFeature.Effects];
        effectManifest.Callables[0].SelectionReasons = [
            WorkerSelectionReason.ExplicitAnnotation
        ];
        { effectManifest = effectManifest with { Claims = effectManifest.Claims.ReplaceAt(0, effectManifest.Claims[0] with { Kind = WorkerClaimKind.Effect }) }; }
        { effectManifest = effectManifest with { Claims = effectManifest.Claims.ReplaceAt(0, effectManifest.Claims[0] with { Evidence = WorkerClaimEvidence.Attribute }) }; }
        { effectManifest = effectManifest with { Claims = effectManifest.Claims.ReplaceAt(0, effectManifest.Claims[0] with { EffectContractKind = WorkerEffectContractKind.EnforcePure }) }; }
        WorkerProtocolJson.SealManifest(effectManifest);
        response = CreateResponse(effectManifest);
        response = SetUnknown(response, WorkerClaimReason.DeepPostcondition);
        { response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { EffectCertainty = WorkerEffectEvidenceCertainty.Unavailable }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        { response = response with { Summary = CreateSummary(response) }; }
        AssertContainsErrorCode(ValidateForRequest(response), ["response.claim_reason"]);
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
        System.ArgumentNullException.ThrowIfNull(code);
        var response = CreateResponse(CreateManifest());
        { response = response with { RunStatus = WorkerRunStatus.Failed }; }
        { response = response with { FailureReason = reason }; }
        response.Errors = [new WorkerProtocolError { Code = code, Message = "failure" }];

        AssertContainsErrorCode(ValidateForRequest(response), ["response.run_projection"]);
    }

    [Test]
    public void ExactRunProjectionAcceptsSemanticProducerStatesAndCacheHits()
    {
        var complete = CreateResponse(CreateManifest());
        Assert.That(ValidateForRequest(complete).IsValid, Is.True);

        { complete = complete with { ClaimResults = complete.ClaimResults.ReplaceAt(0, complete.ClaimResults[0] with { Outcome = WorkerClaimOutcome.Refuted }) }; }
        { complete = complete with { Summary = CreateSummary(complete) }; }
        { complete = complete with { Summary = complete.Summary with { CacheStatus = WorkerCacheStatus.Written } }; }
        Assert.That(ValidateForRequest(complete).IsValid, Is.True);

        var unknown = CreateResponse(CreateManifest());
        unknown = SetUnknown(unknown, WorkerClaimReason.UnsupportedBody);
        { unknown = unknown with { CallableResults = unknown.CallableResults.ReplaceAt(0, unknown.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { unknown = unknown with { CallableResults = unknown.CallableResults.ReplaceAt(0, unknown.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        { unknown = unknown with { Summary = CreateSummary(unknown) }; }
        Assert.That(ValidateForRequest(unknown).IsValid, Is.True);

        var timedOut = CreateResponse(CreateManifest());
        timedOut = SetUnknown(timedOut, WorkerClaimReason.MethodTimeout);
        { timedOut = timedOut with { CallableResults = timedOut.CallableResults.ReplaceAt(0, timedOut.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { timedOut = timedOut with { CallableResults = timedOut.CallableResults.ReplaceAt(0, timedOut.CallableResults[0] with { Reason = WorkerCallableCoverageReason.MethodTimeout }) }; }
        { timedOut = timedOut with { RunStatus = WorkerRunStatus.TimedOut }; }
        { timedOut = timedOut with { Summary = CreateSummary(timedOut) }; }
        Assert.That(ValidateForRequest(timedOut).IsValid, Is.True);

        var canceled = CreateResponse(CreateManifest());
        canceled = SetUnknown(canceled, WorkerClaimReason.Canceled);
        { canceled = canceled with { CallableResults = canceled.CallableResults.ReplaceAt(0, canceled.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { canceled = canceled with { CallableResults = canceled.CallableResults.ReplaceAt(0, canceled.CallableResults[0] with { Reason = WorkerCallableCoverageReason.Canceled }) }; }
        { canceled = canceled with { RunStatus = WorkerRunStatus.Canceled }; }
        { canceled = canceled with { Summary = CreateSummary(canceled) }; }
        Assert.That(ValidateForRequest(canceled).IsValid, Is.True);

        var backend = CreateResponse(CreateManifest());
        backend = SetUnknown(backend, WorkerClaimReason.BackendUnavailable);
        { backend = backend with { CallableResults = backend.CallableResults.ReplaceAt(0, backend.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { backend = backend with { CallableResults = backend.CallableResults.ReplaceAt(0, backend.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        { backend = backend with { RunStatus = WorkerRunStatus.Failed }; }
        { backend = backend with { FailureReason = WorkerRunFailureReason.BackendUnavailable }; }
        { backend = backend with { Summary = CreateSummary(backend) }; }
        Assert.That(ValidateForRequest(backend).IsValid, Is.True);

        var replay = CreateResponse(CreateManifest());
        replay = SetUnknown(replay, WorkerClaimReason.CounterexampleReplayFailed);
        { replay = replay with { CallableResults = replay.CallableResults.ReplaceAt(0, replay.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { replay = replay with { CallableResults = replay.CallableResults.ReplaceAt(0, replay.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        { replay = replay with { RunStatus = WorkerRunStatus.Failed }; }
        { replay = replay with { FailureReason = WorkerRunFailureReason.CounterexampleReplayFailed }; }
        { replay = replay with { Summary = CreateSummary(replay) }; }
        Assert.That(ValidateForRequest(replay).IsValid, Is.True);

        { complete = complete with { Summary = complete.Summary with { CacheStatus = WorkerCacheStatus.Hit } }; }
        { complete = complete with { Summary = complete.Summary with { CacheHit = true } }; }
        Assert.That(ValidateForRequest(complete).IsValid, Is.True);
    }

    [Test]
    public void RequestBoundValidationRejectsUnknownCacheStatus()
    {
        var response = CreateResponse(CreateManifest());
        { response = response with { Summary = response.Summary with { CacheStatus = (WorkerCacheStatus)int.MaxValue } }; }
        AssertContainsErrorCode(ValidateForRequest(response), ["response.cache_request_mismatch"]);
    }

    [Test]
    public void ResponseValidationRejectsUnknownRunStatus()
    {
        var response = CreateResponse(CreateManifest());
        { response = response with { RunStatus = (WorkerRunStatus)int.MaxValue }; }
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.False);
        Assert.That(WorkerProtocolMetadata.MatchesRunFailure(response.RunStatus,
            WorkerRunFailureReason.None), Is.False);
    }

    [Test]
    public void RequestBoundValidationRequiresProducerCompatibleCacheStates()
    {
        var activeRequest = CreateRequest();
        var proven = CreateResponse(CreateManifest());
        AssertCacheState(activeRequest, proven, WorkerCacheStatus.Miss, true);
        AssertCacheState(activeRequest, proven, WorkerCacheStatus.Disabled, true);
        AssertCacheState(activeRequest, proven, WorkerCacheStatus.Hit, true);
        AssertCacheState(activeRequest, proven, WorkerCacheStatus.Written, true);

        var inactiveRequest = CreateRequest();
        { inactiveRequest = inactiveRequest with { Cache = inactiveRequest.Cache with { Enabled = false } }; }
        AssertCacheState(inactiveRequest, proven, WorkerCacheStatus.Disabled, true);
        AssertCacheState(inactiveRequest, proven, WorkerCacheStatus.Miss, false);
        AssertCacheState(inactiveRequest, proven, WorkerCacheStatus.Unavailable, false);

        var provenOnlyRequest = CreateRequest();
        { provenOnlyRequest = provenOnlyRequest with { VerifyPolicy = WorkerVerifyPolicy.RequireProven }; }
        AssertCacheState(provenOnlyRequest, proven, WorkerCacheStatus.Hit, true);
        AssertCacheState(provenOnlyRequest, proven, WorkerCacheStatus.Disabled, true);

        var refuted = CreateResponse(CreateManifest());
        { refuted = refuted with { ClaimResults = refuted.ClaimResults.ReplaceAt(0, refuted.ClaimResults[0] with { Outcome = WorkerClaimOutcome.Refuted }) }; }
        { refuted = refuted with { Summary = CreateSummary(refuted) }; }
        AssertCacheState(activeRequest, refuted, WorkerCacheStatus.Hit, true);
        AssertCacheState(activeRequest, refuted, WorkerCacheStatus.Written, true);
        AssertCacheState(activeRequest, refuted, WorkerCacheStatus.Unavailable, true);
        AssertCacheState(activeRequest, refuted, WorkerCacheStatus.Miss, true);

        var unknown = CreateResponse(CreateManifest());
        unknown = SetUnknown(unknown, WorkerClaimReason.UnsupportedBody);
        { unknown = unknown with { CallableResults = unknown.CallableResults.ReplaceAt(0, unknown.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { unknown = unknown with { CallableResults = unknown.CallableResults.ReplaceAt(0, unknown.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        { unknown = unknown with { Summary = CreateSummary(unknown) }; }
        AssertCacheState(activeRequest, unknown, WorkerCacheStatus.Miss, true);
        AssertCacheState(activeRequest, unknown, WorkerCacheStatus.Unavailable, true);
        AssertCacheState(activeRequest, unknown, WorkerCacheStatus.Hit, true);
        AssertCacheState(activeRequest, unknown, WorkerCacheStatus.Written, true);

        var earlyFailureManifest = CreateManifest();
        earlyFailureManifest.Callables = [];
        earlyFailureManifest.Claims = [];
        WorkerProtocolJson.SealManifest(earlyFailureManifest);
        var earlyFailure = CreateResponse(earlyFailureManifest);
        { earlyFailure = earlyFailure with { RunStatus = WorkerRunStatus.Failed }; }
        { earlyFailure = earlyFailure with { FailureReason = WorkerRunFailureReason.InputUnavailable }; }
        earlyFailure.Errors = [new WorkerProtocolError {
            Code = "input.unavailable",
            Message = "failure"
        }];
        AssertCacheState(activeRequest, earlyFailure, WorkerCacheStatus.Disabled, true);

        var malformed = CreateResponse(earlyFailureManifest);
        { malformed = malformed with { RunStatus = WorkerRunStatus.Failed }; }
        { malformed = malformed with { FailureReason = WorkerRunFailureReason.MalformedResult }; }
        malformed.Errors = [new WorkerProtocolError {
            Code = "response.claim_set",
            Message = "failure"
        }];
        AssertCacheState(activeRequest, malformed, WorkerCacheStatus.Rejected, true);
        { malformed = malformed with { FailureReason = WorkerRunFailureReason.InfrastructureFailure }; }
        { malformed = malformed with { Errors = malformed.Errors.ReplaceAt(0, malformed.Errors[0] with { Code = "worker.infrastructure" }) }; }
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
        response = SetUnknown(response, WorkerClaimReason.UnsupportedExpression, 1);
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Incomplete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.SemanticUnknown }) }; }
        { response = response with { Summary = CreateSummary(response) }; }

        Assert.That(ValidateForRequest(response).IsValid, Is.True);

        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Complete }) }; }
        { response = response with { CallableResults = response.CallableResults.ReplaceAt(0, response.CallableResults[0] with { Reason = WorkerCallableCoverageReason.None }) }; }
        AssertContainsErrorCode(ValidateForRequest(response), ["response.callable_projection"]);
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
        System.ArgumentNullException.ThrowIfNull(code);
        var manifest = CreateManifest();
        manifest.Callables = [];
        manifest.Claims = [];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);
        { response = response with { RunStatus = WorkerRunStatus.Failed }; }
        { response = response with { FailureReason = reason }; }
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
        { response = response with { RunStatus = WorkerRunStatus.Failed }; }
        { response = response with { FailureReason = WorkerRunFailureReason.InfrastructureFailure }; }
        response.Errors = [new WorkerProtocolError {
            Code = "worker.infrastructure",
            Message = "failure"
        }];
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        foreach (var separator in ImmutableArray.Create("\n", "\r", "\t", "\u001b", "\u2028", "\u2029"))
        {
            { response = response with { Errors = response.Errors.ReplaceAt(0, response.Errors[0] with { Code = "worker" + separator + "infrastructure" }) }; }
            AssertContainsErrorCode(
                WorkerProtocolJson.Validate(response),
                ["response.errors"]);

            { response = response with { Errors = response.Errors.ReplaceAt(0, response.Errors[0] with { Code = "worker.infrastructure" }) }; }
            { response = response with { Errors = response.Errors.ReplaceAt(0, response.Errors[0] with { Message = "failure" + separator + "forged" }) }; }
            AssertContainsErrorCode(
                WorkerProtocolJson.Validate(response),
                ["response.errors"]);
            { response = response with { Errors = response.Errors.ReplaceAt(0, response.Errors[0] with { Message = "failure" }) }; }
        }
    }

    [TestCase("worker.timeout", WorkerRunStatus.TimedOut)]
    [TestCase("worker.canceled", WorkerRunStatus.Canceled)]
    public void EmptyManifestInterruptionRequiresExactEvidence(
        string code,
        WorkerRunStatus status)
    {
        System.ArgumentNullException.ThrowIfNull(code);
        var manifest = CreateManifest();
        manifest.Callables = [];
        manifest.Claims = [];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);
        { response = response with { RunStatus = status }; }
        response.Errors = [new WorkerProtocolError { Code = code, Message = "interrupted" }];

        Assert.That(ValidateForRequest(response).IsValid, Is.True);
    }

    [Test]
    public void AssumptionSummaryUnionsDeclarationsAndUsageById()
    {
        var response = CreateResponse(CreateManifest());
        var used = response.ClaimResults[0].Assumptions.Single(
            static assumption =>
                assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Kind == WorkerAssumptionKind.UserAssume));
        { var originalFixtureRow72002 = used; used = used with { Used = true }; response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Assumptions = response.ClaimResults[0].Assumptions.ReplaceReference(originalFixtureRow72002, used) }) }; }
        { response = response with { Summary = CreateSummary(response) }; }
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);

        { var originalFixtureRow72158 = used; used = used with { Kind = WorkerAssumptionKind.TrustedBoundary }; response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, response.ClaimResults[0] with { Assumptions = response.ClaimResults[0].Assumptions.ReplaceReference(originalFixtureRow72158, used) }) }; }
        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["summary.assumption_conflict"]);
    }

    [Test]
    public void ClaimResultsRequireOwningCallableAssumptionDeclarations()
    {
        var response = CreateResponse(CreateManifest());
        response.ClaimResults[0].Assumptions =
            [.. response.ClaimResults[0].Assumptions.Skip(1)];
        { response = response with { Summary = CreateSummary(response) }; }

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.claim_assumption_set"]);
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
            .Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)).ToImmutableArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(codes, Does.Contain("response.claim_results"));
            Assert.That(codes, Does.Contain("response.callable_results"));
            Assert.That(codes, Does.Contain("response.errors"));
        }
    }

    [Test]
    public void OutcomePayloadMustMatchClaimResults()
    {
        var response = CreateResponse(CreateManifest());
        response.ClaimResults[0].Model = [
            new WorkerModelValue {
                Variable = "parameter:0",
                Kind = "Integer",
                Value = "1"
            }
        ];

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.claim_payload"]);
    }


    [Test]
    public void ManifestRequiresDenseOrdinalsAndExactCallableMembership()
    {
        var manifest = CreateManifest();
        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Ordinal = 2 }) }; }
        manifest.Callables[0].ClaimIds = [];
        WorkerProtocolJson.SealManifest(manifest);
        var response = CreateResponse(manifest);

        AssertErrorCode(
            WorkerProtocolJson.Validate(response),
            ["manifest.dense_ordinals",
            "manifest.claim_membership"]);
    }

    [Test]
    public void ManifestRejectsEffectClaimsBeforePostconditions()
    {
        var manifest = CreateManifest();
        var postcondition = manifest.Claims[0];
        { postcondition = postcondition with { Ordinal = 1 }; manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, postcondition) }; }
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
                .Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)),
            Is.EqualTo(["manifest.claim_order"]));
    }

    [Test]
    public void NullProtocolRootsAndArrayRequestsAreRejected()
    {
        var request = WorkerProtocolJson.Validate((WorkerVerifyRequest?)null);
        var response = WorkerProtocolJson.Validate((WorkerVerifyResponse?)null);

        using (Assert.EnterMultipleScope())
        {
            AssertErrorCode(request, ["request.null"]);
            AssertErrorCode(response, ["response.null"]);
            Assert.Throws<JsonException>((Action)(() =>
                WorkerProtocolJson.DeserializeRequest("[]")));
        }
    }

    [Test]
    public void SharedInvalidExpectedManifestRetainsRequestBoundValidationError()
    {
        var response = CreateResponse(CreateManifest());
        var manifest = response.Manifest with { Callables = default };
        response = response with { Manifest = manifest };
        Assert.That(response.Manifest, Is.SameAs(manifest));
        AssertContainsErrorCode(WorkerProtocolJson.Validate(response, InputHash, manifest),
            ["response.expected_manifest"]);
    }

    [Test]
    public void CanonicalCopyNormalizesDefaultCallablesWithoutReusingManifestOwner()
    {
        var response = CreateResponse(CreateManifest());
        var manifest = response.Manifest with { Callables = default };
        response = response with { Manifest = manifest };
        var copy = WorkerProtocolJson.CanonicalizeCopy(response);
        Assert.That(copy.Manifest, Is.Not.SameAs(manifest));
        Assert.That(copy.Manifest.Callables.IsDefault, Is.False);
        Assert.That(copy.Manifest.Callables, Is.Empty);
        Assert.That(response.Manifest, Is.SameAs(manifest));
        Assert.That(manifest.Callables.IsDefault, Is.True);
    }

    [Test]
    public void MissingResponseSubdocumentsAndInvalidExpectedManifestAreRejected()
    {
        var missingManifest = CreateResponse(CreateManifest());
        { missingManifest = missingManifest with { Manifest = null! }; }
        var missingSummary = CreateResponse(CreateManifest());
        { missingSummary = missingSummary with { Summary = null! }; }
        var expectedManifest = CreateManifest();
        { expectedManifest = expectedManifest with { SchemaVersion = int.MaxValue }; }

        using (Assert.EnterMultipleScope())
        {
            AssertContainsErrorCode(
                WorkerProtocolJson.Validate(missingManifest),
                ["manifest.null"]);
            AssertContainsErrorCode(
                WorkerProtocolJson.Validate(missingSummary),
                ["response.summary"]);
            AssertContainsErrorCode(WorkerProtocolJson.Validate(
                        CreateResponse(CreateManifest()),
                        InputHash,
                        expectedManifest)
                    , ["response.expected_manifest"]);
        }
    }

    [Test]
    public void RequestBoundValidationRequiresAndTotallyComparesManifests()
    {
        var request = CreateRequest();
        var expected = CreateManifest();
        var response = CreateResponse(expected);
        { response = response with { RequestHash = WorkerProtocolJson.ComputeRequestHash(request) }; }

        var nullError = Assert.Throws<ArgumentNullException>((Action)(() =>
            WorkerProtocolJson.ValidateForRequest(
                response,
                response.RequestHash,
                InputHash,
                null!,
                request,
                CreateExpectedVersions())));

        { response = response with { Manifest = response.Manifest with { Claims = response.Manifest.Claims.ReplaceAt(0, response.Manifest.Claims[0] with { Kind = (WorkerClaimKind)int.MaxValue }) } }; }
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
            AssertContainsErrorCode(validation, ["manifest.claim_kind"]);
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
        { response = response with { Summary = response.Summary with { ElapsedMilliseconds = elapsedMilliseconds } }; }

        var validation = WorkerProtocolJson.Validate(response);
        var elapsedErrors = validation.Errors
            .Where(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code is
                "response.elapsed_unrepresentable" or "summary.elapsed"))
            .ToImmutableArray();

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
        { response = response with { RunStatus = status }; }
        { response = response with { Summary = response.Summary with { ElapsedMilliseconds = WorkerExecutionEnvelope.MaximumProducerElapsedMilliseconds + 1 } }; }

        AssertContainsErrorCode(
            WorkerProtocolJson.Validate(response),
            ["response.elapsed_unrepresentable"]);
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

        request.Budgets.MethodWallTimeMilliseconds = 0;
        Assert.That(Assert.Throws<ArgumentException>((Action)(() =>
            WorkerExecutionEnvelope.MaximumElapsedMilliseconds(request, 1000)))!.ParamName,
            Is.EqualTo("request"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RequestBoundValidationRejectsInvalidExpectedAuthority(bool invalidRuntime)
    {
        var request = CreateRequest();
        var manifest = CreateManifest();
        var response = CreateResponse(manifest);
        var versions = CreateExpectedVersions();
        var requestHash = WorkerProtocolJson.ComputeRequestHash(request);
        { response = response with { RequestHash = requestHash }; }
        if (invalidRuntime)
        {
            { versions = versions with { WorkerVersion = string.Empty }; }
        }
        else
        {
            requestHash = new string('0', 64);
        }
        Assert.That(Assert.Throws<ArgumentException>((Action)(() =>
            WorkerProtocolJson.ValidateForRequest(response, requestHash, response.InputHash,
                manifest, request, versions)))!.ParamName,
            Is.EqualTo(invalidRuntime ? "expectedVersions" : "expectedRequest"));
    }

    [TestCase(WorkerRunStatus.TimedOut, false)]
    [TestCase(WorkerRunStatus.Canceled, false)]
    [TestCase(WorkerRunStatus.TimedOut, true)]
    [TestCase(WorkerRunStatus.Canceled, true)]
    public void ClaimlessInterruptionRetainsItsBoundStatusAndCannotBeCached(WorkerRunStatus status, bool hasCallable)
    {
        var request = CreateRequest();
        var manifest = hasCallable ? CreateManifest() : WorkerResultAssembler.EmptyManifest();
        if (hasCallable)
        {
            manifest.Callables[0].ClaimIds = [];
            manifest.Claims = [];
            WorkerProtocolJson.SealManifest(manifest);
        }
        var versions = CreateExpectedVersions();
        var inputHash = new string('c', 64);
        var response = WorkerResultAssembler.CreateIncomplete(inputHash,
            WorkerProtocolJson.ComputeRequestHash(request), manifest, request.Budgets,
            status, WorkerRunFailureReason.None,
            status == WorkerRunStatus.Canceled ? WorkerCallableCoverageReason.Canceled : WorkerCallableCoverageReason.ProjectTimeout,
            status == WorkerRunStatus.Canceled ? WorkerClaimReason.Canceled : WorkerClaimReason.ProjectTimeout,
            versions: versions);
        var roundtrip = WorkerProtocolJson.DeserializeResponse(WorkerProtocolJson.SerializeResponse(response))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolJson.ValidateForRequest(roundtrip, response.RequestHash,
                inputHash, manifest, request, versions).IsValid, Is.True);
            Assert.That(roundtrip.RunStatus, Is.EqualTo(status));
            Assert.That(roundtrip.Manifest.Claims, Is.Empty);
            Assert.That(roundtrip.ClaimResults, Is.Empty);
            Assert.That(roundtrip.Errors.Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)), Is.EqualTo(hasCallable ? [] : ImmutableArray.Create(status == WorkerRunStatus.Canceled ? "worker.canceled" : WorkerProtocolErrorCodes.WorkerTimeout)));
            Assert.That(VerificationCache.IsCacheable(roundtrip, inputHash, manifest), Is.False);
        }
        var originalError = new WorkerProtocolError
        {
            Code = status == WorkerRunStatus.Canceled ? "worker.canceled" : WorkerProtocolErrorCodes.WorkerTimeout,
            Message = "The actual interrupted operation's context."
        };
        var contextual = WorkerResultAssembler.CreateIncomplete(inputHash, response.RequestHash,
            manifest, request.Budgets, status, WorkerRunFailureReason.None,
            WorkerCallableCoverageReason.ProjectTimeout, WorkerClaimReason.ProjectTimeout,
            errors: [originalError], versions: versions);
        Assert.That(contextual.Errors, Is.EqualTo(ImmutableArray.Create(originalError)));
        { roundtrip = roundtrip with { RunStatus = WorkerRunStatus.Complete }; }
        Assert.That(WorkerProtocolJson.ValidateForRequest(roundtrip, response.RequestHash,
            inputHash, manifest, request, versions).IsValid, Is.False);
        if (hasCallable)
        {
            { roundtrip = roundtrip with { RunStatus = status }; }
            {
                roundtrip = roundtrip with
                {
                    CallableResults = roundtrip.CallableResults.ReplaceAt(0, roundtrip.CallableResults[0] with
                    {
                        Reason = status == WorkerRunStatus.Canceled
                ? WorkerCallableCoverageReason.ProjectTimeout : WorkerCallableCoverageReason.Canceled
                    })
                };
            }
            Assert.That(WorkerProtocolJson.ValidateForRequest(roundtrip, response.RequestHash,
                inputHash, manifest, request, versions).IsValid, Is.False);
        }
        contextual.Errors = [originalError, new WorkerProtocolError
        {
            Code = status == WorkerRunStatus.Canceled ? WorkerProtocolErrorCodes.WorkerTimeout : "worker.canceled",
            Message = "Contradictory interruption evidence."
        }];
        Assert.That(WorkerProtocolJson.ValidateForRequest(contextual, response.RequestHash,
            inputHash, manifest, request, versions).IsValid, Is.False);
    }

    [Test]
    public async Task CacheRejectsIncompleteEvidenceAndUnreadableEntries()
    {
        using var directory = new TempDirectory("sharpproof-cache-validation-");
        var manifest = CreateManifest();
        var response = CreateResponse(manifest);
        var cache = new VerificationCache(directory.FullName, 100_000);
        Assert.That(await cache.TryWriteAsync(response, response.InputHash, manifest, TestContext.CurrentContext.CancellationToken), Is.True);
        var path = Path.Combine(directory.FullName, response.InputHash + VerificationCache.CacheFileSuffix);
        var entry = JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        entry["claimResults"]![0]!["reason"] = "ProjectTimeout";
        await File.WriteAllTextAsync(path, entry.ToJsonString());
        Assert.That(await cache.TryReadAsync(response.InputHash, manifest, response.Summary.Budgets, TestContext.CurrentContext.CancellationToken), Is.Null);
        Assert.That(File.Exists(path), Is.False, "A complete cache envelope cannot admit interrupted claim evidence.");
        await File.WriteAllBytesAsync(path, [0xff]);
        Assert.That(await cache.TryReadAsync(response.InputHash, manifest, response.Summary.Budgets, TestContext.CurrentContext.CancellationToken), Is.Null);
        Assert.That(cache.LastReadUnavailable, Is.True);
    }

    [TestCase(WorkerRunStatus.TimedOut)]
    [TestCase(WorkerRunStatus.Canceled)]
    public void InterruptionProjectsEverySelectedCallableIncludingClaimlessDeclarations(WorkerRunStatus status)
    {
        var request = CreateRequest();
        var manifest = CreateManifest();
        var claimless = new WorkerCallableManifestEntry
        {
            CallableId = "M:Subject.NoPostcondition(System.Int64)",
            SelectedFeatures = [WorkerSelectedFeature.Contracts],
            SelectionReasons = [WorkerSelectionReason.ExplicitAnnotation],
            Location = manifest.Callables[0].Location,
            ClaimIds = []
        };
        manifest.Callables = [.. manifest.Callables, claimless];
        WorkerProtocolJson.SealManifest(manifest);
        var versions = CreateExpectedVersions();
        var response = WorkerResultAssembler.CreateIncomplete(InputHash,
            WorkerProtocolJson.ComputeRequestHash(request), manifest, request.Budgets,
            status, WorkerRunFailureReason.None,
            status == WorkerRunStatus.Canceled ? WorkerCallableCoverageReason.Canceled : WorkerCallableCoverageReason.ProjectTimeout,
            status == WorkerRunStatus.Canceled ? WorkerClaimReason.Canceled : WorkerClaimReason.ProjectTimeout,
            versions: versions);
        var roundtrip = WorkerProtocolJson.DeserializeResponse(WorkerProtocolJson.SerializeResponse(response))!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(WorkerProtocolJson.ValidateForRequest(roundtrip, response.RequestHash,
                InputHash, manifest, request, versions).IsValid, Is.True);
            Assert.That(roundtrip.CallableResults.Select(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId)), Is.EquivalentTo(manifest.Callables.Select(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId))));
            Assert.That(roundtrip.ClaimResults.Select(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.ClaimId)), Is.EqualTo(manifest.Claims.Select(static item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.ClaimId))));
            Assert.That(VerificationCache.IsCacheable(roundtrip, InputHash, manifest), Is.False);
        }
    }

    [Test]
    public async Task CacheEvictionKeepsTheNewestCompleteEntryAndUnrelatedFiles()
    {
        using var directory = new TempDirectory("sharpproof-cache-eviction-");
        var manifest = CreateManifest();
        var response = CreateResponse(manifest);
        var cache = new VerificationCache(directory.FullName, 100_000);
        var firstHash = new string('a', 64);
        var secondHash = new string('b', 64);
        Assert.That(await cache.TryWriteAsync(response, firstHash, manifest, TestContext.CurrentContext.CancellationToken), Is.True);
        var firstPath = Path.Combine(directory.FullName, firstHash + VerificationCache.CacheFileSuffix);
        File.SetLastWriteTimeUtc(firstPath, DateTime.UtcNow.AddDays(-1));
        var maximumBytes = new FileInfo(firstPath).Length + 1;
        var unrelated = Path.Combine(directory.FullName, "unrelated.json");
        await File.WriteAllTextAsync(unrelated, "preserve");
        cache = new VerificationCache(directory.FullName, maximumBytes);
        Assert.That(await cache.TryWriteAsync(response, secondHash, manifest, TestContext.CurrentContext.CancellationToken), Is.True);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(firstPath), Is.False);
            Assert.That(File.Exists(Path.Combine(directory.FullName, secondHash + VerificationCache.CacheFileSuffix)), Is.True);
            Assert.That(await File.ReadAllTextAsync(unrelated), Is.EqualTo("preserve"));
            Assert.That(Directory.GetFiles(directory.FullName, "*" + VerificationCache.CacheFileSuffix).Sum(static path => path is null ? throw new System.ArgumentNullException(nameof(path)) : (new FileInfo(path).Length)), Is.LessThanOrEqualTo(maximumBytes));
        }
    }

    [Test]
    public void ManifestHashAndExpectedInputHashValidateBoundaryValues()
    {
        var nullIdentity = CreateManifest();
        { nullIdentity = nullIdentity with { Callables = nullIdentity.Callables.ReplaceAt(0, nullIdentity.Callables[0] with { CallableId = null! }) }; }
        var nullIdentityHash = WorkerProtocolJson.ComputeManifestHash(nullIdentity);
        var unknownEnum = CreateManifest();
        { unknownEnum = unknownEnum with { Claims = unknownEnum.Claims.ReplaceAt(0, unknownEnum.Claims[0] with { Kind = (WorkerClaimKind)int.MaxValue }) }; }

        Assert.Throws<JsonException>((Action)(() =>
            WorkerProtocolJson.ComputeManifestHash(unknownEnum)));
        var hashError = Assert.Throws<ArgumentException>((Action)(() =>
            WorkerProtocolJson.Validate(
                CreateResponse(CreateManifest()),
                "not-a-hash")));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(nullIdentityHash, Does.Match("^[0-9a-f]{64}$"));
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
        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Kind = WorkerClaimKind.Effect }) }; }
        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { Evidence = WorkerClaimEvidence.Attribute }) }; }
        { manifest = manifest with { Claims = manifest.Claims.ReplaceAt(0, manifest.Claims[0] with { EffectContractKind = WorkerEffectContractKind.DoesNotThrow }) }; }
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
                validation.Errors.Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code))));
        return stopwatch.Elapsed;
    }

    private static void AssertErrorCode(
        WorkerProtocolValidationResult validation,
        ImmutableArray<string> expected)
    {
        if (expected.IsDefault)
        {
            throw new ArgumentNullException(nameof(expected));
        }
        System.ArgumentNullException.ThrowIfNull(validation);
        var actual = validation.Errors
            .Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code))
            .ToImmutableArray();
        Assert.That(actual, Is.EquivalentTo(expected));
    }

    private static void AssertContainsErrorCode(
        WorkerProtocolValidationResult validation,
        ImmutableArray<string> expected)
    {
        if (expected.IsDefault)
        {
            throw new ArgumentNullException(nameof(expected));
        }
        System.ArgumentNullException.ThrowIfNull(validation);
        var actual = validation.Errors
            .Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code))
            .ToImmutableArray();
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
        System.ArgumentNullException.ThrowIfNull(response);
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
        if (size < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(size)); }

        var callables = ImmutableArray.CreateBuilder<WorkerCallableManifestEntry>();
        var claims = ImmutableArray.CreateBuilder<WorkerClaimManifestEntry>();
        var callableResults = ImmutableArray.CreateBuilder<WorkerCallableResult>();
        var claimResults = ImmutableArray.CreateBuilder<WorkerClaimResult>();
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
            callables.Add(new WorkerCallableManifestEntry
            {
                CallableId = callableId,
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [
                    WorkerSelectionReason.DiscoveredPostcondition
                ],
                Location = location,
                ClaimIds = [claimId]
            });
            claims.Add(new WorkerClaimManifestEntry
            {
                ClaimId = claimId,
                CallableId = callableId,
                Kind = WorkerClaimKind.Postcondition,
                Evidence = WorkerClaimEvidence.DirectClause,
                Location = location
            });
            callableResults.Add(new WorkerCallableResult
            {
                CallableId = callableId,
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None
            });
            claimResults.Add(new WorkerClaimResult
            {
                ClaimId = claimId,
                Outcome = WorkerClaimOutcome.Proven,
                Reason = WorkerClaimReason.None
            });
        }

        var manifest = new WorkerClaimManifest
        {
            Callables = callables.ToImmutable(),
            Claims = claims.ToImmutable()
        };
        manifest.Hash = WorkerProtocolJson.ComputeManifestHash(manifest);
        return new WorkerVerifyResponse
        {
            InputHash = InputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = callableResults.ToImmutable(),
            ClaimResults = claimResults.ToImmutable(),
            Summary = new WorkerVerificationSummary
            {
                CacheStatus = WorkerCacheStatus.Miss,
                Versions = CreateExpectedVersions()
            }
        };
    }

    private static string ManifestHashAfter(
        Func<WorkerClaimManifest, WorkerClaimManifest> mutation)
    {
        System.ArgumentNullException.ThrowIfNull(mutation);
        var manifest = CreateManifest();
        manifest = mutation(manifest);
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
        System.ArgumentNullException.ThrowIfNull(manifest);
        var response = new WorkerVerifyResponse
        {
            InputHash = InputHash,
            Manifest = manifest,
            RunStatus = WorkerRunStatus.Complete,
            FailureReason = WorkerRunFailureReason.None,
            CallableResults = [.. manifest.Callables.Select(
                static callable => callable is null ? throw new System.ArgumentNullException(nameof(callable)) : (new WorkerCallableResult {
                    CallableId = callable.CallableId,
                    Coverage = WorkerCallableCoverage.Complete,
                    Reason = WorkerCallableCoverageReason.None,
                    Assumptions = CopyAssumptions(callable.Assumptions)
                }))],
            ClaimResults = [.. manifest.Claims.Select(
                claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (new WorkerClaimResult {
                    ClaimId = claim.ClaimId,
                    Outcome = WorkerClaimOutcome.Proven,
                    Reason = WorkerClaimReason.None,
                    EffectCertainty = claim.Kind == WorkerClaimKind.Effect
                        ? WorkerEffectEvidenceCertainty.CompleteMayEffectSummary
                        : WorkerEffectEvidenceCertainty.Unspecified,
                    Assumptions = CopyAssumptions(
                        manifest.Callables.Single(callable =>
                            callable is null ? throw new System.ArgumentNullException(nameof(callable)) : (callable.CallableId == claim.CallableId))
                            .Assumptions)
                }))]
        };
        { response = response with { Summary = CreateSummary(response) }; }
        return response;
    }

    private static ImmutableArray<WorkerAssumptionEvidence> CopyAssumptions(
        IEnumerable<WorkerAssumptionEvidence> assumptions)
    {
        System.ArgumentNullException.ThrowIfNull(assumptions);
        return [.. assumptions.Select(static assumption =>
            assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (new WorkerAssumptionEvidence {
                Id = assumption.Id,
                Kind = assumption.Kind,
                Used = assumption.Used
            }))];
    }

    private static WorkerVerificationSummary CreateSummary(
        WorkerVerifyResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        return new WorkerVerificationSummary
        {
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
        {
            claim = claim with
            {
                EffectWitness = CreateEffectWitness(
            response.Manifest.Claims[0].Location)
            };
            response = response with { ClaimResults = response.ClaimResults.ReplaceAt(0, claim) };
        }
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
        System.ArgumentNullException.ThrowIfNull(json);
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

    private static ArgumentNullException CreateArgumentNullException(
        string parameterName)
    {
        System.ArgumentNullException.ThrowIfNull(parameterName);
        return new ArgumentNullException(parameterName);
    }

    private static WorkerVerifyResponse SetUnknown(
        WorkerVerifyResponse response,
        WorkerClaimReason reason,
        int index = 0)
    {
        if (index < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(index)); }

        System.ArgumentNullException.ThrowIfNull(response);
        return response with { ClaimResults = response.ClaimResults.ReplaceAt(index, response.ClaimResults[index] with { Outcome = WorkerClaimOutcome.Unknown, Reason = reason }) };
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
        { response = response with { RequestHash = WorkerProtocolJson.ComputeRequestHash(request) }; }
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
        { response = response with { Summary = response.Summary with { CacheStatus = status } }; }
        { response = response with { Summary = response.Summary with { CacheHit = status == WorkerCacheStatus.Hit } }; }
        var validation = ValidateForRequest(response, request);
        Assert.That(
            validation.IsValid,
            Is.EqualTo(expectedValid),
            string.Join(", ", validation.Errors.Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code))));
        if (!expectedValid)
        {
            AssertErrorCode(validation, ["response.cache_request_mismatch"]);
        }
    }

    private static WorkerEffectViolationWitness CreateEffectWitness(
        WorkerSourceLocation location)
    {
        System.ArgumentNullException.ThrowIfNull(location);
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
