using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreDomainCoverageTests
{
    [Test]
    public void UnreachableProgramStateIsAnIdentityForWidening()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var text = factory.CreateVariable("text", factory.StringType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var unreachable = builder.CreateBlock();
        var reachable = builder.CreateBlock();
        builder.Assign(entry, factory.CreateOperation(), flag, factory.Boolean(false));
        builder.Assign(entry, factory.CreateOperation(), text, factory.Null(factory.StringType));
        builder.Branch(entry, factory.CreateOperation(), factory.Variable(flag), unreachable, reachable);
        builder.Assign(unreachable, factory.CreateOperation(), value, factory.Integer(1));
        builder.Return(unreachable, factory.CreateOperation(), factory.Variable(value));
        builder.Assign(reachable, factory.CreateOperation(), value, factory.Integer(0));
        builder.Return(reachable, factory.CreateOperation(), factory.Variable(value));
        var program = builder.Build();
        var facts = new CoreIrAdvisoryInterpreter(program, [flag, value, text])
            .Run(ImmutableDictionary<IrVarId, IntervalValue>.Empty);
        Assert.That(facts.Accepted, Is.True);
        Assert.That(facts.Gaps, Is.Empty);
        var bottom = facts.RequireOutput(1);
        var normal = facts.RequireOutput(2);
        Assert.That(bottom.Reachable, Is.False);
        Assert.That(normal.Reachable, Is.True);
        var domain = new CoreIrAdvisoryDomain(factory, [flag, value, text]);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(domain.AreEquivalent(domain.Havoc(bottom), bottom), Is.True);
            Assert.That(domain.AreEquivalent(domain.Havoc(normal), domain.Top), Is.True);
            Assert.That(domain.AreEquivalent(domain.Widen(bottom, normal), normal), Is.True);
            Assert.That(domain.AreEquivalent(domain.Widen(normal, bottom), normal), Is.True);
            Assert.That(domain.AreEquivalent(domain.Join(bottom, normal), normal), Is.True);
            Assert.That(domain.Set(normal, value, IntervalDomain.Instance.Bottom).Reachable, Is.False);
            Assert.That(domain.SetReference(normal, text, NullnessDomain.Instance.Bottom,
                SequenceCardinalityDomain.Instance.Empty).Reachable, Is.False);
            Assert.That(domain.SetReference(normal, text, NullnessDomain.Instance.Top,
                SequenceCardinalityDomain.Instance.Bottom).Reachable, Is.False);
        }
        Assert.That(new IrProgramInterpreter(factory).Execute(program).ReturnValue!.Integer, Is.Zero);
    }
}
