using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Test
{
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
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCapabilities-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
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
                Assert.That(root.GetProperty("CapabilityText").GetString(), Does.Contain("Console"));
                var site = root.GetProperty("Sites")[0];
                Assert.That(site.GetProperty("CapabilityText").GetString(), Does.Contain("Console"));
                Assert.That(site.GetProperty("SiteKind").GetString(), Is.EqualTo("invocation"));
                Assert.That(site.GetProperty("OperationKind").GetString(), Is.EqualTo("Invocation"));
            }
            finally
            {
                File.Delete(sourcePath);
            }
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
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCapabilitiesDynamic-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await SymbolicCliTestHost.RunAsync(
                    "--file",
                    sourcePath,
                    "--line",
                    FindLine(source, "value.ToString()").ToString(),
                    "--capabilities",
                    "--compact-json");

                Assert.That(result.ExitCode, Is.EqualTo(0), result.StandardError);
                using var document = JsonDocument.Parse(result.StandardOutput);
                var root = document.RootElement;
                Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("capabilities"));
                Assert.That(root.GetProperty("hasUnknowns").GetBoolean(), Is.True);
                Assert.That(root.GetProperty("unknownReasons")[0].GetString(), Is.EqualTo("DynamicDispatch"));
            }
            finally
            {
                File.Delete(sourcePath);
            }
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
            var sourcePath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "SymbolicCapabilitiesInvalid-" + Guid.NewGuid().ToString("N") + ".cs");
            File.WriteAllText(sourcePath, source);
            try
            {
                var result = await SymbolicCliTestHost.RunAsync(
                    "--file",
                    sourcePath,
                    "--capabilities",
                    "--all-lines");

                Assert.That(result.ExitCode, Is.EqualTo(64));
                Assert.That(result.StandardError, Does.Contain("--capabilities supports --line, --line with --column, or --position only."));
            }
            finally
            {
                File.Delete(sourcePath);
            }
        }

        private static int FindLine(string source, string marker)
        {
            var position = source.IndexOf(marker, StringComparison.Ordinal);
            if (position < 0)
            {
                throw new InvalidOperationException("Marker was not found in source.");
            }

            var line = 1;
            for (var index = 0; index < position; index++)
            {
                if (source[index] == '\n')
                {
                    line++;
                }
            }

            return line;
        }
    }
}
