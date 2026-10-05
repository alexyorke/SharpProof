using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrTotalExecutionTests
{
    [Test]
    public void AssignmentObservationsRetainApproximationStateAtEachCheckpoint()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var marker = factory.CreateVariable("marker", factory.BooleanType);
        var unknown = factory.CreateVariable("unknown", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var first = builder.Assign(entry, factory.CreateOperation(), marker, factory.Boolean(false));
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, unknown);
        var second = builder.Assign(entry, factory.CreateOperation(), marker, factory.Variable(unknown));
        var third = builder.Assign(entry, factory.CreateOperation(), marker, factory.Boolean(false));
        builder.Throw(entry, factory.CreateOperation(), IrExceptionKind.DivideByZero, exit);
        builder.ExceptionalExit(exit, factory.CreateOperation());
        var observed = new List<(IrAssignInstruction Instruction, bool Value, bool Approximation)>();
        var replay = new IrProgramReplayOptions(_ => factory.CreateBooleanValue(true))
        {
            AssignmentObserver = (instruction, value, approximation) => observed.Add((instruction, value.Boolean, approximation))
        };
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 64, replay);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(result.ConsumedApproximation, Is.True);
        Assert.That(observed, Is.EqualTo(new[] { (first, false, false), (second, true, true), (third, false, true) }));
    }

    [Test]
    public void FailedAssignmentDoesNotEmitAnObservationOrEraseAnEarlierCheckpoint()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var marker = factory.CreateVariable("marker", factory.BooleanType);
        var missing = factory.CreateVariable("missing", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var first = builder.Assign(entry, factory.CreateOperation(), marker, factory.Boolean(false));
        builder.Assign(entry, factory.CreateOperation(), marker, factory.Variable(missing));
        builder.Return(entry, factory.CreateOperation());
        var observed = new List<IrAssignInstruction>();
        var replay = new IrProgramReplayOptions(_ => null)
        {
            AssignmentObserver = (instruction, _, _) => observed.Add(instruction)
        };
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 64, replay);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(observed, Is.EqualTo(new[] { first }));
    }

    [TestCase("allocation", true)]
    [TestCase("lock", true)]
    [TestCase("allocation", false)]
    [TestCase("lock", false)]
    public void EffectPrefixObservationsIncludeOperandApproximation(string kind, bool supplied)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var operand = factory.CreateVariable("operand", kind == "allocation" ? factory.IntegerType : factory.ObjectType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        if (supplied)
        { builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, operand); }
        var site = factory.CreateOperation("effect");
        if (kind == "allocation")
        {
            var sequence = factory.GetOrCreateSequenceType(factory.IntegerType);
            var target = factory.CreateVariable("array", sequence);
            builder.Allocate(entry, site, sequence, target, factory.Variable(operand));
            builder.Return(entry, factory.CreateOperation());
        }
        else
        {
            builder.Lock(entry, site, factory.Variable(operand));
            builder.Return(entry, factory.CreateOperation());
        }
        var observed = new List<(OperationId Site, bool Approximation)>();
        var replay = new IrProgramReplayOptions(_ => kind == "allocation"
            ? factory.CreateIntegerValue(0) : factory.CreateReferenceValue(factory.ObjectType, new object()))
        {
            AllocationPrefixObserver = (instruction, approximation) => observed.Add((instruction.Operation, approximation)),
            LockPrefixObserver = (instruction, approximation) => observed.Add((instruction.Operation, approximation))
        };
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 64, replay);
        Assert.That(result.Status, Is.EqualTo(kind == "allocation" && supplied
            ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Unsupported));
        if (supplied)
        { Assert.That(observed, Is.EqualTo(new[] { (site, true) })); }
        else
        { Assert.That(observed, Is.Empty); }
    }

    [Test]
    public void ArgumentFaultClassificationDoesNotCollapseOtherArgumentExceptions()
    {
        Assert.That(IrExceptionKindFacts.FromException(new ArgumentException()), Is.EqualTo(IrExceptionKind.Argument));
        Assert.That(IrExceptionKindFacts.FromException(new ArgumentNullException()), Is.Null);
        Assert.That(IrExceptionKindFacts.FromException(new ArgumentOutOfRangeException()), Is.Null);
    }

    [Test]
    public void RuntimeEmptyStringConstructionPreservesCanonicalIdentity()
    {
        Assert.That(new string(Array.Empty<char>()), Is.SameAs(string.Empty));
        Assert.That(new string('\0', 0), Is.SameAs(string.Empty));
        Assert.That(string.Concat(null, string.Empty), Is.SameAs(string.Empty));
    }

    [Test]
    public void EmptyArrayTermsRequireTotalOwnedSequenceTypes()
    {
        var total = new IrFactory(IrExecutionSemantics.Total);
        var type = total.GetOrCreateSequenceType(total.IntegerType);
        var empty = total.EmptyArray(type);
        Assert.That(total.EmptyArray(type), Is.SameAs(empty));
        Assert.That(total.CreateEmptyArrayValue(type), Is.SameAs(total.CreateEmptyArrayValue(type)));
        Assert.That(new IrPrinter(total).Print(empty), Does.StartWith("empty("));
        Assert.Throws<ArgumentException>(new Action(() => total.EmptyArray(total.IntegerType)));
        var foreign = new IrFactory(IrExecutionSemantics.Total);
        Assert.Throws<ArgumentException>(new Action(() => total.EmptyArray(foreign.GetOrCreateSequenceType(foreign.IntegerType))));
        var legacy = new IrFactory();
        Assert.Throws<ArgumentException>(new Action(() => legacy.EmptyArray(legacy.GetOrCreateSequenceType(legacy.IntegerType))));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TotalStringEqualityUsesConcreteIdentityWhileLegacyUsesContent(bool alias)
    {
        var first = new string('x', 3);
        var second = alias ? first : new string('x', 3);
        foreach (var semantics in new[] { IrExecutionSemantics.Legacy, IrExecutionSemantics.Total })
        {
            var factory = new IrFactory(semantics);
            var x = factory.CreateVariable("x", factory.StringType);
            var y = factory.CreateVariable("y", factory.StringType);
            var equal = factory.Binary(IrBinaryOperator.Equal, factory.Variable(x), factory.Variable(y));
            var values = new Dictionary<IrVarId, IrValue>
            { [x] = factory.CreateStringValue(first), [y] = factory.CreateStringValue(second) };
            var evaluated = new IrInterpreter(factory).Evaluate(equal, values);
            Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(evaluated.Value!.Boolean, Is.EqualTo(alias || semantics == IrExecutionSemantics.Legacy));
        }
    }

    [Test]
    public void TotalDefaultsUseInt32WhileParameterlessFactoryRetainsLegacy()
    {
        var legacy = new IrFactory();
        var total = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(typeof(IrFactory).GetConstructor(Type.EmptyTypes), Is.Not.Null);
        Assert.That(legacy.GetTypeInfo(legacy.IntegerType).Width, Is.Zero);
        Assert.That(total.IntegerType, Is.EqualTo(total.GetOrCreateIntegerType(32, true)));
        Assert.That(total.Length(total.Null(total.StringType)).Type, Is.EqualTo(total.IntegerType));
        Assert.That(new IrInterpreter(total).Evaluate(total.Length(total.Null(total.StringType))).Value!.Integer, Is.Zero);
    }

    [TestCase(IrBinaryOperator.Divide, 0, 0, 4294967295UL)]
    [TestCase(IrBinaryOperator.Divide, -1, 0, 1UL)]
    [TestCase(IrBinaryOperator.Remainder, -1, 0, 4294967295UL)]
    [TestCase(IrBinaryOperator.Divide, int.MinValue, -1, 2147483648UL)]
    [TestCase(IrBinaryOperator.Remainder, int.MinValue, -1, 0UL)]
    public void TotalArithmeticAndFoldingUseTheSameBitVectorCompletion(
        IrBinaryOperator operation, int left, int right, ulong expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", factory.IntegerType);
        var y = factory.CreateVariable("y", factory.IntegerType);
        var term = factory.Binary(operation, factory.Variable(x), factory.Variable(y));
        var values = ImmutableDictionary<IrVarId, IrValue>.Empty
            .Add(x, factory.CreateIntegerValue(left)).Add(y, factory.CreateIntegerValue(right));
        var interpreted = new IrInterpreter(factory).Evaluate(term, values);
        var folded = (IrIntegerTerm)factory.Binary(operation, factory.Integer(left), factory.Integer(right));
        Assert.That(interpreted.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(interpreted.Value!.IntegerBits, Is.EqualTo(expected));
        Assert.That(folded.Bits, Is.EqualTo(expected));
    }

    [TestCase("unused", false)]
    [TestCase("overwritten", false)]
    [TestCase("read-then-overwritten", true)]
    [TestCase("untaken", false)]
    public void ApproximationIsConsumedOnlyByAnActualRead(string scenario, bool expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", factory.IntegerType);
        var y = factory.CreateVariable("y", factory.IntegerType);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, x);
        if (scenario == "read-then-overwritten")
        {
            builder.Assign(entry, factory.CreateOperation(), y, factory.Variable(x));
            builder.Assign(entry, factory.CreateOperation(), y, factory.Integer(7));
        }
        if (scenario == "overwritten")
        {
            builder.Assign(entry, factory.CreateOperation(), x, factory.Integer(7));
        }
        builder.Return(entry, factory.CreateOperation(), scenario == "untaken"
            ? factory.Conditional(factory.Variable(flag), factory.Variable(x), factory.Integer(7))
            : factory.Integer(7));
        var initial = ImmutableDictionary<IrVarId, IrValue>.Empty.Add(flag, factory.CreateBooleanValue(false));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), initial, 100,
            new IrProgramReplayOptions(_ => factory.CreateIntegerValue(2)));
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ConsumedApproximation, Is.EqualTo(expected));
        Assert.That(result.ApproximationVariables.Contains(x), Is.EqualTo(scenario != "overwritten"));
    }

    [Test]
    public void ThrowPreservesTheOriginalOperationUntilExceptionalExit()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var cleanup = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var site = factory.CreateOperation("division", new IrSourceSpan("Subject.cs", 12, 4));
        builder.Throw(entry, site, IrExceptionKind.DivideByZero, cleanup);
        builder.Goto(cleanup, factory.CreateOperation(), exit);
        builder.ExceptionalExit(exit, factory.CreateOperation());
        var program = builder.Build();
        var order = IrBlockOrder.TryCreateAcyclicOrder(program, _ => true, out var failure);
        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.None));
        Assert.That(order, Is.EqualTo(new[] { entry, cleanup, exit }));
        var successors = IrInstructionFacts.TryGetSuccessors(program.GetBlock(entry).Terminator);
        Assert.That(successors!.Value.First, Is.EqualTo(cleanup));
        Assert.That(successors.Value.Second, Is.Null);
        var terminal = IrInstructionFacts.TryGetSuccessors(program.GetBlock(exit).Terminator);
        Assert.That(terminal!.Value.First, Is.Null);
        Assert.That(terminal.Value.Second, Is.Null);
        var result = new IrProgramInterpreter(factory).Execute(program);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        Assert.That(result.Exception.Site, Is.EqualTo(site));
        Assert.That(factory.GetOperationInfo(result.Instruction!.Operation).SourceSpan!.Start, Is.EqualTo(12));
    }

    [Test]
    public void ThrowCycleIsRejectedByAcyclicOrdering()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Throw(entry, factory.CreateOperation(), IrExceptionKind.DivideByZero, entry);
        var order = IrBlockOrder.TryCreateAcyclicOrder(builder.Build(), _ => true, out var failure);
        Assert.That(order.IsDefault, Is.True);
        Assert.That(failure, Is.EqualTo(IrAcyclicOrderFailure.CyclicControlFlow));
    }

    [Test]
    public void LegacyExecutionRejectsModeledHavocBeforeInvokingItsProvider()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Return(entry, factory.CreateOperation(), factory.Integer(1));
        var invoked = false;
        var options = new IrProgramReplayOptions(_ => { invoked = true; return factory.CreateIntegerValue(2); });
        var exception = Assert.Throws<ArgumentException>((Action)(() =>
            new IrProgramInterpreter(factory).Execute(builder.Build(), null, 100, options)));
        Assert.That(exception!.ParamName, Is.EqualTo("replayOptions"));
        Assert.That(invoked, Is.False);
    }

    [TestCase("replacement", IrProgramExecutionStatus.Exception)]
    [TestCase("handled", IrProgramExecutionStatus.Returned)]
    [TestCase("naked-exit", IrProgramExecutionStatus.Unsupported)]
    public void ExceptionalContinuationsAreDistinctFromNormalReturn(string scenario, IrProgramExecutionStatus expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var cleanup = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var replacement = factory.CreateOperation("replacement");
        if (scenario == "naked-exit")
        {
            builder.Goto(entry, factory.CreateOperation(), cleanup);
        }
        else
        {
            builder.Throw(entry, factory.CreateOperation(), IrExceptionKind.DivideByZero, cleanup);
        }
        if (scenario == "replacement")
        {
            builder.Throw(cleanup, replacement, IrExceptionKind.Overflow, exit);
        }
        else if (scenario == "handled")
        {
            builder.Return(cleanup, factory.CreateOperation(), factory.Integer(7));
        }
        else
        {
            builder.Goto(cleanup, factory.CreateOperation(), exit);
        }
        builder.ExceptionalExit(exit, factory.CreateOperation());
        var result = new IrProgramInterpreter(factory).Execute(builder.Build());
        Assert.That(result.Status, Is.EqualTo(expected));
        if (scenario == "replacement")
        {
            Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
            Assert.That(result.Exception.Site, Is.EqualTo(replacement));
        }
        else
        {
            Assert.That(result.Exception, Is.Null);
        }
    }

    [TestCase(IrHavocOrigin.Input, false)]
    [TestCase(IrHavocOrigin.SpecResult, false)]
    [TestCase(IrHavocOrigin.Approximation, true)]
    public void HavocOccurrencesArePerVisitAndSharedAcrossVariables(IrHavocOrigin origin, bool approximate)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", factory.IntegerType);
        var y = factory.CreateVariable("y", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var havoc = builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, origin, x, y);
        builder.Branch(entry, factory.CreateOperation(), factory.Binary(IrBinaryOperator.LessThan,
            factory.Variable(x), factory.Integer(1)), entry, exit);
        builder.Return(exit, factory.CreateOperation(), factory.Variable(y));
        var requests = new List<IrHavocRequest>();
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 100,
            new IrProgramReplayOptions(request =>
            {
                requests.Add(request);
                return factory.CreateIntegerValue(request.Occurrence);
            }));
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(1));
        int[] expectedOccurrences = [0, 0, 1, 1];
        Assert.That(requests.Select(request => request.Occurrence), Is.EqualTo(expectedOccurrences));
        Assert.That(requests.Select(request => request.Instruction), Has.All.EqualTo(havoc.Id));
        Assert.That(result.ConsumedApproximation, Is.EqualTo(approximate));
    }

    [TestCase("missing")]
    [TestCase("wrong-type")]
    [TestCase("memory")]
    public void UnsupportedHavocModelsFailClosed(string scenario)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), scenario == "memory" ? IrHavocKind.VariablesAndMemory
            : IrHavocKind.Variables, IrHavocOrigin.Approximation, x);
        builder.Return(entry, factory.CreateOperation(), factory.Integer(0));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 100,
            new IrProgramReplayOptions(_ => scenario == "missing" ? null : factory.CreateBooleanValue(true)));
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Unsupported));
    }

    [TestCase("argument", true)]
    [TestCase("receiver", true)]
    [TestCase("earlier-unsupported-argument", false)]
    public void CallOperandsUseTheSameActualReadObserver(string scenario, bool expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var x = factory.CreateVariable("x", scenario == "receiver" ? factory.ObjectType : factory.IntegerType);
        var unavailable = factory.CreateVariable("unavailable", factory.IntegerType);
        var resultVariable = factory.CreateVariable("result", factory.IntegerType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Call", factory.IntegerType,
            scenario != "receiver", scenario == "receiver" ? []
                : scenario == "argument" ? [factory.IntegerType] : [factory.IntegerType, factory.IntegerType]);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, x);
        builder.Call(entry, factory.CreateOperation(), resultVariable, member,
            scenario == "receiver" ? factory.Variable(x) : null,
            scenario == "receiver" ? [] : scenario == "argument" ? [factory.Variable(x)]
                : [factory.Variable(unavailable), factory.Variable(x)]);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(resultVariable));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 100,
            (_, _, _) => factory.CreateIntegerValue(7),
            new IrProgramReplayOptions(_ => scenario == "receiver"
                ? factory.CreateReferenceValue(factory.ObjectType, new object()) : factory.CreateIntegerValue(2)),
            CancellationToken.None);
        Assert.That(result.ConsumedApproximation, Is.EqualTo(expected));
        Assert.That(result.Status, Is.EqualTo(expected ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Unsupported));
    }

    [TestCase("approximation", true, true)]
    [TestCase("missing-first", false, false)]
    [TestCase("unused", false, true)]
    public void SkippedCallsStillEvaluateTheirOperands(string scenario, bool consumed, bool returned)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var unknown = factory.CreateVariable("unknown", factory.IntegerType);
        var missing = factory.CreateVariable("missing", factory.IntegerType);
        var arguments = scenario == "missing-first" ? new IrTerm[] { factory.Variable(missing), factory.Variable(unknown) }
            : new IrTerm[] { scenario == "unused" ? factory.Integer(4) : factory.Variable(unknown) };
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Call", factory.IntegerType,
            true, arguments.Select(argument => argument.Type).ToArray());
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, unknown);
        builder.Call(entry, factory.CreateOperation(), null, member, null, arguments);
        builder.Return(entry, factory.CreateOperation(), factory.Integer(0));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 100,
            new IrProgramReplayOptions(_ => factory.CreateIntegerValue(2)));
        Assert.That(result.Status, Is.EqualTo(returned ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Unsupported));
        Assert.That(result.ConsumedApproximation, Is.EqualTo(consumed));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void HostedCallArgumentsReadCurrentHeap(bool array, bool store)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.ObjectType;
        var owner = factory.CreateVariable("owner", type);
        var target = factory.CreateVariable("result", factory.IntegerType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Call", factory.IntegerType, true, [factory.IntegerType]);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        if (store)
        {
            if (array)
            { builder.ElementStore(entry, factory.CreateOperation(), factory.Variable(owner), factory.Integer(0), factory.Integer(7)); }
            else
            { builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(owner), field, factory.Integer(7)); }
        }
        var argument = array ? factory.SequenceAccess(factory.Variable(owner), factory.Integer(0))
            : factory.PureOpaque(field, factory.Variable(owner));
        builder.Call(entry, factory.CreateOperation(), target, member, null, argument);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(target));
        var initial = array ? factory.CreateSequenceValue(type, [factory.CreateIntegerValue(3)])
            : factory.CreateReferenceValue(type, new IrObjectState().WithField(field, factory.CreateIntegerValue(3)));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(),
            new Dictionary<IrVarId, IrValue> { [owner] = initial }, 100,
            (_, _, values) => values.Single(), null, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(store ? 7 : 3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SkippedInstanceCallsRespectReceiverReadOrder(bool missing)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var owner = factory.CreateVariable("owner", factory.ObjectType);
        var argument = factory.CreateVariable("argument", factory.IntegerType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Call", factory.IntegerType, false, [factory.IntegerType]);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, argument);
        builder.Call(entry, factory.CreateOperation(), null, member, factory.Variable(owner), factory.Variable(argument));
        builder.Return(entry, factory.CreateOperation(), factory.Integer(0));
        var initial = new Dictionary<IrVarId, IrValue>();
        if (!missing)
        { initial.Add(owner, factory.CreateNullValue(factory.ObjectType)); }
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), initial, 100,
            new IrProgramReplayOptions(_ => factory.CreateIntegerValue(2)));
        Assert.That(result.Status, Is.EqualTo(missing ? IrProgramExecutionStatus.Unsupported : IrProgramExecutionStatus.Exception));
        Assert.That(result.ConsumedApproximation, Is.EqualTo(!missing));
        if (!missing)
        { Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.NullReference)); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void HostedCallReceiverReadsCurrentHeap(bool store)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var holder = factory.CreateVariable("holder", factory.ObjectType);
        var replacement = factory.CreateVariable("replacement", factory.ObjectType);
        var target = factory.CreateVariable("result", factory.IntegerType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Next", factory.ObjectType, false);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Call", factory.IntegerType, false);
        var oldValue = factory.CreateReferenceValue(factory.ObjectType, new object());
        var newValue = factory.CreateReferenceValue(factory.ObjectType, new object());
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        if (store)
        { builder.FieldStore(entry, factory.CreateOperation(), IrWriteRegion.Field, factory.Variable(holder), field, factory.Variable(replacement)); }
        builder.Call(entry, factory.CreateOperation(), target, member, factory.PureOpaque(field, factory.Variable(holder)));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(target));
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), new Dictionary<IrVarId, IrValue>
        {
            [holder] = factory.CreateReferenceValue(factory.ObjectType, new IrObjectState().WithField(field, oldValue)),
            [replacement] = newValue
        }, 100, (_, receiver, _) => factory.CreateIntegerValue(ReferenceEquals(receiver!.Reference, newValue.Reference) ? 7 : 3),
            null, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(store ? 7 : 3));
    }

    [Test]
    public void TotalSequenceAndUnboxingFaultsUseTypedDefaults()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var elementType = factory.GetOrCreateIntegerType(64, false);
        var sequenceType = factory.GetOrCreateSequenceType(factory.CreateIdentity(), elementType, "values");
        var sequence = factory.CreateVariable("sequence", sequenceType);
        var boxed = factory.CreateVariable("boxed", factory.ObjectType);
        var values = ImmutableDictionary<IrVarId, IrValue>.Empty
            .Add(sequence, factory.CreateSequenceValue(sequenceType, []))
            .Add(boxed, factory.CreateReferenceValue(factory.ObjectType, 1));
        var terms = new[]
        {
            factory.SequenceAccess(factory.Null(sequenceType), factory.Integer(0)),
            factory.SequenceAccess(factory.Variable(sequence), factory.Integer(-1)),
            factory.Cast(elementType, factory.Null(factory.ObjectType)),
            factory.Cast(elementType, factory.Variable(boxed))
        };
        foreach (var term in terms)
        {
            var result = new IrInterpreter(factory).Evaluate(term, values);
            Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(result.Value!.Type, Is.EqualTo(elementType));
            Assert.That(result.Value.IntegerBits, Is.Zero);
        }
    }

    [TestCase(false, "null", true)]
    [TestCase(true, "null", true)]
    [TestCase(false, "missing", false)]
    [TestCase(true, "missing", false)]
    [TestCase(false, "nonnull", true)]
    [TestCase(true, "nonnull", true)]
    public void MemberLocationOperandsPreserveReadOrderBeforeMemoryFailure(bool store, string receiverKind, bool consumed)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var receiver = factory.CreateVariable("receiver", factory.ObjectType);
        var argument = factory.CreateVariable("argument", factory.IntegerType);
        var value = factory.CreateVariable("value", factory.IntegerType);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "Item",
            factory.IntegerType, false, [factory.IntegerType]);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables,
            IrHavocOrigin.Approximation, argument, value);
        var location = builder.MemberLocation(member, factory.Variable(receiver), factory.Variable(argument));
        var site = factory.CreateOperation("member access");
        if (store)
        {
            builder.Store(entry, site, location, factory.Variable(value));
        }
        else
        {
            builder.Load(entry, site, value, location);
        }
        builder.Return(entry, factory.CreateOperation());
        var initial = ImmutableDictionary<IrVarId, IrValue>.Empty;
        if (receiverKind != "missing")
        {
            initial = initial.Add(receiver, receiverKind == "null" ? factory.CreateNullValue(factory.ObjectType)
                : factory.CreateReferenceValue(factory.ObjectType, new object()));
        }
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), initial, 100,
            new IrProgramReplayOptions(_ => factory.CreateIntegerValue(2)));
        Assert.That(result.ConsumedApproximation, Is.EqualTo(consumed));
        Assert.That(result.Instruction!.Operation, Is.EqualTo(site));
        Assert.That(result.Status, Is.EqualTo(receiverKind == "null"
            ? IrProgramExecutionStatus.Exception : IrProgramExecutionStatus.Unsupported));
        if (receiverKind == "null")
        {
            Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.NullReference));
        }
        else if (receiverKind == "missing")
        {
            Assert.That(result.Unsupported!.Reason, Is.EqualTo(IrUnsupportedReason.MissingVariable));
        }
    }

    [Test]
    public void TotalUnboxingFaultsUseBooleanAndStringDefaults()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var boxed = factory.CreateVariable("boxed", factory.ObjectType);
        var values = ImmutableDictionary<IrVarId, IrValue>.Empty
            .Add(boxed, factory.CreateReferenceValue(factory.ObjectType, new object()));
        var interpreter = new IrInterpreter(factory);
        var boolean = interpreter.Evaluate(factory.Cast(factory.BooleanType, factory.Null(factory.ObjectType)), values);
        var text = interpreter.Evaluate(factory.Cast(factory.StringType, factory.Variable(boxed)), values);
        Assert.That(boolean.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(boolean.Value!.Type, Is.EqualTo(factory.BooleanType));
        Assert.That(boolean.Value.Boolean, Is.False);
        Assert.That(text.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(text.Value!.Type, Is.EqualTo(factory.StringType));
        Assert.That(text.Value.Kind, Is.EqualTo(IrValueKind.Null));
    }

    [TestCase(true, IrUnsupportedReason.InvalidVariableValue)]
    [TestCase(false, IrUnsupportedReason.MissingVariable)]
    public void ReadsAreObservedAfterSuccessfulLookupBeforeTypeValidation(bool present, IrUnsupportedReason reason)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var values = ImmutableDictionary<IrVarId, IrValue>.Empty;
        if (present)
        {
            values = values.Add(variable, factory.CreateBooleanValue(true));
        }
        var observed = new List<IrVarId>();
        var result = new IrInterpreter(factory).Evaluate(factory.Variable(variable), values,
            observed.Add, CancellationToken.None);
        Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Unsupported));
        Assert.That(result.Unsupported!.Reason, Is.EqualTo(reason));
        Assert.That(observed, Is.EqualTo(present ? new[] { variable } : Array.Empty<IrVarId>()));
    }

    [Test]
    public async Task ConcurrentEvaluationsKeepSeparateLazyReadObservers()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var x = factory.CreateVariable("x", factory.IntegerType);
        var y = factory.CreateVariable("y", factory.IntegerType);
        var term = factory.Conditional(factory.Variable(flag), factory.Variable(x), factory.Variable(y));
        var interpreter = new IrInterpreter(factory);
        using var barrier = new Barrier(2);
        var whenTrue = new List<IrVarId>();
        var whenFalse = new List<IrVarId>();
        var tasks = new[]
        {
            Task.Run(() => Evaluate(true, whenTrue)),
            Task.Run(() => Evaluate(false, whenFalse))
        };
        var results = await Task.WhenAll(tasks);
        Assert.That(results.Select(result => result.Status), Is.All.EqualTo(IrEvaluationStatus.Value));
        Assert.That(results[0].Value!.Integer, Is.EqualTo(11));
        Assert.That(results[1].Value!.Integer, Is.EqualTo(22));
        Assert.That(whenTrue, Is.EqualTo(new[] { flag, x }));
        Assert.That(whenFalse, Is.EqualTo(new[] { flag, y }));

        IrEvaluationResult Evaluate(bool selected, List<IrVarId> observed)
        {
            var values = ImmutableDictionary<IrVarId, IrValue>.Empty
                .Add(flag, factory.CreateBooleanValue(selected))
                .Add(x, factory.CreateIntegerValue(11)).Add(y, factory.CreateIntegerValue(22));
            return interpreter.Evaluate(term, values, variable =>
            {
                observed.Add(variable);
                if (variable == flag)
                {
                    Assert.That(barrier.SignalAndWait(TimeSpan.FromSeconds(10)), Is.True);
                }
            }, CancellationToken.None);
        }
    }
}
