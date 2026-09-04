using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Ir;
using SharpProof.Specs;
using static SharpProof.Testing.ApiSpecTestFacets;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectAnalysisTests
{
    [Test]
    public void ReassignedLockUsesTheCurrentNonNullValue()
    {
        var result = Analyze(
            """
            public static class Sample {
                private static int state;

                public static void Run() {
                    object gate = null!;
                    gate = new object();
                    lock (gate) {
                        state++;
                    }
                }
            }
            """,
            "Sample",
            "Run");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Regions,
                Has.Some.EqualTo(EffectRegionId.Static()));
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Not.Contain("System.ArgumentNullException"));
        }
    }

    [Test]
    public void NullnessFallbackChecksAssignmentsBeforeTreatingLockAsNull()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static void Run() {
                    object gate = null!;
                    gate = new object();
                    lock (gate) { }
                }
            }
            """);
        var method = EffectTestHost.SampleMethod(compilation, "Run");
        var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
        var operation = compilation.GetSemanticModel(syntax.SyntaxTree)
            .GetOperation(syntax);
        var @lock = operation!.DescendantsAndSelf()
            .OfType<ILockOperation>()
            .Single();
        var evaluator = new OperationNullnessEvaluator(
            new EffectAnalysisSession(compilation),
            operation!,
            abstractFlow: null,
            monitorType: null);

        Assert.That(evaluator.IsProvenNull(@lock.LockedValue, @lock), Is.False);
    }

    [Test]
    public void AwaitProtocolEffectsAreIncludedInTheSummary()
    {
        var result = Analyze(
            """
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;

            public static class Sample {
                private static int state;

                public static async Task Run() {
                    await new Awaitable();
                }

                public sealed class Awaitable {
                    public Awaiter GetAwaiter() => new();
                }

                public sealed class Awaiter : INotifyCompletion {
                    public bool IsCompleted {
                        get { state++; return true; }
                    }

                    public void OnCompleted(Action continuation) {
                        state++;
                    }

                    public void GetResult() {
                        state++;
                        throw new InvalidOperationException();
                    }
                }
            }
            """,
            "Sample",
            "Run");

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
        Assert.That(
            result.Summary.Throws.Types.Select(static type =>
                type.ToDisplayString()),
            Does.Contain("System.InvalidOperationException"));
    }

    [Test]
    public void NullReferenceAwaiterThrowsBeforeProtocolMembersRun()
    {
        var result = Analyze(
            """
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;

            public static class Sample {
                private static int state;

                public static async Task Run() {
                    await new Awaitable();
                }

                public sealed class Awaitable {
                    public Awaiter GetAwaiter() => null!;
                }

                public sealed class Awaiter : INotifyCompletion {
                    public bool IsCompleted {
                        get { state++; return true; }
                    }

                    public void OnCompleted(Action continuation) {
                        state++;
                    }

                    public void GetResult() {
                        state++;
                    }
                }
            }
            """,
            "Sample",
            "Run");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Contain("System.NullReferenceException"));
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void CriticalAwaitProtocolUsesUnsafeContinuationEffects()
    {
        var result = Analyze(
            """
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;

            public static class Sample {
                private static int state;

                public static async Task Run() {
                    await new Awaitable();
                }

                public sealed class Awaitable {
                    public Awaiter GetAwaiter() => new();
                }

                public sealed class Awaiter : ICriticalNotifyCompletion {
                    public bool IsCompleted {
                        get { state++; return true; }
                    }

                    public void OnCompleted(Action continuation) {
                        throw new InvalidOperationException();
                    }

                    public void UnsafeOnCompleted(Action continuation) {
                        state++;
                    }

                    public void GetResult() {
                        state++;
                    }
                }
            }
            """,
            "Sample",
            "Run");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
                Does.Not.Contain("System.InvalidOperationException"));
        }
    }

    [Test]
    public void AwaitRegistrationExceptionsReachMatchingHandlers()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;

            public static class Sample {
                private static int state;

                public static async Task NotifyCompletion() {
                    try {
                        await new NotifyAwaitable();
                    }
                    catch (InvalidOperationException) {
                        state++;
                    }
                }

                public static async Task CriticalNotifyCompletion() {
                    try {
                        await new CriticalAwaitable();
                    }
                    catch (InvalidOperationException) {
                        state++;
                    }
                }

                public sealed class NotifyAwaitable {
                    public NotifyAwaiter GetAwaiter() => new();
                }

                public sealed class NotifyAwaiter : INotifyCompletion {
                    public bool IsCompleted => false;

                    public void OnCompleted(Action continuation) =>
                        throw new InvalidOperationException();

                    public void GetResult() { }
                }

                public sealed class CriticalAwaitable {
                    public CriticalAwaiter GetAwaiter() => new();
                }

                public sealed class CriticalAwaiter :
                    ICriticalNotifyCompletion {
                    public bool IsCompleted => false;

                    public void OnCompleted(Action continuation) =>
                        throw new ApplicationException();

                    public void UnsafeOnCompleted(Action continuation) =>
                        throw new InvalidOperationException();

                    public void GetResult() { }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            foreach (var methodName in new[] {
                         "NotifyCompletion",
                         "CriticalNotifyCompletion"
                     })
            {
                var result = session.Analyze(Method(compilation, methodName));

                Assert.That(
                    result.Summary.Writes.Contains(EffectRegionId.Static()),
                    Is.True,
                    methodName);
            }
        }
    }

    [Test]
    public void RecordClassWithAllocatesAndMapsCopyConstructorRegions()
    {
        var result = Analyze(
            """
            public sealed record Sample {
                private int _value;

                private Sample(Sample original) {
                    _value = original._value;
                }

                public static Sample Copy(Sample source) =>
                    source with { };
            }
            """,
            "Sample",
            "Copy");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                result.Summary.Reads.IsUnknown,
                Is.False);
            Assert.That(
                result.Summary.Reads.Contains(
                    EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(result.Summary.Reads.Regions, Has.Length.EqualTo(1));
            Assert.That(result.Summary.Writes.IsUnknown, Is.False);
            Assert.That(
                result.Summary.Writes.Regions,
                Has.All.Property(nameof(EffectRegionId.Kind))
                    .EqualTo(EffectRegionKind.Fresh));
            Assert.That(result.Summary.Writes.Regions, Has.Length.EqualTo(1));
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void MetadataListPatternAccessorsRemainConservative()
    {
        var external = EffectTestHost.EmitImage(
            """
            namespace External;
            public sealed class MetadataList {
                private static int state;
                public int Length { get { state++; return 1; } }
                public int this[int index] =>
                    throw new System.InvalidOperationException();
            }
            """,
            "ExternalListPatterns");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;
                public static void Check(External.MetadataList values) {
                    try { _ = values is [0]; }
                    catch (System.InvalidOperationException) { state++; }
                }
            }
            """,
            external.Reference);

        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Check"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
        }
    }

    [Test]
    public void MetadataRefStructListPatternAccessorsRemainConservative()
    {
        var external = EffectTestHost.EmitImage(
            """
            namespace External;
            public ref struct MetadataRefList {
                private static int state;
                public int Length { get { state++; return 1; } }
                public int this[int index] =>
                    throw new System.InvalidOperationException();
            }
            """,
            "ExternalRefStructListPatterns");
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int state;
                public static void Check(External.MetadataRefList values) {
                    try { _ = values is [0]; }
                    catch (System.InvalidOperationException) { state++; }
                }
            }
            """,
            external.Reference);

        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Check"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Incomplete));
        }
    }

    [Test]
    public void PatternSubpatternsRespectImplicitEvaluationGates()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class PatternItem {
                public int P {
                    get { Sample.State++; return 1; }
                }
            }

            public sealed class ThrowingLengthList {
                public int Length => throw new InvalidOperationException();
                public PatternItem this[int index] => new();
            }

            public static class Sample {
                public static int State;

                public static bool AfterThrowingLength(
                    ThrowingLengthList value) =>
                    value is [{ P: 1 }];

                public static bool KnownNullPropertyPattern() {
                    PatternItem value = null!;
                    return value is { P: 1 };
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var afterThrowingLength = session.Analyze(
            Method(compilation, "AfterThrowingLength"));
        var knownNull = session.Analyze(
            Method(compilation, "KnownNullPropertyPattern"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                afterThrowingLength.Summary.Throws.Types.Select(
                    static type => type.ToDisplayString()),
                Does.Contain("System.InvalidOperationException"));
            Assert.That(
                afterThrowingLength.Summary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.False);
            Assert.That(
                knownNull.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
        }
    }

    [Test]
    public void DivergentCatchDoesNotHideCallerTail()
    {
        var result = Analyze(
            """
            public static class Sample {
                private static int state;
                private static void MaybeHang(bool fail) {
                    try { if (fail) throw new System.Exception(); }
                    catch { while (true) { } }
                }
                public static void Caller() {
                    MaybeHang(false);
                    state++;
                }
            }
            """,
            "Sample",
            "Caller");

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
    }

    [Test]
    public void UnreachableDivergentCatchDoesNotFabricateDivergence()
    {
        var result = Analyze(
            """
            public static class Sample {
                private static int state;
                public static void Example() {
                    try { state++; }
                    catch (System.InvalidOperationException) { while (true) { } }
                    state++;
                }
            }
            """,
            "Sample",
            "Example");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Termination, Is.EqualTo(EffectTermination.Terminates));
            Assert.That(result.Summary.Writes.Contains(EffectRegionId.Static()), Is.True);
        }
    }

    [Test]
    public void CaughtThrowInsideFinallyDoesNotHideFollowingWrite()
    {
        var result = Analyze(
            """
            public static class Sample {
                private static int state;
                public static void Example() {
                    try { }
                    finally {
                        try { throw new System.InvalidOperationException(); }
                        catch (System.InvalidOperationException) { }
                    }
                    state++;
                }
            }
            """,
            "Sample",
            "Example");

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.True);
    }

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
    public void InterpolationUsesTheIFormattableFormattingMethod()
    {
        var result = Analyze(
            """
            using System;

            public sealed class FormattedValue : IFormattable {
                private static volatile int s_state;

                public override string ToString() => "";

                string IFormattable.ToString(
                    string? format,
                    IFormatProvider? provider) {
                    s_state = 1;
                    throw new InvalidOperationException();
                }
            }

            public static class Sample {
                public static string Format(FormattedValue value) =>
                    $"{value}";
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
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    [Test]
    public void FormattableStringDefersHoleFormattingEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class DeferredValue {
                private int _formatCount;

                public override string ToString() {
                    _formatCount++;
                    throw new InvalidOperationException();
                }
            }

            public static class Sample {
                private static int s_state;

                private static DeferredValue Evaluate(DeferredValue value) {
                    s_state++;
                    return value;
                }

                public static FormattableString Create(DeferredValue value) =>
                    $"{value}";

                public static FormattableString EvaluateLaterHole(
                    DeferredValue value) => $"{value}{Evaluate(value)}";

                public static void ContinueAfterCreation(
                    DeferredValue value) {
                    FormattableString deferred = $"{value}";
                    s_state++;
                }

                public static void DeferredFormattingCannotReachCatch(
                    DeferredValue value) {
                    try { FormattableString deferred = $"{value}"; }
                    catch (InvalidOperationException) { s_state++; }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var create = session.Analyze(Method(compilation, "Create"));
        var evaluate = session.Analyze(
            Method(compilation, "EvaluateLaterHole"));
        var continuation = session.Analyze(
            Method(compilation, "ContinueAfterCreation"));
        var unreachableCatch = session.Analyze(
            Method(compilation, "DeferredFormattingCannotReachCatch"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                create.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.False,
                "deferred formatter write");
            AssertDoesNotThrow(
                create.Summary,
                "System.InvalidOperationException");
            Assert.That(
                create.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                create.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));

            Assert.That(
                evaluate.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "later hole evaluation");
            Assert.That(
                evaluate.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.False,
                "deferred formatter after hole evaluation");
            AssertDoesNotThrow(
                evaluate.Summary,
                "System.InvalidOperationException");

            Assert.That(
                continuation.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "suffix after deferred construction");
            Assert.That(
                continuation.Summary.Writes.Contains(
                    EffectRegionId.Parameter(0)),
                Is.False,
                "deferred formatter before suffix");
            AssertDoesNotThrow(
                continuation.Summary,
                "System.InvalidOperationException");

            Assert.That(
                unreachableCatch.Summary.Writes.Contains(
                    EffectRegionId.Static()),
                Is.False,
                "deferred formatting catch");
            AssertDoesNotThrow(
                unreachableCatch.Summary,
                "System.InvalidOperationException");
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
    public void UserDefinedStringAdditionUsesOperatorSummaryForAllocation()
    {
        var result = Analyze(
            """
            public sealed class Token {
                public static string operator +(Token left, Token right) =>
                    "cached";
            }

            public static class Sample {
                public static string Combine(Token left, Token right) =>
                    left + right;
            }
            """,
            "Sample",
            "Combine");

        Assert.That(
            result.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.None));
    }

    [Test]
    public void StringCompoundAssignmentIncludesConcatenationEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
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
                public static void Allocate(string text) {
                    text += "suffix";
                }

                public static void Direct(
                    string text,
                    ref int after) {
                    text += new FormattedValue();
                    after++;
                }

                public static void Caught(
                    string text,
                    ref int caught) {
                    try { text += new FormattedValue(); }
                    catch (InvalidOperationException) { caught++; }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var allocation = session.Analyze(Method(compilation, "Allocate"));
        var direct = session.Analyze(Method(compilation, "Direct"));
        var caught = session.Analyze(Method(compilation, "Caught"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                direct.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                direct.Summary.Capabilities.Contains(
                    EffectCapabilityKind.Synchronization),
                Is.True);
            AssertContainsThrows(
                direct.Summary,
                "System.InvalidOperationException");
            Assert.That(
                allocation.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
            Assert.That(
                direct.Summary.Writes.Contains(EffectRegionId.Parameter(1)),
                Is.False,
                "a definitely throwing formatter blocks the suffix");
            Assert.That(
                direct.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                caught.Summary.Writes.Contains(EffectRegionId.Parameter(1)),
                Is.True,
                "the formatting exception must reach its catch");
        }
    }

    [Test]
    public void ImplicitFormattingExceptionsKeepHandlersReachable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class ThrowingValue {
                public override string ToString() =>
                    throw new InvalidOperationException();
            }

            public static class Sample {
                private static int s_state;

                public static void Interpolated(ThrowingValue value) {
                    try { _ = $"{value}"; }
                    catch (InvalidOperationException) { s_state++; }
                }

                public static void Concatenated(ThrowingValue value) {
                    try { _ = "value=" + value; }
                    catch (InvalidOperationException) { s_state++; }
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] { "Interpolated", "Concatenated" })
        {
            Assert.That(
                session.Analyze(Method(compilation, methodName))
                    .Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                methodName);
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
    public void CheckedUserConversionDoesNotInventIntrinsicOverflow()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public readonly struct Source {
                public static explicit operator Target(Source value) =>
                    default;
                public static explicit operator checked Target(Source value) =>
                    default;
            }

            public readonly struct Target {
            }

            public static class Sample {
                public static Target Convert(Source value) =>
                    checked((Target)value);
            }
            """);
        var method = Method(compilation, "Convert");
        var syntax = method.DeclaringSyntaxReferences.Single().GetSyntax();
        var operation = compilation.GetSemanticModel(syntax.SyntaxTree)
            .GetOperation(syntax)!;
        var conversion = operation.DescendantsAndSelf()
            .OfType<IConversionOperation>()
            .Single(static value => value.OperatorMethod != null);

        var result = new EffectAnalysisSession(compilation).Analyze(method);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(conversion.IsChecked, Is.True);
            Assert.That(
                conversion.OperatorMethod?.MetadataName,
                Is.EqualTo("op_CheckedExplicit"));
            Assert.That(result.Summary.Throws.IsEmpty, Is.True);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(result.Projection.IsComplete, Is.True);
        }
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

                public static int SafeCheckedIncrement(int value) {
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
            session.Analyze(Method(compilation, "SafeCheckedIncrement"))
                .Summary,
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

            public class OptionalBase {
                protected OptionalBase(int value = 0) { Global.Touch(); }
            }

            public sealed class OptionalDerived : OptionalBase { }

            public class ParamsBase {
                protected ParamsBase(params int[] values) { Global.Touch(); }
            }

            public sealed class ParamsDerived : ParamsBase { }

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
                public static OptionalDerived Optional() => new();
                public static ParamsDerived Params() => new();
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

        foreach (var methodName in new[] { "Optional", "Params" })
        {
            var result = session.Analyze(Method(compilation, methodName));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(result.Summary.Completeness,
                    Is.EqualTo(EffectCompleteness.Complete), methodName);
                Assert.That(result.Summary.Writes.Contains(EffectRegionId.Static()),
                    Is.True, methodName);
            }
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
            EffectTestHost.SampleMethod(compilation, "WriteReceiver"));
        var @static = session.Analyze(
            EffectTestHost.SampleMethod(compilation, "WriteStatic"));

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
    public void EventAssignmentsPreserveHandlerAndAccessorEvaluationOrder()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            #nullable enable
            using System;

            public sealed class EventTarget {
                private int _state;

                public event Action Changed {
                    add {
                        _state++;
                        throw new InvalidOperationException();
                    }
                    remove {
                        _state--;
                        throw new ApplicationException();
                    }
                }
            }

            public static class Sample {
                private static int s_handlerState;

                private static Action CreateHandler() {
                    s_handlerState++;
                    return static () => { };
                }

                private static Action ThrowHandler() {
                    s_handlerState++;
                    throw new ArgumentException();
                }

                public static void HandlerBeforeReceiverCheck() {
                    EventTarget target = null!;
                    target.Changed += ThrowHandler();
                }

                public static void Add(EventTarget? target) =>
                    target.Changed += CreateHandler();

                public static void Remove(EventTarget? target) =>
                    target.Changed -= CreateHandler();
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var handlerFirst = session.Analyze(
            Method(compilation, "HandlerBeforeReceiverCheck"));
        var add = session.Analyze(Method(compilation, "Add"));
        var remove = session.Analyze(Method(compilation, "Remove"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                handlerFirst.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            AssertContainsThrows(
                handlerFirst.Summary,
                "System.ArgumentException");
            AssertDoesNotThrow(
                handlerFirst.Summary,
                "System.NullReferenceException");

            Assert.That(
                add.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "add handler");
            Assert.That(
                add.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True,
                "add accessor");
            AssertContainsThrows(
                add.Summary,
                "System.InvalidOperationException");

            Assert.That(
                remove.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True,
                "remove handler");
            Assert.That(
                remove.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True,
                "remove accessor");
            AssertContainsThrows(
                remove.Summary,
                "System.ApplicationException");
        }
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
            var method = EffectTestHost.SampleMethod(compilation, methodName);
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
    public void CoalesceAssignmentUpdatesLocalFactsBeforeLaterBranches()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                private static int s_state;

                public static void Calls() {
                    string? value = null;
                    value ??= "assigned";
                    if (value is not null) {
                        s_state++;
                    }
                }
            }
            """);
        var result = new EffectAnalysisSession(compilation).Analyze(
            Method(compilation, "Calls"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
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
    public void UsingValueTypesDisposeOnlyTheirAcquiredCopies()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public struct Resource : IDisposable {
                public int Value;
                public void Dispose() => Value++;
            }

            public static class Sample {
                public static void Statement(ref Resource input) {
                    using (input) { }
                }

                public static void Declaration(ref Resource input) {
                    using Resource copy = input;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] { "Statement", "Declaration" })
        {
            var result = session.Analyze(Method(compilation, methodName));
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.False,
                methodName);
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
    public void UnreachableAliasAssignmentDoesNotTaintLocalOwnership()
    {
        var result = Analyze(
            """
            public sealed class Box {
                public int Value;
            }

            public static class Sample {
                public static void Mutate(Box parameter) {
                    var local = new Box();
                    if (false) {
                        local = parameter;
                    }

                    local.Value = 1;
                }
            }
            """,
            "Sample",
            "Mutate");

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
            Is.False);
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

        AssertFreshContainerAlias(result);
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

        AssertFreshContainerAlias(result);
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

        AssertFreshContainerAlias(result);
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

        var result = EffectTestHost.AnalyzeSample(compilation, "Invoke");

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

        AssertExternalPreconditionFailure(result);
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

        AssertExternalPreconditionFailure(result);
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

        AssertExternalPreconditionFailure(result);
    }

    [Test]
    public void ClosedGenericCompanionDoesNotAffectOtherConstructions()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public sealed class Target<T> {
                public void Run(T value) { }
            }

            [ContractFor(typeof(Target<int>))]
            public static class IntTargetContracts {
                public static void Run(
                    Target<int> receiver,
                    int value) {
                    Contract.Requires(value > 0);
                }
            }

            public static class Sample {
                public static void InvokeInt(
                    Target<int> target,
                    int value) =>
                    target.Run(value);

                public static void InvokeString(
                    Target<string> target,
                    string value) =>
                    target.Run(value);
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        var intCall = session.Analyze(
            Method(compilation, "InvokeInt"));
        var stringCall = session.Analyze(
            Method(compilation, "InvokeString"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                intCall.Summary.AnalysisIncompleteReason,
                Is.EqualTo(
                    EffectAnalysisIncompleteReason
                        .CallPreconditionNotProven));
            Assert.That(intCall.Projection.IsComplete, Is.False);
            Assert.That(
                stringCall.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
            Assert.That(stringCall.Projection.IsComplete, Is.True);
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

            public class ExternalTrustFixture {
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
                [EffectContract(
                    SharpProofEffect.None,
                    Complete = true,
                    PreconditionFree = true)]
                public void InstanceBoth() {
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

        var instance = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "ExternalTrustFixture",
                "InstanceBoth"));
        Assert.That(instance.Summary.Completeness, Is.EqualTo(EffectCompleteness.Complete));
        Assert.That(instance.Projection.IsComplete, Is.True);
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
            $$"""
            using System;

            public static class Sample {
                public static void Explicit(Exception exception) => throw exception;
                {{ExceptionTestSources.CommonMethods}}
                public static uint? NullableUnsignedDivide(
                    uint? left,
                    uint? right) => left / right;
                public static uint? NullableUnsignedRemainder(
                    uint? left,
                    uint? right) => left % right;
                public static int Length(string text) => text.Length;
                public static int Index(int[] values, int index) => values[index];
                public static int CheckedAdd(int left, int right) =>
                    checked(left + right);
                public static int[] FixedArray() => new int[1];
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

        AssertParameterWritesRemap(result);
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

        AssertParameterWritesRemap(result);
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

        AssertParameterWritesRemap(result);
    }

    [Test]
    public void ByValueStructMutationStaysOnTheLocalCopy()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public struct Counter {
                public int Value;
                public void ClearValue() => Value = 0;
                public readonly int ReadValue() => Value;
                public readonly void MutateReadonlyThis() => ClearValue();
                public readonly int ReadReadonlyThis() => ReadValue();
            }

            public static class Sample {
                public static void WriteCopyField(Counter value) =>
                    value.Value = 0;
                public static void MutateCopy(Counter value) =>
                    value.ClearValue();
                public static void WriteRef(ref Counter value) =>
                    value.Value = 0;
                public static void MutateLocalCopy(ref Counter source) {
                    Counter copy = source;
                    copy.ClearValue();
                }
                public static void MutateIn(in Counter source) =>
                    source.ClearValue();
                public static int ReadIn(in Counter source) =>
                    source.ReadValue();
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var fieldCopy = session.Analyze(
            Method(compilation, "WriteCopyField")).Summary;
        var mutatorCopy = session.Analyze(
            Method(compilation, "MutateCopy")).Summary;
        var byReference = session.Analyze(
            Method(compilation, "WriteRef")).Summary;
        var localCopy = session.Analyze(
            Method(compilation, "MutateLocalCopy")).Summary;
        var mutateIn = session.Analyze(
            Method(compilation, "MutateIn")).Summary;
        var readIn = session.Analyze(
            Method(compilation, "ReadIn")).Summary;
        var mutateReadonlyThis = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Counter",
                "MutateReadonlyThis")).Summary;
        var readReadonlyThis = session.Analyze(
            EffectTestHost.RequireMethod(
                compilation,
                "Counter",
                "ReadReadonlyThis")).Summary;
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
                localCopy.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.False);
            Assert.That(
                mutateIn.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.False);
            Assert.That(
                readIn.Reads.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(
                mutateReadonlyThis.Writes.Contains(EffectRegionId.Receiver),
                Is.False);
            Assert.That(
                readReadonlyThis.Reads.Contains(EffectRegionId.Receiver),
                Is.True);
            Assert.That(
                mutableThis.Writes.Contains(EffectRegionId.Receiver),
                Is.True);
        }
    }

    [Test]
    public void RefLikeValueCopiesPreserveExternalAliases()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System.Diagnostics.CodeAnalysis;
            public ref struct RefAlias {
                private static int s_cell;
                public ref int Cell;
                public RefAlias([UnscopedRef] ref int cell) { Cell = ref cell; }
                public void Bind([UnscopedRef] ref int cell) { Cell = ref cell; }
                public void CopyTo(ref RefAlias target) {
                    target.Cell = ref Cell;
                }
                public void CopyFrom(RefAlias source) {
                    Cell = ref source.Cell;
                }
                private static ref int StaticCell() => ref s_cell;
                public void BindStatic() { Cell = ref StaticCell(); }
                private static ref int IgnoreAndReturnStatic([UnscopedRef] ref int ignored) => ref s_cell;
                public void BindMisleading([UnscopedRef] ref int cell) { Cell = ref IgnoreAndReturnStatic(ref cell); }
                public RefAlias Source { set { Cell = ref value.Cell; } }
                public int BindOnRead { get { Cell = ref StaticCell(); return 0; } }
                public int BindOnSet { get => 0; set { Cell = ref StaticCell(); } }
                public static RefAlias operator +(RefAlias value, int ignored) {
                    value.BindStatic();
                    return value;
                }
                public void Set() => Cell = 1;
                public void Dispose() => Cell = 1;
            }

            public static class Sample {
                public static void MutateValue(RefAlias value) => value.Set();
                public static void MutateIn(in RefAlias value) => value.Set();
                public static void MutateLocal(ref RefAlias source) {
                    RefAlias copy = source;
                    copy.Set();
                }
                public static void MutateConstruction(ref int cell) {
                    var alias = new RefAlias(ref cell);
                    alias.Set();
                }
                public static void DisposeValue(RefAlias value) {
                    using (value) { }
                }
                public static void BindThenMutate([UnscopedRef] ref int cell) {
                    RefAlias alias = default;
                    alias.Cell = ref cell;
                    alias.Set();
                }
                public static void CallBindThenMutate([UnscopedRef] ref int cell) {
                    RefAlias alias = default;
                    alias.Bind(ref cell);
                    alias.Set();
                }
                private static void BindStatic(
                    ref RefAlias alias,
                    [UnscopedRef] ref int cell) {
                    alias.Cell = ref cell;
                }
                public static void CallStaticBindThenMutate([UnscopedRef] ref int cell) {
                    RefAlias alias = default;
                    BindStatic(ref alias, ref cell);
                    alias.Set();
                }
                public static void BindAmbientThenMutate() {
                    RefAlias alias = default;
                    alias.BindStatic();
                    alias.Set();
                }
                public static void BindMisleadingThenMutate([UnscopedRef] ref int cell) {
                    RefAlias alias = default;
                    alias.BindMisleading(ref cell);
                    alias.Set();
                }
                public static void CopyPropertyThenMutate(RefAlias source) {
                    RefAlias target = default;
                    target.Source = source;
                    target.Set();
                }
                public static void GetterThenMutate() {
                    RefAlias alias = default;
                    _ = alias.BindOnRead;
                    alias.Set();
                }
                public static void CompoundSetterThenMutate() {
                    RefAlias alias = default;
                    alias.BindOnSet += 1;
                    alias.Set();
                }
                public static void ParameterSetterThenMutate(RefAlias alias) {
                    alias.BindOnSet = 1;
                    alias.Set();
                }
                public static void ReassignParameterThenMutate(
                    RefAlias alias,
                    RefAlias source) {
                    alias = source;
                    alias.Set();
                }
                public static void CompoundReassignThenMutate() {
                    RefAlias alias = default;
                    alias += 1;
                    alias.Set();
                }
                public static void CopyReceiverThenMutate(RefAlias source) {
                    RefAlias target = default;
                    source.CopyTo(ref target);
                    target.Set();
                }
                public static void CopyValueThenMutate(RefAlias source) {
                    RefAlias target = default;
                    target.CopyFrom(source);
                    target.Set();
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var value = session.Analyze(
            Method(compilation, "MutateValue")).Summary;
        var local = session.Analyze(
            Method(compilation, "MutateLocal")).Summary;
        var mutateIn = session.Analyze(
            Method(compilation, "MutateIn")).Summary;
        var construction = session.Analyze(
            Method(compilation, "MutateConstruction")).Summary;
        var disposal = session.Analyze(
            Method(compilation, "DisposeValue")).Summary;
        var rebound = session.Analyze(
            Method(compilation, "BindThenMutate")).Summary;
        var reboundByCall = session.Analyze(
            Method(compilation, "CallBindThenMutate")).Summary;
        var reboundByStaticCall = session.Analyze(
            Method(compilation, "CallStaticBindThenMutate")).Summary;
        var copiedFromReceiver = session.Analyze(
            Method(compilation, "CopyReceiverThenMutate")).Summary;
        var copiedFromValue = session.Analyze(
            Method(compilation, "CopyValueThenMutate")).Summary;
        var boundFromAmbient = session.Analyze(
            Method(compilation, "BindAmbientThenMutate")).Summary;
        var boundFromMisleadingCall = session.Analyze(
            Method(compilation, "BindMisleadingThenMutate")).Summary;
        var copiedByProperty = session.Analyze(
            Method(compilation, "CopyPropertyThenMutate")).Summary;
        var reboundByGetter = session.Analyze(
            Method(compilation, "GetterThenMutate")).Summary;
        var reboundByCompoundSetter = session.Analyze(
            Method(compilation, "CompoundSetterThenMutate")).Summary;
        var reboundParameter = session.Analyze(
            Method(compilation, "ParameterSetterThenMutate")).Summary;
        var reassignedParameter = session.Analyze(
            Method(compilation, "ReassignParameterThenMutate")).Summary;
        var compoundReassigned = session.Analyze(
            Method(compilation, "CompoundReassignThenMutate")).Summary;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                value.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(value.Writes.IsUnknown, Is.False);
            Assert.That(
                local.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(local.Writes.IsUnknown, Is.False);
            Assert.That(
                mutateIn.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(construction.Writes.IsUnknown, Is.True);
            Assert.That(
                disposal.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(
                rebound.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(
                reboundByCall.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(reboundByCall.Writes.IsUnknown, Is.False);
            Assert.That(
                reboundByStaticCall.Writes.Contains(
                    EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(reboundByStaticCall.Writes.IsUnknown, Is.False);
            Assert.That(
                copiedFromReceiver.Writes.Contains(
                    EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(copiedFromReceiver.Writes.IsUnknown, Is.False);
            Assert.That(
                copiedFromValue.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(copiedFromValue.Writes.IsUnknown, Is.False);
            Assert.That(
                boundFromAmbient.Writes.Contains(EffectRegionId.Static())
                || boundFromAmbient.Writes.IsUnknown,
                Is.True);
            Assert.That(
                boundFromMisleadingCall.Writes.Contains(EffectRegionId.Static())
                || boundFromMisleadingCall.Writes.IsUnknown,
                Is.True);
            Assert.That(
                copiedByProperty.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(copiedByProperty.Writes.IsUnknown, Is.False);
            Assert.That(
                reboundByGetter.Writes.Contains(EffectRegionId.Static())
                || reboundByGetter.Writes.IsUnknown,
                Is.True);
            Assert.That(
                reboundByCompoundSetter.Writes.Contains(EffectRegionId.Static())
                || reboundByCompoundSetter.Writes.IsUnknown,
                Is.True);
            Assert.That(
                reboundParameter.Writes.Contains(EffectRegionId.Static())
                || reboundParameter.Writes.IsUnknown,
                Is.True);
            Assert.That(
                reassignedParameter.Writes.Contains(
                    EffectRegionId.Parameter(1)),
                Is.True);
            Assert.That(
                compoundReassigned.Writes.Contains(EffectRegionId.Static())
                || compoundReassigned.Writes.IsUnknown,
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

                public static void ForFalseWithHeaders() {
                    for (Stored = new object(); false; Stored = new object()) {
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
        var forFalseWithHeaders = session.Analyze(
            Method(compilation, "ForFalseWithHeaders")).Summary;
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
            Assert.That(
                forFalseWithHeaders.Termination,
                Is.EqualTo(EffectTermination.Terminates));
            Assert.That(forFalseWithHeaders.Writes.IsEmpty, Is.False);
            Assert.That(
                forFalseWithHeaders.Allocation,
                Is.EqualTo(EffectAllocationKind.Managed));
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
    public void AnalyzeAllIncludesPrimaryConstructorInitializationEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Global {
                public static int State;

                public static int Touch(int value) {
                    State = value;
                    return value;
                }
            }

            public class BaseSample {
                protected BaseSample(int value) {
                }
            }

            public sealed class Sample(int value)
                : BaseSample(Global.Touch(value)) {
                private readonly int _value = Global.Touch(value);
            }
            """);
        var constructor = EffectTestHost.RequireType(compilation, "Sample")
            .InstanceConstructors
            .Single(static method => method.Parameters.Length == 1);
        var session = new EffectAnalysisSession(compilation);

        var direct = session.Analyze(constructor);
        var bulk = session.AnalyzeAll().Single(result =>
            SymbolEqualityComparer.Default.Equals(
                result.Method,
                constructor));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                direct.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                direct.Summary.Writes.Contains(EffectRegionId.Receiver),
                Is.True);
            Assert.That(bulk.Summary, Is.EqualTo(direct.Summary));
            Assert.That(bulk.DirectWitnesses, Is.EqualTo(direct.DirectWitnesses));
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
            using System.Runtime.CompilerServices;
            using System.Threading.Tasks;
            public interface IExternal { void Run(); }
            public sealed class UserException : Exception {
                public UserException(bool fail) {
                    if (fail) throw new ArgumentException();
                }
            }
            public sealed class ThrowingConstructionWithInitializer {
                public ThrowingConstructionWithInitializer() =>
                    throw new ArgumentException();
                public int Value {
                    set => throw new InvalidOperationException();
                }
            }
            public sealed class ThrowingSetter {
                public int Value {
                    get => 0;
                    set => throw new InvalidOperationException();
                }
            }
            public sealed class ThrowingGetter {
                public int Value => throw new InvalidOperationException();
            }
            public record ThrowingCloneRecord {
                public ThrowingCloneRecord() { }
                protected ThrowingCloneRecord(ThrowingCloneRecord other) =>
                    throw new InvalidOperationException();
                public int Value { get; init; }
            }
            public record DivergingCloneRecord {
                public DivergingCloneRecord() { }
                protected DivergingCloneRecord(DivergingCloneRecord other) {
                    while (true) { }
                }
                public int Value { get; init; }
            }
            public sealed class ThrowingDeconstruction {
                public void Deconstruct(out int left, out int right) {
                    left = right = 0;
                    throw new InvalidOperationException();
                }
            }
            public sealed class DivergingDeconstruction {
                public void Deconstruct(out int left, out int right) {
                    left = right = 0;
                    while (true) { }
                }
            }
            public sealed class DivergingDeconstructionTarget {
                public int Value { set { while (true) { } } }
            }
            public readonly struct PatternBomb {
                public int Value { get { while (true) { } } }
            }
            public sealed class ReferencePatternBomb {
                public int Value { get { while (true) { } } }
            }
            public sealed class ReferencePositionalPatternBomb {
                public void Deconstruct(out int value) {
                    value = 0;
                    while (true) { }
                }
            }
            public sealed class ReferenceListPatternBomb {
                public int Length { get { while (true) { } } }
                public int this[int index] => 0;
            }
            public sealed class ReferenceIndexerPatternBomb {
                public int Length => 1;
                public int this[int index] { get { while (true) { } } }
            }
            public sealed class ReferenceSlicePatternBomb {
                public int Length => 1;
                public int this[int index] => 0;
                public ReferenceSlicePatternBomb Slice(int start, int length) {
                    while (true) { }
                }
            }
            public sealed class VariableLengthSlicePatternBomb {
                private readonly int length;
                public VariableLengthSlicePatternBomb(int length) {
                    this.length = length;
                }
                public int Length => length;
                public int this[int index] => 0;
                public VariableLengthSlicePatternBomb Slice(
                    int start,
                    int sliceLength) {
                    while (true) { }
                }
            }
            public readonly struct VariableLengthSlicePatternStructBomb {
                private readonly int length;
                public VariableLengthSlicePatternStructBomb(int length) {
                    this.length = length;
                }
                public int Length => length;
                public int this[int index] => 0;
                public VariableLengthSlicePatternStructBomb Slice(
                    int start,
                    int sliceLength) {
                    while (true) { }
                }
            }
            public readonly struct NestedListPatternBomb {
                public int Length => 1;
                public int this[int index] { get { while (true) { } } }
            }
            public class VirtualLengthPatternBase {
                public virtual int Length => 1;
                public int this[int index] { get { while (true) { } } }
            }
            public sealed class VirtualLengthPatternDerived :
                VirtualLengthPatternBase {
                public override int Length => 0;
            }
            public sealed class ThrowingListLengthPattern {
                public int Length => throw new InvalidOperationException();
                public int this[int index] => 0;
            }
            public sealed class ThrowingListIndexerPattern {
                public int Length => 1;
                public int this[int index] =>
                    throw new ApplicationException();
            }
            public sealed class ThrowingListSlicePattern {
                public int Length => 1;
                public int this[int index] => 0;
                public ThrowingListSlicePattern Slice(int start, int length) =>
                    throw new ArgumentException();
            }
            public sealed class EmptyThrowingListIndexerPattern {
                public int Length => 0;
                public int this[int index] =>
                    throw new ApplicationException();
            }
            public sealed class ThrowingLengthAndIndexerPattern {
                public int Length => throw new InvalidOperationException();
                public int this[int index] =>
                    throw new ApplicationException();
            }
            public sealed class ReceiverMutatingListPattern {
                private int state;
                public int Length => 1;
                public int this[int index] {
                    get { state++; return 0; }
                }
            }
            public sealed class NestedListPatternHolder {
                public ReceiverMutatingListPattern Child { get; } = new();
            }
            public sealed class NonNullNestedSliceListPatternBomb {
                public int Length { get { while (true) { } } }
                public int this[int index] => 0;
            }
            public sealed class NonNullSliceOuterPattern {
                public int Length => 1;
                public int this[int index] => 0;
                public NonNullNestedSliceListPatternBomb Slice(
                    int start,
                    int length) => new();
            }
            public sealed class NullTarget {
                public int Value;
                public void Touch() { }
                public void TouchValue(int value) { }
                public int SetOnly { set { } }
                public void Fail() => throw new InvalidOperationException();
            }
            public sealed class EventTarget {
                public event Action Changed { add { } remove { } }
            }
            public sealed class NullAwaitable {
                public NullAwaiter GetAwaiter() => null!;
            }
            public sealed class NullAwaiter : INotifyCompletion {
                public bool IsCompleted => true;
                public void OnCompleted(Action continuation) { }
                public void GetResult() { }
            }
            public sealed class StaticBomb {
                static StaticBomb() => throw new ApplicationException();
                public StaticBomb() { }
                public static int Value;
                public static int Property { set { } }
            }
            public sealed class DivergingStaticBomb {
                static DivergingStaticBomb() { while (true) { } }
                public static int Value => 0;
            }
            public static class BeforeFieldInitBomb {
                private static readonly int Value = FailInitialization();
                private static int FailInitialization() =>
                    throw new ApplicationException();
                public static int Read() => Value;
                public static void Run() =>
                    throw new InvalidOperationException();
            }
            public static class ExternalInitializationState {
                public static int Value;
                public static void Mark() => Value++;
            }
            public static class SameTypeBeforeFieldInitBomb {
                private static readonly int Value = FailInitialization();
                private static int FailInitialization() =>
                    throw new ApplicationException();
                public static void CatchInitialization() {
                    try { _ = Value; }
                    catch (TypeInitializationException) {
                        ExternalInitializationState.Mark();
                    }
                }
                public static void AfterInitialization() {
                    _ = Value;
                    ExternalInitializationState.Mark();
                }
            }
            public sealed class ThrowingStaticConstruction {
                static ThrowingStaticConstruction() =>
                    throw new ApplicationException();
                public ThrowingStaticConstruction() =>
                    throw new InvalidOperationException();
            }
            public static class Extensions {
                static Extensions() => throw new ApplicationException();
                public static void Touch(this object value) { }
                public static ExtensionEnumerator GetEnumerator(
                    this ExtensionSequence value) => default;
            }
            public static class DivergingExtensions {
                static DivergingExtensions() { while (true) { } }
                public static void TouchDiverging(this object value) { }
            }
            public sealed class ExtensionSequence { }
            public struct ExtensionEnumerator {
                public bool MoveNext() => false;
                public int Current => 0;
            }
            public sealed class GenericStaticBomb<T> {
                static GenericStaticBomb() {
                    if (typeof(T) == typeof(string))
                        throw new ApplicationException();
                }
                public static int Value;
                private static int s_genericState;
                public static void GenericStaticProbe() {
                    try { _ = GenericStaticBomb<string>.Value; }
                    catch (TypeInitializationException) { s_genericState++; }
                }
            }
            public readonly struct ThrowingOperator {
                public static ThrowingOperator operator +(
                    ThrowingOperator left,
                    ThrowingOperator right) =>
                    throw new InvalidOperationException();
            }
            public readonly struct ThrowingStaticOperator {
                static ThrowingStaticOperator() =>
                    throw new ApplicationException();
                public static ThrowingStaticOperator operator +(
                    ThrowingStaticOperator left,
                    ThrowingStaticOperator right) => default;
            }
            public readonly struct NonThrowingDivide {
                public static NonThrowingDivide operator /(
                    NonThrowingDivide left,
                    NonThrowingDivide right) => default;
            }
            public readonly struct ShortCircuitGate {
                public static bool operator false(ShortCircuitGate value) =>
                    true;
                public static bool operator true(ShortCircuitGate value) =>
                    false;
                public static ShortCircuitGate operator &(
                    ShortCircuitGate left,
                    ShortCircuitGate right) => left;
            }
            public sealed class ThrowingCompoundSetter {
                public ThrowingOperator Item {
                    get => default;
                    set => throw new ApplicationException();
                }
            }
            public sealed class ThrowingResource : IDisposable {
                public void Dispose() =>
                    throw new InvalidOperationException();
            }
            public sealed class DivergingResource : IDisposable {
                public void Dispose() { while (true) { } }
            }
            public sealed class ApplicationThrowingResource : IDisposable {
                public void Dispose() => throw new ApplicationException();
            }
            public sealed class ThrowingMutatingResource : IDisposable {
                private int _state;
                public void Dispose() {
                    _state++;
                    throw new InvalidOperationException();
                }
            }
            public sealed class RecursiveResource : IDisposable {
                public void Dispose() => Dispose();
            }
            public sealed class RecursiveThrowingResource : IDisposable {
                private static bool s_throw;
                public void Dispose() {
                    if (s_throw) throw new InvalidOperationException();
                    Dispose();
                }
            }
            public sealed class ThrowingSequence {
                public Enumerator GetEnumerator() =>
                    throw new InvalidOperationException();
                public struct Enumerator {
                    public bool MoveNext() => false;
                    public int Current => 0;
                }
            }
            public sealed class ThrowingMoveNextSequence {
                public Enumerator GetEnumerator() => new Enumerator();
                public struct Enumerator {
                    public bool MoveNext() =>
                        throw new InvalidOperationException();
                    public int Current =>
                        throw new ApplicationException();
                }
            }
            public sealed class NullEnumeratorSequence {
                public Enumerator GetEnumerator() => null!;
                public sealed class Enumerator {
                    public bool MoveNext() => false;
                    public int Current => 0;
                }
            }
            public static class Sample {
                private static int s_state;
                public static void EmptyTry() { try { } catch { s_state++; } }
                public static void NoThrowTry() { try { var value = 1; value++; } catch { s_state++; } }
                public static void KnownThrow() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) { s_state++; } }
                public static void UnknownThrow(IExternal external) { try { external.Run(); } catch (Exception) { s_state++; } }
                public static void FalseFilter() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) when (false) { s_state++; } }
                public static void TrueFilter() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) when (true) { s_state++; } }
                public static void OrderedHierarchy() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) { } catch (Exception) { s_state++; } }
                public static void MismatchedCatch() { try { throw new InvalidOperationException(); } catch (ArgumentException) { } s_state++; }
                public static void FinallyAfterFailure() { Fail(); try { } finally { s_state++; } }
                public static void FinallyAfterDivergence() { try { Spin(); } finally { s_state++; } }
                public static void AfterNonreturningFinally() { try { } finally { Spin(); } s_state++; }
                public static void AfterConstantNonreturningFinally() { try { } finally { _ = true ? SpinInteger() : 0; } s_state++; }
                public static void AfterShortCircuitedFinally() { try { } finally { _ = false && SpinInteger() == 0; } s_state++; }
                private static ShortCircuitGate SpinGate() { while (true) { } }
                public static void AfterUserShortCircuitedFinally() { try { } finally { _ = new ShortCircuitGate() && SpinGate(); } s_state++; }
                public static void AfterNonreturningArgument() { Sink(SpinInteger()); s_state++; }
                private static void Sink(int value) { }
                private static int SpinInteger() { while (true) { } }
                public static void AfterNestedNonreturningFinally() { try { try { } finally { _ = 1; } } finally { Spin(); } s_state++; }
                public static void NestedFinallyAfterDivergence() { try { try { throw new InvalidOperationException(); } finally { Spin(); } } finally { s_state++; } }
                public static void BranchedFinallyAfterDivergence(bool condition) { try { if (condition) Spin(); else Spin(); } finally { s_state++; } }
                public static void InnerFinallyCaughtOutside() { try { try { throw new InvalidOperationException(); } finally { s_state++; } } catch (InvalidOperationException) { } }
                public static int ReturnThroughFinally() { try { return 1; } finally { s_state++; } }
                private static void Fail() => throw new InvalidOperationException();
                private static void Spin() { while (true) { } }
                public static void ThrowOperandFailure() { try { throw Make(); } catch (ArgumentException) { s_state++; } }
                public static void MismatchedThrowOperandFailure() { try { throw Make(); } catch (InvalidOperationException) { s_state++; } }
                private static Exception Make() => throw new ArgumentException();
                public static void DivergentThrowOperand() { try { throw SpinException(); } catch { s_state++; } }
                private static Exception SpinException() { while (true) { } }
                public static void ConstructorOperandFailure(bool fail) { try { throw new UserException(fail); } catch (ArgumentException) { s_state++; } catch (UserException) { } }
                public static void ConstructorNotReached() { try { _ = new UserException(ThrowBoolean()); } catch (ArgumentException) { s_state++; } catch (InvalidOperationException) { } }
                public static void CheckedConversionAfterFailure() { try { _ = checked((int)ThrowLong()); } catch (OverflowException) { s_state++; } catch (ArgumentException) { } }
                public static void DivisionAfterFailure() { try { _ = ThrowInteger() / 0; } catch (DivideByZeroException) { s_state++; } catch (ArgumentException) { } }
                public static void UserDivisionHasNoIntrinsicFailure(NonThrowingDivide left, NonThrowingDivide right) { try { _ = left / right; } catch (DivideByZeroException) { s_state++; } }
                public static void CheckedBinaryOverflow(int value) { try { _ = checked(int.MaxValue + value); } catch (OverflowException) { s_state++; } }
                public static void CheckedUnaryOverflow(int value) { try { _ = checked(-value); } catch (OverflowException) { s_state++; } }
                public static void CheckedCompoundOverflow(int value) { try { var total = int.MaxValue; checked { total += value; } } catch (OverflowException) { s_state++; } }
                private static long ThrowLong() => throw new ArgumentException();
                private static int ThrowInteger() => throw new ArgumentException();
                public static void InitializerAfterThrowingConstructor() { try { _ = new ThrowingConstructionWithInitializer { Value = 1 }; } catch (InvalidOperationException) { s_state++; } catch (ArgumentException) { } }
                private static bool ThrowBoolean() => throw new InvalidOperationException();
                public static void OperatorFailure(ThrowingOperator left, ThrowingOperator right) { try { _ = left + right; } catch (InvalidOperationException) { s_state++; } }
                public static void CompoundSetterNotReached(ThrowingCompoundSetter box, ThrowingOperator value) { try { box.Item += value; } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void UsingDisposalFailure(ThrowingResource resource) { try { using (resource) { } } catch (InvalidOperationException) { s_state++; } }
                public static void UsingDisposalAfterDivergence(ThrowingResource resource) { try { using (resource) { Spin(); } } catch (InvalidOperationException) { s_state++; } }
                public static void UsingDeclarationAfterDivergence(ThrowingResource resource) { try { using var value = resource; Spin(); } catch (InvalidOperationException) { s_state++; } }
                public static void UsingDeclarationGotoSkipsDivergence(ThrowingResource resource) { try { using var value = resource; goto Done; Spin(); Done: ; } catch (InvalidOperationException) { s_state++; } }
                public static void UsingDeclarationGotoBeforeLifetime(ThrowingResource resource) { try { Retry: ; { using var value = resource; } goto Retry; } catch (InvalidOperationException) { s_state++; } }
                public static void UsingDeclarationGotoInsideLifetimeThenDiverges(ThrowingResource resource) { try { using var value = resource; Retry: ; goto Retry; } catch (InvalidOperationException) { s_state++; } }
                public static void UsingInitialAcquisitionFails() { try { using ThrowingResource first = FailResource(), second = new ThrowingResource(); } catch (InvalidOperationException) { s_state++; } catch (ArgumentException) { } }
                public static void UsingLaterAcquisitionFails(ThrowingResource resource) { try { using ThrowingResource first = resource, second = FailResource(); } catch (InvalidOperationException) { s_state++; } catch (ArgumentException) { } }
                public static void UsingLaterAcquisitionFailsBeforeDivergentBody(ThrowingResource resource) { try { using (ThrowingResource first = resource, second = FailResource()) { Spin(); } } catch (InvalidOperationException) { s_state++; } catch (ArgumentException) { } }
                public static void LaterDeclarationDivergesBeforeEarlierDispose(ThrowingResource outer, DivergingResource inner) { try { using var first = outer; using var second = inner; } catch (InvalidOperationException) { s_state++; } }
                public static void LaterDeclaratorDivergesBeforeEarlierDispose(ThrowingResource outer, DivergingResource inner) { try { using (IDisposable first = outer, second = inner) { } } catch (InvalidOperationException) { s_state++; } }
                public static void LaterDisposeThrowsThenEarlierDisposeRuns(ThrowingResource outer, ApplicationThrowingResource inner) { try { using var first = outer; using var second = inner; } catch (InvalidOperationException) { s_state++; } catch (ApplicationException) { } }
                public static void ThrowingDeclaratorsUnwindInReverse(ThrowingMutatingResource outer, ThrowingMutatingResource inner) { using (ThrowingMutatingResource first = outer, second = inner) { } }
                public static void RecursiveDeclaratorDoesNotUnwindOuter(ThrowingMutatingResource outer, RecursiveResource inner) { using (IDisposable first = outer, second = inner) { } }
                public static void RecursiveThrowingDeclaratorMayUnwindOuter(ThrowingMutatingResource outer, RecursiveThrowingResource inner) { using (IDisposable first = outer, second = inner) { } }
                private static ThrowingResource FailResource() => throw new ArgumentException();
                public static void ForeachAcquisitionFailure(ThrowingSequence values) { try { foreach (var value in values) { _ = value; } } catch (InvalidOperationException) { s_state++; } }
                public static void ForeachNullReceiverFailure() { ThrowingSequence values = null!; try { foreach (var value in values) { _ = value; } } catch (NullReferenceException) { s_state++; } }
                public static void ForeachCurrentAfterMoveNextFailure(ThrowingMoveNextSequence values) { try { foreach (var value in values) { _ = value; } } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void ForeachNullEnumeratorFailure(NullEnumeratorSequence values) { try { foreach (var value in values) { _ = value; } } catch (NullReferenceException) { s_state++; } }
                public static void ForeachExtensionInitializationFailure(ExtensionSequence values) { try { foreach (var value in values) { _ = value; } } catch (TypeInitializationException) { s_state++; } }
                public static void UnreachableWhileCatch() { try { while (false) { throw new InvalidOperationException(); } } catch (InvalidOperationException) { s_state++; } }
                public static void UnreachableForCatch() { try { for (; false;) { throw new InvalidOperationException(); } } catch (InvalidOperationException) { s_state++; } }
                public static void ShortCircuitedAndCatch() { try { _ = false && FailBoolean(); } catch (InvalidOperationException) { s_state++; } }
                public static void ShortCircuitedOrCatch() { try { _ = true || FailBoolean(); } catch (InvalidOperationException) { s_state++; } }
                public static void ConstantSwitchExpressionCatch() { try { _ = 0 switch { 1 => ThrowObject(), _ => new object() }; } catch (InvalidOperationException) { s_state++; } }
                public static void ConstantUnmatchedSwitchExpressionCatch() { try { _ = 0 switch { 1 => 1 }; } catch (System.Runtime.CompilerServices.SwitchExpressionException) { s_state++; } }
                public static void ConstantMatchedNonExhaustiveSwitchCatch() { try { _ = 0 switch { 0 => 1 }; } catch (System.Runtime.CompilerServices.SwitchExpressionException) { s_state++; } }
                public static void ExhaustiveTypePatternSwitchCatch() { try { _ = 0 switch { int value => value }; } catch (System.Runtime.CompilerServices.SwitchExpressionException) { s_state++; } }
                public static void ConstantRelationalSwitchCatch() { try { _ = 0 switch { >= 0 => new object(), _ => ThrowObject() }; } catch (InvalidOperationException) { s_state++; } }
                public static void NaNSingleRelationalSwitchCatch() { try { _ = float.NaN switch { < 0f => ThrowObject(), _ => new object() }; } catch (InvalidOperationException) { s_state++; } }
                public static void NaNDoubleRelationalSwitchCatch() { try { _ = double.NaN switch { < 0d => ThrowObject(), _ => new object() }; } catch (InvalidOperationException) { s_state++; } }
                public static void ConstantLogicalSwitchExpressionCatch() { try { _ = 0 switch { not 1 => new object(), _ => ThrowObject() }; } catch (InvalidOperationException) { s_state++; } }
                public static void AfterConstantUnmatchedSwitchExpression() { _ = 0 switch { 1 => 1 }; s_state++; }
                public static void ConstantSwitchStatementCatch() { try { switch (0) { case 1: ThrowObject(); break; default: break; } } catch (InvalidOperationException) { s_state++; } }
                public static void ConstantRelationalSwitchStatementCatch() { try { switch (0) { case >= 0: break; default: ThrowObject(); break; } } catch (InvalidOperationException) { s_state++; } }
                public static void NaNSwitchStatementCatch() { try { switch (double.NaN) { case < 0d: ThrowObject(); break; default: break; } } catch (InvalidOperationException) { s_state++; } }
                public static void ConstantLogicalSwitchStatementCatch() { try { switch (0) { case not 1: break; default: ThrowObject(); break; } } catch (InvalidOperationException) { s_state++; } }
                public static void TotalSliceSwitchExpressionGuardCatch(Span<int> value) { try { _ = value switch { [..] when ThrowBoolean() => 1, _ => ThrowInteger() }; } catch (ArgumentException) { s_state++; } catch (InvalidOperationException) { } }
                public static void TotalSliceSwitchStatementGuardCatch(Span<int> value) { try { switch (value) { case [..] when ThrowBoolean(): break; default: ThrowInteger(); break; } } catch (ArgumentException) { s_state++; } catch (InvalidOperationException) { } }
                public static void ThrowingSwitchExpressionGuard() { try { _ = 0 switch { 0 when ThrowBoolean() => new object(), _ => ThrowApplicationObject() }; } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void AfterThrowingTotalSwitchGuard(int value) { _ = value switch { _ when ThrowBoolean() => 1, _ => 2 }; s_state++; }
                public static void VarPatternThrowingGuardBeforeFallback(int value) { try { _ = value switch { var captured when ThrowBoolean() => 1, _ => ThrowInteger() }; } catch (ArgumentException) { s_state++; } catch (InvalidOperationException) { } }
                public static void AfterDivergingPropertyPattern() { _ = new PatternBomb() switch { { Value: 0 } => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingReferencePropertyPattern() { _ = new ReferencePatternBomb() switch { { Value: 0 } => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingReferencePositionalPattern() { _ = new ReferencePositionalPatternBomb() switch { ReferencePositionalPatternBomb(0) => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingReferenceListPattern() { _ = new ReferenceListPatternBomb() switch { [] => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingReferenceIndexerPattern() { _ = new ReferenceIndexerPatternBomb() switch { [0] => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingParenthesizedIndexerPattern() { _ = (new ReferenceIndexerPatternBomb()) switch { [0] => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingReferenceSlicePattern() { _ = new ReferenceSlicePatternBomb() switch { [.. var rest] => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingVariableLengthSlicePattern(int length) { _ = new VariableLengthSlicePatternBomb(length) switch { [.. var rest] => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingNestedSlicePattern(VariableLengthSlicePatternStructBomb value) { _ = value switch { [.. { Length: 0 }] => 1, _ => 2 }; s_state++; }
                public static void VirtualLengthMismatchCompletes() { _ = ((VirtualLengthPatternBase)new VirtualLengthPatternDerived()) switch { [0] => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingNestedListPattern() { _ = new NestedListPatternBomb[2] switch { [[0], _] => 1, _ => 2 }; s_state++; }
                public static void ThrowingListLengthCatch(ThrowingListLengthPattern value) { if (value is null) return; try { _ = value switch { [] => 1, _ => 2 }; } catch (InvalidOperationException) { s_state++; } }
                public static void ThrowingListIndexerCatch() { try { _ = new ThrowingListIndexerPattern() switch { [0] => 1, _ => 2 }; } catch (ApplicationException) { s_state++; } }
                public static void ThrowingListSliceCatch() { try { _ = new ThrowingListSlicePattern() switch { [.. var rest] => 1, _ => 2 }; } catch (ArgumentException) { s_state++; } }
                public static void LengthMismatchSkipsIndexerCatch() { try { _ = new EmptyThrowingListIndexerPattern() switch { [0] => 1, _ => 2 }; } catch (ApplicationException) { s_state++; } }
                public static void NullListSkipsLengthCatch() { try { _ = ((ThrowingListLengthPattern)null!) switch { [] => 1, _ => 2 }; } catch (InvalidOperationException) { s_state++; } }
                public static void ThrowingLengthSkipsIndexerCatch() { try { _ = new ThrowingLengthAndIndexerPattern() switch { [0] => 1, _ => 2 }; } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static bool NestedListReceiverWrite(NestedListPatternHolder value) => value is { Child: [0] };
                public static void AfterDivergingNonNullNestedSliceList() { _ = new NonNullSliceOuterPattern() switch { [.. []] => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingNegatedPattern() { _ = new PatternBomb() switch { not { Value: 0 } => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingAndPattern() { _ = new PatternBomb() switch { { Value: 0 } and _ => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingOrPattern() { _ = new ReferencePatternBomb() switch { null or { Value: 0 } => 1, _ => 2 }; s_state++; }
                public static void AfterDivergingNotNullAndPattern() { _ = new ReferencePatternBomb() switch { not null and { Value: 0 } => 1, _ => 2 }; s_state++; }
                public static void ThrowingSwitchStatementGuard() { try { switch (0) { case 0 when ThrowBoolean(): break; default: ThrowApplicationObject(); break; } } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void VarPatternThrowingSwitchGuard(int value) { try { switch (value) { case var captured when ThrowBoolean(): break; default: ThrowApplicationObject(); break; } } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void ThrowingSwitchStatementGuardBeforeGoto() { try { switch (0) { case 0 when ThrowBoolean(): goto default; default: ThrowApplicationObject(); break; } } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void ThrowingSwitchBodyBeforeGoto() { try { switch (0) { case 0: Fail(); goto default; default: ThrowApplicationObject(); break; } } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void SwitchGotoOrdinaryLabel() { try { switch (0) { case 0: goto Done; throw new InvalidOperationException(); Done: ThrowApplicationObject(); break; } } catch (ApplicationException) { s_state++; } }
                public static void GotoNestedOrdinaryLabel() { try { goto Inner; Outer: Inner: ; ThrowApplicationObject(); } catch (ApplicationException) { s_state++; } }
                public static void AfterNonreturningCallCatch() { try { Fail(); throw new ApplicationException(); } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void NestedSwallowedCatch() { try { try { throw new InvalidOperationException(); } catch (InvalidOperationException) { } } catch (InvalidOperationException) { s_state++; } }
                public static void NestedUnreachableHandler() { try { try { throw new InvalidOperationException(); } catch (ArgumentException) { throw new ApplicationException(); } } catch (ApplicationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void NestedRethrowCatch() { try { try { throw new InvalidOperationException(); } catch (InvalidOperationException) { throw; } } catch (InvalidOperationException) { s_state++; } }
                public static void UnreachableNestedRethrowCatch() { try { try { throw new InvalidOperationException(); } catch (InvalidOperationException) { Spin(); throw; } } catch (InvalidOperationException) { s_state++; } }
                public static void NestedFinallyAfterPureDivergence() { try { try { Spin(); } finally { throw new ApplicationException(); } } catch (ApplicationException) { s_state++; } }
                public static void UsingDeclarationNestedBreakThenDivergence(ThrowingResource resource) { try { using var value = resource; while (true) { break; } Spin(); } catch (InvalidOperationException) { s_state++; } }
                public static void UsingDeclarationInternalGotoThenDivergence(ThrowingResource resource) { try { using var value = resource; goto Loop; Loop: Spin(); } catch (InvalidOperationException) { s_state++; } }
                public static void NonNullCoalesceAssignment() { object value = new object(); try { value ??= ThrowObject(); } catch (InvalidOperationException) { s_state++; } }
                public static void NullConditionalAccess() { NullTarget? value = null; try { value?.Fail(); } catch (InvalidOperationException) { s_state++; } }
                public static void UnreachableReturnAfterDivergence(ThrowingResource resource) { try { using var value = resource; if (true) { Spin(); return; } } catch (InvalidOperationException) { s_state++; } }
                public static void ReturnBlockedByDivergentFinally(ThrowingResource resource) { try { using var value = resource; try { return; } finally { Spin(); } } catch (InvalidOperationException) { s_state++; } }
                public static void ReturnThroughCompletingFinally(ThrowingResource resource) { try { using var value = resource; try { return; } finally { _ = 1; } } catch (InvalidOperationException) { s_state++; } }
                private static object ThrowObject() => throw new InvalidOperationException();
                private static object ThrowApplicationObject() => throw new ApplicationException();
                private static bool FailBoolean() => throw new InvalidOperationException();
                public static void SetterFailure(ThrowingSetter value) { try { value.Value = 1; } catch (InvalidOperationException) { s_state++; } }
                public static void NullReceiverFailure() { NullTarget value = null!; try { value.Touch(); } catch (NullReferenceException) { s_state++; } }
                public static void NullReceiverAfterThrowingArgument() { NullTarget value = null!; try { value.TouchValue(ThrowInteger()); } catch (NullReferenceException) { s_state++; } catch (ArgumentException) { } }
                public static void ThrowingRhsBeforeNullSetter() { NullTarget value = null!; try { value.SetOnly = ThrowInteger(); } catch (ArgumentException) { s_state++; } catch (NullReferenceException) { } }
                public static void NullFieldFailure() { NullTarget value = null!; try { _ = value.Value; } catch (NullReferenceException) { s_state++; } }
                public static void NullArrayCannotReachBoundsCatch() { int[] values = null!; try { _ = values[0]; } catch (IndexOutOfRangeException) { s_state++; } catch (NullReferenceException) { } }
                public static void StaticInitializationFailure() { try { _ = StaticBomb.Value; } catch (TypeInitializationException) { s_state++; } }
                public static void AfterDivergingStaticInitialization() { try { } finally { _ = DivergingStaticBomb.Value; } s_state++; }
                public static void BeforeFieldInitMethodMayRun() { try { BeforeFieldInitBomb.Run(); } catch (InvalidOperationException) { s_state++; } catch (TypeInitializationException) { } }
                public static void StaticInitializationWrongCatch() { try { _ = StaticBomb.Value; } catch (ApplicationException) { s_state++; } catch (TypeInitializationException) { } }
                public static void ConstructionAfterFailingStaticInitialization() { try { _ = new ThrowingStaticConstruction(); } catch (InvalidOperationException) { s_state++; } catch (TypeInitializationException) { } }
                public static void StaticOperatorInitializationFailure(ThrowingStaticOperator left, ThrowingStaticOperator right) { try { _ = left + right; } catch (TypeInitializationException) { s_state++; } }
                public static void ConstructionInitializationFailure() { try { _ = new StaticBomb(); } catch (TypeInitializationException) { s_state++; } }
                public static void StaticPropertyInitializationAfterRhs() { try { StaticBomb.Property = ThrowInteger(); } catch (TypeInitializationException) { s_state++; } catch (ArgumentException) { } }
                public static void StaticFieldInitializationAfterRhs() { try { StaticBomb.Value = ThrowInteger(); } catch (TypeInitializationException) { s_state++; } catch (ArgumentException) { } }
                public static void ExtensionInitializationAfterReceiver() { try { ThrowObject().Touch(); } catch (TypeInitializationException) { s_state++; } catch (InvalidOperationException) { } }
                public static void AfterDivergingExtensionInitialization() { new object().TouchDiverging(); s_state++; }
                public static void NameofOperandIsCompileTime(ThrowingGetter value) { try { _ = nameof(value.Value); } catch (InvalidOperationException) { s_state++; } }
                public static void WithCloneFailure(ThrowingCloneRecord value) { try { _ = value with { }; } catch (InvalidOperationException) { s_state++; } }
                public static void AfterDivergingWithClone(DivergingCloneRecord value) { _ = value with { Value = 1 }; s_state++; }
                public static void DeconstructionFailure(ThrowingDeconstruction value) { try { var (left, right) = value; _ = left + right; } catch (InvalidOperationException) { s_state++; } }
                public static void AfterDivergingDeconstruction(DivergingDeconstruction value) { var (left, right) = value; _ = left + right; s_state++; }
                public static void AfterDivergingDeconstructionSetter(DivergingDeconstructionTarget target) { int ignored; (target.Value, ignored) = (1, 2); s_state++; }
                public static void NullLockWrongCatch() { object gate = null!; try { lock (gate) { } } catch (NullReferenceException) { s_state++; } catch (ArgumentNullException) { } }
                public static void NullLockCorrectCatch() { object gate = null!; try { lock (gate) { } } catch (ArgumentNullException) { s_state++; } }
                public static void NullEventWrongCatch() { EventTarget value = null!; Action handler = FailHandler; try { value.Changed += handler; } catch (InvalidOperationException) { s_state++; } catch (NullReferenceException) { } }
                public static void NullEventCorrectCatch() { EventTarget value = null!; Action handler = FailHandler; try { value.Changed += handler; } catch (NullReferenceException) { s_state++; } }
                public static async Task NullAwaitWrongCatch() { Task value = null!; try { await value; } catch (InvalidOperationException) { s_state++; } catch (NullReferenceException) { } }
                public static async Task NullAwaitCorrectCatch() { Task value = null!; try { await value; } catch (NullReferenceException) { s_state++; } }
                public static async Task NullAwaitAfterThrowingOperand() { try { await ThrowTask(); } catch (NullReferenceException) { s_state++; } catch (ArgumentException) { } }
                public static async Task NullCustomAwaiterCatch() { try { await new NullAwaitable(); } catch (NullReferenceException) { s_state++; } }
                private static Task ThrowTask() => throw new ArgumentException();
                private static void FailHandler() { }
                public static void ThrowingFilter() { try { throw new InvalidOperationException(); } catch (InvalidOperationException) when (Filter()) { s_state++; } }
                private static bool Filter() => throw new ApplicationException();
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
            Assert.That(HasStaticWrite("MismatchedCatch"), Is.False);
            Assert.That(HasStaticWrite("FinallyAfterFailure"), Is.False);
            Assert.That(HasStaticWrite("FinallyAfterDivergence"), Is.False);
            Assert.That(HasStaticWrite("AfterNonreturningFinally"), Is.False);
            Assert.That(
                HasStaticWrite("AfterConstantNonreturningFinally"),
                Is.False);
            Assert.That(HasStaticWrite("AfterShortCircuitedFinally"), Is.True);
            Assert.That(
                HasStaticWrite("AfterUserShortCircuitedFinally"),
                Is.True);
            Assert.That(HasStaticWrite("AfterNonreturningArgument"), Is.False);
            Assert.That(
                HasStaticWrite("AfterNestedNonreturningFinally"),
                Is.False);
            Assert.That(HasStaticWrite("NestedFinallyAfterDivergence"), Is.False);
            Assert.That(HasStaticWrite("BranchedFinallyAfterDivergence"), Is.False);
            Assert.That(HasStaticWrite("InnerFinallyCaughtOutside"), Is.True);
            Assert.That(HasStaticWrite("ReturnThroughFinally"), Is.True);
            Assert.That(HasStaticWrite("ThrowOperandFailure"), Is.True);
            Assert.That(HasStaticWrite("MismatchedThrowOperandFailure"), Is.False);
            Assert.That(HasStaticWrite("DivergentThrowOperand"), Is.False);
            Assert.That(HasStaticWrite("ConstructorOperandFailure"), Is.True);
            Assert.That(HasStaticWrite("ConstructorNotReached"), Is.False);
            Assert.That(HasStaticWrite("CheckedConversionAfterFailure"), Is.False);
            Assert.That(HasStaticWrite("DivisionAfterFailure"), Is.False);
            Assert.That(
                HasStaticWrite("UserDivisionHasNoIntrinsicFailure"),
                Is.False);
            Assert.That(HasStaticWrite("CheckedBinaryOverflow"), Is.True);
            Assert.That(HasStaticWrite("CheckedUnaryOverflow"), Is.True);
            Assert.That(HasStaticWrite("CheckedCompoundOverflow"), Is.True);
            Assert.That(HasStaticWrite("InitializerAfterThrowingConstructor"), Is.False);
            Assert.That(HasStaticWrite("OperatorFailure"), Is.True);
            Assert.That(HasStaticWrite("CompoundSetterNotReached"), Is.False);
            Assert.That(HasStaticWrite("UsingDisposalFailure"), Is.True);
            Assert.That(HasStaticWrite("UsingDisposalAfterDivergence"), Is.False);
            Assert.That(HasStaticWrite("UsingDeclarationAfterDivergence"), Is.False);
            Assert.That(HasStaticWrite("UsingDeclarationGotoSkipsDivergence"), Is.True);
            Assert.That(HasStaticWrite("UsingDeclarationGotoBeforeLifetime"), Is.True);
            Assert.That(HasStaticWrite("UsingDeclarationGotoInsideLifetimeThenDiverges"), Is.False);
            Assert.That(HasStaticWrite("UsingInitialAcquisitionFails"), Is.False);
            Assert.That(HasStaticWrite("UsingLaterAcquisitionFails"), Is.True);
            Assert.That(HasStaticWrite("UsingLaterAcquisitionFailsBeforeDivergentBody"), Is.True);
            Assert.That(HasStaticWrite("LaterDeclarationDivergesBeforeEarlierDispose"), Is.False);
            Assert.That(HasStaticWrite("LaterDeclaratorDivergesBeforeEarlierDispose"), Is.False);
            Assert.That(HasStaticWrite("LaterDisposeThrowsThenEarlierDisposeRuns"), Is.True);
            Assert.That(HasStaticWrite("ForeachAcquisitionFailure"), Is.True);
            Assert.That(HasStaticWrite("ForeachNullReceiverFailure"), Is.True);
            Assert.That(HasStaticWrite("ForeachCurrentAfterMoveNextFailure"), Is.False);
            Assert.That(HasStaticWrite("ForeachNullEnumeratorFailure"), Is.True);
            Assert.That(
                HasStaticWrite("ForeachExtensionInitializationFailure"),
                Is.True);
            Assert.That(HasStaticWrite("UnreachableWhileCatch"), Is.False);
            Assert.That(HasStaticWrite("UnreachableForCatch"), Is.False);
            Assert.That(HasStaticWrite("ShortCircuitedAndCatch"), Is.False);
            Assert.That(HasStaticWrite("ShortCircuitedOrCatch"), Is.False);
            Assert.That(HasStaticWrite("ConstantSwitchExpressionCatch"), Is.False);
            Assert.That(
                HasStaticWrite("ConstantUnmatchedSwitchExpressionCatch"),
                Is.True);
            Assert.That(
                HasStaticWrite("ConstantMatchedNonExhaustiveSwitchCatch"),
                Is.False);
            Assert.That(
                HasStaticWrite("ExhaustiveTypePatternSwitchCatch"),
                Is.False);
            Assert.That(
                HasStaticWrite("ConstantRelationalSwitchCatch"),
                Is.False);
            Assert.That(
                HasStaticWrite("NaNSingleRelationalSwitchCatch"),
                Is.False);
            Assert.That(
                HasStaticWrite("NaNDoubleRelationalSwitchCatch"),
                Is.False);
            Assert.That(
                HasStaticWrite("ConstantLogicalSwitchExpressionCatch"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterConstantUnmatchedSwitchExpression"),
                Is.False);
            Assert.That(HasStaticWrite("ConstantSwitchStatementCatch"), Is.False);
            Assert.That(
                HasStaticWrite("ConstantRelationalSwitchStatementCatch"),
                Is.False);
            Assert.That(HasStaticWrite("NaNSwitchStatementCatch"), Is.False);
            Assert.That(
                HasStaticWrite("ConstantLogicalSwitchStatementCatch"),
                Is.False);
            Assert.That(
                HasStaticWrite("TotalSliceSwitchExpressionGuardCatch"),
                Is.False);
            Assert.That(
                HasStaticWrite("TotalSliceSwitchStatementGuardCatch"),
                Is.False);
            Assert.That(HasStaticWrite("ThrowingSwitchExpressionGuard"), Is.False);
            Assert.That(HasStaticWrite("AfterThrowingTotalSwitchGuard"), Is.False);
            Assert.That(
                HasStaticWrite("VarPatternThrowingGuardBeforeFallback"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingPropertyPattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingReferencePropertyPattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingReferencePositionalPattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingReferenceListPattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingReferenceIndexerPattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingParenthesizedIndexerPattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingReferenceSlicePattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingVariableLengthSlicePattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingNestedSlicePattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("VirtualLengthMismatchCompletes"),
                Is.True);
            Assert.That(
                HasStaticWrite("AfterDivergingNestedListPattern"),
                Is.False);
            Assert.That(HasStaticWrite("ThrowingListLengthCatch"), Is.True);
            Assert.That(HasStaticWrite("ThrowingListIndexerCatch"), Is.True);
            Assert.That(HasStaticWrite("ThrowingListSliceCatch"), Is.True);
            Assert.That(
                HasStaticWrite("LengthMismatchSkipsIndexerCatch"),
                Is.False);
            Assert.That(HasStaticWrite("NullListSkipsLengthCatch"), Is.False);
            Assert.That(
                HasStaticWrite("ThrowingLengthSkipsIndexerCatch"),
                Is.False);
            Assert.That(
                session.Analyze(Method(compilation, "NestedListReceiverWrite"))
                    .Summary.Writes.IsUnknown,
                Is.True);
            Assert.That(
                HasStaticWrite("AfterDivergingNonNullNestedSliceList"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingNegatedPattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingAndPattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingOrPattern"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingNotNullAndPattern"),
                Is.False);
            Assert.That(HasStaticWrite("ThrowingSwitchStatementGuard"), Is.False);
            Assert.That(HasStaticWrite("VarPatternThrowingSwitchGuard"), Is.False);
            Assert.That(HasStaticWrite("ThrowingSwitchStatementGuardBeforeGoto"), Is.False);
            Assert.That(HasStaticWrite("ThrowingSwitchBodyBeforeGoto"), Is.False);
            Assert.That(HasStaticWrite("SwitchGotoOrdinaryLabel"), Is.True);
            Assert.That(HasStaticWrite("GotoNestedOrdinaryLabel"), Is.True);
            Assert.That(HasStaticWrite("AfterNonreturningCallCatch"), Is.False);
            Assert.That(HasStaticWrite("NestedSwallowedCatch"), Is.False);
            Assert.That(HasStaticWrite("NestedUnreachableHandler"), Is.False);
            Assert.That(HasStaticWrite("NestedRethrowCatch"), Is.True);
            Assert.That(HasStaticWrite("UnreachableNestedRethrowCatch"), Is.False);
            Assert.That(HasStaticWrite("NestedFinallyAfterPureDivergence"), Is.False);
            Assert.That(HasStaticWrite("UsingDeclarationNestedBreakThenDivergence"), Is.False);
            Assert.That(HasStaticWrite("UsingDeclarationInternalGotoThenDivergence"), Is.False);
            Assert.That(HasStaticWrite("NonNullCoalesceAssignment"), Is.False);
            Assert.That(HasStaticWrite("NullConditionalAccess"), Is.False);
            Assert.That(HasStaticWrite("UnreachableReturnAfterDivergence"), Is.False);
            Assert.That(HasStaticWrite("ReturnBlockedByDivergentFinally"), Is.False);
            Assert.That(HasStaticWrite("ReturnThroughCompletingFinally"), Is.True);
            Assert.That(HasStaticWrite("SetterFailure"), Is.True);
            Assert.That(HasStaticWrite("NullReceiverFailure"), Is.True);
            Assert.That(
                HasStaticWrite("NullReceiverAfterThrowingArgument"),
                Is.False);
            Assert.That(
                HasStaticWrite("ThrowingRhsBeforeNullSetter"),
                Is.True);
            Assert.That(HasStaticWrite("NullFieldFailure"), Is.True);
            Assert.That(
                HasStaticWrite("NullArrayCannotReachBoundsCatch"),
                Is.False);
            Assert.That(HasStaticWrite("StaticInitializationFailure"), Is.True);
            Assert.That(
                HasStaticWrite("AfterDivergingStaticInitialization"),
                Is.False);
            Assert.That(HasStaticWrite("BeforeFieldInitMethodMayRun"), Is.True);
            Assert.That(
                session.Analyze(EffectTestHost.RequireMethod(
                    compilation,
                    "SameTypeBeforeFieldInitBomb",
                    "CatchInitialization"))
                    .Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(
                session.Analyze(EffectTestHost.RequireMethod(
                    compilation,
                    "SameTypeBeforeFieldInitBomb",
                    "AfterInitialization"))
                    .Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
            Assert.That(
                HasStaticWrite("StaticInitializationWrongCatch"),
                Is.False);
            Assert.That(
                HasStaticWrite("ConstructionAfterFailingStaticInitialization"),
                Is.False);
            Assert.That(
                HasStaticWrite("StaticOperatorInitializationFailure"),
                Is.True);
            Assert.That(
                HasStaticWrite("ConstructionInitializationFailure"),
                Is.True);
            Assert.That(
                HasStaticWrite("StaticPropertyInitializationAfterRhs"),
                Is.False);
            Assert.That(
                HasStaticWrite("StaticFieldInitializationAfterRhs"),
                Is.False);
            Assert.That(
                HasStaticWrite("ExtensionInitializationAfterReceiver"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingExtensionInitialization"),
                Is.False);
            Assert.That(HasStaticWrite("NameofOperandIsCompileTime"), Is.False);
            Assert.That(HasStaticWrite("WithCloneFailure"), Is.True);
            Assert.That(HasStaticWrite("AfterDivergingWithClone"), Is.False);
            Assert.That(HasStaticWrite("DeconstructionFailure"), Is.True);
            Assert.That(
                HasStaticWrite("AfterDivergingDeconstruction"),
                Is.False);
            Assert.That(
                HasStaticWrite("AfterDivergingDeconstructionSetter"),
                Is.False);
            Assert.That(HasStaticWrite("NullLockWrongCatch"), Is.False);
            Assert.That(HasStaticWrite("NullLockCorrectCatch"), Is.True);
            Assert.That(HasStaticWrite("NullEventWrongCatch"), Is.False);
            Assert.That(HasStaticWrite("NullEventCorrectCatch"), Is.True);
            Assert.That(HasStaticWrite("NullAwaitWrongCatch"), Is.False);
            Assert.That(HasStaticWrite("NullAwaitCorrectCatch"), Is.True);
            Assert.That(
                HasStaticWrite("NullAwaitAfterThrowingOperand"),
                Is.False);
            Assert.That(HasStaticWrite("NullCustomAwaiterCatch"), Is.True);
            Assert.That(
                session.Analyze(EffectTestHost.RequireMethod(
                    compilation,
                    "GenericStaticBomb`1",
                    "GenericStaticProbe"))
                    .Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(HasStaticWrite("ThrowingFilter"), Is.False);
            Assert.That(HasStaticWrite("Rethrow"), Is.True);
            Assert.That(HasStaticWrite("FinallyRuns"), Is.True);
            var reverseDisposal = session.Analyze(
                Method(compilation, "ThrowingDeclaratorsUnwindInReverse"));
            Assert.That(
                reverseDisposal.Summary.Writes.Contains(
                    EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(
                reverseDisposal.Summary.Writes.Contains(
                    EffectRegionId.Parameter(1)),
                Is.True);
            var recursiveDisposal = session.Analyze(
                Method(compilation, "RecursiveDeclaratorDoesNotUnwindOuter"));
            Assert.That(
                recursiveDisposal.Summary.Writes.Contains(
                    EffectRegionId.Parameter(0)),
                Is.False);
            var recursiveThrowingDisposal = session.Analyze(Method(
                compilation,
                "RecursiveThrowingDeclaratorMayUnwindOuter"));
            Assert.That(
                recursiveThrowingDisposal.Summary.Writes.Contains(
                    EffectRegionId.Parameter(0)),
                Is.True);
        }

        bool HasStaticWrite(string methodName)
        {
            var summary = session.Analyze(Method(compilation, methodName)).Summary;
            return summary.Writes.Contains(EffectRegionId.Static());
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
    public void BareRethrowPreservesRuntimeSubtypeForOuterCatch()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Sample {
                private static int s_state;

                public static void RuntimeSubtypeReachesOuterHandler(
                    [NotNull] InvalidOperationException exception) {
                    try {
                        try {
                            throw exception;
                        }
                        catch (Exception) {
                            throw;
                        }
                    }
                    catch (InvalidOperationException) {
                        s_state++;
                    }
                }
            }
            """);

        var result = new EffectAnalysisSession(compilation)
            .Analyze(Method(compilation, "RuntimeSubtypeReachesOuterHandler"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.True);
            Assert.That(result.Summary.Throws.IsEmpty, Is.True);
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
    public void ConstructionInitializationScanHonorsCancellation()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public class Base {
                public static object Value = new object();
            }

            public sealed class Derived : Base {
            }
            """);
        var apiSpecs = new ApiSpecResolver(
            ApiSpecTable.Default).Resolve(compilation);
        var type = compilation.GetTypeByMetadataName("Derived")!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Action action = () => _ = EffectMethodNodeBuilder
            .HasPotentialConstructionInitialization(
                type,
                apiSpecs,
                cancellation.Token);

        Assert.That(
            action,
            Throws.InstanceOf<OperationCanceledException>());
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

        var throwingSummary = session.Analyze(
            Method(compilation, "ThrowingObjectReceiver")).Summary;
        Assert.That(
            throwingSummary.Capabilities.Contains(
                EffectCapabilityKind.Synchronization),
            Is.False);

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
                NeutralFacets(evidence),
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

    private static void AssertFreshContainerAlias(EffectMethodResult result)
    {
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

    private static void AssertExternalPreconditionFailure(
        EffectMethodResult result)
    {
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

    private static void AssertParameterWritesRemap(EffectMethodResult result)
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Writes.Contains(EffectRegionId.Parameter(0)),
                Is.True);
            Assert.That(result.Summary.Writes.IsUnknown, Is.False);
            Assert.That(
                result.Summary.Completeness,
                Is.EqualTo(EffectCompleteness.Complete));
        }
    }

    private static IMethodSymbol Method(
        Compilation compilation,
        string methodName)
    {
        return EffectTestHost.SampleMethod(compilation, methodName);
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
    public void ThrowingOperatorsAndInterpolationHolesSuppressLaterEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public readonly struct Source {
                public static Source operator -(Source value) =>
                    throw new System.InvalidOperationException();
                public static Source operator +(Source left, Source right) =>
                    throw new System.InvalidOperationException();
                public static Source operator ++(Source value) =>
                    throw new System.InvalidOperationException();
                public static explicit operator Target(Source value) =>
                    throw new System.InvalidOperationException();
            }

            public readonly struct Target {
            }

            public readonly struct Formatted {
                public override string ToString() =>
                    throw new System.InvalidOperationException();
            }

            public static class Sample {
                private static int s_state;

                public static void Unary(Source value) {
                    _ = -value;
                    s_state++;
                }

                public static void Binary(Source value) {
                    _ = value + value;
                    s_state++;
                }

                public static void Increment(Source value) {
                    value++;
                    s_state++;
                }

                public static void Conversion(Source value) {
                    _ = (Target)value;
                    s_state++;
                }

                public static string Interpolation() =>
                    $"{Fail()}{Mutate()}";

                public static string InterpolationFormatting(Formatted value) =>
                    $"{value}{Mutate()}";

                private static int Fail() =>
                    throw new System.InvalidOperationException();

                private static int Mutate() {
                    s_state++;
                    return 1;
                }
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        using (Assert.EnterMultipleScope())
        {
            foreach (var methodName in new[] {
                         "Unary", "Binary", "Conversion", "Interpolation",
                         "InterpolationFormatting", "Increment"
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
    }

    [Test]
    public void ThrowingIncrementAndConstructorArgumentsSuppressLaterEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public struct Counter {
                public static Counter operator ++(Counter value) =>
                    throw new System.InvalidOperationException();
            }

            public sealed class Box {
                public Box(int value) { }
            }

            public sealed class Sample {
                private Counter _counter;
                private static int s_state;

                public void Increment() {
                    _counter++;
                    s_state++;
                }

                public static Box Allocate() => new Box(Fail());

                private static int Fail() => throw null!;
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var increment = session.Analyze(Method(compilation, "Increment"));
        var allocation = session.Analyze(Method(compilation, "Allocate"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                increment.Summary.Writes.Contains(EffectRegionId.Receiver),
                Is.False);
            Assert.That(
                increment.Summary.Writes.Contains(EffectRegionId.Static()),
                Is.False);
            Assert.That(
                allocation.Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.None));
        }
    }

    [Test]
    public void FailingThrowExpressionsStaySequenced()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                public static void OuterThrow() => throw Make();

                private static InvalidOperationException Make() =>
                    throw new ArgumentException();
            }
            """);
        var session = new EffectAnalysisSession(compilation);
        var outerThrow = session.Analyze(Method(compilation, "OuterThrow"));
        var invalidOperation = compilation.GetTypeByMetadataName(
            "System.InvalidOperationException")!;
        var argument = compilation.GetTypeByMetadataName(
            "System.ArgumentException")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                outerThrow.Summary.Throws.Types.Any(type =>
                    SymbolEqualityComparer.Default.Equals(
                        type,
                        invalidOperation)),
                Is.False);
            Assert.That(
                outerThrow.Summary.Throws.Types.Any(type =>
                    SymbolEqualityComparer.Default.Equals(type, argument)),
                Is.True);
        }
    }

    [Test]
    public void FailingAnonymousInitializerDoesNotAllocate()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public static class Sample {
                public static object Anonymous() =>
                    new { Value = Fail() };

                private static int Fail() => throw null!;
            }
            """);
        var result = new EffectAnalysisSession(compilation)
            .Analyze(Method(compilation, "Anonymous"));

        Assert.That(
            result.Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.None));
    }

    [Test]
    public void FailingManagedAllocationsStopEnclosingSequences()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class Box {
                public void Run() { }
            }

            public static class Sample {
                public static object Anonymous() {
                    var value = new { Value = FailValue() };
                    return new object();
                }

                public static object Delegate() {
                    Action value = FailReceiver().Run;
                    return new object();
                }

                public static object NullDelegate() {
                    Box value = null!;
                    Action callback = value.Run;
                    return new object();
                }

                private static int FailValue() => throw null!;

                private static Box FailReceiver() => throw null!;
            }
            """);
        var session = new EffectAnalysisSession(compilation);

        foreach (var methodName in new[] { "Anonymous", "Delegate" })
        {
            var method = Method(compilation, methodName);
            Assert.That(
                session.Analyze(method)
                    .Summary.Allocation,
                Is.EqualTo(EffectAllocationKind.None),
                methodName);
        }
        Assert.That(
            session.Analyze(Method(compilation, "NullDelegate"))
                .Summary.Allocation,
            Is.EqualTo(EffectAllocationKind.Managed));
        Assert.That(
            session.Analyze(Method(compilation, "NullDelegate"))
                .Summary.Throws.Types.Select(static type =>
                    type.ToDisplayString()),
            Is.EqualTo(["System.ArgumentException"]));
    }

    [Test]
    public void ThrowingCoalesceTargetSuppressesValueAndWriteEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box {
                public object? Value { get; set; }
            }

            public static class Sample {
                private static object? s_state;

                public static void Evaluate() {
                    Box box = null!;
                    box.Value ??= Mutate();
                }

                private static object Mutate() =>
                    s_state = new object();
            }
            """);
        var result = new EffectAnalysisSession(compilation)
            .Analyze(Method(compilation, "Evaluate"));

        Assert.That(
            result.Summary.Writes.Contains(EffectRegionId.Static()),
            Is.False);
    }

    [Test]
    public void ThrowingArrayLengthReceiverSuppressesAccessEffects()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public static class Sample {
                public static int Read() => Fail().Length;

                private static int[] Fail() =>
                    throw new InvalidOperationException();
            }
            """);
        var result = new EffectAnalysisSession(compilation)
            .Analyze(Method(compilation, "Read"));
        var nullReference = compilation.GetTypeByMetadataName(
            "System.NullReferenceException")!;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Summary.Throws.Types.Any(type =>
                    SymbolEqualityComparer.Default.Equals(
                        type,
                        nullReference)),
                Is.False);
            Assert.That(result.Summary.Reads.Regions, Is.Empty);
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
        var methodCount = EffectCallGraph.MaximumCallGraphDepth + 1;
        var methods = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, methodCount).Select(index =>
                $"    public static long Step{index}(long value) => " +
                (index == methodCount - 1
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
