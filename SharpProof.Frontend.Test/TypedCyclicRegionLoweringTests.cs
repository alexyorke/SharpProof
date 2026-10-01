using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedCyclicRegionLoweringTests
{
    [TestCase("int Target(int x) { try { try { return 10 / x; } finally { x = 10 / x; } } catch (System.Exception) when (x == 0) { return 1; } }", 0, 1)]
    [TestCase("int Target(int x) { int n = 0; try { try { return 10 / x; } finally { x = 10 / x; } } catch (System.DivideByZeroException) when (++n > 0) { return n; } }", 0, 2)]
    [TestCase("int Target(int x) { int n = 0; try { try { return 10 / x; } finally { x = checked((byte)(x + 256)); } } catch (System.Exception) when (++n > 0) { return n; } }", 0, 2)]
    [TestCase("int Target(int x) { int i = 0, y = 0; while (i < 2) { try { y = 10 / x; } catch (System.DivideByZeroException) { x = 1; } finally { i++; } } return y; }", 0, 10)]
    [TestCase("int Target(int x) { while (x < 3) { try { x++; } finally { x++; } } return x; }", 0, 4)]
    [TestCase("int Target(int x) { while (x < 3) { try { return x; } finally { x++; } } return x; }", 0, 0)]
    [TestCase("int Target(int x) { try { for (int i = 0; i < 2; i++) { try { x++; } finally { x++; } } } finally { x += 10; } return x; }", 0, 14)]
    [TestCase("int Target(int x) { while (x < 0) { try { x = checked(x + 1); } catch (System.OverflowException) { x = 1; } finally { x++; } } return x; }", 1, 1)]
    public void CyclicRegionMatchesCompiledOracle(string members, int argument, int expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([argument]), Is.EqualTo(expected));
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        var execution = subject.Execute(lowered, [argument]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    [Test]
    public void CompiledNestedRethrowThroughLoopKeepsOriginalKind()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { while (x == 0) { try { return 10 / x; } catch (System.DivideByZeroException) { try { x = checked((byte)(x + 256)); } catch (System.OverflowException) { } throw; } finally { x = 7; } } return x; }");
        Assert.That(subject.Invoke([0]), Is.TypeOf<DivideByZeroException>());
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [0]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(7)));
        var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
        Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo("10 / x"));
    }

    [TestCase(0)]
    [TestCase(1)]
    public void CyclicSharedFinallyPreservesCaptureBeforeOuterMutation(int input)
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { try { for (int i = 0; i < 2; i++) { try { x = checked(x + 1); } finally { x = checked(x + 1); } } return x; } finally { x = checked(x + 10); } }");
        Assert.That(subject.Invoke([input]), Is.EqualTo(input + 4));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [input]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(input + 4)));
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(input + 14)));
    }

    [Test]
    public void SecondIterationRethrowRetainsItsSiteAcrossReplacementAndFinally()
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(int x) { int i = 0;
                while (i < 2) {
                    try { if (i == 0) return 10 / x; return 20 / (x - 1); }
                    catch (System.DivideByZeroException) {
                        try { x = checked((byte)(x + 256)); } catch (System.OverflowException) { }
                        if (i++ == 1) throw;
                    } finally { x = 1; }
                }
                return 9;
            }
            """);
        Assert.That(subject.Invoke([0]), Is.TypeOf<DivideByZeroException>());
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [0]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
        Assert.That(execution.Values.Single(pair => subject.Factory.GetString(subject.Factory.GetVariableInfo(pair.Key).Name) == "i").Value.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(2)));
        Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo("20 / (x - 1)"));
    }

    [TestCase("rethrow", 0, 1)]
    [TestCase("rethrow", 1, 2)]
    [TestCase("finally", 0, 7)]
    [TestCase("finally", 1, 3)]
    public void ConditionalRegionTerminalEdgesEvaluateTheirGuardsExactlyOnce(string kind, int input, int current)
    {
        using var subject = TypedProgramSubject.Create(kind == "rethrow"
            ? "int Target(int x) { try { return 10 / (x - x); } catch (System.DivideByZeroException) { if (++x > 1) throw; return x; } }"
            : "int Target(int x) { try { x++; } finally { if (x++ == 1) x = 7; } return x; }");
        var actual = subject.Invoke([input]);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [input]);
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(current)));
        if (actual is DivideByZeroException)
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
            Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo("10 / (x - x)"));
        }
        else
        {
            Assert.That(actual, Is.EqualTo(current));
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(current)));
        }
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ConditionalVoidReturnLeavesThroughFinallyAfterItsGuard(int input)
    {
        using var subject = TypedProgramSubject.Create("void Target(int x) { try { if (x++ == 0) return; x = 1 / (x - x); } finally { x++; } }");
        var actual = subject.Invoke([input]);
        Assert.That(actual, input == 0 ? Is.Null : Is.TypeOf<DivideByZeroException>());
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var execution = subject.Execute(lowering, [input]);
        Assert.That(execution.Status, Is.EqualTo(input == 0 ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Exception));
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(input + 2)));
    }
}
