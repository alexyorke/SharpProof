using System.Collections.Immutable;
using System.Numerics;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ZeroCostAlternativesPreserveSymbolicUpperGrowthWithoutInventingTightness(bool loopWhenTrue)
    {
        var branches = loopWhenTrue
            ? "if(selected) {for(int i=0;i<n;i++) {}} else {}"
            : "if(selected) {} else {for(int i=0;i<n;i++) {}}";
        var source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0,nameof(n),1)] public static void Target(int n,bool selected) {" +
            branches + "} }";
        var target = Read(CreateArtifact(source));
        Assert.That(target.Total, Is.Not.Null);
        Assert.That(target.ResourceProgram, Is.Not.Null);
        var owner = target.Total!;
        var token = TestContext.CurrentContext.CancellationToken;
        var check = NativeResourceProgramVerifier.Verify(target.ResourceProgram!, target.Entry.CallableId, owner, token);
        Assert.That(check.Verified, Is.True, check.UnknownReason);
        Assert.That(check.LowerDomainVerified, Is.True, check.LowerDomainUnknownReason);
        var upper = check.Cost!.LoopEntries;
        var lower = check.LowerCost!.LoopEntries;
        Assert.That(upper.Kind, Is.EqualTo(ResourceExpressionKind.Maximum));
        Assert.That(upper.Operands[loopWhenTrue ? 1 : 0].Constant, Is.EqualTo(BigInteger.Zero));
        Assert.That(upper.Operands[loopWhenTrue ? 0 : 1].Input?.Ordinal, Is.Zero);
        Assert.That(lower.Constant, Is.EqualTo(BigInteger.Zero));

        foreach (var size in new[] { -2, 0, 1, 7, 17 })
        {
            var expectedUpper = Math.Max(0, size);
            Assert.That(upper.Evaluate(_ => size, token), Is.EqualTo(new BigInteger(expectedUpper)));
            Assert.That(lower.Evaluate(_ => size, token), Is.EqualTo(BigInteger.Zero));
            foreach (var selected in new[] { false, true })
            {
                var observed = 0;
                var factory = owner.Program.Factory;
                var inputs = ImmutableDictionary<IrVarId, IrValue>.Empty
                    .Add(owner.Parameters[0].Entry, factory.CreateIntegerValue(size))
                    .Add(owner.Parameters[1].Entry, factory.CreateBooleanValue(selected));
                var options = new IrProgramReplayOptions(static _ => null)
                {
                    SourceLoopEntryObserver = (instruction, approximation) =>
                    {
                        ArgumentNullException.ThrowIfNull(instruction);
                        Assert.That(approximation, Is.False);
                        observed++;
                    }
                };
                var execution = new IrProgramInterpreter(factory).Execute(owner.Program, inputs, 1000, options, token);
                Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(execution.ConsumedApproximation, Is.False);
                Assert.That(observed, Is.EqualTo(selected == loopWhenTrue ? expectedUpper : 0));
            }
        }

        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), token)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo(upper.ToDisplayString()));
        Assert.That(result.ResourceBound.ObservedLowerBound, Is.Null);
        var growth = result.ResourceBound.Asymptotic!;
        Assert.That(growth.UnknownReason, Is.Null);
        Assert.That(growth.Model, Is.EqualTo(ResourceAsymptoticGrowth.ModelId));
        Assert.That(growth.UpperBound, Is.EqualTo("O(n)"));
        Assert.That(growth.PolynomialDegree, Is.EqualTo(1));
        Assert.That(growth.LogarithmicDegree, Is.Zero);
        Assert.That(growth.IsZero, Is.False);
        Assert.That(growth.InputOrdinal, Is.Zero);
        Assert.That(growth.InputKind, Is.EqualTo(nameof(ResourceInputKind.NonNegativeScalar)));
        Assert.That(growth.InputName, Is.EqualTo("n"));
        Assert.That(growth.Lower!.UnknownReason, Is.EqualTo("ResourcePositiveLowerBoundUnproved"));
        Assert.That(growth.Lower.InferredBound, Is.Null);
        Assert.That(growth.Lower.LowerBound, Is.Null);
        Assert.That(growth.Lower.TightBound, Is.Null);
        Assert.That(growth.Lower.Growth, Is.Null);
        Assert.That(result.ProofCore, Does.Contain("resource-asymptotic-model:" + WorkerResourceModels.Asymptotic));
    }
}
