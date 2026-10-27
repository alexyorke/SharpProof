using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeTaskRuntimePremiseTests
{
    private const string DeploymentHash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string DeploymentEvidence = "fixture deployment manifest, including cold initialization";
    private const string RuntimeScope = "joined-isolated-cumulative-runtime-premises";
    private static readonly ImmutableArray<string> QuantityNames =
    [
        "RawTraceArrayAttemptsPerFaultEpisode", "RawTracePayloadBytesPerArrayAttempt",
        "ResolverArrayAttemptsPerFaultEpisode", "ResolverSlotsPerArrayAttempt",
        "FaultConstructionBytesPerFaultEpisode", "ResolverReferentBytesPerFaultEpisode",
        "SchedulingBytesPerChildStartAttempt", "JoinBytesPerJoinAttempt", "AwaitBytesPerConfiguredAwait",
        "InitializationBytesPerInvocation", "TotalFaultEpisodesPerInvocation"
    ];
    private static readonly ImmutableArray<long> QuantityValues = [2, 5, 3, 7, 11, 13, 17, 19, 23, 29, 31];
    private static readonly ImmutableArray<string> QuantityUnits =
    [
        "allocation-attempts", "payload-bytes", "allocation-attempts", "reference-slots",
        "managed-instance-bytes", "managed-instance-bytes", "managed-instance-bytes", "managed-instance-bytes",
        "managed-instance-bytes", "managed-instance-bytes", "fault-episodes"
    ];
    private static readonly ImmutableArray<string> QuantityScopes =
    [
        "per-outermost-fault-episode:all-raw-trace-affinity-restore-dispatch-arrays",
        "per-raw-trace-array-allocation-attempt",
        "per-outermost-fault-episode:all-resolver-affinity-restore-dispatch-arrays",
        "per-resolver-array-allocation-attempt",
        "per-outermost-fault-episode:transitive-fault-construction-messages-nested-replacement-retries",
        "per-outermost-fault-episode:fresh-loader-resolver-referents-and-losing-candidates",
        "per-child-start-attempt:remaining-transitive-scheduler-queue-worker-entire-causal-lifecycle-builder-recording-notification-control",
        "per-join-attempt:remaining-transitive-join-entire-causal-lifecycle-builder-recording-notification-control",
        "per-configured-await:remaining-transitive-await-entire-causal-lifecycle-builder-recording-notification-control",
        "per-root-invocation:all-causally-reached-transitive-cold-initialization-and-retries",
        "per-root-invocation:all-source-helper-runtime-scheduler-initialization-and-existing-exception-processing-entries"
    ];

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public async Task ActualCompilerArtifactPublishesOnlyTheCompleteOwnedConditionalRuntimeProof(
        OptimizationLevel optimization, bool configured)
    {
        var target = Prepare(optimization, configured, Attributes("complete"));
        var projection = target.ForkJoinProjection!;
        var capture = projection.RuntimePremises!;
        var inventory = capture.Inventory!;
        var shapes = projection.Carriers!.Shapes!;
        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(inventory.RootCallableId, Is.EqualTo(target.Entry.CallableId));
        Assert.That(inventory.ModuleSha256, Is.EqualTo(shapes.ModuleSha256));
        Assert.That(inventory.CompilerIdentity, Is.EqualTo(shapes.CompilerIdentity));
        var declarations = inventory.Declarations;
        Assert.That(declarations.Quantities, Has.Length.EqualTo(11));
        for (var index = 0; index < QuantityNames.Length; index++)
        {
            Assert.That(declarations.Quantities[index].Quantity.ToString(), Is.EqualTo(QuantityNames[index]));
            Assert.That(declarations.Quantities[index].UpperBound, Is.EqualTo(QuantityValues[index]));
            Assert.That(declarations.Quantities[index].Evidence, Is.EqualTo(QuantityEvidence(index)));
        }
        var result = await Verify(target);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ExecutionScope, Is.EqualTo(RuntimeScope));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo(ManualSum(projection).ToString(CultureInfo.InvariantCulture)));
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(2));
        Assert.That(result.ResourceBound.Asymptotic!.Lower!.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ResourceBound.Expected!.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        var evidence = result.ResourceBound.RuntimePremises!;
        Assert.That(evidence, Is.Not.Null);
        Assert.That(evidence.Model, Is.EqualTo(ResourceTaskRuntimePremiseIdentity.Model));
        Assert.That(evidence.ProfileCatalogSha256, Is.EqualTo(ResourceTaskRuntimePremiseIdentity.ProfileCatalogSha256));
        Assert.That(evidence.RootCallableId, Is.EqualTo(target.Entry.CallableId));
        Assert.That(evidence.ModuleSha256, Is.EqualTo(inventory.ModuleSha256));
        Assert.That(evidence.CompilerIdentity, Is.EqualTo(inventory.CompilerIdentity));
        Assert.That(evidence.ReferenceAssembly, Is.EqualTo(inventory.FrameworkAssemblyIdentity));
        Assert.That(evidence.ReferenceSha256, Is.EqualTo(target.Compilation.ResourceMetadataReferences.Single().Modules.Single().Sha256));
        Assert.That(evidence.Profile, Is.EqualTo(WorkerResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed));
        Assert.That(evidence.DeploymentManifestSha256, Is.EqualTo(DeploymentHash));
        Assert.That(evidence.DeploymentEvidence, Is.EqualTo(DeploymentEvidence));
        Assert.That(evidence.DeclarationSha256, Is.EqualTo(ResourceTaskRuntimePremiseIdentity.DeclarationSha256(declarations)));
        Assert.That(evidence.DeploymentAssumptionId, Is.EqualTo(ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(declarations)));
        Assert.That(evidence.Quantities, Has.Length.EqualTo(11));
        for (var index = 0; index < QuantityNames.Length; index++)
        {
            var quantity = evidence.Quantities[index];
            Assert.That(quantity.Quantity.ToString(), Is.EqualTo(QuantityNames[index]));
            Assert.That(quantity.UpperBound, Is.EqualTo(QuantityValues[index]));
            Assert.That(quantity.Evidence, Is.EqualTo(QuantityEvidence(index)));
            Assert.That(quantity.Unit, Is.EqualTo(QuantityUnits[index]));
            Assert.That(quantity.Scope, Is.EqualTo(QuantityScopes[index]));
            Assert.That(quantity.AssumptionId, Is.EqualTo(ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, declarations.Quantities[index])));
        }
        Assert.That(result.Assumptions.Count(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
            assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used), Is.EqualTo(12));
        Assert.That(target.Entry.Assumptions.Where(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
            assumption.Kind == WorkerAssumptionKind.UserAssume).All(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
            !assumption.Used), Is.True, "Compiler declarations do not mark assumptions used before verification.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UppercaseDeploymentDigestReachesTheConditionalCompilerConsumer(bool configured)
    {
        var attributes = Attributes("complete").Replace(DeploymentHash, DeploymentHash.ToUpperInvariant(), StringComparison.Ordinal);
        var target = Prepare(OptimizationLevel.Release, configured, attributes);
        var capture = target.ForkJoinProjection!.RuntimePremises!;
        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(capture.Inventory!.Declarations.DeploymentManifestSha256, Is.EqualTo(DeploymentHash));
        var result = await Verify(target);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.RuntimePremises!.DeploymentManifestSha256, Is.EqualTo(DeploymentHash));
        Assert.That(result.ResourceBound.InferredUpperBound,
            Is.EqualTo(ManualSum(target.ForkJoinProjection!).ToString(CultureInfo.InvariantCulture)));
        Assert.That(result.Assumptions.Count(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
            assumption.Kind == WorkerAssumptionKind.UserAssume && assumption.Used), Is.EqualTo(12));
    }

    [TestCase("malformed", false)]
    [TestCase("malformed", true)]
    [TestCase("foreign-input", false)]
    [TestCase("foreign-input", true)]
    public async Task RuntimePremiseRootRejectsMalformedOrForeignDeclaredBoundsWithoutPartialEvidence(string mutation, bool configured)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var target = Prepare(OptimizationLevel.Release, configured, Attributes("complete"));
        var claim = target.EffectClaims.Single();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var payload = mutation switch
        {
            "malformed" => Encoding.UTF8.GetBytes("{}"),
            "foreign-input" => ResourceExpressionCodec.Encode(ResourceExpression.InputSize(
                new ResourceInput(1, ResourceInputKind.NonNegativeScalar, "foreign", int.MaxValue)), cancellation),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var malformed = claim with
        {
            Constraint = claim.Constraint with
            { ResourceUpperBound = claim.Constraint.ResourceUpperBound! with { UpperBound = payload } }
        };
        CompilerEffectClaimArtifactCodec.Seal(malformed);
        var changed = target with { EffectClaims = target.EffectClaims.SetItem(0, malformed) };
        var rejected = await Verify(changed);
        AssertUnknown(rejected);
        Assert.That(rejected.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
        Assert.That(rejected.ResourceBound!.UnknownReason, Is.EqualTo("ForkJoinDeclaredBoundInvalid"));
        Assert.That(rejected.EffectWitness, Is.Null);
        Assert.That((await Verify(target)).Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase("none")]
    [TestCase("missing-quantity")]
    [TestCase("missing-deployment")]
    [TestCase("duplicate-quantity")]
    [TestCase("negative")]
    [TestCase("unsupported-profile")]
    [TestCase("invalid-deployment-hash")]
    public async Task ActualAttributesNeverSupplyDefaultsForAbsentOrMalformedPremises(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var target = Prepare(OptimizationLevel.Release, false, Attributes(mutation));
        var result = await Verify(target);
        AssertUnknown(result);
        if (mutation == "none")
        { Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("TaskPrimitiveHelperDynamicArrayRouteUnproved")); }
        else
        {
            var capture = target.ForkJoinProjection!.RuntimePremises!;
            Assert.That(capture.Inventory, Is.Null);
            Assert.That(capture.UnknownReason, Is.Not.Null.And.Not.Empty);
        }
    }

    [TestCase("foreign-user-assumption")]
    [TestCase("partial-set")]
    [TestCase("all-missing")]
    [TestCase("duplicate-id")]
    [TestCase("used")]
    [TestCase("wrong-kind")]
    public void ConfiguredEntryAdmitsOnlyTheCompleteUnusedRuntimeManifestSet(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var initial = TestCompilation.Create("ConfiguredRuntimeManifest", [("Subject.cs", Source(Attributes("complete"), true, 1000000))]);
        const string references = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.32/ref/net8.0";
        var compilation = initial.WithReferences(Directory.GetFiles(references, "*.dll")
            .Append(typeof(ResourceUpperBoundAttribute).Assembly.Location)
            .Select(static path => path is null ? throw new ArgumentNullException(nameof(path)) : MetadataReference.CreateFromFile(path)))
            .WithOptions(initial.Options.WithPlatform(Platform.X64).WithOptimizationLevel(OptimizationLevel.Release));
        TestCompilation.AssertNoErrors(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var target = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, cancellation).Build().Targets.Values.Single();
        var lowerer = new CompilerCallableLowerer(compilation, new IrFactory());
        Assert.That(lowerer.Prepare(target, cancellation).ForkJoinProjection, Is.Not.Null);
        var quantities = target.Entry.Assumptions.Where(static assumption => assumption is null
            ? throw new ArgumentNullException(nameof(assumption)) : assumption.Kind == WorkerAssumptionKind.UserAssume).ToImmutableArray();
        Assert.That(quantities, Has.Length.EqualTo(12));
        var later = quantities[quantities.Length - 1];
        var changed = mutation switch
        {
            "foreign-user-assumption" => target.Entry.Assumptions.Add(new() { Id = "foreign-user-assumption", Kind = WorkerAssumptionKind.UserAssume }),
            "partial-set" => target.Entry.Assumptions.Remove(later),
            "all-missing" => target.Entry.Assumptions.Where(static assumption => assumption is null
                ? throw new ArgumentNullException(nameof(assumption)) : assumption.Kind != WorkerAssumptionKind.UserAssume).ToImmutableArray(),
            "duplicate-id" => target.Entry.Assumptions.Add(later),
            "used" => target.Entry.Assumptions.Replace(later, later with { Used = true }),
            "wrong-kind" => target.Entry.Assumptions.Replace(later, later with { Kind = WorkerAssumptionKind.TrustedBoundary }),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var rejected = lowerer.Prepare(target with { Entry = target.Entry with { Assumptions = changed } }, cancellation);
        Assert.That(rejected.TotalEntry, Is.Null);
        Assert.That(rejected.ForkJoinProjection, Is.Null);
    }

    [TestCase("capture")]
    [TestCase("mixed-capture")]
    [TestCase("unknown-reason")]
    [TestCase("root")]
    [TestCase("declaration-root")]
    [TestCase("module")]
    [TestCase("compiler")]
    [TestCase("framework")]
    [TestCase("deployment-digest")]
    [TestCase("quantity-value")]
    [TestCase("quantity-evidence")]
    [TestCase("missing-deployment-id")]
    [TestCase("missing-quantity-id")]
    [TestCase("quantity-id")]
    [TestCase("assumption-kind")]
    [TestCase("duplicate-id")]
    [TestCase("references")]
    [TestCase("reference-hash")]
    [TestCase("reference-conflict")]
    [TestCase("layout-platform")]
    public async Task MissingOrAlteredOwnershipNeverPublishesAPrefixOrUsedPremise(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var target = Prepare(OptimizationLevel.Release, false, Attributes("complete"));
        var projection = target.ForkJoinProjection!;
        var inventory = projection.RuntimePremises!.Inventory!;
        var declarations = inventory.Declarations;
        var deploymentId = ResourceTaskRuntimePremiseIdentity.DeploymentAssumptionId(declarations);
        var quantityId = ResourceTaskRuntimePremiseIdentity.QuantityAssumptionId(declarations, declarations.Quantities[10]);
        var deployment = target.Entry.Assumptions.Single(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id == deploymentId);
        var quantity = target.Entry.Assumptions.Single(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id == quantityId);
        var changed = inventory;
        var entry = target.Entry;
        var compilation = target.Compilation;
        switch (mutation)
        {
            case "capture":
                projection = projection with { RuntimePremises = null };
                break;
            case "unknown-reason":
                projection = projection with { RuntimePremises = new(null, new string('x', 513)) };
                break;
            case "mixed-capture":
                projection = projection with { RuntimePremises = new(inventory, "RejectedRuntimePremiseCapture") };
                break;
            case "root":
                changed = inventory with { RootCallableId = "foreign" };
                break;
            case "declaration-root":
                changed = inventory with { Declarations = declarations with { RootCallableId = "foreign" } };
                break;
            case "module":
                changed = inventory with { ModuleSha256 = new string('b', 64) };
                break;
            case "compiler":
                changed = inventory with { CompilerIdentity = "foreign compiler" };
                break;
            case "framework":
                changed = inventory with { FrameworkAssemblyIdentity = "foreign framework" };
                break;
            case "deployment-digest":
                changed = inventory with { Declarations = declarations with { DeploymentManifestSha256 = new string('b', 64) } };
                break;
            case "quantity-value":
                changed = inventory with { Declarations = declarations with { Quantities = declarations.Quantities.SetItem(0, declarations.Quantities[0] with { UpperBound = 3 }) } };
                break;
            case "quantity-evidence":
                changed = inventory with { Declarations = declarations with { Quantities = declarations.Quantities.SetItem(0, declarations.Quantities[0] with { Evidence = "changed support" }) } };
                break;
            case "missing-deployment-id":
                entry = entry with { Assumptions = entry.Assumptions.Remove(deployment) };
                break;
            case "missing-quantity-id":
                entry = entry with { Assumptions = entry.Assumptions.Remove(quantity) };
                break;
            case "quantity-id":
                entry = entry with { Assumptions = entry.Assumptions.Replace(quantity, quantity with { Id = quantity.Id + "-changed" }) };
                break;
            case "assumption-kind":
                entry = entry with { Assumptions = entry.Assumptions.Replace(quantity, quantity with { Kind = WorkerAssumptionKind.TrustedBoundary }) };
                break;
            case "duplicate-id":
                entry = entry with { Assumptions = [.. entry.Assumptions, quantity] };
                break;
            case "references":
                compilation = compilation with { ResourceMetadataReferences = [] };
                break;
            case "reference-hash":
                var reference = compilation.ResourceMetadataReferences.Single();
                compilation = compilation with { ResourceMetadataReferences = [reference with { Modules = [reference.Modules.Single() with { Sha256 = "bad" }] }] };
                break;
            case "reference-conflict":
                var original = compilation.ResourceMetadataReferences.Single();
                compilation = compilation with { ResourceMetadataReferences = [original, original with { Modules = [original.Modules.Single() with { Sha256 = new string('b', 64) }] }] };
                break;
            case "layout-platform":
                compilation = compilation with { ResourceTargetPlatform = "AnyCpu" };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        if (!ReferenceEquals(changed, inventory))
        { projection = projection with { RuntimePremises = new(changed, null) }; }
        target = target with { Entry = entry, Compilation = compilation, ForkJoinProjection = projection };
        AssertUnknown(await Verify(target));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AnUpperPremiseSumAboveTheContractDoesNotBecomeAnExactRefutation(bool configured)
    {
        var target = Prepare(OptimizationLevel.Release, configured, Attributes("complete"), 0);
        var result = await Verify(target);
        AssertUnknown(result);
        Assert.That(result.EffectWitness, Is.Null);
    }

    [TestCase("missing-property")]
    [TestCase("unknown-property")]
    [TestCase("duplicate-property")]
    [TestCase("mixed-capture")]
    [TestCase("unknown-reason")]
    [TestCase("root")]
    [TestCase("module")]
    [TestCase("missing-quantity")]
    [TestCase("duplicate-quantity")]
    [TestCase("negative")]
    [TestCase("unsupported-profile")]
    public void StrictProjectionCodecRejectsMalformedRuntimePremises(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var target = Prepare(OptimizationLevel.Release, false, Attributes("complete"));
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var json = JsonNode.Parse(CompilerForkJoinProjectionCodec.Encode(target.ForkJoinProjection!, target.TotalEntry!, cancellation))!.AsObject();
        var capture = json["runtimePremises"]!.AsObject();
        var value = capture["value"]!.AsObject();
        switch (mutation)
        {
            case "missing-property":
                json.Remove("runtimePremises");
                break;
            case "unknown-property":
                value["extra"] = true;
                break;
            case "duplicate-property":
                break;
            case "unknown-reason":
                capture["value"] = null;
                capture["unknown"] = new string('x', 513);
                break;
            case "mixed-capture":
                capture["unknown"] = "RejectedRuntimePremiseCapture";
                break;
            case "root":
                value["root"] = "foreign";
                break;
            case "module":
                value["module"] = new string('b', 64);
                break;
            case "missing-quantity":
                value["quantities"]!.AsArray().RemoveAt(10);
                break;
            case "duplicate-quantity":
                value["quantities"]![10]!["quantity"] = 1;
                break;
            case "negative":
                value["quantities"]![0]!["upper"] = -1L;
                break;
            case "unsupported-profile":
                value["profile"] = 999;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var text = json.ToJsonString();
        if (mutation == "duplicate-property")
        { text = text.Replace("\"runtimePremises\":", "\"runtimePremises\":null,\"runtimePremises\":", StringComparison.Ordinal); }
        Action decode = () => { _ = CompilerForkJoinProjectionCodec.Decode(Encoding.UTF8.GetBytes(text), target.TotalEntry!, cancellation); };
        Assert.Throws<InvalidDataException>(decode);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualWorkerPreservesPremiseEvidenceThroughCacheSarifPolicyAndStrictResponseValidation(bool configured)
    {
        using var project = WorkerTests.TestProject.Create(Source(Attributes("complete"), configured, 1000000));
        project.UseNetCoreReferencePack(8);
        var request = project.CreateRequest(cacheEnabled: true,
            compilationOptions: TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary).WithPlatform(Platform.X64));
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var cached = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(first.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), first.ClaimResults.Single().ResourceBound?.UnknownReason);
        Assert.That(cached.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(cached.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
        Assert.That(WorkerProtocolJson.ValidateForRequest(cached, WorkerProtocolJson.ComputeRequestHash(request), cached.InputHash,
            cached.Manifest, request, cached.Summary.Versions).IsValid, Is.True);
        var evidence = cached.ClaimResults.Single().ResourceBound!.RuntimePremises!;
        Assert.That(evidence.Quantities, Has.Length.EqualTo(11));
        var strict = request with { AssumptionPolicy = WorkerAssumptionPolicy.Error };
        var strictResult = await worker.VerifyAsync(strict, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(strictResult.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        using var sarif = JsonDocument.Parse(Launcher.SarifProjection.Serialize(strict, strictResult, project.DirectoryPath));
        var rows = sarif.RootElement.GetProperty("runs")[0].GetProperty("results");
        var policy = rows.EnumerateArray().Single(static row => row.GetProperty("ruleId").GetString() == "SP0048");
        Assert.That(policy.GetProperty("kind").GetString(), Is.EqualTo("fail"));
        Assert.That(policy.GetProperty("level").GetString(), Is.EqualTo("error"));
        Assert.That(policy.GetProperty("message").GetProperty("text").GetString(), Does.Contain(evidence.DeploymentAssumptionId));
        var proven = rows.EnumerateArray().Single(static row => row.GetProperty("ruleId").GetString() == "SharpProof.Proven");
        Assert.That(proven.GetProperty("message").GetProperty("text").GetString(), Does.Contain(RuntimeScope));
        var reported = proven.GetProperty("properties").GetProperty("result").GetProperty("resourceBound").GetProperty("runtimePremises");
        Assert.That(reported.GetProperty("deploymentEvidence").GetString(), Is.EqualTo(DeploymentEvidence));
        Assert.That(reported.GetProperty("quantities").GetArrayLength(), Is.EqualTo(11));
        var original = WorkerProtocolJson.SerializeResponse(cached);
        foreach (var mutation in new[] { "missing-property", "unknown-property", "duplicate-property", "root", "module", "compiler",
            "reference", "declaration-hash", "deployment-id", "quantity-id", "unit", "scope", "upper", "quantity-evidence", "missing-quantity" })
        {
            var json = JsonNode.Parse(original)!.AsObject();
            var bound = json["claimResults"]![0]!["resourceBound"]!.AsObject();
            var runtime = bound["runtimePremises"]!.AsObject();
            switch (mutation)
            {
                case "missing-property":
                    bound.Remove("runtimePremises");
                    break;
                case "unknown-property":
                    runtime["extra"] = true;
                    break;
                case "duplicate-property":
                    break;
                case "root":
                    runtime["rootCallableId"] = "foreign";
                    break;
                case "module":
                    runtime["moduleSha256"] = new string('b', 64);
                    break;
                case "compiler":
                    runtime["compilerIdentity"] = "foreign compiler";
                    break;
                case "reference":
                    runtime["referenceSha256"] = new string('b', 64);
                    break;
                case "declaration-hash":
                    runtime["declarationSha256"] = new string('b', 64);
                    break;
                case "deployment-id":
                    runtime["deploymentAssumptionId"] = "foreign";
                    break;
                case "quantity-id":
                    runtime["quantities"]![10]!["assumptionId"] = "foreign";
                    break;
                case "unit":
                    runtime["quantities"]![0]!["unit"] = "managed-instance-bytes";
                    break;
                case "scope":
                    runtime["quantities"]![0]!["scope"] = "per-root-invocation";
                    break;
                case "upper":
                    runtime["quantities"]![0]!["upperBound"] = 3L;
                    break;
                case "quantity-evidence":
                    runtime["quantities"]![0]!["evidence"] = "changed support";
                    break;
                case "missing-quantity":
                    runtime["quantities"]!.AsArray().RemoveAt(10);
                    break;
                default:
                    throw new InvalidOperationException("Unknown response mutation.");
            }
            var text = json.ToJsonString();
            if (mutation == "duplicate-property")
            { text = text.Replace("\"runtimePremises\":", "\"runtimePremises\":null,\"runtimePremises\":", StringComparison.Ordinal); }
            AssertMalformedResponse(text, mutation);
        }
    }

    private static async Task<WorkerClaimResult> Verify(CompilerCallablePreparation target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken)).Single();
    }

    private static void AssertUnknown(WorkerClaimResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.RuntimePremises, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Used), Is.False);
    }

    private static void AssertMalformedResponse(string json, string mutation)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(mutation);
        WorkerVerifyResponse? decoded;
        try
        { decoded = WorkerProtocolJson.DeserializeResponse(json); }
        catch (JsonException) { return; }
        catch (InvalidDataException) { return; }
        Assert.That(decoded, Is.Not.Null, mutation);
        Assert.That(WorkerProtocolJson.Validate(decoded!).IsValid, Is.False, mutation);
    }

    private static CompilerCallablePreparation Prepare(OptimizationLevel optimization, bool configured, string attributes, long upper = 1000000)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentOutOfRangeException.ThrowIfNegative(upper);
        var initial = TestCompilation.Create("TaskRuntimePremises", [("Subject.cs", Source(attributes, configured, upper))]);
        const string references = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.32/ref/net8.0";
        var compilation = initial.WithReferences(Directory.GetFiles(references, "*.dll")
            .Append(typeof(ResourceUpperBoundAttribute).Assembly.Location)
            .Select(static path => path is null ? throw new ArgumentNullException(nameof(path)) : MetadataReference.CreateFromFile(path)))
            .WithOptions(initial.Options.WithPlatform(Platform.X64).WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, cancellation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net8.0", WorkerFeatureSet.All,
            discovery, WorkerBudgets.DefaultMaximumExpressionDepth, cancellation);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellation);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellation);
        var target = preparations.Single();
        Assert.That(target.ForkJoinProjection, Is.Not.Null, target.ResourceUnknownReason);
        Assert.That(target.TotalEntry, Is.Not.Null);
        return target;
    }

    private static string Source(string attributes, bool configured, long upper)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentOutOfRangeException.ThrowIfNegative(upper);
        return "using SharpProof.Attributes;using System.Threading.Tasks;public static class C{" + attributes +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes," + upper.ToString(CultureInfo.InvariantCulture) +
            "L)]public static " + (configured ? "async " : "") + "Task Target(int n)=>" + (configured ? "await " : "") +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(n)))" + (configured ? ".ConfigureAwait(false)" : "") +
            ";static void Child(int n){var values=new int[3];}}";
    }

    private static string Attributes(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        if (mutation == "none")
        { return string.Empty; }
        var attributes = new StringBuilder();
        if (mutation != "missing-deployment")
        {
            attributes.Append("[ResourceTaskDeployment(");
            attributes.Append(mutation == "unsupported-profile" ? "(ResourceTaskDeploymentProfile)999" :
                "ResourceTaskDeploymentProfile.CoreClr8032LinuxX64OrdinaryJitStockClosed");
            attributes.Append(",\"").Append(mutation == "invalid-deployment-hash" ? "bad" : DeploymentHash)
                .Append("\",\"").Append(DeploymentEvidence).Append("\")]");
        }
        // Source order is deliberately reversed; transport must restore the typed canonical order.
        for (var index = QuantityNames.Length - 1; index >= 0; index--)
        {
            if (mutation == "missing-quantity" && index == 10)
            { continue; }
            AppendQuantity(attributes, index, mutation == "negative" && index == 0 ? -1 : QuantityValues[index]);
        }
        if (mutation == "duplicate-quantity")
        { AppendQuantity(attributes, 0, QuantityValues[0]); }
        return attributes.ToString();
    }

    private static void AppendQuantity(StringBuilder attributes, int index, long upper)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        if ((uint)index >= (uint)QuantityNames.Length)
        { throw new ArgumentOutOfRangeException(nameof(index)); }
        attributes.Append("[ResourceTaskRuntimeUpperBound(ResourceTaskRuntimeQuantity.").Append(QuantityNames[index])
            .Append(',').Append(upper.ToString(CultureInfo.InvariantCulture)).Append("L,\"")
            .Append(QuantityEvidence(index)).Append("\")]");
    }

    private static string QuantityEvidence(int index)
    {
        if ((uint)index >= (uint)QuantityNames.Length)
        { throw new ArgumentOutOfRangeException(nameof(index)); }
        return "quantity-evidence-" + (index + 1).ToString(CultureInfo.InvariantCulture);
    }

    private static BigInteger ManualSum(CompilerForkJoinProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var shapes = projection.Carriers!.Shapes!;
        BigInteger compiler = 40 + 64 * new BigInteger(shapes.MatchedCallbacks);
        foreach (var frame in shapes.CallbackFrames)
        { compiler += BigInteger.Max(24, 16 + 8 * new BigInteger(frame.Fields.Length)); }
        if (shapes.StateMachine is { IsValueType: false } machine)
        { compiler += BigInteger.Max(24, 16 + 8 * new BigInteger(machine.Fields.Length)); }
        // Two int[3] arrays cost 40 each. Raw byte[5] costs 32; seven reference slots cost 80.
        return 80 + compiler + 2 * 17 + 19 + (projection.ConfiguredAwait ? 23 : 0) + 31 * (2 * 32 + 3 * 80 + 11 + 13) + 29;
    }
}
