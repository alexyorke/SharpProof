using System.Text.Json;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

public sealed class SymbolicExplainCliTests
{
    [Test]
    public async Task SymbolicCli_Explain_ComposesProofSurfacesForLine()
    {
        var source = """
                     using System;

                     public static class Example
                     {
                         public static int Work(int divisor, int n)
                         {
                             Console.WriteLine(n);
                             var sum = 0;
                             for (var i = 0; i < n; i++)
                             {
                                 sum += i;
                             }

                             if (divisor == 0)
                             {
                                 return 10 / divisor;
                             }

                             return sum;
                         }
                     }
                     """;
        var filePath = Path.Combine(
            Path.GetTempPath(),
            "SharpProofExplainCli-" + Guid.NewGuid().ToString("N") + ".cs");
        await File.WriteAllTextAsync(filePath, source).ConfigureAwait(false);
        try
        {
            var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
                "explain",
                "--file",
                filePath,
                "--line",
                "16",
                "--column",
                "20",
                "--implies",
                "divisor == 0").ConfigureAwait(false);

            Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
            Assert.That(result.StandardOutput, Does.Contain("SharpProof explanation"));
            Assert.That(result.StandardOutput, Does.Contain("Invariant proof"));
            Assert.That(result.StandardOutput, Does.Contain("Reachability:"));
            Assert.That(result.StandardOutput, Does.Contain("Proof outcomes:"));
            Assert.That(result.StandardOutput, Does.Contain("Runtime hazards"));
            Assert.That(result.StandardOutput, Does.Contain("DivideByZero"));
            Assert.That(result.StandardOutput, Does.Contain("Capabilities"));
            Assert.That(result.StandardOutput, Does.Contain("Console"));
            Assert.That(result.StandardOutput, Does.Contain("Complexity"));
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Test]
    public async Task SymbolicCli_ExplainPosition_IncludesRuntimeHazardsFromResolvedLine()
    {
        var source = CreateMachineReportSource();
        var position = source.IndexOf("throw new InvalidOperationException", StringComparison.Ordinal);

        var result = await SymbolicCliTestHost.RunAsync(
            "explain",
            "--source-text",
            source,
            "--position",
            position.ToString()).ConfigureAwait(false);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(result.StandardOutput, Does.Contain("Runtime hazards"));
        Assert.That(result.StandardOutput, Does.Contain("DirectThrow"));
    }

