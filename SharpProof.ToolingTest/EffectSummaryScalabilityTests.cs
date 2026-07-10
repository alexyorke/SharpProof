using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Test;

[TestFixture]
public sealed class EffectSummaryScalabilityTests
{
    [Test]
    public async Task EffectSummaryTool_RejectsNonPositiveExceptionEdgeCap()
    {
        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--max-exception-edges",
            "0");

        Assert.That(result.ExitCode, Is.Not.EqualTo(0));
        Assert.That(result.StandardError, Does.Contain("must be greater than zero"));
    }

    [Test]
    public async Task EffectSummaryTool_BoundsTransitiveExceptionEdgesForUnboundedCalleeRuns()
    {
        var source = """
using System;

public static class ExceptionFanout
{
    public static void Root()
    {
        Throw0();
        Throw1();
        Throw2();
        Throw3();
    }

    public static void Throw0() => throw new InvalidOperationException();
    public static void Throw1() => throw new InvalidOperationException();
    public static void Throw2() => throw new InvalidOperationException();
    public static void Throw3() => throw new InvalidOperationException();
}
""";

        await using var fixture = await EffectSummaryToolTests.CreateFixtureAssemblyAsync(
            "EffectSummaryExceptionEdgeCap",
            source);
        var result = await EffectSummaryToolTests.RunEffectSummaryProcessAsync(
            "--assembly",
            fixture.AssemblyPath,
            "--symbol-prefix",
            "ExceptionFanout.Root",
            "--include-callees",
            "--max-depth",
            "-1",
            "--transitive-roots",
            "--max-exception-edges",
            "2");

        Assert.That(
            result.ExitCode,
            Is.EqualTo(0),
            result.StandardError + Environment.NewLine + result.StandardOutput);

        using var summary = JsonDocument.Parse(result.StandardOutput);
        var root = summary.RootElement
            .GetProperty("Assemblies")[0]
            .GetProperty("Methods")
            .EnumerateArray()
            .Single(method => string.Equals(
                method.GetProperty("Symbol").GetString(),
                "ExceptionFanout.Root()",
                StringComparison.Ordinal));

        Assert.That(
            root.GetProperty("TransitiveThrownExceptionEdges").GetArrayLength(),
            Is.EqualTo(2));
        Assert.That(
            root.GetProperty("TransitiveThrownExceptionEdgesTruncated").GetBoolean(),
            Is.True);
    }
}
