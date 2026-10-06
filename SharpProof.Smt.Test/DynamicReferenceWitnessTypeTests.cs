using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class DynamicReferenceWitnessTypeTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task BuiltinWitnessAliasRequiresACompatibleReferenceView(bool array, bool objectView)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builtinType = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.StringType;
        var viewType = objectView ? factory.ObjectType : factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Cell");
        var builtin = factory.CreateVariable("builtin", builtinType);
        var view = factory.CreateVariable("view", viewType);
        var query = new VerificationQuery(factory,
            [new Assumption(factory,
                factory.Binary(IrBinaryOperator.Equal, factory.Length(factory.Variable(builtin)), factory.Integer(1)),
                new LoweredJustification(factory.CreateOperation()))],
            new Goal(factory, factory.Binary(IrBinaryOperator.NotEqual,
                factory.Cast(factory.ObjectType, factory.Variable(builtin)),
                objectView ? factory.Variable(view) : factory.Cast(factory.ObjectType, factory.Variable(view))),
                ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [view, builtin]);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(query);
        if (objectView)
        {
            Assert.That(outcome, Is.TypeOf<RefutedOutcome>(), outcome.ToString());
            var model = ((RefutedOutcome)outcome).Model.Assignments;
            Assert.That(model[view].Reference, Is.SameAs(array ? model[builtin] : model[builtin].String));
        }
        else
        { Assert.That(outcome, Is.Not.TypeOf<RefutedOutcome>(), outcome.ToString()); }
    }
}
