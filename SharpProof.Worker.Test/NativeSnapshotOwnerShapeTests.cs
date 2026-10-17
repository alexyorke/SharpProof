using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Algorithm acceptance boundaries for factory-owned IR. The declared field
// is an IR fixture member, with no compiler or C# member identity claim.
[TestFixture]
public sealed class NativeSnapshotOwnerShapeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void SnapshotSelectionRequiresAnOwnerWhoseEntryStateCanBeTracked(bool concatenated)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        PassiveParameterBinding Parameter(string name)
        {
            return new(
                factory.CreateVariable(name + "-entry", factory.StringType),
                factory.CreateVariable(name + "-current", factory.StringType),
                factory.CreateVariable(name + "-old", factory.StringType));
        }
        var first = Parameter("first");
        var second = Parameter("second");
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var site = factory.CreateOperation("snapshot-owner-shape");
        builder.Assign(block, site, first.Current, factory.Variable(first.Entry));
        builder.Assign(block, site, second.Current, factory.Variable(second.Entry));
        IrTerm owner = factory.Variable(first.Current);
        if (concatenated)
        { owner = factory.Binary(IrBinaryOperator.StringConcat, owner, factory.Variable(second.Current)); }
        var receiver = factory.Cast(factory.ObjectType, owner);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "field:Value", factory.IntegerType, false);
        var declaration = factory.GetMemberInfo(member);
        Assert.That(declaration.DeclaringType, Is.EqualTo(receiver.Type));
        Assert.That(declaration.ReturnType, Is.EqualTo(factory.IntegerType));
        Assert.That(declaration.IsStatic, Is.False);
        Assert.That(declaration.ParameterTypes, Is.Empty);
        var read = factory.PureOpaque(member, receiver);
        Assert.That(read.Member, Is.EqualTo(member));
        Assert.That(read.Receiver, Is.SameAs(receiver));
        Assert.That(read.Type, Is.EqualTo(factory.IntegerType));
        builder.Return(block, site, read);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var candidate = new PassiveCallableCandidate("snapshot-owner-shape", builder.Build(),
            [first, second], result, [], []);
        var built = PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(built, Is.EqualTo(!concatenated));
            Assert.That(plan, concatenated ? Is.Null : Is.Not.Null);
            Assert.That(failure, Is.EqualTo(concatenated ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
        }
    }
}
