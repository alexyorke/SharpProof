using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[NonParallelizable]
public sealed class LoopScriptCaseSensitivityTests
{
    [TestCase("LoopSnapshot")]
    [TestCase("PreprocessorSymbols")]
    public async Task CaseSensitiveScriptInputsRemainDistinct(string scenario)
    {
        var root = TestRepository.FindRoot();
        var result = await ArchitectureRepository.RunScriptAsync(
            root,
            "Test-SharpProofLoopCaseSensitivityFixtures.ps1",
            "-Scenario",
            scenario);

        await ArchitectureRepository.AssertSuccessAsync(
            Task.FromResult(result),
            includeOutput: true);
    }
}
