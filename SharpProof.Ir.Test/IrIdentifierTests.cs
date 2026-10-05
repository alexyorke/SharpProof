using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrIdentifierTests
{
    private static readonly string[] s_identifierPrefixes = [
        "identity",
        "ir",
        "v",
        "t",
        "m",
        "s",
        "op",
        "b",
        "i"
    ];

    [Test]
    public void DefaultIdentifiersPreserveKindSpecificFormatting()
    {
        var identifiers = new[]
        {
            (IsDefault: default(IrIdentityId).IsDefault,
                Text: default(IrIdentityId).ToString()),
            (IsDefault: default(IrId).IsDefault,
                Text: default(IrId).ToString()),
            (IsDefault: default(IrVarId).IsDefault,
                Text: default(IrVarId).ToString()),
            (IsDefault: default(IrTypeId).IsDefault,
                Text: default(IrTypeId).ToString()),
            (IsDefault: default(IrMemberId).IsDefault,
                Text: default(IrMemberId).ToString()),
            (IsDefault: default(IrStringId).IsDefault,
                Text: default(IrStringId).ToString()),
            (IsDefault: default(OperationId).IsDefault,
                Text: default(OperationId).ToString()),
            (IsDefault: default(IrBlockId).IsDefault,
                Text: default(IrBlockId).ToString()),
            (IsDefault: default(IrInstructionId).IsDefault,
                Text: default(IrInstructionId).ToString())
        };

        using (Assert.EnterMultipleScope())
        {
            for (var index = 0; index < identifiers.Length; index++)
            {
                Assert.That(identifiers[index].IsDefault, Is.True);
                Assert.That(
                    identifiers[index].Text,
                    Is.EqualTo(s_identifierPrefixes[index] + "0"));
            }
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
        var identifiers = new[]
        {
            (IsDefault: identity.IsDefault,
                Text: identity.ToString(),
                Value: identity.Value),
            (IsDefault: term.Id.IsDefault,
                Text: term.Id.ToString(),
                Value: term.Id.Value),
            (IsDefault: variable.IsDefault,
                Text: variable.ToString(),
                Value: variable.Value),
            (IsDefault: type.IsDefault,
                Text: type.ToString(),
                Value: type.Value),
            (IsDefault: member.IsDefault,
                Text: member.ToString(),
                Value: member.Value),
            (IsDefault: stringId.IsDefault,
                Text: stringId.ToString(),
                Value: stringId.Value),
            (IsDefault: operation.IsDefault,
                Text: operation.ToString(),
                Value: operation.Value),
            (IsDefault: block.IsDefault,
                Text: block.ToString(),
                Value: block.Value),
            (IsDefault: instruction.Id.IsDefault,
                Text: instruction.Id.ToString(),
                Value: instruction.Id.Value)
        };

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                stringId.GetHashCode(),
                Is.EqualTo(factory.InternString("identifier").GetHashCode()));
            for (var index = 0; index < identifiers.Length; index++)
            {
                Assert.That(identifiers[index].IsDefault, Is.False);
                Assert.That(
                    identifiers[index].Text,
                    Is.EqualTo(
                        s_identifierPrefixes[index] + identifiers[index].Value));
            }
        }
    }
}
