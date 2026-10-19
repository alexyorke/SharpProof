using System.Collections.Immutable;
using SharpProof.Ir;
using NUnit.Framework;
using SharpProof.Host;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class WorkerLauncherArgumentCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task BootstrapDistinguishesSafeUsageFromUnsafeRuntimeOverrides(bool unsafeOverride)
    {
        // This fixture runs without parallel tests: keep ambient test-host overrides
        // private to this invocation and restore every value even when an assertion fails.
        var savedEnvironment = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
        var savedEnvironmentOrder = ImmutableArray.CreateBuilder<string>();
        var originalError = Console.Error;
        using var diagnostic = new StringWriter();
        try
        {
            for (var attempt = 0; attempt < 64; attempt++)
            {
                var variable = TrustedChildEnvironment.FindUnsafeRuntimeVariable();
                if (variable == null)
                {
                    break;
                }
                savedEnvironment.AddUnique(variable, Environment.GetEnvironmentVariable(variable));
                savedEnvironmentOrder.Add(variable);
                Environment.SetEnvironmentVariable(variable, null);
            }
            Assert.That(TrustedChildEnvironment.FindUnsafeRuntimeVariable(), Is.Null);
            if (unsafeOverride)
            {
                const string variable = "DOTNET_STARTUP_HOOKS";
                if (savedEnvironment.TryAdd(variable, Environment.GetEnvironmentVariable(variable)))
                { savedEnvironmentOrder.Add(variable); }
                Environment.SetEnvironmentVariable(variable, "untrusted-bootstrap-hook");
            }
            Console.SetError(diagnostic);
            var exitCode = await Launcher.Program.Main([]);
            Assert.That(exitCode, Is.EqualTo(unsafeOverride ? 125 : 2));
            Assert.That(diagnostic.ToString(), Does.StartWith(unsafeOverride
                ? "SharpProof launcher refused unsafe runtime environment variable DOTNET_STARTUP_HOOKS."
                : "Usage: SharpProof.Worker "));
        }
        finally
        {
            Console.SetError(originalError);
            var capturedEnvironment = savedEnvironment.ToImmutable();
            foreach (var name in savedEnvironmentOrder.ToImmutable())
            {
                Environment.SetEnvironmentVariable(name, capturedEnvironment[name]);
            }
        }
    }
    [TestCase("missing-prefix")]
    [TestCase("empty-required-value")]
    [TestCase("partial-publication")]
    [TestCase("sarif-without-publication")]
    public async Task InvalidInvocationReportsUsageBeforeCreatingPrivateFiles(string scenario)
    {
        System.ArgumentNullException.ThrowIfNull(scenario);
        using var directory = new TempDirectory("SharpProof.Worker.Test-cli-");
        var request = Path.Combine(directory.FullName, "request.json");
        var result = Path.Combine(directory.FullName, "result.json");
        var arguments = ImmutableArray.CreateBuilder<string>();
        arguments.AddRange(ImmutableArray.Create(
            "verify", "--request", request, "--result", result,
            "--compiler-manifest", Path.Combine(directory.FullName, "manifest.json"),
            "--verify-policy", "advisory", "--assumption-policy", "allow"));
        switch (scenario)
        {
            case "missing-prefix":
                arguments[1] = "request";
                break;
            case "empty-required-value":
                arguments[2] = " ";
                break;
            case "partial-publication":
                arguments.AddRange(ImmutableArray.Create("--publish-request", Path.Combine(directory.FullName, "published-request.json")));
                break;
            case "sarif-without-publication":
                arguments.AddRange(ImmutableArray.Create("--publish-sarif", Path.Combine(directory.FullName, "report.sarif")));
                break;
        }
        var originalError = Console.Error;
        using var diagnostic = new StringWriter();
        int exitCode;
        try
        {
            Console.SetError(diagnostic);
            exitCode = await Launcher.Program.RunMain(arguments.ToImmutable().ToArray(), verify: null, validatePreflight: null,
                cancellationToken: TestContext.CurrentContext.CancellationToken);
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
