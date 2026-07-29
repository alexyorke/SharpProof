using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrIdentifierTests
{
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
