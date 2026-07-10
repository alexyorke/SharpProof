using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class RecursiveExceptionFlowTests
{
    [Test]
    public async Task Sp0010AndSp0011_RecursiveAcceptedDocumentChain_PreserveTopLevelExceptionEvidence()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
                                                                     using System;

                                                                     public sealed class VoucherService
                                                                     {
                                                                         public AcceptedDocument Render(Voucher voucher)
                                                                         {
                                                                             return LoadAcceptedDocument(voucher);
                                                                         }

                                                                         private static AcceptedDocument LoadAcceptedDocument(Voucher voucher)
                                                                         {
                                                                             return RequireAcceptedDocument(voucher);
                                                                         }

                                                                         private static AcceptedDocument RequireAcceptedDocument(Voucher voucher)
                                                                         {
                                                                             return voucher.AcceptedDocument;
                                                                         }
                                                                     }

                                                                     public sealed class Voucher
                                                                     {
                                                                         private readonly AcceptedDocument? _acceptedDocument;

                                                                         public Voucher(AcceptedDocument? acceptedDocument)
                                                                         {
                                                                             _acceptedDocument = acceptedDocument;
                                                                         }

                                                                         public AcceptedDocument AcceptedDocument
                                                                         {
                                                                             get
                                                                             {
                                                                                 if (_acceptedDocument is null)
                                                                                 {
                                                                                     throw new InvalidOperationException();
                                                                                 }

                                                                                 return _acceptedDocument;
                                                                             }
                                                                         }
                                                                     }

                                                                     public sealed class AcceptedDocument
                                                                     {
                                                                     }
                                                                     """,
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_report_exceptions", "true")
                .Add("sharpproof_checked_exceptions", "true"));

        var summaryDiagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId)
            .Single(d => d.GetMessage().Contains("'Render'", StringComparison.Ordinal));

        var summarySources = summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty];
        Assert.That(summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(summarySources, Does.Contain("VoucherService.LoadAcceptedDocument(Voucher)"));
        Assert.That(summarySources, Does.Contain("VoucherService.RequireAcceptedDocument(Voucher)"));
        Assert.That(summarySources, Does.Contain("Voucher.AcceptedDocument.get"));
        Assert.That(summarySources, Does.Contain("direct_throw:throw"));

        var siteDiagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId)
            .Single(d => d.GetMessage().Contains("LoadAcceptedDocument(voucher)", StringComparison.Ordinal));

        var siteSources = siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty];
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("source_callee"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSymbolProperty],
            Does.Contain("LoadAcceptedDocument"));
        Assert.That(siteSources, Does.Contain("VoucherService.LoadAcceptedDocument(Voucher)"));
        Assert.That(siteSources, Does.Contain("VoucherService.RequireAcceptedDocument(Voucher)"));
        Assert.That(siteSources, Does.Contain("Voucher.AcceptedDocument.get"));
        Assert.That(siteSources, Does.Contain("direct_throw:throw"));
    }

    [Test]
    public async Task Sp0011_RecursiveAcceptedDocumentChain_CheckedOnly_PreservesTopLevelExceptionEvidence()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
                                                                     using System;

                                                                     public sealed class VoucherService
                                                                     {
                                                                         public AcceptedDocument Render(Voucher voucher)
                                                                         {
                                                                             return LoadAcceptedDocument(voucher);
                                                                         }

                                                                         private static AcceptedDocument LoadAcceptedDocument(Voucher voucher)
                                                                         {
                                                                             return RequireAcceptedDocument(voucher);
                                                                         }

                                                                         private static AcceptedDocument RequireAcceptedDocument(Voucher voucher)
                                                                         {
                                                                             return voucher.AcceptedDocument;
                                                                         }
                                                                     }

                                                                     public sealed class Voucher
                                                                     {
                                                                         private readonly AcceptedDocument? _acceptedDocument;

                                                                         public Voucher(AcceptedDocument? acceptedDocument)
                                                                         {
                                                                             _acceptedDocument = acceptedDocument;
                                                                         }

                                                                         public AcceptedDocument AcceptedDocument
                                                                         {
                                                                             get
                                                                             {
                                                                                 if (_acceptedDocument is null)
                                                                                 {
                                                                                     throw new InvalidOperationException();
                                                                                 }

                                                                                 return _acceptedDocument;
                                                                             }
                                                                         }
                                                                     }

                                                                     public sealed class AcceptedDocument
                                                                     {
                                                                     }
                                                                     """,
            CheckedExceptionsOptions());

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);

        var siteDiagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId)
            .Single(d => d.GetMessage().Contains("LoadAcceptedDocument(voucher)", StringComparison.Ordinal));

        var siteSources = siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty];
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("source_callee"));
        Assert.That(siteSources, Does.Contain("VoucherService.LoadAcceptedDocument(Voucher)"));
        Assert.That(siteSources, Does.Contain("VoucherService.RequireAcceptedDocument(Voucher)"));
        Assert.That(siteSources, Does.Contain("Voucher.AcceptedDocument.get"));
        Assert.That(siteSources, Does.Contain("direct_throw:throw"));
    }

    [Test]
    public async Task Sp0010AndSp0011_MutualRecursionCycle_CompletesAndPreservesOuterEvidence()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
                                                                     using System;

                                                                     public sealed class CycleService
                                                                     {
                                                                         public int Render(int depth)
                                                                         {
                                                                             return StepA(depth);
                                                                         }

                                                                         private static int StepA(int depth)
                                                                         {
                                                                             if (depth <= 0)
                                                                             {
                                                                                 throw new InvalidOperationException();
                                                                             }

                                                                             return StepB(depth - 1);
                                                                         }

                                                                         private static int StepB(int depth)
                                                                         {
                                                                             return StepA(depth);
                                                                         }
                                                                     }
                                                                     """,
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_report_exceptions", "true")
                .Add("sharpproof_checked_exceptions", "true"));

        var summaryDiagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId)
            .Single(d => d.GetMessage().Contains("'Render'", StringComparison.Ordinal));

        var summarySources = summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty];
        Assert.That(summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(summarySources, Does.Contain("CycleService.StepA(int)"));
        Assert.That(summarySources, Does.Contain("direct_throw:throw"));

        var siteDiagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId)
            .First(d => d.GetMessage().Contains("StepA(depth)", StringComparison.Ordinal));

        var siteSources = siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty];
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("source_callee"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSymbolProperty], Does.Contain("StepA"));
        Assert.That(siteSources, Does.Contain("CycleService.StepA(int)"));
        Assert.That(siteSources, Does.Contain("direct_throw:throw"));
    }

    [Test]
    public async Task Sp0011_MutualRecursionCycle_CheckedOnly_CompletesAndPreservesOuterEvidence()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
                                                                     using System;

                                                                     public sealed class CycleService
                                                                     {
                                                                         public int Render(int depth)
                                                                         {
                                                                             return StepA(depth);
                                                                         }

                                                                         private static int StepA(int depth)
                                                                         {
                                                                             if (depth <= 0)
                                                                             {
                                                                                 throw new InvalidOperationException();
                                                                             }

                                                                             return StepB(depth - 1);
                                                                         }

                                                                         private static int StepB(int depth)
                                                                         {
                                                                             return StepA(depth);
                                                                         }
                                                                     }
                                                                     """,
            CheckedExceptionsOptions());

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);

        var siteDiagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId)
            .First(d => d.GetMessage().Contains("StepA(depth)", StringComparison.Ordinal));

        var siteSources = siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty];
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("source_callee"));
        Assert.That(siteSources, Does.Contain("CycleService.StepA(int)"));
        Assert.That(siteSources, Does.Contain("direct_throw:throw"));
    }

    [Test]
    public async Task Sp0010AndSp0011_FiveHopSourceChain_PreservesEveryIntermediateSource()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
                                                                     using System;

                                                                     public sealed class WorkflowService
                                                                     {
                                                                         public AcceptedDocument Render(Voucher voucher)
                                                                         {
                                                                             return Stage1(voucher);
                                                                         }

                                                                         private static AcceptedDocument Stage1(Voucher voucher) => Stage2(voucher);

                                                                         private static AcceptedDocument Stage2(Voucher voucher) => Stage3(voucher);

                                                                         private static AcceptedDocument Stage3(Voucher voucher) => Stage4(voucher);

                                                                         private static AcceptedDocument Stage4(Voucher voucher) => Stage5(voucher);

                                                                         private static AcceptedDocument Stage5(Voucher voucher) => voucher.AcceptedDocument;
                                                                     }

                                                                     public sealed class Voucher
                                                                     {
                                                                         private readonly AcceptedDocument? _acceptedDocument;

                                                                         public Voucher(AcceptedDocument? acceptedDocument)
                                                                         {
                                                                             _acceptedDocument = acceptedDocument;
                                                                         }

                                                                         public AcceptedDocument AcceptedDocument
                                                                         {
                                                                             get
                                                                             {
                                                                                 if (_acceptedDocument is null)
                                                                                 {
                                                                                     throw new InvalidOperationException();
                                                                                 }

                                                                                 return _acceptedDocument;
                                                                             }
                                                                         }
                                                                     }

                                                                     public sealed class AcceptedDocument
                                                                     {
                                                                     }
                                                                     """,
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_report_exceptions", "true")
                .Add("sharpproof_checked_exceptions", "true"));

        var summaryDiagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId)
            .Single(d => d.GetMessage().Contains("'Render'", StringComparison.Ordinal));

        var summarySources = summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty];
        Assert.That(summaryDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(summarySources, Does.Contain("WorkflowService.Stage1(Voucher)"));
        Assert.That(summarySources, Does.Contain("WorkflowService.Stage2(Voucher)"));
        Assert.That(summarySources, Does.Contain("WorkflowService.Stage3(Voucher)"));
        Assert.That(summarySources, Does.Contain("WorkflowService.Stage4(Voucher)"));
        Assert.That(summarySources, Does.Contain("WorkflowService.Stage5(Voucher)"));
        Assert.That(summarySources, Does.Contain("Voucher.AcceptedDocument.get"));
        Assert.That(summarySources, Does.Contain("direct_throw:throw"));

        var siteDiagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId)
            .Single(d => d.GetMessage().Contains("Stage1(voucher)", StringComparison.Ordinal));

        var siteSources = siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty];
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("source_callee"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSymbolProperty], Does.Contain("Stage1"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage1(Voucher)"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage2(Voucher)"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage3(Voucher)"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage4(Voucher)"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage5(Voucher)"));
        Assert.That(siteSources, Does.Contain("Voucher.AcceptedDocument.get"));
        Assert.That(siteSources, Does.Contain("direct_throw:throw"));
    }

    [Test]
    public async Task Sp0011_FiveHopSourceChain_CheckedOnly_PreservesEveryIntermediateSource()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync("""
                                                                     using System;

                                                                     public sealed class WorkflowService
                                                                     {
                                                                         public AcceptedDocument Render(Voucher voucher)
                                                                         {
                                                                             return Stage1(voucher);
                                                                         }

                                                                         private static AcceptedDocument Stage1(Voucher voucher) => Stage2(voucher);

                                                                         private static AcceptedDocument Stage2(Voucher voucher) => Stage3(voucher);

                                                                         private static AcceptedDocument Stage3(Voucher voucher) => Stage4(voucher);

                                                                         private static AcceptedDocument Stage4(Voucher voucher) => Stage5(voucher);

                                                                         private static AcceptedDocument Stage5(Voucher voucher) => voucher.AcceptedDocument;
                                                                     }

                                                                     public sealed class Voucher
                                                                     {
                                                                         private readonly AcceptedDocument? _acceptedDocument;

                                                                         public Voucher(AcceptedDocument? acceptedDocument)
                                                                         {
                                                                             _acceptedDocument = acceptedDocument;
                                                                         }

                                                                         public AcceptedDocument AcceptedDocument
                                                                         {
                                                                             get
                                                                             {
                                                                                 if (_acceptedDocument is null)
                                                                                 {
                                                                                     throw new InvalidOperationException();
                                                                                 }

                                                                                 return _acceptedDocument;
                                                                             }
                                                                         }
                                                                     }

                                                                     public sealed class AcceptedDocument
                                                                     {
                                                                     }
                                                                     """,
            CheckedExceptionsOptions());

        Assert.That(diagnostics.Any(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId), Is.False);

        var siteDiagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.UncaughtExceptionSiteId)
            .Single(d => d.GetMessage().Contains("Stage1(voucher)", StringComparison.Ordinal));

        var siteSources = siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty];
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(siteDiagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("source_callee"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage1(Voucher)"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage2(Voucher)"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage3(Voucher)"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage4(Voucher)"));
        Assert.That(siteSources, Does.Contain("WorkflowService.Stage5(Voucher)"));
        Assert.That(siteSources, Does.Contain("Voucher.AcceptedDocument.get"));
        Assert.That(siteSources, Does.Contain("direct_throw:throw"));
    }

    private static ImmutableDictionary<string, string> CheckedExceptionsOptions()
    {
        return ImmutableDictionary<string, string>.Empty.Add("sharpproof_checked_exceptions", "true");
    }
}