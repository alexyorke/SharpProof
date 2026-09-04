namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ConditionalTruthOperatorEffectTests
{
    private static readonly Compilation FixedTruthCompilation =
        CreateFixedTruthCompilation();

    private static readonly Compilation TruthOperatorCompilation =
        CreateTruthOperatorCompilation();

    [TestCase("AndRightNeverCompletes", false)]
    [TestCase("OrRightNeverCompletes", false)]
    [TestCase("AndOperatorNeverCompletes", false)]
    [TestCase("OrOperatorNeverCompletes", false)]
    public void FixedTruthResultControlsConditionalCompletion(
        string methodName,
        bool suffixIsReachable)
    {
        var compilation = FixedTruthCompilation;
        var method = EffectTestHost.SampleMethod(compilation, methodName);

        var result = new EffectAnalysisSession(compilation).Analyze(method);

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(1)),
            Is.EqualTo(suffixIsReachable));
    }

    private static CSharpCompilation CreateFixedTruthCompilation()
    {
        return EffectTestHost.CreateCompilation(
            """
            public sealed class Cell {
                public int Value;
            }

            public readonly struct RequiredGate {
                public static bool operator false(RequiredGate value) {
                    _ = value;
                    return false;
                }
                public static bool operator true(RequiredGate value) {
                    _ = value;
                    return false;
                }
                public static RequiredGate operator &(
                    RequiredGate left,
                    RequiredGate right) => left;
                public static RequiredGate operator |(
                    RequiredGate left,
                    RequiredGate right) => left;
            }

            public readonly struct NonReturningGate {
                public static bool operator false(NonReturningGate value) =>
                    false;
                public static bool operator true(NonReturningGate value) =>
                    false;
                public static NonReturningGate operator &(
                    NonReturningGate left,
                    NonReturningGate right) {
                    while (true) { }
                }
                public static NonReturningGate operator |(
                    NonReturningGate left,
                    NonReturningGate right) {
                    while (true) { }
                }
            }

            public static class Sample {
                public static void AndRightNeverCompletes(
                    RequiredGate left,
                    Cell suffix) {
                    _ = left && Spin(left);
                    suffix.Value++;
                }
                public static void OrRightNeverCompletes(
                    RequiredGate left,
                    Cell suffix) {
                    _ = left || Spin(left);
                    suffix.Value++;
                }
                public static void AndOperatorNeverCompletes(
                    NonReturningGate left,
                    Cell suffix) {
                    _ = left && Identity(left);
                    suffix.Value++;
                }
                public static void OrOperatorNeverCompletes(
                    NonReturningGate left,
                    Cell suffix) {
                    _ = left || Identity(left);
                    suffix.Value++;
                }
                private static RequiredGate Spin(RequiredGate value) {
                    while (true) { }
                }
                private static NonReturningGate Identity(
                    NonReturningGate value) => value;
            }
            """);
    }

    [TestCase("And")]
    [TestCase("Or")]
    public void TruthOperatorEffectsPrecedeTheRightOperandAndReachCatches(
        string methodName)
    {
        var compilation = TruthOperatorCompilation;
        var method = EffectTestHost.SampleMethod(compilation, methodName);
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

    private static CSharpCompilation CreateTruthOperatorCompilation()
    {
        return EffectTestHost.CreateCompilation(
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
    }
}
