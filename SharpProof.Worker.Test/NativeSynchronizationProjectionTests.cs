using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;
namespace SharpProof.Worker.Test;
[TestFixture]
public sealed class NativeSynchronizationProjectionTests
{
    [TestCase("reachable", false, "Refuted")]
    [TestCase("unreachable", false, "Proven")]
    [TestCase("reachable", true, "Unknown")]
    [TestCase("approximation", false, "Unknown")]
    [TestCase("loop", false, "Refuted")]
    [TestCase("loop", true, "Unknown")]
    public async Task SyntheticPrefix(string shape, bool permitted, string expected)
    {
        Install();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var site = factory.CreateOperation("original-lock");
        var receiver = factory.Null(factory.ObjectType);
        if (shape == "unreachable")
        {
            builder.Return(entry, site);
            var dead = builder.CreateBlock("dead");
            builder.Lock(dead, site, receiver);
            builder.Return(dead, site);
        }
        else if (shape == "approximation")
        {
            var flag = factory.CreateVariable("approx", factory.BooleanType);
            builder.Havoc(entry, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, flag);
            var locked = builder.CreateBlock("locked");
            var done = builder.CreateBlock("done");
            builder.Branch(entry, site, factory.Variable(flag), done, locked);
            builder.Return(done, site);
            builder.Lock(locked, site, receiver);
            builder.Return(locked, site);
        }
        else if (shape == "loop")
        {
            var loop = builder.CreateBlock("loop");
            builder.Goto(entry, site, loop);
            builder.Lock(loop, site, receiver);
            builder.Goto(loop, site, loop);
        }
        else
        { builder.Lock(entry, site, receiver); builder.Return(entry, site); }
        var candidate = new PassiveCallableCandidate("synthetic", builder.Build(), [], null, [], []);
        var result = await NativeEffectSiteVerifier.VerifySynchronizationProjectionAsync(candidate,
            permitted ? WorkerEffectCapabilitySet.Synchronization : WorkerEffectCapabilitySet.None, new WorkerBudgets());
        Check(result, expected);
        if (expected == "Refuted")
        { Assert.That(result.LockWitness, Is.EqualTo(site)); }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task OwnedSourceHelper(bool loop)
    {
        Install();
        var fixture = LoadSynchronizationFixture(loop ? "synchronization-projection-helper-loop" : "synchronization-projection-helper");
        var source = fixture.Source;
        var preparation = Prepare(source);
        var claim = preparation.EffectClaims.Single();
        var result = await NativeEffectSiteVerifier.VerifySynchronizationShadowAsync(preparation, claim.ClaimId, WorkerEffectCapabilitySet.None, new WorkerBudgets());
        Check(result, "Refuted");
        GoldenTest.Compare(fixture, "Refuted");
        Assert.That(preparation.Total!.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrLockInstruction>().Any(instruction => instruction.Operation == result.LockWitness), Is.True);
        Assert.That(preparation.Total.Program.Factory.GetOperationInfo(result.LockWitness!.Value).SourceSpan, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyAllocationsAsync()).Outcome, Is.Not.TypeOf<ProvenOutcome>());
    }
    [TestCase("baseline", "Proven")]
    [TestCase("wrong-id", "Unknown")]
    [TestCase("wrong-mask", "Unknown")]
    [TestCase("missing-validation", "Unknown")]
    [TestCase("wrong-contract", "Unknown")]
    [TestCase("invalid-constraint", "Unknown")]
    [TestCase("changed-mask", "Unknown")]
    [TestCase("entry-incomplete", "Unknown")]
    [TestCase("other-category", "Unknown")]
    public async Task ClaimAdmission(string mutation, string expected)
    {
        Install();
        var fixture = LoadSynchronizationFixture("synchronization-projection-baseline");
        var source = mutation == "other-category" ? fixture.Source.Replace("SharpProofCapability.None", "SharpProofCapability.IO", StringComparison.Ordinal) : fixture.Source;
        var preparation = Prepare(source);
        var claim = preparation.EffectClaims.Single();
        var claimId = claim.ClaimId;
        var expectedMask = mutation == "other-category" ? WorkerEffectCapabilitySet.IO : WorkerEffectCapabilitySet.None;
        switch (mutation)
        {
            case "wrong-id":
                claimId = "foreign";
                break;
            case "wrong-mask":
                expectedMask = WorkerEffectCapabilitySet.Synchronization;
                break;
            case "missing-validation":
                preparation = preparation with { Total = preparation.Total! with { ValidEffectClaimIds = [] } };
                break;
            case "wrong-contract":
                claim.ContractKind = WorkerEffectContractKind.ZeroAllocations;
                break;
            case "invalid-constraint":
                claim.Constraint.AllowedEffects = WorkerEffectSet.Allocates;
                break;
            case "changed-mask":
                claim.Constraint.AllowedCapabilities = WorkerEffectCapabilitySet.Synchronization;
                CompilerEffectClaimArtifactCodec.Seal(claim);
                break;
            case "entry-incomplete":
                preparation = preparation with { Total = preparation.Total! with { EffectsCompleteAtEntry = false } };
                break;
        }
        var result = await NativeEffectSiteVerifier.VerifySynchronizationShadowAsync(preparation, claimId, expectedMask, new WorkerBudgets());
        Check(result, expected);
        if (mutation == "baseline")
        { GoldenTest.Compare(fixture, "Proven"); }
    }
    [Test]
    public async Task ExpressionDepthPreflightRejectsDeepRequires()
    {
        Install();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(32, true);
        var input = factory.CreateVariable("entry:0", type);
        var current = factory.CreateVariable("current:0", type);
        var old = factory.CreateVariable("old:0", type);
        IrTerm condition = factory.Binary(IrBinaryOperator.GreaterThan, factory.Variable(input), factory.Integer(type, 0));
        for (var index = 0; index < 8; index++)
        {
            var comparison = factory.Binary(IrBinaryOperator.GreaterThan, factory.Variable(input), factory.Integer(type, index + 1));
            condition = factory.Binary(IrBinaryOperator.AndAlso, condition, comparison);
        }
        Assert.That(IrTermAnalysis.GetDepth(condition), Is.GreaterThan(1));
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var site = factory.CreateOperation("deep-requires");
        builder.Return(entry, site);
        var candidate = new PassiveCallableCandidate("depth", builder.Build(), [new(input, current, old)], null,
            [new(condition, factory.Boolean(true), site)], []);
        var result = await NativeEffectSiteVerifier.VerifySynchronizationProjectionAsync(candidate,
            WorkerEffectCapabilitySet.None, new WorkerBudgets { MaximumExpressionDepth = 1 });
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
        Assert.That(result.Outcome, Is.Null);
    }

    private static GoldenCase LoadSynchronizationFixture(string name)
    { return GoldenTest.Load("worker", name); }

    private static CompilerCallablePreparation Prepare(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single();
    }
    private static void Install() { SharpProof.Host.ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly); }
    private static void Check(PassiveCallableCheckResult result, string expected)
    {
        var actual = result.Outcome is ProvenOutcome ? "Proven" : result.Outcome is RefutedOutcome ? "Refuted" : "Unknown";
        TestContext.Out.WriteLine($"expected={expected} actual={actual} reason={result.Reason} lock={result.LockWitness}");
        Assert.That(actual, Is.EqualTo(expected));
        if (expected != "Refuted")
        { Assert.That(result.LockWitness, Is.Null); }
    }
}
