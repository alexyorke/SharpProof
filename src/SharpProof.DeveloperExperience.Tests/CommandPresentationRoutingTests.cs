using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
internal sealed class CommandPresentationRoutingTests
{
    [TestCase("text")]
    [TestCase("json")]
    [TestCase("markdown")]
    public async Task NamedExplainFormatsShareTheSameValidatedOutcome(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        using var fixture = new StoredEvidenceFixture();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(["explain", "--format", format, "--result", fixture.ResultPath],
            output, error, UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
        Assert.That(error.ToString(), Is.Empty);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output.ToString());
            var result = document.RootElement;
            Assert.That(result.GetProperty("schema").GetString(), Is.EqualTo("SharpProof.CommandPresentation"));
            Assert.That(result.GetProperty("command").GetString(), Is.EqualTo("explain"));
            Assert.That(result.GetProperty("exitCode").GetInt32(), Is.Zero);
            Assert.That(result.GetProperty("outcomes").EnumerateArray().Any(outcome =>
                outcome.GetProperty("id").GetString() == fixture.Response.Manifest.Claims[0].ClaimId &&
                outcome.GetProperty("outcome").GetString() == "Unknown" &&
                outcome.GetProperty("reason").GetString() == "UnsupportedCallable"), Is.True);
            Assert.That(result.TryGetProperty("protocolVersion", out _), Is.False);
        }
        else
        {
            Assert.That(output.ToString(), Does.Contain("Unknown").And.Contain("UnsupportedCallable"));
            if (format == "markdown")
            { Assert.That(output.ToString(), Does.Contain("#")); }
        }
    }

    [TestCase("json", false, 0)]
    [TestCase("json", true, 1)]
    [TestCase("markdown", false, 0)]
    [TestCase("markdown", true, 1)]
    public async Task FormattedComparisonKeepsRegressionReportingSeparateFromEnforcement(string format, bool enforce, int exitCode)
    {
        ArgumentNullException.ThrowIfNull(format);
        using var baseline = new StoredEvidenceFixture();
        using var current = new StoredEvidenceFixture();
        var assumption = new WorkerAssumptionEvidence { Id = "new-user-assumption", Kind = WorkerAssumptionKind.UserAssume };
        current.Artifact.Manifest.Callables[0].Assumptions = [assumption];
        current.Response.CallableResults[0].Assumptions = [assumption];
        current.Response.ClaimResults[0].Assumptions = [assumption];
        current.Save();
        _ = current.Load();
        var arguments = new List<string>
        {
            "compare", "--format", format, "--baseline", baseline.ResultPath, "--current", current.ResultPath
        };
        if (enforce)
        { arguments.Add("--fail-on-regression"); }
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync([.. arguments], output, error, UnexpectedDiscovery,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(exitCode), error.ToString());
        Assert.That(error.ToString(), Is.Empty);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output.ToString());
            var result = document.RootElement;
            Assert.That(result.GetProperty("command").GetString(), Is.EqualTo("compare"));
            Assert.That(result.GetProperty("exitCode").GetInt32(), Is.EqualTo(exitCode));
            Assert.That(result.GetProperty("outcomes").EnumerateArray().Any(outcome => outcome.GetProperty("isRegression").GetBoolean()), Is.True);
        }
        else
        {
            Assert.That(output.ToString(), Does.Contain(@"new\-user\-assumption").And.Contain("UserAssume"));
        }
    }

    [TestCase("text")]
    [TestCase("json")]
    [TestCase("markdown")]
    public async Task IncompatibleEvidenceDoesNotPublishPartialComparisonNotes(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        using var baseline = new StoredEvidenceFixture();
        using var current = new StoredEvidenceFixture();
        current.Request.Budgets.QueryRlimit++;
        current.Response = current.Response with
        {
            Summary = current.Response.Summary with
            {
                Versions = current.Response.Summary.Versions with { ApiSpecContentSha256 = new string('a', 64), WorkerVersion = "other-worker" }
            }
        };
        current.Save();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(["compare", "--baseline", baseline.ResultPath, "--current", current.ResultPath,
            "--format", format], output, error, UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.StartWith("SharpProof: ").And.Contain("incompatible"));
    }

    [Test]
    public async Task ExplicitTextPreservesTheDefaultNamedExplainBytes()
    {
        using var fixture = new StoredEvidenceFixture();
        using var original = new StringWriter(CultureInfo.InvariantCulture);
        using var formatted = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(["explain", "--result", fixture.ResultPath], original, error,
            UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
        Assert.That(await ToolApplication.RunAsync(["explain", "--result", fixture.ResultPath, "--format", "text"], formatted, error,
            UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
        Assert.That(formatted.ToString(), Is.EqualTo(original.ToString()));
        Assert.That(error.ToString(), Is.Empty);
    }

    private static Task<string> UnexpectedDiscovery(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new AssertionException("Legacy SDK discovery must not run for evidence commands.");
    }
}
