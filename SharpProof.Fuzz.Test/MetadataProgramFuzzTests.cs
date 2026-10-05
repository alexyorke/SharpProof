using NUnit.Framework;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class MetadataProgramFuzzTests
{
    [TestCase(140)]
    [TestCase(1000)]
    public async Task ScalarImplementationImagesAgreeWithCompiledCodeAndOwnedReplay(int cases)
    {
        var timings = new Dictionary<string, long>(StringComparer.Ordinal);
        var result = await MetadataProgramDifferentialOracle.RunAsync(cases, 23063, timingSink: (stage, elapsed) =>
        { timings[stage] = timings.GetValueOrDefault(stage) + elapsed; });
        Assert.That(result.Passed, Is.True, string.Join("\n", result.Failures));
        Assert.That(result.Agreements, Is.EqualTo(cases));
        Assert.That(result.NativeProofs, Is.EqualTo(cases));
        Assert.That(result.NativeRefutations, Is.EqualTo(cases));
        Assert.That(result.TypeMask, Is.EqualTo(1023));
        Assert.That(result.RecipeMask, Is.EqualTo(127));
        await TestContext.Out.WriteLineAsync($"metadata stages: cases={cases} compile-ms={timings["compile"]} artifact-ms={timings["artifact"]} verification-ms={timings["verification"]}");
    }
}
