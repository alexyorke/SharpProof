using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class CanonicalCacheResourceAccountingTests
{
    [Test]
    public void SameElementNominalCacheComparisonsRespectTheCurrentBudget()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner);
        for (var index = 0; index < 16; index++)
        {
            encoder.EncodeBoolean(CreateCachePredicate(), new SmtQueryResourceMeter(1_000, CancellationToken.None));
        }
        var next = CreateCachePredicate();
        var limited = new SmtQueryResourceMeter(10, CancellationToken.None);

        Assert.Throws<SmtResourceLimitException>((Action)(() => encoder.EncodeBoolean(next, limited)));
        Assert.That(limited.Consumed, Is.EqualTo(10));

        IrTerm CreateCachePredicate()
        {
            var type = factory.GetOrCreateSequenceType(factory.CreateIdentity(), factory.IntegerType, "nominal[]");
            var cached = factory.Cast(factory.ObjectType, factory.EmptyArray(type));
            return factory.Binary(IrBinaryOperator.NotEqual, cached, factory.Null(factory.ObjectType));
        }
    }
}
