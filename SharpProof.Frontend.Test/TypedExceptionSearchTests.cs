using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class TypedExceptionSearchTests
{
    public static IEnumerable<TestCaseData> Cases()
    {
        yield return Case("search-before-unwind", "int Target(int x) { try { try { return 10 / x; } finally { x = x * 10 + 2; } } catch (System.DivideByZeroException) when ((x = x * 10 + 1) > 0) { return x * 10 + 3; } }", 0, 123);
        yield return Case("false-filter-side-effect", "int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) when ((x = 7) == 8) { return 1; } catch (System.DivideByZeroException) when (x == 7) { return x; } }", 0, 7);
        yield return Case("filter-fault-side-effect", "int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) when ((x = 7) / (x - 7) > 0) { return 1; } catch (System.DivideByZeroException) when (x == 7) { return x; } }", 0, 7);
        yield return Case("finally-cancels-selected-handler", "int Target(int x) { try { try { try { return 10 / x; } finally { x = checked((byte)(x + 256)); } } catch (System.DivideByZeroException) when ((x = 7) == 7) { return 1; } } catch (System.OverflowException) { return x; } }", 0, 7);
        yield return Case("nested-captured-return", "int Target(int x) { try { try { return x; } finally { x = x * 10 + 1; } } finally { x = x * 10 + 2; } }", 3, 3);
        yield return Case("nested-finally-normal", "int Target(int x) { try { try { x++; } finally { x = x * 10 + 1; } } finally { x = x * 10 + 2; } return x; }", 0, 112);
        yield return Case("sibling-finally", "int Target(int x) { try { x++; } finally { x = x * 10 + 1; } try { x++; } finally { x = x * 10 + 2; } return x; }", 0, 122);
        yield return Case("nested-finally-fault", "int Target(int x) { try { try { try { return 10 / x; } finally { x = 7; } } finally { x = x * 10 + 2; } } catch (System.DivideByZeroException) { return x; } }", 0, 72);
        yield return Case("filter-rethrow-original", "int Target(int x) { try { try { return 10 / x; } catch (System.DivideByZeroException) when ((x = 7) == 7) { try { x = checked((byte)(x + 256)); } catch (System.OverflowException) when (x == 7) { } throw; } finally { x = 9; } } catch (System.DivideByZeroException) { return x; } }", 0, 9);
        yield return Case("filter-fault-unhandled-original", "int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) when ((x = 7) / (x - 7) > 0) { return 1; } }", 0, typeof(DivideByZeroException));
        yield return Case("finally-replaces-unhandled-original", "int Target(int x) { try { try { return 10 / x; } finally { x = checked((byte)(x + 256)); } } finally { x = 7; } }", 0, typeof(OverflowException));
        yield return Case("handler-before-outer-finally", "int Target(int x) { try { try { try { x = 10 / x; } finally { x = x * 10 + 1; } } catch (System.DivideByZeroException) when (x == 0) { x = x * 10 + 2; } } finally { x = x * 10 + 3; } return x; }", 0, 123);
        yield return Case("lazy-multi-block-filter", "int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) when ((x = 7) == 7 && (x += 2) > 0) { return x; } }", 0, 9);
        yield return Case("conditional-multi-block-filter", "int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) when (x == 0 ? (x = 7) == 8 : (x = 8) == 8) { return 1; } catch (System.DivideByZeroException) when (x == 7) { return x; } }", 0, 7);
        yield return Case("finally-body-nested-finally-captured-return", "int Target(int x) { try { return x; } finally { try { x++; } finally { x += 2; } } }", 3, 3);
        yield return Case("finally-body-caught-nested-unwind-captured-return", "int Target(int x) { try { return x; } finally { try { try { x = 10 / (x - 3); } finally { x = 7; } } catch (System.DivideByZeroException) { x = 9; } } }", 3, 3);
        yield return Case("finally-body-filtered-nested-unwind-captured-return", "int Target(int x) { try { return x; } finally { try { try { x = 10 / (x - 3); } finally { x = 7; } } catch (System.DivideByZeroException) when (x == 3) { x = 9; } } }", 3, 3);
        yield return Case("finally-body-normal-non-return-entry", "int Target(int x) { try { x++; } finally { try { x++; } finally { x++; } } return x; }", 0, 3);
    }

    private static TestCaseData Case(string name, string members, int argument, object expected)
    {
        return new TestCaseData(name, members, argument, expected).SetName("Exception search oracle: " + name);
    }

    [TestCaseSource(nameof(Cases))]
    public void LoweredRegionMatchesCompiledOracle(string name, string members, int argument, object expected)
    {
        using var subject = TypedProgramSubject.Create(members);
        var actual = subject.Invoke([argument]);
        Assert.That(expected is Type ? actual?.GetType() : actual, Is.EqualTo(expected));
        Trace(subject.Graph, name);
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True, name + " " + lowered.Classification.Abstention);
        var execution = subject.Execute(lowered, [argument]);
        if (actual is Exception exception)
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(execution.Exception!.Kind, Is.EqualTo(exception is DivideByZeroException ? IrExceptionKind.DivideByZero : IrExceptionKind.Overflow));
        }
        else
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(subject.Value(subject.Context.Result!.Value, actual!).IntegerNumericValue));
        }
    }

    private static void Trace(ControlFlowGraph graph, string name)
    {
        TestContext.Out.WriteLine("CFG " + name);
        var pending = new Stack<(ControlFlowRegion Region, int Depth)>();
        pending.Push((graph.Root, 0));
        while (pending.Count != 0)
        {
            var (region, depth) = pending.Pop();
            TestContext.Out.WriteLine($"{new string(' ', depth)}{region.Kind} [{region.FirstBlockOrdinal},{region.LastBlockOrdinal}] type={region.ExceptionType}");
            foreach (var child in region.NestedRegions.Reverse())
            { pending.Push((child, depth + 1)); }
        }
        foreach (var block in graph.Blocks)
        {
            TestContext.Out.WriteLine($"B{block.Ordinal} {block.EnclosingRegion.Kind} condition={block.ConditionKind} value={block.BranchValue?.Syntax}");
            foreach (var operation in block.Operations)
            { TestContext.Out.WriteLine($"  {operation.Kind}: {operation.Syntax}"); }
            Branch("fall", block.FallThroughSuccessor);
            Branch("conditional", block.ConditionalSuccessor);
        }
    }

    private static void Branch(string name, ControlFlowBranch? branch)
    {
        if (branch != null)
        { TestContext.Out.WriteLine($"  {name} {branch.Semantics} -> {branch.Destination?.Ordinal} finally={string.Join(',', branch.FinallyRegions.Select(region => region.FirstBlockOrdinal))}"); }
    }

    [TestCase(0, "10 / x")]
    [TestCase(1, "20 / (x - 1)")]
    public void FilterFaultAndNestedHandledFaultKeepLexicalRethrowSite(int argument, string expectedSite)
    {
        using var subject = TypedProgramSubject.Create("""
            int Target(int x) {
                try {
                    try { if (x == 0) return 10 / x; return 20 / (x - 1); }
                    catch (System.DivideByZeroException) when ((x = 7) / (x - 7) > 0) { return 1; }
                    catch (System.DivideByZeroException) when (x == 7) {
                        try { x = checked((byte)(x + 256)); }
                        catch (System.OverflowException) when (x == 7) { }
                        throw;
                    }
                    finally { x = 9; }
                } finally { x = 11; }
            }
            """);
        Assert.That(subject.Invoke([argument]), Is.TypeOf<DivideByZeroException>());
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        var execution = subject.Execute(lowered, [argument]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        var span = subject.Factory.GetOperationInfo(execution.Instruction!.Operation).SourceSpan!;
        Assert.That(subject.Source.Substring(span.Start, span.Length), Is.EqualTo(expectedSite));
        Assert.That(execution.GetCurrentValue(subject.Context.Parameters[0].Current)!.IntegerNumericValue,
            Is.EqualTo(new System.Numerics.BigInteger(11)));
    }

    [TestCase("int Target(int x) { try { return 10 / x; } catch (System.Exception e) when (e != null) { return 1; } }")]
    [TestCase("int Target(int x) { try { return 10 / x; } catch (System.Exception) when (System.Math.Abs(x) > 0) { return 1; } }")]
    public void UnsupportedFilterObjectsAndCallsStayClosed(string members)
    {
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Lower().IsExact, Is.False);
    }

    [Test]
    public void MultipleRegionConstructionStopsAtExistingBoundAndCancellation()
    {
        var members = "int Target(int x) { " + string.Concat(Enumerable.Repeat("try { x++; } finally { x++; }", 300)) + "return x; }";
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Lower().IsExact, Is.False);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(new Action(() => new RoslynProgramLowerer(subject.Factory)
            .LowerCandidate(subject.Graph, subject.Context, cancellation.Token)));
    }

    [Test]
    public void ExplicitFilterThrowIsFalseAndRetainsEarlierEffects()
    {
        using var subject = TypedProgramSubject.Create("int Target(int x) { try { return 10 / x; } catch (System.DivideByZeroException) when ((x = 7) > 0 ? throw null! : true) { return 1; } catch (System.DivideByZeroException) { return x; } }");
        Assert.That(subject.Invoke([0]), Is.EqualTo(7));
        Trace(subject.Graph, "filter-throw-null");
        var lowered = subject.Lower();
        Assert.That(lowered.IsExact, Is.True);
        var execution = subject.Execute(lowered, [0]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(7)));
    }
}
