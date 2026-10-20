using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

internal sealed class DictionaryInitializationReplayTests
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
    public void PrimitiveReadinessRequiresAnActualSuccessfulFrameworkObservation(string primitive)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        var (factory, request) = Request(primitive, IrHavocOrigin.Approximation, wrongType: false);
        var result = PassiveCallableVcPlan.ObserveDictionaryInitialization(factory, request, PassiveCallableVcPlan.WarmDefaultDictionary);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Boolean, Is.False);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2201", Justification = "Inject a failed observation without exhausting process memory.")]
    [TestCase(false)]
    [TestCase(true)]
    public void FailedInitializationNeverCertifiesNormalReplay(bool allocation)
    {
        var (factory, request) = Request("System_Int32", IrHavocOrigin.Approximation, wrongType: false);
        Assert.That(PassiveCallableVcPlan.ObserveDictionaryInitialization(factory, request, primitive =>
        {
            ArgumentNullException.ThrowIfNull(primitive);
            if (allocation)
            { throw new OutOfMemoryException(); }
            throw new TypeInitializationException("FrameworkProbe", new InvalidOperationException());
        }), Is.Null);
    }

    [TestCase(IrHavocOrigin.Input, false)]
    [TestCase(IrHavocOrigin.SpecResult, false)]
    [TestCase(IrHavocOrigin.Approximation, true)]
    public void UnapprovedHavocsNeverExecuteTheProbe(IrHavocOrigin origin, bool wrongType)
    {
        var (factory, request) = Request("System_Int32", origin, wrongType);
        var invoked = false;
        Assert.That(PassiveCallableVcPlan.ObserveDictionaryInitialization(factory, request, primitive =>
        {
            ArgumentNullException.ThrowIfNull(primitive);
            invoked = true;
            return true;
        }), Is.Null);
        Assert.That(invoked, Is.False);
    }

    [Test]
    public void AnObservationThatDisagreesWithTheExpectedStateNeverCertifiesReplay()
    {
        var (factory, request) = Request("System_Int32", IrHavocOrigin.Approximation, wrongType: false);
        Assert.That(PassiveCallableVcPlan.ObserveDictionaryInitialization(factory, request, primitive =>
        {
            ArgumentNullException.ThrowIfNull(primitive);
            return false;
        }), Is.Null);
    }

    [Test]
    public void UnknownAndReferenceKeyTypesCannotBeObserved()
    {
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => IrOpaqueCallSite.DictionaryInitialization("System_String")));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() => PassiveCallableVcPlan.WarmDefaultDictionary("System_String")));
    }

    private static (IrFactory Factory, IrHavocRequest Request) Request(string primitive, IrHavocOrigin origin, bool wrongType)
    {
        ArgumentNullException.ThrowIfNull(primitive);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("value", wrongType ? factory.IntegerType : factory.BooleanType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var site = factory.CreateOperation(IrOpaqueCallSite.DictionaryInitialization(primitive));
        var havoc = builder.Havoc(block, site, IrHavocKind.Variables, origin, variable);
        return (factory, new IrHavocRequest(havoc.Id, site, variable, 0, origin));
    }
}
