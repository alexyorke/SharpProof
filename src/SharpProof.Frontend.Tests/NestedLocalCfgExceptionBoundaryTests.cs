using System.Numerics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NestedLocalCfgExceptionBoundaryTests
{
    // SP-COV-NESTED-CFG-EH-001: lexical declaration regions belong to the caller.
    [TestCase(false, 2)]
    [TestCase(false, 0)]
    [TestCase(true, 2)]
    [TestCase(true, 0)]
    public void LocalFinallyExecutionStopsAtItsOwnGraphBoundary(bool enclosingFinally, int input)
    {
        const string local = "static int Local(int n) { try { return 10 / n; } finally { n = unchecked(n + 1); } }";
        var members = enclosingFinally
            ? "int Target(int value) { try { " + local + " return Local(value); } finally { value = unchecked(value + 1); } }"
            : "int Target(int value) { " + local + " return Local(value); }";
        using var subject = TypedProgramSubject.Create(members);
        var actual = subject.Invoke([input]);
        if (input == 0)
        { Assert.That(actual, Is.TypeOf<DivideByZeroException>()); }
        else
        { Assert.That(actual, Is.EqualTo(5)); }

        var method = subject.Graph.LocalFunctions.Single();
        var graph = subject.Graph.GetLocalFunctionControlFlowGraph(method);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var externalFinally = false;
        for (var region = graph.Root.EnclosingRegion; region != null; region = region.EnclosingRegion)
        { externalFinally |= region.Kind == ControlFlowRegionKind.TryAndFinally; }
        Assert.That(externalFinally, Is.EqualTo(enclosingFinally));

        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var parameter = context.Parameters.Single();
        var execution = new IrProgramInterpreter(factory).Execute(lowered.Program,
            new Dictionary<IrVarId, IrValue>
            {
                [parameter.Entry] = factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, input)
            });
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.Values[parameter.Current].IntegerNumericValue, Is.EqualTo(new BigInteger(input + 1)));
        if (input == 0)
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        }
        else
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(5)));
        }
    }
}
