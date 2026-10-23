using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase(16)]
    [TestCase(17)]
    public async Task AccountingEvidenceLimitRejectsTheWholeCompiledClaim(int loopCount)
    {
        var loops = string.Concat(Enumerable.Range(0, loopCount).Select(index =>
        {
            var suffix = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return $"int credit{suffix}=0; for(int i{suffix}=0;i{suffix}<n;i{suffix}++) {{ " +
                $"credit{suffix}++; while(credit{suffix}>0) {{credit{suffix}--;}} }}";
        }));
        // Each independent loop enters its outer body at most max(0,n) times
        // and drains that many credits. The metadata call adds no entries.
        var coefficient = (2 * loopCount).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),{coefficient})] public static void Target(int n) {{ " +
            "int value=System.Math.Max(n,0); " + loops + " } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        Assert.That(target.ResourceProgram, Is.Not.Null, target.ResourceUnknownReason);
        var pending = new Stack<ResourceProgram>();
        pending.Push(target.ResourceProgram!);
        var credits = 0;
        var metadataCalls = 0;
        while (pending.Count != 0)
        {
            var region = pending.Pop();
            if (region.Kind == ResourceProgramKind.CreditLoop)
            { credits++; }
            if (region.Kind == ResourceProgramKind.MetadataCall)
            { metadataCalls++; }
            foreach (var child in region.Children)
            { pending.Push(child); }
        }
        Assert.That(credits, Is.EqualTo(loopCount));
        Assert.That(metadataCalls, Is.EqualTo(1));
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var metadata = new NativeResourceMetadataBinding(target);
        var costs = NativeResourceProgramVerifier.Verify(target.ResourceProgram!, target.Entry.CallableId,
            target.Total!, metadata.Bind, cancellation);
        Assert.That(costs.Verified, Is.True, costs.UnknownReason);
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), cancellation)).Single();
        if (loopCount == 16)
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
            Assert.That(result.ResourceBound!.Accounting, Has.Length.EqualTo(16));
            Assert.That(result.ResourceBound.Accounting.All(static record => record is not null &&
                record.Model == NativeResourceAccountingVerifier.ModelId && record.InitialCredit == "0" &&
                record.DepositPerIteration == "1" && record.CounterWidth == 32), Is.True);
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Not.Null.And.Not.Empty);
            Assert.That(result.ProofCore, Does.Contain("resource-accounting:" + NativeResourceAccountingVerifier.ModelId));
            var dependency = result.ResourceBound.MetadataAssumptions.Single();
            Assert.That(dependency.WitnessIdentifier, Is.EqualTo("bcl.math.max.int32-int32"));
            Assert.That(result.Assumptions.Single(assumption => assumption is null ? throw new ArgumentNullException(nameof(assumption)) :
                assumption.Id == dependency.AssumptionId).Used, Is.True);
        }
        else
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(result.ResourceBound!.UnknownReason, Is.EqualTo("ResourceProofBudgetExceeded"));
            Assert.That(result.ResourceBound.Accounting, Is.Empty);
            Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
            Assert.That(result.ResourceBound.RuntimePremises, Is.Null);
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
            Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
            Assert.That(result.ResourceBound.Asymptotic, Is.Null);
            Assert.That(result.ResourceBound.Expected, Is.Null);
            Assert.That(result.ProofCore, Is.Empty);
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
        }
    }

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
    [TestCase("credit++; while(credit>0) {credit--;}", "int credit=-1;")]
    [TestCase("credit++; while(credit>0) {credit--;}", "int credit=n;")]
    [TestCase("credit++; while(credit>0) {credit--;}", "double credit=0;")]
    [TestCase("credit++; while(credit>0) {}")]
    public async Task UnsupportedCreditTransitionsReturnUnknownWithoutPartialAccounting(string body, string initialize = "int credit=0;")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(initialize);
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),10)] public static void Target(int n,bool flush) { " +
            initialize + " for(int i=0;i<n;i++) { " + body + " } } }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.Accounting, Is.Empty);
        Assert.That(result.ResourceBound.Asymptotic, Is.Null);
    }
}
