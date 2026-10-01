using NUnit.Framework;

namespace SharpProof.Fuzz.Test;

[TestFixture]
public sealed class TotalProgramFuzzTests
{
    [Test]
    public async Task RepresentativeTotalBodiesAgreeWithCompiledCodeAndNativeReplay()
    {
        var result = await TotalProgramDifferentialOracle.RunAsync(128, 23063);
        Assert.That(result.Passed, Is.True, string.Join("\n", result.Failures));
        Assert.That(result.Coverage.Agreements, Is.EqualTo(128));
        Assert.That(result.Coverage.NativeProofs, Is.EqualTo(128));
        Assert.That(result.Coverage.NativeRefutations + result.Coverage.ExceptionalExits, Is.EqualTo(128));
        Assert.That(result.Coverage.HasExpandedCategories, Is.True);
    }
}
