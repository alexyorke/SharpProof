using System.Text.Json;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.Test;

internal static class SymbolicCliTestAssertions
{
    internal static void AssertCompactEnvelope(JsonElement root, string kind)
    {
        var canonicalProperty = kind == "capabilities" ? "capabilities" : "complexity";
        Assert.That(root.TryGetProperty(canonicalProperty, out _), Is.True);
        Assert.That(root.GetProperty("filePath").GetString(), Is.Not.Empty);
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
