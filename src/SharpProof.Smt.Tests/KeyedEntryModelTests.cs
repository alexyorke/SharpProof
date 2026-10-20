using System.Collections.Immutable;
using Microsoft.Z3;

namespace SharpProof.Smt.Test;

public sealed class KeyedEntryModelTests
{
    [TestCase(0L)]
    [TestCase(long.MinValue)]
    [TestCase(long.MaxValue)]
    public void DecoderReconstructsOnlyObservedOwnerAndFullWidthKey(long bits)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(64, true);
        var first = factory.CreateVariable("first", factory.ObjectType);
        var second = factory.CreateVariable("second", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:map", factory.IntegerType, false, type);
        var read = factory.PureOpaque(field, factory.Variable(first), factory.Integer(type, bits));
        Decode(factory, [first, second], [
            factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(first), factory.Null(factory.ObjectType)),
            factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(second), factory.Null(factory.ObjectType)),
            factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(first), factory.Variable(second)),
            factory.Binary(IrBinaryOperator.Equal, read, factory.Integer(23))], values =>
        {
            ArgumentNullException.ThrowIfNull(values);
            var interpreter = new IrInterpreter(factory);
            Assert.That(interpreter.Evaluate(read, values).Value!.Integer, Is.EqualTo(23));
            var absent = factory.PureOpaque(field, factory.Variable(first), factory.Integer(type, bits == 0 ? 1 : 0));
            Assert.That(interpreter.Evaluate(absent, values).Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
            var other = factory.PureOpaque(field, factory.Variable(second), factory.Integer(type, bits));
            Assert.That(interpreter.Evaluate(other, values).Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ObservedReferenceCellsPreserveNullAndCyclicIdentity(bool cycle)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("owner", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:refs", factory.ObjectType, false, factory.IntegerType);
        var read = factory.PureOpaque(field, factory.Variable(variable), factory.Integer(0));
        Decode(factory, [variable], [
            factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(variable), factory.Null(factory.ObjectType)),
            factory.Binary(IrBinaryOperator.Equal, read, cycle ? factory.Variable(variable) : factory.Null(factory.ObjectType))], values =>
        {
            ArgumentNullException.ThrowIfNull(values);
            var result = new IrInterpreter(factory).Evaluate(read, values).Value!;
            if (cycle)
            { Assert.That(result.Reference, Is.SameAs(values[variable].Reference)); }
            else
            { Assert.That(result.Kind, Is.EqualTo(IrValueKind.Null)); }
        });
    }

    [TestCase("list")]
    [TestCase("queue")]
    public void OversizedCollectionWitnessIsRejectedBeforeReplay(string collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("owner", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:" + collection + ":count", factory.IntegerType, false);
        Assert.Throws<SmtResourceLimitException>((Action)(() => Decode(factory, [variable], [
            factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(variable), factory.Null(factory.ObjectType)),
            factory.Binary(IrBinaryOperator.Equal, factory.PureOpaque(field, factory.Variable(variable)), factory.Integer(int.MaxValue))], _ => Assert.Fail("Oversized witness must not be published."))));
    }

    private static void Decode(IrFactory factory, ImmutableArray<ScopedIrId<IrVariableTag>> variables, ImmutableArray<IrTerm> predicates,
        Action<ImmutableDictionary<ScopedIrId<IrVariableTag>, IrValue>> inspect)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (variables.IsDefault)
        { throw new ArgumentNullException(nameof(variables)); }
        if (predicates.IsDefault)
        { throw new ArgumentNullException(nameof(predicates)); }
        ArgumentNullException.ThrowIfNull(inspect);
        using var context = new Context();
        using var solver = context.MkSolver();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner);
        try
        {
            var meter = new SmtQueryResourceMeter(IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);
            var query = new VerificationQuery(factory,
                predicates.Select(predicate => new Assumption(factory, predicate ?? throw new ArgumentNullException(nameof(predicate)), new LoweredJustification(factory.CreateOperation()))),
                new SharpProof.Verify.Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), variables);
            encoder.ValidateQuery(query, meter, TestContext.CurrentContext.CancellationToken);
            foreach (var predicate in predicates)
            { solver.Assert(encoder.EncodeBoolean(predicate, meter)); }
            foreach (var fact in encoder.ReferenceFacts)
            { solver.Assert(fact); }
            Assert.That(SmtNativeCheck.Run(solver, [], meter), Is.EqualTo(Status.SATISFIABLE));
            using var model = solver.Model;
            inspect(encoder.DecodeModel(query, model, meter)!);
        }
        finally
        { encoder.DisposeReferences(); }
    }
}
