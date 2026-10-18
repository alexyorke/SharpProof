using System.Numerics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NestedLocalCfgCallerCatchBoundaryTests
{
    // SP-COV-NESTED-CFG-EH-001: caller handlers execute only after a local call escapes.
    [TestCase(false, 2)]
    [TestCase(false, 0)]
    [TestCase(true, 2)]
    [TestCase(true, 0)]
    public void LocalExceptionEscapesBeforeTheCallingWrapperCatchesIt(bool enclosingCatch, int input)
    {
        const string local = "static int Local(int n) { try { return 10 / n; } finally { n = unchecked(n + 1); } }";
        var members = enclosingCatch
            ? "int Target(int value) { try { " + local + " return Local(value); } catch (System.DivideByZeroException) { return -1; } }"
            : "int Target(int value) { " + local + " return Local(value); }";
        using var subject = TypedProgramSubject.Create(members);
        var actual = subject.Invoke([input]);
        if (input == 0 && !enclosingCatch)
        { Assert.That(actual, Is.TypeOf<DivideByZeroException>()); }
        else
        { Assert.That(actual, Is.EqualTo(input == 0 ? -1 : 5)); }

        var method = subject.Graph.LocalFunctions.Single();
        var graph = subject.Graph.GetLocalFunctionControlFlowGraph(method);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        Assert.That(context.OwnsBody(graph.OriginalOperation), Is.True);
        Assert.That(graph.OriginalOperation.SemanticModel!.Compilation, Is.SameAs(subject.Compilation));
        var externalCatch = false;
        for (var region = graph.Root.EnclosingRegion; region != null; region = region.EnclosingRegion)
        { externalCatch |= region.Kind == ControlFlowRegionKind.TryAndCatch; }
        Assert.That(externalCatch, Is.EqualTo(enclosingCatch));

        // Execute the local CFG alone: its caller's catch is outside this program.
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph, context);
        Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
        Assert.That(lowered.ConstructionLimitExceeded, Is.False);
        var parameter = context.Parameters.Single();
        var execution = new IrProgramInterpreter(factory).Execute(lowered.Program,
            global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty.SetItem(parameter.Entry, factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, input)));
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
