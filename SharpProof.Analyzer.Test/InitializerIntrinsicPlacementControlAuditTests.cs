using System.Globalization;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class InitializerIntrinsicPlacementControlAuditTests
{
    [Test]
    public async Task ConstructorBodyMisuseActivatesPlacementDiagnostic()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            public class Parent {
                public Parent(int value) { }
            }
            public sealed class Subject : Parent {
                public Subject() : base(1) { _ = Contract.Result<int>(); }
            }
            """, "contracts", []);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        Assert.That(diagnostics.Single().GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("Contract.Result").And.Contain("<placement>")
                .And.Contain("expected use inside Contract.Ensures"));
    }

    [Test]
    public async Task OrdinaryBaseArgumentHasNoPlacementDiagnostic()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using SharpProof.Attributes;
            public class Parent {
                public Parent(int value) { }
            }
            public sealed class Subject : Parent {
                public Subject() : base(1) { }
            }
            """, "contracts", []);
        AnalyzerTestHost.AssertIds(diagnostics);
    }
}