    [Test]
    public async Task SymbolicCli_ExplainJson_ComposesBoundedEvidenceReport()
    {
        var source = CreateMachineReportSource();
        var result = await SymbolicCliTestHost.RunAsync(
            "explain",
            "--source-text",
            source,
            "--source-file-name",
            "virtual/ExplainReport.cs",
            "--source-map-uri",
            "editor://workspace/ExplainReport.cs",
            "--source-map-original-line",
            "21",
            "--source-map-original-column",
            "5",
            "--line",
            FindLine(source, "throw new InvalidOperationException").ToString(),
            "--column",
            "17",
            "--json",
            "--report-max-diagnostics",
            "0",
            "--report-max-hazards",
            "1",
            "--report-max-items",
            "1").ConfigureAwait(false);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(result.StandardError, Is.Empty);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("explain"));
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("evidenceSchemaVersion").GetInt32(), Is.GreaterThan(0));
        Assert.That(root.GetProperty("source").GetProperty("filePath").GetString(),
            Is.EqualTo("virtual/ExplainReport.cs"));
        Assert.That(root.GetProperty("source").GetProperty("sourceMap").GetProperty("sourceUri").GetString(),
            Is.EqualTo("editor://workspace/ExplainReport.cs"));
        Assert.That(root.GetProperty("source").GetProperty("sourceMap").GetProperty("originalStartLine").GetInt32(),
            Is.EqualTo(21));
        Assert.That(root.GetProperty("target").GetProperty("nodeKind").GetString(),
            Is.EqualTo("ThrowStatement"));

        var invariant = root.GetProperty("invariant");
        Assert.That(invariant.GetProperty("kind").GetString(), Is.EqualTo("point"));
        Assert.That(invariant.GetProperty("pointReachability").GetString(), Is.EqualTo("Reachable"));
        Assert.That(invariant.GetProperty("invariantQuery").GetProperty("status").GetString(), Is.Not.Empty);

        var hazards = root.GetProperty("runtimeHazards");
        Assert.That(hazards.GetProperty("hazardCount").GetInt32(), Is.EqualTo(1));
        Assert.That(hazards.GetProperty("hazards").GetArrayLength(), Is.EqualTo(1));
        Assert.That(hazards.GetProperty("hazards")[0].GetProperty("kind").GetString(),
            Is.EqualTo("DirectThrow"));

        var capabilities = root.GetProperty("capabilities");
        Assert.That(capabilities.GetProperty("kind").GetString(), Is.EqualTo("capabilities"));
        Assert.That(capabilities.GetProperty("sites").GetArrayLength(), Is.LessThanOrEqualTo(1));
        Assert.That(capabilities.GetProperty("siteCount").GetInt32(),
            Is.GreaterThanOrEqualTo(capabilities.GetProperty("sites").GetArrayLength()));

        var complexity = root.GetProperty("complexity");
        Assert.That(complexity.GetProperty("kind").GetString(), Is.EqualTo("complexity"));
        Assert.That(complexity.GetProperty("drivers").GetArrayLength(), Is.LessThanOrEqualTo(1));
        Assert.That(complexity.GetProperty("calleeSummaries").GetArrayLength(), Is.LessThanOrEqualTo(1));

        Assert.That(root.GetProperty("diagnostics").GetProperty("items").GetArrayLength(), Is.Zero);
        Assert.That(root.GetProperty("crossLinks").GetArrayLength(), Is.GreaterThanOrEqualTo(4));
        Assert.That(root.GetProperty("truncation").GetProperty("isTruncated").GetBoolean(), Is.True);
    }

    [Test]
    public async Task SymbolicCli_ExplainSarif_FromJsonRequest_EmitsSarif21Results()
    {
        var source = CreateMachineReportSource();
        var request = JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            mode = "explain",
            source = new { text = source, filePath = "virtual/ExplainSarif.cs" },
            target = new
            {
                kind = "point",
                line = FindLine(source, "throw new InvalidOperationException"),
                column = 17
            },
            output = new
            {
                format = "sarif",
                maxDiagnostics = 1,
                maxHazards = 1,
                maxItems = 1
            }
        });

        var result = await SymbolicCliTestHost.RunAsync("--request-json", request).ConfigureAwait(false);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(result.StandardError, Is.Empty);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.That(root.GetProperty("$schema").GetString(),
            Is.EqualTo("https://json.schemastore.org/sarif-2.1.0.json"));
        Assert.That(root.GetProperty("version").GetString(), Is.EqualTo("2.1.0"));
        var run = root.GetProperty("runs")[0];
        Assert.That(run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString(),
            Is.EqualTo("SharpProof"));
        Assert.That(
            run.GetProperty("results").EnumerateArray().Any(item =>
                item.GetProperty("ruleId").GetString() == "SPQ-HZ-DIRECT-THROW"),
            Is.True);
        Assert.That(run.GetProperty("properties").GetProperty("crossLinks").GetArrayLength(),
            Is.GreaterThanOrEqualTo(4));
    }

    [Test]
    public async Task SymbolicCli_ExplainMarkdown_EmitsIssueReadySections()
    {
        var source = CreateMachineReportSource();
        var result = await SymbolicCliTestHost.RunAsync(
            "explain",
            "--source-text",
            source,
            "--line",
            FindLine(source, "throw new InvalidOperationException").ToString(),
            "--column",
            "17",
            "--markdown",
            "--report-max-hazards",
            "1",
            "--report-max-items",
            "1").ConfigureAwait(false);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        Assert.That(result.StandardOutput, Does.StartWith("# SharpProof explanation"));
        Assert.That(result.StandardOutput, Does.Contain("## Invariant and reachability"));
        Assert.That(result.StandardOutput, Does.Contain("## Runtime hazards"));
        Assert.That(result.StandardOutput, Does.Contain("DirectThrow"));
        Assert.That(result.StandardOutput, Does.Contain("## Capabilities"));
        Assert.That(result.StandardOutput, Does.Contain("## Complexity"));
        Assert.That(result.StandardOutput, Does.Contain("## Analyzer diagnostics"));
        Assert.That(result.StandardOutput, Does.Contain("## Cross-links"));
    }

    [Test]
    public async Task SymbolicCli_ProjectExplainJson_IncludesBuildDiagnosticsAndInputs()
    {
        var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "explain",
            "--file",
            Path.Combine("SharpProof.Demo", "Program.cs"),
            "--project",
            Path.Combine("SharpProof.Demo", "SharpProof.Demo.csproj"),
            "--configuration",
            "Debug",
            "--framework",
            "net8.0",
            "--line",
            "39",
            "--json",
            "--report-max-diagnostics",
            "50",
            "--report-max-hazards",
            "5",
            "--report-max-items",
            "5").ConfigureAwait(false);

        Assert.That(result.ExitCode, Is.Zero, result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        var project = root.GetProperty("project");
        Assert.That(project.GetProperty("name").GetString(), Is.EqualTo("SharpProof.Demo"));
        Assert.That(project.GetProperty("hasBaseline").GetBoolean(), Is.True);
        Assert.That(project.GetProperty("effectSummaryFileCount").GetInt32(), Is.EqualTo(1));
        Assert.That(project.GetProperty("additionalFileCount").GetInt32(), Is.EqualTo(2));
        var diagnostics = root.GetProperty("diagnostics").GetProperty("items");
        Assert.That(
            diagnostics.EnumerateArray().Any(item => item.GetProperty("id").GetString() == "SP0004"),
            Is.True);
        Assert.That(
            root.GetProperty("crossLinks").EnumerateArray().Any(link =>
                link.GetProperty("from").GetString()!.StartsWith("#/diagnostics/items/", StringComparison.Ordinal)),
            Is.True);
    }

    [Test]
    public async Task SymbolicCli_ExplainReportOptions_RejectInvalidCombinations()
    {
        var sarifWithoutExplain = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            "class C { }",
            "--line",
            "1",
            "--sarif").ConfigureAwait(false);

        Assert.That(sarifWithoutExplain.ExitCode, Is.EqualTo(SymbolicErrorExitCodes.Usage));
        Assert.That(sarifWithoutExplain.StandardError, Is.Empty);
        using (var document = JsonDocument.Parse(sarifWithoutExplain.StandardOutput))
        {
            Assert.That(document.RootElement.GetProperty("error").GetProperty("code").GetString(),
                Is.EqualTo(SymbolicErrorCodes.InvalidRequest));
            Assert.That(document.RootElement.GetProperty("error").GetProperty("message").GetString(),
                Does.Contain("require explain"));
        }

        var reportLimitWithoutExplain = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            "class C { }",
            "--line",
            "1",
            "--report-max-items",
            "1").ConfigureAwait(false);
        Assert.That(reportLimitWithoutExplain.ExitCode, Is.EqualTo(SymbolicErrorExitCodes.Usage));
        Assert.That(reportLimitWithoutExplain.StandardError, Does.Contain("require explain"));

        var reportLimitWithTextExplain = await SymbolicCliTestHost.RunAsync(
            "explain",
            "--source-text",
            "class C { static void M() { int value = 0; } }",
            "--position",
            "29",
            "--report-max-items",
            "1").ConfigureAwait(false);
        Assert.That(reportLimitWithTextExplain.ExitCode, Is.Zero, reportLimitWithTextExplain.StandardError);
        Assert.That(reportLimitWithTextExplain.StandardOutput, Does.Contain("SharpProof explanation"));

        var mixedFormats = await SymbolicCliTestHost.RunAsync(
            "explain",
            "--source-text",
            "class C { static void M() { } }",
            "--line",
            "1",
            "--json",
            "--markdown").ConfigureAwait(false);
        Assert.That(mixedFormats.ExitCode, Is.EqualTo(SymbolicErrorExitCodes.Usage));
        Assert.That(mixedFormats.StandardError + mixedFormats.StandardOutput,
            Does.Contain("mutually exclusive"));
    }

    private static string CreateMachineReportSource()
    {
        return """
               using System;

               public static class ExplainReportSample
               {
                   public static int Work(int value)
                   {
                       Console.WriteLine(value);
                       if (value > 0)
                       {
                           throw new InvalidOperationException();
                       }

                       return value;
                   }
               }
               """;
    }

    private static int FindLine(string source, string marker)
    {
        var offset = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(offset, Is.GreaterThanOrEqualTo(0), marker);
        return source[..offset].Count(static character => character == '\n') + 1;
    }
}
