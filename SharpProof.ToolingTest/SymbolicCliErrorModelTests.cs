using System.Text.Json;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicCliErrorModelTests
{
    [Test]
    public async Task SymbolicCli_JsonOutputConflict_EmitsUsageErrorEnvelope()
    {
        var result = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            "class C { }",
            "--line",
            "1",
            "--json",
            "--compact-json");

        AssertErrorEnvelope(
            result,
            SymbolicErrorCodes.InvalidRequest,
            SymbolicErrorCategory.Usage,
            SymbolicErrorExitCodes.Usage);
    }

    [Test]
    public async Task SymbolicCli_InvalidJsonRequest_EmitsParseErrorEnvelope()
    {
        var result = await SymbolicCliTestHost.RunAsync(
            "--request-json",
            "{ not-valid-json");

        AssertErrorEnvelope(
            result,
            SymbolicErrorCodes.ParseFailed,
            SymbolicErrorCategory.Parse,
            SymbolicErrorExitCodes.InvalidData);
    }

    [Test]
    public async Task SymbolicCli_MissingSource_EmitsMissingInputEnvelope()
    {
        var missingPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "MissingSource-" + Guid.NewGuid().ToString("N") + ".cs");
        var result = await SymbolicCliTestHost.RunAsync(
            "--file",
            missingPath,
            "--line",
            "1",
            "--error-json");

        var error = AssertErrorEnvelope(
            result,
            SymbolicErrorCodes.SourceNotFound,
            SymbolicErrorCategory.Input,
            SymbolicErrorExitCodes.MissingInput);
        Assert.That(error.GetProperty("details").GetProperty("path").GetString(), Is.EqualTo(missingPath));
    }

    [Test]
    public async Task SymbolicCli_MissingReference_EmitsReferenceErrorEnvelope()
    {
        var missingPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "MissingReference-" + Guid.NewGuid().ToString("N") + ".dll");
        var result = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            "class C { }",
            "--line",
            "1",
            "--reference",
            missingPath,
            "--error-json");

        var error = AssertErrorEnvelope(
            result,
            SymbolicErrorCodes.ReferenceNotFound,
            SymbolicErrorCategory.Input,
            SymbolicErrorExitCodes.MissingInput);
        Assert.That(error.GetProperty("details").GetProperty("path").GetString(), Is.EqualTo(missingPath));
    }

    [Test]
    public async Task SymbolicCli_InvalidTarget_EmitsInvalidDataEnvelope()
    {
        var result = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            "class C { }",
            "--line",
            "99",
            "--compact-json");

        AssertErrorEnvelope(
            result,
            SymbolicErrorCodes.InvalidTarget,
            SymbolicErrorCategory.Input,
            SymbolicErrorExitCodes.InvalidData);
    }

    [Test]
    public async Task SymbolicCli_MissingProject_EmitsProjectErrorEnvelope()
    {
        var missingPath = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "MissingProject-" + Guid.NewGuid().ToString("N") + ".csproj");
        var result = await SymbolicCliTestHost.RunAsync(
            "--file",
            Path.Combine("SharpProof.Demo", "Program.cs"),
            "--project",
            missingPath,
            "--line",
            "1",
            "--error-json");

        AssertErrorEnvelope(
            result,
            SymbolicErrorCodes.ProjectLoadFailed,
            SymbolicErrorCategory.Project,
            SymbolicErrorExitCodes.MissingInput);
    }

    [Test]
    public async Task SymbolicCli_TextFailure_EmitsStableCodeAndUsageOnStderr()
    {
        var result = await SymbolicCliTestHost.RunAsync(
            "--source-text",
            "class C { }",
            "--line",
            "1",
            "--unknown-option");

        Assert.That(result.ExitCode, Is.EqualTo(SymbolicErrorExitCodes.Usage));
        Assert.That(result.StandardOutput, Is.Empty);
        Assert.That(result.StandardError, Does.Contain(SymbolicErrorCodes.InvalidRequest));
        Assert.That(result.StandardError, Does.Contain("[Usage]"));
        Assert.That(result.StandardError, Does.Contain("Usage: SharpProof.SymbolicCli"));
    }

    private static JsonElement AssertErrorEnvelope(
        (int ExitCode, string StandardOutput, string StandardError) result,
        string expectedCode,
        SymbolicErrorCategory expectedCategory,
        int expectedExitCode)
    {
        Assert.That(result.ExitCode, Is.EqualTo(expectedExitCode), result.StandardError);
        Assert.That(result.StandardError, Is.Empty);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("error"));
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        var error = root.GetProperty("error");
        Assert.That(error.GetProperty("code").GetString(), Is.EqualTo(expectedCode));
        Assert.That(error.GetProperty("category").GetString(), Is.EqualTo(expectedCategory.ToString()));
        Assert.That(error.GetProperty("recommendedExitCode").GetInt32(), Is.EqualTo(expectedExitCode));
        Assert.That(error.GetProperty("message").GetString(), Is.Not.Empty);
        return error.Clone();
    }
}
