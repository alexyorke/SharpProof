using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class EventAccessorRegressionTests
{
    [Test]
    public async Task Sp0002_CustomEventAddAccessor_ReportsImpurity()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

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
            diagnostics.Where(candidate => candidate.Id == "SP0002")
                .ToImmutableArray(),
            "SP0002");

        Assert.That(diagnostic.Properties["sharpproof.impurity.symbol"],
            Does.Contain("System.Console.WriteLine"));
    }

    [Test]
    public async Task Sp0002_CustomEventRemoveAccessor_ReportsImpurity()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

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
            diagnostics.Where(candidate => candidate.Id == "SP0002")
                .ToImmutableArray(),
            "SP0002");

        Assert.That(diagnostic.Properties["sharpproof.impurity.symbol"],
            Does.Contain("System.Console.WriteLine"));
    }

    [Test]
    public async Task Sp0010_CustomEventRemoveAccessor_ReportsSummary()
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
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true"));

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == "SP0010").ToImmutableArray(),
            "SP0010");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionCategoriesProperty],
            Is.EqualTo("direct_throw"));
    }

    [Test]
    public async Task Sp0011_CustomEventRemoveAccessor_ReportsThrowSite()
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
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_checked_exceptions", "true"));

        var diagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(candidate => candidate.Id == "SP0011")
                .ToImmutableArray(),
            "SP0011");

        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionTypesProperty],
            Is.EqualTo("System.InvalidOperationException"));
        Assert.That(diagnostic.Properties[DiagnosticPropertyNames.ExceptionCategoriesProperty],
            Is.EqualTo("direct_throw"));
    }
}