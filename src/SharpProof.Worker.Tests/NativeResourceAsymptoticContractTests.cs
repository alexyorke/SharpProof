using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase("for(int i=0;i<n;i++) { for(int j=0;j<n;j++) { var a=new int[1]; } }", 20, 16, 16)]
    [TestCase("for(int i=0;i<n;i++) { for(int j=0;j<i;j++) { var a=new int[1]; } }", 4, 0, 0)]
    [TestCase("for(int i=0;i<n;i++) { Walk(i); }", 4, 0, 0)]
    [TestCase("for(int i=0;i<n;i++) { var a=new int[i]; }", 4, 4, 0)]
    [TestCase("checked { for(int i=0;i<n;i+=2) { var a=new int[1]; } }", 2, 2, 2)]
    [TestCase("for(int i=n;i>0;i/=2) { var a=new int[i]; }", 3, 3, 0)]
    [TestCase("if(n>0) { var a=new int[3]; } else { var b=new int[2]; }", 0, 1, 2)]
    [TestCase("if(n>0) return; for(int i=0;i<n;i++) {}", 0, 0, 0)]
    [TestCase("for(int i=0;i<n;i++) { return; }", 0, 0, 0)]
    [TestCase("bool value=n>0 || Allocate();", 0, 0, 0)]
    [TestCase("bool value=n>0 && Allocate();", 0, 0, 0)]
    public void NativeLowerCostsRespectCounterEnvelopesAndActualControlFlow(string body, int loops, int arrays, int elements)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,long.MaxValue)] public static void Target(int n) { " + body + " } " +
            "private static void Walk(int count) { for(int j=0;j<count;j++) { var a=new int[1]; } } " +
            "private static bool Allocate() { var a=new int[1]; return true; } }";
        var target = Read(CreateArtifact(source));
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var check = NativeResourceProgramVerifier.Verify(target.ResourceProgram!, target.Entry.CallableId, target.Total!, cancellation);
        Assert.That(check.Verified, Is.True, check.UnknownReason);
        Assert.That(check.LowerCost!.LoopEntries.Evaluate(static _ => 4, cancellation).ToString(System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(loops.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(check.LowerCost.Arrays.Evaluate(static _ => 4, cancellation).ToString(System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(arrays.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(check.LowerCost.Elements.Evaluate(static _ => 4, cancellation).ToString(System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(elements.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [TestCase("LoopBodyEntries", "for(int i=0;i<n;i++) {}", "O(n)", 1, 0)]
    [TestCase("LoopBodyEntries", "for(int i=0;i<n;i++) { for(int j=0;j<n;j++) {} }", "O(n^2)", 2, 0)]
    [TestCase("LoopBodyEntries", "checked { for(int i=1;i<n;i*=2) {} }", "O(log(n))", 0, 1)]
    [TestCase("ArrayAllocations", "for(int i=0;i<n;i++) { var a=new int[1]; }", "O(n)", 1, 0)]
    [TestCase("AllocatedElements", "var a=new int[n];", "O(n)", 1, 0)]
    public async Task NativeGrowthEvidenceComesFromTheOwnedInferredUpperFormula(string metric, string body, string order, int polynomial, int logarithmic)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(order);
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric." + metric + ",long.MaxValue)]", body)));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        var growth = result.ResourceBound!.Asymptotic!;
        Assert.That(growth.Model, Is.EqualTo(ResourceAsymptoticGrowth.ModelId));
        Assert.That(WorkerResourceModels.MaximumAsymptoticDegree, Is.EqualTo(ResourceExpression.MaximumNodes));
        Assert.That(growth.UpperBound, Is.EqualTo(order));
        Assert.That(growth.PolynomialDegree, Is.EqualTo(polynomial));
        Assert.That(growth.LogarithmicDegree, Is.EqualTo(logarithmic));
        Assert.That(growth.IsZero, Is.False);
        Assert.That(growth.InputOrdinal, Is.Zero);
        Assert.That(growth.InputKind, Is.EqualTo("NonNegativeScalar"));
        Assert.That(growth.InputName, Is.EqualTo("n"));
        Assert.That(growth.UnknownReason, Is.Null);
        Assert.That(result.ProofCore, Does.Contain("resource-asymptotic-model:" + WorkerResourceModels.Asymptotic));
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
    }

    [Test]
    public async Task UnsupportedSingleInputGrowthDoesNotInvalidateANumericNativeProof()
    {
        const string source = "using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,long.MaxValue)] public static void Target(int n,int m) { for(int i=0;i<n;i++) {} for(int j=0;j<m;j++) {} } }";
        var target = Read(CreateArtifact(source));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        var growth = result.ResourceBound!.Asymptotic!;
        Assert.That(growth.UnknownReason, Is.EqualTo("AsymptoticMultipleInputs"));
        Assert.That(growth.UpperBound, Is.Null);
        Assert.That(growth.PolynomialDegree, Is.Null);
        Assert.That(growth.LogarithmicDegree, Is.Null);
        Assert.That(growth.IsZero, Is.Null);
        Assert.That(growth.InputOrdinal, Is.Null);
        Assert.That(growth.InputKind, Is.Null);
        Assert.That(growth.InputName, Is.Null);
    }

    [TestCase(false, WorkerClaimOutcome.Unknown)]
    [TestCase(true, WorkerClaimOutcome.Refuted)]
    public async Task UndecidedAndRefutedNumericClaimsPublishNoAsymptoticProof(bool violation, WorkerClaimOutcome expected)
    {
        var target = Read(CreateArtifact(Source("[ResourceUpperBound(ResourceMetric.AllocatedElements,0)]",
            violation ? "var a=new int[1];" : "System.GC.Collect();")));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.ResourceBound!.Asymptotic, Is.Null);
    }
    [TestCase("public static void Target(string text) { for(int i=0;i<text.Length;i++) {} }", "text.Length", "StringLength", "text")]
    [TestCase("public static void Target(string text) { Walk(text); } private static void Walk(string value) { for(int i=0;i<value.Length;i++) {} }", "text.Length", "StringLength", "text")]
    [TestCase("public static void Target(int n) { Walk(new string('x',n)); } private static void Walk(string value) { for(int i=0;i<value.Length;i++) {} }", "n", "NonNegativeScalar", "n")]
    [TestCase("public static Box Target(string text) { return new Box(text); } public sealed class Box { public Box(string value) { for(int i=0;i<value.Length;i++) {} } }", "text.Length", "StringLength", "text")]
    public async Task StringLengthContractsAndCallSubstitutionsKeepTheOwnedInput(string members, string size, string inputKind, string inputName)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(size);
        ArgumentNullException.ThrowIfNull(inputKind);
        ArgumentNullException.ThrowIfNull(inputName);
        var source = "using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,0," +
            System.Text.Json.JsonSerializer.Serialize(size) + ",1)] " + members + " }";
        var target = Read(CreateArtifact(source));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        var growth = result.ResourceBound!.Asymptotic!;
        Assert.That(growth.UpperBound, Is.EqualTo("O(n)"));
        Assert.That(growth.InputOrdinal, Is.Zero);
        Assert.That(growth.InputKind, Is.EqualTo(inputKind));
        Assert.That(growth.InputName, Is.EqualTo(inputName));
    }

    [Test]
    public async Task RepeatedStringCallsDoNotReuseThePreviousArgumentSize()
    {
        const string source = """using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,5)] public static void Target() { Walk("abcd"); Walk("x"); } private static void Walk(string value) { for(int i=0;i<value.Length;i++) {} } }""";
        var target = Read(CreateArtifact(source));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("5"));
        Assert.That(result.ResourceBound.Asymptotic!.UpperBound, Is.EqualTo("O(1)"));
        Assert.That(result.ResourceBound.Asymptotic.InputOrdinal, Is.Null);
    }
    [TestCase("public static void Target(Owner text) { for(int i=0;i<text.Length;i++) {} } public sealed class Owner { public int Length { get { var a=new int[1]; return 10; } } }")]
    [TestCase("public static void Target(string text) { var copy=text; for(int i=0;i<copy.Length;i++) {} }")]
    [TestCase("public static void Target(int n,string text) { if(n<=0) return; for(int i=0;i<text.Length;i++) {} Target(n-1,new string('x',n)); }")]
    public async Task UntrustedLengthSourcesAliasesAndChangingRecursiveStringsRemainUnknown(string members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,long.MaxValue)] " + members + " }"));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
    }
}
