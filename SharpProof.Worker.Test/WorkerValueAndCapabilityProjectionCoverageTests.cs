using System.Text.Json;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerValueAndCapabilityProjectionCoverageTests
{
    [TestCase("")]
    [TestCase("plain value")]
    [TestCase("  ")]
    public void FactoryCreatedStringsRetainTheirRawAndProtocolPresentations(string text)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var value = factory.CreateStringValue(text);
        var raw = WorkerProjections.FormatValue(value);
        var transported = WorkerProjections.FormatTotalValue(value);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(raw, Is.EqualTo(("String", text)));
            Assert.That(transported.Kind, Is.EqualTo("String"));
            Assert.That(transported.Value, Is.Not.Empty);
            Assert.That(JsonSerializer.Deserialize<string>(transported.Value), Is.EqualTo(text));
        }
    }

    [TestCase((int)IrOpaqueCallEffects.None, WorkerEffectCapabilitySet.None)]
    [TestCase((int)IrOpaqueCallEffects.All, WorkerEffectCapabilitySet.AllKnown)]
    [TestCase((int)IrOpaqueCallEffects.InputOutput, WorkerEffectCapabilitySet.IO)]
    [TestCase((int)IrOpaqueCallEffects.Nondeterminism, WorkerEffectCapabilitySet.Randomness)]
    [TestCase((int)IrOpaqueCallEffects.NativeCode, WorkerEffectCapabilitySet.NativeInterop)]
    [TestCase((int)IrOpaqueCallEffects.Reflection, WorkerEffectCapabilitySet.Reflection)]
    public void UnqualifiedEffectFlagsPreserveTheirCapabilityDomains(
        int effects, WorkerEffectCapabilitySet expected)
    {
        Assert.That(NativeEffectSiteVerifier.Capabilities((IrOpaqueCallEffects)effects), Is.EqualTo(expected));
    }
}
