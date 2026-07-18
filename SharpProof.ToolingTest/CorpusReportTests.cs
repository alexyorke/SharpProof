using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Tools.CorpusReport;

namespace SharpProof.Test;

[TestFixture]
public class CorpusReportTests
{
    [Test]
    public void CreateFromSarifJson_AggregatesSharpProofCountsAndEvidence()
    {
        var report = SarifCorpusReport.CreateFromSarifJson("sample.sarif", """
                                                                           {
                                                                             "version": "2.1.0",
                                                                             "runs": [
                                                                               {
                                                                                 "results": [
                                                                                   {
                                                                                     "ruleId": "SP0002",
                                                                                     "message": { "text": "impure" },
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "catalog_hit",
                                                                                       "sharpproof.impurity.rule": "MethodInvocationPurityRule",
                                                                                       "sharpproof.impurity.operation_kind": "Invocation",
                                                                                       "sharpproof.impurity.symbol": "System.Console.WriteLine(string)",
                                                                                       "sharpproof.impurity.catalog_source": "known_impure_namespace_or_type",
                                                                                       "sharpproof.impurity.callee_chain": "TestClass.Callee()"
                                                                                     }
                                                                                   },
                                                                                   {
                                                                                     "ruleId": "SP0002",
                                                                                     "message": { "text": "unknown dispatch" },
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "unknown_external_call",
                                                                                       "sharpproof.impurity.operation_kind": "Invocation",
                                                                                       "sharpproof.impurity.symbol": "ITest.Run()"
                                                                                     }
                                                                                   },
                                                                                   {
                                                                                     "ruleId": "SP0004",
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
        Assert.That(report.Sp0002Count, Is.EqualTo(2));
        Assert.That(report.Sp0004Count, Is.EqualTo(1));
        Assert.That(report.TotalSharpProofDiagnostics, Is.EqualTo(3));
        Assert.That(report.ImpurityCategories["catalog_hit"], Is.EqualTo(1));
        Assert.That(report.ImpurityCategories["unknown_external_call"], Is.EqualTo(1));
        Assert.That(report.RuleNames["MethodInvocationPurityRule"], Is.EqualTo(1));
        Assert.That(report.OperationKinds["Invocation"], Is.EqualTo(2));
        Assert.That(report.TopImpureApis[0].Value, Is.EqualTo("ITest.Run()"));
        Assert.That(report.Diagnostics, Has.Length.EqualTo(3));
        Assert.That(report.Diagnostics[0].Input, Is.EqualTo("sample.sarif"));
        Assert.That(report.Diagnostics[0].RuleId, Is.EqualTo("SP0002"));
        Assert.That(report.Diagnostics[0].Message, Is.EqualTo("impure"));
        Assert.That(report.Diagnostics[0].Category, Is.EqualTo("catalog_hit"));
        Assert.That(report.Diagnostics[0].RuleName, Is.EqualTo("MethodInvocationPurityRule"));
        Assert.That(report.Diagnostics[0].OperationKind, Is.EqualTo("Invocation"));
        Assert.That(report.Diagnostics[0].Symbol, Is.EqualTo("System.Console.WriteLine(string)"));
        Assert.That(report.Diagnostics[0].CatalogSource, Is.EqualTo("known_impure_namespace_or_type"));
        Assert.That(report.Diagnostics[0].CalleeChain, Is.EqualTo("TestClass.Callee()"));
        Assert.That(report.Diagnostics[2].RuleId, Is.EqualTo("SP0004"));

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
                                                                                     "ruleId": "SP0010",
                                                                                     "message": { "text": "Method 'TestMethod' can throw: System.ArgumentNullException" },
                                                                                     "properties": {
                                                                                       "sharpproof.exceptions.types": "System.ArgumentNullException",
                                                                                       "sharpproof.exceptions.categories": "effect_summary",
                                                                                       "sharpproof.exceptions.sources": "System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)"
                                                                                     }
                                                                                   },
                                                                                   {
                                                                                     "ruleId": "SP0011",
                                                                                     "message": { "text": "Operation 'LoadAcceptedDocument(voucher)' may throw uncaught exceptions: System.InvalidOperationException" },
                                                                                     "properties": {
                                                                                       "sharpproof.exceptions.symbol": "VoucherService.LoadAcceptedDocument(Voucher)",
                                                                                       "sharpproof.exceptions.types": "System.InvalidOperationException",
                                                                                       "sharpproof.exceptions.categories": "source_callee",
                                                                                       "sharpproof.exceptions.sources": "System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw"
                                                                                     }
                                                                                   }
                                                                                 ]
                                                                               }
                                                                             ]
                                                                           }
                                                                           """);

        Assert.That(report.Sp0010Count, Is.EqualTo(1));
        Assert.That(report.Sp0011Count, Is.EqualTo(1));
        Assert.That(report.TotalSharpProofDiagnostics, Is.EqualTo(2));
        Assert.That(report.ExceptionCategories["effect_summary"], Is.EqualTo(1));
        Assert.That(report.ExceptionCategories["source_callee"], Is.EqualTo(1));
        Assert.That(report.ExceptionSources,
            Does.Contain(new RankedItem(
                "System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)",
                1)));
        Assert.That(report.ExceptionSources,
            Does.Contain(new RankedItem(
                "System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw",
                1)));
        Assert.That(report.Diagnostics[0].RuleId, Is.EqualTo("SP0010"));
        Assert.That(report.Diagnostics[0].ExceptionTypes, Is.EqualTo("System.ArgumentNullException"));
        Assert.That(report.Diagnostics[0].ExceptionCategories, Is.EqualTo("effect_summary"));
        Assert.That(report.Diagnostics[0].ExceptionSources, Does.Contain("System.ArgumentNullException.ThrowIfNull"));
        Assert.That(report.Diagnostics[1].RuleId, Is.EqualTo("SP0011"));
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
                                                                                       "ruleId": "SP0010",
                                                                                       "message": { "text": "Method 'TestMethod' can throw: System.ArgumentNullException" },
                                                                                       "properties": {
                                                                                         "sharpproof.exceptions.types": "System.ArgumentNullException",
                                                                                         "sharpproof.exceptions.categories": "effect_summary",
                                                                                         "sharpproof.exceptions.sources": "System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)",
                                                                                         "sharpproof.exceptions.edges": {{JsonSerializer.Serialize(summaryEdges)}}
                                                                                       }
                                                                                     },
                                                                                     {
                                                                                       "ruleId": "SP0011",
                                                                                       "message": { "text": "Operation 'LoadAcceptedDocument(voucher)' may throw uncaught exceptions: System.InvalidOperationException" },
                                                                                       "properties": {
                                                                                         "sharpproof.exceptions.symbol": "VoucherService.LoadAcceptedDocument(Voucher)",
                                                                                         "sharpproof.exceptions.types": "System.InvalidOperationException",
                                                                                         "sharpproof.exceptions.categories": "source_callee",
                                                                                         "sharpproof.exceptions.sources": "System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw",
                                                                                         "sharpproof.exceptions.edges": {{JsonSerializer.Serialize(siteEdges)}}
                                                                                       }
                                                                                     }
                                                                                   ]
                                                                                 }
                                                                               ]
                                                                             }
                                                                             """);

        Assert.That(report.Sp0010Count, Is.EqualTo(1));
        Assert.That(report.Sp0011Count, Is.EqualTo(1));
        Assert.That(report.ExceptionCategories["effect_summary"], Is.EqualTo(1));
        Assert.That(report.ExceptionCategories["source_callee"], Is.EqualTo(1));
        Assert.That(report.ExceptionSources,
            Does.Contain(new RankedItem(
                "System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)",
                1)));
        Assert.That(report.ExceptionSources,
            Does.Contain(new RankedItem(
                "System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw",
                1)));
        Assert.That(report.Diagnostics[0].ExceptionTypes, Is.EqualTo("System.ArgumentNullException"));
        Assert.That(report.Diagnostics[0].ExceptionCategories, Is.EqualTo("effect_summary"));
        Assert.That(report.Diagnostics[0].ExceptionSources,
            Is.EqualTo(
                "System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull(object, string)"));
        Assert.That(report.Diagnostics[0].ExceptionEdges, Is.EqualTo(summaryEdges));
        Assert.That(report.Diagnostics[1].ExceptionSymbol, Is.EqualTo("VoucherService.LoadAcceptedDocument(Voucher)"));
        Assert.That(report.Diagnostics[1].ExceptionTypes, Is.EqualTo("System.InvalidOperationException"));
        Assert.That(report.Diagnostics[1].ExceptionCategories, Is.EqualTo("source_callee"));
        Assert.That(report.Diagnostics[1].ExceptionSources,
            Is.EqualTo(
                "System.InvalidOperationException=source_callee:VoucherService.LoadAcceptedDocument(Voucher) -> VoucherService.RequireAcceptedDocument(Voucher) -> Voucher.get_AcceptedDocument() -> direct_throw:throw"));
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
                                                   "ruleId": "SP0002",
                                                   "message": { "text": "impure" },
                                                   "properties": {
                                                     "sharpproof.impurity.category": "catalog_hit",
                                                     "sharpproof.impurity.symbol": "System.Console.WriteLine(string)"
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
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "unknown_external_call",
                                                                                       "sharpproof.impurity.operation_kind": "Invocation",
                                                                                       "sharpproof.impurity.symbol": "ExternalLibrary.Hash(byte[])"
                                                                                     }
                                                                                   },
                                                                                   {
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "dynamic_dispatch",
                                                                                       "sharpproof.impurity.operation_kind": "Invocation",
                                                                                       "sharpproof.impurity.symbol": "dynamic.ToString()"
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
        Assert.That(report.FalsePositiveCandidates.Select(item => item.Category),
            Does.Contain("unknown_external_call"));
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
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "unknown_external_call",
                                                                                       "sharpproof.impurity.operation_kind": "Invocation",
                                                                                       "sharpproof.impurity.symbol": "External.Api()"
                                                                                     }
                                                                                   },
                                                                                   {
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "unsupported_operation",
                                                                                       "sharpproof.impurity.operation_kind": "FunctionPointerInvocation",
                                                                                       "sharpproof.impurity.symbol": "External.Api()"
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
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "unsupported_operation",
                                                                                       "sharpproof.impurity.operation_kind": "FunctionPointerInvocation",
                                                                                       "sharpproof.impurity.symbol": "delegate*<void>"
                                                                                     }
                                                                                   },
                                                                                   {
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "unsupported_operation",
                                                                                       "sharpproof.impurity.operation_kind": "FunctionPointerInvocation",
                                                                                       "sharpproof.impurity.symbol": "delegate*<void>"
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
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": " unsupported_operation ",
                                                                                       "sharpproof.impurity.rule": " UnsupportedRule ",
                                                                                       "sharpproof.impurity.operation_kind": " FunctionPointerInvocation ",
                                                                                       "sharpproof.impurity.symbol": " delegate*<void> ",
                                                                                       "sharpproof.impurity.catalog_source": " analyzer ",
                                                                                       "sharpproof.impurity.callee_chain": " Caller -> Callee "
                                                                                     }
                                                                                   },
                                                                                   {
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "unsupported_operation",
                                                                                       "sharpproof.impurity.rule": "UnsupportedRule",
                                                                                       "sharpproof.impurity.operation_kind": "FunctionPointerInvocation",
                                                                                       "sharpproof.impurity.symbol": "delegate*<void>"
                                                                                     }
                                                                                   },
                                                                                   {
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": " unsupported_operation ",
                                                                                       "sharpproof.impurity.operation_kind": " ",
                                                                                       "sharpproof.impurity.symbol": " "
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
        Assert.That(report.FalsePositiveCandidates[0],
            Is.EqualTo(new RankedItem("delegate*<void>", 2, "unsupported_operation")));
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
                                                                                     "ruleId": "SP0002",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "unsupported_operation",
                                                                                       "sharpproof.impurity.rule": "MethodInvocationPurityRule",
                                                                                       "sharpproof.impurity.operation_kind": "Invocation",
                                                                                       "sharpproof.impurity.symbol": "ExternalLibrary.Hash(byte[])"
                                                                                     }
                                                                                   },
                                                                                   {
                                                                                     "ruleId": "SP0009",
                                                                                     "properties": {
                                                                                       "sharpproof.impurity.category": "unsupported_operation",
                                                                                       "sharpproof.impurity.rule": "MethodInvocationPurityRule",
                                                                                       "sharpproof.impurity.operation_kind": "Invocation",
                                                                                       "sharpproof.impurity.symbol": "ExternalLibrary.Hash(byte[])"
                                                                                     }
                                                                                   }
                                                                                 ]
                                                                               }
                                                                             ]
                                                                           }
                                                                           """);

        Assert.That(report.Sp0002Count, Is.EqualTo(1));
        Assert.That(report.Sp0009Count, Is.EqualTo(1));
        Assert.That(report.TotalSharpProofDiagnostics, Is.EqualTo(2));
        Assert.That(report.Diagnostics, Has.Length.EqualTo(2));
        Assert.That(report.ImpurityCategories["unsupported_operation"], Is.EqualTo(1));
        Assert.That(report.RuleNames["MethodInvocationPurityRule"], Is.EqualTo(1));
        Assert.That(report.OperationKinds["Invocation"], Is.EqualTo(1));
        Assert.That(report.UnknownOperationKinds["Invocation"], Is.EqualTo(1));
        Assert.That(report.TopImpureApis[0].Count, Is.EqualTo(1));
        Assert.That(report.CatalogMisses[0].Count, Is.EqualTo(1));
        Assert.That(report.FalsePositiveCandidates[0].Count, Is.EqualTo(1));
    }

    [Test]
    public async Task CorpusReportCli_HelpWithoutInputs_ReturnsSuccess()
    {
        var result = await RunCorpusReportCliAsync("--help");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
        Assert.That(result.StandardError, Does.Contain("Usage: SharpProof.CorpusReport"));
    }

    [Test]
    public async Task CorpusReportCli_MissingOutputValue_ReturnsUsageError()
    {
        var result = await RunCorpusReportCliAsync("--output");

        Assert.That(result.ExitCode, Is.EqualTo(64), result.StandardError);
        Assert.That(result.StandardError, Does.Contain("--output requires a path."));
        Assert.That(result.StandardError, Does.Contain("Usage: SharpProof.CorpusReport"));
    }

    [Test]
    [NonParallelizable]
    public async Task CorpusReportCli_ProjectAndSarifInputs_PreserveOrderAndCleanupMaterializedSarif()
    {
        var repositoryRoot = ReadmeExampleFixture.GetRepositoryRoot();
        var projectPath = Path.Combine(repositoryRoot, "SharpProof.Attributes", "SharpProof.Attributes.csproj");
        var sarifPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid() + ".sarif");
        var temporarySarifBefore = Directory.GetFiles(Path.GetTempPath(), "sharpproof-*.sarif")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            File.WriteAllText(sarifPath, """
                                             {
                                               "version": "2.1.0",
                                               "runs": [{ "results": [] }]
                                             }
                                             """);

            var result = await RunCorpusReportCliAsync(sarifPath, projectPath);

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            using var report = JsonDocument.Parse(result.StandardOutput);
            var inputs = report.RootElement.GetProperty("Inputs")
                .EnumerateArray()
                .Select(static input => input.GetString())
                .ToArray();
            Assert.That(inputs, Is.EqualTo(new[] { sarifPath, projectPath }));
            Assert.That(
                Directory.GetFiles(Path.GetTempPath(), "sharpproof-*.sarif")
                    .Except(temporarySarifBefore, StringComparer.OrdinalIgnoreCase),
                Is.Empty);
        }
        finally
        {
            File.Delete(sarifPath);
        }
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunCorpusReportCliAsync(
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = ReadmeExampleFixture.GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--configuration");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine("Tools", "SharpProof.CorpusReport", "SharpProof.CorpusReport.csproj"));
        startInfo.ArgumentList.Add("--");
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start corpus report CLI.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            process.Kill(true);
            throw;
        }

        return (
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
    }
}
