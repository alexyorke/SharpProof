using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class IrTotalExecutionTests
{
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
        var result = new IrProgramInterpreter(factory).Execute(builder.Build());
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
        Assert.That(result.Exception.Site, Is.EqualTo(site));
        Assert.That(factory.GetOperationInfo(result.Instruction!.Operation).SourceSpan!.Start, Is.EqualTo(12));
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
}
