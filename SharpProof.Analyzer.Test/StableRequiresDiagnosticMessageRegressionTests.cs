using System.Globalization;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class StableRequiresDiagnosticMessageRegressionTests
{
    [Test]
    public async Task RequiresDiagnosticUsesStableSourceClauseText()
    {
        var plainDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateSource(includeUnrelatedCallerContract: false, argument: 0),
            "contracts",
            ["SP0027"]);
        var shiftedDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateSource(includeUnrelatedCallerContract: true, argument: 0),
            "contracts",
            ["SP0027"]);
        var passingDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateSource(includeUnrelatedCallerContract: true, argument: 1),
            "contracts",
            ["SP0027"]);

        var plain = plainDiagnostics.Single(static diagnostic =>
            diagnostic.Id == "SP0027");
        var shifted = shiftedDiagnostics.Single(static diagnostic =>
            diagnostic.Id == "SP0027");
        var plainMessage = plain.GetMessage(CultureInfo.InvariantCulture);
        var shiftedMessage = shifted.GetMessage(CultureInfo.InvariantCulture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(plainMessage, Is.EqualTo(shiftedMessage));
            Assert.That(plainMessage, Does.Contain("value"));
            Assert.That(plainMessage, Does.Contain("value > 0"));
            Assert.That(plainMessage, Does.Not.Contain("#t"));
            Assert.That(
                passingDiagnostics,
                Has.None.Matches<Diagnostic>(static diagnostic =>
                    diagnostic.Id == "SP0027"));
        }
    }

    [Test]
    public async Task ClosedAttributeDiagnosticUsesStableAttributeAndParameterText()
    {
        var plainDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateAttributeSource(includeUnrelatedCallerContract: false, argument: 0),
            "contracts",
            ["SP0027"]);
        var shiftedDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateAttributeSource(includeUnrelatedCallerContract: true, argument: 0),
            "contracts",
            ["SP0027"]);
        var passingDiagnostics = await AnalyzerTestHost.AnalyzeAsync(
            CreateAttributeSource(includeUnrelatedCallerContract: true, argument: 1),
            "contracts",
            ["SP0027"]);

        var plain = plainDiagnostics.Single(static diagnostic =>
            diagnostic.Id == "SP0027");
        var shifted = shiftedDiagnostics.Single(static diagnostic =>
            diagnostic.Id == "SP0027");
        var plainMessage = plain.GetMessage(CultureInfo.InvariantCulture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                plainMessage,
                Is.EqualTo(shifted.GetMessage(CultureInfo.InvariantCulture)));
            Assert.That(plainMessage, Does.Contain("[Positive] value"));
            Assert.That(plainMessage, Does.Not.Contain("#t"));
            Assert.That(
                passingDiagnostics,
                Has.None.Matches<Diagnostic>(static diagnostic =>
                    diagnostic.Id == "SP0027"));
        }
    }

    [Test]
    public async Task CompanionRequiresDiagnosticUsesCompanionSourceText()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public interface IService {
                int Find(int value);
            }

            [ContractFor(typeof(IService))]
            public static class ServiceContracts {
                public static int Find(IService receiver, int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }

            public sealed class Service : IService {
                public int Find(int value) => value;
            }

            public static class Caller {
                public static int Call(IService service) => service.Find(0);
            }
            """,
            "contracts",
            ["SP0027"]);

        var diagnostic = diagnostics.Single(static item => item.Id == "SP0027");
        var message = diagnostic.GetMessage(CultureInfo.InvariantCulture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("value > 0"));
            Assert.That(message, Does.Not.Contain("#t"));
        }
    }

    [Test]
    public async Task OversizedSourceClauseUsesBoundedDiagnosticText()
    {
        var condition = string.Join(
            " && ",
            Enumerable.Repeat("value > 0", 100));
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            $$"""
            using SharpProof.Attributes;

            public static class Fixture {
                public static int Positive(int value) {
                    Contract.Requires({{condition}});
                    return value;
                }

                public static int Caller() => Positive(0);
            }
            """,
            "contracts",
            ["SP0027"]);

        var message = diagnostics.Single(static item => item.Id == "SP0027")
            .GetMessage(CultureInfo.InvariantCulture);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(message, Does.Contain("condition exceeds the display limit"));
            Assert.That(message.Length, Is.LessThan(128));
        }
    }

    private static string CreateSource(
        bool includeUnrelatedCallerContract,
        int argument)
    {
        var unrelatedContract = includeUnrelatedCallerContract
            ? "Contract.Requires(unrelated > 0);"
            : string.Empty;
        return $$"""
            using SharpProof.Attributes;

            public static class Fixture {
                public static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int Caller(int unrelated) {
                    {{unrelatedContract}}
                    return Positive({{argument}});
                }
            }
            """;
    }

    private static string CreateAttributeSource(
        bool includeUnrelatedCallerContract,
        int argument)
    {
        var unrelatedContract = includeUnrelatedCallerContract
            ? "Contract.Requires(unrelated > 0);"
            : string.Empty;
        return $$"""
            using SharpProof.Attributes;

            public static class Fixture {
                public static int Positive([Positive] int value) => value;

                public static int Caller(int unrelated) {
                    {{unrelatedContract}}
                    return Positive({{argument}});
                }
            }
            """;
    }
}
