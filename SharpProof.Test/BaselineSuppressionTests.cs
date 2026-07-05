using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
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

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId), Is.False);
        }

        [Test]
        public async Task Baseline_SuppressesExactSp0004Match()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int Pure() => 1;
}", Baseline("SP0004", "M:TestClass.Pure", "src/ProductionCode.cs"));

            Assert.That(diagnostics.Any(diagnostic => diagnostic.Id == SharpProofDiagnostics.MissingEnforcePureAttributeId), Is.False);
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

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            string baseline,
            string? sourcePath = null,
            string? baselinePath = null)
        {
            return await AnalyzerTestHost.GetDiagnosticsAsync(
                source,
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new AnalyzerTestHost.InMemoryAdditionalText(
                        baselinePath ?? "SharpProof.Baseline.json",
                        baseline)),
                sourcePath: sourcePath ?? Path.Combine("src", "ProductionCode.cs"),
                autoEnableEffectSummaryJsonForAdditionalFiles: false);
        }
    }
}
