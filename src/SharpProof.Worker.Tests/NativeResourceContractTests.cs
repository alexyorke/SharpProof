using Microsoft.CodeAnalysis.CSharp;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed partial class NativeResourceContractTests
{
    [TestCase("ordinal", "ResourceLowerInputMismatch")]
    [TestCase("kind", "ResourceLowerInputMismatch")]
    [TestCase("name", "ResourceLowerInputMismatch")]
    [TestCase("multiple", "AsymptoticMultipleInputs")]
    public void LowerProjectionCannotPublishTightnessForAnUnmatchedInput(string corruption, string reason)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        ArgumentNullException.ThrowIfNull(reason);
        var owned = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        var foreign = ResourceExpression.InputSize(new ResourceInput(corruption is "ordinal" or "multiple" ? 1 : 0,
            corruption == "kind" ? ResourceInputKind.ArrayLength : ResourceInputKind.NonNegativeScalar,
            corruption is "name" or "multiple" ? "other" : "n", int.MaxValue));
        var lower = corruption == "multiple" ? ResourceExpression.Add(owned, foreign) : foreign;
        var upper = new WorkerResourceAsymptoticEvidence
        {
            UpperBound = "O(n)",
            PolynomialDegree = 1,
            LogarithmicDegree = 0,
            IsZero = false,
            InputOrdinal = 0,
            InputKind = nameof(ResourceInputKind.NonNegativeScalar),
            InputName = "n"
        };
        var method = typeof(NativeResourceContractVerifier).GetMethod("LowerEvidence",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        var result = (WorkerResourceLowerAsymptoticEvidence)method!.Invoke(null,
            [upper, lower, null, TestContext.CurrentContext.CancellationToken])!;
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
        Assert.That(result.InferredBound, Is.Null);
        Assert.That(result.LowerBound, Is.Null);
        Assert.That(result.TightBound, Is.Null);
        Assert.That(result.Growth, Is.Null);
    }
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
        var initial = TestCompilation.Create("ResourceContract", [("Subject.cs", source)]);
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
    [TestCase("[ResourceUpperBound(ResourceMetric.ArrayAllocations, 0, \"n.Length\", 1)]")]
    [TestCase("[ResourceUpperBound(ResourceMetric.ArrayAllocations, 0, \"n\", 0)]")]
    public async Task InvalidContractMetadataCannotBecomeAZeroBoundProof(string attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var preparation = Prepare(Source(attributes, ""));
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
        Assert.That(result.ResourceBound?.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound?.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound?.EvaluatedUpperBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
    }

    [Test]
    public void ArtifactConstraintMustMatchItsSealedManifestClaim()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var claim = artifact.Callables.Single().EffectClaims.Single();
        claim = claim with { Constraint = claim.Constraint with { ResourceUpperBound = claim.Constraint.ResourceUpperBound! with { Metric = WorkerResourceMetric.ArrayAllocations } } };
        CompilerEffectClaimArtifactCodec.Seal(claim);
        artifact = artifact with { Callables = artifact.Callables.SetItem(0, artifact.Callables.Single() with { EffectClaims = [claim] }) };
        Action serialize = () => Read(artifact);
        Rejects(serialize);
    }

    [Test]
    public void ForeignProgramInputsAndEvidenceWithoutAClaimAreRejected()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var foreign = ResourceExpression.InputSize(new ResourceInput(99, ResourceInputKind.NonNegativeScalar, "foreign", int.MaxValue));
        artifact = artifact with { Callables = artifact.Callables.SetItem(0, artifact.Callables.Single() with { ResourceProgram = ResourceProgramCodec.Encode(ResourceProgram.Array(foreign), TestContext.CurrentContext.CancellationToken) }) };
        Action foreignInput = () => Read(artifact);
        Rejects(foreignInput);
        var other = CreateArtifact(Source("[ZeroAllocations]", ""));
        other = other with { Callables = other.Callables.SetItem(0, other.Callables.Single() with { ResourceProgram = ResourceProgramCodec.Encode(ResourceProgram.Zero, TestContext.CurrentContext.CancellationToken) }) };
        Action unowned = () => Read(other);
        Rejects(unowned);
    }

    [TestCase("missing")]
    [TestCase("empty")]
    [TestCase("oversized")]
    [TestCase("control")]
    [TestCase("program-and-gap")]
    public void ResourceProofGapsMustBeValidAndExclusiveAtTheArtifactBoundary(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var hasProgram = mutation == "program-and-gap";
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.ArrayAllocations,1)]",
            hasProgram ? "var value=new int[1];" : "while(true) {}"));
        var control = Read(artifact);
        Assert.That(control.ResourceProgram, hasProgram ? Is.Not.Null : Is.Null);
        Assert.That(control.ResourceUnknownReason, hasProgram ? Is.Null : Is.Not.Null.And.Not.Empty);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellation);
        var root = JsonNode.Parse(json)!;
        var callable = root["callables"]![0]!.AsObject();
        switch (mutation)
        {
            case "missing":
                Assert.That(callable.Remove("resourceUnknownReason"), Is.True);
                break;
            case "empty":
                callable["resourceUnknownReason"] = "";
                break;
            case "oversized":
                callable["resourceUnknownReason"] = new string('x', 129);
                break;
            case "control":
                callable["resourceUnknownReason"] = "Unsupported\nBody";
                break;
            case "program-and-gap":
                callable["resourceUnknownReason"] = "UnsupportedBody";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Action decode = () => CompilerManifestArtifactJson.DeserializePrepared(root.ToJsonString(), out _, cancellation);
        Rejects(decode);
    }

    [Test]
    public void NumericConstraintChangesTheManifestSeal()
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var before = artifact.Manifest.Hash;
        artifact = artifact with { Manifest = artifact.Manifest with { Claims = artifact.Manifest.Claims.SetItem(0, artifact.Manifest.Claims.Single() with { ResourceUpperBound = artifact.Manifest.Claims.Single().ResourceUpperBound! with { UpperBound = ResourceExpressionCodec.Encode(ResourceExpression.Literal(4), TestContext.CurrentContext.CancellationToken) } }) } };
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
        claim = claim with { Outcome = WorkerClaimOutcome.Proven };
        claim = claim with { Reason = WorkerClaimReason.None };
        claim = claim with { Certainty = WorkerEffectEvidenceCertainty.TrustedCompleteBoundary };
        CompilerEffectClaimArtifactCodec.Seal(claim);
        artifact = artifact with { Callables = artifact.Callables.SetItem(0, artifact.Callables.Single() with { EffectClaims = [claim] }) };
        Action serialize = () => Read(artifact);
        Rejects(serialize);
    }

    [TestCase("unspecified-metric")]
    [TestCase("undefined-metric")]
    [TestCase("cost-model")]
    [TestCase("metric-cost-model")]
    [TestCase("expression-model")]
    public void ResourceClaimsRejectUnsupportedMetricAndModels(string corruption)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var claim = preparation.EffectClaims.Single();
        var control = NativeResourceContractVerifier.Verify(preparation, claim, TestContext.CurrentContext.CancellationToken);
        Assert.That(control.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), control.ResourceBound?.UnknownReason);
        Assert.That(control.ResourceBound!.InferredUpperBound, Is.EqualTo("3"));
        var constraint = claim.Constraint.ResourceUpperBound!;
        constraint = corruption switch
        {
            "unspecified-metric" => constraint with { Metric = WorkerResourceMetric.Unspecified },
            "undefined-metric" => constraint with { Metric = (WorkerResourceMetric)999 },
            "cost-model" => constraint with { CostModel = "unreviewed-cost-model" },
            "metric-cost-model" => constraint with { CostModel = WorkerResourceModels.AllocationCostModel },
            "expression-model" => constraint with { ExpressionModel = WorkerResourceModels.Expression + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        claim = claim with { Constraint = claim.Constraint with { ResourceUpperBound = constraint } };
        if (corruption == "undefined-metric")
        {
            Action seal = () => CompilerEffectClaimArtifactCodec.Seal(claim);
            Assert.Throws<ArgumentOutOfRangeException>(seal);
            return;
        }
        CompilerEffectClaimArtifactCodec.Seal(claim);
        preparation = preparation with { EffectClaims = [claim] };
        Action verify = () => NativeResourceContractVerifier.Verify(preparation, claim, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<InvalidDataException>(verify);
    }

    [TestCase("PeakAuxiliaryBytes")]
    [TestCase("RetainedBytes")]
    public async Task LifetimeContractsRequireByteStorageEvenWhenSourceElementCostsAreVerified(string metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var artifact = CreateArtifact("using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.{metric},31,nameof(n),4)] public static int[] Target(int n) => new int[n]; }}",
            reviewedRuntime: true, x64: true);
        var preparation = Read(artifact);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var control = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        Assert.That(control.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), control.ResourceBound?.UnknownReason);
        Assert.That(control.ProofCore, Does.Contain("resource-memory-envelope:" + NativeResourceMemoryEnvelope.ModelId));
        var activation = preparation.ResourceProgram!;
        Assert.That(activation.Kind, Is.EqualTo(ResourceProgramKind.Activation));
        var array = activation.Children.Single();
        Assert.That(array.Kind, Is.EqualTo(ResourceProgramKind.AllocateArray));
        var untyped = ResourceProgram.Activate(activation.Activation, ResourceProgram.Array(array.Length!, array.LowerLength));
        artifact = artifact with
        {
            Callables = artifact.Callables.SetItem(0, artifact.Callables.Single() with
            {
                ResourceProgram = ResourceProgramCodec.Encode(untyped, cancellation)
            })
        };
        preparation = Read(artifact);
        var sourceCosts = NativeResourceProgramVerifier.Verify(preparation.ResourceProgram!, preparation.Entry.CallableId,
            preparation.Total!, cancellation);
        Assert.That(sourceCosts.Verified, Is.True, sourceCosts.UnknownReason);
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("MissingResourceAllocationStorage"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.EffectWitness, Is.Null);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ComparisonBudgetIsConservativeForLargeOwnedDeclaredBounds(bool directComparisonFirst)
    {
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.LoopBodyEntries, 0, nameof(n), 1)]",
            "for(int i=0;i<n;i++) {}"));
        var preparation = Read(artifact);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var claim = artifact.Callables.Single().EffectClaims.Single();
        var input = ResourceExpressionCodec.DecodeForCallable(claim.Constraint.ResourceUpperBound!.UpperBound,
            preparation.Entry.CallableId, preparation.Total!, cancellation);
        var alternatives = Enumerable.Range(1, 1024).Select(offset => ResourceExpression.Difference(input, offset)).ToArray();
        while (alternatives.Length > 1)
        {
            var combined = new ResourceExpression[alternatives.Length / 2];
            for (var index = 0; index < combined.Length; index++)
            { combined[index] = ResourceExpression.Max(alternatives[index * 2], alternatives[index * 2 + 1]); }
            alternatives = combined;
        }
        // Both orders declare the same valid upper bound. A direct comparison
        // can finish early; exhausting the bounded search must publish Unknown.
        var limit = directComparisonFirst ? ResourceExpression.Max(input, alternatives[0]) :
            ResourceExpression.Max(alternatives[0], input);
        var constraint = claim.Constraint.ResourceUpperBound! with { UpperBound = ResourceExpressionCodec.Encode(limit, cancellation) };
        claim = claim with { Constraint = claim.Constraint with { ResourceUpperBound = constraint } };
        CompilerEffectClaimArtifactCodec.Seal(claim);
        artifact = artifact with
        {
            Callables = artifact.Callables.SetItem(0, artifact.Callables.Single() with { EffectClaims = [claim] }),
            Manifest = artifact.Manifest with
            {
                Claims = artifact.Manifest.Claims.SetItem(0, artifact.Manifest.Claims.Single() with { ResourceUpperBound = constraint })
            }
        };
        WorkerProtocolJson.SealManifest(artifact.Manifest);
        preparation = Read(artifact);
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        if (directComparisonFirst)
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
            Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("max(0, n)"));
        }
        else
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceProofBudgetExceeded"));
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
            Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
            Assert.That(result.ResourceBound.Asymptotic, Is.Null);
            Assert.That(result.ResourceBound.Expected, Is.Null);
            Assert.That(result.ResourceBound.Accounting, Is.Empty);
            Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
            Assert.That(result.EffectWitness, Is.Null);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TaskJoinComparisonBudgetPublishesEvidenceOnlyAfterComparisonCompletes(bool directComparisonFirst)
    {
        var artifact = CreateArtifact("""
            using SharpProof.Attributes;
            using System.Threading.Tasks;
            public static class C {
                [ResourceUpperBound(ResourceMetric.LoopBodyEntries, 0, nameof(n), 1)]
                public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(0)));
                static void Child(int count) { for(int i=0;i<count;i++) {} }
            }
            """, reviewedRuntime: true);
        var preparation = Read(artifact);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(preparation.Total, Is.Null);
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        Assert.That(preparation.ForkJoinProjection, Is.Not.Null, preparation.ResourceUnknownReason);
        var claim = artifact.Callables.Single().EffectClaims.Single();
        var input = ResourceExpressionCodec.DecodeForCallable(claim.Constraint.ResourceUpperBound!.UpperBound,
            preparation.Entry.CallableId, preparation.TotalEntry!, cancellation);
        var source = NativeForkJoinSourceCostVerifier.Check(preparation.ForkJoinProjection!, preparation.TotalEntry!, cancellation);
        Assert.That(source.Upper, Is.Not.Null, source.UnknownReason);
        Assert.That(source.Upper!.LoopEntries, Is.EqualTo(input));
        var alternatives = Enumerable.Range(1, 1024).Select(offset => ResourceExpression.Difference(input, offset)).ToArray();
        while (alternatives.Length > 1)
        {
            var combined = new ResourceExpression[alternatives.Length / 2];
            for (var index = 0; index < combined.Length; index++)
            { combined[index] = ResourceExpression.Max(alternatives[index * 2], alternatives[index * 2 + 1]); }
            alternatives = combined;
        }
        // The same valid task bound either proves immediately or exhausts the
        // comparison budget before reaching the final direct alternative.
        var limit = directComparisonFirst ? ResourceExpression.Max(input, alternatives[0]) :
            ResourceExpression.Max(alternatives[0], input);
        var constraint = claim.Constraint.ResourceUpperBound! with { UpperBound = ResourceExpressionCodec.Encode(limit, cancellation) };
        claim = claim with { Constraint = claim.Constraint with { ResourceUpperBound = constraint } };
        CompilerEffectClaimArtifactCodec.Seal(claim);
        artifact = artifact with
        {
            Callables = artifact.Callables.SetItem(0, artifact.Callables.Single() with { EffectClaims = [claim] }),
            Manifest = artifact.Manifest with
            {
                Claims = artifact.Manifest.Claims.SetItem(0, artifact.Manifest.Claims.Single() with { ResourceUpperBound = constraint })
            }
        };
        WorkerProtocolJson.SealManifest(artifact.Manifest);
        preparation = Read(artifact);
        var result = (await NativeEffectClaims.VerifyAsync(preparation, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        if (directComparisonFirst)
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
            Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("max(0, n)"));
            Assert.That(result.ResourceBound.ExecutionScope, Is.EqualTo(WorkerResourceModels.TaskJoinScope));
            var dependency = result.ResourceBound.MetadataAssumptions.Single();
            Assert.That(dependency.RuntimeModel, Is.EqualTo(WorkerResourceModels.TaskJoinModel));
            Assert.That(result.Assumptions.Single().Id, Is.EqualTo(dependency.AssumptionId));
            Assert.That(result.Assumptions.Single().Used, Is.True);
            Assert.That(result.ProofCore, Does.Contain("resource-task-model:" + dependency.RuntimeModel));
        }
        else
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ForkJoinResourceBudgetExceeded"));
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
            Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
            Assert.That(result.ResourceBound.Asymptotic, Is.Null);
            Assert.That(result.ResourceBound.Expected, Is.Null);
            Assert.That(result.ResourceBound.Accounting, Is.Empty);
            Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
            Assert.That(result.ProofCore, Is.Empty);
            Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
            Assert.That(result.EffectWitness, Is.Null);
        }
    }

    [TestCase("missing-entry", "ResourceEntryUnavailable")]
    [TestCase("abstract-entry", "ResourceEntryUnavailable")]
    [TestCase("incomplete-entry", "ResourceEntryUnavailable")]
    [TestCase("invalid-claim", "UnsupportedResourceContract")]
    [TestCase("missing-program", "ResourceProgramUnavailable")]
    [TestCase("metadata", "ResourceMetadataAssumptionsNotBound")]
    [TestCase("unsupported-contract", "UnsupportedResourceContract")]
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
            case "unsupported-contract":
                claim = claim with { Reason = WorkerClaimReason.UnsupportedContract, Certainty = WorkerEffectEvidenceCertainty.Unavailable };
                CompilerEffectClaimArtifactCodec.Seal(claim);
                preparation = preparation with { EffectClaims = [claim] };
                break;
            default:
                claim = claim with { Constraint = claim.Constraint with { ResourceUpperBound = claim.Constraint.ResourceUpperBound! with { UpperBound = System.Text.Encoding.UTF8.GetBytes("{}") } } };
                CompilerEffectClaimArtifactCodec.Seal(claim);
                preparation = preparation with { EffectClaims = [claim] };
                break;
        }
        var result = NativeResourceContractVerifier.Verify(preparation, claim, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.UnknownReason, Is.EqualTo(expected));
        Assert.That(result.EffectWitness, Is.Null);
    }

    [TestCase("SourceActivationDepth")]
    [TestCase("SourceStackBytes")]
    public async Task MissingActivationCannotBecomeAZeroStackProofOrReplayRefutation(string metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var preparation = Prepare(Source($"[ResourceUpperBound(ResourceMetric.{metric}, 0)]", ""));
        preparation = preparation with { ResourceProgram = ResourceProgram.Zero };
        var claim = preparation.EffectClaims.Single();
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var result = NativeResourceContractVerifier.Verify(preparation, claim, cancellation);
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceStackRootActivationMissing"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
        var replay = await NativeResourceContractVerifier.VerifyAsync(preparation, claim, new WorkerBudgets(), cancellation);
        Assert.That(replay.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(replay.ResourceBound!.UnknownReason, Is.EqualTo(result.ResourceBound.UnknownReason));
        Assert.That(replay.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(replay.EffectWitness, Is.Null);
    }

    [Test]
    public void UnownedNativeResourceClaimsAreRejectedBeforeEvaluation()
    {
        var preparation = Prepare(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 3)]", "var a=new int[3];"));
        var claim = preparation.EffectClaims.Single();
        claim = claim with { ClaimId = "foreign" };
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
        var artifact = CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements, 0)]",
            $"int value=System.Math.{method}(n,0);"), reviewedRuntime: true);
        var preparation = Read(artifact);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(preparation.Compilation.References, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(System.Text.Json.JsonSerializer.Serialize(artifact.Compilation.References, WorkerProtocolJson.SharedOptions)));
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
        var capturedReference = preparation.Compilation.ResourceMetadataReferences.Single(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.Identity == reference.AssemblyIdentity);
        switch (corruption)
        {
            case "undeclared":
                preparation.Entry.Assumptions = [];
                break;
            case "foreign-owner":
                preparation = preparation with { Entry = preparation.Entry with { Assumptions = preparation.Entry.Assumptions.SetItem(0, preparation.Entry.Assumptions.Single() with { Id = "foreign" }) } };
                break;
            case "missing-reference":
                preparation = preparation with { Compilation = new CompilerCompilationSnapshot() };
                break;
            case "wrong-target-framework":
                preparation = preparation with { Compilation = preparation.Compilation with { TargetFramework = "net10.0" } };
                break;
            case "null-references":
                preparation = preparation with { Compilation = preparation.Compilation with { ResourceMetadataReferences = default } };
                break;
            case "null-reference":
                preparation = preparation with { Compilation = preparation.Compilation with { ResourceMetadataReferences = [null!] } };
                break;
            case "missing-module":
                preparation = preparation with { Compilation = preparation.Compilation with { ResourceMetadataReferences = preparation.Compilation.ResourceMetadataReferences.SetItem(preparation.Compilation.ResourceMetadataReferences.IndexOf(capturedReference), capturedReference with { Modules = [] }) } };
                break;
            case "conflicting-reference":
                preparation = preparation with
                {
                    Compilation = preparation.Compilation with
                    {
                        ResourceMetadataReferences = [.. preparation.Compilation.ResourceMetadataReferences, new CompilerReferenceSnapshot
                    {
                        Kind = "Assembly", Identity = reference.AssemblyIdentity,
                        Modules = [new CompilerReferenceModuleSnapshot { Sha256 = new string('0',64) }]
                    }]
                    }
                };
                break;
            case "invalid-reference-hash":
                preparation = preparation with { Compilation = preparation.Compilation with { ResourceMetadataReferences = preparation.Compilation.ResourceMetadataReferences.SetItem(preparation.Compilation.ResourceMetadataReferences.IndexOf(capturedReference), capturedReference with { Modules = capturedReference.Modules.SetItem(0, capturedReference.Modules[0] with { Sha256 = "invalid" }) }) } };
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
