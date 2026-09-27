using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrBlockOrderTests
{
    [Test]
    public void NestedLoopsAreCutAtTheirHeaders()
    {
        // entry -> outer; outer -> inner | exit; inner -> body | latch;
        // body -> inner; latch -> outer
        var graph = new Graph();
        var entry = graph.Block();
        var outer = graph.Block();
        var inner = graph.Block();
        var body = graph.Block();
        var latch = graph.Block();
        var exit = graph.Block();
        graph.Goto(entry, outer);
        graph.Branch(outer, inner, exit);
        graph.Branch(inner, body, latch);
        graph.Goto(body, inner);
        graph.Goto(latch, outer);
        graph.Return(exit);

        var cut = graph.Cut(out var failure);

        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.None));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(cut!.BackEdges, Is.EquivalentTo(new[] { (body, inner), (latch, outer) }));
            Assert.That(cut.Loops[inner], Is.EquivalentTo(new[] { inner, body }));
            Assert.That(cut.Loops[outer], Is.EquivalentTo(new[] { outer, inner, body, latch }));
            Assert.That(cut.Order.IndexOf(outer), Is.LessThan(cut.Order.IndexOf(inner)));
            Assert.That(cut.Order.IndexOf(inner), Is.LessThan(cut.Order.IndexOf(latch)));
            Assert.That(
                IrBlockOrder.TryCreateAcyclicOrder(graph.Program, static _ => true, out var acyclic).IsDefault,
                Is.True);
            Assert.That(acyclic, Is.EqualTo(IrAcyclicOrderFailure.CyclicControlFlow));
        }
    }

    [Test]
    public void SelfLoopIsItsOwnNaturalLoop()
    {
        var graph = new Graph();
        var entry = graph.Block();
        var spin = graph.Block();
        var exit = graph.Block();
        graph.Goto(entry, spin);
        graph.Branch(spin, spin, exit);
        graph.Return(exit);

        var cut = graph.Cut(out _);

        Assert.That(cut!.Loops[spin], Is.EqualTo(new[] { spin }));
    }

    [Test]
    public void IrreducibleControlFlowIsRejected()
    {
        // entry branches into both a and b, which jump to each other: the
        // cycle has two entries and no dominating header.
        var graph = new Graph();
        var entry = graph.Block();
        var a = graph.Block();
        var b = graph.Block();
        graph.Branch(entry, a, b);
        graph.Goto(a, b);
        graph.Goto(b, a);

        Assert.That(graph.Cut(out var failure), Is.Null);
        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.CyclicControlFlow));
    }

    private sealed class Graph
    {
        private readonly IrFactory _factory = new();
        private readonly IrProgramBuilder _builder;
        private readonly IrVarId _flag;
        private IrProgram? _program;

        internal Graph()
        {
            _builder = new IrProgramBuilder(_factory);
            _flag = _factory.CreateVariable("flag", _factory.BooleanType);
        }

        internal IrProgram Program => _program ??= _builder.Build();

        internal IrBlockId Block()
        {
            return _builder.CreateBlock();
        }

        internal void Goto(IrBlockId from, IrBlockId to)
        {
            _builder.Goto(from, _factory.CreateOperation("goto"), to);
        }

        internal void Branch(IrBlockId from, IrBlockId whenTrue, IrBlockId whenFalse)
        {
            _builder.Branch(
                from, _factory.CreateOperation("branch"), _factory.Variable(_flag), whenTrue, whenFalse);
        }

        internal void Return(IrBlockId block)
        {
            _builder.Return(block, _factory.CreateOperation("return"));
        }

        internal IrLoopCut? Cut(out IrAcyclicOrderFailure failure)
        {
            return IrBlockOrder.TryCutLoops(Program, static _ => true, out failure);
        }
    }
}
