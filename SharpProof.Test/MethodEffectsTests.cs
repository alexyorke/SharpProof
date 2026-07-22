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
