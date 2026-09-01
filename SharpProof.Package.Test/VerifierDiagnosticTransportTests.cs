using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class VerifierDiagnosticTransportTests
{
    [TestCase("schema", "1.5")]
    [TestCase("schema", "2147483648")]
    [TestCase("line", "1.5")]
    [TestCase("line", "2147483648")]
    [TestCase("column", "1.5")]
    [TestCase("column", "2147483648")]
    public void MalformedNumericFieldsReturnFalseWithoutThrowing(
        string field,
        string malformedValue)
    {
        var schema = field == "schema" ? malformedValue : "1";
        var line = field == "line" ? malformedValue : "2";
        var column = field == "column" ? malformedValue : "3";
        var payload = VerifierDiagnosticTransport.Prefix +
            $"{{\"schema\":{schema},\"severity\":\"warning\"," +
            "\"code\":\"SP0048\",\"file\":\"source.cs\"," +
            $"\"line\":{line},\"column\":{column},\"message\":\"message\"}}";
        VerifierDiagnostic diagnostic = null!;
        var result = false;

        Assert.DoesNotThrow((Action)(() =>
        {
            result = VerifierDiagnosticTransport.TryDeserialize(
                payload,
                out diagnostic);
        }));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result, Is.False);
            Assert.That(diagnostic, Is.Null);
        }
    }
}
