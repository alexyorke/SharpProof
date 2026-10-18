using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrSemanticTermsTests
{
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
