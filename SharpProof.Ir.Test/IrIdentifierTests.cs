using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrIdentifierTests
{
    [Test]
    public void DefaultIdentifiersPreserveKindSpecificFormatting()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(default(IrIdentityId).IsDefault, Is.True);
            Assert.That(default(IrIdentityId).ToString(), Is.EqualTo("identity0"));
            Assert.That(default(IrId).ToString(), Is.EqualTo("ir0"));
            Assert.That(default(IrVarId).ToString(), Is.EqualTo("v0"));
            Assert.That(default(IrTypeId).ToString(), Is.EqualTo("t0"));
            Assert.That(default(IrMemberId).ToString(), Is.EqualTo("m0"));
            Assert.That(default(IrStringId).ToString(), Is.EqualTo("s0"));
            Assert.That(default(OperationId).ToString(), Is.EqualTo("op0"));
            Assert.That(default(IrBlockId).ToString(), Is.EqualTo("b0"));
            Assert.That(default(IrInstructionId).ToString(), Is.EqualTo("i0"));
        }
    }

    [Test]
    public void FactoryIdentifiersExposeStableNondefaultFormatting()
    {
        var factory = new IrFactory();
        var identity = factory.CreateIdentity();
        var stringId = factory.InternString("identifier");
        var type = factory.GetOrCreateReferenceType(identity, "Identifier");
        var variable = factory.CreateVariable("value", type);
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            type,
            "Read",
            type,
            isStatic: true);
        var operation = factory.CreateOperation("return");
        var term = factory.Variable(variable);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var instruction = builder.Return(block, operation, term);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(identity.IsDefault, Is.False);
            Assert.That(
                identity.ToString(),
                Is.EqualTo("identity" + identity.Value));
            Assert.That(term.Id.IsDefault, Is.False);
            Assert.That(term.Id.ToString(), Is.EqualTo("ir" + term.Id.Value));
            Assert.That(variable.IsDefault, Is.False);
            Assert.That(
                variable.ToString(),
                Is.EqualTo("v" + variable.Value));
            Assert.That(type.IsDefault, Is.False);
            Assert.That(type.ToString(), Is.EqualTo("t" + type.Value));
            Assert.That(member.IsDefault, Is.False);
            Assert.That(member.ToString(), Is.EqualTo("m" + member.Value));
            Assert.That(stringId.IsDefault, Is.False);
            Assert.That(
                stringId.ToString(),
                Is.EqualTo("s" + stringId.Value));
            Assert.That(
                stringId.GetHashCode(),
                Is.EqualTo(factory.InternString("identifier").GetHashCode()));
            Assert.That(operation.IsDefault, Is.False);
            Assert.That(
                operation.ToString(),
                Is.EqualTo("op" + operation.Value));
            Assert.That(block.IsDefault, Is.False);
            Assert.That(block.ToString(), Is.EqualTo("b" + block.Value));
            Assert.That(instruction.Id.IsDefault, Is.False);
            Assert.That(
                instruction.Id.ToString(),
                Is.EqualTo("i" + instruction.Id.Value));
        }
    }
}
