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
