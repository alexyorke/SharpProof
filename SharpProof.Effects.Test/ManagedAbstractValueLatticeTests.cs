using Microsoft.CodeAnalysis.Operations;
using SharpProof.Dataflow;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ManagedAbstractValueLatticeTests
{
    private static readonly object FirstStorage = new();
    private static readonly object SecondStorage = new();

    [Test]
    public void FactoryValuesAreCanonicalJoinRepresentatives()
    {
        foreach (var value in ValueSamples)
        {
            Assert.That(
                ManagedAbstractValue.Join(value, value),
                Is.EqualTo(value),
                $"Join is not idempotent for {value}.");
        }

        foreach (var left in ValueSamples)
        {
            foreach (var right in ValueSamples)
            {
                var join = ManagedAbstractValue.Join(left, right);
                Assert.That(
                    ValueLessThanOrEqual(left, join),
                    Is.True,
                    "Value join is not above its left operand.");
                Assert.That(
                    ValueLessThanOrEqual(right, join),
                    Is.True,
                    "Value join is not above its right operand.");
                Assert.That(
                    ManagedAbstractValue.Join(left, right),
                    Is.EqualTo(ManagedAbstractValue.Join(right, left)),
                    "Value join is not commutative.");

                if (ValueLessThanOrEqual(left, right))
                {
                    Assert.That(join, Is.EqualTo(right));
                }

                if (ValueLessThanOrEqual(right, left))
                {
                    Assert.That(join, Is.EqualTo(left));
                }
            }
        }

        foreach (var first in ValueSamples)
        {
            foreach (var second in ValueSamples)
            {
                foreach (var third in ValueSamples)
                {
                    Assert.That(
                        ManagedAbstractValue.Join(
                            ManagedAbstractValue.Join(first, second), third),
                        Is.EqualTo(
                            ManagedAbstractValue.Join(
                                first,
                                ManagedAbstractValue.Join(second, third))),
                        "Value join is not associative.");
                }
            }
        }
    }

    [Test]
    public void ReferenceFactoryRejectsUndefinedNullnessValues()
    {
        var value = ManagedAbstractValue.Reference((NullnessValue)int.MaxValue);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(value, Is.EqualTo(ManagedAbstractValue.Bottom));
            Assert.That(value.IsCanonicalRepresentation(), Is.True);
        }
    }

    [Test]
    public void StateLawsHoldAcrossTrackedAndUntrackedRepresentations()
    {
        var samples = StateSamples;

        foreach (var state in samples)
        {
            Assert.That(
                state.IsCanonicalRepresentation(),
                Is.True,
                $"State is not canonical: {state}.");
            Assert.That(
                ManagedFlowState.LessThanOrEqual(state, state),
                Is.True,
                "State order is not reflexive.");
            Assert.That(
                ManagedFlowState.LessThanOrEqual(ManagedFlowState.Bottom, state),
                Is.True,
                "Bottom is not below the state.");
            Assert.That(
                ManagedFlowState.LessThanOrEqual(state, ManagedFlowState.Top),
                Is.True,
                "The state is not below top.");
            Assert.That(
                Equivalent(ManagedFlowState.Join(state, state), state),
                Is.True,
                "State join is not idempotent.");
        }

        foreach (var left in samples)
        {
            foreach (var right in samples)
            {
                var join = ManagedFlowState.Join(left, right);
                Assert.That(
                    join.IsCanonicalRepresentation(),
                    Is.True,
                    "State join produced a non-canonical representation.");
                Assert.That(
                    ManagedFlowState.LessThanOrEqual(left, join),
                    Is.True,
                    "State join is not above its left operand.");
                Assert.That(
                    ManagedFlowState.LessThanOrEqual(right, join),
                    Is.True,
                    "State join is not above its right operand.");
                Assert.That(
                    Equivalent(join, ManagedFlowState.Join(right, left)),
                    Is.True,
                    "State join is not commutative.");

                if (ManagedFlowState.LessThanOrEqual(left, right))
                {
                    Assert.That(
                        Equivalent(join, right),
                        Is.True,
                        "Ordered state join did not absorb its upper operand.");
                }

                if (ManagedFlowState.LessThanOrEqual(right, left))
                {
                    Assert.That(
                        Equivalent(join, left),
                        Is.True,
                        "Ordered state join did not absorb its upper operand.");
                }

                foreach (var upperBound in samples)
                {
                    if (ManagedFlowState.LessThanOrEqual(left, upperBound) &&
                        ManagedFlowState.LessThanOrEqual(right, upperBound))
                    {
                        Assert.That(
                            ManagedFlowState.LessThanOrEqual(join, upperBound),
                            Is.True,
                            "State join is not below a sampled upper bound.");
                    }
                }
            }
        }

        foreach (var first in samples)
        {
            foreach (var second in samples)
            {
                foreach (var third in samples)
                {
                    Assert.That(
                        Equivalent(
                            ManagedFlowState.Join(
                                ManagedFlowState.Join(first, second), third),
                            ManagedFlowState.Join(
                                first,
                                ManagedFlowState.Join(second, third))),
                        Is.True,
                        "State join is not associative.");
                }
            }
        }
    }

    [Test]
    public void StateOperationsPreserveCanonicalRepresentationsAndExtrema()
    {
        var value = ManagedAbstractValue.Integer(IntervalValue.Range(1, 10));
        var tracked = ManagedFlowState.Empty.Set(FirstStorage, value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(ManagedFlowState.Bottom.Get(FirstStorage), Is.EqualTo(
                ManagedAbstractValue.Bottom));
            Assert.That(ManagedFlowState.Empty.Get(FirstStorage), Is.EqualTo(
                ManagedAbstractValue.Unknown));
            Assert.That(tracked.IsCanonicalRepresentation(), Is.True);
            Assert.That(tracked.Set(FirstStorage, ManagedAbstractValue.Bottom),
                Is.SameAs(ManagedFlowState.Bottom));
            Assert.That(ManagedFlowState.Top.Set(FirstStorage, value),
                Is.SameAs(ManagedFlowState.Top));
            Assert.That(ManagedFlowState.Bottom.WithUntrackedAlias(),
                Is.SameAs(ManagedFlowState.Bottom));
            Assert.That(ManagedFlowState.Empty.WithUntrackedAlias(),
                Is.SameAs(ManagedFlowState.Top));
            Assert.That(tracked.Forget(), Is.SameAs(ManagedFlowState.Empty));
            Assert.That(ManagedFlowState.Top.Forget(),
                Is.SameAs(ManagedFlowState.Top));
            Assert.That(tracked.WithUntrackedAlias(), Is.SameAs(ManagedFlowState.Top));
        }
    }

    [Test]
    public void MissingSymbolsUseTypedTopWhileNonSymbolsRemainUnknown()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Method(int integer, bool boolean, string text) { }
            }
            """);
        var method = EffectTestHost.RequireMethod(compilation, "Sample", "Method");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ManagedFlowState.Empty.Get(method.Parameters[0]),
                Is.EqualTo(ManagedAbstractValue.TopForType(method.Parameters[0].Type)));
            Assert.That(
                ManagedFlowState.Empty.Get(method.Parameters[1]),
                Is.EqualTo(ManagedAbstractValue.BooleanUnknown));
            Assert.That(
                ManagedFlowState.Empty.Get(method.Parameters[2]),
                Is.EqualTo(ManagedAbstractValue.Reference(NullnessValue.MaybeNull)));
            Assert.That(
                ManagedFlowState.Empty.Get(new object()),
                Is.EqualTo(ManagedAbstractValue.Unknown));
        }
    }

    [Test]
    public void RefinePreservesTheCanonicalValueFactories()
    {
        var integerState = ManagedFlowState.Empty.Set(
            FirstStorage,
            ManagedAbstractValue.Integer(IntervalValue.Range(0, 10)));
        var refinedInteger = ManagedAbstractFlow.Refine(
            integerState,
            FirstStorage,
            BinaryOperatorKind.GreaterThan,
            ManagedAbstractValue.Integer(IntervalValue.Constant(5)),
            expected: true);
        var impossibleInteger = ManagedAbstractFlow.Refine(
            integerState,
            FirstStorage,
            BinaryOperatorKind.Equals,
            ManagedAbstractValue.Integer(IntervalValue.Constant(42)),
            expected: true);

        var booleanState = ManagedFlowState.Empty.Set(
            FirstStorage,
            ManagedAbstractValue.BooleanUnknown);
        var refinedBoolean = ManagedAbstractFlow.Refine(
            booleanState,
            FirstStorage,
            BinaryOperatorKind.Equals,
            ManagedAbstractValue.Boolean(true),
            expected: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                refinedInteger.Get(FirstStorage),
                Is.EqualTo(ManagedAbstractValue.Integer(IntervalValue.Range(6, 10))));
            Assert.That(impossibleInteger, Is.SameAs(ManagedFlowState.Bottom));
            Assert.That(
                refinedBoolean.Get(FirstStorage),
                Is.EqualTo(ManagedAbstractValue.Boolean(true)));
            Assert.That(refinedInteger.IsCanonicalRepresentation(), Is.True);
            Assert.That(refinedBoolean.IsCanonicalRepresentation(), Is.True);
        }
    }

    [Test]
    public void TransferJoinResultsRemainCanonicalAcrossRepresentativeOperations()
    {
        var integer = ManagedAbstractValue.Integer(IntervalValue.Range(-4, 4));
        var nonZero = ManagedAbstractValue.Integer(
            IntervalValue.Range(1, 4),
            excludesZero: true);
        var boolean = ManagedAbstractValue.Boolean(true);
        var reference = ManagedAbstractValue.Reference(
            NullnessValue.MaybeNull,
            IntervalValue.Range(0, 4));

        var values = new[]
        {
            ManagedAbstractValue.Binary(BinaryOperatorKind.Add, integer, nonZero),
            ManagedAbstractValue.Binary(BinaryOperatorKind.Equals, integer, integer),
            ManagedAbstractValue.NegateBoolean(boolean),
            ManagedAbstractValue.Join(ManagedAbstractValue.Null, reference),
            ManagedAbstractValue.Join(nonZero, integer),
            ManagedAbstractValue.BinaryOverIrScalars(
                BinaryOperatorKind.Multiply,
                integer,
                nonZero)
        };

        foreach (var value in values)
        {
            Assert.That(
                ManagedAbstractValue.Join(value, value),
                Is.EqualTo(value),
                $"Transfer result is not canonical: {value}.");
        }

        var state = ManagedFlowState.Empty
            .Set(FirstStorage, values[0])
            .Set(SecondStorage, values[3]);
        Assert.That(state.IsCanonicalRepresentation(), Is.True);
    }

    private static bool Equivalent(ManagedFlowState left, ManagedFlowState right)
    {
        return ManagedFlowState.LessThanOrEqual(left, right) &&
               ManagedFlowState.LessThanOrEqual(right, left);
    }

    private static bool ValueLessThanOrEqual(
        ManagedAbstractValue left,
        ManagedAbstractValue right)
    {
        return ManagedAbstractValue.Join(left, right) == right;
    }

    private static ManagedAbstractValue[] ValueSamples { get; } =
    [
        ManagedAbstractValue.Bottom,
        ManagedAbstractValue.Unknown,
        ManagedAbstractValue.Boolean(false),
        ManagedAbstractValue.Boolean(true),
        ManagedAbstractValue.BooleanUnknown,
        ManagedAbstractValue.Integer(IntervalValue.Constant(0)),
        ManagedAbstractValue.Integer(IntervalValue.Constant(-1)),
        ManagedAbstractValue.Integer(IntervalValue.Range(-4, 4)),
        ManagedAbstractValue.Integer(
            IntervalValue.Congruent(-10, 10, new System.Numerics.BigInteger(2), 0)),
        ManagedAbstractValue.Integer(IntervalValue.Range(1, 10), excludesZero: true),
        ManagedAbstractValue.Null,
        ManagedAbstractValue.NonNull,
        ManagedAbstractValue.Reference(NullnessValue.MaybeNull),
        ManagedAbstractValue.Reference(
            NullnessValue.NonNull,
            IntervalValue.Constant(3)),
        ManagedAbstractValue.Reference(
            NullnessValue.MaybeNull,
            IntervalValue.Range(0, 10))
    ];

    private static ManagedFlowState[] StateSamples
    {
        get
        {
            var integer = ManagedAbstractValue.Integer(IntervalValue.Constant(1));
            var range = ManagedAbstractValue.Integer(IntervalValue.Range(0, 10));
            var maybeNull = ManagedAbstractValue.Reference(NullnessValue.MaybeNull);
            var boolean = ManagedAbstractValue.Boolean(true);
            var first = ManagedFlowState.Empty.Set(FirstStorage, integer);
            var second = ManagedFlowState.Empty.Set(FirstStorage, range);
            var split = ManagedFlowState.Empty
                .Set(FirstStorage, integer)
                .Set(SecondStorage, boolean);

            return
            [
                ManagedFlowState.Bottom,
                ManagedFlowState.Empty,
                ManagedFlowState.Top,
                first,
                second,
                split,
                ManagedFlowState.Empty.Set(SecondStorage, maybeNull),
                ManagedFlowState.Join(first, second),
                ManagedFlowState.Join(split, ManagedFlowState.Top)
            ];
        }
    }
}
