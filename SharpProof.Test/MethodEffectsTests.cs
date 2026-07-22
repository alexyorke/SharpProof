using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Symbolic;
namespace SharpProof.Test;
[TestFixture]
public sealed class MethodEffectsTests {
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
    public void NameofDoesNotEvaluatePropertyGetter() {
        var result = Analyze("""
            class C {
                static int state;
                static int P { get { state++; return 1; } }
                static string M() => nameof(P);
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.ReadsStaticState), Is.False);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [Test]
    public void UndefinedConditionalCallHasNoRuntimeEffects() {
        var result = Analyze("""
            class C {
                static int state;
                [System.Diagnostics.Conditional("SHARPPROOF_NEVER")]
                static void Trace() { state++; }
                static void M() { Trace(); }
            }
            """, 5);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [Test]
    public void DefinedConditionalCallKeepsRuntimeEffects() {
        var result = Analyze("""
            #define SHARPPROOF_ENABLED
            class C {
                static int state;
                [System.Diagnostics.Conditional("SHARPPROOF_ENABLED")]
                static void Trace() { state++; }
                static void M() { Trace(); }
            }
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void UnimplementedPartialCallHasNoRuntimeEffects() {
        var result = Analyze("""
            partial class C {
                static int state;
                static partial void Hook(int value);
                static int Mutate() { state++; return 0; }
                static void M() { Hook(Mutate()); }
            }
            """, 5);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [Test]
    public void ImplementedPartialCallIncludesImplementationEffects() {
        var result = Analyze("""
            partial class C {
                static int state;
                static partial void Hook();
                static partial void Hook() { state++; }
                static void M() { Hook(); }
            }
            """, 5);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void ConstantSwitchExpressionSkipsUnselectedArm() {
        var result = Analyze("""
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => 0 switch { 0 => 1, _ => Mutate() };
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [Test]
    public void ConstantSwitchExpressionKeepsSelectedArm() {
        var result = Analyze("""
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => 1 switch { 0 => 1, _ => Mutate() };
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void ConstantRelationalSwitchSkipsUnselectedArm() {
        var result = Analyze("""
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => 5 switch { > 0 => 1, _ => Mutate() };
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [Test]
    public void ConstantRelationalSwitchKeepsSelectedArm() {
        var result = Analyze("""
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => -5 switch { > 0 => 1, _ => Mutate() };
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
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
    public void ConstantTypeSwitchSkipsUnselectedArm() {
        var result = Analyze("""
            class C {
                static int state;
                static int Mutate() { state++; return 1; }
                static int M() => "value" switch { string => 1, _ => Mutate() };
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [Test]
    public void NullConditionalSkipsGetterForConstantNullReceiver() {
        var result = Analyze("""
            class C {
                static int state;
                int P { get { state++; return 1; } }
                static int? M() => ((C?)null)?.P;
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [Test]
    public void NullConditionalKeepsGetterForNonNullReceiver() {
        var result = Analyze("""
            class C {
                static int state;
                int P { get { state++; return 1; } }
                static int? M() => new C()?.P;
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void NonNullCoalesceSkipsRightOperand() {
        var result = Analyze("""
            class C {
                static int state;
                static string Mutate() { state++; return "fallback"; }
                static string M() => "value" ?? Mutate();
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.False);
        });
    }
    [Test]
    public void NullCoalesceKeepsRightOperand() {
        var result = Analyze("""
            class C {
                static int state;
                static string Mutate() { state++; return "fallback"; }
                static string M() => ((string?)null) ?? Mutate();
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
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
    [Test]
    public void StringNullOrWhiteSpaceGuardWithThrowRemainsPure() {
        var result = Analyze("""
            class C {
                sealed class E : System.Exception { }
                static int M(string text) => string.IsNullOrWhiteSpace(text) ? throw new E() : text.Length;
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Disproven));
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
    public void AwaitIncludesGetAwaiterEffects() {
        var result = Analyze("""
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
            """, 11);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void AwaitIncludesContinuationEffects() {
        var result = Analyze("""
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
            """, 11);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void ListCollectionExpressionAllocates() {
        var result = Analyze("""
            class C {
                static System.Collections.Generic.List<int> M() => [1, 2, 3];
            }
            """);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.AllocationFree, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Allocates), Is.True);
        });
    }
    [Test]
    public void FreshCollectionInitializerRemainsPure() {
        var result = Analyze("""
            class C {
                static System.Collections.Generic.List<int> M() => new() { 1, 2, 3 };
            }
            """);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.AllocationFree, Is.EqualTo(SharpProofVerdict.Disproven));
        });
    }
    [Test]
    public void CollectionExpressionIncludesConstructorEffects() {
        var result = Analyze("""
            sealed class Bag : System.Collections.Generic.IEnumerable<int> {
                private static int state;
                public Bag() { state++; }
                public void Add(int value) { }
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => throw null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            class C { static Bag M() => [1, 2]; }
            """, 8);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void CollectionExpressionIncludesAddEffects() {
        var result = Analyze("""
            sealed class Bag : System.Collections.Generic.IEnumerable<int> {
                private static int state;
                public Bag() { }
                public void Add(int value) { state++; }
                public System.Collections.Generic.IEnumerator<int> GetEnumerator() => throw null!;
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
            }
            class C { static Bag M() => [1, 2]; }
            """, 8);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void CollectionExpressionIncludesSpreadEnumerationEffects() {
        var result = Analyze("""
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
            """, 12);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [TestCase("unsafe static int M() { int* value = stackalloc int[1]; value[0] = 1; return value[0]; }")]
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
    public void OpenVirtualDispatchDoesNotInlineTheBaseImplementation() {
        var result = Analyze("""
            class B { public virtual void Work() { } }
            sealed class D : B { static int state; public override void Work() { state++; } }
            class C { static void M(B value) => value.Work(); }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Unknown));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.DispatchUncertainty), Is.True);
        });
    }
    [Test]
    public void ReassignedFreshLocalLosesFreshOwnership() {
        var result = Analyze("""
            class Box { public int Value; }
            class C { static void M(Box input) { var box = new Box(); box = input; box.Value++; } }
            """, 2);
        Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
    }
    [Test]
    public void DeconstructionAssignmentWritesArgumentState() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    (input.Value, _) = (1, 2);
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void CoalesceAssignmentWritesArgumentState() {
        var result = Analyze("""
            #nullable enable
            sealed class Box { public object? Value; }
            class C {
                static void M(Box input) {
                    input.Value ??= new object();
                }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void PointerIndirectionAssignmentWritesArgumentState() {
        var result = Analyze("""
            unsafe class C {
                static void M(int* pointer) {
                    *pointer = 1;
                }
            }
            """, 2);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void InlineArrayAssignmentWritesArgumentState() {
        var result = Analyze("""
            using System.Runtime.CompilerServices;
            [InlineArray(4)] struct Buffer { private int element; }
            class C {
                static void M(ref Buffer buffer) {
                    buffer[0] = 1;
                }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void ImplicitIndexerAssignmentWritesArgumentState() {
        var result = Analyze("""
            sealed class Bag {
                public int Length => 3;
                public int this[int index] { get => index; set { } }
            }
            class C {
                static void M(Bag input) {
                    input[^1] = 1;
                }
            }
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void RefLocalAssignmentWritesAliasedArgumentState() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    ref int alias = ref input.Value;
                    alias = 1;
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void RefReturnInvocationAssignmentWritesArgumentState() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static ref int GetValue(Box box) => ref box.Value;
                static void M(Box input) {
                    GetValue(input) = 1;
                }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void RefLocalFromRefReturnWritesArgumentState() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static ref int GetValue(Box box) => ref box.Value;
                static void M(Box input) {
                    ref int alias = ref GetValue(input);
                    alias = 1;
                }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void RefReturnPropertyAssignmentTracksExposedStaticState() {
        var result = Analyze("""
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
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void ConditionalRefAssignmentWritesArgumentState() {
        var result = Analyze("""
            sealed class Box { public int Left; public int Right; }
            class C {
                static void M(Box input, bool chooseLeft) {
                    (chooseLeft ? ref input.Left : ref input.Right) = 1;
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void RefReassignmentDoesNotWriteThroughPreviousAlias() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    var fresh = new Box();
                    ref int alias = ref input.Value;
                    alias = ref fresh.Value;
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.False);
        });
    }
    [Test]
    public void FixedPointerWriteToFreshObjectRemainsFreshOwned() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            unsafe class C {
                static void M() {
                    var fresh = new Box();
                    fixed (int* pointer = &fresh.Value) {
                        *pointer = 1;
                    }
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
        });
    }
    [Test]
    public void NestedFreshObjectGraphWritesRemainFreshOwned() {
        var result = Analyze("""
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
            """, 5);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.False);
        });
    }
    [Test]
    public void FreshWrapperDoesNotTakeOwnershipOfNestedArgument() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            sealed class Outer { public Box Value { get; init; } }
            class C {
                static void M(Box input) {
                    var outer = new Outer { Value = input };
                    outer.Value.Value = 1;
                }
            }
            """, 5);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
        });
    }
    [Test]
    public void InstanceMutationOnFreshLocalRemainsFreshOwned() {
        var result = Analyze("""
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
            """, 7);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesReceiverState), Is.False);
        });
    }
    [Test]
    public void InstanceMutationOnArgumentIsRemappedToArgumentState() {
        var result = Analyze("""
            sealed class Box {
                public int Value;
                public void SetValue() { Value = 1; }
            }
            class C { static void M(Box box) { box.SetValue(); } }
            """, 5);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesReceiverState), Is.False);
        });
    }
    [Test]
    public void ExplicitConstructorReceiverWritesRemainFreshOwned() {
        var result = Analyze("""
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
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesReceiverState), Is.False);
        });
    }
    [Test]
    public void StaticHelperMutationOfFreshArgumentRemainsFreshOwned() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static void SetValue(Box box) { box.Value = 1; }
                static int M() {
                    var box = new Box();
                    SetValue(box);
                    return box.Value;
                }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.False);
        });
    }
    [Test]
    public void StaticHelperMutationOfCallerArgumentRemainsArgumentOwned() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static void SetValue(Box box) { box.Value = 1; }
                static void M(Box input) { SetValue(input); }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.False);
        });
    }
    [Test]
    public void RepeatedStaticHelperMutationPreservesFreshOwnership() {
        var result = Analyze("""
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
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesCapturedState), Is.False);
        });
    }
    [Test]
    public void PublishingFreshArgumentInvalidatesFreshOwnership() {
        var result = Analyze("""
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
            """, 5);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesCapturedState), Is.True);
        });
    }
    [Test]
    public void PropertySetterMutationOfFreshValueRemainsFreshOwned() {
        var result = Analyze("""
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
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.False);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.False);
        });
    }
    [Test]
    public void PropertySetterMutationOfCallerValueRemainsArgumentOwned() {
        var result = Analyze("""
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
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.False);
        });
    }
    [Test]
    public void DelegateMutationOfFreshReceiverRemainsFreshOwned() {
        var result = Analyze("""
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
            """, 7);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.False);
        });
    }
    [Test]
    public void DelegateMutationOfCallerReceiverRemainsArgumentOwned() {
        var result = Analyze("""
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
            """, 7);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.False);
        });
    }
    [Test]
    public void InlineDelegateMutationOfCallerReceiverRemainsArgumentOwned() {
        var result = Analyze("""
            sealed class Box {
                public int Value;
                public void SetValue() { Value = 1; }
            }
            class C {
                static void M(Box input) {
                    ((System.Action)input.SetValue)();
                }
            }
            """, 7);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True,
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
        });
    }
    [Test]
    public void DelegateRetainsFreshReceiverAfterLocalReassignment() {
        var result = Analyze("""
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
            """, 7);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesCapturedState), Is.False);
        });
    }
    [Test]
    public void DelegateRetainsCallerReceiverAfterLocalReassignment() {
        var result = Analyze("""
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
            """, 7);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.False);
        });
    }
    [Test]
    public void CopiedDelegateRetainsImpureTarget() {
        var result = Analyze("""
            class C {
                static int state;
                static void Impure() { state++; }
                static void M() {
                    System.Action first = Impure;
                    System.Action second = first;
                    second();
                }
            }
            """, 4);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void CopiedDelegateTargetIsIndependentOfSourceReassignment() {
        var result = Analyze("""
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
            """, 5);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
        });
    }
    [Test]
    public void LambdaMutationOfFreshCaptureRemainsFreshOwned() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static int M() {
                    var box = new Box();
                    System.Action action = () => box.Value = 1;
                    action();
                    return box.Value;
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesCapturedState), Is.False);
        });
    }
    [Test]
    public void LambdaMutationOfCallerCaptureRemainsArgumentOwned() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    System.Action action = () => input.Value = 1;
                    action();
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.False);
        });
    }
    [Test]
    public void LocalFunctionMutationOfFreshCaptureRemainsFreshOwned() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static int M() {
                    var box = new Box();
                    void SetValue() { box.Value = 1; }
                    SetValue();
                    return box.Value;
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesCapturedState), Is.False);
        });
    }
    [Test]
    public void LocalFunctionMutationOfCallerCaptureRemainsArgumentOwned() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    void SetValue() { input.Value = 1; }
                    SetValue();
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesArgumentState), Is.True);
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.False);
        });
    }
    [Test]
    public void LocalFunctionUsesCaptureOwnershipAtInvocation() {
        var result = Analyze("""
            sealed class Box { public int Value; }
            class C {
                static void M(Box input) {
                    var box = new Box();
                    void SetValue() { box.Value = 1; }
                    box = input;
                    SetValue();
                }
            }
            """, 3);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.WritesFreshOwnedState), Is.False);
        });
    }
    [Test]
    public void UncheckedExpressionBodiedLocalFunctionHasAnalyzableBody() {
        var result = Analyze("""
            class C {
                static int M(int x) {
                    int Local() => unchecked((x << 1) ^ 17);
                    return Local();
                }
            }
            """, 2);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.MethodEffects.Sites.Select(static site =>
                    site.Symbol + ":" + site.Effect + ":" + site.Reason)));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.False);
        });
    }
    [Test]
    public void CheckedExpressionBodiedLocalFunctionHasAnalyzableBody() {
        var result = Analyze("""
            class C {
                static int M(int x) {
                    int Local() => checked(x + 1);
                    return Local();
                }
            }
            """, 2);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.Unknown), Is.False);
        });
    }
    [Test]
    public void ReassignedDelegateUsesTheCurrentTarget() {
        var result = Analyze("""
            class C {
                static int state;
                static void Pure() { }
                static void Impure() { state++; }
                static void M() { System.Action action = Pure; action = Impure; action(); }
            }
            """, 5);
        Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
    }
    [Test]
    public void CombinedDelegateIncludesAddedTargetEffects() {
        var result = Analyze("""
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
            """, 6);
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
    public void CompoundPropertyUpdateIncludesGetterAndSetterBehavior() {
        var result = Analyze("""
            class C {
                static int state;
                static int P { get { state++; return state; } set { } }
                static void M() { P += 1; }
            }
            """, 4);
        Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
    }
    [Test]
    public void NativeBoundaryCannotProveDoesNotThrow() {
        var result = Analyze("""
            using System.Runtime.InteropServices;
            class C {
                [DllImport("missing")] static extern int Native();
                static int M() => Native();
            }
            """, 4);
        Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Unknown));
    }
    [Test]
    public void CompleteNativeContractCanCloseTheExceptionBoundary() {
        var result = Analyze("""
            using System.Runtime.InteropServices;
            using SharpProof.Attributes;
            class C {
                [DllImport("missing"), EffectContract(SharpProofEffect.None, Complete = true)]
                static extern int Native();
                static int M() => Native();
            }
            """, 6);
        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Proven));
            Assert.That(result.MethodEffects.Effects.HasFlag(SharpProofEffect.UsesNativeCode), Is.True);
        });
    }
    [Test]
    public void AmbientWritesDisprovePurity() {
        var result = Analyze("""
            using SharpProof.Attributes;
            class C {
                [EffectContract(SharpProofEffect.WritesAmbientState, Complete = true)]
                static extern void M();
            }
            """, 4);
        Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
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
