using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrProgramTests
{
    [Test]
    public void AssignmentsReadTheCurrentVariableValue()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Assign(
            entry,
            factory.CreateOperation("initialize"),
            value,
            factory.Integer(4));
        builder.Assign(
            entry,
            factory.CreateOperation("increment"),
            value,
            factory.Binary(
                IrBinaryOperator.Add,
                factory.Variable(value),
                factory.Integer(3)));
        builder.Return(
            entry,
            factory.CreateOperation("return"),
            factory.Variable(value));

        var result = new IrProgramInterpreter(factory).Execute(builder.Build());

        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Integer, Is.EqualTo(7));
        Assert.That(result.GetCurrentValue(value)!.Integer, Is.EqualTo(7));
    }

    [TestCase(true, 10L)]
    [TestCase(false, 20L)]
    public void BranchesSelectOneDeterministicSuccessor(
        bool condition,
        long expected)
    {
        var factory = new IrFactory();
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var resultVariable =
            factory.CreateVariable("result", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var whenTrue = builder.CreateBlock("true");
        var whenFalse = builder.CreateBlock("false");
        var exit = builder.CreateBlock("exit");
        builder.Branch(
            entry,
            factory.CreateOperation("branch"),
            factory.Variable(flag),
            whenTrue,
            whenFalse);
        builder.Assign(
            whenTrue,
            factory.CreateOperation("true-value"),
            resultVariable,
            factory.Integer(10));
        builder.Goto(
            whenTrue,
            factory.CreateOperation("true-exit"),
            exit);
        builder.Assign(
            whenFalse,
            factory.CreateOperation("false-value"),
            resultVariable,
            factory.Integer(20));
        builder.Goto(
            whenFalse,
            factory.CreateOperation("false-exit"),
            exit);
        builder.Return(
            exit,
            factory.CreateOperation("return"),
            factory.Variable(resultVariable));

        var execution = new IrProgramInterpreter(factory).Execute(
            builder.Build(),
            new Dictionary<IrVarId, IrValue>
            {
                [flag] = factory.CreateBooleanValue(condition)
            });

        Assert.That(
            execution.Status,
            Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.Integer, Is.EqualTo(expected));
    }

    [TestCase(IrInstructionKind.Assume)]
    [TestCase(IrInstructionKind.Assert)]
    [TestCase(IrInstructionKind.Branch)]
    public void InterpreterRejectsBooleanValuesWithWrongRuntimeKinds(
        IrInstructionKind instructionKind)
    {
        var factory = new IrFactory();
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var whenTrue = builder.CreateBlock("true");
        var whenFalse = builder.CreateBlock("false");
        var condition = factory.Variable(flag);
        IrInstruction instruction = instructionKind switch
        {
            IrInstructionKind.Assume => builder.Assume(
                entry,
                factory.CreateOperation("assume"),
                condition),
            IrInstructionKind.Assert => builder.Assert(
                entry,
                factory.CreateOperation("assert"),
                condition),
            IrInstructionKind.Branch => builder.Branch(
                entry,
                factory.CreateOperation("branch"),
                condition,
                whenTrue,
                whenFalse),
            _ => throw new ArgumentOutOfRangeException(nameof(instructionKind))
        };
        if (instructionKind != IrInstructionKind.Branch)
        {
            builder.Return(entry, factory.CreateOperation("entry-return"));
        }
        builder.Return(whenTrue, factory.CreateOperation("true-return"));
        builder.Return(whenFalse, factory.CreateOperation("false-return"));
        var malformed = new IrValue(
            factory.BooleanType,
            IrValueKind.Integer,
            1L);

        var result = new IrProgramInterpreter(factory).Execute(
            builder.Build(),
            new Dictionary<IrVarId, IrValue>
            {
                [flag] = malformed
            });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Status,
                Is.EqualTo(IrProgramExecutionStatus.Unsupported));
            Assert.That(result.Instruction, Is.SameAs(instruction));
            Assert.That(
                result.Unsupported!.Reason,
                Is.EqualTo(IrUnsupportedReason.InvalidVariableValue));
            Assert.That(result.Exception, Is.Null);
        }
    }

    [Test]
    public void BuilderCreatesClosedTypedMemoryAndCallInstructions()
    {
        var factory = new IrFactory();
        var receiverType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Box");
        var receiver = factory.CreateVariable("box", receiverType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var valueMember = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            receiverType,
            "Value",
            factory.IntegerType,
            isStatic: false);
        var nextMember = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            receiverType,
            "Next",
            factory.IntegerType,
            isStatic: false,
            factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var location = builder.MemberLocation(
            valueMember,
            factory.Variable(receiver));
        builder.Store(
            entry,
            factory.CreateOperation("store"),
            location,
            factory.Integer(5));
        builder.Load(
            entry,
            factory.CreateOperation("load"),
            result,
            location);
        builder.Call(
            entry,
            factory.CreateOperation("call"),
            result,
            nextMember,
            factory.Variable(receiver),
            factory.Variable(result));
        builder.Havoc(
            entry,
            factory.CreateOperation("havoc"),
            IrHavocKind.Memory);
        builder.Return(
            entry,
            factory.CreateOperation("return"),
            factory.Variable(result));

        var instructions = builder.Build().Blocks.Single().Instructions;
        IrInstructionKind[] expectedKinds = [
            IrInstructionKind.Store,
            IrInstructionKind.Load,
            IrInstructionKind.Call,
            IrInstructionKind.Havoc,
            IrInstructionKind.Return
        ];
        int[] expectedIds = [0, 1, 2, 3, 4];

        Assert.That(
            instructions.Select(static instruction => instruction.Kind),
            Is.EqualTo(expectedKinds));
        Assert.That(
            instructions.Select(static instruction => instruction.Id.Value),
            Is.EqualTo(expectedIds));
        Assert.That(
            ((IrLoadInstruction)instructions[1]).Location.Type,
            Is.EqualTo(factory.IntegerType));
    }

    [Test]
    public void SequenceLocationsRejectNullTermsWithThePublicParameterName()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);

        var sequenceError = Assert.Throws<ArgumentNullException>(new Action(
            () => builder.SequenceLocation(null!, factory.Integer(0))));
        var indexError = Assert.Throws<ArgumentNullException>(new Action(
            () => builder.SequenceLocation(
                factory.Null(sequenceType),
                null!)));

        Assert.That(sequenceError!.ParamName, Is.EqualTo("sequence"));
        Assert.That(indexError!.ParamName, Is.EqualTo("index"));
    }

    [Test]
    public void ProgramIdentifiersAreScopedAndValuesAreDeterministic()
    {
        var first = CreateShape();
        var second = CreateShape();

        Assert.That(first.Entry, Is.Not.EqualTo(second.Entry));
        Assert.That(first.Entry.Value, Is.EqualTo(second.Entry.Value));
        Assert.That(
            first.Blocks.Select(static block => block.Id.Value),
            Is.EqualTo(second.Blocks.Select(static block => block.Id.Value)));
        Assert.That(
            first.Blocks
                .SelectMany(static block => block.Instructions)
                .Select(static instruction =>
                    (instruction.Id.Value, instruction.Kind)),
            Is.EqualTo(
                second.Blocks
                    .SelectMany(static block => block.Instructions)
                    .Select(static instruction =>
                        (instruction.Id.Value, instruction.Kind))));
        Assert.Throws<ArgumentException>(
            (Action)(() => first.GetBlock(second.Entry)));
    }

    [Test]
    public void BuilderRequiresClosedBlocksAndRejectsPostTerminatorInstructions()
    {
        var factory = new IrFactory();
        var empty = new IrProgramBuilder(factory);
        var open = new IrProgramBuilder(factory);
        open.CreateBlock("open");
        var closed = new IrProgramBuilder(factory);
        var entry = closed.CreateBlock("entry");
        closed.Return(entry, factory.CreateOperation("return"));

        Assert.Throws<InvalidOperationException>(
            (Action)(() => empty.Build()));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => open.Build()));
        Assert.Throws<InvalidOperationException>(
            (Action)(() => closed.Assign(
                entry,
                factory.CreateOperation("late"),
                factory.CreateVariable("value", factory.IntegerType),
                factory.Integer(1))));
    }

    [Test]
    public void BuilderRejectsInvalidInstructionsAfterBuildAsConsumed()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(entry, factory.CreateOperation("return"));
        builder.Build();

        Assert.Throws<InvalidOperationException>(
            (Action)(() => builder.Return(entry, default)));
    }

    [Test]
    public void BuilderEnforcesHavocKindVariableConsistency()
    {
        var factory = new IrFactory();
        var value =
            factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");

        Assert.Throws<ArgumentException>(
            (Action)(() => builder.Havoc(
                entry,
                factory.CreateOperation("memory-with-variable"),
                IrHavocKind.Memory,
                value)));
        Assert.Throws<ArgumentException>(
            (Action)(() => builder.Havoc(
                entry,
                factory.CreateOperation("variables-without-variable"),
                IrHavocKind.Variables)));
        Assert.Throws<ArgumentException>(
            (Action)(() => builder.Havoc(
                entry,
                factory.CreateOperation("combined-without-variable"),
                IrHavocKind.VariablesAndMemory)));
    }

    [TestCase(IrInstructionKind.Assume, IrProgramExecutionStatus.AssumptionViolated)]
    [TestCase(IrInstructionKind.Assert, IrProgramExecutionStatus.AssertionFailed)]
    public void InterpreterDistinguishesAssumptionAndAssertionFailures(
        IrInstructionKind instructionKind,
        IrProgramExecutionStatus expectedStatus)
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var operation = factory.CreateOperation(
            instructionKind == IrInstructionKind.Assume ? "assume" : "assert");
        switch (instructionKind)
        {
            case IrInstructionKind.Assume:
                builder.Assume(entry, operation, factory.Boolean(false));
                break;
            case IrInstructionKind.Assert:
                builder.Assert(entry, operation, factory.Boolean(false));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(instructionKind));
        }
        builder.Return(entry, factory.CreateOperation("return"));
        var interpreter = new IrProgramInterpreter(factory);

        var result = interpreter.Execute(builder.Build());

        Assert.That(
            result.Status,
            Is.EqualTo(expectedStatus));
        Assert.That(
            result.Instruction!.Operation,
            Is.EqualTo(operation));
    }

    [Test]
    public void InterpreterStopsLoopsAtTheStepBudget()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Goto(
            entry,
            factory.CreateOperation("loop"),
            entry);

        var result = new IrProgramInterpreter(factory).Execute(
            builder.Build(),
            maximumSteps: 3);

        Assert.That(
            result.Status,
            Is.EqualTo(IrProgramExecutionStatus.StepLimit));
        Assert.That(result.Steps, Is.EqualTo(3));
    }

    [Test]
    public void InterpreterStopsWithinABlockAtTheExactStepBudget()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Assign(
            entry,
            factory.CreateOperation("first"),
            value,
            factory.Integer(1));
        builder.Assign(
            entry,
            factory.CreateOperation("second"),
            value,
            factory.Integer(2));
        builder.Return(entry, factory.CreateOperation("return"));

        var result = new IrProgramInterpreter(factory).Execute(
            builder.Build(),
            maximumSteps: 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.StepLimit));
            Assert.That(result.Steps, Is.EqualTo(1));
            Assert.That(result.Instruction, Is.Null);
            Assert.That(result.GetCurrentValue(value)?.Integer, Is.EqualTo(1));
        }
    }

    [TestCase(IrHavocKind.Variables)]
    [TestCase(IrHavocKind.VariablesAndMemory)]
    public void InterpreterFailsClosedAtVariableHavocAfterInvalidatingValues(
        IrHavocKind havocKind)
    {
        var (result, havoc, value) = ExecuteHavoc(havocKind);

        Assert.That(
            result.Status,
            Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(result.Instruction, Is.SameAs(havoc));
        Assert.That(
            result.Unsupported!.Reason,
            Is.EqualTo(IrUnsupportedReason.UnsupportedOperation));
        Assert.That(result.GetCurrentValue(value), Is.Null);
    }

    [Test]
    public void InterpreterPreservesVariablesAtMemoryOnlyHavoc()
    {
        var (result, havoc, value) = ExecuteHavoc(IrHavocKind.Memory);

        Assert.That(
            result.Status,
            Is.EqualTo(IrProgramExecutionStatus.Unsupported));
        Assert.That(result.Instruction, Is.SameAs(havoc));
        Assert.That(result.GetCurrentValue(value)!.Integer, Is.EqualTo(7));
    }

    private static (
        IrProgramExecutionResult Result,
        IrHavocInstruction Havoc,
        IrVarId Value) ExecuteHavoc(IrHavocKind havocKind)
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var havocOperation = factory.CreateOperation("havoc");
        var havoc = havocKind == IrHavocKind.Memory
            ? builder.Havoc(entry, havocOperation, havocKind)
            : builder.Havoc(entry, havocOperation, havocKind, value);
        builder.Return(
            entry,
            factory.CreateOperation("return"),
            factory.Variable(value));

        var result = new IrProgramInterpreter(factory).Execute(
            builder.Build(),
            new Dictionary<IrVarId, IrValue>
            {
                [value] = factory.CreateIntegerValue(7)
            });
        return (result, havoc, value);
    }

    [Test]
    public void InterpreterEvaluatesCallArgumentsBeforeNullReceiverFailure()
    {
        AssertOperandEvaluatedBeforeNullReceiver(
            (factory, builder, entry, resultVariable) =>
            {
                var receiverType = factory.GetOrCreateReferenceType(
                    factory.CreateIdentity(),
                    "Box");
                var member = factory.GetOrCreateMember(
                    factory.CreateIdentity(),
                    receiverType,
                    "Read",
                    factory.IntegerType,
                    isStatic: false,
                    factory.IntegerType);
                return builder.Call(
                    entry,
                    factory.CreateOperation("call"),
                    resultVariable,
                    member,
                    factory.Null(receiverType),
                    DivisionByZero(factory));
            });
    }

    [Test]
    public void InterpreterEvaluatesLoadIndexBeforeNullReceiverFailure()
    {
        AssertOperandEvaluatedBeforeNullReceiver(
            (factory, builder, entry, resultVariable) =>
            {
                var sequenceType =
                    factory.GetOrCreateSequenceType(factory.IntegerType);
                return builder.Load(
                    entry,
                    factory.CreateOperation("load"),
                    resultVariable,
                    builder.SequenceLocation(
                        factory.Null(sequenceType),
                        DivisionByZero(factory)));
            });
    }

    [Test]
    public void InterpreterEvaluatesStoreValueBeforeDeferredBoundsCheck()
    {
        var factory = new IrFactory();
        var sequenceType =
            factory.GetOrCreateSequenceType(factory.IntegerType);
        var sequence =
            factory.CreateVariable("values", sequenceType);
        var (failingProgram, failingStore) = BuildStoreProgram(
            factory,
            sequence,
            "failing-store",
            "failing-return",
            DivisionByZero(factory));
        var (boundsProgram, boundsStore) = BuildStoreProgram(
            factory,
            sequence,
            "bounds-store",
            "bounds-return",
            factory.Integer(7));
        var values = new Dictionary<IrVarId, IrValue>
        {
            [sequence] = factory.CreateSequenceValue(sequenceType, [])
        };
        var interpreter = new IrProgramInterpreter(factory);

        var failing =
            interpreter.Execute(failingProgram, values);
        var bounds =
            interpreter.Execute(boundsProgram, values);

        Assert.That(
            failing.Status,
            Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(failing.Instruction, Is.SameAs(failingStore));
        Assert.That(
            failing.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.DivideByZero));
        Assert.That(
            bounds.Status,
            Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(bounds.Instruction, Is.SameAs(boundsStore));
        Assert.That(
            bounds.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.IndexOutOfRange));
    }

    private static (IrProgram Program, IrStoreInstruction Store) BuildStoreProgram(
        IrFactory factory,
        IrVarId sequence,
        string storeOperation,
        string returnOperation,
        IrTerm value)
    {
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var store = builder.Store(
            entry,
            factory.CreateOperation(storeOperation),
            builder.SequenceLocation(
                factory.Variable(sequence),
                factory.Integer(1)),
            value);
        builder.Return(entry, factory.CreateOperation(returnOperation));
        return (builder.Build(), store);
    }

    [Test]
    public void InterpreterObservesPreCanceledExecution()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.Return(entry, factory.CreateOperation("return"));
        var cancellationToken = new CancellationToken(canceled: true);

        Assert.Throws<OperationCanceledException>(new Action(() =>
            _ = new IrProgramInterpreter(factory).Execute(
                builder.Build(),
                cancellationToken: cancellationToken)));
    }

    private static void AssertOperandEvaluatedBeforeNullReceiver(
        Func<IrFactory, IrProgramBuilder, IrBlockId, IrVarId, IrInstruction> append)
    {
        var factory = new IrFactory();
        var resultVariable =
            factory.CreateVariable("result", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var instruction = append(factory, builder, entry, resultVariable);
        builder.Return(entry, factory.CreateOperation("return"));

        var result = new IrProgramInterpreter(factory).Execute(builder.Build());

        Assert.That(
            result.Status,
            Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(result.Instruction, Is.SameAs(instruction));
        Assert.That(
            result.Exception!.Kind,
            Is.EqualTo(IrExceptionKind.DivideByZero));
    }

    private static IrProgram CreateShape()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var exit = builder.CreateBlock("exit");
        builder.Goto(
            entry,
            factory.CreateOperation("goto"),
            exit);
        builder.Return(
            exit,
            factory.CreateOperation("return"));
        return builder.Build();
    }

    private static IrTerm DivisionByZero(IrFactory factory)
    {
        return factory.Binary(
            IrBinaryOperator.Divide,
            factory.Integer(1),
            factory.Integer(0));
    }
}
