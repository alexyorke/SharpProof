using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ContractIntrinsicValidationTests
{
    [Test]
    public async Task ResultInsideOldReportsNestingForDirectContract()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public static class Target {
                public static int Read(int value) {
                    Contract.Ensures(
                        Contract.Old(Contract.Result<int>()) == value);
                    return value;
                }
            }
            """,
            "contracts",
            ["SP0024"]);

        AssertNestingDiagnostic(diagnostics);
    }

    [Test]
    public async Task ResultInsideOldReportsNestingForCompanionContract()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public interface Target {
                int Read(int value);
            }

            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static int Read(
                    Target receiver,
                    int value) {
                    Contract.Ensures(
                        Contract.Old(Contract.Result<int>()) == value);
                    return value;
                }
            }
            """,
            "contracts",
            ["SP0024"]);

        AssertNestingDiagnostic(diagnostics);
    }

    [Test]
    public async Task CompanionNestedCallableIntrinsicsAreValidated()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;

            public interface Target {
                int Read(int value);
            }

            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static int Read(Target receiver, int value) {
                    int Invalid() => Contract.Result<int>();
                    return Invalid();
                }
            }
            """,
            "contracts",
            ["SP0024"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0024"]));
        Assert.That(
            diagnostics.Single().GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("Contract.Result")
                .And.Contain("expected use inside Contract.Ensures"));
    }

    [Test]
    public async Task IndirectIntrinsicCallsReportPlacementDiagnostics()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Target {
                public static int Read(int value) {
                    Func<int> result = Contract.Result<int>;
                    Func<int, int> old = Contract.Old<int>;
                    return result() + old(value);
                }
            }
            """,
            "contracts",
            ["SP0024"]);

        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(Enumerable.Repeat("SP0024", 2)));
        var messages = diagnostics.Select(diagnostic =>
                diagnostic.GetMessage(CultureInfo.InvariantCulture))
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                messages[0],
                Does.Contain("Contract.Result").And.Contain("<placement>"));
            Assert.That(
                messages[1],
                Does.Contain("Contract.Old").And.Contain("<placement>"));
        }
    }

    private static void AssertNestingDiagnostic(
        IReadOnlyCollection<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
    {
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0024"]));
        Assert.That(
            diagnostics.Select(diagnostic =>
                diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Has.All.Contains("Contract.Result")
                .And.All.Contains("<nesting>")
                .And.All.Contains("nested inside Contract.Old")
                .And.None.Contains("<signature>"));
    }
}
