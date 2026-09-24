using System.Text;
using NUnit.Framework;

namespace SharpProof.ArchitectureTest;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class PackageDependencyAuthorityTests
{
    [TestCase("canonical", true)]
    [TestCase("fabricated", false)]
    [TestCase("missing", false)]
    [TestCase("duplicate", false)]
    [TestCase("swapped-owner", false)]
    [TestCase("foreign-entry", false)]
    [TestCase("self-consistent-rewrite", false)]
    public async Task ThirdPartyInventoryMatchesCatalogPayload(
        string mutation,
        bool expectedSuccess)
    {
        using var workspace = new TempDirectory("sharpproof-component-authority-");
        var root = workspace.FullName;
        var result = await RunComponentAuthorityAsync(root, mutation);
        Assert.That(
            result.ExitCode == 0,
            Is.EqualTo(expectedSuccess),
            result.Output + Environment.NewLine + result.Error);
    }

    private static async Task<ProcessRunnerResult> RunComponentAuthorityAsync(
        string root,
        string mutation)
    {
        var repositoryRoot = TestRepository.FindRoot();
        var runner = Path.Combine(root, "run-component-authority.ps1");
        await File.WriteAllTextAsync(
            runner,
            "param([string]$Helper, [string]$Mutation)\n" +
            ". $Helper\n" +
            "$expected=@(" +
            "[pscustomobject]@{packageId='SharpProof';id='Component.A';" +
            "version='1.0';license='MIT';entries=@('tools/a.dll');" +
            "entrySha256=@()}," +
            "[pscustomobject]@{packageId='SharpProof.Verifier';id='Component.B';" +
            "version='2.0';license='MIT';entries=@('tools/b.so');" +
            "entrySha256=@()})\n" +
            "$actual=@($expected | ConvertTo-Json -Depth 4 | ConvertFrom-Json)\n" +
            "switch ($Mutation) {\n" +
            " 'fabricated' {$actual[0].id='Fabricated'}\n" +
            " 'missing' {$actual=@($actual[0])}\n" +
            " 'duplicate' {$actual+= $actual[0]}\n" +
            " 'swapped-owner' {$actual[0].packageId='SharpProof.Verifier'}\n" +
            " 'foreign-entry' {$actual[0].entries=@('tools/foreign.dll')}\n" +
            " 'self-consistent-rewrite' {$actual[0].id='Fabricated'}\n" +
            "}\n" +
            "Test-SharpProofThirdPartyComponentProjection " +
            "-ActualComponents $actual -ExpectedComponents $expected\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return await RunPowerShellAsync(repositoryRoot, runner, mutation);
    }

    private static Task<ProcessRunnerResult> RunPowerShellAsync(
        string repositoryRoot,
        string runner,
        params string[] arguments)
    {
        var startInfo = ProcessRunner.CreateStartInfo(
            repositoryRoot,
            "pwsh",
            [
                "-NoLogo",
                "-NoProfile",
                "-File",
                runner,
                Path.Combine(
                    repositoryRoot,
                    "scripts",
                    "Test-SharpProofPackageDependencies.ps1"),
                .. arguments
            ]);
        return ProcessRunner.RunCapturedAsync(
            startInfo,
            CancellationToken.None);
    }
}
