using System.Collections.Immutable;
using Microsoft.Z3;
using IrVarId = SharpProof.Ir.ScopedIrId<SharpProof.Ir.IrVariableTag>;
using Goal = SharpProof.Verify.Goal;

namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class ReferenceCallableSolverTests
{
    [TestCase("")]
    [TestCase("text")]
    [TestCase("a\0b")]
    public async Task StringLiteralAliasesDecodeWithExactContents(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var input = factory.CreateVariable("input", factory.StringType);
        var literal = factory.String(text);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [Assume(factory, Equal(factory, factory.Variable(input), literal))],
            factory.Boolean(false), [input]);
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        Assert.That(result.Model!.Assignments[input].String, Is.SameAs(factory.GetString(literal.Value)));
        AssertAssumptions(factory, query, result.Model.Assignments);
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
        var length = Query(factory, [], Equal(factory, factory.Length(literal), factory.Integer(text.Length)), []);
        Assert.That((await session.CheckAsync(length, TestContext.CurrentContext.CancellationToken)).Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
    }

    [Test]
    public async Task DifferentStringLiteralsCannotShareAnIdentity()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [], factory.Binary(IrBinaryOperator.NotEqual, factory.String("left"), factory.String("same")), []);
        Assert.That((await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken)).Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
    }

    [Test]
    public async Task EmptyArrayAliasesDecodeToTheSameConcreteValue()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var input = factory.CreateVariable("input", type);
        var empty = factory.EmptyArray(type);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [Assume(factory, Equal(factory, factory.Variable(input), empty))],
            factory.Boolean(false), [input]);
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        Assert.That(result.Model!.Assignments[input], Is.SameAs(factory.CreateEmptyArrayValue(type)));
        AssertAssumptions(factory, query, result.Model.Assignments);
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
        var nonNull = Query(factory, [], factory.Binary(IrBinaryOperator.NotEqual, empty, factory.Null(type)), []);
        Assert.That((await session.CheckAsync(nonNull, TestContext.CurrentContext.CancellationToken)).Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
        var length = Query(factory, [], Equal(factory, factory.Length(empty), factory.Integer(0)), []);
        Assert.That((await session.CheckAsync(length, TestContext.CurrentContext.CancellationToken)).Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
    }

    [TestCase(true, 0)]
    [TestCase(false, 0)]
    [TestCase(true, 3)]
    [TestCase(false, 3)]
    public async Task StringIdentityWitnessesMatchConcreteAliases(bool alias, int length)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", factory.StringType);
        var y = factory.CreateVariable("y", factory.StringType);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [NotNull(factory, x), NotNull(factory, y),
            Assume(factory, factory.Binary(alias ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual, factory.Variable(x), factory.Variable(y))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(x)), factory.Integer(length))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(y)), factory.Integer(length)))], factory.Boolean(false), [x, y]);
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        // Distinct empty strings exist on the CLR (string.Copy("") is not ""),
        // so two non-aliased empty strings are a satisfiable, replayable witness.
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var model = result.Model!.Assignments;
        Assert.That(ReferenceEquals(model[x].String, model[y].String), Is.EqualTo(alias));
        AssertAssumptions(factory, query, model);
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ObjectWitnessesPreserveIdentity(bool same)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", factory.ObjectType);
        var y = factory.CreateVariable("y", factory.ObjectType);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [NotNull(factory, x), NotNull(factory, y),
            Assume(factory, factory.Binary(same ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                factory.Variable(x), factory.Variable(y)))], factory.Boolean(false), [x, y]);
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var model = result.Model!.Assignments;
        Assert.That(ReferenceEquals(model[x].Reference, model[y].Reference), Is.EqualTo(same));
        AssertAssumptions(factory, query, model);
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ArrayWitnessesPreserveIdentityAndLength(bool same)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var x = factory.CreateVariable("x", type);
        var y = factory.CreateVariable("y", type);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [NotNull(factory, x), NotNull(factory, y),
            Assume(factory, factory.Binary(same ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                factory.Variable(x), factory.Variable(y))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(x)), factory.Integer(3))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(y)), factory.Integer(3)))],
            factory.Boolean(false), [x, y]);
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var model = result.Model!.Assignments;
        Assert.That(ReferenceEquals(model[x], model[y]), Is.EqualTo(same));
        Assert.That(model[x].Elements.Length, Is.EqualTo(3));
        Assert.That(model[y].Elements.Length, Is.EqualTo(3));
        AssertAssumptions(factory, query, model);
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
    }

    [TestCase(true, 0)]
    [TestCase(false, 3)]
    public async Task StringWitnessesPreserveNullAndCodeUnitLength(bool isNull, int length)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var text = factory.CreateVariable("text", factory.StringType);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [
            Assume(factory, factory.Binary(isNull ? IrBinaryOperator.Equal : IrBinaryOperator.NotEqual,
                factory.Variable(text), factory.Null(factory.StringType))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(text)), factory.Integer(length)))],
            factory.Boolean(false), [text]);
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var value = result.Model!.Assignments[text];
        Assert.That(value.Kind, Is.EqualTo(isNull ? IrValueKind.Null : IrValueKind.String));
        if (!isNull)
        { Assert.That(value.String.Length, Is.EqualTo(length)); }
        AssertAssumptions(factory, query, result.Model.Assignments);
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task FullLengthProofDomainIsPreservedWhileOversizedWitnessesAbstain(bool array)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.StringType;
        var text = factory.CreateVariable("text", type);
        var length = factory.Length(factory.Variable(text));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var nonnegative = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, length, factory.Integer(0));
        var proof = await new ProofKernel(session).VerifyAsync(Query(factory, [], nonnegative, [text]), TestContext.CurrentContext.CancellationToken);
        Assert.That(proof, Is.TypeOf<ProvenOutcome>());
        var maximum = Equal(factory, length, factory.Integer(int.MaxValue));
        var assumptions = ImmutableArray.Create(NotNull(factory, text), Assume(factory, maximum));
        var maximumProof = await new ProofKernel(session).VerifyAsync(Query(factory, assumptions, maximum, [text]), TestContext.CurrentContext.CancellationToken);
        Assert.That(maximumProof, Is.TypeOf<ProvenOutcome>());
        var witness = await session.CheckAsync(Query(factory, assumptions, factory.Boolean(false), [text]), TestContext.CurrentContext.CancellationToken);
        Assert.That(witness.Status, Is.EqualTo(BackendCheckStatus.Unknown));
        Assert.That(witness.FailureReason, Is.EqualTo(BackendFailureReason.ResourceLimit));
    }

    [TestCase(false, 1)]
    [TestCase(true, 1)]
    [TestCase(false, 64)]
    [TestCase(true, 64)]
    [TestCase(false, 65)]
    [TestCase(true, 65)]
    [TestCase(false, 257)]
    [TestCase(true, 257)]
    public async Task WitnessPreferenceRetriesUnboundedQueriesAndKeepsLargeCounterexamples(bool array, int minimum)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.StringType;
        var input = factory.CreateVariable("input", type);
        var length = factory.Length(factory.Variable(input));
        var atLeast = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, length, factory.Integer(minimum));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [NotNull(factory, input), Assume(factory, atLeast),
            Assume(factory, factory.Binary(IrBinaryOperator.LessThanOrEqual, length, factory.Integer(minimum + 32)))],
            factory.Boolean(false), [input]);
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        var value = result.Model!.Assignments[input];
        var actual = array ? value.Elements.Length : value.String.Length;
        Assert.That(actual, Is.GreaterThanOrEqualTo(minimum));
        if (minimum <= 64)
        { Assert.That(actual, Is.LessThanOrEqualTo(64)); }
        AssertAssumptions(factory, query, result.Model.Assignments);
        // Reuse the session: a failed bounded preference cannot turn a later
        // large-input counterexample into a proof or leak a previous goal.
        var wrong = Query(factory, query.Assumptions,
            factory.Binary(IrBinaryOperator.LessThan, length, factory.Integer(minimum)), [input]);
        Assert.That(await new ProofKernel(session).VerifyAsync(wrong, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
    }
    [TestCase(0, false, 1UL)]
    [TestCase(8, true, 255UL)]
    [TestCase(16, false, 65535UL)]
    [TestCase(32, true, 2147483648UL)]
    [TestCase(32, false, 4294967295UL)]
    [TestCase(64, true, 9223372036854775808UL)]
    [TestCase(64, false, ulong.MaxValue)]
    public async Task ArrayElementWitnessesPreserveScalarBitsAndAliases(int width, bool isSigned, ulong bits)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var element = width == 0 ? factory.BooleanType : factory.GetOrCreateIntegerType(width, isSigned);
        var type = factory.GetOrCreateSequenceType(element);
        var x = factory.CreateVariable("x", type);
        var y = factory.CreateVariable("y", type);
        var index = factory.CreateVariable("index", factory.IntegerType);
        var read = factory.SequenceAccess(factory.Variable(x), factory.Variable(index));
        IrTerm expected = width == 0 ? factory.Boolean(true) : factory.IntegerBits(element, bits);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, [NotNull(factory, x),
            Assume(factory, Equal(factory, factory.Variable(x), factory.Variable(y))),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(x)), factory.Integer(3))),
            Assume(factory, Equal(factory, factory.Variable(index), factory.Integer(2))),
            Assume(factory, Equal(factory, read, expected))], factory.Boolean(false), [x, y, index]);
        var outcome = await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        var model = result.Model!.Assignments;
        Assert.That(ReferenceEquals(model[x], model[y]), Is.True);
        Assert.That(model[x].Elements[2].Kind == IrValueKind.Boolean ? (ulong)(model[x].Elements[2].Boolean ? 1 : 0) : model[x].Elements[2].IntegerBits,
            Is.EqualTo(bits));
        AssertAssumptions(factory, query, model);
    }

    [TestCase(-1)]
    [TestCase(3)]
    [TestCase(int.MaxValue)]
    public async Task TotalArrayReadOutsideItsRangeHasTheDefaultValue(int index)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var x = factory.CreateVariable("x", type);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var read = factory.SequenceAccess(factory.Variable(x), factory.Integer(index));
        var query = Query(factory, [NotNull(factory, x),
            Assume(factory, Equal(factory, factory.Length(factory.Variable(x)), factory.Integer(3)))],
            Equal(factory, read, factory.Integer(0)), [x]);
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        var nullQuery = Query(factory, [Assume(factory, Equal(factory, factory.Variable(x), factory.Null(type)))],
            Equal(factory, read, factory.Integer(0)), [x]);
        Assert.That(await new ProofKernel(session).VerifyAsync(nullQuery, TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
    }

    [TestCase(0, "list")]
    [TestCase(64, "list")]
    [TestCase(65, "list")]
    [TestCase(257, "list")]
    [TestCase(int.MaxValue, "list")]
    [TestCase(0, "queue")]
    [TestCase(64, "queue")]
    [TestCase(65, "queue")]
    [TestCase(257, "queue")]
    [TestCase(int.MaxValue, "queue")]
    public async Task ListWitnessPreferencePreservesLargeProofsAndCounterexamples(int minimum, string collection = "list")
    {
        ArgumentNullException.ThrowIfNull(collection);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var input = factory.CreateVariable("list", factory.ObjectType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:" + collection + ":count", factory.IntegerType, false);
        var count = factory.Binary(IrBinaryOperator.BitwiseAnd,
            factory.PureOpaque(field, factory.Variable(input)), factory.Integer(int.MaxValue));
        var assumptions = ImmutableArray.Create(NotNull(factory, input),
            Assume(factory, factory.Binary(IrBinaryOperator.GreaterThanOrEqual, count, factory.Integer(minimum))),
            Assume(factory, factory.Binary(IrBinaryOperator.LessThanOrEqual, count, factory.Integer(minimum == int.MaxValue ? int.MaxValue : minimum + 32))));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var predicate = factory.Binary(IrBinaryOperator.LessThan, count, factory.Integer(minimum));
        var query = Query(factory, assumptions, predicate, [input]);
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        if (minimum == int.MaxValue)
        {
            Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Unknown));
            Assert.That(result.FailureReason, Is.EqualTo(BackendFailureReason.ResourceLimit));
        }
        else
        {
            Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
            var actual = new IrInterpreter(factory).Evaluate(count, result.Model!.Assignments).Value!.Integer;
            Assert.That(actual, Is.GreaterThanOrEqualTo(minimum));
            if (minimum <= 64)
            { Assert.That(actual, Is.LessThanOrEqualTo(64)); }
            Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
        }
        // A preference selector cannot restrict a subsequent universal proof.
        var largeProof = Query(factory, assumptions,
            factory.Binary(IrBinaryOperator.GreaterThanOrEqual, count, factory.Integer(minimum)), [input]);
        Assert.That(await new ProofKernel(session).VerifyAsync(largeProof, TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task NominalWitnessPreferenceCannotProveAClaimOrOverrideRequiredAliases(bool fields)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var firstType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "First");
        var secondType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Second");
        var firstVariable = factory.CreateVariable("first", fields ? factory.ObjectType : firstType);
        var secondVariable = factory.CreateVariable("second", secondType);
        IrTerm first = factory.Variable(firstVariable);
        IrTerm second = factory.Variable(secondVariable);
        ImmutableArray<IrVarId> variables = [firstVariable, secondVariable];
        if (fields)
        {
            var firstField = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:first", firstType, false);
            var secondField = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:second", secondType, false);
            first = factory.PureOpaque(firstField, factory.Variable(firstVariable));
            second = factory.PureOpaque(secondField, factory.Variable(firstVariable));
            variables = [firstVariable];
        }
        var assumptions = ImmutableArray.Create(Assume(factory, factory.Binary(IrBinaryOperator.NotEqual, first, factory.Null(firstType))),
            Assume(factory, factory.Binary(IrBinaryOperator.NotEqual, second, factory.Null(secondType))));
        if (fields)
        { assumptions = assumptions.Add(NotNull(factory, firstVariable)); }
        var alias = Equal(factory, factory.Cast(factory.ObjectType, first), factory.Cast(factory.ObjectType, second));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var requiredAlias = Query(factory, assumptions.Add(Assume(factory, alias)), factory.Boolean(false), variables);
        // The nominal retry is UNSAT here. The original uncertified SAT
        // alias remains rejected; this retry never turns UNSAT into a proof.
        Assert.That(await new ProofKernel(session).VerifyAsync(requiredAlias, TestContext.CurrentContext.CancellationToken), Is.TypeOf<UnknownOutcome>());
        // Rechecking the same rejected preference must preserve abstention;
        // cached selectors cannot turn that required alias into a proof.
        Assert.That(await new ProofKernel(session).VerifyAsync(requiredAlias, TestContext.CurrentContext.CancellationToken), Is.TypeOf<UnknownOutcome>());
        var wrongIdentity = Query(factory, assumptions, alias, variables);
        Assert.That(await new ProofKernel(session).VerifyAsync(wrongIdentity, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
        var notDisjoint = Query(factory, assumptions, factory.Unary(IrUnaryOperator.Not, alias), variables);
        Assert.That(await new ProofKernel(session).VerifyAsync(notDisjoint, TestContext.CurrentContext.CancellationToken), Is.TypeOf<UnknownOutcome>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NominalWitnessPreferenceRemainsConservativeBeyondItsTypeGroupLimit(bool fields)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var receiver = factory.CreateVariable("receiver", factory.ObjectType);
        var variables = ImmutableArray.CreateBuilder<IrVarId>();
        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        if (fields)
        { variables.Add(receiver); assumptions.Add(NotNull(factory, receiver)); }
        for (var index = 0; index < 68; index++)
        {
            var type = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "View" + index);
            IrTerm value;
            if (fields)
            {
                var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:view" + index, type, false);
                value = factory.PureOpaque(field, factory.Variable(receiver));
            }
            else
            {
                var variable = factory.CreateVariable("view" + index, type);
                variables.Add(variable);
                value = factory.Variable(variable);
            }
            assumptions.Add(Assume(factory, factory.Binary(IrBinaryOperator.NotEqual, value, factory.Null(type))));
        }
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, assumptions.ToImmutable(), factory.Boolean(false), variables.ToImmutable());
        // A capped candidate search can abstain, but cannot prove this false
        // claim about a feasible entry with 68 independent non-null views.
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.Not.TypeOf<ProvenOutcome>());
        var invariant = Query(factory, query.Assumptions, query.Assumptions[0].Predicate, query.ModelVariables);
        Assert.That(await new ProofKernel(session).VerifyAsync(invariant, TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
    }

    [Test]
    public async Task ObservedFieldWitnessesAreNotCrowdedOutByRepeatedVariableTypes()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var repeated = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Repeated");
        var variables = ImmutableArray.CreateBuilder<IrVarId>();
        for (var index = 0; index < 80; index++)
        { variables.Add(factory.CreateVariable("earlier" + index, repeated)); }
        var receiver = factory.CreateVariable("receiver", factory.ObjectType);
        variables.Add(receiver);
        var firstType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "First");
        var secondType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Second");
        var firstField = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:first", firstType, false);
        var secondField = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:second", secondType, false);
        var first = factory.PureOpaque(firstField, factory.Variable(receiver));
        var second = factory.PureOpaque(secondField, factory.Variable(receiver));
        var assumptions = ImmutableArray.Create(NotNull(factory, receiver),
            Assume(factory, factory.Binary(IrBinaryOperator.NotEqual, first, factory.Null(firstType))),
            Assume(factory, factory.Binary(IrBinaryOperator.NotEqual, second, factory.Null(secondType))));
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, assumptions, factory.Boolean(false), variables.ToImmutable());
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
    }

    [Test]
    public async Task EveryRepeatedNominalViewRetainsItsPreferredMembership()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var repeated = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Repeated");
        var rare = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Rare");
        var variables = ImmutableArray.CreateBuilder<IrVarId>();
        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        for (var index = 0; index < 96; index++)
        {
            var variable = factory.CreateVariable("view" + index, index == 95 ? rare : repeated);
            variables.Add(variable);
            assumptions.Add(NotNull(factory, variable));
        }
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var query = Query(factory, assumptions.ToImmutable(), factory.Boolean(false), variables.ToImmutable());
        var result = await session.CheckAsync(query, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Status, Is.EqualTo(BackendCheckStatus.Satisfiable));
        AssertAssumptions(factory, query, result.Model!.Assignments);
        Assert.That(await new ProofKernel(session).VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
    }
    [Test]
    public void BuiltinOnlyInputsDoNotCreateNominalMembershipConstraints()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variables = ImmutableArray.Create(factory.CreateVariable("integer", factory.IntegerType),
            factory.CreateVariable("text", factory.StringType), factory.CreateVariable("object", factory.ObjectType),
            factory.CreateVariable("array", factory.GetOrCreateSequenceType(factory.IntegerType)));
        var query = Query(factory, [], factory.Boolean(false), variables);
        using var context = new Context();
        using var owner = new Z3ExpressionOwner();
        var encoder = new BvEncoder(context, factory, owner);
        try
        {
            Assert.That(encoder.NominalReferenceBound(query,
                new SmtQueryResourceMeter(100, TestContext.CurrentContext.CancellationToken)), Is.Null);
        }
        finally
        { encoder.DisposeReferences(); }
    }
    private static Assumption NotNull(IrFactory factory, IrVarId variable)
    {
        if (variable.IsDefault)
        {
            throw new System.ArgumentOutOfRangeException(nameof(variable));
        }
        System.ArgumentNullException.ThrowIfNull(factory);
        return Assume(factory, factory.Binary(IrBinaryOperator.NotEqual,
            factory.Variable(variable), factory.Null(factory.GetVariableInfo(variable).Type)));
    }

    private static Assumption Assume(IrFactory factory, IrTerm predicate)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(predicate);
        return new(factory, predicate, new LoweredJustification(factory.CreateOperation()));
    }

    private static IrTerm Equal(IrFactory factory, IrTerm left, IrTerm right)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(left);
        System.ArgumentNullException.ThrowIfNull(right);
        return factory.Binary(IrBinaryOperator.Equal, left, right);
    }

    private static VerificationQuery Query(IrFactory factory, IEnumerable<Assumption> assumptions,
        IrTerm goal, ImmutableArray<IrVarId> variables)
    {

        System.ArgumentNullException.ThrowIfNull(assumptions);
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(goal);
        if (variables.IsDefault)
        {
            throw new System.ArgumentException("Array must be initialized.", nameof(variables));
        }
        return new(factory, assumptions, new Goal(factory, goal, ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), variables);
    }

    private static void AssertAssumptions(IrFactory factory, VerificationQuery query, IReadOnlyDictionary<IrVarId, IrValue> model)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        System.ArgumentNullException.ThrowIfNull(model);
        System.ArgumentNullException.ThrowIfNull(query);
        var interpreter = new IrInterpreter(factory);
        foreach (var assumption in query.Assumptions)
        {
            var value = interpreter.Evaluate(assumption.Predicate, model);
            Assert.That(value.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(value.Value!.Boolean, Is.True);
        }
    }
}
