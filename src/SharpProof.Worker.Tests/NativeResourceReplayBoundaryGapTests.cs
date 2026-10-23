using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task ZeroWeightCostArmsStillRequireCompleteWorstCaseEvidence(bool zeroTrue, bool unsupported)
    {
        var yes = zeroTrue ? 0 : 1;
        var no = zeroTrue ? 1 : 0;
        var trueBody = "var value=new int[7];";
        var falseBody = "var value=new int[2];";
        if (unsupported)
        {
            if (zeroTrue)
            { trueBody = "System.Console.WriteLine(0);" + trueBody; }
            else
            { falseBody = "System.Console.WriteLine(0);" + falseBody; }
        }
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedElements,7)] public static void Target(" +
            $"[ResourceBooleanInput({yes},{no})] bool selected) " +
            "{if(selected) {" + trueBody + "} else {" + falseBody + "}} }";
        var target = Read(CreateArtifact(source));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(unsupported ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
        Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
        Assert.That(result.EffectWitness, Is.Null);
        if (unsupported)
        {
            Assert.That(result.ResourceBound.UnknownReason, Is.Not.Null.And.Not.Empty);
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.Asymptotic, Is.Null);
            Assert.That(result.ResourceBound.Expected, Is.Null);
        }
        else
        {
            Assert.That(result.ResourceBound.UnknownReason, Is.Null);
            Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo("7"));
            var expected = result.ResourceBound.Expected!;
            Assert.That(expected.UnknownReason, Is.Null);
            Assert.That(expected.TrueWeight, Is.EqualTo(yes));
            Assert.That(expected.FalseWeight, Is.EqualTo(no));
            Assert.That(expected.TrueUpperBound, Is.EqualTo("7"));
            Assert.That(expected.FalseUpperBound, Is.EqualTo("2"));
            Assert.That(expected.UpperBound, Is.EqualTo(zeroTrue ? "2" : "7"));
        }
    }
}
