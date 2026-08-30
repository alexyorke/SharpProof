using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrFactoryStringValueTests
{
    [Test]
    public void CreateStringValueRejectsIllFormedUtf16LikeStringTerms()
    {
        var factory = new IrFactory();
        foreach (var surrogate in new[] { '\uD800', '\uDC00' })
        {
            var value = new string(surrogate, 1);
            var valueError = Assert.Throws<ArgumentException>(
                (Action)(() => factory.CreateStringValue(value)));
            var termError = Assert.Throws<ArgumentException>(
                (Action)(() => factory.String(value)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(valueError!.ParamName, Is.EqualTo("value"));
                Assert.That(
                    valueError.Message,
                    Does.Contain("well-formed UTF-16"));
                Assert.That(termError!.ParamName, Is.EqualTo("value"));
            }
        }
    }

    [Test]
    public void CreateStringValuePreservesWellFormedSurrogatePairs()
    {
        const string value = "\U0001F600";
        var factory = new IrFactory();

        var created = factory.CreateStringValue(value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(created.Type, Is.EqualTo(factory.StringType));
            Assert.That(created.Kind, Is.EqualTo(IrValueKind.String));
            Assert.That(created.String, Is.EqualTo(value));
            Assert.That(factory.String(value), Is.Not.Null);
        }
    }

    [Test]
    public void ObjectToStringCastsUseValidatedStringValues()
    {
        var factory = new IrFactory();
        var source = factory.CreateVariable("source", factory.ObjectType);
        var cast = factory.Cast(
            factory.StringType,
            factory.Variable(source));
        var malformed = new string('\uD800', 1);
        var interpreter = new IrInterpreter(factory);

        var error = Assert.Throws<ArgumentException>((Action)(() =>
            interpreter.Evaluate(
                cast,
                new Dictionary<IrVarId, IrValue>
                {
                    [source] = factory.CreateReferenceValue(
                        factory.ObjectType,
                        malformed)
                })));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.ParamName, Is.EqualTo("value"));
            Assert.That(
                error.Message,
                Does.Contain("well-formed UTF-16"));
        }
    }
}
