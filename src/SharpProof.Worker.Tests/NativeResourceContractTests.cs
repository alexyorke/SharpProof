using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed partial class NativeResourceContractTests
{
    private static string Source(string attributes, string body)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(body);
        return "using SharpProof.Attributes; public static class C { " + attributes + " public static void Target(int n, int[] values) { " + body + " } }";
    }

    private static CompilerManifestArtifact CreateArtifact(string source, bool reviewedRuntime = false, bool x64 = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var initial = TestCompilation.Create("ResourceContract", ("Subject.cs", source));
        if (reviewedRuntime)
        {
            const string references = "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.32/ref/net8.0";
            initial = initial.WithReferences(Directory.GetFiles(references, "*.dll")
                .Append(typeof(ResourceUpperBoundAttribute).Assembly.Location)
                .Select(static path => path is null ? throw new ArgumentNullException(nameof(path)) : Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path)));
        }
        if (x64)
        { initial = initial.WithOptions(initial.Options.WithPlatform(Microsoft.CodeAnalysis.Platform.X64)); }
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

    [TestCase("LoopBodyEntries", 0, "n", 2, "int left=n,right=n; while(left>0||right>0) { if(left>0) left--; else right--; }")]
    [TestCase("LoopBodyEntries", 0, "n", 2, "int left=n,right=n; while(left>0||right>0) { if(left>0) left/=2; else right/=2; }")]
    [TestCase("ArrayAllocations", 0, "n", 2, "int left=n,right=n; while(left>0||right>0) { if(left>0) { var a=new int[2]; left--; } else { var b=new int[3]; right--; } }")]
    [TestCase("AllocatedElements", 0, "n", 5, "int left=n,right=n; while(left>0||right>0) { if(left>0) { var a=new int[2]; left--; } else { var b=new int[3]; right--; } }")]
    [TestCase("LoopBodyEntries", 0, "n", 1, "int i=0; while(i<n) { i++; }")]
    [TestCase("LoopBodyEntries", 0, "n", 1, "checked { int i=0; while(i<n) { i+=2; } }")]
    [TestCase("LoopBodyEntries", 0, "n", 1, "checked { int i=1; while(i<n) { i*=2; } }")]
    [TestCase("LoopBodyEntries", 0, "n", 1, "int remaining=n; while(remaining>2) { remaining-=3; }")]
    [TestCase("ArrayAllocations", 0, "n", 1, "int i=0; while(i<n) { var a=new int[2]; i++; }")]
    [TestCase("AllocatedElements", 0, "n", 2, "int i=0; while(i<n) { var a=new int[2]; i++; }")]
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
        Assert.That(results[1].Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(results[1].ResourceBound!.ObservedLowerBound, Is.EqualTo("3"));
        Assert.That(results[1].ResourceBound!.EvaluatedUpperBound, Is.EqualTo("2"));
        Assert.That(results[1].ResourceBound!.UnknownReason, Is.Null);
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
    [TestCase("ArrayAllocations", 1, "var a=new int[0]; var b=new int[0];", "2")]
    [TestCase("AllocatedElements", 2, "var a=new int[3];", "3")]
    [TestCase("AllocatedElements", 2, "Contract.Requires(n>=3); var a=new int[n];", "3")]
    [TestCase("AllocatedElements", 2, "var a=new int[3]; int denominator=0; int q=1/denominator;", "3")]
    public async Task ExactArrayPrefixesRefuteNumericContracts(string metric, long limit, string body, string expected)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(expected);
        var preparation = Prepare(Source($"[ResourceUpperBound(ResourceMetric.{metric}, {limit})]", body));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo(expected));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.EffectWitness!.Kind, Is.EqualTo("resource-bound-exceeded"));
        Assert.That(result.EffectWitness.Location.Line, Is.GreaterThan(0));
        Assert.That(result.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
    }

    [Test]
    public async Task LinearAllocationViolationUsesTheReplayedEntrySize()
    {
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.ArrayAllocations, 0, \"n\", 1)]",
            "for(int i=0;i<n;i++) { var a=new int[0]; var b=new int[0]; }"));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("2"));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo("1"));
        Assert.That(result.Model.Single(value => value is null ? throw new ArgumentNullException(nameof(value)) : value.Variable == "parameter:0").Value, Is.EqualTo("1"));
    }

    [TestCase("Contract.Requires(n>=0); if(n>=0)return; var a=new int[3];")]
    [TestCase("var a=new int[5000];")]
    public async Task LooseEnvelopesAndUnobservedLargeAllocationsNeverBecomeViolations(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 2)]", body));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
    }
    [Test]
    public async Task SolverCandidatesMustReplayDistinctConstrainedInputs()
    {
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 2)]",
            "Contract.Requires(n==7 && values!=null && values.Length==11); var a=new int[n];"));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("7"));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo("2"));
        Assert.That(result.Model.Single(value => value is null ? throw new ArgumentNullException(nameof(value)) : value.Variable == "parameter:0").Value, Is.EqualTo("7"));
        Assert.That(result.Model.Single(value => value is null ? throw new ArgumentNullException(nameof(value)) : value.Variable == "parameter:1").Kind, Is.EqualTo("Sequence"));
    }
    [TestCase("int left=2147483647,right=2147483647; while(left>0||right>0) { if(left>0) left-=2147483647; else right/=2147483647; }", 2, "3")]
    [TestCase("int left=2,right=3; while(left>0||right>0) { if(left>0) left--; else right--; }", 4, "5")]
    [TestCase("int left=-2,right=3; while(left>0||right>0) { if(left>0) left--; else right--; }", 2, "3")]
    [TestCase("int left=3,right=-2; while(left>0||right>0) { if(left>0) left--; else right--; }", 2, "3")]
    [TestCase("int left=8,right=9; while(left>0||right>0) { if(left>0) left/=2; else right/=3; }", 6, "7")]
    [TestCase("int left=2,right=1; while(left>0||right>0) { if(left>0) { for(int j=0;j<3;j++) {} left--; } else right--; }", 8, "9")]
    [TestCase("int left=2,right=3; while((left>0)||(right>0)) { if(left>0) left--; else right--; }", 4, "5")]
    [TestCase("int i=0; while(i<5) { i+=2; }", 2, "3")]
    [TestCase("int i=1; while(i<17) { i*=2; }", 2, "3")]
    [TestCase("int remaining=7; while(remaining>1) { remaining-=2; }", 1, "2")]
    [TestCase("checked { int i=2147483646; while(i<2147483647) { i+=2; } }", 0, "1")]
    [TestCase("for(int i=0;i<4;i++) {}", 2, "3")]
    [TestCase("for(int i=0;(i<4);i++) {}", 2, "3")]
    [TestCase("for(long i=0;i<4;i++) {}", 2, "3")]
    [TestCase("for(int i=1;i<17;i*=2) {}", 2, "3")]
    [TestCase("for(int i=7;i>0;i-=2) {}", 1, "2")]
    [TestCase("int remaining=5; while(remaining>0) { remaining/=2; }", 1, "2")]
    [TestCase("for(int i=0;i<2;i++) { for(int j=0;j<3;j++) {} }", 6, "7")]
    [TestCase("checked { for(int i=2147483646;i<2147483647;i+=2) {} }", 0, "1")]
    [TestCase("for(int i=0;i<4;i++) { return; }", 0, "1")]
    public async Task LoopViolationsCountOnlyActualSourceBodyEntries(string body, long limit, string expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(expected);
        var preparation = Prepare(Source($"[ResourceUpperBound(ResourceMetric.LoopBodyEntries, {limit})]", body));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo(expected));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(result.EffectWitness!.Effects, Is.EqualTo(WorkerEffectSet.None));
        Assert.That(result.EffectWitness.Location.Line, Is.GreaterThan(0));
    }

    [Test]
    public async Task SourceCallInliningPreservesLoopEntrySemantics()
    {
        var preparation = Prepare("""
            using SharpProof.Attributes;
            public static class C {
                [ResourceUpperBound(ResourceMetric.LoopBodyEntries, 5)]
                public static void Target() { Leaf(); Leaf(); }
                static void Leaf() { for(int i=0;i<4;i++) {} }
            }
            """);
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("6"));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo("5"));
    }

    [TestCase("for(int i=0;i<4;i++) { return; }")]
    [TestCase("Contract.Requires(n<=0); for(int i=0;i<n;i++) {}")]
    [TestCase("int x=3; if(x>0) {} if(x>0) {} for(int i=0;i<4;i++) { return; }")]
    public async Task LooseLoopEnvelopesDoNotProduceInventedEntries(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.LoopBodyEntries, 1)]", body));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
    }
    [TestCase("Min")]
    [TestCase("Max")]
    public async Task ReviewedMetadataCostsRequireDeclaredOwnedReferenceEvidence(string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var preparation = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 0)]",
            $"int value=System.Math.{method}(n,0);"), reviewedRuntime: true));
        Assert.That(preparation.Compilation.References, Is.Empty);
        Assert.That(preparation.Compilation.ResourceMetadataReferences, Has.Length.EqualTo(1));
        Assert.That(preparation.Compilation.ResourceMetadataReferences[0].Identity, Is.EqualTo(preparation.ResourceProgram!.Children.Single().Metadata!.AssemblyIdentity));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        var metadata = result.ResourceBound!.MetadataAssumptions.Single();
        Assert.That(metadata.WitnessIdentifier, Is.EqualTo(method == "Min" ? "bcl.math.min.int32-int32" : "bcl.math.max.int32-int32"));
        Assert.That(metadata.RuntimeModel, Is.EqualTo("net8.0.32"));
        Assert.That(metadata.EvidenceSource, Does.Contain("b9a49e4a6d3a444a3685e318c74a7892d01873f7"));
        Assert.That(metadata.CatalogSha256, Has.Length.EqualTo(64));
        Assert.That(metadata.ReferenceSha256, Has.Length.EqualTo(64));
        Assert.That(result.Assumptions.Single(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id == metadata.AssumptionId).Used, Is.True);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void MetadataProjectionRequiresADecidedResourceClaim(int corruption)
    {
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 0)]", ""));
        var binding = new NativeResourceMetadataBinding(preparation);
        var result = new WorkerClaimResult
        {
            Outcome = corruption == 0 ? WorkerClaimOutcome.Proven : corruption == 1 ? WorkerClaimOutcome.Unknown : (WorkerClaimOutcome)999,
            ResourceBound = corruption == 0 ? null : new WorkerResourceBoundEvidence()
        };
        Action project = () => binding.Project(result);
        Assert.Throws<ArgumentException>(project);
    }
    [Test]
    public async Task RefutedResourceClaimsRetainReviewedMetadataDependencies()
    {
        var preparation = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 0)]",
            "var values2=new int[1]; int value=System.Math.Max(n,0);"), reviewedRuntime: true));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("1"));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo("0"));
        var dependency = result.ResourceBound.MetadataAssumptions.Single();
        Assert.That(result.Assumptions.Single(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Id == dependency.AssumptionId).Used, Is.True);
    }
    [Test]
    public async Task MetadataCostSummaryDoesNotSupplyAnExactExecutionValue()
    {
        var preparation = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 0)]",
            "int value=System.Math.Max(n,0); var values2=new int[1];"), reviewedRuntime: true));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
    }
    [TestCase("undeclared")]
    [TestCase("foreign-owner")]
    [TestCase("missing-reference")]
    [TestCase("invalid-reference-hash")]
    [TestCase("conflicting-reference")]
    [TestCase("wrong-target-framework")]
    [TestCase("null-references")]
    [TestCase("null-reference")]
    [TestCase("missing-module")]
    [TestCase("unreviewed-catalog")]
    [TestCase("unreviewed-method")]
    [TestCase("wrong-runtime")]
    public async Task UnboundMetadataCannotSupplyNativeNumericEvidence(string corruption)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        var preparation = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 0)]", "int value=System.Math.Max(n,0);"), reviewedRuntime: true));
        var reference = preparation.ResourceProgram!.Children.Single().Metadata!;
        switch (corruption)
        {
            case "undeclared":
                preparation.Entry.Assumptions = [];
                break;
            case "foreign-owner":
                preparation.Entry.Assumptions.Single().Id = "foreign";
                break;
            case "missing-reference":
                preparation = preparation with { Compilation = new CompilerCompilationSnapshot() };
                break;
            case "wrong-target-framework":
                preparation.Compilation.TargetFramework = "net10.0";
                break;
            case "null-references":
                preparation.Compilation.ResourceMetadataReferences = null!;
                break;
            case "null-reference":
                preparation.Compilation.ResourceMetadataReferences = [null!];
                break;
            case "missing-module":
                preparation.Compilation.ResourceMetadataReferences.Single(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.Identity == reference.AssemblyIdentity).Modules = [];
                break;
            case "conflicting-reference":
                preparation.Compilation.ResourceMetadataReferences = [.. preparation.Compilation.ResourceMetadataReferences, new CompilerReferenceSnapshot {
                    Kind = "Assembly", Identity = reference.AssemblyIdentity,
                    Modules = [new CompilerReferenceModuleSnapshot { Sha256 = new string('0',64) }]
                }];
                break;
            case "invalid-reference-hash":
                preparation.Compilation.ResourceMetadataReferences.Single(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.Identity == reference.AssemblyIdentity).Modules[0].Sha256 = "invalid";
                break;
            default:
                preparation = preparation with
                {
                    ResourceProgram = ResourceProgram.MetadataCall(new ResourceMetadataReference(
                        corruption == "unreviewed-method" ? "bcl.math.abs.int32" : reference.WitnessIdentifier,
                        corruption == "unreviewed-catalog" ? new string('0', 64) : reference.CatalogSha256,
                        corruption == "wrong-runtime" ? reference.AssemblyIdentity.Replace("8.0.0.0", "10.0.0.0", StringComparison.Ordinal) : reference.AssemblyIdentity))
                };
                break;
        }
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceMetadataAssumptionsNotBound"));
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
    }

    [Test]
    public async Task MetadataDependenciesInSourceCalleesBindToTheRootCallable()
    {
        var preparation = Read(CreateArtifact("""
            using SharpProof.Attributes;
            public static class C {
                [ResourceUpperBound(ResourceMetric.ArrayAllocations, 0)]
                public static void Target(int n) { int value=Leaf(n); }
                static int Leaf(int n) => System.Math.Min(n,0)+System.Math.Max(n,0);
            }
            """, reviewedRuntime: true));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.MetadataAssumptions, Has.Length.EqualTo(2));
        Assert.That(result.Assumptions.Count(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) : assumption.Used), Is.EqualTo(2));
    }
    [TestCase("LoopBodyEntries", 7)]
    [TestCase("ArrayAllocations", 7)]
    [TestCase("AllocatedElements", 18)]
    public async Task PhasedNativeCertificatesPreserveSeparatePrefixCosts(string metric, long bound)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var preparation = Prepare(Source($"[ResourceUpperBound(ResourceMetric.{metric}, {bound})]",
            "int left=5,right=4; while(left>0||right>0) { if(left>0) { var a=new int[2]; left-=2; } else { var b=new int[3]; right--; } }"));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(bound.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public async Task PhasedBranchSelectionCannotInventEntriesAfterEarlyReturn()
    {
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.LoopBodyEntries, 1)]",
            "int left=3,right=3; while(left>0||right>0) { if(left>0) { return; left--; } else right--; }"));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceBoundComparisonUnproved"));
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
    }
    [Test]
    public async Task PhasedCalleeInliningPreservesOwnedEntryCounts()
    {
        var preparation = Read(CreateArtifact("""
            using SharpProof.Attributes;
            public static class C {
                [ResourceUpperBound(ResourceMetric.LoopBodyEntries, 9)]
                public static void Target() { Leaf(); Leaf(); }
                private static void Leaf() {
                    int left=2,right=3;
                    while(left>0||right>0) { if(left>0) left--; else right--; }
                }
            }
            """));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("10"));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo("9"));
    }
    [Test]
    public async Task PhasedRanksKeepPhysicalDomainsSeparateFromNaturalCostSums()
    {
        var preparation = Read(CreateArtifact("""
            using SharpProof.Attributes;
            public static class C {
                [ResourceUpperBound(ResourceMetric.LoopBodyEntries, 0, "n", 2)]
                public static void Target(long n) {
                    long left=n,right=n;
                    while(left>0||right>0) { if(left>0) left--; else right--; }
                }
            }
            """));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        var check = NativeResourceProgramVerifier.Verify(preparation.ResourceProgram!, preparation.Entry.CallableId, preparation.Total!, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Verified, Is.True, check.UnknownReason);
        Assert.That(check.Cost!.LoopEntries.Maximum, Is.EqualTo(new System.Numerics.BigInteger(long.MaxValue) * 2));
    }
}
