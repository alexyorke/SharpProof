using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    public class InitAccessorRegressionTests
    {
        [Test]
        public async Task Sp0002_InitAccessor_ReportsImpurity()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class ConfigData
{
    private int _value;

    public int Value
    {
        get => _value;
        [EnforcePure]
        init
        {
            Console.WriteLine(value);
            _value = value;
        }
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.PurityNotVerifiedId).ToImmutableArray(),
                SharpProofDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Sp0010_InitAccessor_ReportsSummary()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class ConfigData
{
    private int _value;

    public int Value
    {
        get => _value;
        init
        {
            throw new InvalidOperationException();
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                SharpProofDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Sp0011_InitAccessor_ReportsThrowSite()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public class ConfigData
{
    private int _value;

    public int Value
    {
        get => _value;
        init
        {
            throw new InvalidOperationException();
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("sharpproof_checked_exceptions", "true"));

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == SharpProofDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                SharpProofDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }
    }
}
