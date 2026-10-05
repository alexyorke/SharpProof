using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedLoopLoweringTests
{
    [TestCase("int Target(int x) { while (x < 3) x++; return x; }", 0, 3)]
    [TestCase("int Target(int x) { while (x < 3) x++; return x; }", 8, 8)]
    [TestCase("byte Target(byte x) { do { unchecked { x++; } } while (x != 0); return x; }", (byte)254, 0)]
    [TestCase("int Target(int x) { for (int i = 0; i < 3; i++) { if (i == 1) continue; x++; } return x; }", 7, 9)]
    [TestCase("int Target(int x) { while (x < 10) { x++; if (x == 3) break; } return x; }", 0, 3)]
    [TestCase("int Target(int x) { while (x < 10) { if (x == 2) return x; x++; } return x; }", 0, 2)]
    [TestCase("int Target(int x) { for (int i = 0; i < 2; i++) { for (int j = 0; j < 2; j++) x++; } return x; }", 1, 5)]
    [TestCase("int Target(int x) { do { x++; } while (x < 3); return x; }", 0, 3)]
    public void CompiledFiniteLoopBoundary(string members, object argument, int expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(Convert.ToInt32(subject.Invoke([argument]), System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(expected));
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var execution = subject.Execute(lowered, [argument]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    [TestCase("long Target(long x) { do { unchecked { x++; } } while (x > 0L); return x; }", long.MaxValue, long.MinValue)]
    public void Signed64LoopWrapMatchesCompiled(string members, long argument, long expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([argument]), Is.EqualTo(expected));
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(subject.Execute(lowered, [argument]).ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    [Test]
    public void FullUnsigned64LoopWrapMatchesCompiled()
    {
        using var subject = TypedProgramSubject.Create("ulong Target(ulong x) { do { unchecked { x++; } } while (x != 0UL); return x; }");
        Assert.That(subject.Invoke([ulong.MaxValue]), Is.EqualTo(0UL));
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        Assert.That(subject.Execute(lowered, [ulong.MaxValue]).ReturnValue!.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.Zero));
    }

    [TestCase(int.MaxValue)]
    [TestCase(int.MaxValue - 1)]
    public void CheckedFaultInsideLoopMatchesOriginalCSharpSite(int argument)
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { int i = 0; while (i < 2) { x = checked(x + 1); i++; } return x; }");
        Assert.That(subject.Invoke([argument]), Is.TypeOf<OverflowException>());
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        var execution = subject.Execute(lowered, [argument]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
        var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
        Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo("x + 1"));
    }

    [Test]
    public void OriginalCyclicInterpreterKeepsItsStepAndCancellationBounds()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { while (true) x++; }");
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        var inputs = subject.Context.Parameters.ToDictionary(parameter => parameter.Entry, parameter => subject.Value(parameter.Entry, 0));
        var execution = new IrProgramInterpreter(subject.Factory).Execute(lowered.Program, inputs, maximumSteps: 4096);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.StepLimit));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => new IrProgramInterpreter(subject.Factory).Execute(lowered.Program, inputs, cancellationToken: cancellation.Token)));
    }
}
