using System.Text.Json;
using NUnit.Framework;
using static SharpProof.Test.SourceMarker;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicCapabilityQueryTests
{
    [Test]
    public async Task SymbolicCli_CapabilitiesJson_EmitsCapabilitySummaryAndSites()
    {
        const string source = """
                              using System;

                              public sealed class TestClass
                              {
                                  public void TestMethod()
                                  {
                                      Console.WriteLine("hello");
                                  }
                              }
                              """;
        using var sourceFile = TemporarySourceFile.Create("SymbolicCapabilities-", source);
        var sourcePath = sourceFile.Path;

        var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "--file",
            sourcePath,
            "--line",
            FindLine(source, "Console.WriteLine").ToString(),
            "--capabilities",
            "--json");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.That(root.GetProperty("capabilityText").GetString(), Does.Contain("Console"));
        var site = root.GetProperty("sites")[0];
        Assert.That(site.GetProperty("capabilityText").GetString(), Does.Contain("Console"));
        Assert.That(site.GetProperty("siteKind").GetString(), Is.EqualTo("invocation"));
        Assert.That(site.GetProperty("operationKind").GetString(), Is.EqualTo("Invocation"));
    }

    [Test]
    public async Task SymbolicCli_CapabilitiesCompactJson_EmitsKindAndUnknownReason()
    {
        const string source = """
                              public sealed class TestClass
                              {
                                  public void TestMethod(dynamic value)
                                  {
                                      _ = value.ToString();
                                  }
                              }
                              """;
        using var sourceFile = TemporarySourceFile.Create("SymbolicCapabilitiesDynamic-", source);
        var sourcePath = sourceFile.Path;

        var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "--file",
            sourcePath,
            "--line",
            FindLine(source, "value.ToString()").ToString(),
            "--capabilities",
            "--json");

        Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        SymbolicCliTestAssertions.AssertCompactEnvelope(root, "capabilities");
        Assert.That(root.GetProperty("hasUnknowns").GetBoolean(), Is.True);
        Assert.That(root.GetProperty("unknownReasons")[0].GetString(), Is.EqualTo("DynamicDispatch"));
    }

    [Test]
    public async Task SymbolicCli_Capabilities_RejectsInvalidCombinations()
    {
        const string source = """
                              public sealed class TestClass
                              {
                                  public int TestMethod()
                                  {
                                      return 42;
                                  }
                              }
                              """;
        using var sourceFile = TemporarySourceFile.Create("SymbolicCapabilitiesInvalid-", source);
        var sourcePath = sourceFile.Path;

        await SymbolicCliTestAssertions.AssertRejectsAllLinesAsync(sourcePath, "capabilities");
    }

    [Test]
    public async Task SymbolicCli_CapabilityExitGates_EnforceAllowlistUnknownsAndThresholds()
    {
        const string source = """
                              using System;

                              public sealed class TestClass
                              {
                                  public void Write() => Console.WriteLine("hello");

                                  public void Dynamic(dynamic value) => value.ToString();
                              }
                              """;
        using var sourceFile = TemporarySourceFile.Create("SymbolicCapabilityGates-", source);
        var sourcePath = sourceFile.Path;

        var violation = await SymbolicCliTestHost.RunAsync(
            "--file",
            sourcePath,
            "--line",
            FindLine(source, "Console.WriteLine").ToString(),
            "--capabilities",
            "--json",
            "--fail-on-capability-violation");
        Assert.That(violation.ExitCode, Is.EqualTo(1));
        Assert.That(violation.StandardError, Does.Contain("CI gate failed [capability-violation]"));
        Assert.That(violation.StandardError, Does.Contain("disallowed=IO, Console"));
        using (JsonDocument.Parse(violation.StandardOutput))
        {
        }

        var allowed = await SymbolicCliTestHost.RunAsync(
            "--file",
            sourcePath,
            "--line",
            FindLine(source, "Console.WriteLine").ToString(),
            "--capabilities",
            "--allowed-capability",
            "Console",
            "--fail-on-capability-violation");
        Assert.That(allowed.ExitCode, Is.Zero, allowed.StandardError);

        var unknown = await SymbolicCliTestHost.RunAsync(
            "--file",
            sourcePath,
            "--line",
            FindLine(source, "value.ToString").ToString(),
            "--capabilities",
            "--fail-on-capability-unknown");
        Assert.That(unknown.ExitCode, Is.EqualTo(1));
        Assert.That(unknown.StandardError, Does.Contain("CI gate failed [capability-unknown]"));

        var threshold = await SymbolicCliTestHost.RunAsync(
            "--file",
            sourcePath,
            "--line",
            FindLine(source, "Console.WriteLine").ToString(),
            "--capabilities",
            "--json",
            "--fail-on-threshold",
            "capability-sites=0");
        Assert.That(threshold.ExitCode, Is.EqualTo(1));
        Assert.That(threshold.StandardError,
            Does.Contain("CI gate failed [threshold.capability-sites]"));
    }

}
