using System.Collections.Immutable;
using Microsoft.Z3;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;
using IrTypeId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrTypeTag>;
using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class NativeCompletedModelInvariantTests
{
    private static IEnumerable<TestCaseData> IntegerCases()
    {
        foreach (var width in ImmutableArray.Create(8, 16, 32, 64))
        {
            foreach (var signedness in ImmutableArray.Create(false, true))
            {
                foreach (var mode in ImmutableArray.Create("free", "zero", "high", "ones"))
                {
                    yield return new TestCaseData(width, signedness, mode);
                }
            }
        }
    }

    [TestCaseSource(nameof(IntegerCases))]
    public void CompletedScalarKeepsItsAdmittedIntegerType(int width, bool signedness, string mode)
    {
        System.ArgumentNullException.ThrowIfNull(mode);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, signedness);
        var variable = factory.CreateVariable("input", type);
        var bits = mode == "high" ? 1UL << (width - 1)
            : mode == "ones" ? width == 64 ? ulong.MaxValue : (1UL << width) - 1 : 0;
        ImmutableArray<IrTerm> predicates = mode == "free" ? []
            : [Equal(factory, factory.Variable(variable), factory.IntegerBits(type, bits))];
        WithNativeModel(factory, variable, predicates, (encoder, model, meter, values) =>
        {
            System.ArgumentNullException.ThrowIfNull(encoder);
            System.ArgumentNullException.ThrowIfNull(meter);
            System.ArgumentNullException.ThrowIfNull(model);
            System.ArgumentNullException.ThrowIfNull(values);
            var value = values[variable];
            Assert.That(value.Type, Is.EqualTo(type));
            Assert.That(value.IntegerBits, Is.EqualTo(bits));
            Assert.That(value.IntegerWidth, Is.EqualTo(width));
            Assert.That(value.IntegerSigned, Is.EqualTo(signedness));
            using var evaluated = model.Evaluate(encoder.GetVariable(variable, meter), true);
            Assert.That(evaluated, Is.TypeOf<BitVecNum>());
            Assert.That(((BitVecNum)evaluated).SortSize, Is.EqualTo((uint)width));
            Assert.That(((BitVecNum)evaluated).UInt64, Is.EqualTo(bits));
        });
    }

    [TestCase("free", false)]
    [TestCase("false", false)]
    [TestCase("true", true)]
    public void CompletedScalarIsAConcreteBoolean(string mode, bool expected)
    {
        System.ArgumentNullException.ThrowIfNull(mode);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("flag", factory.BooleanType);
        ImmutableArray<IrTerm> predicates = mode == "free" ? []
            : [Equal(factory, factory.Variable(variable), factory.Boolean(expected))];
        WithNativeModel(factory, variable, predicates, (encoder, model, meter, values) =>
        {
            System.ArgumentNullException.ThrowIfNull(encoder);
            System.ArgumentNullException.ThrowIfNull(meter);
            System.ArgumentNullException.ThrowIfNull(model);
            System.ArgumentNullException.ThrowIfNull(values);
            Assert.That(values[variable].Type, Is.EqualTo(factory.BooleanType));
            Assert.That(values[variable].Boolean, Is.EqualTo(expected));
            using var evaluated = model.Evaluate(encoder.GetVariable(variable, meter), true);
            Assert.That(evaluated.IsTrue || evaluated.IsFalse, Is.True);
            Assert.That(evaluated.IsTrue, Is.EqualTo(expected));
        });
    }

    [TestCase("string", 0)]
    [TestCase("string", 1)]
    [TestCase("string", 3)]
    [TestCase("bool-array", 0)]
    [TestCase("bool-array", 1)]
    [TestCase("bool-array", 3)]
    [TestCase("int-array", 0)]
    [TestCase("int-array", 1)]
    [TestCase("int-array", 3)]
    public void NonnullBuiltinLengthDecodesIntoTheIndependentClrShape(string kind, int count)
    {
        System.ArgumentNullException.ThrowIfNull(kind);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = BuiltinType(factory, kind);
        var variable = factory.CreateVariable("input", type);
        var term = factory.Variable(variable);
        WithNativeModel(factory, variable,
            [Nonnull(factory, term), Equal(factory, factory.Length(term), factory.Integer(count))],
            (_, _, _, values) => { System.ArgumentNullException.ThrowIfNull(values); AssertBuiltin(factory, values[variable], kind, count); });
    }

    [TestCase("string")]
    [TestCase("bool-array")]
    [TestCase("int-array")]
    public void NativeNullBuiltinDecodesAsNull(string kind)
    {
        System.ArgumentNullException.ThrowIfNull(kind);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = BuiltinType(factory, kind);
        var variable = factory.CreateVariable("input", type);
        WithNativeModel(factory, variable, [Equal(factory, factory.Variable(variable), factory.Null(type))],
            (_, _, _, values) =>
            {
                System.ArgumentNullException.ThrowIfNull(values);
                Assert.That(values[variable].Type, Is.EqualTo(type));
                Assert.That(values[variable].Kind, Is.EqualTo(IrValueKind.Null));
            });
    }

    [TestCase("string")]
    [TestCase("bool-array")]
    [TestCase("int-array")]
    public void ActualEncodedLengthCanReachInt32MaxWithoutAllocatingAWitness(string kind)
    {
        System.ArgumentNullException.ThrowIfNull(kind);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = BuiltinType(factory, kind);
        var variable = factory.CreateVariable("input", type);
        var term = factory.Variable(variable);
        WithNativeModel(factory, variable,
            [Nonnull(factory, term), Equal(factory, factory.Length(term), factory.Integer(int.MaxValue))],
            (encoder, model, meter, _) =>
            {
                System.ArgumentNullException.ThrowIfNull(encoder);
                System.ArgumentNullException.ThrowIfNull(meter);
                System.ArgumentNullException.ThrowIfNull(model);
                var equality = encoder.EncodeBoolean(Equal(factory, factory.Length(term), factory.Integer(int.MaxValue)), meter);
                Assert.That(equality.FuncDecl.DeclKind, Is.EqualTo(Z3_decl_kind.Z3_OP_EQ));
                Assert.That(equality.NumArgs, Is.EqualTo(2U));
                var children = equality.Args.ToImmutableArray();
                try
                {
                    using var evaluated = model.Evaluate(children[0], true);
                    Assert.That(evaluated, Is.TypeOf<BitVecNum>());
                    Assert.That(((BitVecNum)evaluated).SortSize, Is.EqualTo(32U));
                    Assert.That(((BitVecNum)evaluated).UInt64, Is.EqualTo((ulong)int.MaxValue));
                    Assert.That(SmtNativeUtilities.ReadNonnegativeInt32(evaluated), Is.EqualTo(int.MaxValue));
                }
                finally
                {
                    foreach (var child in children)
                    { child.Dispose(); }
                }
            }, decode: false);
    }

    private static void WithNativeModel(IrFactory factory, IrVarId variable, ImmutableArray<IrTerm> predicates,
        Action<BvEncoder, Model, SmtQueryResourceMeter, ImmutableDictionary<IrVarId, IrValue>> inspect, bool decode = true)
    {
        if (predicates.IsDefault)
        { throw new ArgumentNullException(nameof(predicates)); }
        if (variable.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(variable));
        }
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(inspect);
        var query = new VerificationQuery(factory,
            predicates.Select(predicate => predicate is null ? throw new System.ArgumentNullException(nameof(predicate)) : (new Assumption(factory, predicate, new LoweredJustification(factory.CreateOperation())))),
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [variable]);
        using var context = new Context();
        using var solver = context.MkSolver();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner);
        try
        {
            var meter = new SmtQueryResourceMeter(IrSmtBackendOptions.DefaultQueryRlimit, TestContext.CurrentContext.CancellationToken);
            encoder.ValidateQuery(query, meter, TestContext.CurrentContext.CancellationToken);
            foreach (var predicate in predicates)
            { solver.Assert(encoder.EncodeBoolean(predicate, meter)); }
            foreach (var fact in encoder.ReferenceFacts)
            { solver.Assert(fact); }
            var goalSelector = owner.Own(context.MkBoolConst("actual-goal-selector"));
            solver.Assert(owner.Own(context.MkImplies(goalSelector,
                owner.Own(context.MkNot(encoder.EncodeBoolean(query.Goal.Predicate, meter))))));
            using var parameters = context.MkParams();
            SmtNativeUtilities.AddOwnedParameter(parameters, context.MkSymbol("rlimit"), meter.GetRemainingBudget());
            solver.Parameters = parameters;
            Assert.That(SmtNativeCheck.Run(solver, [goalSelector], meter), Is.EqualTo(Status.SATISFIABLE));
            using var model = solver.Model;
            Assert.That(model, Is.Not.Null);
            var values = decode ? encoder.DecodeModel(query, model, meter)! : ImmutableDictionary<IrVarId, IrValue>.Empty;
            if (decode)
            { Assert.That(values.Keys, Is.EquivalentTo(ImmutableArray.Create(variable))); }
            inspect(encoder, model, meter, values);
        }
        finally
        {
            solver.Dispose();
            owner.Dispose();
            encoder.DisposeReferences();
        }
    }

    private static IrTerm Equal(IrFactory factory, IrTerm left, IrTerm right)
    { System.ArgumentNullException.ThrowIfNull(factory, nameof(factory)); System.ArgumentNullException.ThrowIfNull(left, nameof(left)); System.ArgumentNullException.ThrowIfNull(right, nameof(right)); return factory.Binary(IrBinaryOperator.Equal, left, right); }
    private static IrTerm Nonnull(IrFactory factory, IrTerm term)
    { System.ArgumentNullException.ThrowIfNull(factory, nameof(factory)); System.ArgumentNullException.ThrowIfNull(term, nameof(term)); return factory.Binary(IrBinaryOperator.NotEqual, term, factory.Null(term.Type)); }
    private static IrTypeId BuiltinType(IrFactory factory, string kind)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(kind);
        return kind == "string" ? factory.StringType
            : factory.GetOrCreateSequenceType(kind == "bool-array" ? factory.BooleanType : factory.IntegerType);
    }
    private static void AssertBuiltin(IrFactory factory, IrValue value, string kind, int count)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(kind);
        System.ArgumentNullException.ThrowIfNull(value);
        Assert.That(value.Type, Is.EqualTo(BuiltinType(factory, kind)));
        if (kind == "string")
        { Assert.That(value.String, Is.EqualTo(new string('\0', count))); }
        else
        {
            Assert.That(value.Elements.Length, Is.EqualTo(count));
            foreach (var element in value.Elements)
            {
                Assert.That(element.Type, Is.EqualTo(kind == "bool-array" ? factory.BooleanType : factory.IntegerType));
                if (kind == "bool-array")
                { Assert.That(element.Boolean, Is.False); }
                else
                { Assert.That(element.IntegerBits, Is.Zero); }
            }
        }
    }
}
