using NUnit.Framework;

namespace SharpProof.Ir.Test;

[TestFixture]
public sealed class Pr275StringPrinterCoverageTests
{
    [TestCase("", "\"\"")]
    [TestCase("plain text", "\"plain text\"")]
    [TestCase("first\rsecond", "\"first\\rsecond\"")]
    [TestCase("first\tsecond", "\"first\\tsecond\"")]
    [TestCase("caf\u00E9", "\"caf\\u00E9\"")]
    [TestCase("\u03BB", "\"\\u03BB\"")]
    public void StringLiteralDiagnosticsPreserveContentsInTheirCanonicalRepresentation(
        string contents, string expected)
    {
        var factory = new IrFactory();
        var term = factory.String(contents);

        Assert.That(new IrPrinter(factory).Print(term), Is.EqualTo(expected));
        Assert.That(new IrInterpreter(factory).Evaluate(term,
            new Dictionary<IrVarId, IrValue>()).Value!.String, Is.EqualTo(contents));
    }
}
