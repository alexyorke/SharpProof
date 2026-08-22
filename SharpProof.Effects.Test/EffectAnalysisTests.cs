using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectAnalysisTests
{
    [Test]
    public void PureArithmeticHasNoMayEffects()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static int Add(int left, int right) => left + right;
            }
            """,
            "Sample",
            "Add");

        Assert.That(result.Summary.Reads.IsEmpty, Is.True);
        Assert.That(result.Summary.Writes.IsEmpty, Is.True);
        Assert.That(result.Summary.Allocation, Is.EqualTo(EffectAllocationKind.None));
        Assert.That(result.Summary.Capabilities.IsEmpty, Is.True);
        Assert.That(result.Summary.Throws.IsEmpty, Is.True);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Projection.Effects, Is.EqualTo(SharpProofEffect.None));
        Assert.That(result.Projection.Capabilities, Is.EqualTo(SharpProofCapability.None));
    }

    [Test]
    public void NameOfIsAnExactEffectFreeCompileTimeOperation()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static string Name() => nameof(Sample);
            }
            """,
            "Sample",
            "Name");

        Assert.That(result.Summary.Allocation, Is.EqualTo(EffectAllocationKind.None));
        Assert.That(result.Summary.Throws.IsEmpty, Is.True);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void StringConstructionDistinguishesKnownAndUnknownAllocation()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static string Runtime(string left, string right) =>
                    left + right;
                public static string Constant() => "sharp" + "proof";
                public static string Interpolated(int value) =>
                    $"value: {value}";
                public static string InterpolatedString(string value) =>
                    $"{value}";
                public static string InterpolatedConstant() => $"sharp";
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var runtime = session.Analyze(Method(compilation, "Runtime"));
        var constant = session.Analyze(Method(compilation, "Constant"));
        var interpolated = session.Analyze(Method(compilation, "Interpolated"));
        var interpolatedString = session.Analyze(
            Method(compilation, "InterpolatedString"));
        var interpolatedConstant = session.Analyze(
            Method(compilation, "InterpolatedConstant"));

        Assert.That(
            runtime.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(
            runtime.Projection.Effects & SharpProofEffect.Allocates,
            Is.EqualTo(SharpProofEffect.Allocates));
        Assert.That(
            constant.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.None));
        Assert.That(
            interpolated.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Unknown));
        Assert.That(interpolated.Summary.Throws.IncludesUnknown, Is.True);
        Assert.That(interpolated.Projection.IsComplete, Is.False);
        Assert.That(
            interpolatedString.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(
            interpolatedString.Summary.Throws.IsEmpty,
            Is.True);
        Assert.That(
            interpolatedString.Projection.IsComplete,
            Is.True);
        Assert.That(
            interpolatedConstant.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.None));
    }

    [Test]
    public void OrdinaryInterpolationPreservesFormattingAndEvaluationEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class ThrowingValue {
                private static int s_state;
                public override string ToString() {
                    s_state++;
                    throw new InvalidOperationException();
                }
            }

            public static class Sample {
                private static int s_state;
                private static string Next() { s_state++; return "next"; }
                public static string StringValue(string value) => $"{{{value}}}";
                public static string Scalar(int value) => $"value={value}";
                public static string Ordered(string value) => $"{Next()}{value}";
                public static string Throwing(ThrowingValue value) => $"{value}";
                public static string Aligned(string value) => $"{value,4}";
                public static string Formatted(int value) => $"{value:X}";
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var stringValue = session.Analyze(Method(compilation, "StringValue"));
        Assert.That(stringValue.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(stringValue.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete));

        var scalar = session.Analyze(Method(compilation, "Scalar"));
        Assert.That(scalar.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Unknown));
        Assert.That(scalar.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Incomplete));

        var ordered = session.Analyze(Method(compilation, "Ordered"));
        Assert.That(ordered.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);

        var throwing = session.Analyze(Method(compilation, "Throwing"));
        Assert.That(throwing.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
        AssertContainsThrows(
            throwing.Summary,
            "System.InvalidOperationException");

        foreach (var unsupported in new[] { "Aligned", "Formatted" })
        {
            Assert.That(
                session.Analyze(Method(compilation, unsupported))
                    .Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                unsupported);
        }
    }

    [Test]
    public void StringConcatenationIncludesExactSourceToStringEffects()
    {
        var result = Analyze(
            """
            using System;

            public sealed class FormattedValue {
                private static volatile int s_state;

                public override string ToString() {
                    s_state = 1;
                    throw new InvalidOperationException();
                }
            }

            public static class Sample {
                public static string Format(FormattedValue value) =>
                    "value=" + value;
            }
            """,
            "Sample",
            "Format");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Summary.Capabilities.Contains(
                    EffectCapabilityKind.Synchronization),
                Is.True);
            AssertContainsThrows(
                result.Summary,
                "System.InvalidOperationException");
            Assert.That(
                result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }

    [Test]
    public void StringConcatenationKeepsExactNoFormattingControls()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class NoOpValue {
                public override string ToString() => "";
            }

            public readonly struct NoOpStruct {
                public override string ToString() => "";
            }

            public static class Sample {
                public static string StringOperand(string value) =>
                    "value=" + value;
                public static string NullOperand() =>
                    "value=" + (object?)null;
                public static string ReferenceNoOp(NoOpValue value) =>
                    "value=" + value;
                public static string ValueNoOp(NoOpStruct value) =>
                    "value=" + value;
                public static string Constant() => "value=" + "known";
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] {
                     "StringOperand",
                     "NullOperand",
                     "ReferenceNoOp",
                     "ValueNoOp"
                 })
        {
            var result = session.Analyze(Method(compilation, methodName));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Summary.Writes.IsEmpty, Is.True, methodName);
                Assert.That(result.Summary.Capabilities.IsEmpty, Is.True, methodName);
                Assert.That(result.Summary.Throws.IsEmpty, Is.True, methodName);
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
                Assert.That(result.Projection.IsComplete, Is.True, methodName);
            }
        }

        Assert.That(
            session.Analyze(Method(compilation, "ReferenceNoOp"))
                .Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(
            session.Analyze(Method(compilation, "ReferenceNoOp"))
                .Summary.Uncertainty & EffectUncertainty.DirectCall,
            Is.EqualTo(EffectUncertainty.DirectCall));
        Assert.That(
            session.Analyze(Method(compilation, "ValueNoOp"))
                .Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(
            session.Analyze(Method(compilation, "ValueNoOp"))
                .Summary.Uncertainty & EffectUncertainty.DirectCall,
            Is.EqualTo(EffectUncertainty.DirectCall));
        Assert.That(
            session.Analyze(Method(compilation, "Constant"))
                .Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.None));
    }

    [Test]
    public void StringConcatenationFailsClosedForUnresolvedFormatting()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public class OpenValue {
                public override string ToString() => "";
            }

            public static class Sample {
                public static string Open(OpenValue value) =>
                    "value=" + value;
                public static string Primitive(int value) =>
                    "value=" + value;
                public static string Nullable(int? value) =>
                    "value=" + value;
                public static string Interpolated(OpenValue value) =>
                    $"{value}";
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var open = session.Analyze(Method(compilation, "Open"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(open.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                open.Summary.Uncertainty & EffectUncertainty.Dispatch,
                Is.EqualTo(EffectUncertainty.Dispatch));
            Assert.That(open.Projection.IsComplete, Is.False);

            foreach (var methodName in new[] { "Primitive", "Nullable" })
            {
                var result = session.Analyze(Method(compilation, methodName));
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Incomplete),
                    methodName);
                Assert.That(
                    result.Summary.Uncertainty &
                        EffectUncertainty.UnmodeledCall,
                    Is.EqualTo(EffectUncertainty.UnmodeledCall),
                    methodName);
                Assert.That(result.Projection.IsComplete, Is.False, methodName);
            }

            var interpolated = session.Analyze(
                Method(compilation, "Interpolated"));
            Assert.That(interpolated.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(interpolated.Projection.IsComplete, Is.False);
        }
    }

    [Test]
    public void ConversionEffectsPreventFalseZeroAllocationAndDoesNotThrowProofs()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static object Box(int value) => value;
                public static string Cast(object value) => (string)value;
                public static int Unbox(object value) => (int)value;
                public static int Unwrap(int? value) => (int)value;
                public static int Dynamic(dynamic value) => (int)value;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var boxing = session.Analyze(Method(compilation, "Box"));
        Assert.That(
            boxing.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(
            boxing.Projection.Effects & SharpProofEffect.Allocates,
            Is.EqualTo(SharpProofEffect.Allocates));
        Assert.That(boxing.Projection.IsComplete, Is.True);

        var cast = session.Analyze(Method(compilation, "Cast"));
        AssertThrows(cast.Summary, "System.InvalidCastException");
        Assert.That(
            cast.Projection.Effects & SharpProofEffect.Throws,
            Is.EqualTo(SharpProofEffect.Throws));
        Assert.That(cast.Projection.IsComplete, Is.True);

        var unboxing = session.Analyze(Method(compilation, "Unbox"));
        AssertThrows(
            unboxing.Summary,
            "System.InvalidCastException",
            "System.NullReferenceException");
        Assert.That(unboxing.Projection.IsComplete, Is.True);

        var nullable = session.Analyze(Method(compilation, "Unwrap"));
        AssertThrows(nullable.Summary, "System.InvalidOperationException");
        Assert.That(nullable.Projection.IsComplete, Is.True);

        var dynamic = session.Analyze(Method(compilation, "Dynamic"));
        Assert.That(
            dynamic.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Incomplete));
        Assert.That(
            dynamic.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Unknown));
        Assert.That(dynamic.Summary.Throws.IncludesUnknown, Is.True);
        Assert.That(dynamic.Projection.IsComplete, Is.False);
    }

    [Test]
    public void ConversionEffectsUseProvenNullnessAndNullablePresence()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int? NullToNullableValue() =>
                    (int?)(object?)null;
                public static string? NullToReference() =>
                    (string?)(object?)null;

                public static int PresentNullableUnwrap() {
                    int? value = 1;
                    return (int)value;
                }

                public static int? CompatibleNullableUnbox() =>
                    (int?)(object)1;
                public static string CompatibleReferenceCast() =>
                    (string)(object)"text";

                public static int? UnknownNullableUnbox(object? value) =>
                    (int?)value;
                public static int? IncompatibleNullableUnbox() =>
                    (int?)(object)"text";
                public static string? UnknownReferenceCast(object? value) =>
                    (string?)value;
                public static string IncompatibleReferenceCast() =>
                    (string)(object)new object();
                public static int NullToNonNullableValue() =>
                    (int)(object?)null!;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "NullToNullableValue",
                     "NullToReference",
                     "PresentNullableUnwrap",
                     "CompatibleNullableUnbox",
                     "CompatibleReferenceCast"
                 })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Throws.IsEmpty,
                Is.True,
                methodName);
            Assert.That(result.Projection.IsComplete, Is.True, methodName);
        }

        foreach (var methodName in new[]
                 {
                     "UnknownNullableUnbox",
                     "IncompatibleNullableUnbox",
                     "UnknownReferenceCast",
                     "IncompatibleReferenceCast"
                 })
        {
            var result = session.Analyze(Method(compilation, methodName));
            AssertThrows(result.Summary, "System.InvalidCastException");
            AssertDoesNotThrow(
                result.Summary,
                "System.NullReferenceException");
            Assert.That(result.Projection.IsComplete, Is.True, methodName);
        }

        var nonNullable = session.Analyze(
            Method(compilation, "NullToNonNullableValue"));
        AssertThrows(nonNullable.Summary, "System.NullReferenceException");
        AssertDoesNotThrow(
            nonNullable.Summary,
            "System.InvalidCastException");
        Assert.That(nonNullable.Projection.IsComplete, Is.True);
    }

    [Test]
    public void NullableBoxingUsesProvenPresenceWithoutDefiniteUnknownWitness()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static object? Empty() => (int?)null;

                public static object? Present() {
                    int? value = 1;
                    return value;
                }

                public static object? Unknown(int? value) => value;

                public static object? LiftedEmpty() {
                    int? source = null;
                    long? value = source;
                    return value;
                }

                public static object? LiftedPresent() {
                    int? source = 1;
                    long? value = source;
                    return value;
                }

                public static object OrdinaryValue(int value) => value;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] { "Empty", "LiftedEmpty" })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.None),
                methodName);
            Assert.That(
                result.Projection.Effects & SharpProofEffect.Allocates,
                Is.EqualTo(SharpProofEffect.None),
                methodName);
            Assert.That(result.Projection.IsComplete, Is.True, methodName);
        }

        foreach (var methodName in new[]
                 {
                     "Present",
                     "LiftedPresent",
                     "OrdinaryValue"
                 })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed),
                methodName);
            Assert.That(
                result.Projection.Effects & SharpProofEffect.Allocates,
                Is.EqualTo(SharpProofEffect.Allocates),
                methodName);
            Assert.That(result.Projection.IsComplete, Is.True, methodName);
        }

        var unknown = session.Analyze(Method(compilation, "Unknown"));
        Assert.That(
            unknown.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Unknown));
        Assert.That(
            unknown.Projection.Effects & SharpProofEffect.Allocates,
            Is.EqualTo(SharpProofEffect.None));
        Assert.That(unknown.Projection.IsComplete, Is.False);
        Assert.That(unknown.DirectWitnesses, Is.Empty);
    }

    [Test]
    public void LiftedArithmeticSkipsHazardsWhenAnyOperandIsAbsent()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int? DivideNullLeft() {
                    int? left = null;
                    int? right = 0;
                    return left / right;
                }

                public static int? DivideNullRight() {
                    int? left = int.MinValue;
                    int? right = null;
                    return left / right;
                }

                public static int? DivideBothNull() {
                    int? left = null;
                    int? right = null;
                    return left / right;
                }

                public static int? RemainderNullRight() {
                    int? left = int.MinValue;
                    int? right = null;
                    return left % right;
                }

                public static uint? UnsignedDivideNullLeft() {
                    uint? left = null;
                    uint? right = 0;
                    return left / right;
                }

                public static int? CheckedAddNullLeft() {
                    int? left = null;
                    int? right = int.MaxValue;
                    return checked(left + right);
                }

                public static int? CheckedNegateNull() {
                    int? value = null;
                    return checked(-value);
                }

                public static int? CheckedIncrementNull() {
                    int? value = null;
                    checked { value++; }
                    return value;
                }

                public static int? DivideAssignNullRight() {
                    int? left = 1;
                    int? right = null;
                    left /= right;
                    return left;
                }

                public static int? DivideAssignNullLeft() {
                    int? left = null;
                    int? right = 0;
                    left /= right;
                    return left;
                }

                public static int? CheckedAddAssignNull() {
                    int? left = null;
                    int? right = int.MaxValue;
                    checked { left += right; }
                    return left;
                }

                public static int? UncheckedAddNull() {
                    int? left = null;
                    int? right = int.MaxValue;
                    return unchecked(left + right);
                }

                public static long? CheckedConversionNull() {
                    int? value = null;
                    return checked((long?)value);
                }

                public static int? PresentDivideZero() {
                    int? left = 1;
                    int? right = 0;
                    return left / right;
                }

                public static int? PresentCheckedAdd() {
                    int? left = int.MaxValue;
                    int? right = 1;
                    return checked(left + right);
                }

                public static int? PresentCheckedIncrement() {
                    int? value = int.MaxValue;
                    checked { value++; }
                    return value;
                }

                public static int? UnknownDivide(int? left, int? right) =>
                    left / right;

                public static int? UnknownCheckedAdd(int? left, int? right) =>
                    checked(left + right);
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "DivideNullLeft",
                     "DivideNullRight",
                     "DivideBothNull",
                     "RemainderNullRight",
                     "UnsignedDivideNullLeft",
                     "CheckedAddNullLeft",
                     "CheckedNegateNull",
                     "CheckedIncrementNull",
                     "DivideAssignNullRight",
                     "DivideAssignNullLeft",
                     "CheckedAddAssignNull",
                     "UncheckedAddNull",
                     "CheckedConversionNull"
                 })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Throws.IsEmpty,
                Is.True,
                methodName);
            Assert.That(result.Projection.IsComplete, Is.True, methodName);
        }

        AssertThrows(
            session.Analyze(Method(compilation, "PresentDivideZero")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "PresentCheckedAdd")).Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "PresentCheckedIncrement")).Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "UnknownDivide")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "UnknownCheckedAdd")).Summary,
            "System.OverflowException");
    }

    [Test]
    public void ManagedAllocationUsesModeledObjectConstructor()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static object Create() => new object();
            }
            """,
            "Sample",
            "Create");

        Assert.That(result.Summary.Allocation, Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Summary.Uncertainty, Is.EqualTo(EffectUncertainty.DirectCall));
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(SharpProofEffect.Allocates));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void VolatileFieldAccessRequiresSynchronizationCapability()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                private volatile int _volatileValue;
                private int _ordinaryValue;

                public int ReadVolatile() => _volatileValue;
                public int ReadOrdinary() => _ordinaryValue;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var volatileRead = session.Analyze(Method(compilation, "ReadVolatile"));
        var ordinaryRead = session.Analyze(Method(compilation, "ReadOrdinary"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                volatileRead.Summary.Capabilities.Contains(
                    EffectCapabilityKind.Synchronization),
                Is.True);
            Assert.That(
                volatileRead.Projection.Capabilities,
                Is.EqualTo(SharpProofCapability.Synchronization));
            Assert.That(
                ordinaryRead.Summary.Capabilities.IsEmpty,
                Is.True);
            Assert.That(
                ordinaryRead.Projection.Capabilities,
                Is.EqualTo(SharpProofCapability.None));
        }
    }

    [Test]
    public void CompileTimeConstantsDoNotReadStaticState()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private const int Answer = 42;

                public static int ReadConstant() => Answer;
                public static System.DayOfWeek ReadEnum() =>
                    System.DayOfWeek.Monday;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "ReadConstant"))
                    .Summary.Reads.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "ReadEnum"))
                    .Summary.Reads.IsEmpty,
                Is.True);
        }
    }

    [Test]
    public void StringAndArrayLengthsReadTheirParameterRegionsCompletely()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int StringLength(string value) => value.Length;

                public static int ArrayLength(int[] values) {
                    var alias = values;
                    return alias.Length;
                }

                public static long ArrayLongLength(int[] values) =>
                    values.LongLength;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] {
                     "StringLength",
                     "ArrayLength",
                     "ArrayLongLength"
                 })
        {
            var result = session.Analyze(
                Method(compilation, methodName));
            var summary = result.Summary;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    summary.Reads.Regions,
                    Is.EquivalentTo(new[] {
                        EffectRegionId.Parameter(0)
                    }),
                    methodName);
                Assert.That(
                    summary.Writes.IsEmpty,
                    Is.True,
                    methodName);
                Assert.That(
                    summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
                Assert.That(
                    result.Projection.IsComplete,
                    Is.True,
                    methodName);
                Assert.That(
                    summary.Uncertainty.HasFlag(
                        EffectUncertainty.DirectCall),
                    Is.EqualTo(methodName == "StringLength"),
                    methodName);
                AssertContainsThrows(
                    summary,
                    "System.NullReferenceException");
            }
        }
    }

    [Test]
    public void PropertyIncrementUsesBothAccessorsWithoutBecomingIncomplete()
    {
        var result = Analyze(
            """
            public sealed class Sample {
                private int _value;

                private int Value {
                    get => _value;
                    set => _value = value;
                }

                public void Increment() => Value++;
            }
            """,
            "Sample",
            "Increment");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Reads.Regions, Does.Contain(
                EffectRegionId.Receiver));
            Assert.That(result.Summary.Writes.Regions, Does.Contain(
                EffectRegionId.Receiver));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }

    [Test]
    public void CapturedPrimaryConstructorParametersReadReceiverState()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class ClassSample(int seed) {
                private static int Pass(int value) => value;

                public int Read() => seed;
                public int Forward() => Pass(seed);
                public int Value => seed;
            }

            public record class RecordSample(int seed) {
                public int Read() => seed;
                public int Value => seed;
            }

            public struct StructSample(int seed) {
                public int Read() => seed;
                public int Value => seed;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var methods = new[] {
            EffectTestHost.RequireMethod(
                compilation,
                "ClassSample",
                "Read"),
            EffectTestHost.RequireMethod(
                compilation,
                "ClassSample",
                "Forward"),
            RequireGetter(compilation, "ClassSample", "Value"),
            EffectTestHost.RequireMethod(
                compilation,
                "RecordSample",
                "Read"),
            RequireGetter(compilation, "RecordSample", "Value"),
            EffectTestHost.RequireMethod(
                compilation,
                "StructSample",
                "Read"),
            RequireGetter(compilation, "StructSample", "Value")
        };

        foreach (var method in methods)
        {
            var result = session.Analyze(method);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Reads.Regions,
                    Is.EqualTo(new[] { EffectRegionId.Receiver }),
                    method.ToDisplayString());
                Assert.That(
                    result.Summary.Writes.IsEmpty,
                    Is.True,
                    method.ToDisplayString());
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    method.ToDisplayString());
                Assert.That(
                    result.Projection.Effects,
                    Is.EqualTo(SharpProofEffect.ReadsReceiverState),
                    method.ToDisplayString());
                Assert.That(
                    result.Projection.IsComplete,
                    Is.True,
                    method.ToDisplayString());
            }
        }
    }

    [Test]
    public void PrimaryConstructorAndOrdinaryParameterReadsStayLocal()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public class BaseSample {
                protected BaseSample(int value) {
                }
            }

            public sealed class ForwardOnly(int seed) : BaseSample(seed) {
                public int Constant() => 1;
                public int Ordinary(int value) => value;
            }

            public sealed class ConstructorOnly(int seed) {
                private readonly int _copy = seed;

                public int Constant() => 1;
                public static int Ordinary(int value) => value;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var exactControls = new[] {
            EffectTestHost.RequireMethod(
                compilation,
                "ForwardOnly",
                "Constant"),
            EffectTestHost.RequireMethod(
                compilation,
                "ForwardOnly",
                "Ordinary"),
            EffectTestHost.RequireMethod(
                compilation,
                "ConstructorOnly",
                "Constant"),
            EffectTestHost.RequireMethod(
                compilation,
                "ConstructorOnly",
                "Ordinary")
        };

        foreach (var method in exactControls)
        {
            var result = session.Analyze(method);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Reads.IsEmpty,
                    Is.True,
                    method.ToDisplayString());
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    method.ToDisplayString());
                Assert.That(
                    result.Projection.Effects,
                    Is.EqualTo(SharpProofEffect.None),
                    method.ToDisplayString());
            }
        }

        foreach (var typeName in new[] { "ForwardOnly", "ConstructorOnly" })
        {
            var constructor = EffectTestHost.RequireType(
                    compilation,
                    typeName)
                .InstanceConstructors
                .Single(static method => method.Parameters.Length == 1);
            Assert.That(
                session.Analyze(constructor).Summary.Reads.IsEmpty,
                Is.True,
                typeName);
        }
    }

    [Test]
    public void ValueTypeConstructionDoesNotReportManagedAllocation()
    {
        var result = Analyze(
            """
            public readonly struct Token {
                public Token(int value) {
                }
            }
            public static class Sample {
                public static Token Create() => new Token(1);
            }
            """,
            "Sample",
            "Create");

        Assert.That(result.Summary.Allocation, Is.EqualTo(EffectAllocationKind.None));
        Assert.That(
            result.Projection.Effects & SharpProofEffect.Allocates,
            Is.EqualTo(SharpProofEffect.None));
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
    }

    [Test]
    public void ProvablyEmptyImplicitConstructorsAreModeledExactly()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Global {
                public static int State;
                public static int Touch() => ++State;
            }

            public sealed class ImplicitClass { }

            public sealed class ExplicitClass {
                public ExplicitClass() { }
            }

            public class ExplicitEmptyBase {
                protected ExplicitEmptyBase() { }
            }

            public sealed class ImplicitEmptyDerived : ExplicitEmptyBase { }

            public struct ImplicitStruct { }

            public sealed record ImplicitRecord;

            public class EffectfulBase {
                protected EffectfulBase() {
                    Global.Touch();
                }
            }

            public sealed class ImplicitDerived : EffectfulBase { }

            public sealed class MemberInitializer {
                private readonly int _value = Global.Touch();
            }

            public sealed class StaticInitializer {
                private static readonly int Value = Global.Touch();
            }

            public static class Sample {
                public static ImplicitClass Class() => new();
                public static ExplicitClass Explicit() => new();
                public static ImplicitEmptyDerived EmptyDerived() => new();
                public static ImplicitStruct Struct() => new();
                public static ImplicitRecord Record() => new();
                public static ImplicitDerived Derived() => new();
                public static MemberInitializer Member() => new();
                public static StaticInitializer Static() => new();
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] {
                     "Class", "Explicit", "EmptyDerived", "Struct", "Record"
                 })
        {
            var result = session.Analyze(Method(compilation, methodName));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
                Assert.That(
                    result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                    Is.EqualTo(EffectUncertainty.None),
                    methodName);
            }
        }

        foreach (var methodName in new[] { "Member", "Static" })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                methodName);
        }

        var derived = session.Analyze(Method(compilation, "Derived"));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                derived.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                derived.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
        }
    }

    [Test]
    public void ObjectAndCollectionInitializersContributeTheirEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System.Collections;

            public sealed class Value {
                public int Number;

                public Value() {
                }
            }

            public sealed class Values : IEnumerable {
                public Values() {
                }

                public void Add(int value) {
                }

                public IEnumerator GetEnumerator() =>
                    throw new System.NotSupportedException();
            }

            public static class Sample {
                private static int s_state;

                private static int SideEffect() {
                    s_state = 1;
                    return 1;
                }

                public static Value ObjectInitializer() =>
                    new Value { Number = SideEffect() };

                public static Values CollectionInitializer() =>
                    new Values { SideEffect() };

                public static int[] ArrayInitializer() =>
                    new[] { SideEffect() };
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var objectInitializer = session.Analyze(
            Method(compilation, "ObjectInitializer"));
        var collectionInitializer = session.Analyze(
            Method(compilation, "CollectionInitializer"));
        var arrayInitializer = session.Analyze(
            Method(compilation, "ArrayInitializer"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                objectInitializer.Summary.Writes.IsUnknown,
                Is.False);
            Assert.That(
                objectInitializer.Summary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.True);
            Assert.That(
                collectionInitializer.Summary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.True);
            Assert.That(
                objectInitializer.Projection.IsComplete,
                Is.True);
            Assert.That(
                collectionInitializer.Projection.Effects &
                SharpProofEffect.WritesStaticState,
                Is.EqualTo(SharpProofEffect.WritesStaticState));
            Assert.That(
                arrayInitializer.Projection.Effects &
                SharpProofEffect.WritesStaticState,
                Is.EqualTo(SharpProofEffect.WritesStaticState));
        }
    }

    [Test]
    public void FreshObjectInitializerOwnershipMatrixIsExact()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Collections;

            public sealed class Value {
                public int Field;
                private int _property;
                public int Property {
                    get => _property;
                    set => _property = value;
                }
                public int this[int index] { get => 0; set { } }
            }

            public struct StructValue {
                public int Field;
                private int _property;
                public int Property {
                    readonly get => _property;
                    set => _property = value;
                }
            }

            public sealed class Nested {
                public Value Child;
            }

            public sealed class Values : IEnumerable {
                public void Add(int value) { }
                public IEnumerator GetEnumerator() =>
                    throw new NotSupportedException();
            }

            public sealed class ThrowingValue {
                public int Property {
                    set => throw new InvalidOperationException();
                }
            }

            public static class Sample {
                private static int s_state;

                private static int SideEffect() {
                    s_state++;
                    return 1;
                }

                public static Value Field() => new() { Field = 1 };
                public static Value Property() => new() { Property = 1 };
                public static Value Indexer() => new() { [0] = 1 };
                public static Nested NestedFresh() =>
                    new() { Child = new() { Field = 1 } };
                public static Values Collection() => new() { 1 };
                public static StructValue StructField() => new() { Field = 1 };
                public static StructValue StructProperty() => new() { Property = 1 };
                public static Value StaticEffect() => new() { Field = SideEffect() };
                public static ThrowingValue ThrowingSetter() => new() { Property = 1 };
                public static int[] FreshArray() => new[] { 1 };
                public static Nested ExternalAlias(Value value) {
                    var result = new Nested { Child = value };
                    result.Child.Field = 1;
                    return result;
                }
                public static Nested MemberDerived() =>
                    new() { Child = { Field = 1 } };
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            foreach (var name in new[] {
                         "Field", "Property", "Indexer", "NestedFresh",
                         "Collection", "StructField", "StructProperty", "FreshArray"
                     })
            {
                var result = session.Analyze(Method(compilation, name));
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    name);
                Assert.That(
                    EffectContractMappings.IsObservablePure(result.Summary),
                    Is.True,
                    name);
                Assert.That(
                    result.Summary.Writes.IsUnknown,
                    Is.False,
                    name);
            }

            var staticEffect = session.Analyze(Method(compilation, "StaticEffect"));
            Assert.That(
                staticEffect.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "StaticEffect");
            Assert.That(
                staticEffect.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete),
                "StaticEffect");

            var throwing = session.Analyze(Method(compilation, "ThrowingSetter"));
            Assert.That(
                throwing.Summary.Throws.Types.Select(static type => type.Name),
                Does.Contain("InvalidOperationException"),
                "ThrowingSetter");
            Assert.That(
                throwing.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete),
                "ThrowingSetter");

            foreach (var name in new[] { "ExternalAlias", "MemberDerived" })
            {
                var result = session.Analyze(Method(compilation, name));
                Assert.That(
                    EffectContractMappings.IsObservablePure(result.Summary),
                    Is.False,
                    name);
                Assert.That(
                    result.Summary.Writes.IsUnknown,
                    Is.True,
                    name);
            }
        }
    }

    [Test]
    public void FreshInitializerCaptureSourcesAreCompilerOwnedCreations()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Value { public int Field; }
            public struct StructValue {
                public int Field;
                public int Property { get; set; }
            }
            public static class Sample {
                public static Value Field() => new() { Field = 1 };
                public static StructValue StructField() => new() { Field = 1 };
                public static StructValue StructProperty() => new() { Property = 1 };
            }
            """);
        var shapes = new List<string>();
        foreach (var name in new[] { "Field", "StructField", "StructProperty" })
        {
            var method = Method(compilation, name);
            var declaration = method.DeclaringSyntaxReferences.Single().GetSyntax();
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            var body = model.GetOperation(declaration) as IMethodBodyOperation;
            Assert.That(body, Is.Not.Null, name);
            var graph = ControlFlowGraph.Create(body!);
            shapes.AddRange(graph.Blocks
                .SelectMany(static block => block.Operations)
                .SelectMany(static operation => operation.DescendantsAndSelf())
                .OfType<IFlowCaptureOperation>()
                .Select(capture =>
                    name + ":" + capture.Value.Kind + ":" +
                    capture.Value.Syntax.Kind() + ":" +
                    capture.Value.Type?.ToDisplayString()));
        }

        Assert.That(
            string.Join("\n", shapes),
            Is.EqualTo(
                "Field:ObjectCreation:ImplicitObjectCreationExpression:Value\n" +
                "StructField:ObjectCreation:ImplicitObjectCreationExpression:StructValue\n" +
                "StructProperty:ObjectCreation:ImplicitObjectCreationExpression:StructValue"));
    }

    [Test]
    public void ConstructorMemberInitializersContributeTheirEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                private static int s_state;
                private int _value = SideEffect();

                public Sample() {
                }

                private static int SideEffect() {
                    s_state = 1;
                    return 1;
                }
            }
            """);
        var constructor = EffectTestHost.RequireType(compilation, "Sample")
            .InstanceConstructors
            .Single(static method =>
                !method.IsImplicitlyDeclared &&
                method.Parameters.Length == 0);

        var result = new EffectAnalysisSession(compilation).Analyze(constructor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Receiver),
                Is.True);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Projection.Effects &
                (SharpProofEffect.WritesReceiverState |
                 SharpProofEffect.WritesStaticState),
                Is.EqualTo(
                    SharpProofEffect.WritesReceiverState |
                    SharpProofEffect.WritesStaticState));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void ThrowingMemberInitializerSuppressesLaterInitializationAndBody()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                private static int s_state;
                private readonly int _zFirst = Fail();
                private readonly int _aSecond = Mutate();

                public Sample() {
                    s_state++;
                }

                private static int Fail() =>
                    throw new System.InvalidOperationException();

                private static int Mutate() {
                    s_state++;
                    return 1;
                }
            }
            """);
        var constructor = EffectTestHost.RequireType(compilation, "Sample")
            .InstanceConstructors
            .Single(static method => !method.IsImplicitlyDeclared);
        var result = new EffectAnalysisSession(compilation).Analyze(constructor);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Receiver),
                Is.False);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void PossibleTypeInitializationFailsClosed()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                private static int s_state = Initialize();

                public Sample() {
                }

                public static int Read() => s_state;

                private static int Initialize() => 1;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var constructor = EffectTestHost.RequireType(compilation, "Sample")
            .InstanceConstructors
            .Single(static method =>
                !method.IsImplicitlyDeclared &&
                method.Parameters.Length == 0);

        foreach (var method in new[] {
                     constructor,
                     Method(compilation, "Read")
                 })
        {
            var result = session.Analyze(method);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                method.MetadataName);
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                Is.EqualTo(EffectUncertainty.UnmodeledCall),
                method.MetadataName);
            Assert.That(
                result.Projection.IsComplete,
                Is.False,
                method.MetadataName);
        }
    }

    [Test]
    public void ValueTypeInstanceCallsAccountForExplicitTypeInitialization()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Global {
                public static object? State;
            }

            public struct InitializedValue {
                static InitializedValue() {
                    Global.State = new object();
                    throw new InvalidOperationException();
                }

                public readonly void Touch() { }
                public readonly int Number => 0;
            }

            public readonly struct PlainValue {
                public void Touch() { }
                public int Number => 0;
            }

            public sealed class InitializedReference {
                static InitializedReference() {
                    Global.State = new object();
                }

                public void Touch() { }
            }

            public static class InitializedStatic {
                static InitializedStatic() {
                    Global.State = new object();
                }

                public static void Touch() { }
            }

            public static class Sample {
                public static void CallInitializedMethod() =>
                    default(InitializedValue).Touch();

                public static int ReadInitializedProperty() =>
                    default(InitializedValue).Number;

                public static void CallPlainMethod() =>
                    default(PlainValue).Touch();

                public static int ReadPlainProperty() =>
                    default(PlainValue).Number;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var affected = new[]
        {
            session.Analyze(Method(compilation, "CallInitializedMethod")),
            session.Analyze(Method(compilation, "ReadInitializedProperty")),
            session.Analyze(EffectTestHost.RequireMethod(
                compilation,
                "InitializedStatic",
                "Touch"))
        };
        var exact = new[]
        {
            session.Analyze(Method(compilation, "CallPlainMethod")),
            session.Analyze(Method(compilation, "ReadPlainProperty")),
            session.Analyze(EffectTestHost.RequireMethod(
                compilation,
                "InitializedReference",
                "Touch"))
        };

        foreach (var result in affected)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Incomplete),
                    ResultKey(result));
                Assert.That(
                    result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                    Is.EqualTo(EffectUncertainty.UnmodeledCall),
                    ResultKey(result));
                Assert.That(
                    result.Summary.Writes.IsUnknown,
                    Is.True,
                    ResultKey(result));
                Assert.That(
                    result.Summary.Allocation,
                    Is.EqualTo(EffectAllocationKind.Unknown),
                    ResultKey(result));
                Assert.That(
                    result.Summary.Throws.IncludesUnknown,
                    Is.True,
                    ResultKey(result));
                Assert.That(
                    result.Projection.IsComplete,
                    Is.False,
                    ResultKey(result));
            }
        }

        foreach (var result in exact)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    ResultKey(result));
                Assert.That(result.Summary.Reads.IsEmpty, Is.True, ResultKey(result));
                Assert.That(result.Summary.Writes.IsEmpty, Is.True, ResultKey(result));
                Assert.That(
                    result.Summary.Allocation,
                    Is.EqualTo(EffectAllocationKind.None),
                    ResultKey(result));
                Assert.That(result.Summary.Throws.IsEmpty, Is.True, ResultKey(result));
                Assert.That(result.Projection.IsComplete, Is.True, ResultKey(result));
            }
        }
    }

    [Test]
    public void CrossTypeStaticFieldAccessAccountsForTypeInitialization()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Global {
                public static int Value;
            }

            public static class Initialized {
                public static readonly object Value = Initialize();

                private static object Initialize() {
                    Global.Value = 1;
                    return new object();
                }
            }

            public static class Sample {
                public static object Read() => Initialized.Value;
            }
            """);

        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Read"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                Is.EqualTo(EffectUncertainty.UnmodeledCall));
            Assert.That(result.Summary.Reads.IsUnknown, Is.True);
            Assert.That(result.Summary.Writes.IsUnknown, Is.True);
            Assert.That(result.Projection.IsComplete, Is.False);
        }
    }

    [Test]
    public void MetadataStaticFieldAccessFailsClosedAtTypeInitializationBoundary()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            public static class ExternalInitialized {
                public static readonly object Value = new object();
            }
            """,
            "ExternalInitializedAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static object Read() => ExternalInitialized.Value;
            }
            """,
            externalReference);

        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Read"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                Is.EqualTo(EffectUncertainty.UnmodeledCall));
            Assert.That(result.Projection.IsComplete, Is.False);
        }
    }

    [Test]
    public void FieldWritesRetainReceiverAndStaticRegions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Sample {
                private int _value;
                private static int s_value;

                public void WriteReceiver() => _value = 1;
                public static void WriteStatic() => s_value = 1;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var receiver = session.Analyze(
            EffectTestHost.RequireMethod(compilation, "Sample", "WriteReceiver"));
        var @static = session.Analyze(
            EffectTestHost.RequireMethod(compilation, "Sample", "WriteStatic"));

        Assert.That(
            receiver.Summary.Writes.Contains(EffectRegionId.Receiver),
            Is.True);
        Assert.That(
            receiver.Projection.Effects,
            Is.EqualTo(SharpProofEffect.WritesReceiverState));
        Assert.That(
            @static.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
        Assert.That(
            @static.Projection.Effects,
            Is.EqualTo(SharpProofEffect.WritesStaticState));
    }

    [Test]
    public void CoalesceAssignmentRetainsObservableTargetWrites()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public object? Value;
            }

            public sealed class Sample {
                private object? _field;
                private static object? s_field;
                private object? _propertyValue;
                private object? _indexerValue;

                private object? Property {
                    get => _propertyValue;
                    set => _propertyValue = value;
                }

                private object? this[int index] {
                    get => _indexerValue;
                    set => _indexerValue = value;
                }

                public void ReceiverField(object value) =>
                    _field ??= value;
                public static void StaticField(object value) =>
                    s_field ??= value;
                public static void ParameterField(Box box, object value) =>
                    box.Value ??= value;
                public void PropertySetter(object value) =>
                    Property ??= value;
                public void IndexerSetter(object value) =>
                    this[0] ??= value;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var cases = new[] {
            (Method: "ReceiverField", Region: EffectRegionId.Receiver),
            (Method: "StaticField", Region: EffectRegionId.Static()),
            (Method: "ParameterField", Region: EffectRegionId.Parameter(0)),
            (Method: "PropertySetter", Region: EffectRegionId.Receiver),
            (Method: "IndexerSetter", Region: EffectRegionId.Receiver)
        };

        foreach (var (methodName, region) in cases)
        {
            var method = EffectTestHost.RequireMethod(
                compilation,
                "Sample",
                methodName);
            var result = session.Analyze(method);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Writes.Regions,
                    Is.EqualTo(new[] { region }),
                    methodName);
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
                Assert.That(result.Projection.IsComplete, Is.True, methodName);
            }
        }
    }

    [Test]
    public void CoalesceAssignmentLocalTargetsRemainConservativeAndUnobservable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void DefinitelyNull() {
                    object? value = null;
                    value ??= new object();
                }

                public static void DefinitelyNonNull() {
                    object? value = typeof(object);
                    value ??= new object();
                }

                public static void MaybeNull(object? value) {
                    value ??= new object();
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var definitelyNull = session.Analyze(
            Method(compilation, "DefinitelyNull"));
        var definitelyNonNull = session.Analyze(
            Method(compilation, "DefinitelyNonNull"));
        var maybeNull = session.Analyze(
            Method(compilation, "MaybeNull"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                definitelyNull.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                definitelyNonNull.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                maybeNull.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                new[] {
                    definitelyNull.Summary,
                    definitelyNonNull.Summary,
                    maybeNull.Summary
                },
                Has.All.Property(nameof(EffectSummary.Writes))
                    .EqualTo(EffectRegionSet.Empty));
            Assert.That(
                new[] {
                    definitelyNull.Summary,
                    definitelyNonNull.Summary,
                    maybeNull.Summary
                },
                Has.All.Property(nameof(EffectSummary.Completeness))
                    .EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void UsingFormsIncludeImplicitDisposeEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class Resource : IDisposable {
                private static volatile int s_state;

                public void Dispose() {
                    s_state = 1;
                    throw new InvalidOperationException();
                }
            }

            public static class Sample {
                public static void Statement(Resource resource) {
                    using (resource) { }
                }

                public static void Declaration(Resource resource) {
                    using var alias = resource;
                }

                public static void Explicit(Resource resource) =>
                    resource.Dispose();
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] { "Statement", "Declaration", "Explicit" })
        {
            var result = session.Analyze(Method(compilation, methodName));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Summary.Writes.Contains(EffectRegionId.Static()),
                    Is.True,
                    methodName);
                Assert.That(
                    result.Summary.Capabilities.Contains(
                        EffectCapabilityKind.Synchronization),
                    Is.True,
                    methodName);
                AssertContainsThrows(
                    result.Summary,
                    "System.InvalidOperationException");
                Assert.That(result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete), methodName);
            }
        }
    }

    [Test]
    public void UsingNullNoOpAndInterfaceControlsStaySound()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class NoOp : IDisposable {
                public void Dispose() { }
            }

            public sealed class NullSensitive : IDisposable {
                private static int s_state;
                public void Dispose() => s_state = 1;
            }

            public static class Sample {
                public static void NoOpResource(NoOp resource) {
                    using (resource) { }
                }

                public static void NullResource() {
                    NullSensitive? resource = null;
                    using (resource) { }
                }

                public static void InterfaceResource(IDisposable resource) {
                    using (resource) { }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var noOp = session.Analyze(Method(compilation, "NoOpResource"));
        var @null = session.Analyze(Method(compilation, "NullResource"));
        var @interface = session.Analyze(Method(compilation, "InterfaceResource"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(noOp.Summary.Writes.IsEmpty, Is.True);
            Assert.That(noOp.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(@null.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
            Assert.That(@null.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(@interface.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(@interface.Summary.Uncertainty & EffectUncertainty.Dispatch,
                Is.EqualTo(EffectUncertainty.Dispatch));
        }
    }

    [Test]
    public void LocalAliasesRetainCallerOwnedAndFreshRegions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                private static Box? s_box;
                public int Value;

                public void ReceiverAlias() {
                    var alias = this;
                    alias.Value = 1;
                }

                public static void ParameterAlias(Box value) {
                    var alias = value;
                    alias.Value = 1;
                }

                public static void StaticAlias() {
                    var alias = s_box;
                    if (alias != null) {
                        alias.Value = 1;
                    }
                }

                public static void FreshAlias() {
                    var alias = new int[1];
                    alias[0] = 1;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var receiver = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Box",
                "ReceiverAlias"));
        var parameter = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Box",
                "ParameterAlias"));
        var @static = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Box",
                "StaticAlias"));
        var fresh = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Box",
                "FreshAlias"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                receiver.Summary.Writes.Contains(EffectRegionId.Receiver),
                Is.True);
            Assert.That(
                parameter.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(
                @static.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(fresh.Summary.Writes.IsEmpty, Is.False);
            Assert.That(
                fresh.Summary.Writes.Regions,
                Has.All.Property(nameof(EffectRegionId.Kind))
                    .EqualTo(EffectRegionKind.Fresh));
            Assert.That(
                new[] {
                    receiver.Summary,
                    parameter.Summary,
                    @static.Summary,
                    fresh.Summary
                },
                Has.All.Property(nameof(EffectSummary.Completeness))
                    .EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void FreshArrayContentsDoNotBecomeFreshOwnedAliases()
    {
        var result = Analyze(
            """
            public sealed class Box {
                public int Value;
            }

            public static class Sample {
                public static void Mutate(Box value) {
                    var holder = new[] { value };
                    var alias = holder[0];
                    alias.Value = 1;
                }
            }
            """,
            "Sample",
            "Mutate");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Writes.IsEmpty, Is.False);
            Assert.That(
                result.Summary.Writes.IsUnknown ||
                result.Summary.Writes.Regions.Any(
                    static region => region.Kind != EffectRegionKind.Fresh),
                Is.True);
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.False);
        }
    }

    [Test]
    public void FreshObjectContentsDoNotBecomeFreshOwnedAliases()
    {
        var result = Analyze(
            """
            public sealed class Box {
                public int Value;
            }

            public sealed class Holder {
                public Box Child = null!;
            }

            public static class Sample {
                public static void Mutate(Box value) {
                    (new Holder { Child = value }).Child.Value = 1;
                }
            }
            """,
            "Sample",
            "Mutate");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Writes.IsEmpty, Is.False);
            Assert.That(
                result.Summary.Writes.IsUnknown ||
                result.Summary.Writes.Regions.Any(
                    static region => region.Kind != EffectRegionKind.Fresh),
                Is.True);
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.False);
        }
    }

    [Test]
    public void NestedFreshContainerContentsDoNotBecomeFreshOwnedAliases()
    {
        var result = Analyze(
            """
            public sealed class Box {
                public int Value;
            }

            public static class Sample {
                public static void Mutate(Box value) {
                    var inner = new[] { value };
                    var outer = new[] { inner };
                    outer[0][0].Value = 1;
                }
            }
            """,
            "Sample",
            "Mutate");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Writes.IsEmpty, Is.False);
            Assert.That(
                result.Summary.Writes.IsUnknown ||
                result.Summary.Writes.Regions.Any(
                    static region => region.Kind != EffectRegionKind.Fresh),
                Is.True);
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.False);
        }
    }

    [Test]
    public void FreshValueArrayStorageRemainsFreshOwned()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static void Mutate() {
                    var values = new int[1];
                    values[0] = 1;
                }
            }
            """,
            "Sample",
            "Mutate");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Writes.IsUnknown, Is.False);
            Assert.That(result.Summary.Writes.IsEmpty, Is.False);
            Assert.That(
                result.Summary.Writes.Regions,
                Has.All.Property(nameof(EffectRegionId.Kind))
                    .EqualTo(EffectRegionKind.Fresh));
            Assert.That(result.Projection.IsComplete, Is.True);
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.True);
        }
    }

    [Test]
    public void TrustedCompleteExternalContractIsTheCapabilityOverride()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.ReadsAmbientState,
                    Capabilities = SharpProofCapability.Console,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public static void Touch() {
                }
            }
            """,
            "ExternalFixtureAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Invoke() => ExternalFixture.Touch();
            }
            """,
            externalReference);

        var result = new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(compilation, "Sample", "Invoke"));

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(
            result.Summary.Reads.Contains(EffectRegionId.Ambient),
            Is.True);
        Assert.That(
            result.Summary.Capabilities.Contains(EffectCapabilityKind.Console),
            Is.True);
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(SharpProofEffect.ReadsAmbientState));
        Assert.That(result.Projection.IsComplete, Is.True);
        Assert.That(
            result.Projection.Capabilities,
            Is.EqualTo(SharpProofCapability.Console));
    }

    [Test]
    public void UnprovenSourceRequiresMakeImportedEffectsIncomplete()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Sample {
                private static void Restricted(int value) {
                    Contract.Requires(value > 0);
                }

                private static void Unrestricted(int value) {
                }

                public static void InvokeRestricted(int value) =>
                    Restricted(value);

                public static void InvokeUnrestricted(int value) =>
                    Unrestricted(value);
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var restricted = session.Analyze(
            Method(compilation, "InvokeRestricted"));
        var unrestricted = session.Analyze(
            Method(compilation, "InvokeUnrestricted"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                restricted.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                restricted.Summary.AnalysisIncompleteReason,
                Is.EqualTo(
                    EffectAnalysisIncompleteReason
                        .CallPreconditionNotProven));
            Assert.That(
                restricted.Projection.IsComplete,
                Is.False);
            Assert.That(
                unrestricted.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                unrestricted.Projection.IsComplete,
                Is.True);
        }
    }

    [Test]
    public void UnprovenExternalClosedPreconditionMakesTrustedSummaryIncomplete()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.None,
                    IsDeterministic = true,
                    Complete = true)]
                public static void Restricted(
                    [Positive] int value) {
                }
            }
            """,
            "ExternalPreconditionAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Invoke(int value) =>
                    ExternalFixture.Restricted(value);
            }
            """,
            externalReference);

        var result = new EffectAnalysisSession(
            compilation).Analyze(
            Method(compilation, "Invoke"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                result.Summary.AnalysisIncompleteReason,
                Is.EqualTo(
                    EffectAnalysisIncompleteReason
                        .CallPreconditionNotProven));
            Assert.That(
                result.Projection.IsComplete,
                Is.False);
        }
    }

    [Test]
    public void DirectExternalAnalysisAppliesClosedEntryPreconditions()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalFixture {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.None,
                    IsDeterministic = true,
                    PreconditionFree = true,
                    Complete = true)]
                public static void Restricted([Positive] int value) {
                }
            }
            """,
            "DirectExternalPreconditionAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            "public static class Sample { }",
            externalReference);
        var session = new EffectAnalysisSession(compilation);

        var result = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "ExternalFixture",
                "Restricted"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                result.Summary.AnalysisIncompleteReason,
                Is.EqualTo(
                    EffectAnalysisIncompleteReason
                        .CallPreconditionNotProven));
            Assert.That(result.Projection.IsComplete, Is.False);
        }
    }

    [Test]
    public void SourceOnlyMetadataPreconditionsCannotDisappearIntoTrustedSummaries()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class DirectBoundary {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.None,
                    IsDeterministic = true,
                    Complete = true)]
                public static void Restricted(int value) {
                    Contract.Requires(value > 0);
                }

                [SharpProofTrusted("reviewed precondition-free implementation")]
                [EffectContract(
                    SharpProofEffect.None,
                    IsDeterministic = true,
                    Complete = true,
                    PreconditionFree = true)]
                public static void Certified(int value) {
                }
            }

            public sealed class Service {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(
                    SharpProofEffect.None,
                    IsDeterministic = true,
                    Complete = true,
                    PreconditionFree = true)]
                public void Restricted(int value) {
                }
            }

            [ContractFor(typeof(Service))]
            public static class ServiceContracts {
                public static void Restricted(
                    Service receiver,
                    int value) {
                    Contract.Requires(value > 0);
                }
            }
            """,
            "ExternalSourcePreconditionAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void InvokeDirect(int value) =>
                    DirectBoundary.Restricted(value);

                public static void InvokeCompanion(
                    Service service,
                    int value) =>
                    service.Restricted(value);

                public static void InvokeCertified(int value) =>
                    DirectBoundary.Certified(value);
            }
            """,
            externalReference);
        var session = new EffectAnalysisSession(compilation);

        var direct = session.Analyze(Method(compilation, "InvokeDirect"));
        var companion = session.Analyze(Method(compilation, "InvokeCompanion"));
        var certified = session.Analyze(Method(compilation, "InvokeCertified"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                direct.Summary.AnalysisIncompleteReason,
                Is.EqualTo(
                    EffectAnalysisIncompleteReason
                        .CallPreconditionNotProven));
            Assert.That(direct.Projection.IsComplete, Is.False);
            Assert.That(
                companion.Summary.AnalysisIncompleteReason,
                Is.EqualTo(
                    EffectAnalysisIncompleteReason
                        .CallPreconditionNotProven));
            Assert.That(companion.Projection.IsComplete, Is.False);
            Assert.That(
                certified.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(certified.Projection.IsComplete, Is.True);
        }
    }

    [Test]
    public void StandaloneCompanionPreconditionIntentFailsClosed()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using SharpProof.Attributes;

            public sealed class Service {
                public void Restricted(int value) {
                }
            }

            [ContractFor(typeof(Service))]
            public static class ServiceContracts {
                public static void Restricted(
                    Service receiver,
                    int value) {
                    Contract.Requires(value > 0);
                }
            }

            public static class Sample {
                public static void Invoke(
                    Service service,
                    int value) =>
                    service.Restricted(value);
            }
            """);

        var result = new EffectAnalysisSession(
            compilation).Analyze(
            Method(compilation, "Invoke"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(
                result.Summary.AnalysisIncompleteReason,
                Is.EqualTo(
                    EffectAnalysisIncompleteReason
                        .CallPreconditionNotProven));
            Assert.That(
                result.Projection.IsComplete,
                Is.False);
        }
    }

    [Test]
    public void TrustedCompleteBodylessSourceContractIsResolved()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Sample {
                [SharpProofTrusted("reviewed native implementation")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static extern void Boundary();

                public static void Invoke() => Boundary();
            }
            """);

        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Invoke"));

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Summary.Reads.IsEmpty, Is.True);
        Assert.That(result.Summary.Writes.IsEmpty, Is.True);
        Assert.That(result.Summary.Throws.IsEmpty, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnapprovedContractPackageCannotLendTrustedEffectEvidence(
        bool validContractShape)
    {
        var contractReference =
            EffectTestHost.EmitUnapprovedContractApiReference(
                validContractShape);
        var compilation =
            EffectTestHost.CreateCompilationWithoutContractPackage(
                """
                public static class Sample {
                    [SharpProof.Attributes.SharpProofTrusted(
                        "reviewed native implementation")]
                    [SharpProof.Attributes.EffectContract(
                        SharpProof.Attributes.SharpProofEffect.None,
                        Complete = true)]
                    public static extern void Boundary();

                    public static void Invoke() => Boundary();
                }
                """,
                contractReference);
        var boundary = Method(compilation, "Boundary");

        var resolution = new ExternalEffectResolver(
            compilation,
            ApiSpecTable.Default).ResolveContract(boundary);
        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Invoke"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                resolution.Kind,
                Is.EqualTo(EffectContractResolutionKind.Missing));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(result.Summary.Reads.IsUnknown, Is.True);
            Assert.That(result.Summary.Writes.IsUnknown, Is.True);
            Assert.That(result.Summary.Throws.IncludesUnknown, Is.True);
            Assert.That(result.Projection.IsComplete, Is.False);
        }
    }

    [Test]
    public void SourceShadowedExternalEffectEvidenceIsRejected()
    {
        var fakeContract = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            namespace SharpProof.Attributes {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class EffectContractAttribute :
                    System.Attribute {
                    public EffectContractAttribute(
                        SharpProofEffect effects) {
                    }

                    public bool Complete {
                        get;
                        set;
                    }
                }
            }

            public static class ExternalFixture {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Boundary() {
                }
            }
            """);
        var fakeTrust = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            namespace SharpProof.Attributes {
                [System.AttributeUsage(System.AttributeTargets.Method)]
                public sealed class SharpProofTrustedAttribute :
                    System.Attribute {
                    public SharpProofTrustedAttribute(string reason) {
                    }
                }
            }

            public static class ExternalFixture {
                [SharpProofTrusted("reviewed external implementation")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Boundary() {
                }
            }
            """);

        var rejectedContract = new ExternalEffectResolver(
            fakeContract,
            ApiSpecTable.Default).ResolveContract(
                EffectTestHost.RequireMethod(
                    fakeContract,
                    "ExternalFixture",
                    "Boundary"));
        var rejectedTrust = new ExternalEffectResolver(
            fakeTrust,
            ApiSpecTable.Default).ResolveContract(
                EffectTestHost.RequireMethod(
                    fakeTrust,
                    "ExternalFixture",
                    "Boundary"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                rejectedContract.Kind,
                Is.EqualTo(EffectContractResolutionKind.Missing));
            Assert.That(
                rejectedTrust.Kind,
                Is.EqualTo(EffectContractResolutionKind.Untrusted));
        }
    }

    [Test]
    public void ExternalSummaryRequiresBothTrustAndCompleteContract()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            public static class ExternalTrustFixture {
                public static void Neither() {
                }

                [SharpProofTrusted("reviewed implementation")]
                public static void TrustOnly() {
                }

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void ContractOnly() {
                }

                [SharpProofTrusted("reviewed implementation")]
                [EffectContract(
                    SharpProofEffect.None,
                    Complete = true,
                    PreconditionFree = true)]
                public static void Both() {
                }

                [SharpProofTrusted("reviewed implementation")]
                [EffectContract(SharpProofEffect.None, Complete = false)]
                public static void Incomplete() {
                }

                [SharpProofTrusted("reviewed implementation")]
                [EffectContract(SharpProofEffect.None)]
                public static void ImplicitDefaults() {
                }

                [SharpProofTrusted(" ")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void InvalidReason() {
                }
            }
            """,
            "ExternalTrustFixtureAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            "public static class Sample { }",
            externalReference);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] {
                     "Neither",
                     "TrustOnly",
                     "ContractOnly",
                     "Incomplete",
                     "ImplicitDefaults",
                     "InvalidReason"
                 })
        {
            var result = session.Analyze(
                EffectTestHost.RequireMethod(
                    compilation,
                    "ExternalTrustFixture",
                    methodName));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                methodName);
            Assert.That(result.Summary.Reads.IsUnknown, Is.True, methodName);
            Assert.That(result.Summary.Writes.IsUnknown, Is.True, methodName);
            Assert.That(result.Summary.Throws.IncludesUnknown, Is.True, methodName);
            Assert.That(result.Projection.IsComplete, Is.False, methodName);
        }

        var accepted = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "ExternalTrustFixture",
                "Both"));
        Assert.That(
            accepted.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(accepted.Summary.Reads.IsEmpty, Is.True);
        Assert.That(accepted.Summary.Writes.IsEmpty, Is.True);
        Assert.That(accepted.Summary.Throws.IsEmpty, Is.True);
        Assert.That(accepted.Projection.IsComplete, Is.True);
    }

    [Test]
    public void VirtualPropertyAndInterfaceIndexerDispatchFailClosed()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            using SharpProof.Attributes;

            [SharpProofTrusted("reviewed external type")]
            public class ExternalBase {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public virtual int Value => 1;
            }

            [SharpProofTrusted("reviewed external interface")]
            public interface IExternalIndex {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                int this[int index] { set; }
            }
            """,
            "ExternalPropertyFixtureAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static int Read(ExternalBase value) => value.Value;
                public static void Write(IExternalIndex value) => value[0] = 1;
            }
            """,
            externalReference);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] { "Read", "Write" })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete),
                methodName);
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.Dispatch,
                Is.EqualTo(EffectUncertainty.Dispatch),
                methodName);
            Assert.That(result.Projection.IsComplete, Is.False, methodName);
        }
    }

    [Test]
    public void PropertyDispatchUsesTheOperationReceiver()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public class Base {
                private int _value;
                public virtual int Value {
                    get { return _value; }
                    set { _value = value; }
                }
                public virtual int this[int index] {
                    get { return _value; }
                    set { _value = value; }
                }
                public virtual int GetValue() => _value;
            }

            public class Derived : Base {
                public int BaseGet() => base.Value;
                public void BaseSet(int value) => base.Value = value;
                public int BaseIndexerGet() => base[0];
                public void BaseIndexerSet(int value) => base[0] = value;
                public int BaseMethod() => base.GetValue();
                public int ThisVirtual() => this.Value;
            }

            public sealed class SealedDerived : Base {
                public override int Value {
                    get { return base.Value; }
                    set { base.Value = value; }
                }
                public int Read() => Value;
            }

            public sealed class SealedInherited : Base {
                public int Read() => Value;
            }

            public sealed class NonVirtual {
                private int _value;
                public int Value {
                    get { return _value; }
                    set { _value = value; }
                }
                public int Read() => Value;
            }

            public interface IValue {
                int Value { get; }
            }

            public static class Sample {
                public static int Interface(IValue value) => value.Value;
                public static int Conditional(Derived? value) => value?.Value ?? 0;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        foreach (var (type, method) in new[] {
                     ("Derived", "BaseGet"),
                     ("Derived", "BaseSet"),
                     ("Derived", "BaseIndexerGet"),
                     ("Derived", "BaseIndexerSet"),
                     ("Derived", "BaseMethod"),
                     ("SealedDerived", "Read"),
                     ("SealedInherited", "Read"),
                     ("NonVirtual", "Read")
                 })
        {
            var result = session.Analyze(
                EffectTestHost.RequireMethod(compilation, type, method));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete),
                type + "." + method);
            Assert.That(result.Projection.IsComplete, Is.True, type + "." + method);
        }

        foreach (var (type, method) in new[] {
                     ("Derived", "ThisVirtual"),
                     ("Sample", "Interface"),
                     ("Sample", "Conditional")
                 })
        {
            var result = session.Analyze(
                EffectTestHost.RequireMethod(compilation, type, method));
            Assert.That(
                result.Summary.Uncertainty & EffectUncertainty.Dispatch,
                Is.EqualTo(EffectUncertainty.Dispatch),
                type + "." + method);
            Assert.That(result.Projection.IsComplete, Is.False, type + "." + method);
        }
    }

    [Test]
    public void ExplicitAndImplicitExceptionsRemainResolved()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                public static void Explicit(Exception exception) => throw exception;
                public static int Divide(int left, int right) => left / right;
                public static int Remainder(int left, int right) => left % right;
                public static int? NullableDivide(int? left, int? right) =>
                    left / right;
                public static int? NullableRemainder(int? left, int? right) =>
                    left % right;
                public static uint? NullableUnsignedDivide(
                    uint? left,
                    uint? right) => left / right;
                public static uint? NullableUnsignedRemainder(
                    uint? left,
                    uint? right) => left % right;
                public static nint NativeDivide(nint left, nint right) =>
                    left / right;
                public static nint NativeRemainder(nint left, nint right) =>
                    left % right;
                public static nuint NativeUnsignedDivide(
                    nuint left,
                    nuint right) => left / right;
                public static nuint NativeUnsignedRemainder(
                    nuint left,
                    nuint right) => left % right;
                public static int CompoundDivide(int left, int right) {
                    left /= right;
                    return left;
                }
                public static int CompoundRemainder(int left, int right) {
                    left %= right;
                    return left;
                }
                public static int Length(string text) => text.Length;
                public static int Index(int[] values, int index) => values[index];
                public static int CheckedAdd(int left, int right) =>
                    checked(left + right);
                public static int CheckedIncrement(int value) {
                    checked {
                        value++;
                    }
                    return value;
                }
                public static int[] Array(int length) => new int[length];
                public static int[] FixedArray() => new int[1];
                public static void Lock(object gate) {
                    lock (gate) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertThrows(
            session.Analyze(Method(compilation, "Explicit")).Summary,
            "System.Exception");
        AssertThrows(
            session.Analyze(Method(compilation, "Divide")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "Remainder")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NullableDivide")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NullableRemainder")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NullableUnsignedDivide")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "NullableUnsignedRemainder")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "NativeDivide")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NativeRemainder")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NativeUnsignedDivide")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "NativeUnsignedRemainder")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "CompoundDivide")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "CompoundRemainder")).Summary,
            "System.DivideByZeroException",
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "Length")).Summary,
            "System.NullReferenceException");
        AssertThrows(
            session.Analyze(Method(compilation, "Index")).Summary,
            "System.NullReferenceException",
            "System.IndexOutOfRangeException");
        AssertThrows(
            session.Analyze(Method(compilation, "CheckedAdd")).Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "CheckedIncrement")).Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "Array")).Summary,
            "System.OverflowException");
        var lockSummary = session.Analyze(
            Method(compilation, "Lock")).Summary;
        AssertContainsThrows(
            lockSummary,
            "System.ArgumentNullException");
        Assert.That(
            lockSummary.Capabilities.Contains(
                EffectCapabilityKind.Synchronization),
            Is.True);
        Assert.That(
            lockSummary.Uncertainty,
            Is.EqualTo(EffectUncertainty.None));
        Assert.That(
            lockSummary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(
            session.Analyze(Method(compilation, "FixedArray"))
                .Summary.Throws.IsEmpty,
            Is.True);
    }

    [Test]
    public void PossiblyNullThrownExpressionIncludesNullReferenceExceptionUnlessRequiredNonNull()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using System;
            using SharpProof.Attributes;

            public static class Sample {
                public static void MaybeNull(
                    InvalidOperationException? exception) =>
                    throw exception;

                public static void RequiredNonNull(
                    InvalidOperationException? exception) {
                    Contract.Requires(exception != null);
                    throw exception;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var maybeNull = session.Analyze(
            Method(compilation, "MaybeNull")).Summary;
        var requiredNonNull = session.Analyze(
            Method(compilation, "RequiredNonNull")).Summary;

        AssertThrows(
            maybeNull,
            "System.InvalidOperationException",
            "System.NullReferenceException");
        AssertThrows(
            requiredNonNull,
            "System.InvalidOperationException");
        AssertDoesNotThrow(
            requiredNonNull,
            "System.NullReferenceException");
    }

    [Test]
    public void DefinitelyNullThrownExpressionsReplaceTheirDeclaredExceptionType()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using System;

            public static class Sample {
                private static int s_state;
                private static InvalidOperationException? s_exception;

                public static void NullLiteral() => throw null!;

                public static void NullLocal() {
                    InvalidOperationException? exception = null;
                    throw exception!;
                }

                public static void NullBranch(bool condition) {
                    InvalidOperationException? exception;
                    if (condition) {
                        exception = null;
                    }
                    else {
                        exception = null;
                    }
                    throw exception;
                }

                public static void NonNullLocal() {
                    InvalidOperationException? exception =
                        new InvalidOperationException();
                    throw exception;
                }

                public static void NullConditional(bool condition) {
                    InvalidOperationException? left = null;
                    InvalidOperationException? right = null;
                    throw (condition ? left : right);
                }

                public static void NullConversion() {
                    Exception? exception = null;
                    throw (InvalidOperationException?)exception;
                }

                public static void MaybeNull(
                    InvalidOperationException? exception) =>
                    throw exception;

                public static void Field() => throw s_exception;

                public static void Coalesced(
                    InvalidOperationException? exception) =>
                    throw (exception ?? new InvalidOperationException());

                public static void NullCatchReachability() {
                    try {
                        InvalidOperationException? exception = null;
                        throw exception;
                    }
                    catch (InvalidOperationException) {
                        s_state++;
                    }
                    catch (NullReferenceException) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "NullLiteral",
                     "NullLocal",
                     "NullBranch",
                     "NullConditional",
                     "NullConversion"
                 })
        {
            var summary = session.Analyze(Method(compilation, methodName)).Summary;
            AssertThrows(summary, "System.NullReferenceException");
            AssertDoesNotThrow(summary, "System.InvalidOperationException");
        }

        var nonNull = session.Analyze(Method(compilation, "NonNullLocal")).Summary;
        AssertThrows(nonNull, "System.InvalidOperationException");
        AssertDoesNotThrow(nonNull, "System.NullReferenceException");

        AssertThrows(
            session.Analyze(Method(compilation, "MaybeNull")).Summary,
            "System.InvalidOperationException",
            "System.NullReferenceException");
        AssertThrows(
            session.Analyze(Method(compilation, "Field")).Summary,
            "System.InvalidOperationException",
            "System.NullReferenceException");
        AssertThrows(
            session.Analyze(Method(compilation, "Coalesced")).Summary,
            "System.InvalidOperationException");
        Assert.That(
            session.Analyze(Method(compilation, "NullCatchReachability"))
                .Summary.Writes.Contains(EffectRegionId.Static()),
            Is.False);
    }

    [Test]
    public void ApiSpecMakesModeledExternalCallComplete()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static int Absolute(int value) => System.Math.Abs(value);
            }
            """,
            "Sample",
            "Absolute");

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(result.Summary.Reads.IsEmpty, Is.True);
        Assert.That(result.Summary.Writes.IsEmpty, Is.True);
        AssertThrows(result.Summary, "System.OverflowException");
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(SharpProofEffect.Throws));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void CompilerElisionSkipsGhostArgumentsButDirectIntrinsicsThrow()
    {
        const string source =
            """
            using SharpProof.Attributes;

            public static class Sample {
                public static int Elided(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() == Contract.Old(value));
                    return value;
                }

                public static int DirectResult() => Contract.Result<int>();
                public static int DirectOld(int value) => Contract.Old(value);
            }
            """;
        var compilation = EffectTestHost.CreateCompilation(source);
        var session = new EffectAnalysisSession(compilation);
        var directResult = session.Analyze(
            Method(compilation, "DirectResult")).Summary;
        var directOld = session.Analyze(
            Method(compilation, "DirectOld")).Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "Elided"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            AssertThrows(
                directResult,
                "System.InvalidOperationException");
            AssertThrows(
                directOld,
                "System.InvalidOperationException");
            Assert.That(
                directResult.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                directOld.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
        }

        var enabledTree = CSharpSyntaxTree.ParseText(
            source,
            CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.CSharp12)
                .WithPreprocessorSymbols(Contract.ConditionalSymbol),
            path: "EffectsContractsEnabled.cs");
        var enabledCompilation = EffectTestHost.CreateCompilation(
            [enabledTree],
            "EffectsContractsEnabled");
        var enabled = new EffectAnalysisSession(enabledCompilation).Analyze(
            Method(enabledCompilation, "Elided")).Summary;

        AssertThrows(enabled, "System.InvalidOperationException");
        Assert.That(
            enabled.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));

        var directiveCompilation = EffectTestHost.CreateCompilation(
            "#define " + Contract.ConditionalSymbol +
            Environment.NewLine +
            source);
        var directiveEnabled = new EffectAnalysisSession(
            directiveCompilation).Analyze(
            Method(directiveCompilation, "Elided")).Summary;

        AssertThrows(
            directiveEnabled,
            "System.InvalidOperationException");
        Assert.That(
            directiveEnabled.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
    }

    [Test]
    public void UnmodeledMetadataCallFailsClosed()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static System.Guid CreateGuid() => System.Guid.NewGuid();
            }
            """,
            "Sample",
            "CreateGuid");

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
        Assert.That(result.Summary.Reads.IsUnknown, Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.True);
        Assert.That(result.Summary.Throws.IncludesUnknown, Is.True);
        Assert.That(
            result.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
            Is.EqualTo(EffectUncertainty.UnmodeledCall));
        Assert.That(result.Projection.IsComplete, Is.False);
    }

    [Test]
    public void SourceSummaryRemapsParameterWritesAtDepthZero()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public static class Sample {
                private static void Mutate(Box value) => value.Value = 1;
                public static void Invoke(Box value) => Mutate(value);
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Invoke"));

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.False);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        AssertThrows(result.Summary, "System.NullReferenceException");
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(
                SharpProofEffect.WritesArgumentState |
                SharpProofEffect.Throws));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void ReducedSourceExtensionRemapsItsReceiverArgument()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public static class BoxExtensions {
                public static void Mutate(this Box value) => value.Value = 1;
            }

            public static class Sample {
                public static void Invoke(Box value) => value.Mutate();
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Invoke"));

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.False);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
    }

    [Test]
    public void RefParameterWritesRemapToTheCaller()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static void Set(ref int value) => value = 1;
                public static void Invoke(ref int value) => Set(ref value);
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Invoke"));

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.False);
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
    }

    [Test]
    public void ByValueStructMutationStaysOnTheLocalCopy()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public struct Counter {
                public int Value;
                public void ClearValue() => Value = 0;
            }

            public static class Sample {
                public static void WriteCopyField(Counter value) =>
                    value.Value = 0;
                public static void MutateCopy(Counter value) =>
                    value.ClearValue();
                public static void WriteRef(ref Counter value) =>
                    value.Value = 0;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var fieldCopy = session.Analyze(
            Method(compilation, "WriteCopyField")).Summary;
        var mutatorCopy = session.Analyze(
            Method(compilation, "MutateCopy")).Summary;
        var byReference = session.Analyze(
            Method(compilation, "WriteRef")).Summary;
        var mutableThis = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Counter",
                "ClearValue")).Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(fieldCopy.Writes.IsEmpty, Is.True);
            Assert.That(fieldCopy.Completeness, Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(mutatorCopy.Writes.IsEmpty, Is.True);
            Assert.That(mutatorCopy.Completeness, Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                byReference.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(
                mutableThis.Writes.Contains(EffectRegionId.Receiver),
                Is.True);
        }
    }

    [Test]
    public void UnboxingCreatesAValueOwnedCopyWithoutDroppingReferenceAliases()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public struct Counter {
                public int Number;
            }

            public sealed class Box {
                public int Number;
            }

            public sealed class Holder {
                public object Value = null!;
            }

            public interface IBox {
                int Number { get; set; }
            }

            public struct InterfaceCounter : IBox {
                public int Number { get; set; }
            }

            public sealed class BoxWithInterface : IBox {
                public int Number { get; set; }
            }

            public static class Sample {
                public static void BoxedArgument(object value) {
                    var copy = (Counter)value;
                    copy.Number = 1;
                }

                public static void BoxedField(Holder value) {
                    var copy = (Counter)value.Value;
                    copy.Number = 1;
                }

                public static void NullableUnbox(Counter? value) {
                    var copy = (Counter)value;
                    copy.Number = 1;
                }

                public static void RefUnbox(ref object value) {
                    var copy = (Counter)value;
                    copy.Number = 1;
                }

                public static void ReferenceCast(Box value) {
                    var alias = (Box)(object)value;
                    alias.Number = 1;
                }

                public static void InterfaceReferenceCast(BoxWithInterface value) {
                    var alias = (BoxWithInterface)(IBox)value;
                    alias.Number = 1;
                }

                public static object InterfaceBoxing(InterfaceCounter value) => (IBox)value;

                public static void ByValue(Counter value) => value.Number = 1;
                public static void ByRef(ref Counter value) => value.Number = 1;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var valueOwned = new[]
        {
            "BoxedArgument",
            "BoxedField",
            "NullableUnbox",
            "RefUnbox",
            "ByValue"
        };
        foreach (var methodName in valueOwned)
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Writes.IsEmpty,
                Is.True,
                methodName);
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.True,
                methodName);
        }

        foreach (var methodName in new[] { "ReferenceCast", "InterfaceReferenceCast" })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True,
                methodName);
        }

        var byReference = session.Analyze(Method(compilation, "ByRef"));
        Assert.That(
            byReference.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);

        var interfaceBoxing = session.Analyze(
            Method(compilation, "InterfaceBoxing"));
        Assert.That(interfaceBoxing.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(interfaceBoxing.Summary.Writes.IsEmpty, Is.True);
    }

    [Test]
    public void RecursiveSccStartsConservative()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static void Recur() => Recur();
            }
            """,
            "Sample",
            "Recur");

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
        Assert.That(result.Summary.Reads.IsUnknown, Is.True);
        Assert.That(result.Summary.Writes.IsUnknown, Is.True);
        Assert.That(
            result.Summary.Uncertainty & EffectUncertainty.Recursion,
            Is.EqualTo(EffectUncertainty.Recursion));
        Assert.That(result.Projection.IsComplete, Is.False);
    }

    [Test]
    public void ReachableControlFlowCycleMayDiverge()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static void Loop(bool keepGoing) {
                    while (keepGoing) {
                    }
                }
            }
            """,
            "Sample",
            "Loop");

        Assert.That(
            result.Summary.Termination,
            Is.EqualTo(EffectTermination.MayDiverge));
        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(
            result.Summary.AnalysisIncompleteReason,
            Is.EqualTo(EffectAnalysisIncompleteReason.None));
        Assert.That(result.Projection.IsComplete, Is.True);
    }

    [Test]
    public void CompileTimeLoopConditionsControlReachabilityAndTermination()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static object? Stored;

                public static void WhileFalse() {
                    while (false) {
                        Stored = new object();
                        throw new System.Exception();
                    }
                }

                public static void ForFalse() {
                    for (; false;) {
                        Stored = new object();
                    }
                }

                public static void WhileTrue() {
                    while (true) {
                    }
                }

                public static void WhileUnknown(bool condition) {
                    while (condition) {
                    }
                }

                public static void DoFalse() {
                    do {
                        Stored = new object();
                    } while (false);
                }

                public static void DoTrue() {
                    do {
                    } while (true);
                }

                public static void FalseInsideUnknown(bool condition) {
                    while (condition) {
                        while (false) {
                            Stored = new object();
                            throw new System.Exception();
                        }
                    }
                }

                public static void UnknownInsideFalse(bool condition) {
                    while (false) {
                        while (condition) {
                            Stored = new object();
                        }
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var whileFalse = session.Analyze(Method(compilation, "WhileFalse")).Summary;
        var forFalse = session.Analyze(Method(compilation, "ForFalse")).Summary;
        var whileTrue = session.Analyze(Method(compilation, "WhileTrue")).Summary;
        var whileUnknown = session.Analyze(Method(compilation, "WhileUnknown")).Summary;
        var doFalse = session.Analyze(Method(compilation, "DoFalse")).Summary;
        var doTrue = session.Analyze(Method(compilation, "DoTrue")).Summary;
        var falseInsideUnknown = session.Analyze(
            Method(compilation, "FalseInsideUnknown")).Summary;
        var unknownInsideFalse = session.Analyze(
            Method(compilation, "UnknownInsideFalse")).Summary;

        using (Assert.EnterMultipleScope())
        {
            AssertNoEffectsAndTerminates(whileFalse);
            AssertNoEffectsAndTerminates(forFalse);
            AssertNoEffectsAndTerminates(unknownInsideFalse);
            Assert.That(whileTrue.Termination, Is.EqualTo(EffectTermination.MayDiverge));
            Assert.That(whileUnknown.Termination, Is.EqualTo(EffectTermination.MayDiverge));
            Assert.That(doTrue.Termination, Is.EqualTo(EffectTermination.MayDiverge));
            Assert.That(doFalse.Termination, Is.EqualTo(EffectTermination.Terminates));
            Assert.That(doFalse.Writes.IsEmpty, Is.False);
            Assert.That(doFalse.Allocation, Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(falseInsideUnknown.Termination, Is.EqualTo(EffectTermination.MayDiverge));
            Assert.That(falseInsideUnknown.Writes.IsEmpty, Is.True);
            Assert.That(falseInsideUnknown.Allocation, Is.EqualTo(EffectAllocationKind.None));
            Assert.That(falseInsideUnknown.Throws.IsEmpty, Is.True);
        }
    }

    private static void AssertNoEffectsAndTerminates(EffectSummary summary)
    {
        Assert.That(summary.Termination, Is.EqualTo(EffectTermination.Terminates));
        Assert.That(summary.Writes.IsEmpty, Is.True);
        Assert.That(summary.Allocation, Is.EqualTo(EffectAllocationKind.None));
        Assert.That(summary.Throws.IsEmpty, Is.True);
    }

    [Test]
    public void ScalarImpossibleBranchDoesNotContributeEffects()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static object? Allocate(int value) {
                    if (value > 0 && value < 0) {
                        return new object();
                    }
                    return null;
                }
            }
            """,
            "Sample",
            "Allocate");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Projection.Effects & SharpProofEffect.Allocates,
                Is.EqualTo(SharpProofEffect.None));
            Assert.That(result.Projection.IsComplete, Is.True);
            Assert.That(
                result.Summary.AnalysisIncompleteReason,
                Is.EqualTo(EffectAnalysisIncompleteReason.None));
        }
    }

    [Test]
    public void ReferenceArrayStoreRetainsAllImplicitExceptions()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static void Store(object[] values, object value) =>
                    values[0] = value;
            }
            """,
            "Sample",
            "Store");

        AssertThrows(
            result.Summary,
            "System.NullReferenceException",
            "System.IndexOutOfRangeException",
            "System.ArrayTypeMismatchException");
        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.True);
    }

    [Test]
    public void SealedReferenceArrayStoreOmitsArrayTypeMismatchException()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static void Store(string[] values, string value) =>
                    values[0] = value;
            }
            """,
            "Sample",
            "Store");

        AssertThrows(
            result.Summary,
            "System.NullReferenceException",
            "System.IndexOutOfRangeException");
        AssertDoesNotThrow(
            result.Summary,
            "System.ArrayTypeMismatchException");
    }

    [Test]
    public void DefinitelyNullReferenceArrayStoreOmitsArrayTypeMismatchException()
    {
        var result = Analyze(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Sample {
                public static void Store(object[] values, object? value) {
                    Contract.Requires(value is null);
                    values[0] = value;
                }
            }
            """,
            "Sample",
            "Store");

        AssertThrows(
            result.Summary,
            "System.NullReferenceException",
            "System.IndexOutOfRangeException");
        AssertDoesNotThrow(
            result.Summary,
            "System.ArrayTypeMismatchException");
    }

    [Test]
    public void ArrayStoreCompatibilityUsesExactFreshRuntimeElementType()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public interface IValue { }
            public sealed class Value : IValue { }
            public class Base { }
            public sealed class Derived : Base { }
            public static class Sample {
                private static object[] s_field = new object[1];
                public static void FreshObject() { object[] values = new object[1]; values[0] = "value"; }
                public static void LocalAlias() { object[] values = new object[1]; object[] alias = values; alias[0] = "value"; }
                public static void FreshBase() { Base[] values = new Base[1]; values[0] = new Derived(); }
                public static void FreshInterface() { IValue[] values = new IValue[1]; values[0] = new Value(); }
                public static void FreshBoxing() { object[] values = new object[1]; values[0] = 1; }
                public static void Covariant() { object[] values = new string[1]; values[0] = new object(); }
                public static void UnknownParameter(object[] values, string value) { values[0] = value; }
                public static void FieldAlias(string value) { s_field[0] = value; }
                public static void CovariantNull() { object[] values = new string[1]; values[0] = null; }
                public static void ValueArray() { int[] values = new int[1]; values[0] = 1; }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            AssertCompatible("FreshObject");
            AssertCompatible("LocalAlias");
            AssertCompatible("FreshBase");
            AssertCompatible("FreshInterface");
            AssertCompatible("FreshBoxing");
            AssertIncompatible("Covariant");
            AssertIncompatible("UnknownParameter");
            AssertIncompatible("FieldAlias");
            AssertCompatible("CovariantNull");
            AssertCompatible("ValueArray");
        }

        void AssertCompatible(string methodName)
        {
            AssertDoesNotThrow(
                session.Analyze(Method(compilation, methodName)).Summary,
                "System.ArrayTypeMismatchException");
        }

        void AssertIncompatible(string methodName)
        {
            AssertContainsThrows(
                session.Analyze(Method(compilation, methodName)).Summary,
                "System.ArrayTypeMismatchException");
        }
    }

    [Test]
    public void ResolvedNullReceiverThrowSurvivesUnknownDispatch()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static string Invoke(object value) => value.ToString();
            }
            """,
            "Sample",
            "Invoke");

        Assert.That(result.Summary.Throws.IncludesUnknown, Is.True);
        AssertContainsThrows(result.Summary, "System.NullReferenceException");
        Assert.That(
            result.Projection.Effects & SharpProofEffect.Throws,
            Is.EqualTo(SharpProofEffect.Throws));
        Assert.That(result.Projection.IsComplete, Is.False);
    }

    [Test]
    public void DefinitelyNonNullReceiverDoesNotAddNullReferenceException()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static int Invoke() => new object().GetHashCode();
            }
            """,
            "Sample",
            "Invoke");

        Assert.That(
            result.Summary.Throws.Types.Select(static type => type.MetadataName),
            Does.Not.Contain("NullReferenceException"));
    }

    [Test]
    public void TryCastReceiverCanStillThrowNullReferenceException()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static int Invoke() =>
                    (new object() as string)!.Length;
            }
            """,
            "Sample",
            "Invoke");

        Assert.That(
            result.Summary.Throws.Types.Select(static type => type.MetadataName),
            Does.Contain("NullReferenceException"));
    }

    [Test]
    public void SourceEffectContractCannotOverrideTheBody()
    {
        var result = Analyze(
            """
            using SharpProof.Attributes;

            public static class Sample {
                private static int s_value;

                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Write() => s_value = 1;
            }
            """,
            "Sample",
            "Write");

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
        Assert.That(
            result.Projection.Effects,
            Is.EqualTo(SharpProofEffect.WritesStaticState));
    }

    [Test]
    public void UntrustedSourceContractRetainsItsDecodedSummaryForChecking()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Sample {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static void Empty() {
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var resolution = session.ResolveExternalContract(
            Method(compilation, "Empty"));

        Assert.That(
            resolution.Kind,
            Is.EqualTo(EffectContractResolutionKind.Untrusted));
        Assert.That(
            resolution.Summary.Completeness,
            Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(resolution.Summary.Reads.IsEmpty, Is.True);
        Assert.That(resolution.Summary.Writes.IsEmpty, Is.True);
        Assert.That(
            resolution.Summary.Capabilities.Contains(
                EffectCapabilityKind.Randomness),
            Is.True);
    }

    [Test]
    public void AnalyzeBuildsOnlyTheRequestedReachableCallGraph()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int Reachable(int value) => value + 1;
                public static int Selected(int value) => Reachable(value);
                public static int Unselected(int value) => value - 1;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var selected = Method(compilation, "Selected");

        var first = session.Analyze(selected);
        var second = session.Analyze(selected);

        Assert.That(session.AnalyzedSourceMethodCount, Is.EqualTo(2));
        Assert.That(ReferenceEquals(first.Summary, second.Summary), Is.True);

        session.Analyze(Method(compilation, "Unselected"));

        Assert.That(session.AnalyzedSourceMethodCount, Is.EqualTo(3));
        Assert.That(session.AnalyzeAll(), Has.Length.EqualTo(3));
    }

    [Test]
    public void AnalyzeAllOrderAndSummariesAreDeterministic()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Zeta {
                public static int Last(int value) => Alpha.First(value);
            }

            public static class Alpha {
                public static int First(int value) => value + 1;
                public static int Second(int value) => First(value) + 1;
            }
            """);

        var first = new EffectAnalysisSession(compilation).AnalyzeAll();
        var second = new EffectAnalysisSession(compilation).AnalyzeAll();

        Assert.That(
            second.Select(ResultKey),
            Is.EqualTo(first.Select(ResultKey)));
        Assert.That(
            second.Select(static result => result.Summary),
            Is.EqualTo(first.Select(static result => result.Summary)));
        Assert.That(
            second.Select(static result => result.Projection),
            Is.EqualTo(first.Select(static result => result.Projection)));
    }

    [Test]
    public void AnalyzeAllIncludesTheSameDirectWitnessesAsAnalyze()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static object Allocate() => new object();
            }
            """);
        var method = Method(compilation, "Allocate");
        var session = new EffectAnalysisSession(compilation);

        var direct = session.Analyze(method).DirectWitnesses;
        var all = session.AnalyzeAll().Single(
            result => SymbolEqualityComparer.Default.Equals(result.Method, method));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(direct.Length, Is.EqualTo(1));
            Assert.That(direct[0].Kind, Is.EqualTo("managed-allocation"));
            Assert.That(all.DirectWitnesses, Is.EqualTo(direct));
        }
    }

    [Test]
    public void ColdConcurrentAnalysisPublishesOneDeterministicCache()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_value;

                private static void Write(int value) => s_value = value;
                public static void Invoke(int value) => Write(value);
            }
            """);
        var method = Method(compilation, "Invoke");
        var session = new EffectAnalysisSession(compilation);
        var results = new EffectMethodResult?[64];

        System.Threading.Tasks.Parallel.For(
            0,
            results.Length,
            index => results[index] = session.Analyze(method));

        var expected = results[0] ??
                       throw new InvalidOperationException(
                           "Concurrent analysis produced no result.");
        foreach (var result in results)
        {
            Assert.That(result, Is.Not.Null);
            Assert.That(
                ReferenceEquals(result!.Summary, expected.Summary),
                Is.True);
            Assert.That(result.Projection, Is.EqualTo(expected.Projection));
        }
        Assert.That(
            session.AnalyzeAll().Select(ResultKey),
            Is.EqualTo(session.AnalyzeAll().Select(ResultKey)));
    }

    [Test]
    public void UnsupportedDynamicInvocationFailsClosed()
    {
        var result = Analyze(
            """
            public static class Sample {
                public static object? Invoke(dynamic value) => value();
            }
            """,
            "Sample",
            "Invoke");

        Assert.That(result.Summary.Completeness, Is.EqualTo(EffectCompleteness.Incomplete));
        Assert.That(
            result.Summary.Uncertainty & EffectUncertainty.UnsupportedOperation,
            Is.EqualTo(EffectUncertainty.UnsupportedOperation));
        Assert.That(result.Projection.IsComplete, Is.False);
    }

    [Test]
    public void ConditionalAccessDoesNotInventNullReceiverExceptions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Receiver {
                public int Value => 1;
                public int GetValue() => 1;
                public Child? Child => null;
            }

            public sealed class Child {
                public int Value => 1;
            }

            public static class Sample {
                public static int? Read(Receiver? receiver) =>
                    receiver?.Value;

                public static int? Invoke(Receiver? receiver) =>
                    receiver?.GetValue();

                public static int? Nested(Receiver? receiver) =>
                    receiver?.Child.Value;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "Read"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "Invoke"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            AssertThrows(
                session.Analyze(Method(compilation, "Nested")).Summary,
                "System.NullReferenceException");
        }
    }

    [Test]
    public void NormallyCompletingCatchFlowsIntoPostTryEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                private static int s_state;

                public static int HandledThrow() {
                    var divisor = 1;
                    try {
                        throw new InvalidOperationException();
                    }
                    catch (InvalidOperationException) {
                        divisor = 0;
                    }

                    s_state++;
                    return 1 / divisor;
                }

                public static int NoThrowControl() {
                    var divisor = 1;
                    try {
                        divisor = 1;
                    }
                    catch (InvalidOperationException) {
                        divisor = 0;
                    }

                    return 1 / divisor;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var handled = session.Analyze(Method(compilation, "HandledThrow"));
        var control = session.Analyze(Method(compilation, "NoThrowControl"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                handled.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            AssertContainsThrows(
                handled.Summary,
                "System.DivideByZeroException");
            Assert.That(
                EffectContractMappings.IsObservablePure(handled.Summary),
                Is.False);
            Assert.That(
                handled.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(control.Summary.Throws.IsEmpty, Is.True);
        }
    }

    [Test]
    public void ExceptionHandlersContributeEffectsOnlyWhenReachable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            public interface IExternal { void Run(); }
            public static class Sample {
                private static int s_state;
                public static void EmptyTry() { try { } catch { s_state++; } }
                public static void NoThrowTry() { try { var value = 1; value++; } catch { s_state++; } }
                public static void KnownThrow() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) { s_state++; } }
                public static void UnknownThrow(IExternal external) { try { external.Run(); } catch (Exception) { s_state++; } }
                public static void FalseFilter() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) when (false) { s_state++; } }
                public static void TrueFilter() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) when (true) { s_state++; } }
                public static void OrderedHierarchy() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) { } catch (Exception) { s_state++; } }
                public static void Rethrow() { try { try { throw new InvalidOperationException(); } catch (InvalidOperationException) { throw; } } catch (Exception) { s_state++; } }
                public static void FinallyRuns() { try { } finally { s_state++; } }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(HasStaticWrite("EmptyTry"), Is.False);
            Assert.That(HasStaticWrite("NoThrowTry"), Is.False);
            Assert.That(HasStaticWrite("KnownThrow"), Is.True);
            Assert.That(HasStaticWrite("UnknownThrow"), Is.True);
            Assert.That(HasStaticWrite("FalseFilter"), Is.False);
            Assert.That(HasStaticWrite("TrueFilter"), Is.True);
            Assert.That(HasStaticWrite("OrderedHierarchy"), Is.False);
            Assert.That(HasStaticWrite("Rethrow"), Is.True);
            Assert.That(HasStaticWrite("FinallyRuns"), Is.True);
        }

        bool HasStaticWrite(string methodName)
        {
            return session.Analyze(Method(compilation, methodName))
                .Summary.Writes.Contains(EffectRegionId.Static());
        }
    }

    [Test]
    public void ExceptionFlowReportsOnlyExceptionsThatEscape()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Sample {
                private static void ThrowInvalid(
                    InvalidOperationException exception) {
                    Contract.Requires(exception != null);
                    throw exception;
                }

                private static bool ThrowFilter(
                    ApplicationException exception) =>
                    throw exception;

                public static void ExactCatch(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void BaseCatch(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (Exception) {
                    }
                }

                public static void TrueFilter(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) when (true) {
                    }
                }

                public static void FalseFilter(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) when (false) {
                    }
                }

                public static void Rethrow(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) {
                        throw;
                    }
                }

                public static void SiblingCatchAfterRethrow(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) {
                        throw;
                    }
                    catch (Exception) {
                    }
                }

                public static void FilteredSiblingCatchAfterRethrow(
                    InvalidOperationException exception,
                    bool rethrow) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) when (rethrow) {
                        throw;
                    }
                    catch (InvalidOperationException) {
                    }
                }

                public static void RuntimeSubtypeSiblingCatchAfterRethrow(
                    [NotNull] Exception exception) {
                    try {
                        throw exception;
                    }
                    catch (InvalidOperationException) {
                        throw;
                    }
                    catch (Exception) {
                    }
                }

                public static void ThrowingFilter(
                    InvalidOperationException exception,
                    ApplicationException filterException) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException)
                        when (ThrowFilter(filterException)) {
                    }
                }

                public static void NestedRethrow(
                    InvalidOperationException exception) {
                    try {
                        try {
                            ThrowInvalid(exception);
                        }
                        catch (InvalidOperationException) {
                            throw;
                        }
                    }
                    catch (Exception) {
                    }
                }

                public static void HandlerThrows(
                    InvalidOperationException exception,
                    ApplicationException handlerException) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) {
                        throw handlerException;
                    }
                }

                public static void ThrowFromFinally(
                    InvalidOperationException exception,
                    ApplicationException finallyException) {
                    try {
                        ThrowInvalid(exception);
                    }
                    catch (InvalidOperationException) {
                    }
                    finally {
                        throw finallyException;
                    }
                }

                public static void FinallyOverrides(
                    InvalidOperationException exception,
                    ApplicationException finallyException) {
                    try {
                        ThrowInvalid(exception);
                    }
                    finally {
                        throw finallyException;
                    }
                }

                public static void NonReturningFinally(
                    InvalidOperationException exception) {
                    try {
                        ThrowInvalid(exception);
                    }
                    finally {
                        while (true) {
                        }
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "ExactCatch"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "BaseCatch"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "TrueFilter"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            AssertThrows(
                session.Analyze(Method(compilation, "FalseFilter")).Summary,
                "System.InvalidOperationException");
            AssertThrows(
                session.Analyze(Method(compilation, "Rethrow")).Summary,
                "System.InvalidOperationException");
            AssertThrows(
                session.Analyze(Method(compilation, "SiblingCatchAfterRethrow")).Summary,
                "System.InvalidOperationException");
            AssertThrows(
                session.Analyze(Method(compilation, "FilteredSiblingCatchAfterRethrow")).Summary,
                "System.InvalidOperationException");
            AssertThrows(
                session.Analyze(Method(compilation, "RuntimeSubtypeSiblingCatchAfterRethrow")).Summary,
                "System.Exception");
            AssertThrows(
                session.Analyze(Method(compilation, "ThrowingFilter")).Summary,
                "System.InvalidOperationException");
            Assert.That(
                session.Analyze(Method(compilation, "NestedRethrow"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            AssertThrows(
                session.Analyze(Method(compilation, "HandlerThrows")).Summary,
                "System.ApplicationException");
            AssertThrows(
                session.Analyze(Method(compilation, "ThrowFromFinally")).Summary,
                "System.ApplicationException");
            AssertThrows(
                session.Analyze(Method(compilation, "FinallyOverrides")).Summary,
                "System.ApplicationException");
            Assert.That(
                session.Analyze(Method(compilation, "NonReturningFinally"))
                    .Summary.Throws.IsEmpty,
                Is.True);
        }
    }

    [Test]
    public void BareRethrowBelongsOnlyToItsNearestCatch()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Sample {
                private static void ThrowInvalid(InvalidOperationException value) {
                    Contract.Requires(value != null);
                    throw value;
                }
                private static void ThrowApplication(ApplicationException value) {
                    Contract.Requires(value != null);
                    throw value;
                }
                private static void ThrowArgument(ArgumentException value) {
                    Contract.Requires(value != null);
                    throw value;
                }

                public static void NestedCatch(
                    [NotNull] InvalidOperationException outer,
                    [NotNull] ApplicationException inner) {
                    try { ThrowInvalid(outer); }
                    catch (InvalidOperationException) {
                        try { ThrowApplication(inner); }
                        catch (ApplicationException) { throw; }
                    }
                }

                public static void DirectOuterRethrow(
                    [NotNull] InvalidOperationException outer,
                    [NotNull] ApplicationException inner) {
                    try { ThrowInvalid(outer); }
                    catch (InvalidOperationException) {
                        try { ThrowApplication(inner); }
                        catch (ApplicationException) { }
                        throw;
                    }
                }

                public static void MultipleNestedCatch(
                    [NotNull] InvalidOperationException outer,
                    [NotNull] ApplicationException middle,
                    [NotNull] ArgumentException inner) {
                    try { ThrowInvalid(outer); }
                    catch (InvalidOperationException) {
                        try { ThrowApplication(middle); }
                        catch (ApplicationException) {
                            try { ThrowArgument(inner); }
                            catch (ArgumentException) { throw; }
                        }
                    }
                }

                public static void NestedFilteredCatch(
                    [NotNull] InvalidOperationException outer,
                    [NotNull] ApplicationException inner,
                    bool selected) {
                    try { ThrowInvalid(outer); }
                    catch (InvalidOperationException) {
                        try { ThrowApplication(inner); }
                        catch (ApplicationException) when (selected) { throw; }
                        catch (ApplicationException) { }
                    }
                }

                public static void NestedFinally(
                    [NotNull] InvalidOperationException outer,
                    [NotNull] ApplicationException inner) {
                    try { ThrowInvalid(outer); }
                    catch (InvalidOperationException) {
                        try { ThrowApplication(inner); }
                        catch (ApplicationException) { throw; }
                        finally { _ = 1; }
                    }
                }

                public static void NestedCallableDeclarations(
                    [NotNull] InvalidOperationException outer) {
                    try { ThrowInvalid(outer); }
                    catch (InvalidOperationException) {
                        Action lambda = () => {
                            try { throw new ApplicationException(); }
                            catch (ApplicationException) { throw; }
                        };
                        static void Local() {
                            try { throw new ArgumentException(); }
                            catch (ArgumentException) { throw; }
                        }
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                ExceptionNames(session, compilation, "NestedCatch"),
                Is.EqualTo(["System.ApplicationException"]));
            AssertThrows(
                session.Analyze(Method(compilation, "DirectOuterRethrow")).Summary,
                "System.InvalidOperationException");
            Assert.That(
                ExceptionNames(session, compilation, "MultipleNestedCatch"),
                Is.EqualTo(["System.ArgumentException"]));
            Assert.That(
                ExceptionNames(session, compilation, "NestedFilteredCatch"),
                Is.EqualTo(["System.ApplicationException"]));
            Assert.That(
                ExceptionNames(session, compilation, "NestedFinally"),
                Is.EqualTo(["System.ApplicationException"]));
            Assert.That(
                session.Analyze(Method(compilation, "NestedCallableDeclarations"))
                    .Summary.Throws.Types.IsEmpty,
                Is.True);
        }

        static string[] ExceptionNames(
            EffectAnalysisSession session,
            CSharpCompilation compilation,
            string method)
        {
            return [.. session.Analyze(Method(compilation, method)).Summary.Throws.Types
                .Select(static type =>
                    type.ContainingNamespace.MetadataName + "." + type.MetadataName)];
        }
    }

    [Test]
    public void CatchVariableFlowUsesTheEffectDiscoveryCatalog()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                public static Exception Capture() {
                    try {
                        throw new Exception();
                    }
                    catch (Exception caught) {
                        return caught;
                    }
                }
            }
            """);

        var result = new EffectAnalysisSession(compilation)
            .Analyze(Method(compilation, "Capture"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Uncertainty &
                EffectUncertainty.UnsupportedOperation,
                Is.EqualTo(EffectUncertainty.None));
            Assert.That(
                result.Summary.AnalysisIncompleteReason,
                Is.EqualTo(EffectAnalysisIncompleteReason.None));
            Assert.That(
                result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(result.Summary.Throws.IsEmpty, Is.True);
            Assert.That(result.Projection.IsComplete, Is.True);
        }
    }

    [Test]
    public void CatchAllConsumesUnknownManagedThrowsOnlyWhenUnfiltered()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public interface IExternal {
                void Run();
            }

            public static class Sample {
                public static void CatchAll(IExternal external) {
                    try {
                        external.Run();
                    }
                    catch {
                    }
                }

                public static void CatchException(IExternal external) {
                    try {
                        external.Run();
                    }
                    catch (Exception) {
                    }
                }

                public static void Filtered(IExternal external) {
                    try {
                        external.Run();
                    }
                    catch (Exception) when (true) {
                    }
                }

                public static void MaybeFiltered(
                    IExternal external,
                    bool handle) {
                    try {
                        external.Run();
                    }
                    catch (Exception) when (handle) {
                    }
                }

                public static void Rethrow(IExternal external) {
                    try {
                        external.Run();
                    }
                    catch (Exception) {
                        throw;
                    }
                }

                public static void SiblingCatchAfterRethrow(
                    IExternal external) {
                    try {
                        external.Run();
                    }
                    catch (InvalidOperationException) {
                        throw;
                    }
                    catch (Exception) {
                    }
                }

                public static void NormalFinally(IExternal external) {
                    try {
                        external.Run();
                    }
                    finally {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                session.Analyze(Method(compilation, "CatchAll"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "CatchException"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "Filtered"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "MaybeFiltered"))
                    .Summary.Throws.IncludesUnknown,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "Rethrow"))
                    .Summary.Throws.IncludesUnknown,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "SiblingCatchAfterRethrow"))
                    .Summary.Throws.IncludesUnknown,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "NormalFinally"))
                    .Summary.Throws.IncludesUnknown,
                Is.True);
        }
    }

    [Test]
    public void ExceptionFlowDistinguishesConstructedGenericExceptionTypes()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public class GenericException<T> : System.Exception {
            }

            public sealed class DerivedStringException
                : GenericException<string> {
            }

            public static class Sample {
                public static void MismatchedCatch(
                    [NotNull] GenericException<string> exception) {
                    try {
                        throw exception;
                    }
                    catch (GenericException<int>) {
                    }
                }

                public static void ExactCatch(
                    [NotNull] GenericException<string> exception) {
                    try {
                        throw exception;
                    }
                    catch (GenericException<string>) {
                    }
                }

                public static void BaseCatch(
                    [NotNull] GenericException<string> exception) {
                    try {
                        throw exception;
                    }
                    catch (System.Exception) {
                    }
                }

                public static void DerivedCatch(
                    [NotNull] DerivedStringException exception) {
                    try {
                        throw exception;
                    }
                    catch (GenericException<string>) {
                    }
                }

                public static void MismatchedDerivedCatch(
                    [NotNull] DerivedStringException exception) {
                    try {
                        throw exception;
                    }
                    catch (GenericException<int>) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var genericException = EffectTestHost.RequireType(
            compilation,
            "GenericException`1");
        var integerException = genericException.Construct(
            compilation.GetSpecialType(SpecialType.System_Int32));
        var stringException = genericException.Construct(
            compilation.GetSpecialType(SpecialType.System_String));
        var derivedException = EffectTestHost.RequireType(
            compilation,
            "DerivedStringException");
        var mismatched = session.Analyze(
            Method(compilation, "MismatchedCatch"));
        var mismatchedDerived = session.Analyze(
            Method(compilation, "MismatchedDerivedCatch"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                mismatched.Summary.Throws.Types,
                Is.EqualTo([stringException])
                    .Using<INamedTypeSymbol>(SymbolEqualityComparer.Default));
            Assert.That(
                mismatchedDerived.Summary.Throws.Types,
                Is.EqualTo([derivedException])
                    .Using<INamedTypeSymbol>(SymbolEqualityComparer.Default));
            Assert.That(
                session.Analyze(Method(compilation, "ExactCatch"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "BaseCatch"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                session.Analyze(Method(compilation, "DerivedCatch"))
                    .Summary.Throws.IsEmpty,
                Is.True);
            Assert.That(
                EffectTypeFacts.IsDerivedFrom(
                    derivedException,
                    stringException),
                Is.True);
            Assert.That(
                EffectTypeFacts.IsDerivedFrom(
                    derivedException,
                    integerException),
                Is.False);
        }
    }

    [Test]
    public void MutationBearingExpressionsKeepRuntimeReachableEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_state;

                public static void DirectFalseEdge() {
                    var value = 1;
                    if (value + (value = 2) == 4) {
                    }
                    else {
                        s_state++;
                    }
                }

                public static void DirectTrueEdge() {
                    var value = 1;
                    if (value + (value = 2) == 3) {
                        s_state++;
                    }
                }

                public static void InitializerFalseEdge() {
                    var value = 1;
                    var predicate = value + (value = 2) == 4;
                    if (predicate) {
                    }
                    else {
                        s_state++;
                    }
                }

                public static void AssignmentFalseEdge() {
                    var value = 1;
                    var predicate = false;
                    predicate = value + (value = 2) == 4;
                    if (predicate) {
                    }
                    else {
                        s_state++;
                    }
                }

                public static void IncrementFalseEdge() {
                    var value = 1;
                    if (value++ + value == 4) {
                    }
                    else {
                        s_state++;
                    }
                }

                private static int Replace(ref int value) {
                    value = 2;
                    return value;
                }

                public static void RefCallFalseEdge() {
                    var value = 1;
                    if (value + Replace(ref value) == 4) {
                    }
                    else {
                        s_state++;
                    }
                }

                public static void NestedTrueEdge() {
                    var value = 1;
                    if ((value + (value = 2) == 3) && true) {
                        s_state++;
                    }
                }

                public static void StableImpossibleEdge() {
                    var value = 2;
                    if (value + value == 4) {
                    }
                    else {
                        s_state++;
                    }
                }

                public static void StableReachableEdge() {
                    var value = 1;
                    if (value + value == 4) {
                    }
                    else {
                        s_state++;
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            foreach (var methodName in new[] {
                         "DirectFalseEdge",
                         "DirectTrueEdge",
                     "InitializerFalseEdge",
                     "AssignmentFalseEdge",
                     "IncrementFalseEdge",
                     "RefCallFalseEdge",
                     "NestedTrueEdge",
                         "StableReachableEdge"
                     })
            {
                var result = session.Analyze(Method(compilation, methodName));
                Assert.That(
                    result.Summary.Writes.Contains(EffectRegionId.Static()),
                    Is.True,
                    methodName);
                Assert.That(
                    result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete),
                    methodName);
            }

            Assert.That(
                session.Analyze(Method(compilation, "StableImpossibleEdge"))
                    .Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
        }
    }

    [Test]
    public void AcyclicFlowDischargesOnlyProvenImplicitExceptions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Sample {
                public static int RequiredDivide(
                    [InRange(-10, 10)] int value,
                    int divisor) {
                    Contract.Requires(divisor != 0);
                    return value / divisor;
                }

                public static int GuardedDivide(
                    [InRange(-10, 10)] int value,
                    int divisor) {
                    if (divisor == 0) {
                        return 0;
                    }
                    return value / divisor;
                }

                public static int CheckedAdd(
                    [InRange(0, 10)] int left,
                    [InRange(0, 10)] int right) =>
                    checked(left + right);

                public static int PositiveSize(
                    [Positive] int length) =>
                    new int[length].Length;

                public static int GuardedIndex(int index) {
                    var values = new int[2];
                    if (index < 0 || index >= values.Length) {
                        return 0;
                    }
                    return values[index];
                }

                public static int NonNullReceiver(
                    [NotNull] string value) {
                    lock (value) {
                        return value.Length;
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var method in new[] {
                     "RequiredDivide",
                     "GuardedDivide",
                     "CheckedAdd",
                     "PositiveSize",
                     "GuardedIndex",
                     "NonNullReceiver"
                 })
        {
            var throws = session.Analyze(Method(compilation, method))
                .Summary.Throws;
            Assert.That(
                throws.IsEmpty,
                Is.True,
                method + ": unknown=" + throws.IncludesUnknown + "; " +
                string.Join(
                    ", ",
                    throws.Types.Select(static type =>
                        type.ContainingNamespace.MetadataName + "." +
                        type.MetadataName)));
        }
    }

    [Test]
    public void ReassignmentsWrapAndBadIndexesRetainImplicitExceptions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Sample {
                public static int ReassignedDivisor(int divisor) {
                    if (divisor == 0) {
                        return 0;
                    }
                    divisor = 0;
                    return 1 / divisor;
                }

                public static int NegativeIndex() =>
                    (new int[2])[-1];

                public static int TooLargeIndex() =>
                    (new int[2])[2];

                public static int CheckedBoundary(int value) =>
                    checked(value + 1);

                public static int NarrowedDivisor(long value) {
                    var divisor = unchecked((int)value);
                    return 1 / divisor;
                }

                public static int ReassignedNull(
                    [NotNull] string value) {
                    value = null!;
                    return value.Length;
                }

                public static int DivisionEvaluationOrder(int value) =>
                    value / (value = -1);

                public static int CheckedEvaluationOrder(int value) =>
                    checked(value + (value = 0));

                public static int ArrayEvaluationOrder() {
                    var first = new int[0];
                    var other = new int[2];
                    return first[(first = other).Length - 1];
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertThrows(
            session.Analyze(Method(compilation, "ReassignedDivisor")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "NegativeIndex")).Summary,
            "System.IndexOutOfRangeException");
        AssertThrows(
            session.Analyze(Method(compilation, "TooLargeIndex")).Summary,
            "System.IndexOutOfRangeException");
        AssertThrows(
            session.Analyze(Method(compilation, "CheckedBoundary")).Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "NarrowedDivisor")).Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "ReassignedNull")).Summary,
            "System.NullReferenceException");
        AssertThrows(
            session.Analyze(Method(compilation, "DivisionEvaluationOrder"))
                .Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "CheckedEvaluationOrder"))
                .Summary,
            "System.OverflowException");
        AssertThrows(
            session.Analyze(Method(compilation, "ArrayEvaluationOrder"))
                .Summary,
            "System.IndexOutOfRangeException");
    }

    [Test]
    public void UnverifiedReturnAnnotationsCannotDischargeRuntimeExceptions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Sample {
                [return: NotNull]
                private static string MissingText() => null!;

                [return: Positive]
                private static int ZeroDivisor() => 0;

                [return: InRange(0, 0)]
                private static int InvalidIndex() => 1;

                [SharpProofTrusted(" ")]
                [return: Positive]
                private static int InvalidTrustReason() => 0;

                public static int NullReceiver() =>
                    MissingText().Length;

                public static int Division() =>
                    10 / ZeroDivisor();

                public static int Bounds() {
                    var values = new int[1];
                    return values[InvalidIndex()];
                }

                public static int BlankTrustReason() =>
                    10 / InvalidTrustReason();
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertThrows(
            session.Analyze(Method(compilation, "NullReceiver"))
                .Summary,
            "System.NullReferenceException");
        AssertThrows(
            session.Analyze(Method(compilation, "Division"))
                .Summary,
            "System.DivideByZeroException");
        AssertThrows(
            session.Analyze(Method(compilation, "Bounds"))
                .Summary,
            "System.IndexOutOfRangeException");
        AssertThrows(
            session.Analyze(Method(compilation, "BlankTrustReason"))
                .Summary,
            "System.DivideByZeroException");
    }

    [Test]
    public void TrustedReturnAnnotationsRefineReceiversDivisorsAndIndexes()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using SharpProof.Attributes;

            public static class Sample {
                [SharpProofTrusted("Reviewed return contract.")]
                [return: NotNull]
                private static string Text() => "";

                [SharpProofTrusted("Reviewed return contract.")]
                [return: Positive]
                private static int Divisor() => 1;

                [SharpProofTrusted("Reviewed return contract.")]
                [return: InRange(0, 1)]
                private static int Index() => 1;

                [SharpProofTrusted("Reviewed return contract.")]
                private static string TextProperty {
                    [return: NotNull]
                    get => "";
                }

                [SharpProofTrusted("Reviewed return contract.")]
                [return: InRange(2, 1)]
                private static int Malformed() => 0;

                public static int Safe() {
                    var values = new int[2];
                    return Text().Length +
                        TextProperty.Length +
                        10 / Divisor() +
                        values[Index()];
                }

                public static int MalformedRemainsUnsafe() =>
                    10 / Malformed();
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        Assert.That(
            session.Analyze(Method(compilation, "Safe"))
                .Summary.Throws.IsEmpty,
            Is.True);
        AssertThrows(
            session.Analyze(Method(compilation, "MalformedRemainsUnsafe"))
                .Summary,
            "System.DivideByZeroException");
    }

    [Test]
    public void CallsThatCanMutateLocalsInvalidateFlowFacts()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private delegate void Mutation();

                private static void SetZero(ref int value) => value = 0;

                private static void CreateZero(out int value) => value = 0;

                public static int RefCall() {
                    var value = 1;
                    SetZero(ref value);
                    return 1 / value;
                }

                public static int OutConstructor() {
                    var value = 1;
                    _ = new Holder(out value);
                    return 1 / value;
                }

                public static int LocalFunctionCall() {
                    var value = 1;
                    void Mutate() => value = 0;
                    Mutate();
                    return 1 / value;
                }

                public static int DelegateCall() {
                    var value = 1;
                    Mutation mutate = () => value = 0;
                    mutate();
                    return 1 / value;
                }

                private sealed class Holder {
                    public Holder(out int value) => CreateZero(out value);
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var method in new[] {
                     "RefCall",
                     "OutConstructor",
                     "LocalFunctionCall",
                     "DelegateCall"
                 })
        {
            AssertContainsThrows(
                session.Analyze(Method(compilation, method)).Summary,
                "System.DivideByZeroException");
        }
    }

    [Test]
    public void DirectWitnessesAreNarrowDeterministicAndOrdered()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Threading;

            public sealed class UserException : Exception {
            }

            public sealed class PlainAllocation {
                public PlainAllocation() {
                }
            }

            public sealed class ThrowingInitialization {
                static ThrowingInitialization() {
                    throw new InvalidOperationException();
                }

                public ThrowingInitialization() {
                }
            }

            public class ThrowingBaseInitialization {
                static ThrowingBaseInitialization() {
                    throw new InvalidOperationException();
                }
            }

            public sealed class DerivedInitialization
                : ThrowingBaseInitialization {
            }

            public sealed class Sample {
                private int _field;
                private volatile int _volatile;

                public object Allocate() => new object();
                public PlainAllocation AllocatePlain() =>
                    new PlainAllocation();
                public ThrowingInitialization AllocateBlockedByTypeInitializer() =>
                    new ThrowingInitialization();
                public DerivedInitialization AllocateBlockedByBaseTypeInitializer() =>
                    new DerivedInitialization();
                public int[] AllocateArray() => new int[1];
                public void Throw() => throw new InvalidOperationException();
                public void ThrowUser() => throw new UserException();
                public void Write() => _field = 1;
                public int Read() => _field;
                public void VolatileWrite() => _volatile = 1;
                public int VolatileRead() => _volatile;

                public void Synchronize() {
                    lock (new object()) {
                    }
                }

                public void EnterMonitor() => Monitor.Enter(this);

                public void Conditional(bool condition) {
                    if (condition) {
                        _field = 1;
                    }
                }

                public void Multiple() {
                    _field = 1;
                    _field = 2;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertKinds("Allocate", "managed-allocation");
        AssertKinds("AllocatePlain", "managed-allocation");
        AssertKinds("AllocateBlockedByTypeInitializer");
        AssertKinds("AllocateBlockedByBaseTypeInitializer");
        AssertKinds("AllocateArray", "managed-array-allocation");
        AssertKinds("Throw", "explicit-throw");
        AssertKinds("ThrowUser");
        AssertKinds("Write", "direct-field-write");
        AssertKinds("Read", "direct-field-read");
        AssertKinds("VolatileWrite", "direct-field-write", "volatile-field-access");
        AssertKinds("VolatileRead", "direct-field-read", "volatile-field-access");
        AssertKinds("Synchronize", "managed-allocation", "synchronization-lock");
        AssertKinds("EnterMonitor", "synchronization-call");
        AssertKinds("Conditional");
        AssertKinds("Multiple");

        var frameworkThrow = Witnesses("Throw");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(frameworkThrow[0].ExceptionType?.MetadataName,
                Is.EqualTo(nameof(InvalidOperationException)));
            Assert.That(Witnesses("VolatileRead")[1].Capabilities,
                Is.EqualTo(EffectContractCapabilityKind.Synchronization));
            Assert.That(Witnesses("Synchronize")[0].Effects,
                Is.EqualTo(EffectContractKind.Allocates));
            Assert.That(
                session.Analyze(Method(
                    compilation,
                    "AllocateBlockedByTypeInitializer")).Summary.Allocation &
                EffectAllocationKind.Managed,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                session.Analyze(Method(
                    compilation,
                    "AllocateBlockedByBaseTypeInitializer"))
                    .Summary.Allocation &
                EffectAllocationKind.Managed,
                Is.EqualTo(EffectAllocationKind.Managed));
        }
        return;

        ImmutableArray<EffectDirectWitness> Witnesses(string name)
        {
            return session.Analyze(Method(compilation, name)).DirectWitnesses;
        }

        void AssertKinds(string name, params string[] expected)
        {
            Assert.That(Witnesses(name).Select(static witness => witness.Kind),
                Is.EqualTo(expected), name);
        }
    }

    [Test]
    public void MetadataBaseInitializationBlocksDirectAllocationWitness()
    {
        var externalReference = EffectTestHost.EmitReference(
            """
            public class ExternalInitializedBase {
                public static readonly object Value = new object();
            }

            public sealed class ExternalDerived
                : ExternalInitializedBase {
            }
            """,
            "ExternalAllocationAssembly");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static ExternalDerived Allocate() =>
                    new ExternalDerived();
            }
            """,
            externalReference);

        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Allocate"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DirectWitnesses, Is.Empty);
            Assert.That(
                result.Summary.Allocation &
                EffectAllocationKind.Managed,
                Is.EqualTo(EffectAllocationKind.Managed));
        }
    }

    [Test]
    public void SystemObjectAllocationRequiresApprovedFrameworkIdentity()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static object Allocate() =>
                    new object();
            }
            """);
        var approved = new ApiSpecResolver(
            ApiSpecTable.Default).Resolve(compilation);
        var unapproved = new ResolvedApiSpecTable(
            ImmutableDictionary.Create<ISymbol, ResolvedApiSpec>(
                SymbolEqualityComparer.Default),
            []);
        var objectType = compilation.GetSpecialType(
            SpecialType.System_Object);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                EffectMethodNodeBuilder
                    .HasPotentialStaticInitialization(
                        objectType,
                        approved),
                Is.False);
            Assert.That(
                EffectMethodNodeBuilder
                    .HasPotentialStaticInitialization(
                        objectType,
                        unapproved),
                Is.True);
            Assert.That(
                EffectMethodNodeBuilder
                    .HasPotentialConstructionInitialization(
                        objectType,
                        unapproved),
                Is.True);
        }
    }

    [Test]
    public void ExcessiveBaseTypeDepthFailsClosedWithoutRecursion()
    {
        var declarations = Enumerable.Range(0, 260)
            .Select(static index => index == 0
                ? "public class Layer0 { }"
                : "public class Layer" + index +
                  " : Layer" + (index - 1) + " { }");
        var compilation = EffectTestHost.CreateCompilation(
            string.Join(Environment.NewLine, declarations) +
            """

            public static class Sample {
                public static Layer259 Allocate() =>
                    new Layer259();
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Allocate"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.DirectWitnesses, Is.Empty);
            Assert.That(
                result.Summary.Allocation &
                EffectAllocationKind.Managed,
                Is.EqualTo(EffectAllocationKind.Managed));
        }
    }

    [Test]
    public void PreBodyExecutionBlocksDirectBodyWitnesses()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class StaticEntry {
                static StaticEntry() =>
                    throw new InvalidOperationException();

                public static object Allocate() => new object();
            }

            public class ThrowingBaseConstructor {
                protected ThrowingBaseConstructor() =>
                    throw new InvalidOperationException();
            }

            public sealed class DerivedConstructor
                : ThrowingBaseConstructor {
                private object _value = null!;

                public DerivedConstructor() =>
                    _value = new object();
            }

            public sealed class InstanceInitializer {
                private readonly object _before = Fail();
                private object _value = null!;

                public InstanceInitializer() =>
                    _value = new object();

                private static object Fail() =>
                    throw new InvalidOperationException();
            }

            public sealed class StaticInitializer {
                private static readonly object Before = Fail();
                private static object s_value = null!;

                static StaticInitializer() =>
                    s_value = new object();

                private static object Fail() =>
                    throw new InvalidOperationException();
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var methods = new[]
        {
            EffectTestHost.RequireMethod(
                compilation,
                "StaticEntry",
                "Allocate"),
            ExplicitConstructor("DerivedConstructor"),
            ExplicitConstructor("InstanceInitializer"),
            EffectTestHost.RequireType(
                    compilation,
                    "StaticInitializer")
                .StaticConstructors
                .Single()
        };

        foreach (var method in methods)
        {
            Assert.That(
                session.Analyze(method).DirectWitnesses,
                Is.Empty,
                method.ContainingType.Name);
        }

        return;

        IMethodSymbol ExplicitConstructor(string typeName)
        {
            return EffectTestHost.RequireType(
                    compilation,
                    typeName)
                .InstanceConstructors
                .Single(static constructor =>
                    !constructor.IsImplicitlyDeclared);
        }
    }

    [Test]
    public void DirectAllocationWitnessesRequireArgumentCompletion()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class IntegerBox {
                public IntegerBox(int value) {
                }
            }

            public sealed class StringBox {
                public StringBox(string value) {
                }
            }

            public sealed class Convertible {
                public static implicit operator int(Convertible value) =>
                    1;
            }

            public static class Sample {
                public static IntegerBox SafeImplicit(short value) =>
                    new IntegerBox(value);

                public static IntegerBox FailingUnbox() =>
                    new IntegerBox((int)(object)null!);

                public static StringBox FailingReferenceCast() =>
                    new StringBox((string)(object)new object());

                public static IntegerBox FailingCheckedConversion(
                    long value) =>
                    new IntegerBox(checked((int)value));

                public static IntegerBox UserConversion() =>
                    new IntegerBox(new Convertible());
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        Assert.That(
            WitnessKinds("SafeImplicit"),
            Is.EqualTo(["managed-allocation"]));
        foreach (var methodName in new[]
                 {
                     "FailingUnbox",
                     "FailingReferenceCast",
                     "FailingCheckedConversion",
                     "UserConversion"
                 })
        {
            Assert.That(
                WitnessKinds(methodName),
                Is.Empty,
                methodName);
        }

        return;

        IEnumerable<string> WitnessKinds(string methodName)
        {
            return session.Analyze(
                    Method(compilation, methodName))
                .DirectWitnesses
                .Select(static witness => witness.Kind);
        }
    }

    [Test]
    public void DirectWitnessesRejectFailingPreEffectConversions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Threading;

            public sealed class Sample {
                private int _field;

                public void Write() =>
                    _field = (int)(object)null!;

                public void Lock() {
                    lock ((string)(object)new object()) {
                    }
                }

                public void MonitorCall() =>
                    Monitor.Enter(
                        (string)(object)new object());

                public void Throw() =>
                    throw (Exception)(object)"not-an-exception";
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[]
                 {
                     "Write",
                     "Lock",
                     "MonitorCall",
                     "Throw"
                 })
        {
            Assert.That(
                session.Analyze(Method(compilation, methodName))
                    .DirectWitnesses,
                Is.Empty,
                methodName);
        }
    }

    [Test]
    public void DirectLockWitnessesRequireReceiverEvaluationToComplete()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class ThrowingLockReceiver {
                public ThrowingLockReceiver() =>
                    throw new InvalidOperationException();
            }

            public sealed class Sample {
                private static readonly int NegativeLength = -1;

                private static int GetLength() =>
                    throw new InvalidOperationException();

                private static object GetElement() =>
                    throw new InvalidOperationException();

                public void ObjectReceiver() {
                    lock (new object()) {
                    }
                }

                public void UpcastObjectReceiver() {
                    lock ((object)(new object())) {
                    }
                }

                public void ThrowingObjectReceiver() {
                    lock (new ThrowingLockReceiver()) {
                    }
                }

                public void UpcastThrowingObjectReceiver() {
                    lock ((object)(new ThrowingLockReceiver())) {
                    }
                }

                public void ArrayReceiver() {
                    lock (new object[1]) {
                    }
                }

                public void HarmlessArrayInitializerReceiver() {
                    lock (new object[] { null! }) {
                    }
                }

                public void ThrowingArrayInitializerReceiver() {
                    lock (new object[] { GetElement() }) {
                    }
                }

                public void NegativeArrayReceiver() {
                    lock (new object[NegativeLength]) {
                    }
                }

                public void ParameterArrayReceiver(int length) {
                    lock (new object[length]) {
                    }
                }

                public void ThrowingArrayReceiver() {
                    lock (new object[GetLength()]) {
                    }
                }

                public void ThisReceiver() {
                    lock (this) {
                    }
                }

                public void TypeReceiver() {
                    lock (typeof(Sample)) {
                    }
                }

                public void ConstantReceiver() {
                    lock ("gate") {
                    }
                }

                public void BoxingReceiver() {
                    lock ((object)1) {
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        AssertKinds(
            "ObjectReceiver",
            "managed-allocation",
            "synchronization-lock");
        AssertKinds(
            "UpcastObjectReceiver",
            "managed-allocation",
            "synchronization-lock");
        AssertKinds(
            "ThrowingObjectReceiver",
            "managed-allocation");
        AssertKinds(
            "UpcastThrowingObjectReceiver",
            "managed-allocation");
        AssertKinds(
            "ArrayReceiver",
            "managed-array-allocation",
            "synchronization-lock");
        AssertKinds(
            "HarmlessArrayInitializerReceiver",
            "managed-array-allocation",
            "synchronization-lock");
        AssertKinds("ThrowingArrayInitializerReceiver");
        AssertKinds("NegativeArrayReceiver");
        AssertKinds("ParameterArrayReceiver");
        AssertKinds("ThrowingArrayReceiver");
        AssertKinds("ThisReceiver", "synchronization-lock");
        AssertKinds("TypeReceiver", "synchronization-lock");
        AssertKinds("ConstantReceiver", "synchronization-lock");
        AssertKinds("BoxingReceiver");
        return;

        void AssertKinds(string name, params string[] expected)
        {
            Assert.That(
                session.Analyze(Method(compilation, name))
                    .DirectWitnesses
                    .Select(static witness => witness.Kind),
                Is.EqualTo(expected),
                name);
        }
    }

    [Test]
    public void ExceptionConstructorsRequireExactSpecsAndGateThrowWitnesses()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Collections.Generic;

            public static class Sample {
                public static InvalidOperationException Safe() =>
                    new InvalidOperationException("message");

                public static AggregateException Unmodeled() =>
                    new AggregateException(
                        (IEnumerable<Exception>)null!);

                public static void ThrowSafe() =>
                    throw new InvalidOperationException();

                public static void ThrowUnmodeled() =>
                    throw new AggregateException(
                        (IEnumerable<Exception>)null!);
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var safe = session.Analyze(Method(compilation, "Safe"));
        var unmodeled = session.Analyze(Method(compilation, "Unmodeled"));
        var safeThrow = session.Analyze(Method(compilation, "ThrowSafe"));
        var unmodeledThrow = session.Analyze(Method(compilation, "ThrowUnmodeled"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(safe.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(safe.Summary.Throws.IsEmpty, Is.True);
            Assert.That(
                safe.Summary.Termination,
                Is.EqualTo(EffectTermination.Terminates));
            Assert.That(unmodeled.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
            Assert.That(unmodeled.Summary.Throws.IncludesUnknown, Is.True);
            Assert.That(
                unmodeled.Summary.Uncertainty & EffectUncertainty.UnmodeledCall,
                Is.EqualTo(EffectUncertainty.UnmodeledCall));
            Assert.That(
                safeThrow.DirectWitnesses.Select(static witness => witness.Kind),
                Is.EqualTo(["explicit-throw"]));
            Assert.That(
                safeThrow.DirectWitnesses[0].ExceptionType?.ToDisplayString(),
                Is.EqualTo("System.InvalidOperationException"));
            Assert.That(
                unmodeledThrow.DirectWitnesses.Select(static witness => witness.Kind),
                Is.Empty);
        }
    }

    [Test]
    public void NonThrowingSpecWithoutTerminationCannotYieldAThrowWitness()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Collections.Generic;

            public static class Sample {
                public static void Throw() =>
                    throw new AggregateException(
                        (IEnumerable<Exception>)null!);
            }
            """);
        var frameworkAssemblies = ApiSpecTable.Default.Templates.Single(
            static template =>
                template.Target.WitnessIdentifier == "bcl.object.ctor")
            .Target.ApprovedAssemblies;
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Observed,
            "synthetic-non-terminating-constructor");
        var table = ApiSpecTable.Create([
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    "synthetic.aggregate.ctor",
                    "M:System.AggregateException.#ctor(System.Collections.Generic.IEnumerable{System.Exception})",
                    "System.AggregateException",
                    SpecTargetMemberKind.Constructor,
                    ".ctor",
                    false,
                    0,
                    IrTypeKind.Reference,
                    [IrTypeKind.Reference],
                    null,
                    frameworkAssemblies),
                new ApiSpecFacets(
                    new SpecEffectFacet(SpecEffect.None, evidence),
                    new SpecAllocationFacet(
                        SpecAllocationBehavior.None,
                        evidence),
                    new SpecThrowFacet(
                        SpecThrowBehavior.DoesNotThrow,
                        [],
                        evidence),
                    new SpecNullnessFacet(
                        SpecNullness.NotApplicable,
                        evidence),
                    new SpecCardinalityFacet(
                        SpecCardinality.NotApplicable,
                        null,
                        evidence)),
                [])
        ]);
        var result = new EffectAnalysisSession(compilation, table).Analyze(
            Method(compilation, "Throw"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Throws.IncludesUnknown, Is.False);
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.AggregateException"));
            Assert.That(
                result.Summary.Termination,
                Is.EqualTo(EffectTermination.Unknown));
            Assert.That(
                result.DirectWitnesses.Select(static witness => witness.Kind),
                Is.Empty);
        }
    }

    private static EffectMethodResult Analyze(
        string source,
        string typeMetadataName,
        string methodName)
    {
        var compilation = EffectTestHost.CreateCompilation(source);
        return new EffectAnalysisSession(compilation).Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                typeMetadataName,
                methodName));
    }

    private static IMethodSymbol Method(
        Compilation compilation,
        string methodName)
    {
        return EffectTestHost.RequireMethod(compilation, "Sample", methodName);
    }

    private static IMethodSymbol RequireGetter(
        Compilation compilation,
        string typeMetadataName,
        string propertyName)
    {
        return EffectTestHost.RequireType(compilation, typeMetadataName)
            .GetMembers(propertyName)
            .OfType<IPropertySymbol>()
            .Single()
            .GetMethod ?? throw new InvalidOperationException(
                $"Property '{typeMetadataName}.{propertyName}' has no getter.");
    }

    private static void AssertThrows(
        EffectSummary summary,
        params string[] metadataNames)
    {
        Assert.That(summary.Throws.IncludesUnknown, Is.False);
        AssertContainsThrows(summary, metadataNames);
    }

    private static void AssertContainsThrows(
        EffectSummary summary,
        params string[] metadataNames)
    {
        var actual = summary.Throws.Types
            .Select(static type =>
                type.ContainingNamespace.MetadataName + "." + type.MetadataName)
            .ToImmutableArray();
        foreach (var metadataName in metadataNames)
        {
            Assert.That(
                actual,
                Does.Contain(metadataName));
        }
    }

    private static void AssertDoesNotThrow(
        EffectSummary summary,
        params string[] metadataNames)
    {
        var actual = summary.Throws.Types
            .Select(static type =>
                type.ContainingNamespace.MetadataName + "." + type.MetadataName)
            .ToImmutableArray();
        foreach (var metadataName in metadataNames)
        {
            Assert.That(
                actual,
                Does.Not.Contain(metadataName));
        }
    }

    private static string ResultKey(EffectMethodResult result)
    {
        return result.Method.ContainingType.MetadataName + "." +
        result.Method.MetadataName + "/" +
        result.Method.Parameters.Length;
    }

    [Test]
    public void ThrowingAssignmentValueSuppressesTargetWrite()
    {
        var result = Analyze(
            """
            public static class Sample {
                private static int s_state;

                public static void Assign() {
                    s_state = Fail();
                    s_state++;
                }

                private static int Fail() =>
                    throw new System.InvalidOperationException();
            }
            """,
            "Sample",
            "Assign");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void AssignmentTargetEvaluationPrecedesThrowingValue()
    {
        var result = Analyze(
            """
            public sealed class Box {
                public int[] Values = new int[1];
            }

            public static class Sample {
                private static int s_state;

                public static void Assign(Box box) {
                    box.Values[RecordIndex()] = Fail();
                }

                private static int RecordIndex() {
                    s_state++;
                    return 0;
                }

                private static int Fail() =>
                    throw new System.InvalidOperationException();
            }
            """,
            "Sample",
            "Assign");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.False);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void ThrowingLeftBinaryOperandSuppressesRightOperandEffects()
    {
        var result = Analyze(
            """
            public static class Sample {
                private static int s_state;

                public static int Evaluate() => Fail() + Mutate();

                private static int Fail() =>
                    throw new System.InvalidOperationException();

                private static int Mutate() {
                    s_state++;
                    return 1;
                }
            }
            """,
            "Sample",
            "Evaluate");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void FailingCompoundTargetReadSuppressesValueEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public int Value;
            }

            public static class Sample {
                private static int s_state;

                public static int Evaluate() {
                    Box box = null!;
                    return box.Value += Mutate();
                }

                public static void EvaluateThenContinue() {
                    Box box = null!;
                    box.Value += 1;
                    Mutate();
                }

                private static int Mutate() {
                    s_state++;
                    return 1;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] {
                     "Evaluate",
                     "EvaluateThenContinue"
                 })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False,
                methodName);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete),
                methodName);
        }
    }

    [Test]
    public void EffectsAfterDefiniteNoncompletionAreSuppressed()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Global {
                public static int State;

                public static int Fail() =>
                    throw new InvalidOperationException();

                public static int Mutate() {
                    State++;
                    return 1;
                }
            }

            public sealed class Target {
                public void Touch() => Global.State++;

                public void Pair(int first, int second) => Global.State++;
            }

            public static class Sample {
                public static void NullReceiver() {
                    Target? target = null;
                    target.Touch();
                }

                public static void FirstArgumentThrows() {
                    var target = new Target();
                    target.Pair(Global.Fail(), Global.Mutate());
                }

                public static void NullLock() {
                    object? gate = null;
                    lock (gate) {
                        Global.State++;
                    }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] {
                     "NullReceiver", "FirstArgumentThrows", "NullLock"
                 })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False,
                methodName);
        }
    }

    [Test]
    public void DeeplyNestedExpressionsAbstainInsteadOfExhaustingTheStack()
    {
        var chain = string.Join(" + ", Enumerable.Repeat("value", 400));
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            public static class Sample {
                public static long Deep(long value) => {{chain}};
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        // The scanner and the abstract flow both walk this tree recursively.
        // Past their depth budget they must abstain, because
        // StackOverflowException is uncatchable and would kill the compiler.
        var result = session.Analyze(Method(compilation, "Deep"));

        Assert.That(result.Projection.IsComplete, Is.False);
    }

    [Test]
    public void DeepCallChainsAbstainInsteadOfExhaustingTheStack()
    {
        var methods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 600).Select(static index =>
                $"    public static long Step{index}(long value) => " +
                (index == 599
                    ? "value;"
                    : $"Step{index + 1}(value);")));
        var compilation = EffectTestHost.CreateCompilation(
            $$"""
            public static class Sample {
            {{methods}}
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var result = session.Analyze(Method(compilation, "Step0"));

        Assert.That(result.Projection.IsComplete, Is.False);
    }
}
