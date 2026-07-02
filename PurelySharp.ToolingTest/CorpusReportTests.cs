using NUnit.Framework;
using PurelySharp.Tools.CorpusReport;
using System.Text.Json;

namespace PurelySharp.Test
{
    [TestFixture]
    public class CorpusReportTests
    {
        [Test]
        public void CreateFromSarifJson_AggregatesPurelySharpCountsAndEvidence()
        {
            var report = SarifCorpusReport.CreateFromSarifJson("sample.sarif", """
{
  "version": "2.1.0",
  "runs": [
    {
      "results": [
        {
          "ruleId": "PS0002",
          "message": { "text": "impure" },
          "properties": {
            "purelysharp.impurity.category": "catalog_hit",
            "purelysharp.impurity.rule": "MethodInvocationPurityRule",
            "purelysharp.impurity.operation_kind": "Invocation",
            "purelysharp.impurity.symbol": "System.Console.WriteLine(string)",
            "purelysharp.impurity.catalog_source": "known_impure_namespace_or_type",
            "purelysharp.impurity.callee_chain": "TestClass.Callee()"
          }
        },
        {
          "ruleId": "PS0002",
          "message": { "text": "unknown dispatch" },
          "properties": {
            "purelysharp.impurity.category": "unknown_external_call",
            "purelysharp.impurity.operation_kind": "Invocation",
            "purelysharp.impurity.symbol": "ITest.Run()"
          }
        },
        {
          "ruleId": "PS0004",
          "message": { "text": "missing purity" }
        },
        {
          "ruleId": "CS0168",
          "message": { "text": "compiler diagnostic ignored" }
        }
      ]
    }
  ]
}
""");

            Assert.That(report.Inputs, Is.EqualTo(new[] { "sample.sarif" }));
            Assert.That(report.SchemaVersion, Is.EqualTo(CorpusReportSummary.CurrentSchemaVersion));
            Assert.That(report.Ps0002Count, Is.EqualTo(2));
            Assert.That(report.Ps0004Count, Is.EqualTo(1));
            Assert.That(report.TotalPurelySharpDiagnostics, Is.EqualTo(3));
            Assert.That(report.ImpurityCategories["catalog_hit"], Is.EqualTo(1));
            Assert.That(report.ImpurityCategories["unknown_external_call"], Is.EqualTo(1));
            Assert.That(report.RuleNames["MethodInvocationPurityRule"], Is.EqualTo(1));
            Assert.That(report.OperationKinds["Invocation"], Is.EqualTo(2));
            Assert.That(report.TopImpureApis[0].Value, Is.EqualTo("ITest.Run()"));
            Assert.That(report.Diagnostics, Has.Length.EqualTo(3));
            Assert.That(report.Diagnostics[0].Input, Is.EqualTo("sample.sarif"));
            Assert.That(report.Diagnostics[0].RuleId, Is.EqualTo("PS0002"));
            Assert.That(report.Diagnostics[0].Message, Is.EqualTo("impure"));
            Assert.That(report.Diagnostics[0].Category, Is.EqualTo("catalog_hit"));
            Assert.That(report.Diagnostics[0].RuleName, Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(report.Diagnostics[0].OperationKind, Is.EqualTo("Invocation"));
            Assert.That(report.Diagnostics[0].Symbol, Is.EqualTo("System.Console.WriteLine(string)"));
            Assert.That(report.Diagnostics[0].CatalogSource, Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(report.Diagnostics[0].CalleeChain, Is.EqualTo("TestClass.Callee()"));
            Assert.That(report.Diagnostics[2].RuleId, Is.EqualTo("PS0004"));

            var json = JsonSerializer.Serialize(report);
            Assert.That(json, Does.Contain(@"""SchemaVersion"":""" + CorpusReportSummary.CurrentSchemaVersion + @""""));
        }

        [Test]
        public void CreateFromSarifJson_AggregatesExceptionFlowEvidence()
        {
            var report = SarifCorpusReport.CreateFromSarifJson("sample.sarif", """
{
  "version": "2.1.0",
  "runs": [
    {
      "results": [
        {
          "ruleId": "PS0010",
          "message": { "text": "Method 'TestMethod' can throw: System.ArgumentNullException" },
          "properties": {
            "purelysharp.exceptions.types": "System.ArgumentNullException",
            "purelysharp.exceptions.categories": "effect_summary",
            "purelysharp.exceptions.sources": "System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)"
          }
        },
        {
          "ruleId": "PS0011",
          "message": { "text": "Operation 'LoadAcceptedDocument(voucher)' may throw uncaught exceptions: System.InvalidOperationException" },
          "properties": {
            "purelysharp.exceptions.symbol": "VoucherService.LoadAcceptedDocument(Voucher)",
            "purelysharp.exceptions.types": "System.InvalidOperationException",
            "purelysharp.exceptions.categories": "source_callee",
            "purelysharp.exceptions.sources": "System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw"
          }
        }
      ]
    }
  ]
}
""");

            Assert.That(report.Ps0010Count, Is.EqualTo(1));
            Assert.That(report.Ps0011Count, Is.EqualTo(1));
            Assert.That(report.TotalPurelySharpDiagnostics, Is.EqualTo(2));
            Assert.That(report.ExceptionCategories["effect_summary"], Is.EqualTo(1));
            Assert.That(report.ExceptionCategories["source_callee"], Is.EqualTo(1));
            Assert.That(report.ExceptionSources, Does.Contain(new RankedItem("System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)", 1)));
            Assert.That(report.ExceptionSources, Does.Contain(new RankedItem("System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw", 1)));
            Assert.That(report.Diagnostics[0].RuleId, Is.EqualTo("PS0010"));
            Assert.That(report.Diagnostics[0].ExceptionTypes, Is.EqualTo("System.ArgumentNullException"));
            Assert.That(report.Diagnostics[0].ExceptionCategories, Is.EqualTo("effect_summary"));
            Assert.That(report.Diagnostics[0].ExceptionSources, Does.Contain("System.ArgumentNullException.ThrowIfNull"));
            Assert.That(report.Diagnostics[1].RuleId, Is.EqualTo("PS0011"));
            Assert.That(report.Diagnostics[1].ExceptionSymbol, Is.EqualTo("VoucherService.LoadAcceptedDocument(Voucher)"));
            Assert.That(report.Diagnostics[1].ExceptionTypes, Is.EqualTo("System.InvalidOperationException"));
            Assert.That(report.Diagnostics[1].ExceptionCategories, Is.EqualTo("source_callee"));
        }

        [Test]
        public void CreateFromSarifJson_PreservesExceptionEdges_WithoutChangingLegacyExceptionFields()
        {
            const string summaryEdges = """
[{"ExceptionType":"System.ArgumentNullException","Category":"effect_summary","SourcePath":"System.ArgumentNullException.ThrowIfNull(object, string)","CalleeExactSymbolKey":"System.ArgumentNullException.ThrowIfNull(System.Object,System.String)","Depth":0}]
""";
            const string siteEdges = """
[{"ExceptionType":"System.InvalidOperationException","Category":"source_callee","SourcePath":"VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw","CalleeExactSymbolKey":"Voucher.get_AcceptedDocument()","Depth":2}]
""";

            var report = SarifCorpusReport.CreateFromSarifJson("sample.sarif", $$"""
{
  "version": "2.1.0",
  "runs": [
    {
      "results": [
        {
          "ruleId": "PS0010",
          "message": { "text": "Method 'TestMethod' can throw: System.ArgumentNullException" },
          "properties": {
            "purelysharp.exceptions.types": "System.ArgumentNullException",
            "purelysharp.exceptions.categories": "effect_summary",
            "purelysharp.exceptions.sources": "System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)",
            "purelysharp.exceptions.edges": {{JsonSerializer.Serialize(summaryEdges)}}
          }
        },
        {
          "ruleId": "PS0011",
          "message": { "text": "Operation 'LoadAcceptedDocument(voucher)' may throw uncaught exceptions: System.InvalidOperationException" },
          "properties": {
            "purelysharp.exceptions.symbol": "VoucherService.LoadAcceptedDocument(Voucher)",
            "purelysharp.exceptions.types": "System.InvalidOperationException",
            "purelysharp.exceptions.categories": "source_callee",
            "purelysharp.exceptions.sources": "System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw",
            "purelysharp.exceptions.edges": {{JsonSerializer.Serialize(siteEdges)}}
          }
        }
      ]
    }
  ]
}
""");

            Assert.That(report.Ps0010Count, Is.EqualTo(1));
            Assert.That(report.Ps0011Count, Is.EqualTo(1));
            Assert.That(report.ExceptionCategories["effect_summary"], Is.EqualTo(1));
            Assert.That(report.ExceptionCategories["source_callee"], Is.EqualTo(1));
            Assert.That(report.ExceptionSources, Does.Contain(new RankedItem("System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)", 1)));
            Assert.That(report.ExceptionSources, Does.Contain(new RankedItem("System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw", 1)));
            Assert.That(report.Diagnostics[0].ExceptionTypes, Is.EqualTo("System.ArgumentNullException"));
            Assert.That(report.Diagnostics[0].ExceptionCategories, Is.EqualTo("effect_summary"));
            Assert.That(report.Diagnostics[0].ExceptionSources, Is.EqualTo("System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)"));
            Assert.That(report.Diagnostics[0].ExceptionEdges, Is.EqualTo(summaryEdges));
            Assert.That(report.Diagnostics[1].ExceptionSymbol, Is.EqualTo("VoucherService.LoadAcceptedDocument(Voucher)"));
            Assert.That(report.Diagnostics[1].ExceptionTypes, Is.EqualTo("System.InvalidOperationException"));
            Assert.That(report.Diagnostics[1].ExceptionCategories, Is.EqualTo("source_callee"));
            Assert.That(report.Diagnostics[1].ExceptionSources, Is.EqualTo("System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw"));
            Assert.That(report.Diagnostics[1].ExceptionEdges, Is.EqualTo(siteEdges));
        }

        [Test]
        public void CreateFromNamedSarifFiles_UsesStableInputNameForReportRows()
        {
            var sarifPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".sarif");
            try
            {
                File.WriteAllText(sarifPath, """
{
  "version": "2.1.0",
  "runs": [
    {
      "results": [
        {
          "ruleId": "PS0002",
          "message": { "text": "impure" },
          "properties": {
            "purelysharp.impurity.category": "catalog_hit",
            "purelysharp.impurity.symbol": "System.Console.WriteLine(string)"
          }
        }
      ]
    }
  ]
}
""");

                var report = SarifCorpusReport.CreateFromSarifFiles(
                    new[] { new SarifCorpusInput("src/App/App.csproj", sarifPath) });

                Assert.That(report.Inputs, Is.EqualTo(new[] { "src/App/App.csproj" }));
                Assert.That(report.Diagnostics[0].Input, Is.EqualTo("src/App/App.csproj"));
            }
            finally
            {
                File.Delete(sarifPath);
            }
        }

        [Test]
        public void CreateFromSarifJson_IdentifiesCatalogMissesAndFalsePositiveCandidates()
        {
            var report = SarifCorpusReport.CreateFromSarifJson("sample.sarif", """
{
  "version": "2.1.0",
  "runs": [
    {
      "results": [
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "unknown_external_call",
            "purelysharp.impurity.operation_kind": "Invocation",
            "purelysharp.impurity.symbol": "ExternalLibrary.Hash(byte[])"
          }
        },
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "dynamic_dispatch",
            "purelysharp.impurity.operation_kind": "Invocation",
            "purelysharp.impurity.symbol": "dynamic.ToString()"
          }
        }
      ]
    }
  ]
}
""");

            Assert.That(report.CatalogMisses, Has.Length.EqualTo(1));
            Assert.That(report.CatalogMisses[0].Value, Is.EqualTo("ExternalLibrary.Hash(byte[])"));
            Assert.That(report.FalsePositiveCandidates, Has.Length.EqualTo(2));
            Assert.That(report.FalsePositiveCandidates.Select(item => item.Category), Does.Contain("unknown_external_call"));
            Assert.That(report.FalsePositiveCandidates.Select(item => item.Category), Does.Contain("dynamic_dispatch"));
        }

        [Test]
        public void CreateFromSarifJson_PreservesCatalogMissCategoriesForSameSymbol()
        {
            var report = SarifCorpusReport.CreateFromSarifJson("sample.sarif", """
{
  "version": "2.1.0",
  "runs": [
    {
      "results": [
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "unknown_external_call",
            "purelysharp.impurity.operation_kind": "Invocation",
            "purelysharp.impurity.symbol": "External.Api()"
          }
        },
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "unsupported_operation",
            "purelysharp.impurity.operation_kind": "FunctionPointerInvocation",
            "purelysharp.impurity.symbol": "External.Api()"
          }
        }
      ]
    }
  ]
}
""");

            Assert.That(report.CatalogMisses, Has.Length.EqualTo(2));
            Assert.That(report.CatalogMisses, Does.Contain(new RankedItem("External.Api()", 1, "unknown_external_call")));
            Assert.That(report.CatalogMisses, Does.Contain(new RankedItem("External.Api()", 1, "unsupported_operation")));
        }

        [Test]
        public void CreateFromSarifJson_AggregatesUnknownOperationKinds()
        {
            var report = SarifCorpusReport.CreateFromSarifJson("sample.sarif", """
{
  "version": "2.1.0",
  "runs": [
    {
      "results": [
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "unsupported_operation",
            "purelysharp.impurity.operation_kind": "FunctionPointerInvocation",
            "purelysharp.impurity.symbol": "delegate*<void>"
          }
        },
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "unsupported_operation",
            "purelysharp.impurity.operation_kind": "FunctionPointerInvocation",
            "purelysharp.impurity.symbol": "delegate*<void>"
          }
        }
      ]
    }
  ]
}
""");

            Assert.That(report.UnknownOperationKinds["FunctionPointerInvocation"], Is.EqualTo(2));
        }

        [Test]
        public void CreateFromSarifJson_NormalizesEvidencePropertiesBeforeAggregating()
        {
            var report = SarifCorpusReport.CreateFromSarifJson("sample.sarif", """
{
  "version": "2.1.0",
  "runs": [
    {
      "results": [
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": " unsupported_operation ",
            "purelysharp.impurity.rule": " UnsupportedRule ",
            "purelysharp.impurity.operation_kind": " FunctionPointerInvocation ",
            "purelysharp.impurity.symbol": " delegate*<void> ",
            "purelysharp.impurity.catalog_source": " analyzer ",
            "purelysharp.impurity.callee_chain": " Caller -> Callee "
          }
        },
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "unsupported_operation",
            "purelysharp.impurity.rule": "UnsupportedRule",
            "purelysharp.impurity.operation_kind": "FunctionPointerInvocation",
            "purelysharp.impurity.symbol": "delegate*<void>"
          }
        },
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": " unsupported_operation ",
            "purelysharp.impurity.operation_kind": " ",
            "purelysharp.impurity.symbol": " "
          }
        }
      ]
    }
  ]
}
""");

            Assert.That(report.ImpurityCategories["unsupported_operation"], Is.EqualTo(3));
            Assert.That(report.RuleNames["UnsupportedRule"], Is.EqualTo(2));
            Assert.That(report.UnknownOperationKinds["FunctionPointerInvocation"], Is.EqualTo(2));
            Assert.That(report.TopImpureApis, Has.Length.EqualTo(1));
            Assert.That(report.TopImpureApis[0], Is.EqualTo(new RankedItem("delegate*<void>", 2)));
            Assert.That(report.CatalogMisses[0], Is.EqualTo(new RankedItem("delegate*<void>", 2, "unsupported_operation")));
            Assert.That(report.FalsePositiveCandidates[0], Is.EqualTo(new RankedItem("delegate*<void>", 2, "unsupported_operation")));
            Assert.That(report.Diagnostics[0].Category, Is.EqualTo("unsupported_operation"));
            Assert.That(report.Diagnostics[0].OperationKind, Is.EqualTo("FunctionPointerInvocation"));
            Assert.That(report.Diagnostics[0].Symbol, Is.EqualTo("delegate*<void>"));
            Assert.That(report.Diagnostics[0].CatalogSource, Is.EqualTo("analyzer"));
            Assert.That(report.Diagnostics[0].CalleeChain, Is.EqualTo("Caller -> Callee"));
            Assert.That(report.Diagnostics[2].OperationKind, Is.Null);
            Assert.That(report.Diagnostics[2].Symbol, Is.Null);
        }

        [Test]
        public void CreateFromSarifJson_DoesNotDoubleCountExplanationEvidence()
        {
            var report = SarifCorpusReport.CreateFromSarifJson("sample.sarif", """
{
  "version": "2.1.0",
  "runs": [
    {
      "results": [
        {
          "ruleId": "PS0002",
          "properties": {
            "purelysharp.impurity.category": "unsupported_operation",
            "purelysharp.impurity.rule": "MethodInvocationPurityRule",
            "purelysharp.impurity.operation_kind": "Invocation",
            "purelysharp.impurity.symbol": "ExternalLibrary.Hash(byte[])"
          }
        },
        {
          "ruleId": "PS0009",
          "properties": {
            "purelysharp.impurity.category": "unsupported_operation",
            "purelysharp.impurity.rule": "MethodInvocationPurityRule",
            "purelysharp.impurity.operation_kind": "Invocation",
            "purelysharp.impurity.symbol": "ExternalLibrary.Hash(byte[])"
          }
        }
      ]
    }
  ]
}
""");

            Assert.That(report.Ps0002Count, Is.EqualTo(1));
            Assert.That(report.Ps0009Count, Is.EqualTo(1));
            Assert.That(report.TotalPurelySharpDiagnostics, Is.EqualTo(2));
            Assert.That(report.Diagnostics, Has.Length.EqualTo(2));
            Assert.That(report.ImpurityCategories["unsupported_operation"], Is.EqualTo(1));
            Assert.That(report.RuleNames["MethodInvocationPurityRule"], Is.EqualTo(1));
            Assert.That(report.OperationKinds["Invocation"], Is.EqualTo(1));
            Assert.That(report.UnknownOperationKinds["Invocation"], Is.EqualTo(1));
            Assert.That(report.TopImpureApis[0].Count, Is.EqualTo(1));
            Assert.That(report.CatalogMisses[0].Count, Is.EqualTo(1));
            Assert.That(report.FalsePositiveCandidates[0].Count, Is.EqualTo(1));
        }
    }
}
