using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrKernelTests
{
    [Test]
    public void FactoryInternsStringsTypesMembersAndLiteralTerms()
    {
        var factory = new IrFactory();
        var firstString = factory.InternString("value");
        var typeIdentity = factory.CreateIdentity();
        var memberIdentity = factory.CreateIdentity();
        var firstType = factory.GetOrCreateReferenceType(
            typeIdentity,
            "Widget");
        var firstSequence = factory.GetOrCreateSequenceType(firstType);
        var firstMember = factory.GetOrCreateMember(
            memberIdentity,
            firstType,
            "Create",
            firstSequence,
            isStatic: true,
            factory.IntegerType);

        Assert.That(factory.InternString("value"), Is.EqualTo(firstString));
        Assert.That(
            factory.GetOrCreateReferenceType(typeIdentity, "Widget"),
            Is.EqualTo(firstType));
        Assert.That(
            factory.GetOrCreateSequenceType(firstType),
            Is.EqualTo(firstSequence));
        Assert.That(
            factory.GetOrCreateMember(
                memberIdentity,
                firstType,
                "Create",
                firstSequence,
                isStatic: true,
                factory.IntegerType),
            Is.EqualTo(firstMember));
        Assert.That(factory.Integer(42), Is.SameAs(factory.Integer(42)));
        Assert.That(factory.String("same"), Is.SameAs(factory.String("same")));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.InternExternalIdentity(
                "semantic-key",
                StringComparer.Ordinal)));
    }

    [Test]
    public void FactoryRejectsIllFormedStringValues()
    {
        var factory = new IrFactory();

        Assert.That(
            (Action)(() => factory.CreateStringValue("\uD800")),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void SemanticTermCombinatorsRejectNonBooleanSingletons()
    {
        var factory = new IrFactory();

        Action conjoin = () => IrSemanticTerms.Conjoin(
            factory, [factory.Integer(1)]);
        Action disjoin = () => IrSemanticTerms.Disjoin(
            factory, [factory.Integer(1)]);
        Assert.Throws<ArgumentException>(conjoin);
        Assert.Throws<ArgumentException>(disjoin);
        Assert.That(
            IrSemanticTerms.Conjoin(factory, [factory.Boolean(true)]),
            Is.SameAs(factory.Boolean(true)));
    }

    [Test]
    public void ConstrainSuccessfulEvaluationValidatesSortAndOwnership()
    {
        var factory = new IrFactory();
        var foreign = new IrFactory();

        Assert.Throws<ArgumentException>(
            (Action)(() => IrSemanticTerms.ConstrainSuccessfulEvaluation(
                factory,
                factory.Integer(1),
                factory.Boolean(true))));
        Assert.Throws<ArgumentException>(
            (Action)(() => IrSemanticTerms.ConstrainSuccessfulEvaluation(
                factory,
                foreign.Boolean(true),
                factory.Boolean(true))));
        Assert.Throws<ArgumentException>(
            (Action)(() => IrSemanticTerms.ConstrainSuccessfulEvaluation(
                factory,
                factory.Boolean(true),
                foreign.Boolean(true))));

        Assert.That(
            IrSemanticTerms.ConstrainSuccessfulEvaluation(
                factory,
                factory.Boolean(true),
                factory.Integer(1)),
            Is.SameAs(factory.Boolean(true)));
    }

    [Test]
    public void MemberInterningIncludesTheMemberName()
    {
        var factory = new IrFactory();
        var identity = factory.CreateIdentity();
        var type = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(), "Widget");

        var first = factory.GetOrCreateMember(
            identity, type, "First", factory.IntegerType, isStatic: true);
        var second = factory.GetOrCreateMember(
            identity, type, "Second", factory.IntegerType, isStatic: true);

        Assert.That(second, Is.Not.EqualTo(first));
        Assert.That(factory.GetString(factory.GetMemberInfo(first).Name), Is.EqualTo("First"));
        Assert.That(factory.GetString(factory.GetMemberInfo(second).Name), Is.EqualTo("Second"));
    }

    [Test]
    public void TypeInterningDoesNotRetainDiscardedNames()
    {
        var factory = new IrFactory();
        var identity = factory.CreateIdentity();
        var first = factory.GetOrCreateReferenceType(identity, "Widget");
        var second = factory.GetOrCreateReferenceType(identity, "Gadget");

        Assert.That(second, Is.EqualTo(first));
        Assert.That(
            factory.GetString(factory.GetTypeInfo(second).Name),
            Is.EqualTo("Widget"));
        var next = factory.InternString("next");
        Assert.That(
            next.Value,
            Is.EqualTo(factory.InternString("Widget").Value + 1));

        var sequenceIdentity = factory.CreateIdentity();
        var sequence = factory.GetOrCreateSequenceType(
            sequenceIdentity,
            factory.IntegerType,
            "Numbers");
        Assert.That(
            factory.GetOrCreateSequenceType(
                sequenceIdentity,
                factory.IntegerType,
                "OtherNumbers"),
            Is.EqualTo(sequence));
        var afterSequence = factory.InternString("after-sequence");
        Assert.That(
            afterSequence.Value,
            Is.EqualTo(factory.InternString("Numbers").Value + 1));
    }

    [Test]
    public void StructuralTermsAreReferenceIdenticalAndConstantsFoldCentrally()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var first = factory.Binary(
            IrBinaryOperator.Add,
            factory.Variable(variable),
            factory.Integer(1));
        var second = factory.Binary(
            IrBinaryOperator.Add,
            factory.Variable(variable),
            factory.Integer(1));

        Assert.That(second, Is.SameAs(first));
        Assert.That(
            factory.Binary(
                IrBinaryOperator.Add,
                factory.Integer(2),
                factory.Integer(3)),
            Is.SameAs(factory.Integer(5)));
        Assert.That(
            factory.Binary(
                IrBinaryOperator.StringConcat,
                factory.String("sharp"),
                factory.String("proof")),
            Is.SameAs(factory.String("sharpproof")));
        Assert.That(
            factory.Unary(IrUnaryOperator.Not, factory.Boolean(false)),
            Is.SameAs(factory.Boolean(true)));
    }

    [Test]
    public void EqualityIdentityDoesNotEraseOperandEvaluation()
    {
        var factory = new IrFactory();
        var divisor =
            factory.CreateVariable("divisor", factory.IntegerType);
        var quotient = factory.Binary(
            IrBinaryOperator.Divide,
            factory.Integer(1),
            factory.Variable(divisor));
        var equal = factory.Binary(
            IrBinaryOperator.Equal,
            quotient,
            quotient);
        var notEqual = factory.Binary(
            IrBinaryOperator.NotEqual,
            quotient,
            quotient);
        var variables = new Dictionary<IrVarId, IrValue>
        {
            [divisor] = factory.CreateIntegerValue(0)
        };
        var interpreter = new IrInterpreter(factory);

        var equalResult = interpreter.Evaluate(equal, variables);
        var notEqualResult = interpreter.Evaluate(notEqual, variables);

        Assert.That(equal, Is.TypeOf<IrBinaryTerm>());
        Assert.That(notEqual, Is.TypeOf<IrBinaryTerm>());
        Assert.That(
            equalResult.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.DivideByZero));
        Assert.That(
            notEqualResult.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.DivideByZero));
    }

    [Test]
    public void InterpreterDoesNotInventIdentityForSequenceEquality()
    {
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var left = factory.CreateVariable("left", sequenceType);
        var right = factory.CreateVariable("right", sequenceType);
        var equality = factory.Binary(
            IrBinaryOperator.Equal,
            factory.Variable(left),
            factory.Variable(right));
        var values = new Dictionary<IrVarId, IrValue>
        {
            [left] = factory.CreateSequenceValue(sequenceType, [factory.CreateIntegerValue(1)]),
            [right] = factory.CreateSequenceValue(sequenceType, [factory.CreateIntegerValue(1)])
        };

        var result = new IrInterpreter(factory).Evaluate(equality, values);

        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(result.Unsupported!.Reason,
            Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
        Assert.That(result.Unsupported.Detail,
            Does.Contain("compatible runtime kinds"));
    }

    [Test]
    public void IdenticalConditionalBranchesDoNotEraseGuardEvaluation()
    {
        var factory = new IrFactory();
        var divisor =
            factory.CreateVariable("divisor", factory.IntegerType);
        var condition = factory.Binary(
            IrBinaryOperator.GreaterThan,
            factory.Binary(
                IrBinaryOperator.Divide,
                factory.Integer(1),
                factory.Variable(divisor)),
            factory.Integer(0));
        var conditional = factory.Conditional(
            condition,
            factory.Integer(7),
            factory.Integer(7));

        var result = new IrInterpreter(factory).Evaluate(
            conditional,
            new Dictionary<IrVarId, IrValue>
            {
                [divisor] = factory.CreateIntegerValue(0)
            });

        Assert.That(conditional, Is.TypeOf<IrConditionalTerm>());
        Assert.That(
            result.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.DivideByZero));
    }

    [Test]
    public void InterpreterMemoizesSharedDagOnlyWithinOneEnvironment()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        IrTerm term = factory.Variable(variable);
        for (var depth = 0; depth < 60; depth++)
        {
            term = factory.Binary(IrBinaryOperator.Add, term, term);
        }

        var interpreter = new IrInterpreter(factory);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var first = interpreter.Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [variable] = factory.CreateIntegerValue(1)
            },
            timeout.Token);
        var second = interpreter.Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [variable] = factory.CreateIntegerValue(2)
            },
            timeout.Token);

        Assert.That(first.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(first.Value!.Integer, Is.EqualTo(1L << 60));
        Assert.That(second.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(second.Value!.Integer, Is.EqualTo(1L << 61));
    }

    [Test]
    public void InterpreterSessionSharesMemoAcrossReplayRootsAndModelsStaySeparate()
    {
        var factory = new IrFactory();
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var shared = factory.Binary(
            IrBinaryOperator.Add,
            factory.Variable(variable),
            factory.Integer(1));
        var outer = factory.Binary(
            IrBinaryOperator.Add,
            shared,
            factory.Integer(1));

        var firstModel = new Dictionary<IrVarId, IrValue>
        {
            [variable] = factory.CreateIntegerValue(1)
        };
        var firstSession = new IrInterpreter(factory).CreateSession(firstModel);
        var outerResult = firstSession.Evaluate(outer);
        var sharedResult = firstSession.Evaluate(shared);

        Assert.That(outerResult.Value!.Integer, Is.EqualTo(3));
        Assert.That(sharedResult.Value!.Integer, Is.EqualTo(2));
        Assert.That(
            ReferenceEquals(sharedResult, firstSession.Evaluate(shared)),
            Is.True);

        var secondSession = new IrInterpreter(factory).CreateSession(
            new Dictionary<IrVarId, IrValue>
            {
                [variable] = factory.CreateIntegerValue(9)
            });
        Assert.That(secondSession.Evaluate(shared).Value!.Integer, Is.EqualTo(10));
    }

    [Test]
    public void InterpreterHonorsPreCanceledEvaluation()
    {
        var factory = new IrFactory();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(
            (Action)(() => new IrInterpreter(factory).Evaluate(
                factory.Integer(1),
                cancellationToken: cancellation.Token)));
    }

    [Test]
    public void IdentifiersAndTermsAreScopedToTheirFactory()
    {
        var first = new IrFactory();
        var second = new IrFactory();
        var firstVariable =
            first.CreateVariable("value", first.IntegerType);
        var secondVariable =
            second.CreateVariable("value", second.IntegerType);

        Assert.That(first.IntegerType.Value, Is.EqualTo(second.IntegerType.Value));
        Assert.That(first.IntegerType, Is.Not.EqualTo(second.IntegerType));
        Assert.That(firstVariable.Value, Is.EqualTo(secondVariable.Value));
        Assert.That(firstVariable, Is.Not.EqualTo(secondVariable));
        Assert.Throws<ArgumentException>(
            (Action)(() => first.Variable(secondVariable)));
        Assert.Throws<ArgumentException>(
            (Action)(() => first.Binary(
                IrBinaryOperator.Add,
                first.Integer(1),
                second.Integer(2))));
        Assert.Throws<ArgumentException>(
            (Action)(() => new IrInterpreter(first).Evaluate(second.Integer(1))));
        Assert.Throws<ArgumentException>(
            (Action)(() => new IrPrinter(first).Print(second.Integer(1))));
    }

    [Test]
    public void FactoryRejectsIllTypedTermsAtConstruction()
    {
        var factory = new IrFactory();
        var sequenceType =
            factory.GetOrCreateSequenceType(factory.IntegerType);
        var sequence =
            factory.CreateVariable("values", sequenceType);

        Assert.Throws<ArgumentException>(
            (Action)(() => factory.Unary(
                IrUnaryOperator.Not,
                factory.Integer(1))));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.Binary(
                IrBinaryOperator.Add,
                factory.Boolean(true),
                factory.Boolean(false))));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.Conditional(
                factory.Integer(1),
                factory.Integer(2),
                factory.Integer(3))));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.Conditional(
                factory.Boolean(true),
                factory.Integer(2),
                factory.Boolean(false))));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.Null(factory.IntegerType)));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.Length(factory.Integer(1))));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.SequenceAccess(
                factory.Variable(sequence),
                factory.Boolean(false))));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.Cast(
                factory.BooleanType,
                factory.Integer(1))));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.Cast(
                factory.ObjectType,
                factory.Integer(1))));
        Assert.Throws<ArgumentException>(
            (Action)(() => factory.CreateSequenceValue(
                sequenceType,
                [factory.CreateBooleanValue(true)])));
    }

    [Test]
    public void FactoryRejectsInvalidPublicArgumentsAndOperators()
    {
        var factory = new IrFactory();
        var referenceType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Reference");

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<ArgumentNullException>(
                (Action)(() => factory.InternExternalIdentity<object>(
                    null!,
                    EqualityComparer<object>.Default)));
            Assert.Throws<ArgumentNullException>(
                (Action)(() => factory.InternExternalIdentity(
                    new object(),
                    (IEqualityComparer<object>)null!)));
            Assert.Throws<ArgumentNullException>(
                (Action)(() => factory.InternString(null!)));
            Assert.Throws<ArgumentException>(
                (Action)(() => factory.InternString("a\uD800")));
            Assert.Throws<ArgumentNullException>(
                (Action)(() => factory.CreateStringValue(null!)));
            Assert.Throws<ArgumentNullException>(
                (Action)(() =>
                    factory.CreateReferenceValue(referenceType, null!)));
            Assert.Throws<ArgumentException>(
                (Action)(() =>
                    factory.CreateReferenceValue(
                        factory.IntegerType,
                        new object())));
            Assert.Throws<ArgumentNullException>(
                (Action)(() => factory.CreateSequenceValue(
                    factory.GetOrCreateSequenceType(factory.IntegerType),
                    null!)));
            Assert.Throws<ArgumentException>(
                (Action)(() =>
                    factory.CreateSequenceValue(factory.IntegerType, [])));
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => factory.Unary(
                    (IrUnaryOperator)int.MaxValue,
                    factory.Boolean(true))));
            Assert.Throws<ArgumentOutOfRangeException>(
                (Action)(() => factory.Binary(
                    (IrBinaryOperator)int.MaxValue,
                    factory.Integer(1),
                    factory.Integer(2))));
            Assert.Throws<ArgumentNullException>(
                (Action)(() => factory.Unary(
                    IrUnaryOperator.Not,
                    null!)));
            Assert.Throws<ArgumentNullException>(
                (Action)(() => factory.Binary(
                    IrBinaryOperator.Add,
                    null!,
                    factory.Integer(1))));
            Assert.Throws<ArgumentNullException>(
                (Action)(() => factory.Binary(
                    IrBinaryOperator.Add,
                    factory.Integer(1),
                    null!)));
        }
    }

    [Test]
    public void FactoryRejectsMalformedUtf16InAllInternedNames()
    {
        var factory = new IrFactory();
        var identity = factory.CreateIdentity();
        var referenceType = factory.GetOrCreateReferenceType(
            identity,
            "Reference");
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var malformedValues = new[] { "a\uD800", "\uDC00", "\uD800x" };
        foreach (var malformed in malformedValues)
        {
            var actions = new (Action Action, string ParameterName)[]
            {
                (() => factory.GetOrCreateReferenceType(
                    factory.CreateIdentity(), malformed), "displayName"),
                (() => factory.GetOrCreateSequenceType(
                    factory.CreateIdentity(), sequenceType, malformed), "displayName"),
                (() => factory.CreateVariable(malformed, factory.IntegerType), "name"),
                (() => factory.GetOrCreateMember(
                    factory.CreateIdentity(),
                    referenceType,
                    malformed,
                    factory.IntegerType,
                    isStatic: false), "name"),
                (() => factory.CreateOperation(malformed), "description"),
                (() => factory.String(malformed), "value"),
                (() => new IrProgramBuilder(factory).CreateBlock(malformed), "value")
            };

            foreach (var (action, parameterName) in actions)
            {
                var exception = Assert.Throws<ArgumentException>(action);
                Assert.That(exception!.ParamName, Is.EqualTo(parameterName));
            }
        }

        var before = factory.InternString("before");
        var after = factory.InternString("after");
        Assert.That(after.Value, Is.EqualTo(before.Value + 1));
        Assert.That(
            factory.InternString("\uD83D\uDE00").Value,
            Is.EqualTo(after.Value + 1));
    }

    [Test]
    public void InstanceMembersRejectMismatchedReceiverTypesEverywhere()
    {
        var factory = new IrFactory();
        var boxType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Box");
        var otherType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Other");
        var other = factory.CreateVariable("other", otherType);
        var receiver = factory.Variable(other);
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            boxType,
            "Read",
            factory.IntegerType,
            isStatic: false);
        var operation = factory.CreateOperation("read");
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");

        Assert.Throws<ArgumentException>(
            (Action)(() => factory.PureOpaque(member, receiver)));
        Assert.Throws<ArgumentException>(
            (Action)(() => builder.MemberLocation(member, receiver)));
        Assert.Throws<ArgumentException>(
            (Action)(() => builder.Call(
                entry,
                operation,
                target: null,
                member,
                receiver)));
    }

    [Test]
    public void PureOpaqueCallsHashConsButImpureOccurrencesRemainDistinct()
    {
        var factory = new IrFactory();
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "Read",
            factory.IntegerType,
            isStatic: true,
            factory.IntegerType);
        var argument = factory.Integer(3);

        var firstPure =
            factory.PureOpaque(member, receiver: null, argument);
        var secondPure =
            factory.PureOpaque(member, receiver: null, argument);
        var firstOperation = factory.CreateOperation("first call");
        var secondOperation = factory.CreateOperation("second call");
        var firstImpure = factory.ImpureOpaque(
            firstOperation,
            member,
            receiver: null,
            argument);
        var secondImpure = factory.ImpureOpaque(
            secondOperation,
            member,
            receiver: null,
            argument);

        Assert.That(secondPure, Is.SameAs(firstPure));
        Assert.That(firstPure.Purity, Is.EqualTo(IrOpaquePurity.Pure));
        Assert.That(secondImpure, Is.Not.SameAs(firstImpure));
        Assert.That(firstImpure.Operation, Is.EqualTo(firstOperation));
        Assert.That(secondImpure.Operation, Is.EqualTo(secondOperation));
    }

    [Test]
    public void VariablesWithTheSameNameRemainDistinctDuringSubstitution()
    {
        var factory = new IrFactory();
        var first =
            factory.CreateVariable("value", factory.IntegerType);
        var second =
            factory.CreateVariable("value", factory.IntegerType);
        var root = factory.Binary(
            IrBinaryOperator.Add,
            factory.Variable(first),
            factory.Variable(second));

        var substituted = IrSubstitution.Substitute(
            factory,
            root,
            first,
            factory.Integer(5));
        var evaluation = new IrInterpreter(factory).Evaluate(
            substituted,
            new Dictionary<IrVarId, IrValue>
            {
                [second] = factory.CreateIntegerValue(7)
            });

        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(
            substituted,
            Is.SameAs(factory.Binary(
                IrBinaryOperator.Add,
                factory.Integer(5),
                factory.Variable(second))));
        Assert.That(evaluation.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(evaluation.Value!.Integer, Is.EqualTo(12));
    }

    [Test]
    public void SubstitutionRewritesOpaqueOperandsAndPreservesOperationIdentity()
    {
        var factory = new IrFactory();
        var receiverType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Box");
        var receiver =
            factory.CreateVariable("box", receiverType);
        var argument =
            factory.CreateVariable("value", factory.IntegerType);
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            receiverType,
            "Read",
            factory.IntegerType,
            isStatic: false,
            factory.IntegerType);
        var operation = factory.CreateOperation("call");
        var root = factory.ImpureOpaque(
            operation,
            member,
            factory.Variable(receiver),
            factory.Variable(argument));

        var substituted = IrSubstitution.Substitute(
            factory,
            root,
            new Dictionary<IrVarId, IrTerm>
            {
                [receiver] = factory.Null(receiverType),
                [argument] = factory.Integer(4)
            });

        Assert.That(substituted, Is.TypeOf<IrOpaqueTerm>());
        var opaque = (IrOpaqueTerm)substituted;
        Assert.That(opaque.Purity, Is.EqualTo(IrOpaquePurity.Impure));
        Assert.That(opaque.Operation, Is.EqualTo(operation));
        Assert.That(opaque.Receiver, Is.SameAs(factory.Null(receiverType)));
        Assert.That(opaque.Arguments.Single(), Is.SameAs(factory.Integer(4)));
    }

    [Test]
    public void SubstitutionIsSimultaneousAndAnEmptyMapPreservesIdentity()
    {
        var factory = new IrFactory();
        var first =
            factory.CreateVariable("first", factory.IntegerType);
        var second =
            factory.CreateVariable("second", factory.IntegerType);
        var root = factory.Binary(
            IrBinaryOperator.Add,
            factory.Variable(first),
            factory.Variable(second));

        var unchanged = IrSubstitution.Substitute(
            factory,
            root,
            new Dictionary<IrVarId, IrTerm>());
        var substituted = IrSubstitution.Substitute(
            factory,
            root,
            new Dictionary<IrVarId, IrTerm>
            {
                [first] = factory.Variable(second),
                [second] = factory.Integer(1)
            });

        Assert.That(unchanged, Is.SameAs(root));
        Assert.That(
            substituted,
            Is.SameAs(factory.Binary(
                IrBinaryOperator.Add,
                factory.Variable(second),
                factory.Integer(1))));
    }

    [Test]
    public void SubstitutionRejectsWrongTypesAndForeignTerms()
    {
        var factory = new IrFactory();
        var foreign = new IrFactory();
        var variable =
            factory.CreateVariable("value", factory.IntegerType);

        Assert.Throws<ArgumentException>(
            (Action)(() => IrSubstitution.Substitute(
                factory,
                factory.Variable(variable),
                variable,
                factory.Boolean(true))));
        Assert.Throws<ArgumentException>(
            (Action)(() => IrSubstitution.Substitute(
                factory,
                foreign.Integer(1),
                variable,
                factory.Integer(2))));
        Assert.Throws<ArgumentException>(
            (Action)(() => IrSubstitution.Substitute(
                factory,
                factory.Variable(variable),
                variable,
                foreign.Integer(2))));
    }

    [Test]
    public void PrinterIsCanonicalAcrossSourceNamesAndEscapesStrings()
    {
        var firstFactory = new IrFactory();
        var first = CreatePrintableTerm(
            firstFactory,
            "source flag",
            "source text");
        var secondFactory = new IrFactory();
        var second = CreatePrintableTerm(
            secondFactory,
            "renamed flag",
            "renamed text");

        var firstText = new IrPrinter(firstFactory).Print(first);
        var secondText = new IrPrinter(secondFactory).Print(second);

        Assert.That(
            firstText,
            Is.EqualTo(
                "(v0 ? (\"line\\n\\\"x\\\"\" ++ v1) : \"fallback\")"));
        Assert.That(secondText, Is.EqualTo(firstText));
    }

    [TestCase(IrUnaryOperator.Not, IrTypeKind.Boolean, "(!v0)")]
    [TestCase(IrUnaryOperator.Negate, IrTypeKind.Integer, "(-v0)")]
    public void UnaryOperatorMetadataPreservesTypesKeysAndTokens(
        IrUnaryOperator @operator,
        IrTypeKind operandKind,
        string expectedText)
    {
        var factory = new IrFactory();
        var type = operandKind == IrTypeKind.Boolean
            ? factory.BooleanType
            : factory.IntegerType;
        var operand = factory.Variable(factory.CreateVariable("value", type));

        var first = factory.Unary(@operator, operand);
        var second = factory.Unary(@operator, operand);

        Assert.That(second, Is.SameAs(first));
        Assert.That(new IrPrinter(factory).Print(first), Is.EqualTo(expectedText));
    }

    [TestCase(IrBinaryOperator.Add, IrTypeKind.Integer, "+")]
    [TestCase(IrBinaryOperator.Subtract, IrTypeKind.Integer, "-")]
    [TestCase(IrBinaryOperator.Multiply, IrTypeKind.Integer, "*")]
    [TestCase(IrBinaryOperator.Divide, IrTypeKind.Integer, "/")]
    [TestCase(IrBinaryOperator.Remainder, IrTypeKind.Integer, "%")]
    [TestCase(IrBinaryOperator.AndAlso, IrTypeKind.Boolean, "&&")]
    [TestCase(IrBinaryOperator.OrElse, IrTypeKind.Boolean, "||")]
    [TestCase(IrBinaryOperator.Equal, IrTypeKind.Integer, "==")]
    [TestCase(IrBinaryOperator.NotEqual, IrTypeKind.Integer, "!=")]
    [TestCase(IrBinaryOperator.LessThan, IrTypeKind.Integer, "<")]
    [TestCase(IrBinaryOperator.LessThanOrEqual, IrTypeKind.Integer, "<=")]
    [TestCase(IrBinaryOperator.GreaterThan, IrTypeKind.Integer, ">")]
    [TestCase(IrBinaryOperator.GreaterThanOrEqual, IrTypeKind.Integer, ">=")]
    [TestCase(IrBinaryOperator.StringConcat, IrTypeKind.String, "++")]
    public void BinaryOperatorMetadataPreservesTypesKeysAndTokens(
        IrBinaryOperator @operator,
        IrTypeKind operandKind,
        string token)
    {
        var factory = new IrFactory();
        var type = operandKind switch
        {
            IrTypeKind.Boolean => factory.BooleanType,
            IrTypeKind.Integer => factory.IntegerType,
            IrTypeKind.String => factory.StringType,
            _ => throw new AssertionException("Unexpected test operand kind.")
        };
        var left = factory.Variable(factory.CreateVariable("left", type));
        var right = factory.Variable(factory.CreateVariable("right", type));

        var first = factory.Binary(@operator, left, right);
        var second = factory.Binary(@operator, left, right);

        Assert.That(second, Is.SameAs(first));
        Assert.That(
            new IrPrinter(factory).Print(first),
            Is.EqualTo("(v0 " + token + " v1)"));
    }

    [Test]
    public void BinaryOperatorKeysAreDistinctWithinEachTypedDomain()
    {
        var factory = new IrFactory();
        var integers = new[] {
            factory.Variable(factory.CreateVariable("left", factory.IntegerType)),
            factory.Variable(factory.CreateVariable("right", factory.IntegerType))
        };
        var booleans = new[] {
            factory.Variable(factory.CreateVariable("first", factory.BooleanType)),
            factory.Variable(factory.CreateVariable("second", factory.BooleanType))
        };

        AssertDistinct(
            [IrBinaryOperator.Add, IrBinaryOperator.Subtract,
                IrBinaryOperator.Multiply, IrBinaryOperator.Divide,
                IrBinaryOperator.Remainder],
            integers);
        AssertDistinct(
            [IrBinaryOperator.Equal, IrBinaryOperator.NotEqual,
                IrBinaryOperator.LessThan, IrBinaryOperator.LessThanOrEqual,
                IrBinaryOperator.GreaterThan, IrBinaryOperator.GreaterThanOrEqual],
            integers);
        AssertDistinct(
            [IrBinaryOperator.AndAlso, IrBinaryOperator.OrElse],
            booleans);

        void AssertDistinct(
            IrBinaryOperator[] operators,
            IrTerm[] operands)
        {
            var terms = operators
                .Select(value => factory.Binary(value, operands[0], operands[1]))
                .Cast<IrBinaryTerm>()
                .ToArray();
            Assert.That(terms, Is.Unique);
            Assert.That(
                terms.Select(static term => term.Operator),
                Is.EqualTo(operators));
        }
    }

    [Test]
    public void InterpreterPreservesShortCircuitEvaluation()
    {
        var factory = new IrFactory();
        var enabled =
            factory.CreateVariable("enabled", factory.BooleanType);
        var divisor =
            factory.CreateVariable("divisor", factory.IntegerType);
        var positiveQuotient = factory.Binary(
            IrBinaryOperator.GreaterThan,
            factory.Binary(
                IrBinaryOperator.Divide,
                factory.Integer(10),
                factory.Variable(divisor)),
            factory.Integer(0));
        var term = factory.Binary(
            IrBinaryOperator.AndAlso,
            factory.Variable(enabled),
            positiveQuotient);
        var interpreter = new IrInterpreter(factory);

        var skipped = interpreter.Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [enabled] = factory.CreateBooleanValue(false),
                [divisor] = factory.CreateIntegerValue(0)
            });
        var evaluated = interpreter.Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [enabled] = factory.CreateBooleanValue(true),
                [divisor] = factory.CreateIntegerValue(2)
            });

        Assert.That(skipped.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(skipped.Value!.Boolean, Is.False);
        Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(evaluated.Value!.Boolean, Is.True);
    }

    [Test]
    public void InterpreterClassifiesArithmeticAndEnvironmentFailures()
    {
        var factory = new IrFactory();
        var dividend =
            factory.CreateVariable("dividend", factory.IntegerType);
        var divisor =
            factory.CreateVariable("divisor", factory.IntegerType);
        var division = factory.Binary(
            IrBinaryOperator.Divide,
            factory.Variable(dividend),
            factory.Variable(divisor));
        var interpreter = new IrInterpreter(factory);

        var divideByZero = interpreter.Evaluate(
            division,
            new Dictionary<IrVarId, IrValue>
            {
                [dividend] = factory.CreateIntegerValue(1),
                [divisor] = factory.CreateIntegerValue(0)
            });
        var overflow = interpreter.Evaluate(
            division,
            new Dictionary<IrVarId, IrValue>
            {
                [dividend] = factory.CreateIntegerValue(long.MinValue),
                [divisor] = factory.CreateIntegerValue(-1)
            });
        var remainder = interpreter.Evaluate(
            factory.Binary(
                IrBinaryOperator.Remainder,
                factory.Variable(dividend),
                factory.Variable(divisor)),
            new Dictionary<IrVarId, IrValue>
            {
                [dividend] = factory.CreateIntegerValue(long.MinValue),
                [divisor] = factory.CreateIntegerValue(-1)
            });
        var missing = interpreter.Evaluate(
            division,
            new Dictionary<IrVarId, IrValue>
            {
                [dividend] = factory.CreateIntegerValue(1)
            });
        var invalid = interpreter.Evaluate(
            factory.Variable(dividend),
            new Dictionary<IrVarId, IrValue>
            {
                [dividend] = factory.CreateBooleanValue(true)
            });

        Assert.That(
            divideByZero.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.DivideByZero));
        Assert.That(
            overflow.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.Overflow));
        Assert.That(
            remainder.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.Overflow));
        Assert.That(
            missing.Unsupported!.Reason,
            Is.EqualTo(IrUnsupportedReason.MissingVariable));
        Assert.That(
            invalid.Unsupported!.Reason,
            Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
    }

    [TestCase(IrBinaryOperator.Add, "Integer arithmetic requires integer values.")]
    [TestCase(IrBinaryOperator.LessThan, "Integer comparison requires integer values.")]
    [TestCase(IrBinaryOperator.LessThanOrEqual, "Integer comparison requires integer values.")]
    [TestCase(IrBinaryOperator.GreaterThan, "Integer comparison requires integer values.")]
    [TestCase(IrBinaryOperator.GreaterThanOrEqual, "Integer comparison requires integer values.")]
    public void InterpreterRejectsNonIntegerRuntimeKindsForIntegerBinaryOperators(
        IrBinaryOperator @operator, string expectedDetail)
    {
        var factory = new IrFactory();
        var left = factory.CreateVariable("left", factory.IntegerType);
        var right = factory.CreateVariable("right", factory.IntegerType);
        var term = factory.Binary(
            @operator,
            factory.Variable(left),
            factory.Variable(right));
        var wrongKind = new IrValue(
            factory.IntegerType,
            IrValueKind.Boolean,
            true);

        var result = new IrInterpreter(factory).Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [left] = wrongKind,
                [right] = factory.CreateIntegerValue(1)
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Status,
                Is.EqualTo(IrEvaluationStatus.Unsupported));
            Assert.That(
                result.Unsupported!.Reason,
                Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
            Assert.That(result.Unsupported.Detail, Is.EqualTo(expectedDetail));
        }
    }

    [Test]
    public void InterpreterEvaluatesSequenceLengthAndAccessFailures()
    {
        var factory = new IrFactory();
        var sequenceType =
            factory.GetOrCreateSequenceType(factory.IntegerType);
        var sequence =
            factory.CreateVariable("values", sequenceType);
        var index =
            factory.CreateVariable("index", factory.IntegerType);
        var access = factory.SequenceAccess(
            factory.Variable(sequence),
            factory.Variable(index));
        var interpreter = new IrInterpreter(factory);
        var values = factory.CreateSequenceValue(
            sequenceType,
            [factory.CreateIntegerValue(4), factory.CreateIntegerValue(8)]);

        var found = interpreter.Evaluate(
            access,
            new Dictionary<IrVarId, IrValue>
            {
                [sequence] = values,
                [index] = factory.CreateIntegerValue(1)
            });
        var outside = interpreter.Evaluate(
            access,
            new Dictionary<IrVarId, IrValue>
            {
                [sequence] = values,
                [index] = factory.CreateIntegerValue(2)
            });
        var nullAccess = interpreter.Evaluate(
            access,
            new Dictionary<IrVarId, IrValue>
            {
                [sequence] = factory.CreateNullValue(sequenceType),
                [index] = factory.CreateIntegerValue(0)
            });
        var failingIndex = interpreter.Evaluate(
            factory.SequenceAccess(
                factory.Null(sequenceType),
                factory.Binary(
                    IrBinaryOperator.Divide,
                    factory.Integer(1),
                    factory.Integer(0))));
        var length = interpreter.Evaluate(
            factory.Length(factory.Variable(sequence)),
            new Dictionary<IrVarId, IrValue>
            {
                [sequence] = values
            });

        Assert.That(found.Value!.Integer, Is.EqualTo(8));
        Assert.That(
            outside.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.IndexOutOfRange));
        Assert.That(
            nullAccess.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.NullReference));
        Assert.That(
            failingIndex.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.DivideByZero));
        Assert.That(length.Value!.Integer, Is.EqualTo(2));
    }

    [Test]
    public void InterpreterClassifiesReferenceCastsWithoutInventingTypeRelations()
    {
        var factory = new IrFactory();
        var sourceType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Source");
        var targetType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Target");
        var source = factory.CreateVariable("source", sourceType);
        var cast = factory.Cast(
            targetType,
            factory.Variable(source));
        var interpreter = new IrInterpreter(factory);

        var unsupported = interpreter.Evaluate(
            cast,
            new Dictionary<IrVarId, IrValue>
            {
                [source] = factory.CreateReferenceValue(
                    sourceType,
                    new object())
            });
        var nullCast = interpreter.Evaluate(
            factory.Cast(targetType, factory.Null(sourceType)));

        Assert.That(
            unsupported.Unsupported!.Reason,
            Is.EqualTo(IrUnsupportedReason.UnsupportedCast));
        Assert.That(nullCast.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(nullCast.Value!.Kind, Is.EqualTo(IrValueKind.Null));
        Assert.That(nullCast.Value.Type, Is.EqualTo(targetType));
    }

    [Test]
    public void FactoryRejectsNullCastsToNonNullableTypes()
    {
        var factory = new IrFactory();

        Action castNull = () =>
        {
            _ = factory.Cast(
                factory.IntegerType,
                factory.Null(factory.StringType));
        };
        Assert.That(castNull, Throws.ArgumentException);
    }

    [Test]
    public void InterpreterUsesConcreteStringReferenceTypeForStringCasts()
    {
        var factory = new IrFactory();
        var source = factory.CreateVariable("source", factory.ObjectType);
        var cast = factory.Cast(
            factory.StringType,
            factory.Variable(source));
        var interpreter = new IrInterpreter(factory);

        var succeeded = interpreter.Evaluate(
            cast,
            new Dictionary<IrVarId, IrValue>
            {
                [source] = factory.CreateReferenceValue(
                    factory.ObjectType,
                    "sharp")
            });
        var failed = interpreter.Evaluate(
            cast,
            new Dictionary<IrVarId, IrValue>
            {
                [source] = factory.CreateReferenceValue(
                    factory.ObjectType,
                    new object())
            });
        var malformed = interpreter.Evaluate(
            cast,
            new Dictionary<IrVarId, IrValue>
            {
                [source] = factory.CreateReferenceValue(
                    factory.ObjectType,
                    "a\uD800")
            });
        var concatNull = interpreter.Evaluate(
            factory.Binary(
                IrBinaryOperator.StringConcat,
                factory.Null(factory.StringType),
                factory.String("proof")));

        Assert.That(succeeded.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(succeeded.Value!.String, Is.EqualTo("sharp"));
        Assert.That(failed.Status, Is.EqualTo(IrEvaluationStatus.Exception));
        Assert.That(malformed.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(
            malformed.Unsupported!.Reason,
            Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
        Assert.That(
            failed.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.InvalidCast));
        Assert.That(concatNull.Value!.String, Is.EqualTo("proof"));
    }

    [Test]
    public void InterpreterEvaluatesOperandsBeforeOpaqueAbstention()
    {
        var factory = new IrFactory();
        var receiverType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Box");
        var receiver =
            factory.CreateVariable("box", receiverType);
        var argument =
            factory.CreateVariable("value", factory.IntegerType);
        var instanceMember = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            receiverType,
            "Read",
            factory.IntegerType,
            isStatic: false,
            factory.IntegerType);
        var term = factory.PureOpaque(
            instanceMember,
            factory.Variable(receiver),
            factory.Variable(argument));
        var failingArgumentTerm = factory.PureOpaque(
            instanceMember,
            factory.Null(receiverType),
            factory.Binary(
                IrBinaryOperator.Divide,
                factory.Integer(1),
                factory.Integer(0)));
        var interpreter = new IrInterpreter(factory);

        var failingArgument = interpreter.Evaluate(failingArgumentTerm);
        var nullReceiver = interpreter.Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [receiver] = factory.CreateNullValue(receiverType),
                [argument] = factory.CreateIntegerValue(1)
            });
        var missingArgument = interpreter.Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [receiver] = factory.CreateReferenceValue(
                    receiverType,
                    new object())
            });
        var opaque = interpreter.Evaluate(
            term,
            new Dictionary<IrVarId, IrValue>
            {
                [receiver] = factory.CreateReferenceValue(
                    receiverType,
                    new object()),
                [argument] = factory.CreateIntegerValue(1)
            });

        Assert.That(
            failingArgument.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.DivideByZero));
        Assert.That(
            nullReceiver.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.NullReference));
        Assert.That(
            missingArgument.Unsupported!.Reason,
            Is.EqualTo(IrUnsupportedReason.MissingVariable));
        Assert.That(
            opaque.Unsupported!.Reason,
            Is.EqualTo(IrUnsupportedReason.OpaqueTerm));
    }

    private static IrTerm CreatePrintableTerm(
        IrFactory factory,
        string flagName,
        string textName)
    {
        var flag = factory.CreateVariable(flagName, factory.BooleanType);
        var text = factory.CreateVariable(textName, factory.StringType);
        return factory.Conditional(
            factory.Variable(flag),
            factory.Binary(
                IrBinaryOperator.StringConcat,
                factory.String("line\n\"x\""),
                factory.Variable(text)),
            factory.String("fallback"));
    }

    [Test]
    public void DeeplyNestedTermsAbstainInsteadOfExhaustingTheStack()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);

        // A variable operand keeps the factory from constant-folding the chain
        // away, so the term really is 512 levels deep.
        var term = (IrTerm)factory.Variable(value);
        for (var index = 0; index < 512; index++)
        {
            term = factory.Binary(
                IrBinaryOperator.Add,
                term,
                factory.Variable(value));
        }

        var environment = new Dictionary<IrVarId, IrValue>
        {
            [value] = factory.CreateIntegerValue(1)
        };
        var result = new IrInterpreter(factory).Evaluate(term, environment);

        // StackOverflowException is uncatchable, so the interpreter has to
        // refuse the term rather than try to evaluate it.
        Assert.That(
            result.Status,
            Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(
            result.Unsupported!.Reason,
            Is.EqualTo(IrUnsupportedReason.UnsupportedOperation));
    }

    [Test]
    public void PrinterRejectsTermsBeyondItsDepthBudget()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var term = (IrTerm)factory.Variable(value);
        for (var index = 0; index < 512; index++)
        {
            term = factory.Binary(
                IrBinaryOperator.Add,
                term,
                factory.Variable(value));
        }

        Assert.Throws<InvalidOperationException>(
            (Action)(() => new IrPrinter(factory).Print(term)));
    }

    [Test]
    public void TermsWithinTheDepthBudgetStillEvaluate()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var term = (IrTerm)factory.Variable(value);
        for (var index = 0; index < 32; index++)
        {
            term = factory.Binary(
                IrBinaryOperator.Add,
                term,
                factory.Variable(value));
        }

        var environment = new Dictionary<IrVarId, IrValue>
        {
            [value] = factory.CreateIntegerValue(1)
        };
        var result = new IrInterpreter(factory).Evaluate(term, environment);

        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(result.Value!.Integer, Is.EqualTo(33L));
    }
}
