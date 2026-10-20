using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

internal sealed class DefaultComparerValueReplayTests
{
    [TestCase("System_SByte")]
    [TestCase("System_Byte")]
    [TestCase("System_Int16")]
    [TestCase("System_UInt16")]
    [TestCase("System_Char")]
    [TestCase("System_Int32")]
    [TestCase("System_UInt32")]
    [TestCase("System_Int64")]
    [TestCase("System_UInt64")]
    public void ApprovedValuesObserveTheActualCachedInstance(string primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var term = Value(factory, primitive);
        var result = PassiveCallableVcPlan.ObserveDefaultComparerValue(factory, term, PassiveCallableVcPlan.ReadDefaultComparer);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Reference, Is.SameAs(PassiveCallableVcPlan.ReadDefaultComparer(primitive)));
    }

    [TestCase("symbolic")]
    [TestCase("System_Boolean")]
    [TestCase("unowned")]
    [TestCase("wrong-type")]
    [TestCase("instance")]
    [TestCase("argument")]
    [TestCase("impure")]
    [TestCase("declaring-type")]
    public void UnapprovedValuesNeverExecuteTheObserver(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var term = Value(factory, scenario is "symbolic" or "System_Boolean" ? scenario : "System_Int32", scenario);
        var invoked = false;
        Assert.That(PassiveCallableVcPlan.ObserveDefaultComparerValue(factory, term, primitive =>
        {
            ArgumentNullException.ThrowIfNull(primitive);
            invoked = true;
            return new object();
        }), Is.Null);
        Assert.That(invoked, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ObservedValuesDoNotErasePreviouslyConsumedApproximation(bool approximate)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation("comparer-value-replay");
        if (approximate)
        {
            var choice = factory.CreateVariable("choice", factory.BooleanType);
            builder.Havoc(block, site, IrHavocKind.Variables, IrHavocOrigin.Approximation, choice);
            var next = builder.CreateBlock();
            var other = builder.CreateBlock();
            builder.Branch(block, site, factory.Variable(choice), next, other);
            builder.Goto(other, site, next);
            block = next;
        }
        builder.Return(block, site, Value(factory, "System_Int32"));
        var options = new IrProgramReplayOptions(_ => factory.CreateBooleanValue(false))
        {
            ConcreteOpaqueValueProvider = term => term is null ? throw new ArgumentNullException(nameof(term)) : PassiveCallableVcPlan.ObserveDefaultComparerValue(factory, term, PassiveCallableVcPlan.ReadDefaultComparer)
        };
        var result = new IrProgramInterpreter(factory).Execute(builder.Build(), null, 100, options);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(result.ReturnValue!.Reference, Is.SameAs(Comparer<int>.Default));
        Assert.That(result.ConsumedApproximation, Is.EqualTo(approximate));
    }

    [TestCase("missing")]
    [TestCase("wrong-type")]
    public void UnavailableOrInvalidObservationsCannotReturnAValue(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Return(block, factory.CreateOperation("comparer-value-replay"), Value(factory, "System_Int32"));
        var options = new IrProgramReplayOptions(_ => null)
        {
            ConcreteOpaqueValueProvider = scenario == "missing" ? null : _ => factory.CreateIntegerValue(0)
        };
        Assert.That(new IrProgramInterpreter(factory).Execute(builder.Build(), null, 100, options).Status,
            Is.Not.EqualTo(IrProgramExecutionStatus.Returned));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201", Justification = "Inject failed observation without exhausting process memory.")]
    [TestCase("null")]
    [TestCase("initialization")]
    [TestCase("allocation")]
    public void FailedObservationsStayUnavailable(string failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        Assert.That(PassiveCallableVcPlan.ObserveDefaultComparerValue(factory, Value(factory, "System_Int32"), _ => failure switch
        {
            "null" => null,
            "initialization" => throw new TypeInitializationException("probe", new InvalidOperationException()),
            "allocation" => throw new OutOfMemoryException(),
            _ => throw new ArgumentOutOfRangeException(nameof(failure))
        }), Is.Null);
    }

    private static IrOpaqueTerm Value(IrFactory factory, string primitive, string? scenario = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(primitive);
        var type = scenario == "wrong-type" ? factory.IntegerType : factory.ObjectType;
        ImmutableArray<IrTerm> arguments = scenario == "argument" ? [factory.Integer(0)] : [];
        var member = factory.GetOrCreateMember(factory.CreateIdentity(),
            scenario == "declaring-type" ? factory.StringType : type,
            scenario == "unowned" ? "ordinary" : IrOpaqueCallSite.DefaultComparerValuePrefix + primitive,
            type, scenario != "instance", [.. arguments.Select(argument => argument is null ? throw new ArgumentNullException(nameof(argument)) : argument.Type)]);
        var receiver = scenario == "instance" ? factory.Null(type) : null;
        return scenario == "impure" ? factory.ImpureOpaque(factory.CreateOperation("ordinary"), member, receiver, arguments.ToArray()) :
            factory.PureOpaque(member, receiver, arguments.ToArray());
    }
}
