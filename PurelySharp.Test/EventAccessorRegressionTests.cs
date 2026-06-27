using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public class EventAccessorRegressionTests
    {
        [Test]
        public async Task Ps0002_CustomEventAddAccessor_ReportsImpurity()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public sealed class Publisher
{
    public event Action Changed
    {
        [EnforcePure]
        add
        {
            Console.WriteLine(value);
        }
        remove
        {
        }
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToImmutableArray(),
                PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_CustomEventRemoveAccessor_ReportsImpurity()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public sealed class Publisher
{
    public event Action Changed
    {
        add
        {
        }
        [EnforcePure]
        remove
        {
            Console.WriteLine(value);
        }
    }
}");

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId).ToImmutableArray(),
                PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0010_CustomEventRemoveAccessor_ReportsSummary()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public sealed class Publisher
{
    public event Action Changed
    {
        add
        {
        }
        remove
        {
            throw new InvalidOperationException();
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(),
                PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }

        [Test]
        public async Task Ps0011_CustomEventRemoveAccessor_ReportsThrowSite()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;

public sealed class Publisher
{
    public event Action Changed
    {
        add
        {
        }
        remove
        {
            throw new InvalidOperationException();
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_checked_exceptions", "true"));

            var diagnostic = AnalyzerTestHost.SingleDiagnostic(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.UncaughtExceptionSiteId).ToImmutableArray(),
                PurelySharpDiagnostics.UncaughtExceptionSiteId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
        }
    }
}
