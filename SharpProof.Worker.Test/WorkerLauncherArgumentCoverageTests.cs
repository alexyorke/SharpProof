using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerLauncherArgumentCoverageTests
{
    [TestCase("missing-prefix")]
    [TestCase("empty-required-value")]
    [TestCase("partial-publication")]
    [TestCase("sarif-without-publication")]
    public async Task InvalidInvocationReportsUsageBeforeCreatingPrivateFiles(string scenario)
    {
        using var directory = new TempDirectory("SharpProof.Worker.Test-cli-");
        var request = Path.Combine(directory.FullName, "request.json");
        var result = Path.Combine(directory.FullName, "result.json");
        var arguments = new List<string>
        {
            "verify", "--request", request, "--result", result,
            "--compiler-manifest", Path.Combine(directory.FullName, "manifest.json"),
            "--verify-policy", "advisory", "--assumption-policy", "allow"
        };
        switch (scenario)
        {
            case "missing-prefix":
                arguments[1] = "request";
                break;
            case "empty-required-value":
                arguments[2] = " ";
                break;
            case "partial-publication":
                arguments.AddRange(["--publish-request", Path.Combine(directory.FullName, "published-request.json")]);
                break;
            case "sarif-without-publication":
                arguments.AddRange(["--publish-sarif", Path.Combine(directory.FullName, "report.sarif")]);
                break;
        }
        var originalError = Console.Error;
        using var diagnostic = new StringWriter();
        int exitCode;
        try
        {
            Console.SetError(diagnostic);
            exitCode = await Launcher.Program.Main([.. arguments]);
        }
        finally
        {
            Console.SetError(originalError);
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(exitCode, Is.EqualTo(2));
            Assert.That(diagnostic.ToString(), Does.StartWith("Usage: SharpProof.Worker "));
            Assert.That(Directory.EnumerateFileSystemEntries(directory.FullName), Is.Empty);
        }
    }
}
