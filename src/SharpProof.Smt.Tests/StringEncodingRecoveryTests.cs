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
        var input = factory.Variable(factory.CreateVariable("input", factory.StringType));
        foreach (var literal in new[] { left, right })
        {
            encoder.EncodeBoolean(factory.Binary(IrBinaryOperator.Equal, input, literal),
                new SmtQueryResourceMeter(10_000, TestContext.CurrentContext.CancellationToken));
        }
        var aliasesRight = encoder.EncodeBoolean(factory.Binary(IrBinaryOperator.Equal, input, right),
            new SmtQueryResourceMeter(10_000, TestContext.CurrentContext.CancellationToken));
        // A variable keeps this comparison from folding to a Boolean literal.
        var sameText = factory.Binary(IrBinaryOperator.StringEquals, input, left);
        Assert.Throws<SmtResourceLimitException>((Action)(() => encoder.EncodeBoolean(sameText,
            new SmtQueryResourceMeter(8, TestContext.CurrentContext.CancellationToken))));

        var encoded = encoder.EncodeBoolean(sameText,
            new SmtQueryResourceMeter(10_000, TestContext.CurrentContext.CancellationToken));
        using var solver = context.MkSolver();
        solver.Assert(encoder.ReferenceFacts.ToArray());
        solver.Assert(aliasesRight);
        solver.Assert(encoded);
        Assert.That(solver.Check(), Is.EqualTo(Status.UNSATISFIABLE),
            "A later query must recover both distinct literal contents after partial initialization.");
    }
}
