using System.Numerics;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    private static readonly string[] BooleanCaseMetadataWitnesses = ["bcl.math.min.int32-int32", "bcl.math.max.int32-int32"];

    [TestCase("LoopBodyEntries", 0, 0, 0, 0)]
    [TestCase("ArrayAllocations", 1, 1, 1, 1)]
    [TestCase("AllocatedElements", 1, 3, 2, 3)]
    public async Task BooleanCasesPreserveMetadataDependenciesThroughConditionalCostDerivation(string metric,
        int falseUpper, int trueUpper, int expectedUpper, int worstUpper)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.{metric},{worstUpper})] public static void Target(int n," +
            "[ResourceBooleanInput(1,3)] bool selected) {if(selected) {int value=System.Math.Max(n,0); var a=new long[3];} " +
            "else {int value=System.Math.Min(n,0); var a=new byte[1];}} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var token = TestContext.CurrentContext.CancellationToken;
        var unbound = NativeResourceExpectedVerifier.Verify(target.ResourceProgram!,
            (ResourceCostMetric)Enum.Parse<WorkerResourceMetric>(metric), target.Entry.CallableId, target.Total!, null, token);
        Assert.That(unbound.UnknownReason, Is.EqualTo("ResourceMetadataAssumptionsNotBound"));
        Assert.That(unbound.ExpectedUpper, Is.Null);
        var unboundBytes = NativeResourceAllocationVerifier.Verify(target.ResourceProgram!, target.Entry.CallableId,
            target.Total!, new("CoreCLR", "8.0.32", "x64", 8), token);
        Assert.That(unboundBytes.UnknownReason, Is.EqualTo("ResourceMetadataAssumptionsNotBound"));
        Assert.That(unboundBytes.ConditionalUpperBound, Is.Null);
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), token)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        var expected = result.ResourceBound!.Expected!;
        Assert.That(expected.UnknownReason, Is.Null);
        Assert.That(expected.FalseUpperBound, Is.EqualTo(falseUpper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(expected.TrueUpperBound, Is.EqualTo(trueUpper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(expected.UpperBound, Is.EqualTo(expectedUpper.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(result.ProofCore, Does.Contain("resource-input-model:" + WorkerResourceModels.Expected));
        var dependencies = result.ResourceBound!.MetadataAssumptions;
        Assert.That(dependencies, Has.Length.EqualTo(2));
        Assert.That(dependencies.Select(static dependency => dependency is null ? throw new ArgumentNullException(nameof(dependency)) :
            dependency.WitnessIdentifier), Is.EquivalentTo(BooleanCaseMetadataWitnesses));
        foreach (var dependency in dependencies)
        {
            Assert.That(result.Assumptions.Single(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
                assumption.Id == dependency.AssumptionId).Used, Is.True);
        }
    }

    [Test]
    public async Task CountMetadataCannotAuthorizeByteCostsOrProbabilityEvidence()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,48)] public static void Target(int n," +
            "[ResourceBooleanInput(1,3)] bool selected) {if(selected) {int value=System.Math.Max(n,0); var a=new long[3];} " +
            "else {int value=System.Math.Min(n,0); var a=new byte[1];}} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var token = TestContext.CurrentContext.CancellationToken;
        var metadata = new NativeResourceMetadataBinding(target);
        var counts = NativeResourceProgramVerifier.Verify(target.ResourceProgram!, target.Entry.CallableId, target.Total!, metadata.Bind, token);
        Assert.That(counts.Verified, Is.True, counts.UnknownReason);
        var expected = NativeResourceExpectedVerifier.Verify(target.ResourceProgram!, ResourceCostMetric.AllocatedBytes,
            target.Entry.CallableId, target.Total!, new("CoreCLR", "8.0.32", "x64", 8), metadata.Bind, token);
        Assert.That(expected.UnknownReason, Is.EqualTo("ResourceExpectedConditionalCostUnproved"));
        Assert.That(expected.Distribution, Is.Null);
        Assert.That(expected.FalseUpper, Is.Null);
        Assert.That(expected.TrueUpper, Is.Null);
        Assert.That(expected.ExpectedUpper, Is.Null);
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), token)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceMetadataAssumptionsNotBound"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    [TestCase("AllocatedElements", false)]
    [TestCase("AllocatedElements", true)]
    [TestCase("AllocatedBytes", false)]
    [TestCase("AllocatedBytes", true)]
    public async Task ConditionalCostsRequireOwnedMetadataDeclarationsAndReferenceEvidence(string metric, bool invalidReference)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.{metric},48)] public static void Target(int n," +
            "[ResourceBooleanInput(1,3)] bool selected) {if(selected) {int value=System.Math.Max(n,0); var a=new long[3];} " +
            "else {int value=System.Math.Min(n,0); var a=new byte[1];}} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        if (invalidReference)
        {
            var reference = target.Compilation.ResourceMetadataReferences[0];
            target = target with
            {
                Compilation = target.Compilation with
                {
                    ResourceMetadataReferences =
                target.Compilation.ResourceMetadataReferences.SetItem(0, reference with
                {
                    Modules =
                    reference.Modules.SetItem(0, reference.Modules[0] with { Sha256 = "invalid" })
                })
                }
            };
        }
        else
        { target = target with { Entry = target.Entry with { Assumptions = [] } }; }
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceMetadataAssumptionsNotBound"));
        Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Expected, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
    }

    [TestCase("!selected", 7, 2, 6)]
    [TestCase("(bool)selected", 2, 7, 4)]
    [TestCase("true", 7, 7, 7)]
    [TestCase("false", 2, 2, 2)]
    [TestCase("other", 7, 7, 7)]
    public async Task BooleanInputModelsPreserveBranchMeaningWithoutModelingOtherFlags(string condition,
        int falseUpper, int trueUpper, int expectedUpper)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedElements,7)] public static void Target(" +
            "[ResourceBooleanInput(1,3)] bool selected,bool other) {if(" + condition +
            ") {var a=new int[7];} else {var b=new int[2];}} }";
        var target = Read(CreateArtifact(source));
        var token = TestContext.CurrentContext.CancellationToken;
        var check = NativeResourceExpectedVerifier.Verify(target.ResourceProgram!, ResourceCostMetric.AllocatedElements,
            target.Entry.CallableId, target.Total!, null, token);
        Assert.That(check.UnknownReason, Is.Null, target.ResourceUnknownReason);
        Assert.That(check.FalseUpper!.Constant, Is.EqualTo(new BigInteger(falseUpper)));
        Assert.That(check.TrueUpper!.Constant, Is.EqualTo(new BigInteger(trueUpper)));
        Assert.That(check.ExpectedUpper!.Constant, Is.EqualTo(new BigInteger(expectedUpper)));
        foreach (var other in new[] { false, true })
        {
            var observedWeighted = 3 * Execute(false, other) + Execute(true, other);
            Assert.That(new BigInteger(expectedUpper * 4), Is.GreaterThanOrEqualTo(new BigInteger(observedWeighted)));
        }
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), token)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.Expected!.UnknownReason, Is.Null);
        Assert.That(result.ResourceBound.Expected.UpperBound, Is.EqualTo(check.ExpectedUpper.ToDisplayString()));

        int Execute(bool selected, bool other)
        {
            var takeLarge = condition switch
            {
                "!selected" => !selected,
                "(bool)selected" => selected,
                "true" => true,
                "false" => false,
                "other" => other,
                _ => throw new ArgumentOutOfRangeException(nameof(condition))
            };
            return takeLarge ? new int[7].Length : new int[2].Length;
        }
    }

    [TestCase("n>=0")]
    [TestCase("selected")]
    public async Task InputRestrictionsDoNotSilentlyConditionTheDeclaredDistribution(string restriction)
    {
        ArgumentNullException.ThrowIfNull(restriction);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),1)] public static void Target(int n,[ResourceBooleanInput(1,3)] bool selected) { " +
            "Contract.Requires(" + restriction + "); if(selected) { for(int i=0;i<n;i++) {} } } }";
        var target = Read(CreateArtifact(source));
        var token = TestContext.CurrentContext.CancellationToken;
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), token)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.Expected!.UnknownReason, Is.EqualTo("ResourceInputModelSupportUnproved"));
        Assert.That(result.ResourceBound.Expected.UpperBound, Is.Null);
        Assert.That(result.ResourceBound.Expected.TrueWeight, Is.Null);
        Assert.That(result.ResourceBound.Expected.FalseWeight, Is.Null);
    }

    [TestCase((int)ResourceCostMetric.PeakAuxiliaryBytes)]
    [TestCase((int)ResourceCostMetric.SourceStackBytes)]
    public void UnsupportedExpectedMetricsHaveNoPartialProbabilityEvidence(int metric)
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0)] public static void Target([ResourceBooleanInput(1,1)] bool selected) {} }";
        var target = Read(CreateArtifact(source));
        var check = NativeResourceExpectedVerifier.Verify(target.ResourceProgram!, (ResourceCostMetric)metric, target.Entry.CallableId,
            target.Total!, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.EqualTo("ResourceExpectedMetricUnsupported"));
        Assert.That(check.Distribution, Is.Null);
        Assert.That(check.ExpectedUpper, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExpectedBytesRequireACompatibleLayoutWithoutPartialProbabilityEvidence(bool incompatible)
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,48)] public static void Target(" +
            "[ResourceBooleanInput(1,3)] bool selected) {if(selected) {var a=new long[3];} else {var a=new byte[1];}} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var check = NativeResourceExpectedVerifier.Verify(target.ResourceProgram!, ResourceCostMetric.AllocatedBytes,
            target.Entry.CallableId, target.Total!, incompatible ? new("CoreCLR", "8.0.32", "arm64", 8) : null,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.EqualTo("ResourceExpectedConditionalCostUnproved"));
        Assert.That(check.Distribution, Is.Null);
        Assert.That(check.FalseUpper, Is.Null);
        Assert.That(check.TrueUpper, Is.Null);
        Assert.That(check.ExpectedUpper, Is.Null);
        var projected = NativeResourceExpectedVerifier.Project(check);
        Assert.That(projected.UnknownReason, Is.EqualTo(check.UnknownReason));
        Assert.That(projected.UpperBound, Is.Null);
        Assert.That(projected.InputOrdinal, Is.Null);
        Assert.That(projected.TrueWeight, Is.Null);
        Assert.That(projected.FalseWeight, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExpectedCompositionBudgetPreservesWorstCaseCostsWithoutPartialProbabilityEvidence(bool exhausted)
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedElements,0)] public static void Target(int n," +
            "[ResourceBooleanInput(1,3)] bool selected) {} }";
        var target = Read(CreateArtifact(source));
        var no = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        var yes = no;
        var reductions = ResourceExpression.MaximumDepth - (exhausted ? 3 : 4);
        for (var index = 0; index < reductions; index++)
        {
            no = ResourceExpression.Difference(no, 1);
            yes = ResourceExpression.Difference(yes, 2);
        }
        var program = ResourceProgram.WithDistribution(target.ResourceProgram!.Distribution!,
            ResourceProgram.Array(no), ResourceProgram.Array(yes));
        var token = TestContext.CurrentContext.CancellationToken;
        program = ResourceProgramCodec.Decode(ResourceProgramCodec.Encode(program, token), token);
        var worst = NativeResourceProgramVerifier.Verify(program, target.Entry.CallableId, target.Total!, token);
        Assert.That(worst.Verified, Is.True, worst.UnknownReason);
        Assert.That(worst.Cost!.Elements.Evaluate(_ => 300, token), Is.EqualTo(new BigInteger(300 - reductions)));
        var check = NativeResourceExpectedVerifier.Verify(program, ResourceCostMetric.AllocatedElements,
            target.Entry.CallableId, target.Total!, null, token);
        if (exhausted)
        {
            Assert.That(check.UnknownReason, Is.EqualTo("ResourceExpectedProofBudgetExceeded"));
            Assert.That(check.Distribution, Is.Null);
            Assert.That(check.FalseUpper, Is.Null);
            Assert.That(check.TrueUpper, Is.Null);
            Assert.That(check.ExpectedUpper, Is.Null);
        }
        else
        {
            Assert.That(check.UnknownReason, Is.Null);
            var weighted = 3 * (300 - reductions) + 300 - 2 * reductions;
            Assert.That(check.ExpectedUpper!.Evaluate(_ => 300, token), Is.EqualTo(new BigInteger((weighted + 3) / 4)));
        }
    }

    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(1, 3)]
    [TestCase(1000000, 1000000)]
    public void PublicInputModelAndNativeWeightsAgreeAtSupportedBoundaries(int yes, int no)
    {
        var attribute = new SharpProof.Attributes.ResourceBooleanInputAttribute(yes, no);
        var model = new ResourceBooleanDistribution(0, "selected", attribute.TrueWeight, attribute.FalseWeight);
        Assert.That(model.TotalWeight, Is.EqualTo(yes + no));
        Assert.That(model.ExpectedUpper(ResourceExpression.Literal(2), ResourceExpression.Literal(7)).Constant,
            Is.EqualTo(new BigInteger((2L * no + 7L * yes + yes + no - 1) / (yes + no))));
    }

    [TestCase(-1, 1)]
    [TestCase(1, -1)]
    [TestCase(1000001, 1)]
    [TestCase(1, 1000001)]
    [TestCase(0, 0)]
    public void InvalidProbabilityWeightsAreRejectedByBothPublicAndNativeModels(int yes, int no)
    {
        Action attribute = () => { _ = new SharpProof.Attributes.ResourceBooleanInputAttribute(yes, no); };
        Action model = () => { _ = new ResourceBooleanDistribution(0, "selected", yes, no); };
        Assert.Throws<ArgumentOutOfRangeException>(attribute);
        Assert.Throws<ArgumentOutOfRangeException>(model);
    }

    [Test]
    public async Task MissingInputModelsAreExplicitlyUnknownWithoutAnExpectedNumber()
    {
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,0)] public static void Target() {} }"));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        var expected = result.ResourceBound!.Expected!;
        Assert.That(expected.UnknownReason, Is.EqualTo("ResourceInputModelNotDeclared"));
        Assert.That(expected.UpperBound, Is.Null);
        Assert.That(expected.InputOrdinal, Is.Null);
        Assert.That(expected.TrueWeight, Is.Null);
        Assert.That(expected.FalseWeight, Is.Null);
    }

    [Test]
    public async Task ZeroProbabilityDoesNotTurnAnExpectedLinearBoundIntoWorstCaseLinearTightness()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,long.MaxValue)] public static void Target(int n,[ResourceBooleanInput(0,1)] bool selected) { " +
            "if(selected) { for(int i=0;i<n;i++) { for(int j=0;j<n;j++) {} } } else { for(int k=0;k<n;k++) {} } } }";
        var target = Read(CreateArtifact(source));
        var token = TestContext.CurrentContext.CancellationToken;
        var check = NativeResourceExpectedVerifier.Verify(target.ResourceProgram!, ResourceCostMetric.LoopBodyEntries,
            target.Entry.CallableId, target.Total!, null, token);
        Assert.That(check.UnknownReason, Is.Null, target.ResourceUnknownReason);
        for (var n = 0; n < 32; n++)
        {
            Assert.That(check.ExpectedUpper!.Evaluate(_ => n, token), Is.EqualTo(new BigInteger(n)));
            Assert.That(check.TrueUpper!.Evaluate(_ => n, token), Is.EqualTo(new BigInteger(n * n + n)));
        }
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), token)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.Asymptotic!.UpperBound, Is.EqualTo("O(n^2)"));
        Assert.That(result.ResourceBound.Asymptotic.Lower!.TightBound, Is.Null);
        Assert.That(result.ResourceBound.Expected!.UpperBound, Is.EqualTo(check.FalseUpper!.ToDisplayString()));
    }

    [Test]
    public async Task ZeroWeightDoesNotHideAnUnsupportedCostArm()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0)] public static void Target([ResourceBooleanInput(0,1)] bool selected) { " +
            "if(selected) System.Console.WriteLine(); } }";
        var target = Read(CreateArtifact(source));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.Expected, Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProbabilityInputMustBeOwnedAndBoolean(bool foreign)
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0)] public static void Target(int n,[ResourceBooleanInput(1,1)] bool selected) {} }";
        var target = Read(CreateArtifact(source));
        var sourceProgram = target.ResourceProgram!;
        var invalid = ResourceProgram.WithDistribution(new(foreign ? 2 : 0, "selected", 1, 1), sourceProgram.Children[0], sourceProgram.Children[1]);
        var check = NativeResourceExpectedVerifier.Verify(invalid, ResourceCostMetric.LoopBodyEntries, target.Entry.CallableId,
            target.Total!, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.EqualTo("ResourceInputModelOwnershipInvalid"));
        Assert.That(check.ExpectedUpper, Is.Null);
        Assert.That(check.Distribution, Is.Null);
    }

    [TestCase("LoopBodyEntries", 1)]
    [TestCase("ArrayAllocations", 1)]
    [TestCase("AllocatedElements", 2)]
    [TestCase("AllocatedBytes", 32)]
    public async Task DeclaredBooleanModelsKeepWorstCaseSeparateFromExpectedUpper(string metric, int unit)
    {
        ArgumentNullException.ThrowIfNull(metric);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.{metric},0,nameof(n),{unit})] public static void Target(int n,[ResourceBooleanInput(1,3)] bool selected) {{ " +
            "if(selected) { for(int i=0;i<n;i++) { var a=new int[2]; } } } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var token = TestContext.CurrentContext.CancellationToken;
        var check = NativeResourceExpectedVerifier.Verify(target.ResourceProgram!, (ResourceCostMetric)Enum.Parse<WorkerResourceMetric>(metric),
            target.Entry.CallableId, target.Total!, new("CoreCLR", "8.0.32", "x64", 8), token);
        Assert.That(check.UnknownReason, Is.Null, target.ResourceUnknownReason);
        Assert.That(check.Distribution!.Name, Is.EqualTo("selected"));
        Assert.That(check.FalseUpper!.Constant, Is.EqualTo(BigInteger.Zero));
        for (var n = 0; n <= 32; n++)
        {
            Assert.That(check.TrueUpper!.Evaluate(_ => n, token), Is.EqualTo(new BigInteger(unit * n)));
            Assert.That(check.ExpectedUpper!.Evaluate(_ => n, token), Is.EqualTo(new BigInteger((unit * n + 3) / 4)));
        }
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), token)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.Expected!.UnknownReason, Is.Null);
        Assert.That(result.ResourceBound.Expected.UpperBound, Is.EqualTo(check.ExpectedUpper!.ToDisplayString()));
        Assert.That(result.ProofCore, Does.Contain("resource-input-model:" + WorkerResourceModels.Expected));
    }

    [TestCase(1, 3)]
    [TestCase(3, 1)]
    [TestCase(0, 1)]
    [TestCase(1, 0)]
    [TestCase(999999, 1)]
    public void ConditionalExpectationsAgreeWithIndependentWeightedExecutions(int yes, int no)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedElements,0,nameof(n),7)] public static void Target(int n," +
            $"[ResourceBooleanInput({yes},{no})] bool selected) {{ for(int i=0;i<n;i++) {{ if(selected) {{ var a=new int[7]; }} else {{ var b=new int[2]; }} }} }} }}";
        var target = Read(CreateArtifact(source));
        var token = TestContext.CurrentContext.CancellationToken;
        var check = NativeResourceExpectedVerifier.Verify(target.ResourceProgram!, ResourceCostMetric.AllocatedElements,
            target.Entry.CallableId, target.Total!, null, token);
        Assert.That(check.UnknownReason, Is.Null, target.ResourceUnknownReason);
        for (var n = -3; n <= 64; n++)
        {
            var falseElements = Execute(false, n);
            var trueElements = Execute(true, n);
            var weighted = new BigInteger(no) * falseElements + new BigInteger(yes) * trueElements;
            var expectedCeiling = (weighted + yes + no - 1) / (yes + no);
            Assert.That(check.ExpectedUpper!.Evaluate(_ => n, token), Is.EqualTo(expectedCeiling));
        }

        static int Execute(bool selected, int n)
        {
            var elements = 0;
            for (var i = 0; i < n; i++)
            { elements += selected ? new int[7].Length : new int[2].Length; }
            return elements;
        }
    }

    [TestCase("[ResourceBooleanInput(0,0)] bool selected", "")]
    [TestCase("[ResourceBooleanInput(-1,1)] bool selected", "")]
    [TestCase("[ResourceBooleanInput(1000001,1)] bool selected", "")]
    [TestCase("[ResourceBooleanInput(1,1)] int selected", "")]
    [TestCase("[ResourceBooleanInput(1,1)] bool selected,[ResourceBooleanInput(1,1)] bool second", "")]
    [TestCase("[ResourceBooleanInput(1,1)] bool selected", "selected=!selected;")]
    public async Task InvalidInputModelsAndMutableInputsPublishNoPartialResourceProof(string parameters, string body)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,0)] " +
            "public static void Target(" + parameters + ") { " + body + " } }";
        var target = Read(CreateArtifact(source));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void ProbabilityCertificatesRejectMalformedAndNestedModels(int mutation)
    {
        var model = new ResourceBooleanDistribution(0, "selected", 1, 3);
        var program = ResourceProgram.WithDistribution(model, ResourceProgram.Zero, ResourceProgram.Array(ResourceExpression.One));
        var token = TestContext.CurrentContext.CancellationToken;
        var json = JsonNode.Parse(ResourceProgramCodec.Encode(program, token))!;
        var root = json["program"]!;
        switch (mutation)
        {
            case 0:
                root.AsObject().Remove("distribution");
                break;
            case 1:
                root["distribution"]!["trueWeight"] = -1;
                break;
            case 2:
                root["distribution"]!["falseWeight"] = 0;
                root["distribution"]!["trueWeight"] = 0;
                break;
            case 3:
                root["distribution"]!["ordinal"] = -1;
                break;
            default:
                root["children"]![0] = JsonNode.Parse(ResourceProgramCodec.Encode(program, token))!["program"]!.DeepClone();
                break;
        }
        Action decode = () => ResourceProgramCodec.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), token);
        Assert.Throws<InvalidDataException>(decode);
    }
}
