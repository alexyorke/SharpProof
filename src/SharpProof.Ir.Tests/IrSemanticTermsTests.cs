using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrSemanticTermsTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void BooleanCombinationsRejectDefaultAfterFactoryValidation(bool conjunction)
    {
        var factory = new IrFactory();
        Func<IrFactory, System.Collections.Immutable.ImmutableArray<IrTerm>, IrTerm> operation = conjunction ? IrSemanticTerms.Conjoin : IrSemanticTerms.Disjoin;
        var missingFactory = Assert.Throws<ArgumentNullException>((Action)(() => operation(null!, default)));
        Assert.That(missingFactory!.ParamName, Is.EqualTo("factory"));
        var missingTerms = Assert.Throws<ArgumentNullException>((Action)(() => operation(factory, default)));
        Assert.That(missingTerms!.ParamName, Is.EqualTo("terms"));
        Assert.That(operation(factory, []), Is.SameAs(factory.Boolean(conjunction)));
    }

    [Test]
    public void SingletonBooleanCombinationsRejectInvalidTerms()
    {
        var factory = new IrFactory();
        var foreignFactory = new IrFactory();

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentException>(
                (Action)(() => IrSemanticTerms.Conjoin(
                    factory,
                    [factory.Integer(1)])));
            Assert.Throws<ArgumentException>(
                (Action)(() => IrSemanticTerms.Disjoin(
                    factory,
                    [factory.Integer(1)])));
            Assert.Throws<ArgumentException>(
                (Action)(() => IrSemanticTerms.Conjoin(
                    factory,
                    [foreignFactory.Boolean(true)])));
            Assert.Throws<ArgumentException>(
                (Action)(() => IrSemanticTerms.Disjoin(
                    factory,
                    [foreignFactory.Boolean(false)])));
        }
    }
}
