using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Fuzz.Test;

[TestFixture]
[NonParallelizable]
[Platform("Linux")]
public sealed class FuzzHostSignalTests : IDisposable
{
    private TempDirectory? directory;
    private string? runtimeDirectory;

    [OneTimeSetUp]
    public void PrepareRealFuzzHost()
    {
        TestRepository.RequireCanonicalContainer();
        var elapsed = Stopwatch.StartNew();
        directory = new TempDirectory("SharpProof-fuzz-host-");
        runtimeDirectory = Path.Combine(directory.FullName, "runtime");
        foreach (var source in Directory.EnumerateFiles(AppContext.BaseDirectory, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(runtimeDirectory, Path.GetRelativePath(AppContext.BaseDirectory, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }

        var platformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        var references = platformAssemblies.Split(Path.PathSeparator)
            .Append(typeof(FuzzRunner).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "SharpProof.Fuzz.SignalHost",
            [CSharpSyntaxTree.ParseText(HostSource, new CSharpParseOptions(LanguageVersion.CSharp12))],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication));
        using var assembly = File.Create(Path.Combine(runtimeDirectory, "SharpProof.Fuzz.SignalHost.dll"));
        var emitted = compilation.Emit(assembly);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        TestContext.Progress.WriteLine($"Fuzz signal host copy and compilation: {elapsed.Elapsed}.");
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        directory?.Dispose();
        directory = null;
        GC.SuppressFinalize(this);
    }

    [Test]
    public async Task HostWithoutSignalPreservesRealFuzzResults()
    {
        var result = await RunHostAsync(cases: 24, sendSignal: false);
        Assert.That(result.ExitCode, Is.Zero, result.CombinedOutput);
        Assert.That(result.Error, Is.Empty);
        var summary = JsonSerializer.Deserialize<FuzzSummary>(result.Output);
        Assert.That(summary, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(summary!.Passed, Is.True);
            Assert.That(summary.Cases, Is.EqualTo(24));
            Assert.That(summary.Seed, Is.EqualTo(12345));
            Assert.That(summary.MaximumParallelism, Is.EqualTo(1));
            Assert.That(summary.Agreements, Is.EqualTo(24));
            Assert.That(summary.FrontendAgreements, Is.EqualTo(24));
            Assert.That(summary.SmtAgreements, Is.EqualTo(24));
            Assert.That(summary.PartialSmtAgreements, Is.EqualTo(24));
            Assert.That(summary.TotalProgramCoverage.Agreements, Is.EqualTo(24));
            Assert.That(summary.MetadataProgramCoverage.Agreements, Is.EqualTo(24));
            Assert.That(summary.Abstentions, Is.Zero);
            Assert.That(summary.Failures, Is.Empty);
        }
    }

    [Test]
    public async Task HostReceivesOneSignalAndCancelsRealFuzzTask()
    {
        var result = await RunHostAsync(FuzzOptions.DefaultCases, sendSignal: true);
        Assert.That(result.ExitCode, Is.EqualTo(130), result.CombinedOutput);
        Assert.That(result.Error, Is.Empty);
        using var document = JsonDocument.Parse(result.Output);
        var observation = document.RootElement;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.GetProperty("Signals").GetInt32(), Is.EqualTo(1));
            Assert.That(observation.GetProperty("TokenCancelled").GetBoolean(), Is.True);
            Assert.That(observation.GetProperty("TaskCancelled").GetBoolean(), Is.True);
        }
    }

    private async Task<ProcessRunnerResult> RunHostAsync(int cases, bool sendSignal)
    {
        var elapsed = Stopwatch.StartNew();
        var ready = Path.Combine(directory!.FullName, "ready-" + Guid.NewGuid().ToString("N"));
        var start = ProcessRunner.CreateStartInfo(runtimeDirectory!, "dotnet",
        [
            "exec", "--runtimeconfig", Path.Combine(runtimeDirectory!, "SharpProof.Fuzz.Test.runtimeconfig.json"),
            "--depsfile", Path.Combine(runtimeDirectory!, "SharpProof.Fuzz.Test.deps.json"),
            Path.Combine(runtimeDirectory!, "SharpProof.Fuzz.SignalHost.dll"),
            cases.ToString(CultureInfo.InvariantCulture), ready
        ]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        using var child = Process.Start(start) ?? throw new InvalidOperationException("The fuzz host did not start.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        var completed = false;
        try
        {
            while (!File.Exists(ready))
            {
                if (child.HasExited)
                {
                    Assert.Fail("The fuzz host exited before readiness: " + await output + await error);
                }
                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }

            if (sendSignal)
            {
                // Readiness proves handler registration and the real API's initial call returned.
                // It does not identify the native query phase at the instant of this one signal.
                using var senderTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                senderTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                var sender = ProcessRunner.CreateStartInfo(runtimeDirectory!, "/bin/bash",
                    ["-c", "kill -INT \"$1\"", "--", child.Id.ToString(CultureInfo.InvariantCulture)]);
                var sent = await ProcessRunner.RunCapturedAsync(sender, senderTimeout.Token);
                Assert.That(sent.ExitCode, Is.Zero, sent.CombinedOutput);
            }

            await child.WaitForExitAsync(timeout.Token);
            var result = new ProcessRunnerResult(child.ExitCode, await output, await error);
            completed = true;
            return result;
        }
        finally
        {
            if (!child.HasExited)
            {
                try
                {
                    child.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) when (child.HasExited)
                {
                    // The child exited between the lifetime check and Kill.
                }
            }
            await child.WaitForExitAsync(TestContext.CurrentContext.CancellationToken);
            await Task.WhenAll(output, error);
            if (!completed)
            {
                var capturedOutput = await output;
                var capturedError = await error;
                await TestContext.Progress.WriteLineAsync("Fuzz host stdout: " + capturedOutput[..Math.Min(capturedOutput.Length, 4096)]);
                await TestContext.Progress.WriteLineAsync("Fuzz host stderr: " + capturedError[..Math.Min(capturedError.Length, 4096)]);
            }
            await TestContext.Progress.WriteLineAsync($"Fuzz host cases={cases}, signal={sendSignal}, elapsed={elapsed.Elapsed}.");
        }
    }

    // This is an independent host of the public API, not the product's Program entry point.
    // Its handler, token, task and result are real; no solver or native status is substituted.
    private const string HostSource = """
        using System;
        using System.Globalization;
        using System.IO;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using SharpProof.Fuzz;

        using var cancellation = new CancellationTokenSource();
        var signals = 0;
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Interlocked.Increment(ref signals);
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            var options = new FuzzOptions(int.Parse(args[0], CultureInfo.InvariantCulture), 12345, 1);
            Task<FuzzSummary> run = FuzzRunner.RunAsync(options, cancellation.Token);
            File.WriteAllText(args[1] + ".tmp", "ready");
            File.Move(args[1] + ".tmp", args[1]);
            try
            {
                var summary = await run;
                Console.WriteLine(JsonSerializer.Serialize(summary));
                return summary.Passed ? 0 : 1;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    Signals = Volatile.Read(ref signals),
                    TokenCancelled = cancellation.IsCancellationRequested,
                    TaskCancelled = run.IsCanceled
                }));
                return 130;
            }
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
        """;
}
