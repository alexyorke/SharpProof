using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkNaturalConstructionBoundCoverageTests
{
    [Test]
    public void RealFlatMutationsRespectTheUnchangedFinalInstructionBound()
    {
        for (var ordinal = 0; ordinal < 2; ordinal++)
        {
            var increments = ordinal == 0 ? 8 : 1400;
            using var subject = TypedProgramSubject.Create("int Target(int value) { " + string.Concat(Enumerable.Repeat("value++; ", increments)) + "return value; }");
            Assert.That(subject.Invoke([1]), Is.EqualTo(1 + increments));
            Assert.That(subject.Graph.Blocks.Length, Is.LessThan(RoslynTotalProgramLowerer.MaximumRegionSteps));
            var lowering = subject.Lower();
            Assert.That(lowering.ConstructionLimitExceeded, Is.EqualTo(ordinal != 0));
            Assert.That(lowering.IsExact, Is.EqualTo(ordinal == 0));
            if (ordinal != 0)
            { Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedControlFlow)); }
            else
            {
                var replay = subject.Execute(lowering, [1]);
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(9)));
            }
        }
    }

}
