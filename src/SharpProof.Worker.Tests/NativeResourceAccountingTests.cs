using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase("LoopBodyEntries", 0, 2, "int credit=0;", "credit++;", "", "", false)]
    [TestCase("LoopBodyEntries", 0, 2, "int credit=0;", "credit++;", "", "", true)]
    [TestCase("ArrayAllocations", 0, 1, "int credit=0;", "credit++;", "", "var a=new int[2];", true)]
    [TestCase("AllocatedElements", 0, 2, "int credit=0;", "credit++;", "", "var a=new int[2];", true)]
    [TestCase("AllocatedElements", 0, 5, "int credit=0;", "credit++;", "var p=new int[3];", "var a=new int[2];", true)]
    [TestCase("AllocatedBytes", 0, 32, "int credit=0;", "credit++;", "", "var a=new int[2];", true)]
    [TestCase("LoopBodyEntries", 3, 3, "long credit=3;", "credit+=2;", "", "", true)]
    public async Task CreditPotentialBoundsComposeOuterAndDrainWork(string metric, int constant, int coefficient,
        string initialize, string deposit, string prefix, string spending, bool optional)
    {
        ArgumentNullException.ThrowIfNull(metric);
        ArgumentNullException.ThrowIfNull(initialize);
        ArgumentNullException.ThrowIfNull(deposit);
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(spending);
        var drain = "while(credit>0) { " + spending + " credit--; }";
        if (optional)
        { drain = "if(flush) { " + drain + " }"; }
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.{metric},{constant},nameof(n),{coefficient})] public static void Target(int n,bool flush) {{ " +
            initialize + " for(int i=0;i<n;i++) { " + deposit + prefix + drain + " } } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason ?? target.ResourceUnknownReason);
        Assert.That(result.ResourceBound!.Accounting, Has.Length.EqualTo(1));
        var evidence = result.ResourceBound.Accounting[0];
        Assert.That(evidence.Model, Is.EqualTo(NativeResourceAccountingVerifier.ModelId));
        Assert.That(evidence.InitialCredit, Is.EqualTo(constant == 3 ? "3" : "0"));
        Assert.That(evidence.DepositPerIteration, Is.EqualTo(constant == 3 ? "2" : "1"));
        Assert.That(evidence.MaximumSpend, Does.Contain("n"));
        Assert.That(result.ProofCore, Does.Contain("resource-accounting:" + NativeResourceAccountingVerifier.ModelId));
    }

    [TestCase("credit++; while(credit>=0) {credit--;}")]
    [TestCase("credit++; while(credit>0) {credit-=2;}")]
    [TestCase("credit++; credit+=2; while(credit>0) {credit--;}")]
    [TestCase("credit+=2; while(credit>0) {credit--;}")]
    [TestCase("credit++; while(credit>0) { if(flush) break; credit--; }")]
    public async Task UnsupportedCreditTransitionsReturnUnknownWithoutPartialAccounting(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),10)] public static void Target(int n,bool flush) { " +
            "int credit=0; for(int i=0;i<n;i++) { " + body + " } } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
    }
}
