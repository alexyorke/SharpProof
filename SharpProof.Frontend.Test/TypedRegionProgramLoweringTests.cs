using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedRegionProgramLoweringTests
{
    public static IEnumerable<TestCaseData> RegionCases()
    {
        yield return Case("captured-return", "int Target(int x) { try { return x; } finally { x = 7; } }", 3, 3);
        yield return Case("normal-finally", "int Target(int x) { try { x++; } finally { x += 2; } return x; }", 3, 6);
        yield return Case("exceptional-finally", "int Target(int x) { try { try { x = 10 / x; } finally { x = 7; } } catch (System.DivideByZeroException) { return x; } return x; }", 0, 7);
        yield return Case("finally-replaces-exception", "int Target(int x) { try { return 10 / x; } finally { x = checked((byte)(unchecked(256 + x))); } }", 0, typeof(OverflowException));
        yield return Case("ordered-catches", "int Target(int x) { try { return 10 / x; } catch (System.OverflowException) { return 1; } catch (System.ArithmeticException) { return 2; } catch (System.Exception) { return 3; } }", 0, 2);
        yield return Case("unmatched-catch", "int Target(int x) { try { return 10 / x; } catch (System.OverflowException) { return 2; } }", 0, typeof(DivideByZeroException));
        yield return Case("nested-rethrow", "int Target(int x) { try { try { return 10 / x; } catch (System.DivideByZeroException) { try { x = checked((byte)(unchecked(256 + x))); } catch (System.OverflowException) { } throw; } } catch (System.DivideByZeroException) { return 1; } catch (System.OverflowException) { return 2; } }", 0, 1);
        yield return Case("rethrow-finally", "int Target(int x) { try { try { return 10 / x; } catch (System.DivideByZeroException) { x = 3; throw; } finally { x = 7; } } catch (System.DivideByZeroException) { return x; } }", 0, 7);
        yield return Case("null-throw", "int Target(int x) { try { throw null!; } catch (System.NullReferenceException) { return 9; } }", 0, 9);
        yield return Case("shared-finally-return", "int Target(int x) { try { if (x < 0) return 1; x++; } finally { x = 7; } return x; }", -1, 1);
        yield return Case("shared-finally-normal", "int Target(int x) { try { if (x < 0) return 1; x++; } finally { x = 7; } return x; }", 3, 7);
        yield return Case("first-catch-type", "int Target(int x) { try { return x / -1; } catch (System.OverflowException) { return 1; } catch (System.ArithmeticException) { return 2; } }", int.MinValue, 1);
        yield return Case("finally-caught-fault-preserves-return", "int Target(int x) { try { return x; } finally { try { x = 10 / x; } catch (System.DivideByZeroException) { x = 7; } } }", 0, 0);
        yield return Case("catch-all", "int Target(int x) { try { return 10 / x; } catch { return 9; } }", 0, 9);
    }

    [TestCaseSource(nameof(RegionCases))]
    public void CandidateRegionMatchesCompiledOracle(string name, string members, int argument, object expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        var actual = subject.Invoke([argument]);
        Assert.That(expected is Type type ? actual?.GetType() : actual, Is.EqualTo(expected));
        if (name == "catch-all")
        {
            var regions = new Stack<Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowRegion>();
            regions.Push(subject.Graph.Root);
            while (regions.Count != 0)
            {
                var region = regions.Pop();
                if (region.Kind == Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowRegionKind.Catch)
                { Assert.That(region.ExceptionType!.SpecialType, Is.EqualTo(Microsoft.CodeAnalysis.SpecialType.System_Object)); }
                foreach (var child in region.NestedRegions)
                { regions.Push(child); }
            }
        }
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, name + " " + lowered.Classification.Abstention);
        var execution = subject.Execute(lowered, [argument]);
        if (actual is Exception exceptionResult)
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(execution.Exception!.Kind, Is.EqualTo(exceptionResult is DivideByZeroException ? IrExceptionKind.DivideByZero : IrExceptionKind.Overflow));
        }
        else
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(subject.Value(subject.Context.Result!.Value, actual!).IntegerNumericValue));
        }
    }

    private static TestCaseData Case(string name, string members, int argument, object expected)
    {
        return new TestCaseData(name, members, argument, expected).SetName("Region oracle: " + name);
    }

    [TestCase(0, "10 / x")]
    [TestCase(1, "20 / (x - 1)")]
    public void NestedHandledFaultCannotReplaceOriginalRethrowSite(int argument, string site)
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(int x) {
                try { if (x == 0) return 10 / x; return 20 / (x - 1); }
                catch (System.DivideByZeroException) {
                    try { x = checked((byte)(unchecked(x + 256))); }
                    catch (System.OverflowException) { }
                    throw;
                }
            }
            """);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(subject.Invoke([argument]), Is.TypeOf<DivideByZeroException>());
        var execution = subject.Execute(lowered, [argument]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
        Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo(site));
    }

    [Test]
    public void SameNameSourceExceptionCannotCatchRuntimeScalarFault()
    {
        using var subject = TypedProgramSubject.Create(
            "int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) { return 7; } }",
            "namespace System { public sealed class DivideByZeroException : Exception { } }");
        Assert.That(subject.Invoke([0]), Is.TypeOf<DivideByZeroException>());
        Assert.That(subject.Lower().IsExact, Is.False);
    }

    [TestCase(false, 2)]
    [TestCase(false, 0)]
    [TestCase(true, 2)]
    [TestCase(true, 0)]
    public void NormalOnlyLocalHasCompiledResultAndOriginalFaultAfterFinally(bool skipUnreachable, int divisor)
    {
        var jump = skipUnreachable ? "goto L; d = 2; L:" : "";
        using var subject = TypedProgramSubject.Create($$"""
            int Target(int d) { int y; {{jump}} try { y = 10 / d; } finally { d = 1; } return y; }
            """);
        var actual = subject.Invoke([divisor]);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        var execution = subject.Execute(lowered, [divisor]);
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(1)));
        if (divisor == 0)
        {
            Assert.That(actual, Is.TypeOf<DivideByZeroException>());
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
            Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo("10 / d"));
        }
        else
        {
            Assert.That(actual, Is.EqualTo(5));
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(5)));
        }
    }

    [TestCase("int Target(int x) { try { return x; } catch (System.Exception) when (x == 0) { return 0; } }")]
    [TestCase("int Target(int x) { try { try { return x; } finally { x++; } } finally { x++; } }")]
    [TestCase("int Target(int x) { try { x++; } finally { x++; } try { return x; } finally { x++; } }")]
    [TestCase("int Target(int x) { try { return 10 / x; } catch (System.Exception e) { return 0; } }")]
    [TestCase("int Target(int x) { try { throw new System.DivideByZeroException(); } catch (System.Exception) { return 0; } }")]
    [TestCase("int Target(int x) { try { return System.Math.Abs(x); } catch (System.Exception) { return 0; } }")]
    [TestCase("int Target(int x) { ref int r = ref x; try { r++; return x; } finally { x = 7; } }")]
    public void IncompleteRegionFormsStayClosed(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Lower().IsExact, Is.False);
    }

    [Test]
    public void CandidateConstructionIsBoundedAndHonorsCancellation()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { " + string.Concat(Enumerable.Repeat("x = 0;", 5000)) + "return x; }");
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.False);
        Assert.That(lowered.Program.Blocks.Sum(block => block.Instructions.Length), Is.EqualTo(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => new RoslynProgramLowerer(subject.Factory).LowerCandidate(subject.Graph, subject.Context, cancellation.Token)));
    }

    [Test]
    public void SynthesizedLockLocalMustAbstainInsteadOfAbortingCandidateConstruction()
    {
        using var subject = TypedProgramSubject.Create("void Target() { lock (new object()) { } }");
        var pending = new Stack<Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowRegion>();
        pending.Push(subject.Graph.Root);
        var unnamed = false;
        while (pending.Count != 0)
        {
            var region = pending.Pop();
            foreach (var local in region.Locals)
            {
                TestContext.Out.WriteLine($"lock CFG local: name='{local.Name}' implicit={local.IsImplicitlyDeclared} type={local.Type}");
                unnamed |= string.IsNullOrEmpty(local.Name);
            }
            foreach (var child in region.NestedRegions)
            { pending.Push(child); }
        }
        Assert.That(unnamed, Is.True);
        Assert.That(subject.Lower().IsExact, Is.False);
    }
}
