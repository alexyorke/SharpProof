using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeResourceContractTests
{
    private static string Source(string attributes, string body)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(body);
        return "using SharpProof.Attributes; public static class C { " + attributes + " public static void Target(int n, int[] values) { " + body + " } }";
    }

    private static CompilerManifestArtifact CreateArtifact(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var initial = TestCompilation.Create("ResourceContract", ("Subject.cs", source));
        var trees = initial.SyntaxTrees.Select(tree => tree is null ? throw new ArgumentNullException(nameof(tree)) :
            CSharpSyntaxTree.ParseText(tree.GetText(cancellation), ((CSharpParseOptions)tree.Options).WithPreprocessorSymbols(Array.Empty<string>()), tree.FilePath, cancellationToken: cancellation));
        var compilation = initial.RemoveAllSyntaxTrees().AddSyntaxTrees(trees);
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All, cancellation).Build();
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net8.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, cancellation);
    }

    private static CompilerCallablePreparation Read(CompilerManifestArtifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, TestContext.CurrentContext.CancellationToken);
        return preparations.Single();
    }

    private static void Rejects(Action decode)
    {
        ArgumentNullException.ThrowIfNull(decode);
        var exception = Assert.Throws<JsonException>(decode);
        Assert.That(exception!.InnerException, Is.TypeOf<InvalidDataException>());
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var artifact = CreateArtifact(source);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, TestContext.CurrentContext.CancellationToken);
        return preparations.Single();
    }

    [TestCase("LoopBodyEntries", 0, "n", 1, "Contract.Requires(n>=0); for(int i=0;i<n;i++) {}")]
    [TestCase("LoopBodyEntries", 0, "n", 1, "for(int i=0;i<n;i++) {}")]
    [TestCase("ArrayAllocations", 0, "n", 1, "for(int i=0;i<n;i++) { var a = new int[2]; }")]
    [TestCase("AllocatedElements", 0, "n", 2, "for(int i=0;i<n;i++) { var a = new int[2]; }")]
    [TestCase("AllocatedElements", 4, "n", 3, "var before=new int[4]; for(int i=0;i<n;i++) { var a = new int[3]; }")]
    [TestCase("LoopBodyEntries", 0, "values.Length", 1, "for(int i=0;i<values.Length;i++) { values[i]=i; }")]
    public async Task SelectedNativeClaimsProveConstantAndLinearBounds(string metric, long constant, string size, long coefficient, string body)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(body);
        var preparation = Prepare(Source($"[ResourceUpperBound(ResourceMetric.{metric}, {constant}, \"{size}\", {coefficient})]", body));
        Assert.That(preparation.ResourceProgram, Is.Not.Null, preparation.ResourceUnknownReason);
        var results = await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        var result = results.Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason ?? result.Reason.ToString());
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ResourceBound.UnknownReason, Is.Null);
        Assert.That(result.ProofCore, Has.Some.StartsWith("native-resource:"));
        Assert.That(result.Assumptions.All(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : !assumption.Used), Is.True);
    }

    [Test]
    public async Task EachMetricHasAnIndependentClaimOutcome()
    {
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.ArrayAllocations, 1)] [ResourceUpperBound(ResourceMetric.AllocatedElements, 2)]", "var a=new int[3];"));
        var results = await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        Assert.That(results, Has.Length.EqualTo(2));
        Assert.That(results[0].Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(results[1].Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(results[1].ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
        Assert.That(results[1].ResourceBound!.InferredUpperBound, Is.Null);
    }

    [TestCase("while(true) {}")]
    [TestCase("System.Console.WriteLine(n);")]
    public async Task UnsupportedBodiesRemainExplicitUnknown(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.ArrayAllocations, 0)]", body));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound?.InferredUpperBound, Is.Null);
    }

    [TestCase("[ResourceUpperBound((ResourceMetric)99, 0)]")]
    [TestCase("[ResourceUpperBound(ResourceMetric.ArrayAllocations, -1)]")]
    [TestCase("[ResourceUpperBound(ResourceMetric.ArrayAllocations, 0, \"missing\", 1)]")]
    [TestCase("[ResourceUpperBound(ResourceMetric.ArrayAllocations, 0, \"n\", 0)]")]
    public async Task InvalidContractMetadataCannotBecomeAZeroBoundProof(string attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var preparation = Prepare(Source(attributes, ""));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
    }

    [Test]
    public void ArtifactConstraintMustMatchItsSealedManifestClaim()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var claim = artifact.Callables.Single().EffectClaims.Single();
        claim.Constraint.ResourceUpperBound!.Metric = WorkerResourceMetric.ArrayAllocations;
        CompilerEffectClaimArtifactCodec.Seal(claim);
        Action serialize = () => Read(artifact);
        Rejects(serialize);
    }

    [Test]
    public void ForeignProgramInputsAndEvidenceWithoutAClaimAreRejected()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var foreign = ResourceExpression.InputSize(new ResourceInput(99, ResourceInputKind.NonNegativeScalar, "foreign", int.MaxValue));
        artifact.Callables.Single().ResourceProgram = ResourceProgramCodec.Encode(ResourceProgram.Array(foreign), TestContext.CurrentContext.CancellationToken);
        Action foreignInput = () => Read(artifact);
        Rejects(foreignInput);
        var other = CreateArtifact(Source("[ZeroAllocations]", ""));
        other.Callables.Single().ResourceProgram = ResourceProgramCodec.Encode(ResourceProgram.Zero, TestContext.CurrentContext.CancellationToken);
        Action unowned = () => Read(other);
        Rejects(unowned);
    }

    [Test]
    public void NumericConstraintChangesTheManifestSeal()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var before = artifact.Manifest.Hash;
        artifact.Manifest.Claims.Single().ResourceUpperBound!.UpperBound = ResourceExpressionCodec.Encode(ResourceExpression.Literal(4), TestContext.CurrentContext.CancellationToken);
        WorkerProtocolJson.SealManifest(artifact.Manifest);
        Assert.That(artifact.Manifest.Hash, Is.Not.EqualTo(before));
    }

    [Test]
    public void PublicContractRejectsInvalidRuntimeArguments()
    {
        Action invalidMetric = () => _ = new ResourceUpperBoundAttribute((ResourceMetric)0, 0);
        Action negative = () => _ = new ResourceUpperBoundAttribute(ResourceMetric.ArrayAllocations, -1);
        Action noSize = () => _ = new ResourceUpperBoundAttribute(ResourceMetric.ArrayAllocations, 0, coefficient: 1);
        Assert.Throws<ArgumentOutOfRangeException>(invalidMetric);
        Assert.Throws<ArgumentOutOfRangeException>(negative);
        Assert.Throws<ArgumentException>(noSize);
    }
    [Test]
    public void CompilerCannotSupplyAResourceProofOrTrustedCostVerdict()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.ArrayAllocations, 0)]", ""));
        var claim = artifact.Callables.Single().EffectClaims.Single();
        claim.Outcome = WorkerClaimOutcome.Proven;
        claim.Reason = WorkerClaimReason.None;
        claim.Certainty = WorkerEffectEvidenceCertainty.TrustedCompleteBoundary;
        CompilerEffectClaimArtifactCodec.Seal(claim);
        Action serialize = () => Read(artifact);
        Rejects(serialize);
    }
    [TestCase("missing-entry", "ResourceEntryUnavailable")]
    [TestCase("abstract-entry", "ResourceEntryUnavailable")]
    [TestCase("incomplete-entry", "ResourceEntryUnavailable")]
    [TestCase("invalid-claim", "UnsupportedResourceContract")]
    [TestCase("missing-program", "ResourceProgramUnavailable")]
    [TestCase("metadata", "ResourceMetadataAssumptionsNotBound")]
    [TestCase("malformed-bound", "MalformedResourceEvidence")]
    public void IncompleteNativeEvidenceAlwaysReturnsUnknownWithoutNumericBounds(string scenario, string expected)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(expected);
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var claim = preparation.EffectClaims.Single();
        switch (scenario)
        {
            case "missing-entry":
                preparation = preparation with { Total = null };
                break;
            case "abstract-entry":
                preparation = preparation with { Total = preparation.Total! with { IsBodyAbstraction = true } };
                break;
            case "incomplete-entry":
                preparation = preparation with { Total = preparation.Total! with { EffectsCompleteAtEntry = false } };
                break;
            case "invalid-claim":
                preparation = preparation with { Total = preparation.Total! with { ValidEffectClaimIds = [] } };
                break;
            case "missing-program":
                preparation = preparation with { ResourceProgram = null, ResourceUnknownReason = null };
                break;
            case "metadata":
                preparation = preparation with { ResourceProgram = ResourceProgram.MetadataBoundary };
                break;
            default:
                claim.Constraint.ResourceUpperBound!.UpperBound = System.Text.Encoding.UTF8.GetBytes("{}");
                CompilerEffectClaimArtifactCodec.Seal(claim);
                break;
        }
        var result = NativeResourceContractVerifier.Verify(preparation, claim, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.UnknownReason, Is.EqualTo(expected));
        Assert.That(result.EffectWitness, Is.Null);
    }

    [Test]
    public void UnownedNativeResourceClaimsAreRejectedBeforeEvaluation()
    {
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var claim = preparation.EffectClaims.Single();
        claim.ClaimId = "foreign";
        CompilerEffectClaimArtifactCodec.Seal(claim);
        Action verify = () => NativeResourceContractVerifier.Verify(preparation, claim, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<ArgumentException>(verify);
    }
}
