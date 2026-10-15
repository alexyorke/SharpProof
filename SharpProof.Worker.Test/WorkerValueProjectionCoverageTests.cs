using System.Text.Json;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerValueProjectionCoverageTests
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

}
