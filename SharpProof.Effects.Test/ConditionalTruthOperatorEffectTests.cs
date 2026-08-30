namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ConditionalTruthOperatorEffectTests
{
    [TestCase("And")]
    [TestCase("Or")]
    public void TruthOperatorEffectsPrecedeTheRightOperandAndReachCatches(
        string methodName)
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class Cell {
                public int Value;
            }

            public sealed class Gate {
                public int Value;

                public static bool operator false(Gate value) {
                    value.Value++;
                    throw new InvalidOperationException();
                }

                public static bool operator true(Gate value) {
                    value.Value++;
                    throw new ApplicationException();
                }

                public static Gate operator &(Gate left, Gate right) => left;

                public static Gate operator |(Gate left, Gate right) => left;
            }

            public static class Sample {
                public static void And(
                    Gate truth,
                    Cell right,
                    Cell handled,
                    Cell suffix) {
                    try {
                        _ = truth && EvaluateRight(truth, right);
                    }
                    catch (InvalidOperationException) {
                        handled.Value++;
                    }
                    suffix.Value++;
                }

                public static void Or(
                    Gate truth,
                    Cell right,
                    Cell handled,
                    Cell suffix) {
                    try {
                        _ = truth || EvaluateRight(truth, right);
                    }
                    catch (ApplicationException) {
                        handled.Value++;
                    }
                    suffix.Value++;
                }

                private static Gate EvaluateRight(Gate value, Cell state) {
                    state.Value++;
                    return value;
                }
            }
            """);
        var method = EffectTestHost.RequireMethod(
            compilation,
            "Sample",
            methodName);
        var result = new EffectAnalysisSession(compilation).Analyze(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Writes.IsUnknown, Is.False);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True,
                "the truth operator writes its operand");
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(1)),
                Is.False,
                "the throwing truth operator prevents right evaluation");
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(2)),
                Is.True,
                "the matching truth-operator catch is reachable");
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(3)),
                Is.True,
                "execution continues after the matching catch");
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }
}
