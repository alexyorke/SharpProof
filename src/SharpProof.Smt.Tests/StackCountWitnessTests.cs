using Microsoft.Z3;

namespace SharpProof.Smt.Test;

public sealed class StackCountWitnessTests
{
    [TestCase(0)]
    [TestCase(64)]
    [TestCase(65)]
    [TestCase(257)]
    [TestCase(int.MaxValue)]
    public void CountPreferenceDoesNotNarrowProofsAndOversizedWitnessesAreRejected(int count)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("stack", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:stack:count", factory.IntegerType, false);
        var read = factory.PureOpaque(field, factory.Variable(variable));
        IrTerm[] predicates = [factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(variable), factory.Null(factory.ObjectType)),
            factory.Binary(IrBinaryOperator.Equal, read, factory.Integer(count))];
        using var context = new Context();
        using var solver = context.MkSolver();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner);
        try
        {
            var meter = new SmtQueryResourceMeter(IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);
            var query = new VerificationQuery(factory,
                predicates.Select(predicate => new Assumption(factory, predicate ?? throw new ArgumentNullException(nameof(predicate)), new LoweredJustification(factory.CreateOperation()))),
                new SharpProof.Verify.Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [variable]);
            encoder.ValidateQuery(query, meter, TestContext.CurrentContext.CancellationToken);
            foreach (var predicate in predicates)
            { solver.Assert(encoder.EncodeBoolean(predicate, meter)); }
            foreach (var fact in encoder.ReferenceFacts)
            { solver.Assert(fact); }
            var preference = encoder.SmallReferenceBound(query, meter);
            Assert.That(preference, Is.Not.Null);
            solver.Push();
            solver.Assert(preference!);
            Assert.That(SmtNativeCheck.Run(solver, [], meter), Is.EqualTo(count <= 64 ? Status.SATISFIABLE : Status.UNSATISFIABLE));
            solver.Pop();
            Assert.That(SmtNativeCheck.Run(solver, [], meter), Is.EqualTo(Status.SATISFIABLE), "The original proof query retains all Count values.");
            using var model = solver.Model;
            if (count == int.MaxValue)
            {
                Assert.Throws<SmtResourceLimitException>((Action)(() => encoder.DecodeModel(query, model, meter)));
            }
            else
            {
                var decoded = encoder.DecodeModel(query, model, meter)!;
                Assert.That(new IrInterpreter(factory).Evaluate(read, decoded).Value!.Integer, Is.EqualTo(count));
            }
        }
        finally
        { encoder.DisposeReferences(); }
    }
}
