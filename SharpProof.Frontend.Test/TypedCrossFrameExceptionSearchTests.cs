using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedCrossFrameExceptionSearchTests
{
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (++x > 100) { return 7; } catch (System.DivideByZeroException) when (++x > 0) { return x; } catch (System.OverflowException) when (++x > 0) { return x; } } static int Callee(int value) { try { return 10 / value; } finally { value = checked((byte)(value + 256)); } }", 0, 3, 3)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when ((++x > 0) && Filter(x - 1)) { return 7; } catch (System.DivideByZeroException) { return x; } } static int Callee(int value) { try { return 10 / value; } finally { value++; } } static bool Filter(int value) { try { return value > 0; } finally { value = 10 / value; } }", 0, 1, 1)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when ((++x > 0) && Filter(x - 1)) { return 7; } catch (System.DivideByZeroException) { return x; } } static int Callee(int value) { return 10 / value; } static bool Filter(int value) { try { return More(value); } finally { value++; } } static bool More(int value) { try { return value > 0; } finally { value = checked((byte)(value + 256)); } }", 0, 1, 1)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (++x > 0) { return x; } finally { x += 10; } } static int Callee(int value) { try { return value; } finally { value += 100; } }", 3, 3, 13)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (++x > 0) { return x; } finally { x += 10; } } static int Callee(int value) { try { return 10 / value; } finally { value++; } }", 0, 1, 11)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (++x > 0) { return 7; } catch (System.NullReferenceException) { return x; } } static int Callee(int value) { try { try { return 10 / value; } catch (System.DivideByZeroException) { return 2; } } finally { throw null!; } }", 0, 0, 0)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (++x > 0) { return 7; } } static int Callee(int value) { try { return 10 / value; } catch (System.DivideByZeroException) when (Filter(value)) { return 5; } catch (System.DivideByZeroException) { return 2; } finally { value++; } } static bool Filter(int value) { try { return true; } finally { value = 10 / value; } }", 0, 2, 0)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (++x > 0) { return 7; } catch (System.OverflowException) { return x; } } static int Callee(int value) { try { return More(value); } finally { value = checked((byte)(value + 256)); } } static int More(int value) { try { return 10 / value; } finally { value++; } }", 0, 1, 1)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (++x > 0) { return x; } } static int Callee(int value) { try { return 10 / value; } catch (System.DivideByZeroException) when (++value > 0) { throw; } finally { value++; } }", 0, 1, 1)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (++x > 0) { return 7; } catch (System.OverflowException) { return x; } } static int Callee(int value) { try { return 10 / value; } catch (System.DivideByZeroException) { try { throw; } finally { value = checked((byte)(value + 256)); } } }", 0, 1, 1)]
    [TestCase("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (++x > 0) { return x; } } static int Callee(int value) { try { return 10 / value; } finally { value = 10 / value; } }", 0, 2, 2)]
    public void CompiledSearchUnwindAndCapturedStateMatchExpandedOriginal(string members, int input, int expected, int current)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([input]), Is.EqualTo(expected));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        var execution = subject.Execute(lowering, [input]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(current)));
    }

    [Test]
    public void NestedRethrowKeepsItsOriginalCalleeThrowSiteAcrossFrames()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { try { return Outer(x); } catch (System.DivideByZeroException) when (++x > 0) { try { int y = 1 / (x - 1); } catch (System.DivideByZeroException) { } throw; } } " +
            "static int Outer(int value) { try { return Inner(value); } finally { value++; } } static int Inner(int value) { return 20 / value; }");
        Assert.That(subject.Invoke([0]), Is.TypeOf<DivideByZeroException>());
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [0]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
        Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo("20 / value"));
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(1)));
    }

    [Test]
    public void SharedFramesCloseIncompleteFragmentsAndRetainConstructionAndCancellationBounds()
    {
        using var unsupported = TypedProgramSubject.Create("int Target(int x) { try { return Callee(x); } catch (System.DivideByZeroException) when (x > 0) { return x; } } static int Callee(int value) { value++; return System.Math.Abs(value); }");
        Assert.That(unsupported.LowerSourceCalls().IsExact, Is.False);
        var steps = string.Concat(Enumerable.Repeat("value++; ", 500));
        var root = "int Target(int x) { try { return A(x); } catch (System.DivideByZeroException) when (x > 0) { return x; } } ";
        using var individual = TypedProgramSubject.Create(root + "static int A(int value) { " + steps + "return value; }");
        Assert.That(individual.LowerSourceCalls().IsExact, Is.True);
        using var nested = TypedProgramSubject.Create(root + "static int A(int value) { " + steps + "return B(value); } static int B(int value) { " + steps + "return value; }");
        Assert.That(nested.Invoke([0]), Is.EqualTo(1000));
        Assert.That(nested.LowerSourceCalls().IsExact, Is.False);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => individual.LowerSourceCalls(canceled.Token)));
    }
}
