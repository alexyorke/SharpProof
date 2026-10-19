using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

[NonParallelizable]
public sealed class FuzzCliTests
{
    [Test]
    [NonParallelizable]
    public async Task CliPrintsHelpAndCanBeCalledAgain()
    {
        var originalError = Console.Error;
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Console.SetError(error);
        try
        {
            Assert.That(await RunEntryPointAsync(["--help"], Console.Out, Console.Error), Is.Zero);
            Assert.That(await RunEntryPointAsync(["-h"], Console.Out, Console.Error), Is.Zero);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.That(error.ToString().Split(Environment.NewLine,
            StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(2));
        Assert.That(error.ToString(), Does.Contain("Usage: SharpProof.Fuzz"));
    }

    [Test]
    public async Task InvalidOptionPrintsDiagnosticAndUsage()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = await RunEntryPointAsync(["--unknown", "1"], output, error);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.StartWith("Unknown option: --unknown."));
        Assert.That(error.ToString(), Does.Contain("Usage: SharpProof.Fuzz"));
    }

    [Test]
    public async Task OneCaseCampaignPrintsSuccessfulJsonSummary()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        var exitCode = await RunEntryPointAsync(
            ["--cases", "1", "--seed", "12345", "--max-parallelism", "1"], output, error);
        using var summary = JsonDocument.Parse(output.ToString());

        Assert.That(exitCode, Is.Zero, error.ToString());
        Assert.That(error.ToString(), Is.Empty);
        Assert.That(summary.RootElement.GetProperty("Cases").GetInt32(), Is.EqualTo(1));
        Assert.That(summary.RootElement.GetProperty("Seed").GetInt32(), Is.EqualTo(12345));
        Assert.That(summary.RootElement.GetProperty("Passed").GetBoolean(), Is.True);
        Assert.That(summary.RootElement.GetProperty("Failures").GetArrayLength(), Is.Zero);
    }

    [Test]
    public async Task CancelledCampaignPrintsDiagnosticAndCancellationExitCode()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exitCode = await FuzzRunner.RunCliAsync(["--cases", "1"], output, error,
            cancellation.Token);

        Assert.That(exitCode, Is.EqualTo(130));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Is.EqualTo("SharpProof fuzz run cancelled." +
            Environment.NewLine));
    }

    private static async Task<int> RunEntryPointAsync(
        ImmutableArray<string> arguments,
        TextWriter output,
        TextWriter error)
    {
        if (arguments.IsDefault)
        { throw new ArgumentNullException(nameof(arguments)); }
        System.ArgumentNullException.ThrowIfNull(error);
        System.ArgumentNullException.ThrowIfNull(output);
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var entryPoint = typeof(FuzzRunner).Assembly.EntryPoint;
            Assert.That(entryPoint, Is.Not.Null);
            var result = await Task.Run(() => entryPoint!.Invoke(null, [arguments.ToArray()]));
            return result switch
            {
                Task<int> task => await task,
                int exitCode => exitCode,
                _ => throw new InvalidOperationException("Unexpected executable entrypoint result.")
            };
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }
}
