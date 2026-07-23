using NUnit.Framework;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Attributes;
using SharpProof.Symbolic;
namespace SharpProof.Test;
[TestFixture]
public sealed class MethodEffectsTests {
    [Test]
    public void ConditionUnwrappingHandlesRepeatedMixedNesting() {
        var expression = SyntaxFactory.ParseExpression("checked(((value!)))");
        var unwrapped = CSharpSyntaxFacts.UnwrapConditionExpression(expression);
        Assert.That(unwrapped, Is.TypeOf<IdentifierNameSyntax>());
        Assert.That(unwrapped.ToString(), Is.EqualTo("value"));
    }
    public sealed record ExpectedExceptionFact(
        string ExceptionType,
        SharpProofVerdict Escape,
        MethodExceptionSource Source);
    public sealed record ExpectedEffectSite(SharpProofEffect Effect, string? Symbol = null);
    public sealed record EffectCase(
        string Name,
        string Source,
        int Line = 2,
        SharpProofVerdict? Purity = null,
        SharpProofEffect RequiredEffects = SharpProofEffect.None,
        SharpProofEffect ForbiddenEffects = SharpProofEffect.None,
        SharpProofVerdict? AllocationFree = null,
        SharpProofVerdict? DoesNotThrow = null,
        SharpProofCapability RequiredCapabilities = SharpProofCapability.None,
        SharpProofCapability ForbiddenCapabilities = SharpProofCapability.None,
        ExpectedExceptionFact? ExceptionFact = null,
        ExpectedEffectSite? EffectSite = null,
        string? UnknownReason = null,
        Action<SharpProofAnalysisResult>? Verify = null);
    private static IEnumerable<TestCaseData> EffectCases() {
        yield return Effect("StaticEventSubscriptionWritesStaticState", """
            class C {
                static event System.Action? Changed;
                static void M(System.Action handler) { Changed += handler; }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("NameofDoesNotEvaluatePropertyGetter", """
            class C {
                static int state;
                static int P { get { state++; return 1; } }
                static string M() => nameof(P);
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesStaticState);
        yield return Effect("UndefinedConditionalCallHasNoRuntimeEffects", """
            class C {
                static int state;
                [System.Diagnostics.Conditional("SHARPPROOF_NEVER")]
                static void Trace() { state++; }
                static void M() { Trace(); }
            }
            """, 5,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("DefinedConditionalCallKeepsRuntimeEffects", """
            #define SHARPPROOF_ENABLED
            class C {
                static int state;
                [System.Diagnostics.Conditional("SHARPPROOF_ENABLED")]
                static void Trace() { state++; }
                static void M() { Trace(); }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("UnimplementedPartialCallHasNoRuntimeEffects", """
            partial class C {
                static int state;
                static partial void Hook(int value);
                static int Mutate() { state++; return 0; }
                static void M() { Hook(Mutate()); }
            }
            """, 5,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("ImplementedPartialCallIncludesImplementationEffects", """
            partial class C {
                static int state;
                static partial void Hook();
                static partial void Hook() { state++; }
                static void M() { Hook(); }
            }
            """, 5,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstantSwitchExpressionSkipsUnselectedArm", """
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => 0 switch { 0 => 1, _ => Mutate() };
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstantSwitchExpressionKeepsSelectedArm", """
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => 1 switch { 0 => 1, _ => Mutate() };
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstantRelationalSwitchSkipsUnselectedArm", """
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => 5 switch { > 0 => 1, _ => Mutate() };
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstantRelationalSwitchKeepsSelectedArm", """
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => -5 switch { > 0 => 1, _ => Mutate() };
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstantTypeSwitchSkipsUnselectedArm", """
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => "value" switch { string => 1, _ => Mutate() };
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstantTypeSwitchKeepsSelectedFallback", """
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => ((string?)null) switch { string => 1, _ => Mutate() };
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstantPatternSwitchStatementSkipsUnselectedSection", """
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() {
                    switch (5) { case > 0: return 1; default: return Mutate(); }
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstantPatternSwitchStatementKeepsSelectedSection", """
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() {
                    switch (5) { case > 0: return Mutate(); default: return 1; }
                }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("EarlierDefaultDoesNotPreemptMatchingConstantCase", """
            class C {
                static int state;
                static void M() {
                    switch (1) {
                        default: break;
                        case 1: state++; break;
                    }
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstantSwitchKeepsGotoCaseTargetEffects", """
            class C {
                static int state;
                static void M() {
                    switch (1) {
                        case 1: goto case 2;
                        case 2: state++; break;
                        default: break;
                    }
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("NullConditionalSkipsGetterForConstantNullReceiver", """
            class C {
                static int state;
                int P { get { state++; return 1; } }
                static int? M() => ((C?)null)?.P;
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("NullConditionalKeepsGetterForNonNullReceiver", """
            class C {
                static int state;
                int P { get { state++; return 1; } }
                static int? M() => new C()?.P;
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("NonNullCoalesceSkipsRightOperand", """
            class C {
                static int state;
                static string Mutate() { state++; return "fallback"; }
                static string M() => "value" ?? Mutate();
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("NullCoalesceKeepsRightOperand", """
            class C {
                static int state;
                static string Mutate() { state++; return "fallback"; }
                static string M() => ((string?)null) ?? Mutate();
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("NonNullCoalesceAssignmentSkipsRightOperand", """
            class C {
                static int state;
                static string Mutate() { state++; return "fallback"; }
                static string M() {
                    string value = "value";
                    value ??= Mutate();
                    return value;
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("CoalesceAssignmentPreservesNonNullResult", """
            class C {
                static int state;
                static string Mutate() { state++; return "unused"; }
                static string M(string? value) {
                    value ??= "fallback";
                    value ??= Mutate();
                    return value;
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("NullGuardSkipsCoalesceAssignmentFallback", """
            class C {
                static int state;
                static string Mutate() { state++; return "unused"; }
                static string M(string? value) {
                    if (value is null) return "empty";
                    value ??= Mutate();
                    return value;
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("PositionalPatternIncludesDeconstructEffects", """
            sealed class D {
                private static int state;
                public void Deconstruct(out int value) { state++; value = 0; }
            }
            class C {
                static bool M(D value) => value is D(var item);
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("PurePositionalPatternRemainsPure", """
            sealed class D {
                public void Deconstruct(out int value) { value = 0; }
            }
            class C {
                static bool M(D value) => value is D(var item);
            }
            """, 5,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("ListPatternIncludesLengthEffects", """
            sealed class D {
                private static int state;
                public int Length { get { state++; return 0; } }
                public int this[int index] => 0;
            }
            class C {
                static bool M(D value) => value is [];
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("EmptyListPatternDoesNotReadIndexer", """
            sealed class D {
                private static int state;
                public int Length => 0;
                public int this[int index] { get { state++; return 0; } }
            }
            class C {
                static bool M(D value) => value is [];
            }
            """, 7,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("ArraySliceListPatternRemainsPure", """
            class C {
                static int M(int[] values) {
                    return values is [_, .. var rest] ? rest.Length : 0;
                }
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("SlicePatternIncludesSliceMethodEffects", """
            sealed class D {
                private static int state;
                public int Length => 0;
                public int this[int index] => 0;
                public D Slice(int start, int length) { state++; return this; }
            }
            class C {
                static bool M(D value) => value is [.. var rest];
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("BareSlicePatternDoesNotCallSliceMethod", """
            sealed class D {
                private static int state;
                public int Length => 0;
                public int this[int index] => 0;
                public D Slice(int start, int length) { state++; return this; }
            }
            class C {
                static bool M(D value) => value is [..];
            }
            """, 8,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("ImplicitIndexerArgumentEvaluatesEffects", """
            static class G { public static int C; }
            sealed class Bag { public int Length => 0; public int this[int index] => 0; }
            class C {
                static int Impure() { G.C++; return 1; }
                static int M(Bag bag) => bag[^Impure()];
            }
            """, 5,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("RangeIndexerExpressionIncludesSliceMethodEffects", """
            sealed class D {
                private static int state;
                public int Length => 0;
                public D Slice(int start, int length) { state++; return this; }
            }
            class C {
                static D M(D value) => value[1..2];
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("AwaitUsingIncludesDisposeAsyncEffects", """
            sealed class D : System.IAsyncDisposable {
                private static int state;
                public System.Threading.Tasks.ValueTask DisposeAsync() { state++; return default; }
            }
            class C {
                static async System.Threading.Tasks.Task M() { await using var value = new D(); }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("AwaitUsingIncludesDisposalAwaiterEffects", """
            sealed class D {
                public Awaitable DisposeAsync() => default;
            }
            readonly struct Awaitable {
                private static int state;
                public Awaiter GetAwaiter() { state++; return default; }
            }
            readonly struct Awaiter : System.Runtime.CompilerServices.INotifyCompletion {
                public bool IsCompleted => true;
                public void OnCompleted(System.Action continuation) { }
                public void GetResult() { }
            }
            class C {
                static async System.Threading.Tasks.Task M() { await using var value = new D(); }
            }
            """, 14,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("AwaitUsingIncludesExtensionAwaiterEffects", """
            sealed class D { public Awaitable DisposeAsync() => default; }
            readonly struct Awaitable { }
            readonly struct Awaiter : System.Runtime.CompilerServices.INotifyCompletion {
                public bool IsCompleted => true;
                public void OnCompleted(System.Action continuation) { }
                public void GetResult() { }
            }
            static class Extensions {
                private static int state;
                public static Awaiter GetAwaiter(this Awaitable value) { state++; return default; }
            }
            class C {
                static async System.Threading.Tasks.Task M() { await using var value = new D(); }
            }
            """, 13,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("PureAwaitUsingRemainsPure", """
            sealed class D : System.IAsyncDisposable {
                public System.Threading.Tasks.ValueTask DisposeAsync() => default;
            }
            class C {
                static async System.Threading.Tasks.Task M() { await using var value = new D(); }
            }
            """, 5,
            purity: SharpProofVerdict.Proven);
        yield return Effect("AwaitForeachIncludesMoveNextAwaiterEffects", """
            sealed class AsyncEnumerable {
                public Enumerator GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default) => default;
            }
            struct Enumerator {
                public Awaitable MoveNextAsync() => default;
                public int Current => 0;
            }
            readonly struct Awaitable {
                private static int state;
                public Awaiter GetAwaiter() { state++; return default; }
            }
            readonly struct Awaiter : System.Runtime.CompilerServices.INotifyCompletion {
                public bool IsCompleted => true;
                public void OnCompleted(System.Action continuation) { }
                public bool GetResult() => false;
            }
            class C {
                static async System.Threading.Tasks.Task M() {
                    await foreach (var item in new AsyncEnumerable()) { }
                }
            }
            """, 18,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("AwaitForeachMapsMoveNextAwaitableReceiverEffects", """
            static class Globals { public static Awaitable Shared = new(); }
            sealed class AsyncEnumerable {
                public Enumerator GetAsyncEnumerator(System.Threading.CancellationToken cancellationToken = default) => new();
            }
            sealed class Enumerator {
                public Awaitable MoveNextAsync() => Globals.Shared;
                public int Current => 0;
            }
            sealed class Awaitable {
                public int State;
                public Awaiter GetAwaiter() { State++; return default; }
            }
            readonly struct Awaiter : System.Runtime.CompilerServices.INotifyCompletion {
                public bool IsCompleted => true;
                public void OnCompleted(System.Action continuation) { }
                public bool GetResult() => false;
            }
            class C {
                static async System.Threading.Tasks.Task M() {
                    await foreach (var item in new AsyncEnumerable()) { }
                }
            }
            """, 19,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Unknown);
        yield return Effect("ForeachWithoutDisposalRemainsPure", """
            sealed class Enumerable {
                public Enumerator GetEnumerator() => default;
            }
            struct Enumerator {
                public bool MoveNext() => false;
                public int Current => 0;
            }
            class C {
                static void M(Enumerable values) {
                    foreach (var value in values) { }
                }
            }
            """, 9,
            purity: SharpProofVerdict.Proven);
        yield return Effect("ForeachMapsGetEnumeratorReceiverEffects", """
            sealed class Enumerable {
                private readonly Enumerator enumerator = new();
                public Enumerator GetEnumerator() => enumerator;
            }
            sealed class Enumerator {
                public bool MoveNext() => false;
                public int Current => 0;
            }
            class C {
                static void M(Enumerable values) {
                    foreach (var value in values) { }
                }
            }
            """, 10,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsArgumentState);
        yield return Effect("ForeachTreatsStructEnumeratorAsCompilerOwned", """
            sealed class Enumerable {
                public Enumerator GetEnumerator() => default;
            }
            struct Enumerator {
                private int index;
                public bool MoveNext() => index++ < 0;
                public int Current => 0;
            }
            class C {
                static void M(Enumerable values) {
                    foreach (var value in values) { }
                }
            }
            """, 10,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("ForeachTreatsFreshReferenceEnumeratorAsCompilerOwned", """
            sealed class Enumerable {
                public Enumerator GetEnumerator() => new();
            }
            sealed class Enumerator {
                private int index;
                public bool MoveNext() => index++ < 0;
                public int Current => 0;
            }
            class C {
                static void M(Enumerable values) {
                    foreach (var value in values) { }
                }
            }
            """, 10,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("ForeachMapsCachedReferenceEnumeratorWrites", """
            sealed class Enumerable {
                private readonly Enumerator enumerator = new();
                public Enumerator GetEnumerator() => enumerator;
            }
            sealed class Enumerator {
                private int index;
                public bool MoveNext() => index++ < 0;
                public int Current => 0;
            }
            class C {
                static void M(Enumerable values) {
                    foreach (var value in values) { }
                }
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("SpanRefForeachWritesArgumentState", """
            class C {
                static void M(System.Span<int> values) {
                    foreach (ref var value in values) { value = 1; }
                }
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("ConstructedSpanMutationWritesArrayArgumentState", """
            class C {
                static void M(int[] values) {
                    var span = new System.Span<int>(values);
                    span[0] = 1;
                }
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ArrayAsSpanMutationWritesArrayArgumentState", """
            using System;
            class C {
                static void M(int[] values) {
                    var span = values.AsSpan();
                    span[0] = 1;
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("StaticArrayAsSpanMutationWritesArrayArgumentState", """
            class C {
                static void M(int[] values) {
                    var span = System.MemoryExtensions.AsSpan(values);
                    span[0] = 1;
                }
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("StringAsSpanReadIsPureAndAllocationFree", """
            using System;
            class C {
                static char M(string value) {
                    var span = value.AsSpan();
                    return span[0];
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("StaticStringAsSpanReadIsPureAndAllocationFree", """
            class C {
                static char M(string value) {
                    var span = System.MemoryExtensions.AsSpan(value);
                    return span[0];
                }
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ArrayAsMemorySpanMutationWritesArrayArgumentState", """
            using System;
            class C {
                static void M(int[] values) {
                    var memory = values.AsMemory();
                    memory.Span[0] = 1;
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("StaticArrayAsMemorySpanMutationWritesArrayArgumentState", """
            class C {
                static void M(int[] values) {
                    var memory = System.MemoryExtensions.AsMemory(values);
                    memory.Span[0] = 1;
                }
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("StringAsMemorySpanReadIsPureAndAllocationFree", """
            using System;
            class C {
                static char M(string value) {
                    var memory = value.AsMemory();
                    return memory.Span[0];
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ConstructedMemorySpanMutationWritesArrayArgumentState", """
            class C {
                static void M(int[] values) {
                    var memory = new System.Memory<int>(values);
                    memory.Span[0] = 1;
                }
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ConstructedReadOnlyMemorySpanReadIsPureAndAllocationFree", """
            class C {
                static char M(char[] values) {
                    var memory = new System.ReadOnlyMemory<char>(values);
                    return memory.Span[0];
                }
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("MemorySliceMutationWritesMemoryArgumentState", """
            class C {
                static void M(System.Memory<int> memory) {
                    var slice = memory.Slice(1);
                    slice.Span[0] = 1;
                }
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("SpanSliceMutationWritesSpanArgumentState", """
            class C {
                static void M(System.Span<int> values) {
                    var slice = values.Slice(1);
                    slice[0] = 1;
                }
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ReadOnlyMemorySliceReadIsPureAndAllocationFree", """
            class C {
                static int M(System.ReadOnlyMemory<int> values) {
                    var slice = values.Slice(1);
                    return slice.Span[0];
                }
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ReadOnlyMemoryToArrayIsPureKnownAllocation", """
            class C {
                static int[] M(System.ReadOnlyMemory<int> values) => values.ToArray();
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Disproven,
            required: SharpProofEffect.Allocates,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("StringReplaceIsPureKnownAllocation", """
            class C {
                static string M(string value) => value.Replace("a", "b");
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Disproven,
            required: SharpProofEffect.Allocates,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("StringToUpperInvariantIsPureKnownAllocation", """
            class C {
                static string M(string value) => value.ToUpperInvariant();
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Disproven,
            required: SharpProofEffect.Allocates,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("StringToLowerInvariantIsPureKnownAllocation", """
            class C {
                static string M(string value) => value.ToLowerInvariant();
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Disproven,
            required: SharpProofEffect.Allocates,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("StringToCharArrayIsPureKnownAllocation", """
            class C {
                static char[] M(string value) => value.ToCharArray();
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Disproven,
            required: SharpProofEffect.Allocates,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("StringContainsCharIsPureAndAllocationFree", """
            class C {
                static bool M(string value) => value.Contains('x');
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("StringIndexOfCharIsPureAndAllocationFree", """
            class C {
                static int M(string value) => value.IndexOf('x');
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("MathMinIsPureAndAllocationFree", """
            class C {
                static int M(int left, int right) => System.Math.Min(left, right);
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("MathMaxIsPureAndAllocationFree", """
            class C {
                static double M(double left, double right) => System.Math.Max(left, right);
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("MathSqrtIsPureAndAllocationFree", """
            class C {
                static double M(double value) => System.Math.Sqrt(value);
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("MathFSqrtIsPureAndAllocationFree", """
            class C {
                static float M(float value) => System.MathF.Sqrt(value);
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ArrayEmptyIsPureAndAllocationFree", """
            class C {
                static int[] M() => System.Array.Empty<int>();
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ArrayEmptyReferenceTypeIsPureAndAllocationFree", """
            class C {
                static string[] M() => System.Array.Empty<string>();
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("EnumerableEmptyIsPureAndAllocationFree", """
            class C {
                static System.Collections.Generic.IEnumerable<int> M() =>
                    System.Linq.Enumerable.Empty<int>();
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ObjectGetTypeIsPureAndAllocationFree", """
            class C {
                static System.Type M(object value) => value.GetType();
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ConstrainedReferenceGetTypeIsPureAndAllocationFree", """
            class C {
                static System.Type M<T>(T value) where T : class => value.GetType();
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("InterlockedIncrementWritesArgumentWithoutAllocating", """
            class C {
                static int M(ref int value) =>
                    System.Threading.Interlocked.Increment(ref value);
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("InterlockedIncrementLongWritesArgumentWithoutAllocating", """
            class C {
                static long M(ref long value) =>
                    System.Threading.Interlocked.Increment(ref value);
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("InterlockedDecrementWritesArgumentWithoutAllocating", """
            class C {
                static long M(ref long value) =>
                    System.Threading.Interlocked.Decrement(ref value);
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("InterlockedExchangeWritesOnlyRefArgument", """
            static class Globals { public static int Source; }
            class C {
                static int M(ref int target) =>
                    System.Threading.Interlocked.Exchange(ref target, Globals.Source);
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesStaticState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("InterlockedAddWritesOnlyRefArgument", """
            static class Globals { public static long Delta; }
            class C {
                static long M(ref long target) =>
                    System.Threading.Interlocked.Add(ref target, Globals.Delta);
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesStaticState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("InterlockedCompareExchangeWritesOnlyRefArgument", """
            static class Globals { public static int Value; public static int Comparand; }
            class C {
                static int M(ref int target) => System.Threading.Interlocked.CompareExchange(
                    ref target, Globals.Value, Globals.Comparand);
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesStaticState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("VolatileReadReadsButDoesNotWriteArgument", """
            class C {
                static int M(ref int value) => System.Threading.Volatile.Read(ref value);
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsArgumentState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ReadOnlyRefCallDoesNotWriteArgument", """
            class C {
                static int Read(ref int value) => value;
                static int M(ref int value) => Read(ref value);
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("VolatileWriteWritesOnlyRefArgument", """
            static class Globals { public static int Source; }
            class C {
                static void M(ref int target) =>
                    System.Threading.Volatile.Write(ref target, Globals.Source);
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesStaticState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("InterlockedReadReadsButDoesNotWriteArgument", """
            class C {
                static long M(ref long value) => System.Threading.Interlocked.Read(ref value);
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsArgumentState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("StringCopyToSpanWritesDestinationWithoutAllocating", """
            class C {
                static void M(string source, System.Span<char> destination) =>
                    source.CopyTo(destination);
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("StringCopyToSpanDoesNotWriteSourceRoot", """
            static class Globals { public static string Source; }
            class C {
                static void M(System.Span<char> destination) => Globals.Source.CopyTo(destination);
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesStaticState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ReadOnlySpanCopyToWritesDestinationWithoutAllocating", """
            class C {
                static void M(System.ReadOnlySpan<int> source, System.Span<int> destination) =>
                    source.CopyTo(destination);
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("SpanCopyToWritesDestinationWithoutAllocating", """
            class C {
                static void M(System.Span<byte> source, System.Span<byte> destination) =>
                    source.CopyTo(destination);
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("ReadOnlySpanTryCopyToWritesDestinationWithoutAllocating", """
            class C {
                static bool M(System.ReadOnlySpan<int> source, System.Span<int> destination) =>
                    source.TryCopyTo(destination);
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("SpanFillWritesReceiverWithoutAllocating", """
            class C {
                static void M(System.Span<int> destination, int value) => destination.Fill(value);
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("SpanFillDoesNotWriteValueRoot", """
            static class Globals { public static int Value; }
            class C {
                static void M(System.Span<int> destination) => destination.Fill(Globals.Value);
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesStaticState | SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("SpanClearWritesReceiverWithoutAllocating", """
            class C {
                static void M(System.Span<int> destination) => destination.Clear();
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("StructConstructionKeepsConstructorEffectsWithoutAllocating", """
            static class Globals { public static int Count; }
            readonly struct Value {
                public Value(int value) { Globals.Count += value; }
            }
            class C { static Value M() => new Value(1); }
            """, 5,
            purity: SharpProofVerdict.Disproven,
            allocationFree: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Allocates | SharpProofEffect.Unknown);
        yield return Effect("UnusedLocalIncrementDoesNotMakeRefForeachWrite", """
            class C {
                static void M(System.Span<int> values) {
                    foreach (ref var value in values) {
                        static void Local() { var other = 0; other++; }
                    }
                }
            }
            """, 2,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("ForeachDeconstructionIncludesDeconstructEffects", """
            sealed class Item {
                private static int state;
                public void Deconstruct(out int left, out int right) {
                    state++;
                    left = 1;
                    right = 2;
                }
            }
            class C {
                static void M(Item[] values) {
                    foreach (var (left, right) in values) { }
                }
            }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ExtensionForeachWritesArgumentStatePrecisely", """
            sealed class Source { public int State; }
            static class Extensions {
                public static Enumerator GetEnumerator(this Source source) {
                    source.State++;
                    return default;
                }
            }
            struct Enumerator {
                public bool MoveNext() => false;
                public int Current => 0;
            }
            class C {
                static void M(Source source) { foreach (var value in source) { } }
            }
            """, 13,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("ForeachIncludesExplicitEnumeratorProtocolEffects", """
            static class Globals { public static int Count; }
            sealed class Enumerator : System.Collections.Generic.IEnumerator<int> {
                int System.Collections.Generic.IEnumerator<int>.Current => 0;
                object System.Collections.IEnumerator.Current => 0;
                bool System.Collections.IEnumerator.MoveNext() { Globals.Count++; return false; }
                void System.Collections.IEnumerator.Reset() { }
                void System.IDisposable.Dispose() { }
            }
            sealed class Source : System.Collections.Generic.IEnumerable<int> {
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => new Enumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            class C { static void M(Source source) { foreach (var item in source) { } } }
            """, 13,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("UsingIncludesDisposeEffects", """
            sealed class D : System.IDisposable {
                private static int state;
                public void Dispose() { state++; }
            }
            class C {
                static void M() { using var value = new D(); }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("UsingMapsDisposeReceiverEffects", """
            sealed class D : System.IDisposable {
                private int state;
                public void Dispose() { state++; }
            }
            class C {
                static void M(D value) { using (value) { } }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("UsingDefinitelyNullResourceSkipsDisposeEffects", """
            sealed class D : System.IDisposable {
                private static int state;
                public void Dispose() { state++; }
            }
            class C {
                static void M() { D? value = null; using (value) { } }
            }
            """, 6,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("GenericUsingHasDisposalDispatchUncertainty", """
            class C {
                static void M<T>(T value) where T : System.IDisposable { using (value) { } }
            }
            """, 2,
            purity: SharpProofVerdict.Unknown,
            required: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("ParenthesizedUsingDeclarationIncludesDisposeEffects", """
            sealed class D : System.IDisposable {
                private static int state;
                public void Dispose() { state++; }
            }
            class C {
                static void M() { using (var value = new D()) { } }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("DynamicInvocationRemainsUnknown", """
            class C {
                static void M(dynamic value) { value.Mutate(); }
            }
            """, 2,
            purity: SharpProofVerdict.Unknown,
            required: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("DynamicConversionRemainsUnknown", """
            class C {
                static int M(dynamic value) => (int)value;
            }
            """, 2,
            purity: SharpProofVerdict.Unknown,
            required: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("DynamicBinaryOperatorRemainsUnknown", """
            class C {
                static dynamic M(dynamic left, dynamic right) => left + right;
            }
            """, 2,
            purity: SharpProofVerdict.Unknown,
            required: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("FunctionPointerInvocationRemainsUnknown", """
            unsafe class C {
                static void M(delegate*<void> action) { action(); }
            }
            """, 2,
            purity: SharpProofVerdict.Unknown,
            required: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("GenericObjectCreationHasAllocationAndDispatchUncertainty", """
            class C {
                static T M<T>() where T : class, new() => new T();
            }
            """, 2,
            purity: SharpProofVerdict.Unknown,
            required: SharpProofEffect.Allocates | SharpProofEffect.Unknown,
            allocationFree: SharpProofVerdict.Disproven);
        yield return Effect("InterfaceGetterDispatchRemainsUnknown", """
            interface IValue { int Value { get; } }
            class C {
                static int M(IValue value) => value.Value;
            }
            """, 3,
            purity: SharpProofVerdict.Unknown,
            required: SharpProofEffect.Unknown);
        yield return Effect("StringNullOrWhiteSpaceGuardWithThrowRemainsPure", """
            class C {
                sealed class E : System.Exception { }
                static int M(string text) => string.IsNullOrWhiteSpace(text) ? throw new E() : text.Length;
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            doesNotThrow: SharpProofVerdict.Disproven);
        yield return Effect("UnusedLocalFunctionAssignmentDoesNotAffectConstructor", """
            class C {
                private static int state;
                C() {
                    static void Local() { state = 1; }
                }
            }
            """, 3,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("WithExpressionIncludesCopyConstructorEffects", """
            sealed record R {
                private static int state;
                public int X { get; init; }
                public R() { }
                private R(R other) { state++; X = other.X; }
            }
            class C {
                static R M(R value) => value with { X = 2 };
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("AwaitIncludesGetAwaiterEffects", """
            sealed class Awaitable {
                private static int state;
                public Awaiter GetAwaiter() { state++; return default; }
            }
            struct Awaiter : System.Runtime.CompilerServices.INotifyCompletion {
                public bool IsCompleted => true;
                public void OnCompleted(System.Action continuation) { }
                public int GetResult() => 1;
            }
            class C {
                static async System.Threading.Tasks.Task<int> M(Awaitable value) => await value;
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("AwaitIncludesContinuationEffects", """
            sealed class Awaitable {
                public Awaiter GetAwaiter() => default;
            }
            struct Awaiter : System.Runtime.CompilerServices.INotifyCompletion {
                private static int state;
                public bool IsCompleted => false;
                public void OnCompleted(System.Action continuation) { state++; }
                public int GetResult() => 1;
            }
            class C {
                static async System.Threading.Tasks.Task<int> M(Awaitable value) => await value;
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("AwaitIncludesInheritedContinuationEffects", """
            static class Globals { public static int Count; }
            class BaseAwaiter : System.Runtime.CompilerServices.INotifyCompletion {
                public void OnCompleted(System.Action continuation) { Globals.Count++; }
            }
            sealed class Awaiter : BaseAwaiter {
                public bool IsCompleted => false;
                public int GetResult() => 1;
            }
            sealed class Awaitable { public Awaiter GetAwaiter() => new(); }
            class C {
                static async System.Threading.Tasks.Task<int> M(Awaitable value) => await value;
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("AwaitIncludesExplicitContinuationEffects", """
            static class Globals { public static int Count; }
            sealed class Awaiter : System.Runtime.CompilerServices.INotifyCompletion {
                public bool IsCompleted => false;
                public int GetResult() => 1;
                void System.Runtime.CompilerServices.INotifyCompletion.OnCompleted(System.Action continuation) {
                    Globals.Count++;
                }
            }
            sealed class Awaitable { public Awaiter GetAwaiter() => new(); }
            class C {
                static async System.Threading.Tasks.Task<int> M(Awaitable value) => await value;
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("ListCollectionExpressionAllocates", """
            class C {
                static System.Collections.Generic.List<int> M() => [1, 2, 3];
            }
            """,
            required: SharpProofEffect.Allocates,
            allocationFree: SharpProofVerdict.Disproven);
        yield return Effect("FreshCollectionInitializerRemainsPure", """
            class C {
                static System.Collections.Generic.List<int> M() => new() { 1, 2, 3 };
            }
            """,
            purity: SharpProofVerdict.Proven,
            allocationFree: SharpProofVerdict.Disproven);
        yield return Effect("CollectionExpressionIncludesConstructorEffects", """
            sealed class Bag : System.Collections.Generic.IEnumerable<int> {
                private static int state;
                public Bag() { state++; }
                public void Add(int value) { }
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => throw null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            class C { static Bag M() => [1, 2]; }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("CollectionExpressionIncludesAddEffects", """
            sealed class Bag : System.Collections.Generic.IEnumerable<int> {
                private static int state;
                public Bag() { }
                public void Add(int value) { state++; }
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => throw null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            class C { static Bag M() => [1, 2]; }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("CollectionExpressionIncludesInheritedAddEffects", """
            class BagBase {
                private static int state;
                public void Add(int value) { state++; }
            }
            sealed class Bag : BagBase, System.Collections.Generic.IEnumerable<int> {
                public Bag() { }
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => throw null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            class C { static Bag M() => [1, 2]; }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("CollectionExpressionIncludesSpreadEnumerationEffects", """
            sealed class Bag : System.Collections.Generic.IEnumerable<int> {
                public Bag() { }
                public void Add(int value) { }
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => throw null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            sealed class Source : System.Collections.Generic.IEnumerable<int> {
                private static int state;
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() { state++; throw null!; }
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            class C { static Bag M(Source source) => [.. source]; }
            """, 12,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("CollectionSpreadIncludesExplicitInterfaceEnumerationEffects", """
            sealed class Source : System.Collections.Generic.IEnumerable<int> {
                private static int state;
                System.Collections.Generic.IEnumerator<int> System.Collections.Generic.IEnumerable<int>.GetEnumerator() {
                    state++;
                    yield break;
                }
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
                    ((System.Collections.Generic.IEnumerable<int>)this).GetEnumerator();
            }
            class C { static int[] M(Source source) => [.. source]; }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("CollectionSpreadIncludesInheritedInterfaceMoveNextEffects", """
            static class Globals { public static int Count; }
            sealed class Enumerator : System.Collections.Generic.IEnumerator<int> {
                int System.Collections.Generic.IEnumerator<int>.Current => 0;
                object System.Collections.IEnumerator.Current => 0;
                bool System.Collections.IEnumerator.MoveNext() { Globals.Count++; return false; }
                void System.Collections.IEnumerator.Reset() { }
                void System.IDisposable.Dispose() { }
            }
            sealed class Source : System.Collections.Generic.IEnumerable<int> {
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => new Enumerator();
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            class C { static int[] M(Source source) => [.. source]; }
            """, 13,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("CollectionSpreadDoesNotCallUnrecognizedDisposePattern", """
            static class Globals { public static int Count; }
            sealed class Enumerator {
                public int Current => 0;
                public bool MoveNext() => false;
                public void Dispose() { Globals.Count++; }
            }
            sealed class Source {
                public Enumerator GetEnumerator() => new();
            }
            class C { static int[] M(Source source) => [.. source]; }
            """, 10,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("CollectionSpreadIncludesExtensionGetEnumeratorEffects", """
            static class Globals { public static int Count; }
            sealed class Enumerator {
                public int Current => 0;
                public bool MoveNext() => false;
            }
            sealed class Source { }
            static class Extensions {
                public static Enumerator GetEnumerator(this Source source) {
                    Globals.Count++;
                    return new();
                }
            }
            class C { static int[] M(Source source) => [.. source]; }
            """, 13,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("CollectionSpreadUsesCompilerSelectedExtensionEnumerator", """
            static class Globals { public static int Count; }
            class Base { }
            sealed class Derived : Base { }
            sealed class Enumerator {
                public int Current => 0;
                public bool MoveNext() => false;
            }
            static class Extensions {
                public static Enumerator GetEnumerator(this Base source) => new();
                public static Enumerator GetEnumerator(this Derived source) { Globals.Count++; return new(); }
            }
            class C { static int[] M(Derived source) => [.. source]; }
            """, 12,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("OpenVirtualDispatchDoesNotInlineTheBaseImplementation", """
            class B { public virtual void Work() { } }
            sealed class D : B { static int state; public override void Work() { state++; } }
            class C { static void M(B value) => value.Work(); }
            """, 3,
            purity: SharpProofVerdict.Unknown,
            required: SharpProofEffect.DispatchUncertainty);
        yield return Effect("ReassignedFreshLocalLosesFreshOwnership", """
            class Box { public int Value; }
            class C { static void M(Box input) { var box = new Box(); box = input; box.Value++; } }
            """, 2,
            purity: SharpProofVerdict.Disproven);
        yield return Effect("DeconstructionAssignmentWritesArgumentState", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    (input.Value, _) = (1, 2);
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("DeconstructionAssignmentIncludesDeconstructEffects", """
            sealed class Pair {
                private static int state;
                public void Deconstruct(out int left, out int right) {
                    state++;
                    left = 1;
                    right = 2;
                }
            }
            class C {
                static void M(Pair value) { var (left, right) = value; }
            }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ExtensionDeconstructionWritesArgumentState", """
            sealed class Box { public int Value; }
            static class Extensions {
                public static void Deconstruct(this Box value, out int left, out int right) {
                    value.Value++;
                    left = 1;
                    right = 2;
                }
            }
            class C {
                static void M(Box input) { var (left, right) = input; }
            }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("CoalesceAssignmentWritesArgumentState", """
            #nullable enable
            sealed class Box { public object? Value; }
            class C {
                static void M(Box input) {
                    input.Value ??= new object();
                }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("ConditionalAccessInvocationWritesArgumentStatePrecisely", """
            sealed class Box {
                public int Value;
                public void Mutate() { Value++; }
            }
            class C {
                static void M(Box input) { input?.Mutate(); }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("CoalescedUsingReceiverMapsDisposalOrigins", """
            sealed class D : System.IDisposable {
                public int State;
                public void Dispose() { State++; }
            }
            class C {
                static void M(D input) { using (input ?? new D()) { } }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("SwitchExpressionReceiverMapsAllOrigins", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            class C {
                static void M(Box input, bool useInput) {
                    (useInput switch { true => input, false => new Box() }).Mutate();
                }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("AssignmentExpressionReceiverUsesAssignedOrigin", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            class C {
                static void M(Box input) {
                    var local = new Box();
                    (local = input).Mutate();
                }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("IdentityCallReceiverUsesReturnedArgumentOrigin", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            class C {
                static Box Identity(Box value) => value;
                static void M(Box input) { Identity(input).Mutate(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("InvocationResultReceiverUsesReturnedStaticOrigin", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            class C {
                private static readonly Box global = new();
                static Box GetGlobal() => global;
                static void M() { GetGlobal().Mutate(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("QualifiedInvocationResultUsesReturnedStaticOrigin", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            static class Globals { public static readonly Box Value = new(); }
            class C {
                static Box GetGlobal() => Globals.Value;
                static void M() { GetGlobal().Mutate(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("ConditionalInvocationResultMapsAllReturnedOrigins", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            class C {
                static Box Choose(Box value, bool choose) => choose ? value : new Box();
                static void M(Box input, bool choose) { Choose(input, choose).Mutate(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("MultipleInvocationReturnsMapAllOrigins", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            class C {
                static Box Choose(Box value, bool choose) {
                    if (choose) return value;
                    return new Box();
                }
                static void M(Box input, bool choose) { Choose(input, choose).Mutate(); }
            }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("InvocationMemberResultUsesReturnedArgumentRoot", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            sealed class Holder { public Box Value = new(); }
            class C {
                static Box Extract(Holder value) => value.Value;
                static void M(Holder input) { Extract(input).Mutate(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("PropertyResultReceiverUsesReturnedStaticOrigin", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            sealed class Holder {
                private static readonly Box global = new();
                public Box Value => global;
            }
            class C {
                static void M(Holder input) { input.Value.Mutate(); }
            }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("MultiplePropertyReturnsMapAllOrigins", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            sealed class Holder {
                private static readonly Box global = new();
                private readonly Box local = new();
                private readonly bool useGlobal;
                public Box Value {
                    get {
                        if (useGlobal) return global;
                        return local;
                    }
                }
            }
            class C {
                static void M(Holder input) { input.Value.Mutate(); }
            }
            """, 17,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState | SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("PointerIndirectionAssignmentWritesArgumentState", """
            unsafe class C {
                static void M(int* pointer) {
                    *pointer = 1;
                }
            }
            """, 2,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("InlineArrayAssignmentWritesArgumentState", """
            using System.Runtime.CompilerServices;
            [InlineArray(4)] struct Buffer { private int element; }
            class C {
                static void M(ref Buffer buffer) {
                    buffer[0] = 1;
                }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("ImplicitIndexerAssignmentWritesArgumentState", """
            sealed class Bag {
                public int Length => 3;
                public int this[int index] { get => index; set { } }
            }
            class C {
                static void M(Bag input) {
                    input[^1] = 1;
                }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("RefLocalAssignmentWritesAliasedArgumentState", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    ref int alias = ref input.Value;
                    alias = 1;
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("RefReturnInvocationAssignmentWritesArgumentState", """
            sealed class Box { public int Value; }
            class C {
                static ref int GetValue(Box box) => ref box.Value;
                static void M(Box input) {
                    GetValue(input) = 1;
                }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("RefLocalFromRefReturnWritesArgumentState", """
            sealed class Box { public int Value; }
            class C {
                static ref int GetValue(Box box) => ref box.Value;
                static void M(Box input) {
                    ref int alias = ref GetValue(input);
                    alias = 1;
                }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("RefReturnPropertyAssignmentTracksExposedStaticState", """
            sealed class Box {
                private static int state;
                public ref int Value => ref state;
            }
            class C {
                static void M() {
                    var box = new Box();
                    box.Value = 1;
                }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ConditionalRefAssignmentWritesArgumentState", """
            sealed class Box { public int Left; public int Right; }
            class C {
                static void M(Box input, bool chooseLeft) {
                    (chooseLeft ? ref input.Left : ref input.Right) = 1;
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("RefReassignmentDoesNotWriteThroughPreviousAlias", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    var fresh = new Box();
                    ref int alias = ref input.Value;
                    alias = ref fresh.Value;
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("FixedPointerWriteToFreshObjectRemainsFreshOwned", """
            sealed class Box { public int Value; }
            unsafe class C {
                static void M() {
                    var fresh = new Box();
                    fixed (int* pointer = &fresh.Value) {
                        *pointer = 1;
                    }
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState);
        yield return Effect("FixedIncludesGetPinnableReferenceEffects", """
            sealed class Pinnable {
                private int value;
                private static int state;
                public ref int GetPinnableReference() { state++; return ref value; }
            }
            unsafe class C {
                static void M(Pinnable value) {
                    fixed (int* pointer = value) { }
                }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("NestedFreshObjectGraphWritesRemainFreshOwned", """
            sealed class Box { public int Value; }
            sealed class Middle { public Box Value { get; init; } }
            sealed class Outer { public Middle Value { get; init; } }
            class C {
                static int M() {
                    var outer = new Outer { Value = new Middle { Value = new Box() } };
                    outer.Value.Value.Value = 1;
                    return outer.Value.Value.Value;
                }
            }
            """, 5,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("FreshWrapperDoesNotTakeOwnershipOfNestedArgument", """
            sealed class Box { public int Value; }
            sealed class Outer { public Box Value { get; init; } }
            class C {
                static void M(Box input) {
                    var outer = new Outer { Value = input };
                    outer.Value.Value = 1;
                }
            }
            """, 5,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("InstanceMutationOnFreshLocalRemainsFreshOwned", """
            sealed class Box {
                public int Value;
                public void SetValue() { Value = 1; }
            }
            class C {
                static int M() {
                    var box = new Box();
                    box.SetValue();
                    return box.Value;
                }
            }
            """, 7,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesReceiverState);
        yield return Effect("InstanceMutationOnArgumentIsRemappedToArgumentState", """
            sealed class Box {
                public int Value;
                public void SetValue() { Value = 1; }
            }
            class C { static void M(Box box) { box.SetValue(); } }
            """, 5,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesReceiverState);
        yield return Effect("ExplicitConstructorReceiverWritesRemainFreshOwned", """
            sealed class Box {
                public int Value;
                public Box() { Value = 1; }
            }
            class C {
                static int M() {
                    var box = new Box();
                    return box.Value;
                }
            }
            """, 6,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesReceiverState);
        yield return Effect("ObjectCreationIncludesEventFieldInitializerEffects", """
            static class Globals { public static int Count; }
            sealed class D {
                public event System.Action Changed = CreateHandler();
                private static System.Action CreateHandler() { Globals.Count++; return () => { }; }
            }
            class C { static D M() => new D(); }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("StaticMethodCallIncludesTypeInitializerEffects", """
            static class Globals { public static int Count; }
            sealed class D {
                static D() { Globals.Count++; }
                public static void Touch() { }
            }
            class C { static void M() { D.Touch(); } }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("StaticMethodAnalysisIncludesOwnTypeInitializerEffects", """
            static class Globals { public static int Count; }
            sealed class D {
                static D() { Globals.Count++; }
                public static void M() { }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("StaticDelegateInvocationIncludesTypeInitializerEffects", """
            static class Globals { public static int Count; }
            sealed class D {
                static D() { Globals.Count++; }
                public static void Touch() { }
            }
            class C {
                static void M() { System.Action action = D.Touch; action(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ObjectCreationIncludesTypeInitializerEffects", """
            static class Globals { public static int Count; }
            sealed class D {
                static D() { Globals.Count++; }
                public D() { }
            }
            class C { static D M() => new D(); }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ConstructorAnalysisIncludesOwnTypeInitializerEffects", """
            static class Globals { public static int Count; }
            sealed class D {
                static D() { Globals.Count++; }
                public D() { }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ObjectCreationIncludesBaseTypeInitializerEffects", """
            static class Globals { public static int Count; }
            class B {
                static B() { Globals.Count++; }
            }
            sealed class D : B { }
            class C { static D M() => new D(); }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("StaticFieldReadIncludesTypeInitializerEffects", """
            static class Globals { public static int Count; }
            sealed class D {
                static D() { Globals.Count++; }
                public static int Value;
            }
            class C { static int M() => D.Value; }
            """, 6,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("StaticFieldReadIncludesImplicitTypeInitializerEffects", """
            static class Globals {
                public static int Count;
                public static int Increment() { Count++; return Count; }
            }
            sealed class D { public static int Value = Globals.Increment(); }
            class C { static int M() => D.Value; }
            """, 6,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("StaticFieldWriteIncludesTypeInitializerEffects", """
            static class Globals { public static object? Value; }
            sealed class D {
                static D() { Globals.Value = new object(); }
                public static int Value;
            }
            class C { static void M() { D.Value = 1; } }
            """, 6,
            required: SharpProofEffect.Allocates,
            allocationFree: SharpProofVerdict.Disproven);
        yield return Effect("StaticHelperMutationOfFreshArgumentRemainsFreshOwned", """
            sealed class Box { public int Value; }
            class C {
                static void SetValue(Box box) { box.Value = 1; }
                static int M() {
                    var box = new Box();
                    SetValue(box);
                    return box.Value;
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("StaticHelperMutationOfCallerArgumentRemainsArgumentOwned", """
            sealed class Box { public int Value; }
            class C {
                static void SetValue(Box box) { box.Value = 1; }
                static void M(Box input) { SetValue(input); }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState);
        yield return Effect("HelperMutationMapsOnlyMutatedArgument", """
            sealed class Box { public int Value; }
            class C {
                static void SetFirst(Box first, Box second) { first.Value = 1; }
                static void M(Box external) {
                    var fresh = new Box();
                    SetFirst(fresh, external);
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("HelperReadMapsOnlyReadArgument", """
            sealed class Box { public int Value; }
            class C {
                static int ReadFirst(Box first, Box second) => first.Value;
                static void M(Box external) {
                    var fresh = new Box();
                    _ = ReadFirst(fresh, external);
                }
            }
            """, 4,
            forbidden: SharpProofEffect.ReadsArgumentState);
        yield return Effect("LocalFunctionMutationMapsOnlyMutatedArgument", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box external) {
                    var fresh = new Box();
                    Local(fresh, external);
                    static void Local(Box first, Box second) { first.Value = 1; }
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("FreshCapturingLocalFunctionMapsMutatedArgument", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box external) {
                    var fresh = new Box();
                    var captured = new Box();
                    Local(fresh, external);
                    void Local(Box first, Box second) {
                        _ = captured.Value;
                        first.Value = 1;
                    }
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("ArgumentCapturingLocalFunctionSeparatesCaptureReadFromParameterWrite", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box external) {
                    var fresh = new Box();
                    Local(fresh, external);
                    void Local(Box first, Box second) {
                        _ = external.Value;
                        first.Value = 1;
                    }
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsArgumentState | SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("ReceiverCapturingLocalFunctionSeparatesCaptureReadFromParameterWrite", """
            sealed class Box { public int Value; }
            sealed class C {
                private int state;
                void M(Box external) {
                    var fresh = new Box();
                    Local(fresh, external);
                    void Local(Box first, Box second) {
                        _ = state;
                        first.Value = 1;
                    }
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.ReadsReceiverState | SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("LocalFunctionReturnPreservesCapturedArgumentOrigin", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box external) {
                    var fresh = new Box();
                    var returned = Local(fresh);
                    returned.Value = 1;
                    Box Local(Box ignored) => external;
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState);
        yield return Effect("LambdaMutationMapsOnlyMutatedArgument", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box external) {
                    var fresh = new Box();
                    System.Action<Box, Box> action = (first, second) => first.Value = 1;
                    action(fresh, external);
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("ReassigningByValueParameterDoesNotWriteArgumentState", """
            sealed class Box { }
            class C {
                static void M(Box input) {
                    input = new Box();
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesArgumentState);
        yield return Effect("AssigningRefParameterWritesArgumentState", """
            sealed class Box { }
            class C {
                static void M(ref Box input) {
                    input = new Box();
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("RepeatedStaticHelperMutationPreservesFreshOwnership", """
            sealed class Box { public int Value; }
            class C {
                static void SetValue(Box box) { box.Value = 1; }
                static int M() {
                    var box = new Box();
                    SetValue(box);
                    SetValue(box);
                    return box.Value;
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesCapturedState);
        yield return Effect("PublishingFreshArgumentInvalidatesFreshOwnership", """
            sealed class Box { public int Value; }
            class C {
                static Box? state;
                static void Publish(Box box) { state = box; }
                static void M() {
                    var box = new Box();
                    Publish(box);
                    box.Value = 1;
                }
            }
            """, 5,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState | SharpProofEffect.WritesCapturedState);
        yield return Effect("UnusedLocalFunctionDoesNotPublishFreshArgument", """
            sealed class Box { }
            class C {
                static Box? state;
                static int count;
                static void Touch(Box box) {
                    count++;
                    static void Local(Box box) { state = box; }
                }
                static void M() {
                    Touch(new Box());
                }
            }
            """, 9,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState);
        yield return Effect("LocalCopyDoesNotPublishFreshArgument", """
            sealed class Box { }
            class C {
                static int count;
                static void Touch(Box box) {
                    count++;
                    Box local;
                    local = box;
                }
                static void M() {
                    Touch(new Box());
                }
            }
            """, 9,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState);
        yield return Effect("FreshLocalMemberDoesNotPublishFreshArgument", """
            sealed class Box { }
            sealed class Holder { public Box? Value; }
            class C {
                static int count;
                static void Touch(Box box) {
                    count++;
                    var holder = new Holder();
                    holder.Value = box;
                }
                static void M() {
                    Touch(new Box());
                }
            }
            """, 10,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState);
        yield return Effect("FreshLocalMemberReadDoesNotPublishFreshArgument", """
            sealed class Box { }
            sealed class Holder { public Box? Value; }
            class C {
                static int count;
                static void Touch(Box box) {
                    count++;
                    var holder = new Holder();
                    holder.Value = box;
                    _ = holder.Value;
                }
                static void M() {
                    Touch(new Box());
                }
            }
            """, 11,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState);
        yield return Effect("FreshReceiverDoesNotPublishFreshArgument", """
            sealed class Box { }
            sealed class Holder {
                private static int count;
                private Box? value;
                void Touch(Box box) { count++; value = box; }
                static void M() {
                    new Holder().Touch(new Box());
                }
            }
            """, 6,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState);
        yield return Effect("PropertySetterMutationOfFreshValueRemainsFreshOwned", """
            sealed class Box { public int Value; }
            sealed class Holder {
                public Box Item { set { value.Value = 1; } }
            }
            class C {
                static void M() {
                    var holder = new Holder();
                    var box = new Box();
                    holder.Item = box;
                }
            }
            """, 6,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("PropertySetterMutationOfCallerValueRemainsArgumentOwned", """
            sealed class Box { public int Value; }
            sealed class Holder {
                public Box Item { set { value.Value = 1; } }
            }
            class C {
                static void M(Box input) {
                    var holder = new Holder();
                    holder.Item = input;
                }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("DelegateMutationOfFreshReceiverRemainsFreshOwned", """
            sealed class Box {
                public int Value;
                public void SetValue() { Value = 1; }
            }
            class C {
                static int M() {
                    var box = new Box();
                    System.Action action = box.SetValue;
                    action();
                    return box.Value;
                }
            }
            """, 7,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("DelegateMutationOfCallerReceiverRemainsArgumentOwned", """
            sealed class Box {
                public int Value;
                public void SetValue() { Value = 1; }
            }
            class C {
                static void M(Box input) {
                    System.Action action = input.SetValue;
                    action();
                }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("InlineDelegateMutationOfCallerReceiverRemainsArgumentOwned", """
            sealed class Box {
                public int Value;
                public void SetValue() { Value = 1; }
            }
            class C {
                static void M(Box input) {
                    ((System.Action)input.SetValue)();
                }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState);
        yield return Effect("DelegateRetainsFreshReceiverAfterLocalReassignment", """
            sealed class Box {
                public int Value;
                public void SetValue() { Value = 1; }
            }
            class C {
                static void M(Box input) {
                    var box = new Box();
                    System.Action action = box.SetValue;
                    box = input;
                    action();
                }
            }
            """, 7,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState);
        yield return Effect("DelegateRetainsCallerReceiverAfterLocalReassignment", """
            sealed class Box {
                public int Value;
                public void SetValue() { Value = 1; }
            }
            class C {
                static void M(Box input) {
                    var box = input;
                    System.Action action = box.SetValue;
                    box = new Box();
                    action();
                }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            forbidden: SharpProofEffect.WritesFreshOwnedState);
        yield return Effect("CopiedDelegateRetainsImpureTarget", """
            class C {
                static int state;
                static void Impure() { state++; }
                static void M() {
                    System.Action first = Impure;
                    System.Action second = first;
                    second();
                }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("ConditionalDelegateInvocationRetainsImpureTarget", """
            class C {
                static int state;
                static void Impure() { state++; }
                static void M() {
                    System.Action action = Impure;
                    action?.Invoke();
                }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("InvocationResultRetainsDelegateTarget", """
            class C {
                static int state;
                static void Impure() { state++; }
                static System.Action GetAction() => Impure;
                static void M() { GetAction()(); }
            }
            """, 5,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("ConditionalInvocationResultRetainsDelegateTargets", """
            class C {
                static int state;
                static void Pure() { }
                static void Impure() { state++; }
                static System.Action Choose(bool impure) => impure ? Impure : Pure;
                static void M(bool impure) { Choose(impure)(); }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("InvocationDelegateTargetRetainsFreshArgumentReceiver", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
            }
            class C {
                static System.Action Bind(Box value) => value.Mutate;
                static void M() {
                    var fresh = new Box();
                    Bind(fresh)();
                }
            }
            """, 7,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshCapturedArgument", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) => () => value.State++;
                static void M() {
                    var fresh = new Box();
                    Bind(fresh)();
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshCapturedReceiver", """
            sealed class Box {
                public int State;
                public System.Action Bind() => () => State++;
            }
            class C {
                static void M() {
                    var fresh = new Box();
                    fresh.Bind()();
                }
            }
            """, 6,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesReceiverState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshCapturedLocal", """
            sealed class Box { public int State; }
            class C {
                static System.Action Make() {
                    var fresh = new Box();
                    return () => fresh.State++;
                }
                static void M() { Make()(); }
            }
            """, 7,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshReceiverAndArgumentCaptures", """
            sealed class Box {
                public int State;
                public System.Action Bind(Box other) => () => { State++; other.State++; };
            }
            class C {
                static void M() {
                    var owner = new Box();
                    var other = new Box();
                    owner.Bind(other)();
                }
            }
            """, 6,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesReceiverState | SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshCapturedArgumentAlias", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    var alias = value;
                    return () => alias.State++;
                }
                static void M() {
                    var fresh = new Box();
                    Bind(fresh)();
                }
            }
            """, 7,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshConditionalCapturedAlias", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value, bool useValue) {
                    var alias = useValue ? value : new Box();
                    return () => alias.State++;
                }
                static void M() {
                    var fresh = new Box();
                    Bind(fresh, true)();
                }
            }
            """, 7,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshCapturedInvocationAlias", """
            sealed class Box { public int State; }
            class C {
                static Box Identity(Box value) => value;
                static System.Action Bind(Box value) {
                    var alias = Identity(value);
                    return () => alias.State++;
                }
                static void M() {
                    var fresh = new Box();
                    Bind(fresh)();
                }
            }
            """, 8,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshCapturedPropertyAlias", """
            sealed class Box {
                public int State;
                public Box Self => this;
            }
            class C {
                static System.Action Bind(Box value) {
                    var alias = value.Self;
                    return () => alias.State++;
                }
                static void M() {
                    var fresh = new Box();
                    Bind(fresh)();
                }
            }
            """, 10,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshCapturedSwitchInvocationAlias", """
            sealed class Box { public int State; }
            class C {
                static Box Choose(Box value, int key) => key switch { 0 => value, _ => new Box() };
                static System.Action Bind(Box value) {
                    var alias = Choose(value, 0);
                    return () => alias.State++;
                }
                static void M() {
                    var fresh = new Box();
                    Bind(fresh)();
                }
            }
            """, 8,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshCapturedMultiHopInvocationAlias", """
            sealed class Box { public int State; }
            class C {
                static Box Identity(Box value) => value;
                static Box Forward(Box value) => Identity(value);
                static System.Action Bind(Box value) {
                    var alias = Forward(value);
                    return () => alias.State++;
                }
                static void M() {
                    var fresh = new Box();
                    Bind(fresh)();
                }
            }
            """, 9,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaRetainsFreshCapturedReturnedLocalAlias", """
            sealed class Box { public int State; }
            class C {
                static Box Forward(Box value) {
                    var alias = value;
                    return alias;
                }
                static System.Action Bind(Box value) {
                    var alias = Forward(value);
                    return () => alias.State++;
                }
                static void M() {
                    var fresh = new Box();
                    Bind(fresh)();
                }
            }
            """, 11,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedFreshMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder { public Box Value = null!; }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder { Value = value };
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesAliasedCapturedFreshMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder { public Box Value = null!; }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder { Value = value };
                    var alias = holder;
                    return () => alias.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 9,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConditionalCapturedFreshMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder { public Box Value = null!; }
            class C {
                static System.Action Bind(Box value, bool first) {
                    var left = new Holder { Value = value };
                    var right = new Holder { Value = value };
                    var alias = first ? left : right;
                    return () => alias.Value.State++;
                }
                static void M(Box value) { Bind(value, true)(); }
            }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedFreshArrayElementOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    var values = new[] { value };
                    return () => values[0].State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedAnonymousMemberOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new { Value = value };
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedCollectionArrayElementOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    Box[] values = [value];
                    return () => values[0].State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedTupleMemberOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    var holder = (Value: value, Marker: 0);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedCollectionIndexerOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    var values = new System.Collections.Generic.List<Box> { value };
                    return () => values[0].State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedDictionaryIndexerOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    var values = new System.Collections.Generic.Dictionary<int, Box> { { 0, value } };
                    return () => values[0].State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedStringDictionaryIndexerOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    var values = new System.Collections.Generic.Dictionary<string, Box> { { "key", value } };
                    return () => values["key"].State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedDictionaryAssignmentOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    var values = new System.Collections.Generic.Dictionary<string, Box> { ["key"] = value };
                    return () => values["key"].State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedListCollectionExpressionOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    System.Collections.Generic.List<Box> values = [value];
                    return () => values[0].State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedCollectionSpreadOrigin", """
            sealed class Box { public int State; }
            class C {
                static System.Action Bind(Box value) {
                    Box[] source = [value];
                    System.Collections.Generic.List<Box> values = [.. source];
                    return () => values[0].State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedConstructorMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                public Holder(Box value) { Value = value; }
            }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedConstructorAliasedMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                public Holder(Box item) {
                    var alias = item;
                    Value = alias;
                }
            }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 14,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedConstructorConditionalAliasOrigins", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                public Holder(Box first, Box second, bool choose) {
                    var alias = choose ? first : second;
                    Value = alias;
                }
            }
            class C {
                static System.Action Bind(Box left, Box right, bool choose) {
                    var holder = new Holder(left, right, choose);
                    return () => holder.Value.State++;
                }
                static void M(Box left, Box right, bool choose) { Bind(left, right, choose)(); }
            }
            """, 14,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedConstructorConditionalOrigins", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                public Holder(Box first, Box second, bool choose) {
                    Value = choose ? first : second;
                }
            }
            class C {
                static System.Action Bind(Box left, Box right, bool choose) {
                    var holder = new Holder(left, right, choose);
                    return () => holder.Value.State++;
                }
                static void M(Box left, Box right, bool choose) { Bind(left, right, choose)(); }
            }
            """, 13,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedConstructorHelperOrigin", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                private static Box Keep(Box input) => input;
                public Holder(Box item) { Value = Keep(item); }
            }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 12,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedConstructorBranchedAssignmentOrigins", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                public Holder(Box first, Box second, bool choose) {
                    if (choose)
                        Value = first;
                    else
                        Value = second;
                }
            }
            class C {
                static System.Action Bind(Box left, Box right, bool choose) {
                    var holder = new Holder(left, right, choose);
                    return () => holder.Value.State++;
                }
                static void M(Box left, Box right, bool choose) { Bind(left, right, choose)(); }
            }
            """, 16,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedChainedConstructorMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                public Holder(Box item) : this(item, 0) { }
                private Holder(Box source, int _) { Value = source; }
            }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 12,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesPartialConstructorAssignmentOrigins", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value = C.Shared;
                public Holder(Box item, bool choose) { if (choose) Value = item; }
            }
            class C {
                internal static Box Shared = new();
                static System.Action Bind(Box value, bool choose) {
                    var holder = new Holder(value, choose);
                    return () => holder.Value.State++;
                }
                static void M(Box value, bool choose) { Bind(value, choose)(); }
            }
            """, 12,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesPartialConstructorPropertyOrigins", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value = C.Shared;
                public Holder(Box item, bool choose) { if (choose) Value = item; }
            }
            class C {
                private static readonly Box backing = new();
                internal static Box Shared => backing;
                static System.Action Bind(Box value, bool choose) {
                    var holder = new Holder(value, choose);
                    return () => holder.Value.State++;
                }
                static void M(Box value, bool choose) { Bind(value, choose)(); }
            }
            """, 13,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesImplicitConstructorMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value = C.Shared;
            }
            class C {
                internal static readonly Box Shared = new();
                static System.Action Bind() {
                    var holder = new Holder();
                    return () => holder.Value.State++;
                }
                static void M() { Bind()(); }
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesPositionalRecordMemberOrigin", """
            sealed class Box { public int State; }
            sealed record Holder(Box Value);
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesBaseConstructorMemberOrigin", """
            sealed class Box { public int State; }
            class Base {
                public Box Value;
                protected Base(Box item) { Value = item; }
            }
            sealed class Holder : Base { public Holder(Box item) : base(item) { } }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 12,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesImplicitBaseConstructorMemberOrigin", """
            sealed class Box { public int State; }
            class Base { public Box Value = C.Shared; }
            sealed class Holder : Base { public Holder() { } }
            class C {
                internal static readonly Box Shared = new();
                static System.Action Bind() {
                    var holder = new Holder();
                    return () => holder.Value.State++;
                }
                static void M() { Bind()(); }
            }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesTransitiveImplicitBaseConstructorMemberOrigin", """
            sealed class Box { public int State; }
            class GrandBase { public Box Value = C.Shared; }
            class Base : GrandBase { }
            sealed class Holder : Base { public Holder() { } }
            class C {
                internal static readonly Box Shared = new();
                static System.Action Bind() {
                    var holder = new Holder();
                    return () => holder.Value.State++;
                }
                static void M() { Bind()(); }
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesTransitiveExplicitBaseConstructorMemberOrigin", """
            sealed class Box { public int State; }
            class GrandBase {
                public Box Value;
                public GrandBase() { Value = C.Shared; }
            }
            class Base : GrandBase { }
            sealed class Holder : Base { public Holder() { } }
            class C {
                internal static readonly Box Shared = new();
                static System.Action Bind() {
                    var holder = new Holder();
                    return () => holder.Value.State++;
                }
                static void M() { Bind()(); }
            }
            """, 14,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesOptionalBaseConstructorMemberOrigin", """
            sealed class Box { public int State; }
            class GrandBase {
                public Box Value;
                public GrandBase(int ignored = 0) { Value = C.Shared; }
            }
            class Base : GrandBase { }
            sealed class Holder : Base { public Holder() { } }
            class C {
                internal static readonly Box Shared = new();
                static System.Action Bind() {
                    var holder = new Holder();
                    return () => holder.Value.State++;
                }
                static void M() { Bind()(); }
            }
            """, 14,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                public Holder(Box item) { (Value, _) = (item, 0); }
            }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorLocalDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                public Holder(Box item) {
                    var pair = (item, 0);
                    (Value, _) = pair;
                }
            }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 14,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorBranchedLocalDeconstructionOrigins", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                public Holder(Box first, Box second, bool choose) {
                    var pair = choose ? (first, 0) : (second, 0);
                    (Value, _) = pair;
                }
            }
            class C {
                static System.Action Bind(Box left, Box right, bool choose) {
                    var holder = new Holder(left, right, choose);
                    return () => holder.Value.State++;
                }
                static void M(Box left, Box right, bool choose) { Bind(left, right, choose)(); }
            }
            """, 14,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorHelperDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pair(Box input) => (input, 0);
                public Holder(Box item) { (Value, _) = Pair(item); }
            }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 12,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorPropertyDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pair => (C.Shared, 0);
                public Holder() { (Value, _) = Pair; }
            }
            class C {
                internal static readonly Box Shared = new();
                static System.Action Bind() {
                    var holder = new Holder();
                    return () => holder.Value.State++;
                }
                static void M() { Bind()(); }
            }
            """, 13,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorInstancePropertyDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Source {
                public Box Value = null!;
                public (Box, int) Pair => (Value, 0);
            }
            sealed class Holder {
                public Box Value;
                public Holder(Source source) { (Value, _) = source.Pair; }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 15,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorAutoPropertyDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Source {
                public Box Value { get; init; } = null!;
                public (Box, int) Pair => (Value, 0);
            }
            sealed class Holder {
                public Box Value;
                public Holder(Source source) { (Value, _) = source.Pair; }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 15,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorHelperAutoPropertyDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Source {
                public Box Value { get; init; } = null!;
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick(Source source) => (source.Value, 0);
                public Holder(Source source) { (Value, _) = Pick(source); }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 15,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorHelperFieldDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Source {
                public Box Value = null!;
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick(Source source) => (source.Value, 0);
                public Holder(Source source) { (Value, _) = Pick(source); }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 15,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorHelperArrayElementDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Source {
                public Box[] Values = null!;
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick(Source source) => (source.Values[0], 0);
                public Holder(Source source) { (Value, _) = Pick(source); }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 15,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorHelperConditionalDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Source {
                public Box Left = null!;
                public Box Right = null!;
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick(Source source, bool flag) => (flag ? source.Left : source.Right, 0);
                public Holder(Source source, bool flag) { (Value, _) = Pick(source, flag); }
            }
            class C {
                static System.Action Bind(Source input, bool flag) {
                    var holder = new Holder(input, flag);
                    return () => holder.Value.State++;
                }
                static void M(Source input, bool flag) { Bind(input, flag)(); }
            }
            """, 16,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorHelperCoalesceDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Source {
                public Box? Left;
                public Box Right = null!;
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick(Source source) => (source.Left ?? source.Right, 0);
                public Holder(Source source) { (Value, _) = Pick(source); }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 16,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorHelperSwitchDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Source {
                public Box Left = null!;
                public Box Right = null!;
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick(Source source, int key) => (key switch { 0 => source.Left, _ => source.Right }, 0);
                public Holder(Source source, int key) { (Value, _) = Pick(source, key); }
            }
            class C {
                static System.Action Bind(Source input, int key) {
                    var holder = new Holder(input, key);
                    return () => holder.Value.State++;
                }
                static void M(Source input, int key) { Bind(input, key)(); }
            }
            """, 16,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorHelperConditionalAccessDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Inner {
                public Box Value = null!;
            }
            sealed class Source {
                public Inner? Maybe;
                public Box Fallback = null!;
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick(Source source) => (source.Maybe?.Value ?? source.Fallback, 0);
                public Holder(Source source) { (Value, _) = Pick(source); }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 19,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorHelperInlineArrayDeconstructionMemberOrigin", """
            using System.Runtime.CompilerServices;
            sealed class Box { public int State; }
            [InlineArray(1)]
            struct Buffer { private Box _element0; }
            sealed class Source {
                public Buffer Values;
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick(Source source) => (source.Values[0], 0);
                public Holder(Source source) { (Value, _) = Pick(source); }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 18,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorInstanceDeconstructionMemberOrigin", """
            sealed class Source {
                public int State;
                public (Source, int) Pair() => (this, 0);
            }
            sealed class Holder {
                public Source Value;
                public Holder(Source source) { (Value, _) = source.Pair(); }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 14,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesConstructorStaticAutoPropertyDeconstructionMemberOrigin", """
            sealed class Box { public int State; }
            static class Globals {
                public static Box Value { get; set; } = new();
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick() => (Globals.Value, 0);
                public Holder() { (Value, _) = Pick(); }
            }
            class C {
                static System.Action Bind() {
                    var holder = new Holder();
                    return () => holder.Value.State++;
                }
                static void M() { Bind()(); }
            }
            """, 15,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaDoesNotBypassUserDefinedConversionDeconstructionOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box Shared = new(); }
            sealed class Source {
                public static implicit operator Box(Source source) => Globals.Shared;
            }
            sealed class Holder {
                public Box Value;
                private static (Box, int) Pick(Source source) => ((Box)source, 0);
                public Holder(Source source) { (Value, _) = Pick(source); }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 16,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaDoesNotBypassUserDefinedConversionCapturedOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box Shared = new(); }
            sealed class Source {
                public static implicit operator Box(Source source) => Globals.Shared;
            }
            class C {
                static System.Action Bind(Source source) {
                    var alias = (Box)source;
                    return () => alias.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaDoesNotBypassUserDefinedConversionConstructorOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box Shared = new(); }
            sealed class Source {
                public static implicit operator Box(Source source) => Globals.Shared;
            }
            sealed class Holder {
                public Box Value;
                public Holder(Source source) { Value = (Box)source; }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 15,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaDoesNotBypassConstructorHelperConversionOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box Shared = new(); }
            sealed class Source {
                public static implicit operator Box(Source source) => Globals.Shared;
            }
            sealed class Holder {
                public Box Value;
                private static Box Convert(Source source) => (Box)source;
                public Holder(Source source) { Value = Convert(source); }
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 16,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaDoesNotBypassUserDefinedConversionMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Payload { public Box Value = null!; }
            static class Globals { public static Payload Shared = new() { Value = new() }; }
            sealed class Source {
                public static implicit operator Payload(Source source) => Globals.Shared;
            }
            class C {
                static System.Action Bind(Source source) {
                    var holder = (Payload)source;
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 12,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("DelegateInvocationDoesNotBypassUserDefinedConversionTarget", """
            static class Globals { public static int State; }
            sealed class Source {
                public static implicit operator System.Action(Source source) => () => Globals.State++;
            }
            class C {
                static void M(Source input) { System.Action action = (System.Action)input; action(); }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("DelegateInvocationMapsUserDefinedConversionCapturedOperand", """
            sealed class Box { public int State; }
            sealed class Source {
                public Box Value = null!;
                public static implicit operator System.Action(Source source) => () => source.Value.State++;
            }
            class C {
                static void M(Source input) { System.Action action = (System.Action)input; action(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaDoesNotBypassPrimaryConstructorConversionOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box Shared = new(); }
            sealed class Source {
                public static implicit operator Box(Source source) => Globals.Shared;
            }
            sealed class Holder(Source source) {
                public Box Value = (Box)source;
            }
            class C {
                static System.Action Bind(Source input) {
                    var holder = new Holder(input);
                    return () => holder.Value.State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 14,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("FreshMemberMutationDoesNotBypassUserDefinedConversionOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box Shared = new(); }
            sealed class Source {
                public static implicit operator Box(Source source) => Globals.Shared;
            }
            sealed class Holder { public Box Value = null!; }
            class C {
                static void M(Source source) {
                    var holder = new Holder { Value = (Box)source };
                    holder.Value.State++;
                }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("PrimaryConstructorInitializerIncludesUserDefinedConversionEffects", """
            sealed class Box { }
            static class Globals { public static int Count; }
            sealed class Source {
                public static implicit operator Box(Source source) {
                    Globals.Count++;
                    return new Box();
                }
            }
            sealed class Holder(Source source) { readonly Box Value = (Box)source; }
            class C { static void M(Source input) { _ = new Holder(input); } }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("ConvertedInvocationArgumentDoesNotEscapeUnrelatedOperand", """
            sealed class Box { }
            static class Globals { public static Box Shared = new(); public static Box? Stored; }
            sealed class Source {
                public int State;
                public static implicit operator Box(Source source) => Globals.Shared;
            }
            class C {
                static void Publish(Box box) { Globals.Stored = box; }
                static void M() {
                    var fresh = new Source();
                    Publish((Box)fresh);
                    fresh.State++;
                }
            }
            """, 9,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState | SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ForeachDoesNotTreatUserDefinedConversionResultAsFresh", """
            sealed class Enumerator {
                public int State;
                public int Current => 0;
                public bool MoveNext() { State++; return false; }
            }
            static class Globals { public static Enumerator Shared = new(); }
            sealed class Source {
                public static implicit operator Enumerator(Source source) => Globals.Shared;
            }
            sealed class Values {
                public Enumerator GetEnumerator() => (Enumerator)new Source();
            }
            class C { static void M() { foreach (var _ in new Values()) { } } }
            """, 13,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesFreshOwnedState | SharpProofEffect.Unknown);
        yield return Effect("RefForeachPreservesUserDefinedSpanConversionOrigin", """
            static class Globals { public static int[] Shared = [0]; }
            sealed class Source {
                public static implicit operator System.Span<int>(Source source) => Globals.Shared;
            }
            class C {
                static void M(Source input) {
                    foreach (ref var value in (System.Span<int>)input) { value++; }
                }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("DirectMutationDoesNotBypassUserDefinedConversionOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box Shared = new(); }
            sealed class Source {
                public static implicit operator Box(Source source) => Globals.Shared;
            }
            class C { static void M(Source input) { ((Box)input).State++; } }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("InvocationMutationDoesNotBypassUserDefinedConversionOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box Shared = new(); }
            sealed class Source {
                public static implicit operator Box(Source source) => Globals.Shared;
            }
            class C {
                static Box Get(Source source) => (Box)source;
                static void M(Source input) { Get(input).State++; }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("PropertyMutationDoesNotBypassUserDefinedConversionOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box Shared = new(); }
            sealed class Source {
                public static implicit operator Box(Source source) => Globals.Shared;
                public Box Converted => (Box)this;
            }
            class C { static void M(Source input) { input.Converted.State++; } }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.Unknown);
        yield return Effect("SpreadDoesNotBypassUserDefinedConversionOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box[] Shared = [new Box()]; }
            sealed class Source {
                public static implicit operator Box[](Source source) => Globals.Shared;
            }
            class C {
                static System.Action Bind(Source source) {
                    System.Collections.Generic.List<Box> values = [.. (Box[])source];
                    return () => values[0].State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("TrailingSpreadDoesNotBypassUserDefinedConversionOrigin", """
            sealed class Box { public int State; }
            static class Globals { public static Box[] Shared = [new Box()]; }
            sealed class Source {
                public static implicit operator Box[](Source source) => Globals.Shared;
            }
            class C {
                static System.Action Bind(Source source) {
                    System.Collections.Generic.List<Box> values = [new Box(), .. (Box[])source];
                    return () => values[1].State++;
                }
                static void M(Source input) { Bind(input)(); }
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.WritesArgumentState | SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedPrimaryConstructorMemberOrigin", """
            sealed class Box { public int State; }
            sealed class Holder(Box value) { public Box Value = value; }
            class C {
                static System.Action Bind(Box value) {
                    var holder = new Holder(value);
                    return () => holder.Value.State++;
                }
                static void M(Box value) { Bind(value)(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("ReturnedLambdaPreservesCapturedPrimaryConstructorConditionalOrigins", """
            sealed class Box { public int State; }
            sealed class Holder(Box first, Box second, bool choose) {
                public Box Value = choose ? first : second;
            }
            class C {
                static System.Action Bind(Box left, Box right, bool choose) {
                    var holder = new Holder(left, right, choose);
                    return () => holder.Value.State++;
                }
                static void M(Box left, Box right, bool choose) { Bind(left, right, choose)(); }
            }
            """, 10,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesCapturedState | SharpProofEffect.Unknown);
        yield return Effect("PropertyResultRetainsDelegateTarget", """
            class C {
                static int state;
                static void Impure() { state++; }
                static System.Action Action => Impure;
                static void M() { Action(); }
            }
            """, 5,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("PropertyDelegateTargetRetainsCallerReceiver", """
            sealed class Box {
                public int State;
                public void Mutate() { State++; }
                public System.Action Action => Mutate;
            }
            class C {
                static void M(Box input) { input.Action(); }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesReceiverState | SharpProofEffect.Unknown);
        yield return Effect("ConditionalVirtualInvocationRetainsExactDispatch", """
            class Base { public virtual void Work() { } }
            sealed class Derived : Base {
                private static int state;
                public override void Work() { state++; }
            }
            class C {
                static void M() {
                    Base value = new Derived();
                    value?.Work();
                }
            }
            """, 7,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("InvocationResultRetainsExactDispatch", """
            class Base { public virtual void Work() { } }
            sealed class Derived : Base {
                private static int state;
                public override void Work() { state++; }
            }
            class C {
                static Base Create() => new Derived();
                static void M() { Create().Work(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("ReturnedParameterRetainsExactArgumentDispatch", """
            class Base { public virtual void Work() { } }
            sealed class Derived : Base {
                private static int state;
                public override void Work() { state++; }
            }
            class C {
                static Base Identity(Base value) => value;
                static void M() { Identity(new Derived()).Work(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("ReturnedConvertedParameterRetainsConversionResultDispatch", """
            class Base { public virtual void Work() { } }
            sealed class Derived : Base {
                private static int state;
                public override void Work() { state++; }
            }
            sealed class Source {
                public static implicit operator Base(Source source) => new Derived();
            }
            class C {
                static Base Identity(Base value) => value;
                static void M() {
                    var source = new Source();
                    Identity((Base)source).Work();
                }
            }
            """, 11,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("PropertyResultRetainsExactDispatch", """
            class Base { public virtual void Work() { } }
            sealed class Derived : Base {
                private static int state;
                public override void Work() { state++; }
            }
            sealed class Holder { public Base Value => new Derived(); }
            class C {
                static void M(Holder input) { input.Value.Work(); }
            }
            """, 8,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState,
            forbidden: SharpProofEffect.DispatchUncertainty | SharpProofEffect.Unknown);
        yield return Effect("CopiedDelegateTargetIsIndependentOfSourceReassignment", """
            class C {
                static int state;
                static void Pure() { }
                static void Impure() { state++; }
                static void M() {
                    System.Action first = Impure;
                    System.Action second = first;
                    first = Pure;
                    second();
                }
            }
            """, 5,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("LambdaMutationOfFreshCaptureRemainsFreshOwned", """
            sealed class Box { public int Value; }
            class C {
                static int M() {
                    var box = new Box();
                    System.Action action = () => box.Value = 1;
                    action();
                    return box.Value;
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState);
        yield return Effect("LambdaMutationOfCallerCaptureRemainsArgumentOwned", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    System.Action action = () => input.Value = 1;
                    action();
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState);
        yield return Effect("LocalFunctionMutationOfFreshCaptureRemainsFreshOwned", """
            sealed class Box { public int Value; }
            class C {
                static int M() {
                    var box = new Box();
                    void SetValue() { box.Value = 1; }
                    SetValue();
                    return box.Value;
                }
            }
            """, 3,
            purity: SharpProofVerdict.Proven,
            required: SharpProofEffect.WritesFreshOwnedState,
            forbidden: SharpProofEffect.WritesCapturedState);
        yield return Effect("LocalFunctionMutationOfCallerCaptureRemainsArgumentOwned", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    void SetValue() { input.Value = 1; }
                    SetValue();
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesArgumentState,
            forbidden: SharpProofEffect.WritesFreshOwnedState);
        yield return Effect("LocalFunctionUsesCaptureOwnershipAtInvocation", """
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    var box = new Box();
                    void SetValue() { box.Value = 1; }
                    box = input;
                    SetValue();
                }
            }
            """, 3,
            purity: SharpProofVerdict.Disproven,
            forbidden: SharpProofEffect.WritesFreshOwnedState);
        yield return Effect("UncheckedExpressionBodiedLocalFunctionHasAnalyzableBody", """
            class C {
                static int M(int x) {
                    int Local() => unchecked((x << 1) ^ 17);
                    return Local();
                }
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("CheckedExpressionBodiedLocalFunctionHasAnalyzableBody", """
            class C {
                static int M(int x) {
                    int Local() => checked(x + 1);
                    return Local();
                }
            }
            """, 2,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.Unknown);
        yield return Effect("ReassignedDelegateUsesTheCurrentTarget", """
            class C {
                static int state;
                static void Pure() { }
                static void Impure() { state++; }
                static void M() { System.Action action = Pure; action = Impure; action(); }
            }
            """, 5,
            purity: SharpProofVerdict.Disproven);
        yield return Effect("CombinedDelegateIncludesAddedTargetEffects", """
            class C {
                static int state;
                static void Pure() { }
                static void Impure() { state++; }
                static void M() {
                    System.Action action = Pure;
                    action += Impure;
                    action();
                }
            }
            """, 6,
            purity: SharpProofVerdict.Disproven,
            required: SharpProofEffect.WritesStaticState);
        yield return Effect("RemovedDelegateTargetDoesNotRetainItsEffects", """
            class C {
                static int state;
                static void Impure() { state++; }
                static void M() {
                    System.Action? action = Impure;
                    action -= Impure;
                    action!();
                }
            }
            """, 4,
            purity: SharpProofVerdict.Proven,
            forbidden: SharpProofEffect.WritesStaticState);
        yield return Effect("DelegateSubtractionRemovesOnlyTheLastMatchingHandler", """
            class C {
                static int state;
                static void Impure() { state++; }
                static void M() {
                    System.Action action = Impure;
                    action += Impure;
                    action -= Impure;
                    action();
                }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven);
        yield return Effect("CompoundPropertyUpdateIncludesGetterAndSetterBehavior", """
            class C {
                static int state;
                static int P { get { state++; return state; } set { } }
                static void M() { P += 1; }
            }
            """, 4,
            purity: SharpProofVerdict.Disproven);
        yield return Effect("NativeBoundaryCannotProveDoesNotThrow", """
            using System.Runtime.InteropServices;
            class C {
                [DllImport("missing")] static extern int Native();
                static int M() => Native();
            }
            """, 4,
            doesNotThrow: SharpProofVerdict.Unknown);
        yield return Effect("CompleteNativeContractCanCloseTheExceptionBoundary", """
            using System.Runtime.InteropServices;
            using SharpProof.Attributes;
            class C {
                [DllImport("missing"), EffectContract(SharpProofEffect.None, Complete = true)]
                static extern int Native();
                static int M() => Native();
            }
            """, 6,
            required: SharpProofEffect.UsesNativeCode,
            doesNotThrow: SharpProofVerdict.Proven);
        yield return Effect("AmbientWritesDisprovePurity", """
            using SharpProof.Attributes;
            class C {
                [EffectContract(SharpProofEffect.WritesAmbientState, Complete = true)]
                static extern void M();
            }
            """, 4,
            purity: SharpProofVerdict.Disproven);
    }
    [TestCaseSource(nameof(EffectCases))]
    public void EffectMatrix(EffectCase testCase) {
        var result = Analyze(testCase.Source, testCase.Line);
        var effects = result.MethodEffects!;
        Assert.Multiple(() => {
            if (testCase.Purity.HasValue)
                Assert.That(effects.Purity, Is.EqualTo(testCase.Purity.Value));
            Assert.That(effects.Effects & testCase.RequiredEffects, Is.EqualTo(testCase.RequiredEffects));
            Assert.That(effects.Effects & testCase.ForbiddenEffects, Is.EqualTo(SharpProofEffect.None));
            if (testCase.AllocationFree.HasValue)
                Assert.That(effects.AllocationFree, Is.EqualTo(testCase.AllocationFree.Value));
            if (testCase.DoesNotThrow.HasValue)
                Assert.That(effects.DoesNotThrow, Is.EqualTo(testCase.DoesNotThrow.Value));
            Assert.That(
                effects.Capabilities & testCase.RequiredCapabilities,
                Is.EqualTo(testCase.RequiredCapabilities));
            Assert.That(
                effects.Capabilities & testCase.ForbiddenCapabilities,
                Is.EqualTo(SharpProofCapability.None));
            if (testCase.ExceptionFact != null)
                Assert.That(effects.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                    fact.ExceptionType == testCase.ExceptionFact.ExceptionType &&
                    fact.Escape == testCase.ExceptionFact.Escape &&
                    fact.Source == testCase.ExceptionFact.Source));
            if (testCase.EffectSite != null)
                Assert.That(effects.Sites, Has.Some.Matches<MethodEffectSite>(site =>
                    site.Effect == testCase.EffectSite.Effect &&
                    (testCase.EffectSite.Symbol == null || site.Symbol == testCase.EffectSite.Symbol)));
            if (testCase.UnknownReason != null)
                Assert.That(result.UnknownReasons, Has.Some.Property(nameof(SharpProofUnknownReason.Message))
                    .EqualTo(testCase.UnknownReason));
            testCase.Verify?.Invoke(result);
        });
    }
    private static TestCaseData Effect(
        string name,
        string source,
        int line = 2,
        SharpProofVerdict? purity = null,
        SharpProofEffect required = SharpProofEffect.None,
        SharpProofEffect forbidden = SharpProofEffect.None,
        SharpProofVerdict? allocationFree = null,
        SharpProofVerdict? doesNotThrow = null,
        SharpProofCapability requiredCapabilities = SharpProofCapability.None,
        SharpProofCapability forbiddenCapabilities = SharpProofCapability.None,
        ExpectedExceptionFact? exceptionFact = null,
        ExpectedEffectSite? effectSite = null,
        string? unknownReason = null,
        Action<SharpProofAnalysisResult>? verify = null) => new TestCaseData(new EffectCase(
            name, source, line, purity, required, forbidden, allocationFree, doesNotThrow,
            requiredCapabilities, forbiddenCapabilities, exceptionFact, effectSite, unknownReason, verify)).SetName(name);
    [TestCase("return new object();", SharpProofVerdict.Proven, SharpProofVerdict.Disproven)]
    [TestCase("throw null!;", SharpProofVerdict.Proven, SharpProofVerdict.Proven)]
    public void PurityIsDerivedIndependentlyFromAllocationAndThrows(string statement, SharpProofVerdict expectedPurity,
        SharpProofVerdict expectedAllocationFree) {
        using var session = SharpProofAnalysisSession.FromText($$"""
            #nullable enable
            class C {
                object M() { {{statement}} }
            }
            """);
        var result = session.Analyze(new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Line, Line: 3),
            SharpProofAnalysisFacet.Effects));
        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(SharpProofQueryStatus.Succeeded));
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(expectedPurity));
            Assert.That(result.MethodEffects.AllocationFree, Is.EqualTo(expectedAllocationFree));
        });
    }
    [Test]
    public void VisibleStaticMutationDisprovesPurity() {
        using var session = SharpProofAnalysisSession.FromText("""
            class C {
                static int value;
                static void M() { value++; }
            }
            """);
        var result = session.Analyze(new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Line, Line: 3),
            SharpProofAnalysisFacet.Effects));
        Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
        Assert.That(result.MethodEffects!.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
    }
    [Test]
    public void VirtualAutoPropertyGetterKeepsDispatchUncertainty() {
        var result = Analyze("""
            class Base { public virtual int Value { get; } }
            sealed class Derived : Base {
                static int state;
                public override int Value { get { state++; return state; } }
            }
            class C { static int M(Base value) => value.Value; }
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.Not.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.DispatchUncertainty), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.True);
        });
    }
    [Test]
    public void AutoPropertyOverrideKeepsFurtherDispatchUncertainty() {
        var result = Analyze("""
            class Root { public virtual int Value { get; } }
            class Middle : Root { public override int Value { get; } }
            sealed class Leaf : Middle {
                static int state;
                public override int Value { get { state++; return state; } }
            }
            class C { static int M(Middle value) => value.Value; }
            """, 7);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.Not.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.DispatchUncertainty), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.True);
        });
    }
    [Test]
    public void SynthesizedVirtualMethodKeepsDispatchUncertainty() {
        var result = Analyze("""
            record Base;
            record Derived : Base {
                static int state;
                public override string ToString() { state++; return state.ToString(); }
            }
            class C { static string M(Base value) => value.ToString(); }
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.Not.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.DispatchUncertainty), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.True);
        });
    }
    [TestCase("> 0 and < 10")]
    [TestCase("< 0 or > 2")]
    [TestCase("not < 0")]
    public void ConstantCompoundSwitchSkipsUnselectedArm(string pattern) {
        var result = Analyze($$"""
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => 5 switch { {{pattern}} => 1, _ => Mutate() };
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [TestCase("> 10 and < 20")]
    [TestCase("< 0 or > 10")]
    [TestCase("not > 0")]
    public void ConstantCompoundSwitchKeepsSelectedFallback(string pattern) {
        var result = Analyze($$"""
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => 5 switch { {{pattern}} => 1, _ => Mutate() };
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void ConstantSwitchKeepsNonconstantGuardEffects() {
        var statement = Analyze("""
            class C {
                static int state;
                static bool Guard() { state++; return false; }
                static void M() {
                    switch (1) {
                        case 1 when Guard(): break;
                        default: break;
                    }
                }
            }
            """, 4);
        var expression = Analyze("""
            class C {
                static int state;
                static bool Guard() { state++; return false; }
                static int M() => 1 switch { 1 when Guard() => 1, _ => 0 };
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(statement.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven), "statement guard");
            Assert.That(statement.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True,
                "statement guard");
            Assert.That(expression.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven), "expression guard");
            Assert.That(expression.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True,
                "expression guard");
        });
    }
    [Test]
    public void ConstantSwitchDoesNotSkipMatchingRecursivePattern() {
        var statement = Analyze("""
            class C {
                static int state;
                static void M() {
                    switch ("value") {
                        case { Length: > 0 }: state++; break;
                        default: break;
                    }
                }
            }
            """, 3);
        var expression = Analyze("""
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => "value" switch { { Length: > 0 } => Mutate(), _ => 0 };
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(statement.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven), "statement pattern");
            Assert.That(statement.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True,
                "statement pattern");
            Assert.That(expression.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven), "expression pattern");
            Assert.That(expression.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True,
                "expression pattern");
        });
    }
    [TestCase("true || Mutate()")]
    [TestCase("false && Mutate()")]
    public void ConstantBooleanShortCircuitSkipsRightOperand(string expression) {
        var result = Analyze($$"""
            class C {
                static int state;
                static bool Mutate() { state++; return true; }
                static bool M() => {{expression}};
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [TestCase("false || Mutate()")]
    [TestCase("true && Mutate()")]
    public void ConstantBooleanShortCircuitKeepsExecutedRightOperand(string expression) {
        var result = Analyze($$"""
            class C {
                static int state;
                static bool Mutate() { state++; return true; }
                static bool M() => {{expression}};
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void ArrayForeachUsesIntrinsicEffects() {
        var result = Analyze("""
            class C {
                static void M(int[] values) {
                    foreach (var value in values) { }
                }
            }
            """, 2);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.ReadsArgumentState), Is.True);
            Assert.That(result.UnknownReasons, Is.Empty);
        });
    }
    [Test]
    public void StringForeachUsesIntrinsicEffects() {
        var result = Analyze("""
            class C {
                static void M(string values) {
                    foreach (var value in values) { }
                }
            }
            """, 2);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.UnknownReasons, Is.Empty);
        });
    }
    [TestCase("System.Span<int>")]
    [TestCase("System.ReadOnlySpan<int>")]
    public void SpanForeachUsesIntrinsicEffects(string spanType) {
        var result = Analyze($$"""
            class C {
                static void M({{spanType}} values) {
                    foreach (var value in values) { }
                }
            }
            """, 2);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.UnknownReasons, Is.Empty);
        });
    }
    [Test]
    public void InlineArrayForeachUsesIntrinsicEffects() {
        var result = Analyze("""
            [System.Runtime.CompilerServices.InlineArray(4)]
            struct Buffer { private int element; }
            class C {
                static void M(Buffer values) {
                    foreach (var value in values) { }
                }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.UnknownReasons, Is.Empty);
        });
    }
    [TestCase("static int M(int[] values) => values.Length;", SharpProofVerdict.Proven)]
    [TestCase("static int[] M(int x) => [1, x, 3];", SharpProofVerdict.Disproven)]
    public void ArrayIntrinsicsHaveStructuralEffects(string method, SharpProofVerdict expectedAllocationFree) {
        var result = Analyze("class C {\n" + method + "\n}");
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.AllocationFree, Is.EqualTo(expectedAllocationFree));
            Assert.That(result.UnknownReasons, Is.Empty);
        });
    }
    [Test]
    public void RangeTargetsAggregateOnlySelectedMethods() {
        const string source = """
            class C {
                static int[] Allocate(int x) => [x];
                static int state;
                static void Mutate() => state++;
            }
            """;
        using var session = SharpProofAnalysisSession.FromText(source);
        var all = session.Analyze(new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.AllLines),
            SharpProofAnalysisFacet.Effects));
        var methodStart = source.IndexOf("static int[]", StringComparison.Ordinal);
        var methodEnd = source.IndexOf(';', methodStart) + 1;
        var span = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Span, SpanStart: methodStart, SpanEnd: methodEnd),
            SharpProofAnalysisFacet.Effects));
        Assert.Multiple(() => {
            Assert.That(all.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(all.MethodEffects.AllocationFree, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(span.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(span.MethodEffects.AllocationFree, Is.EqualTo(SharpProofVerdict.Disproven));
        });
    }
    [Test]
    public void UnresolvedDispatchRemainsUnknown() {
        using var session = SharpProofAnalysisSession.FromText("""
            interface I { int Read(); }
            class C {
                static int M(I value) => value.Read();
            }
            """);
        var result = session.Analyze(new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Line, Line: 3),
            SharpProofAnalysisFacet.Effects));
        Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Unknown));
        Assert.That(result.MethodEffects!.Effects.HasFlag(SharpProofEffect.DispatchUncertainty), Is.True);
    }
    [Test]
    public void UserDefinedBinaryOperatorIncludesOperatorEffects() {
        var result = Analyze("""
            static class Globals { public static int Count; }
            readonly struct Value {
                public static Value operator +(Value left, Value right) {
                    Globals.Count++;
                    return left;
                }
            }
            class C { static Value M(Value left, Value right) => left + right; }
            """, 8);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.False);
        });
    }
    [TestCase("throw new E();", "E", MethodExceptionSource.ExplicitThrow)]
    [TestCase("var zero = 0; return 10 / zero;", "System.DivideByZeroException", MethodExceptionSource.RuntimeHazard)]
    public void EscapingExceptionsAreCanonicalStructuredFacts(string body, string exceptionType, MethodExceptionSource source) {
        var result = Analyze($$"""
            sealed class E : System.Exception { }
            class C {
                static int M() { {{body}} }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven), string.Join(" | ",
                result.UnknownReasons.Select(static reason => reason.Message)));
            Assert.That(result.MethodEffects.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects!.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == exceptionType && fact.Source == source &&
                fact.Escape == SharpProofVerdict.Proven));
        });
    }
    [Test]
    public void CaughtExceptionDoesNotEscape() {
        var result = Analyze("""
            sealed class E : System.Exception { }
            class C {
                static int M() {
                    try { throw new E(); }
                    catch (E) { return 1; }
                }
            }
            """, 3);
        Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Proven),
            string.Join(" | ", result.MethodEffects!.ExceptionFacts.Select(static fact =>
                fact.ExceptionType + ":" + fact.Escape + ":" + fact.Reason)));
        Assert.That(result.MethodEffects!.ExceptionFacts,
            Has.Some.Property(nameof(MethodExceptionFact.Escape)).EqualTo(SharpProofVerdict.Disproven));
    }
    [Test]
    public void FalseCatchFilterDoesNotCatchExplicitThrow() {
        var result = Analyze("""
            sealed class E : System.Exception { }
            class C {
                static void M() {
                    try { throw new E(); }
                    catch (E) when (false) { }
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "E" && fact.Escape == SharpProofVerdict.Proven));
        });
    }
    [Test]
    public void FalseCatchFilterDoesNotCatchRuntimeHazard() {
        var result = Analyze("""
            class C {
                static int M() {
                    var zero = 0;
                    try { return 10 / zero; }
                    catch (System.DivideByZeroException) when (false) { return 0; }
                }
            }
            """, 2);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "System.DivideByZeroException" && fact.Escape == SharpProofVerdict.Proven));
        });
    }
    [Test]
    public void BaseExceptionCatchCatchesRuntimeHazard() {
        var result = Analyze("""
            class C {
                static int M() {
                    var zero = 0;
                    try { return 10 / zero; }
                    catch (System.Exception) { return 0; }
                }
            }
            """, 2);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "System.DivideByZeroException" && fact.Escape == SharpProofVerdict.Disproven));
        });
    }
    [Test]
    public void CompileTimeFalseThrowDoesNotEscape() {
        var result = Analyze("""
            class C {
                static int M() {
                    if (false) {
                        throw new System.InvalidOperationException();
                    }
                    return 1;
                }
            }
            """, 2);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Throws), Is.False);
            Assert.That(result.MethodEffects.ExceptionFacts,
                Has.None.Property(nameof(MethodExceptionFact.Escape)).EqualTo(SharpProofVerdict.Proven));
        });
    }
    [Test]
    public void CatchFilterIncludesEffectsWithoutEscapingItsException() {
        var result = Analyze("""
            static class Globals { public static int Count; }
            sealed class E : System.Exception { }
            class C {
                static bool Filter() { Globals.Count++; throw new System.FormatException(); }
                static void M() {
                    try { throw new E(); }
                    catch (E) when (Filter()) { }
                }
            }
            """, 5);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
            Assert.That(result.MethodEffects.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "E" && fact.Escape == SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.None.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "System.FormatException" && fact.Escape == SharpProofVerdict.Proven));
        });
    }
    [Test]
    public void FinallyThrowReplacesPendingException() {
        var result = Analyze("""
            sealed class FirstException : System.Exception { }
            sealed class FinallyException : System.Exception { }
            class C {
                static void M() {
                    try { throw new FirstException(); }
                    finally { throw new FinallyException(); }
                }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "FinallyException" && fact.Escape == SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.None.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "FirstException" && fact.Escape == SharpProofVerdict.Proven));
        });
    }
    [Test]
    public void FinallyThrowReplacesPendingRuntimeHazard() {
        var result = Analyze("""
            sealed class FinallyException : System.Exception { }
            class C {
                static int M() {
                    var zero = 0;
                    try { return 10 / zero; }
                    finally { throw new FinallyException(); }
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "FinallyException" && fact.Escape == SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.None.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "System.DivideByZeroException" && fact.Escape == SharpProofVerdict.Proven));
        });
    }
    [Test]
    public void RethrowPreservesCaughtExceptionType() {
        var result = Analyze("""
            sealed class E : System.Exception { }
            class C {
                static void M() {
                    try { throw new E(); }
                    catch (E) { throw; }
                }
            }
            """, 3);
        Assert.That(result.MethodEffects, Is.Not.Null);
        Assert.That(result.MethodEffects!.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
            fact.ExceptionType == "E" && fact.Escape == SharpProofVerdict.Proven));
    }
    [Test]
    public void CaughtCalleeExceptionDoesNotEscape() {
        var result = Analyze("""
            sealed class E : System.Exception { }
            class C {
                static void Throw() { throw new E(); }
                static void M() {
                    try { Throw(); }
                    catch (E) { }
                }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "E" && fact.Escape == SharpProofVerdict.Disproven));
        });
    }
    [Test]
    public void SourceCalleeExceptionIsTransitive() {
        var result = Analyze("""
            class C {
                static int Throw() => throw new System.FormatException();
                static int M() => Throw();
            }
            """, 3);
        Assert.That(result.MethodEffects!.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
            fact.Source == MethodExceptionSource.Callee && fact.IsTransitive &&
            fact.ExceptionType == "System.FormatException"));
    }
    [Test]
    public void RecursionProducesUnknownEvidence() {
        var result = Analyze("""
            class C {
                static int M(int value) => value == 0 ? 0 : M(value - 1);
            }
            """);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Unknown));
            Assert.That(result.UnknownReasons, Has.Some.Property(nameof(SharpProofUnknownReason.Message)).EqualTo("recursive_call"));
        });
    }
    [Test]
    public void LockAndNativeCallsProduceStructuralCapabilities() {
        var lockResult = Analyze("""
            class C {
                static void M(object gate) { lock (gate) { } }
            }
            """);
        var nativeResult = Analyze("""
            using System.Runtime.InteropServices;
            class C {
                [DllImport("native")] static extern int Call();
                static int M() => Call();
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(lockResult.MethodEffects!.Effects.HasFlag(SharpProofEffect.Synchronizes), Is.True);
            Assert.That(lockResult.MethodEffects.Capabilities.HasFlag(SharpProofCapability.Synchronization), Is.True);
            Assert.That(nativeResult.MethodEffects!.Effects.HasFlag(SharpProofEffect.UsesNativeCode), Is.True);
            Assert.That(nativeResult.MethodEffects.Capabilities.HasFlag(SharpProofCapability.NativeInterop), Is.True);
        });
    }
    [TestCase("static string M(int value) => value.ToString();")]
    [TestCase("static string[] M(string value) => value.Split(',');")]
    [TestCase("static int[] M(System.Span<int> value) => value.ToArray();")]
    [TestCase("static int[] M(int[] a) => a[1..2];")]
    public void ExactFrameworkAllocationModelsDisproveAllocationFreedom(string method) {
        var result = Analyze("class C {\n" + method + "\n}");
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.AllocationFree, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Allocates), Is.True);
        });
    }
    [Test]
    public void NumericParseDisprovesDoesNotThrow() {
        var result = Analyze("class C {\nstatic int M(string value) => int.Parse(value);\n}");
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.Some.Property(nameof(MethodExceptionFact.ExceptionType))
                .EqualTo("System.FormatException"));
        });
    }
    [Test]
    public void TryParseWritingFreshLocalOutValueRemainsPure() {
        var result = Analyze("class C {\nstatic int M(string text) => int.TryParse(text, out var value) ? value : 0;\n}");
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.UnknownReasons.Select(static reason => reason.Message)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.False);
        });
    }
    [Test]
    public void ConditionalAccessThroughStringTrimRemainsPure() {
        var result = Analyze(
            "class C {\nstatic int M(string text, string fallback) => text?.Trim().Length ?? fallback.Length;\n}");
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.UnknownReasons.Select(static reason => reason.Message)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.False);
        });
    }
    [Test]
    public void StringRangeSliceRemainsPure() {
        var result = Analyze("class C {\nstatic int M(string text) => text[1..^1].Length;\n}");
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.False);
        });
    }
    [TestCase("static string M(string a, string b) => a + b;")]
    [TestCase("static string M(int value) => $\"{value}\";")]
    [TestCase("static object M(int value) => value;")]
    [TestCase("static System.Func<int> M(int value) => () => value;")]
    [TestCase("static System.Func<int> M() => Helper;\nstatic int Helper() => 1;")]
    [TestCase("static R M(R value) => value with { X = 2 }; sealed record R(int X);")]
    [TestCase("static System.Collections.Generic.IEnumerable<int> M() { yield return 1; }")]
    [TestCase("static async System.Threading.Tasks.Task<int> M() { await System.Threading.Tasks.Task.Yield(); return 1; }")]
    public void CompilerGeneratedAllocationsAreVisible(string method) {
        var result = Analyze("class C {\n" + method + "\n}");
        Assert.That(result.MethodEffects!.AllocationFree, Is.EqualTo(SharpProofVerdict.Disproven),
            string.Join(" | ", result.UnknownReasons.Select(static reason => reason.Message)));
    }
    [Test]
    public void AsyncLocalFunctionStateMachineAllocationIsVisible() {
        const string source = """
            class C {
                static System.Threading.Tasks.Task M() {
                    return Local();
                    static async System.Threading.Tasks.Task Local() {
                        await System.Threading.Tasks.Task.Yield();
                    }
                }
            }
            """;
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "AsyncLocalAllocation",
            [tree],
            SymbolicSourceCompilation.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var effects = new MethodEffectAnalysisSession(compilation, default).Analyze(method, declaration, model);
        Assert.Multiple(() => {
            Assert.That(effects.AllocationFree, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.Allocates), Is.True);
            Assert.That(effects.Sites,
                Has.Some.Property(nameof(MethodEffectSite.Reason)).EqualTo("state_machine_allocation"));
        });
    }
    [Test]
    public void UnusedIteratorLocalFunctionDoesNotAllocateInOuterMethod() {
        var result = Analyze("""
            class C {
                static void M() {
                    static System.Collections.Generic.IEnumerable<int> Local() {
                        yield return 1;
                    }
                }
            }
            """);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.AllocationFree, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Sites,
                Has.None.Property(nameof(MethodEffectSite.Reason)).EqualTo("state_machine_allocation"));
        });
    }
    [Test]
    public void PolymorphicWithExpressionKeepsCloneDispatchUncertainty() {
        var result = Analyze("""
            record Base { public int X { get; init; } }
            record Derived : Base {
                static int state;
                protected Derived(Derived other) : base(other) { state++; }
            }
            class C { static Base M(Base value) => value with { X = 1 }; }
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.Not.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.DispatchUncertainty), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.True);
        });
    }
    [TestCase("unsafe static int M() { int* value = stackalloc int[1]; value[0] = 1; return value[0]; }")]
    [TestCase("static System.Span<int> M(int[] values) => new System.Span<int>(values);")]
    [TestCase("static R M() => new R(1); readonly record struct R(int X);")]
    [TestCase("static R M(R value) => value with { X = 2 }; readonly record struct R(int X);")]
    public void StackOnlyOperationsDoNotCreateManagedAllocationSites(string method) {
        var result = Analyze("class C {\n" + method + "\n}");
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Effects.HasFlag(SharpProofEffect.Allocates), Is.False);
            Assert.That(result.MethodEffects.Sites, Has.None.Property(nameof(MethodEffectSite.Effect))
                .EqualTo(SharpProofEffect.Allocates));
        });
    }
    [TestCase("while (false) { state++; }")]
    [TestCase("return; state++;")]
    [TestCase("switch (0) { case 1: state++; break; }")]
    public void UnreachableWritesDoNotDisprovePurity(string body) {
        var result = Analyze("class C { static int state;\nstatic void M() { " + body + " }\n}");
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [Test]
    public void EffectFlowStateKeyIncludesRefLocalBindings() {
        const string source = """
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    ref int alias = ref input.Value;
                }
            }
            """;
        var (syntaxTree, compilation) = SymbolicSourceCompilation.Create(
            source, "RefLocalStateKey.cs", SymbolicSourceCompilationKind.Query, null, default);
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var methodDeclaration = syntaxTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)semanticModel.GetDeclaredSymbol(methodDeclaration)!;
        var aliasDeclaration = methodDeclaration.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
        var alias = (ILocalSymbol)semanticModel.GetDeclaredSymbol(aliasDeclaration)!;
        var initial = EffectFlowState.Create(method);
        var argumentState = initial with {
            RefLocals = initial.RefLocals.SetItem(alias, initial.GetParameter(method.Parameters[0]))
        };
        var freshState = initial with {
            RefLocals = initial.RefLocals.SetItem(alias, EffectFlowValue.Fresh(alias.Type))
        };

        Assert.That(argumentState.Key, Is.Not.EqualTo(freshState.Key));
    }
    [Test]
    public void CrossFilePartialEventInitializerEffectsAreIncluded() {
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var constructorTree = CSharpSyntaxTree.ParseText("""
            partial class D { public D() { } }
            class C { static D M() => new D(); }
            """, options, "Constructor.cs");
        var initializerTree = CSharpSyntaxTree.ParseText("""
            static class Globals { public static int Count; }
            partial class D {
                public event System.Action Changed = CreateHandler();
                private static System.Action CreateHandler() { Globals.Count++; return () => { }; }
            }
            """, options, "Initializer.cs");
        var compilation = CSharpCompilation.Create(
            "PartialInitializer",
            [constructorTree, initializerTree],
            SymbolicSourceCompilation.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
            Is.Empty);
        var model = compilation.GetSemanticModel(constructorTree);
        var declaration = constructorTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method.Identifier.ValueText == "M");
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        var effects = new MethodEffectAnalysisSession(compilation, default).Analyze(method, declaration, model);
        Assert.Multiple(() => {
            Assert.That(effects.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(effects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void StaticMethodAnalysisWrapsTypeInitializerExceptions() {
        var result = Analyze("""
            sealed class D {
                static D() { throw new System.InvalidOperationException(); }
                public static void M() { }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "System.TypeInitializationException" && fact.Escape == SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.ExceptionFacts, Has.None.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == "System.InvalidOperationException" && fact.Escape == SharpProofVerdict.Proven));
        });
    }
    [TestCase("value += 2;")]
    [TestCase("value++;")]
    [TestCase("value--;")]
    public void UpdatingLocalCopyDoesNotWriteArgumentState(string update) {
        var result = Analyze("class C {\nstatic int M(int input) { var value = input; " + update + " return value; }\n}");
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.False);
        });
    }
    [TestCase("choose ? (() => { }) : (() => state++)")]
    [TestCase("choose ? (() => state++) : (() => { })")]
    public void ConditionalLambdaInvocationRetainsEveryTarget(string expression) {
        var result = Analyze($$"""
            class C {
                static int state;
                static void M(bool choose) {
                    System.Action action = {{expression}};
                    action();
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void LoopBackEdgesDoNotReusePreAssignmentFreshness() {
        var result = Analyze("""
            class Box { public int Value; }
            class C {
                static void M(Box input, bool repeat) {
                    var box = new Box();
                    do { box.Value++; box = input; } while (repeat);
                }
            }
            """, 3);
        Assert.That(result.MethodEffects!.Purity, Is.Not.EqualTo(SharpProofVerdict.Proven));
    }
    [Test]
    public void LoopBackEdgesDoNotReusePreAssignmentDelegateTargets() {
        var result = Analyze("""
            class C {
                static int state;
                static void Pure() { }
                static void Impure() { state++; }
                static void M(bool repeat) {
                    System.Action action = Pure;
                    do { action(); action = Impure; } while (repeat);
                }
            }
            """, 6);
        Assert.That(result.MethodEffects!.Purity, Is.Not.EqualTo(SharpProofVerdict.Proven));
    }
    [Test]
    public void ConflictingCompleteEffectContractsRemainUnknown() {
        var result = Analyze("""
            using SharpProof.Attributes;
            class C {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                [EffectContract(SharpProofEffect.WritesStaticState, Complete = true)]
                static extern void Boundary();
                static void M() => Boundary();
            }
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.Not.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.UnknownReasons, Has.Some.Property(nameof(SharpProofUnknownReason.Message))
                .EqualTo("conflicting_effect_contracts"));
        });
    }
    private static SharpProofAnalysisResult Analyze(string source, int line = 2) {
        using var session = SharpProofAnalysisSession.FromText(source);
        return session.Analyze(new SharpProofAnalysisRequest(new SharpProofTarget(SharpProofTargetKind.Line, Line: line),
            SharpProofAnalysisFacet.Effects));
    }
}
