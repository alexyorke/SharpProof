using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class NullableValueTypeReceiverRegressionTests
{
    [Test]
    public async Task NullableValueTypeCallsDoNotGetReferenceNullChecks()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                [DoesNotThrow]
                public static bool HasValue(int? value) => value.HasValue;

                [DoesNotThrow]
                public static int GetDefault(int? value) =>
                    value.GetValueOrDefault();

                [DoesNotThrow]
                public static int GetDefaultWithFallback(int? value) =>
                    value.GetValueOrDefault(7);

                [DoesNotThrow]
                public static bool LongHasValue(long? value) => value.HasValue;

                [ZeroAllocations]
                public static bool HasValueAllocation(int? value) =>
                    value.HasValue;

                [DoesNotThrow]
                public static int Unwrap(int? value) => value.Value;

                [DoesNotThrow]
                public static System.Type GetTypeOfNullable(int? value) =>
                    value.GetType();

                [DoesNotThrow]
                public static int ReferenceLength(string value) =>
                    value.Length;
            }
            """,
            "effects",
            ["SP0045", "SP0046"]);

        AnalyzerTestHost.AssertIds(
            diagnostics,
            "SP0046",
            "SP0046",
            "SP0046");
        var messages = diagnostics.Select(static diagnostic =>
            diagnostic.GetMessage(CultureInfo.InvariantCulture)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                messages.Single(static message =>
                    message.Contains("'Unwrap'", StringComparison.Ordinal)),
                Does.Contain("InvalidOperationException"));
            Assert.That(
                messages.Single(static message =>
                    message.Contains("'Unwrap'", StringComparison.Ordinal)),
                Does.Not.Contain("NullReferenceException"));
            Assert.That(
                messages.Single(static message =>
                    message.Contains(
                        "'GetTypeOfNullable'",
                        StringComparison.Ordinal)),
                Does.Contain("ExceptionSetUnknown"));
            Assert.That(
                messages.Single(static message =>
                    message.Contains(
                        "'ReferenceLength'",
                        StringComparison.Ordinal)),
                Does.Contain("NullReferenceException"));
        }
    }
}
