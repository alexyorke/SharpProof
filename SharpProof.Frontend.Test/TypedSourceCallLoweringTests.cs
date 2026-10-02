using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedSourceCallLoweringTests
{
    [TestCase("int Target(int x) => Helper(x); static int Helper(int x) => x + 1;", 1)]
    [TestCase("int Target(int x) => Target(x);", 1)]
    [TestCase("int Target(int x) => Helper(x); static int Helper(int x) => Target(x);", 1)]
    [TestCase("int Target(int x) => Helper(Helper(x)); static int Helper(int x) => x + 1;", 2)]
    [TestCase("int Target(int x) { Helper(x); return x; } static void Helper(int x) {}", 1)]
    [TestCase("object Target(int x) => Helper(x); static object Helper(int x) => new object();", 1)]
    [TestCase("int Target(int x) => Helper(); static int Helper(int x = 7) => x;", 1)]
    [TestCase("int Target(int x) => Helper(1, 2); static int Helper(params int[] x) => x.Length;", 1)]
    [TestCase("int Target(int x) { try { return Helper(x); } finally { x++; } } static int Helper(int x) => x;", 1)]
    [TestCase("int Target(int x) { try { return Helper(x); } catch (System.DivideByZeroException) { return 7; } } " +
        "static int Helper(int x) => 10 / x;", 1)]
    public void ShadowSourceCallsRemainFiniteAndCannotClaimExecutableExactness(string members, int count)
    {
        using var subject = TypedProgramSubject.Create(members);
        var lowered = subject.LowerShadowSourceCalls();
        Assert.That(lowered.Classification.IsExact, Is.True);
        Assert.That(lowered.IsShadowCallSkeleton, Is.True);
        Assert.That(lowered.IsExact, Is.False);
        Assert.That(lowered.PreservedSourceCalls, Has.Count.EqualTo(count));
        Assert.That(lowered.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrCallInstruction>().Count(),
            Is.EqualTo(count));
        Assert.That(subject.Execute(lowered, [3]).Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
    }

    [TestCase("int Target(int x) => System.Math.Abs(x);")]
    [TestCase("int Target(int x) => Helper(ref x); static int Helper(ref int x) => x;")]
    [TestCase("int Target(int x) => Helper(x); static T Helper<T>(T x) => x;")]
    [TestCase("int Target(int x) => Helper(); static int Helper(params int[] x) => x.Length;")]
    public void UnsupportedShadowSourceCallsRemainExplicitlyIncomplete(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        var lowered = subject.LowerShadowSourceCalls();
        Assert.That(lowered.Classification.IsExact, Is.False);
        Assert.That(lowered.IsExact, Is.False);
        Assert.That(lowered.PreservedSourceCalls, Is.Empty);
    }

    [TestCase("Pack(first: x, second: x++)", 3, 3)]
    [TestCase("Pack(second: x++, first: x)", 4, 3)]
    public void ShadowSourceCallArgumentsCaptureReadsInSourceOrder(string invocation, int first, int second)
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => " + invocation +
            "; static int Pack(int first, int second) => first * 10 + second;");
        var lowered = subject.LowerShadowSourceCalls();
        var execution = subject.Execute(lowered, [3]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        var call = lowered.PreservedSourceCalls.Keys.Single();
        var interpreter = new IrInterpreter(subject.Factory);
        Assert.That(interpreter.Evaluate(call.Arguments[0], execution.Values).Value!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(first)));
        Assert.That(interpreter.Evaluate(call.Arguments[1], execution.Values).Value!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(second)));
    }

    [Test]
    public void ShadowSourceCallArgumentFaultPreventsReachingTheCall()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) => Helper(10 / x); static int Helper(int x) => x;");
        var lowered = subject.LowerShadowSourceCalls();
        Assert.That(lowered.PreservedSourceCalls, Has.Count.EqualTo(1));
        var execution = subject.Execute(lowered, [0]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
    }

    [Test]
    public void ConstantCollectionArrayMatchesCompiledResult()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { int[] values = [1, -2]; return values[0] + values[1]; }");
        Assert.That(subject.Invoke([3]), Is.EqualTo(-1));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        Assert.That(subject.Execute(lowering, [3]).ReturnValue!.Integer, Is.EqualTo(-1));
    }

    [TestCase("int Target(int x) { return Pack(second: x++, first: x++); } static int Pack(int first, int second) { return first * 10 + second; }", 3, 43)]
    [TestCase("int Target(int x) { return Pack(first: x, second: x++); } static int Pack(int first, int second) { return first * 10 + second; }", 3, 33)]
    [TestCase("int Target(int x) { return Pack(first: x, second: (x = 5)); } static int Pack(int first, int second) { return first * 10 + second; }", 3, 35)]
    [TestCase("int Target(int x) { return Multiply(x); } static int Multiply(int value, int factor = 2) { return value * factor; }", 3, 6)]
    [TestCase("int Target(int x) { return Count(1, 2); } static int Count(params int[] values) => values.Length;", 3, 2)]
    [TestCase("int Target(int x) { return First(1, 2); } static int First(params int[] values) => values[0];", 3, 1)]
    [TestCase("int Target(int x) { return Count(new int[0]); } static int Count(params int[] values) => values.Length;", 3, 0)]
    [TestCase("int Target(int x) { try { return Count((int[])null); } catch (System.NullReferenceException) { return 7; } } static int Count(params int[] values) => values.Length;", 3, 7)]
    [TestCase("int Target(int x) { return Pack(values: new int[] { 1, 2 }, first: x++); } static int Pack(int first, params int[] values) => first * 10 + values[0] + values[1];", 3, 33)]
    [TestCase("int Target(int x) { return Mutate(x) + x; } static int Mutate(int value) { value += 4; return value; }", 3, 10)]
    [TestCase("int Target(int x) { return Inner(Outer(x)); } static int Outer(int value) { return value + 2; } static int Inner(int value) { return value * 3; }", 3, 15)]
    [TestCase("int Target(int x) { try { return Divide(x); } catch (System.DivideByZeroException) { return 7; } finally { x++; } } static int Divide(int value) { try { return 10 / value; } finally { value = 99; } }", 0, 7)]
    [TestCase("int Target(int x) { return Captured(x); } static int Captured(int value) { try { return value; } finally { value += 100; } }", 3, 3)]
    [TestCase("int Target(int x) { try { Check(x); return x; } catch (System.NullReferenceException) { return 7; } } static void Check(int value) { if (value == 0) throw null!; value++; }", 0, 7)]
    [TestCase("int Target(int x) { try { Check(x); return x; } catch (System.NullReferenceException) { return 7; } } static void Check(int value) { if (value == 0) throw null!; value++; }", 1, 1)]
    [TestCase("int Target(int x) { for (int i = 0; i < 2; i++) x = Increment(x); return x; } static int Increment(int value) { return value + 1; }", 3, 5)]
    [TestCase("int Target(int x) { try { return Outer(x); } catch (System.DivideByZeroException) when (++x > 0) { return x; } } static int Outer(int value) { return Inner(value); } static int Inner(int value) { return 10 / value; }", 0, 1)]
    [TestCase("int Target(int x) { try { return Inner(x); } catch (System.DivideByZeroException) when (10 / x > 0) { return 7; } catch (System.DivideByZeroException) { return 8; } } static int Inner(int value) { return 10 / value; }", 0, 8)]
    public void SourceCallsHaveIndependentCompiledResults(string members, int input, int expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([input]), Is.EqualTo(expected));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        var execution = subject.Execute(lowering, [input]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    [TestCase("return Count();", 0)]
    [TestCase("return Capture() == Capture() ? 1 : 0;", 1)]
    [TestCase("return Capture() == new int[0] ? 1 : 0;", 0)]
    public void EmptyParamsPreserveCompiledCacheIdentity(string body, int expected)
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { " + body +
            " } static int Count(params int[] values) => values.Length; static int[] Capture(params int[] values) => values;");
        Assert.That(subject.Invoke([3]), Is.EqualTo(expected));
        Assert.That(subject.LowerSourceCalls().IsExact, Is.False, "Empty params need an owned Array.Empty model.");
        var lowering = subject.LowerSourceCalls(arrayModels: true);
        Assert.That(lowering.IsExact, Is.True, lowering.Classification.Abstention.ToString());
        var execution = subject.Execute(lowering, [3]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Integer, Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CrossFrameFilterSearchPrecedesFinallyUnwind(bool transitive)
    {
        var forwarding = transitive ? "static int Forward(int value) { return Callee(value); }" : "";
        var call = transitive ? "Forward(x)" : "Callee(x)";
        using var subject = TypedProgramSubject.Create("int Target(int x) { try { return " + call +
            "; } catch (System.DivideByZeroException) when (++x > 0) { return 7; } catch (System.OverflowException) { return x; } } " +
            forwarding + " static int Callee(int value) { try { return 10 / value; } finally { value = checked((byte)(value + 256)); } }");
        Assert.That(subject.Invoke([0]), Is.EqualTo(1));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [0]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(1)));
    }

    [Test]
    public void AsyncVoidRootRemainsClosedInStandaloneLowering()
    {
        using var subject = TypedProgramSubject.Create("async void Target(int x) { throw null!; }");
        Assert.That(subject.Lower().IsExact, Is.False);
        Assert.That(subject.LowerSourceCalls().IsExact, Is.False);
    }

    [TestCase("byte Target(byte x) { return Add(x); } static byte Add(byte value, byte step = 1) { return checked((byte)(value + step)); }", (byte)254, (byte)255)]
    [TestCase("sbyte Target(sbyte x) { return Add(x); } static sbyte Add(sbyte value) { return checked((sbyte)(value + 1)); }", (sbyte)-128, (sbyte)-127)]
    [TestCase("short Target(short x) { return Add(x); } static short Add(short value) { return checked((short)(value + 1)); }", (short)-32768, (short)-32767)]
    [TestCase("ushort Target(ushort x) { return Add(x); } static ushort Add(ushort value) { return checked((ushort)(value + 1)); }", (ushort)65534, (ushort)65535)]
    [TestCase("long Target(long x) { return Add(x); } static long Add(long value) { return unchecked(value + 1); }", long.MaxValue, long.MinValue)]
    [TestCase("uint Target(uint x) { return Add(x); } static uint Add(uint value) { return unchecked(value + 1); }", uint.MaxValue, 0U)]
    [TestCase("ulong Target(ulong x) { return Add(x); } static ulong Add(ulong value, ulong step = 1) { return unchecked(value + step); }", ulong.MaxValue, 0UL)]
    [TestCase("bool Target(bool x) { return Flip(x); } static bool Flip(bool value) { return !value; }", true, false)]
    public void ScalarFramesPreserveWidthsAndDefaultConstants(string members, object input, object expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([input]), Is.EqualTo(expected));
        var lowering = subject.LowerSourceCalls();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [input]);
        var value = subject.Value(subject.Context.Result!.Value, expected);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Kind == IrValueKind.Boolean ? (object)execution.ReturnValue.Boolean : execution.ReturnValue.IntegerNumericValue,
            Is.EqualTo(value.Kind == IrValueKind.Boolean ? (object)value.Boolean : value.IntegerNumericValue));
    }

    [Test]
    public void NestedEscapingThrowRetainsOriginalCalleeSiteThroughCallerFinally()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { try { return Outer(x); } finally { x++; } } " +
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

    [TestCase("int Target(int x) { return Recurse(x); } static int Recurse(int value) { return Recurse(value); }")]
    [TestCase("int Target(int x) { return A(x); } static int A(int value) { return B(value); } static int B(int value) { return A(value); }")]
    [TestCase("int Target(int x) { return External.Add(x); }", "static class External { static External() { } public static int Add(int x) => x + 1; }")]
    [TestCase("int Target(int x) { return External.Add(x); }", "static class External { static int state = System.Environment.TickCount; public static int Add(int x) => x + 1; }")]
    [TestCase("int Target(int x) { return Generic<int>(x); } static int Generic<T>(int value) => value;")]
    [TestCase("int Target(int x) { return ByReference(ref x); } static int ByReference(ref int value) => value;")]
    [TestCase("int Target(int x) { return Params(x); } static int Params(params int[] value) => value[0];")]
    [TestCase("int Target(int x) { return System.Math.Abs(x); }")]
    public void UnsupportedSourceCallShapesClose(string members, string additionalSource = "")
    {
        using var subject = TypedProgramSubject.Create(members, additionalSource);
        Assert.That(subject.LowerSourceCalls().IsExact, Is.False);
    }

    [Test]
    public void NestedFramesShareOneConstructionLimitAndCancellation()
    {
        var steps = string.Concat(Enumerable.Repeat("value++; ", 240));
        for (var frame = 0; frame < 2; frame++)
        {
            using var individual = TypedProgramSubject.Create("int Target(int x) { return Single(x); } static int Single(int value) { " + steps + "return value; }");
            Assert.That(individual.LowerSourceCalls().IsExact, Is.True, "Each individual frame fits the construction cap.");
        }
        var members = new System.Text.StringBuilder("int Target(int x) { return A(x); } static int A(int value) { ");
        for (var index = 0; index < 240; index++)
        { members.Append("value++; "); }
        members.Append("return B(value); } static int B(int value) { ");
        for (var index = 0; index < 240; index++)
        { members.Append("value++; "); }
        members.Append("return value; }");
        using var subject = TypedProgramSubject.Create(members.ToString());
        Assert.That(subject.Invoke([0]), Is.EqualTo(480));
        Assert.That(subject.LowerSourceCalls().IsExact, Is.False, "Expansion work is shared, not reset for each frame.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => subject.LowerSourceCalls(canceled.Token)));
    }

    [TestCase(0, 1)]
    [TestCase(1, 3)]
    public void OrdinaryConditionalVoidReturnEvaluatesItsGuardBeforeLeaving(int input, int current)
    {
        using var subject = TypedProgramSubject.Create("void Target(int x) { if (x++ == 0) return; x++; }");
        Assert.That(subject.Invoke([input]), Is.Null);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [input]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(current)));
    }
}
