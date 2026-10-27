using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

[NonParallelizable]
public sealed class FuzzConsoleCancellationTests
{
    [Test]
    [Category("Coverage")]
    public async Task ConsoleInterruptCancelsOfficialExecutableEntryPoint()
    {
        TestRepository.RequireCanonicalContainer();
        Assert.That(Environment.GetEnvironmentVariable("SHARPPROOF_FUZZ_CONSOLE_ISOLATED"),
            Is.EqualTo("1"), "This fixture requires its sole isolated testhost.");
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        using var stopSignalTimer = new CancellationTokenSource();
        var observedSignal = 0;
        ConsoleCancelEventHandler protectHost = (_, eventArgs) =>
        {
            System.ArgumentNullException.ThrowIfNull(eventArgs);
            eventArgs.Cancel = true;
            Interlocked.Exchange(ref observedSignal, 1);
        };
        Console.CancelKeyPress += protectHost;
        Console.SetOut(output);
        Console.SetError(error);
        var signalTask = SendInterruptAsync(stopSignalTimer.Token);
        try
        {
            var entryPoint = typeof(FuzzRunner).Assembly.EntryPoint;
            Assert.That(entryPoint, Is.Not.Null);
            ImmutableArray<string> arguments = ["--cases", "1000", "--seed", "12345",
                "--max-parallelism", "1"];
            var result = await Task.Run(() => entryPoint!.Invoke(null, [arguments.ToArray()]));
            var exitCode = result switch
            {
                Task<int> task => await task,
                int code => code,
                _ => throw new InvalidOperationException("Unexpected executable entrypoint result.")
            };
            await signalTask;

            Assert.That(observedSignal, Is.EqualTo(1));
            Assert.That(exitCode, Is.EqualTo(130));
            Assert.That(output.ToString(), Is.Empty);
            Assert.That(error.ToString(), Is.EqualTo("SharpProof fuzz run cancelled." +
                Environment.NewLine));
        }
        finally
        {
            await stopSignalTimer.CancelAsync();
            try
            {
                await signalTask;
            }
            catch (OperationCanceledException) when (stopSignalTimer.IsCancellationRequested)
            {
            }
            finally
            {
                Console.CancelKeyPress -= protectHost;
                Console.SetOut(originalOutput);
                Console.SetError(originalError);
            }
        }
    }

    private static async Task SendInterruptAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        var start = new ProcessStartInfo("/bin/bash")
        {
            UseShellExecute = false
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("kill -INT \"$1\"");
        start.ArgumentList.Add("fuzz-console-interrupt");
        start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        using var signal = Process.Start(start) ??
            throw new InvalidOperationException("Could not start the isolated host interrupt.");
        await signal.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        Assert.That(signal.ExitCode, Is.Zero);
    }
}
