using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public class BaselineSuppressionTests
{
    [Test]
    public async Task Baseline_SuppressesExactSp0002Match()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Impure()
    {
        Console.WriteLine(""impure"");
    }
}", Baseline("SP0002", "M:TestClass.Impure", "src/ProductionCode.cs"));

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False);
    }

    [Test]
    public async Task Baseline_DoesNotSuppressWhenPathDiffers()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Impure()
    {
        Console.WriteLine(""impure"");
    }
}", Baseline("SP0002", "M:TestClass.Impure", "other/ProductionCode.cs"));

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
    }

    [Test]
    public async Task Baseline_DoesNotSuppressFileNameOnlyPath()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Impure()
    {
        Console.WriteLine(""impure"");
    }
}", Baseline("SP0002", "M:TestClass.Impure", "ProductionCode.cs"));

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
    }

    [Test]
    public async Task Baseline_SuppressesRelativePathAgainstAbsoluteSourcePath()
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "SharpProofBaselineTests", "Project");
        var sourcePath = Path.Combine(projectRoot, "src", "ProductionCode.cs");
        var baselinePath = Path.Combine(projectRoot, "SharpProof.Baseline.json");

        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Impure()
    {
        Console.WriteLine(""impure"");
    }
}", Baseline("SP0002", "M:TestClass.Impure", "src/ProductionCode.cs"), sourcePath, baselinePath);

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False);
    }

    [Test]
    public async Task Baseline_ParsesJsonEscapedValues()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Impure()
    {
        Console.WriteLine(""impure"");
    }
}", @"{
  ""diagnostics"": [
    {
      ""diagnosticId"": ""SP0002"",
      ""symbol"": ""M:TestClass.\u0049mpure"",
      ""path"": ""src/ProductionCode.cs""
    }
  ]
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False);
    }

    [Test]
    public async Task Baseline_TrimsStringValues()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Impure()
    {
        Console.WriteLine(""impure"");
    }
}", @"{
  ""diagnostics"": [
    {
      ""id"": "" SP0002 "",
      ""symbol"": ""\n M:TestClass.Impure \t"",
      ""path"": "" src/ProductionCode.cs ""
    }
  ]
}");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False);
    }

    [Test]
    public async Task Baseline_SuppressesExactSp0004Match()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int Pure() => 1;
}", Baseline("SP0004", "M:TestClass.Pure", "src/ProductionCode.cs"));

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.MissingEnforcePureAttributeId),
            Is.False);
    }

    [Test]
    public async Task Baseline_IgnoresInvalidJson()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Impure()
    {
        Console.WriteLine(""impure"");
    }
}", "{ invalid json");

        Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.True);
    }

    [Test]
    public async Task BaselineMetadata_IsEmittedForGeneratedBaselineEntries()
    {
        var impureDiagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Impure()
    {
        Console.WriteLine(""impure"");
    }
}", "{}");
        var impure = AnalyzerTestHost.SingleDiagnostic(
            impureDiagnostics,
            SharpProofDiagnostics.PurityNotVerifiedId);

        Assert.That(impure.Properties[SharpProofDiagnostics.BaselineSymbolProperty], Is.EqualTo("M:TestClass.Impure"));
        Assert.That(impure.Properties[SharpProofDiagnostics.BaselinePathProperty], Is.EqualTo("src/ProductionCode.cs"));

        var missingAttributeDiagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int Pure() => 1;
}", "{}");
        var missingAttribute = AnalyzerTestHost.SingleDiagnostic(
            missingAttributeDiagnostics,
            SharpProofDiagnostics.MissingEnforcePureAttributeId);

        Assert.That(missingAttribute.Properties[SharpProofDiagnostics.BaselineSymbolProperty],
            Is.EqualTo("M:TestClass.Pure"));
        Assert.That(missingAttribute.Properties[SharpProofDiagnostics.BaselinePathProperty],
            Is.EqualTo("src/ProductionCode.cs"));
    }

    [Test]
    public async Task Baseline_SuppressesOnlyMatchingAllocationInstance()
    {
        var source = @"
using SharpProof.Attributes;

public class TestClass
{
    [ZeroAllocations]
    public object[] Allocate()
    {
        var first = new object();
        var second = new object();
        return new[] { first, second };
    }
}";
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source, "{}");
        var allocationDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.AllocationInZeroAllocationMethodId)
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();

        Assert.That(allocationDiagnostics, Has.Length.EqualTo(3));

        var filteredDiagnostics = await GetAnalyzerDiagnosticsAsync(source, Baseline(allocationDiagnostics[0]));
        var remainingAllocations = filteredDiagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.AllocationInZeroAllocationMethodId)
            .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();

        Assert.That(remainingAllocations, Has.Length.EqualTo(2));
        Assert.That(remainingAllocations[0].Location.SourceSpan.Start,
            Is.EqualTo(allocationDiagnostics[1].Location.SourceSpan.Start));
        Assert.That(remainingAllocations[1].Location.SourceSpan.Start,
            Is.EqualTo(allocationDiagnostics[2].Location.SourceSpan.Start));
    }

    [Test]
    public async Task Baseline_SuppressesUsageDiagnostic()
    {
        var source = @"
using SharpProof.Attributes;

[EnforcePure]
public class TestClass
{
}";
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source, "{}");
        var misplaced = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.MisplacedAttributeId);

        var filteredDiagnostics = await GetAnalyzerDiagnosticsAsync(source, Baseline(misplaced));

        Assert.That(filteredDiagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.MisplacedAttributeId),
            Is.False);
    }

    [Test]
    public async Task Baseline_SuppressesExceptionSiteDiagnostic()
    {
        var source = @"
using System;

public class TestClass
{
    public void Thrower()
    {
        throw new InvalidOperationException();
    }
}";
        var options = ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", "sites");
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source, "{}", globalOptions: options);
        var exceptionSite =
            AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.UncaughtExceptionSiteId);

        var filteredDiagnostics =
            await GetAnalyzerDiagnosticsAsync(source, Baseline(exceptionSite), globalOptions: options);

        Assert.That(
            filteredDiagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.UncaughtExceptionSiteId),
            Is.False);
    }

    [Test]
    public async Task Baseline_SuppressesUnknownRuntimeHazardDiagnostic()
    {
        var source = @"
public class TestClass
{
    public int Divide(int divisor)
    {
        return 10 / divisor;
    }
}";
        var options = ImmutableDictionary<string, string>.Empty.Add(
            "sharpproof_runtime_hazard_mode",
            "unknowns");
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source, "{}", globalOptions: options);
        var unknownHazard = AnalyzerTestHost.SingleDiagnostic(
            diagnostics,
            SharpProofDiagnostics.UnknownRuntimeHazardId);

        var filteredDiagnostics = await GetAnalyzerDiagnosticsAsync(
            source,
            Baseline(unknownHazard),
            globalOptions: options);

        Assert.That(
            filteredDiagnostics.Any(diagnostic =>
                diagnostic.Id == SharpProofDiagnostics.UnknownRuntimeHazardId),
            Is.False);
    }

    [Test]
    public async Task Baseline_SuppressesLegacySymbolAliasMatch()
    {
        var source = @"
using System;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Impure(int value)
    {
        Console.WriteLine(value);
    }
}";
        var diagnostics = await GetAnalyzerDiagnosticsAsync(source, "{}");
        var impure = AnalyzerTestHost.SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);
        var preferredSymbol = impure.Properties[SharpProofDiagnostics.BaselineSymbolProperty];
        var legacyAlias = impure.Properties[SharpProofDiagnostics.BaselineSymbolAliasesProperty]!
            .Split('\n')
            .First(alias => !string.Equals(alias, preferredSymbol, StringComparison.Ordinal));

        var filteredDiagnostics = await GetAnalyzerDiagnosticsAsync(
            source,
            Baseline(SharpProofDiagnostics.PurityNotVerifiedId, legacyAlias, "src/ProductionCode.cs"));

        Assert.That(filteredDiagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False);
    }

    private static string Baseline(string id, string symbol, string path)
    {
        return @"{
  ""diagnostics"": [
    {
      ""id"": """ + id + @""",
      ""symbol"": """ + symbol + @""",
      ""path"": """ + path + @"""
    }
  ]
}";
    }

    private static string Baseline(Diagnostic diagnostic)
    {
        var entry = new Dictionary<string, object?>
        {
            ["id"] = diagnostic.Id,
            ["symbol"] = diagnostic.Properties[SharpProofDiagnostics.BaselineSymbolProperty],
            ["path"] = diagnostic.Properties[SharpProofDiagnostics.BaselinePathProperty]
        };

        if (diagnostic.Location != Location.None && diagnostic.Location.IsInSource)
        {
            var lineSpan = diagnostic.Location.GetLineSpan();
            entry["line"] = lineSpan.StartLinePosition.Line + 1;
            entry["column"] = lineSpan.StartLinePosition.Character + 1;
        }

        AddOptionalEntry(entry, diagnostic, SharpProofDiagnostics.BaselineContractProperty, "contract");
        AddOptionalEntry(entry, diagnostic, SharpProofDiagnostics.BaselineOperationKindProperty, "operationKind");
        AddOptionalEntry(entry, diagnostic, SharpProofDiagnostics.BaselineEvidenceKeyProperty, "evidenceKey");

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["diagnostics"] = new[] { entry }
        });
    }

    private static void AddOptionalEntry(
        Dictionary<string, object?> entry,
        Diagnostic diagnostic,
        string diagnosticProperty,
        string baselineProperty)
    {
        if (diagnostic.Properties.TryGetValue(diagnosticProperty, out var value) &&
            !string.IsNullOrWhiteSpace(value))
            entry[baselineProperty] = value;
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        string baseline,
        string? sourcePath = null,
        string? baselinePath = null,
        ImmutableDictionary<string, string>? globalOptions = null)
    {
        return await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            globalOptions,
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    baselinePath ?? "SharpProof.Baseline.json",
                    baseline)),
            sourcePath: sourcePath ?? Path.Combine("src", "ProductionCode.cs"),
            autoEnableEffectSummaryJsonForAdditionalFiles: false);
    }
}
