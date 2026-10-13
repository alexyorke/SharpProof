using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrOpaqueRewriteIdentityTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void UnrelatedSubstitutionPreservesOpaqueIdentityWhileChangedArgumentsRebuild(bool impure)
    {
        var factory = new IrFactory();
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "Read", factory.IntegerType, true, factory.IntegerType);
        var argument = factory.CreateVariable("argument", factory.IntegerType);
        var unrelated = factory.CreateVariable("unrelated", factory.IntegerType);
        var operation = factory.CreateOperation();
        var variable = factory.Variable(argument);
        var opaque = impure
            ? factory.ImpureOpaque(operation, member, null, variable)
            : factory.PureOpaque(member, null, variable);
        var unchanged = IrSubstitution.Substitute(factory, opaque, unrelated, factory.Integer(9));
        Assert.That(unchanged, Is.SameAs(opaque));

        var replacement = factory.Integer(2);
        var changed = IrSubstitution.Substitute(factory, opaque, argument, replacement);
        var expected = impure
            ? factory.ImpureOpaque(operation, member, null, replacement)
            : factory.PureOpaque(member, null, replacement);
        Assert.That(changed, Is.SameAs(expected));
        Assert.That(changed, Is.Not.SameAs(opaque));
        Assert.That(((IrOpaqueTerm)changed).Arguments, Is.EqualTo(new[] { replacement }));
        Assert.That(((IrOpaqueTerm)changed).Operation, Is.EqualTo(opaque.Operation));
        Assert.That(((IrOpaqueTerm)changed).Purity, Is.EqualTo(opaque.Purity));
    }
}
