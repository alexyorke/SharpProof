using System.Text.Json;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

internal static class SymbolicCliTestAssertions
{
    internal static void AssertCompactEnvelope(JsonElement root, string kind)
    {
        Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo(kind));
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        AssertEvidenceSchema(root);
    }

    internal static void AssertEvidenceSchema(JsonElement root)
    {
        Assert.That(root.GetProperty("evidenceSchemaVersion").GetInt32(),
            Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion));
        Assert.That(root.GetProperty("evidenceSchemaCompatibility").GetString(),
            Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy));
    }

    internal static async Task AssertRejectsAllLinesAsync(string sourcePath, string mode)
    {
        var result = await SymbolicCliTestHost.RunOutOfProcessAsync(
            "--file",
            sourcePath,
            "--" + mode,
            "--all-lines");

        Assert.That(result.ExitCode, Is.EqualTo(64));
        Assert.That(result.StandardError,
            Does.Contain("--" + mode + " supports --line, --line with --column, or --position only."));
    }
}
