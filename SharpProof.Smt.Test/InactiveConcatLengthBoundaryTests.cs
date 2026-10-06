using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class InactiveConcatLengthBoundaryTests
{
    [TestCase(1, BackendCheckStatus.Satisfiable, BackendFailureReason.None)]
    [TestCase(800_000_000, BackendCheckStatus.Unknown, BackendFailureReason.ResourceLimit)]
    public async Task PriorConcatContentQueryCannotRestrictLaterEntryLengths(int length, BackendCheckStatus expected, BackendFailureReason reason)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var first = factory.CreateVariable("first", factory.StringType);
        var second = factory.CreateVariable("second", factory.StringType);
        var third = factory.CreateVariable("third", factory.StringType);
        var concat = factory.Binary(IrBinaryOperator.StringConcat,
            factory.Binary(IrBinaryOperator.StringConcat, factory.Variable(first), factory.Variable(second)), factory.Variable(third));
        Assumption Length(SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag> variable, int count)
        {
            return
            new(factory, factory.Binary(IrBinaryOperator.Equal, factory.Length(factory.Variable(variable)), factory.Integer(count)),
                new LoweredJustification(factory.CreateOperation()));
        }
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var content = new VerificationQuery(factory, [Length(first, 1), Length(second, 1), Length(third, 1)],
            new Goal(factory, factory.Binary(IrBinaryOperator.StringEquals, concat, factory.String("ab")),
                ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
        Assert.That((await session.CheckAsync(content, CancellationToken.None)).Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var entry = new VerificationQuery(factory, [Length(first, length), Length(second, length), Length(third, length)],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)));
        var result = await session.CheckAsync(entry, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(expected));
        Assert.That(result.FailureReason, Is.EqualTo(reason));
    }
}
