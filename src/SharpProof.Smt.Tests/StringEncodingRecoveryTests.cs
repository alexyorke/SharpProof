using Microsoft.Z3;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class StringEncodingRecoveryTests
{
    [Test]
    public void ExhaustedContentInitializationResumesAllLiteralFacts()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner);
        var left = factory.String(new string('a', 64));
        var right = factory.String(new string('b', 64));
        foreach (var literal in new[] { left, right })
        {
            encoder.EncodeBoolean(factory.Binary(IrBinaryOperator.NotEqual, literal, factory.Null(factory.StringType)),
                new SmtQueryResourceMeter(10_000, TestContext.CurrentContext.CancellationToken));
        }
        var sameText = factory.Binary(IrBinaryOperator.StringEquals, left, right);
        Assert.Throws<SmtResourceLimitException>((Action)(() => encoder.EncodeBoolean(sameText,
            new SmtQueryResourceMeter(8, TestContext.CurrentContext.CancellationToken))));

        var encoded = encoder.EncodeBoolean(sameText,
            new SmtQueryResourceMeter(10_000, TestContext.CurrentContext.CancellationToken));
        using var solver = context.MkSolver();
        solver.Assert(encoder.ReferenceFacts.ToArray());
        solver.Assert(encoded);
        Assert.That(solver.Check(), Is.EqualTo(Status.UNSATISFIABLE),
            "A later query must recover both distinct literal contents after partial initialization.");
    }
}
