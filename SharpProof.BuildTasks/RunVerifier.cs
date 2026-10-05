using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Build.Framework;
using SharpProof.Host;
using SharpProof.Worker.Protocol;

namespace SharpProof.BuildTasks;

// Runs the verifier as one child process with a hard deadline. On timeout or
// cancellation the whole process tree is killed; the verifier writes its own
// typed result, so a killed run simply has no fresh result.
public sealed class RunVerifier : Microsoft.Build.Utilities.Task,
    ICancelableTask, IDisposable
{
    // The verifier enforces the project budget itself and writes a typed
    // TimedOut result. This reserve covers process startup and publication;
    // the task's kill is only a safety net for a hung process.
    internal const int LauncherProcessReserveMilliseconds = PublicationLease.TimeoutMilliseconds;
    internal const int MaximumCapturedOutputCharacters = 1_048_576;
    // Once the verifier exits, only a leaked descendant can keep its pipes
    // open; the build does not wait for one.
    internal const int OutputDrainMilliseconds = 2000;
    private const int StructuredRefutedFailureExitCode = 5;
    private const int StructuredSemanticFailureExitCode = 6;
    private const int InvalidInputExitCode = 2;
    private const int PublicationFailureExitCode = 3;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _cancellationSignal = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Process? _process;

    [Required]
    public string Executable { get; set; } = string.Empty;

    [Required]
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "MSBuild task item parameters use ITaskItem arrays.")]
    public ITaskItem[] Arguments { get; set; } = [];

    [Required]
    public string WorkingDirectory { get; set; } = string.Empty;

    public int ProjectWallTimeMilliseconds { get; set; } = WorkerBudgets.DefaultProjectWallTimeMilliseconds;

    public int TerminationGraceMilliseconds { get; set; } = WorkerLauncherDefaults.TerminationGraceMilliseconds;

    [Output]
    public int ExitCode { get; set; }

    [Output]
    public bool HasStructuredError { get; set; }

    public void Dispose()
    {
        _cancellationSignal.Dispose();
        _cancellation.Dispose();
        _process?.Dispose();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The MSBuild boundary reports every launch failure as a classified task result.")]
    public override bool Execute()
    {
        HasStructuredError = false;
        ExitCode = 0;
        using var process = new Process();
        try
        {
            ContainerContract.ValidateRequired();
            var timeout = ComputeProcessTimeout(
                ProjectWallTimeMilliseconds,
                TerminationGraceMilliseconds);
            var operation = Stopwatch.StartNew();
            var executable = ResolveDotNetHost(Executable);
            process.StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetFullPath(WorkingDirectory),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            TrustedChildEnvironment.Apply(process.StartInfo, executable);
            var childArguments = Arguments.Select(argument => argument.ItemSpec).ToArray();
            VerificationPublication? publication;
            try
            {
                publication = VerificationPublication.Prepare(childArguments, process.StartInfo.WorkingDirectory);
            }
            catch (ArgumentException exception)
            {
                ExitCode = InvalidInputExitCode;
                Log.LogMessage(MessageImportance.High, "SharpProof launcher input is invalid: {0}: {1}",
                    exception.GetType().Name, exception.Message);
                return true;
            }
            foreach (var argument in childArguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            System.Threading.Tasks.Task<string> standardOutput;
            System.Threading.Tasks.Task<string> standardError;
            lock (_gate)
            {
                if (_cancellationSignal.IsSet)
                {
                    ExitCode = -1;
                    return true;
                }
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "The SharpProof verifier process could not be started.");
                }
                _process = process;
                standardOutput = ReadBoundedAsync(process.StandardOutput);
                standardError = ReadBoundedAsync(process.StandardError);
            }

            var exited = WaitForExitOrCancellation(process, timeout);
            if (!exited)
            {
                Kill(process);
            }
            process.WaitForExit();
            if (!System.Threading.Tasks.Task.WaitAll(
                    [standardOutput, standardError], OutputDrainMilliseconds))
            {
                Log.LogMessage(
                    MessageImportance.High,
                    "SharpProof verifier left a descendant holding its output; output was discarded.");
            }
            var output = standardOutput.IsCompletedSuccessfully ? standardOutput.Result : string.Empty;
            var error = standardError.IsCompletedSuccessfully ? standardError.Result : string.Empty;
            if (!string.IsNullOrWhiteSpace(output))
            {
                Log.LogMessage(MessageImportance.High, "{0}", output);
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                LogStandardError(error);
            }

            ExitCode = _cancellationSignal.IsSet
                ? -1
                : exited
                    ? process.ExitCode
                    : LinuxProcessControlConstants.TimeoutExitCode;
            if (publication != null && ExitCode is 0 or StructuredRefutedFailureExitCode or StructuredSemanticFailureExitCode)
            {
                var remaining = timeout - operation.ElapsedMilliseconds;
                if (remaining <= 0)
                { throw new TimeoutException("Verification publication exceeded the process deadline."); }
                using var publicationDeadline = CancellationTokenSource.CreateLinkedTokenSource(_cancellation.Token);
                publicationDeadline.CancelAfter(TimeSpan.FromMilliseconds(remaining));
                try
                {
                    publication.Publish(publicationDeadline.Token);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    ExitCode = PublicationFailureExitCode;
                    Log.LogMessage(MessageImportance.High, "SharpProof worker result could not be published.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            ExitCode = _cancellationSignal.IsSet ? -1 : LinuxProcessControlConstants.TimeoutExitCode;
        }
        catch (TimeoutException)
        {
            Kill(process);
            ExitCode = LinuxProcessControlConstants.TimeoutExitCode;
        }
        catch (Exception exception)
        {
            Kill(process);
            ExitCode = -1;
            Log.LogMessage(
                MessageImportance.High,
                "SharpProof verifier launch failed: {0}",
                exception.Message);
        }
        finally
        {
            lock (_gate)
            {
                _process = null;
            }
        }

        // Exits 5 and 6 are completed semantic failures. A diagnostic seen
        // before any other nonzero exit is partial and must not suppress the
        // target's infrastructure error.
        HasStructuredError &=
            ExitCode is StructuredRefutedFailureExitCode or
                StructuredSemanticFailureExitCode;
        return true;
    }

    public void Cancel()
    {
        Process? process;
        lock (_gate)
        {
            _cancellationSignal.Set();
            _cancellation.Cancel();
            process = _process;
        }
        if (process != null)
        {
            Kill(process);
        }
    }

    internal static int ComputeProcessTimeout(
        int projectWallTimeMilliseconds,
        int terminationGraceMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(projectWallTimeMilliseconds, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(terminationGraceMilliseconds, 1);
        return checked(
            projectWallTimeMilliseconds +
            terminationGraceMilliseconds +
            LauncherProcessReserveMilliseconds);
    }

    internal void LogStandardError(string standardError)
    {
        using var reader = new StringReader(standardError);
        while (reader.ReadLine() is { } line)
        {
            if (VerifierDiagnosticTransport.TryDeserialize(line, out var diagnostic))
            {
                LogStructuredDiagnostic(diagnostic);
            }
            else if (!string.IsNullOrWhiteSpace(line))
            {
                Log.LogMessage(MessageImportance.High, "{0}", line);
            }
        }
    }

    internal static string ResolveDotNetHost(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must name the direct dotnet muxer.");
        }

        var currentHost = Environment.ProcessPath ??
            throw new InvalidOperationException(
                "SharpProof verifier could not identify its current dotnet muxer.");
        var trusted = ValidateDotNetInstallation(currentHost);
        if (string.Equals(executable, "dotnet", StringComparison.Ordinal))
        {
            return trusted;
        }
        var configured = ValidateDotNetInstallation(executable);
        if (!string.Equals(configured, trusted, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must match the trusted current dotnet muxer.");
        }
        return configured;
    }

    private bool WaitForExitOrCancellation(Process process, int timeoutMilliseconds)
    {
        var deadline = Stopwatch.StartNew();
        while (!process.WaitForExit(50))
        {
            if (_cancellationSignal.IsSet ||
                deadline.ElapsedMilliseconds >= timeoutMilliseconds)
            {
                return false;
            }
        }
        return true;
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
                System.ComponentModel.Win32Exception or NotSupportedException)
        {
        }
    }

    private static async System.Threading.Tasks.Task<string> ReadBoundedAsync(TextReader reader)
    {
        var captured = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
        {
            var room = MaximumCapturedOutputCharacters - captured.Length;
            if (room > 0)
            {
                captured.Append(buffer, 0, Math.Min(room, count));
            }
        }
        return captured.ToString();
    }

    private void LogStructuredDiagnostic(VerifierDiagnostic diagnostic)
    {
        if (diagnostic.Severity == "error")
        {
            HasStructuredError = true;
            Log.LogError(
                string.Empty, diagnostic.Code, string.Empty, diagnostic.File,
                diagnostic.Line, diagnostic.Column, 0, 0, diagnostic.Message);
            return;
        }

        Log.LogWarning(
            string.Empty, diagnostic.Code, string.Empty, diagnostic.File,
            diagnostic.Line, diagnostic.Column, 0, 0, diagnostic.Message);
    }

    private static string ValidateDotNetInstallation(string candidate)
    {
        if (!Path.IsPathRooted(candidate) ||
            !string.Equals(Path.GetFileName(candidate), "dotnet", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must name the direct dotnet muxer.");
        }
        var resolved = Path.GetFullPath(
            new FileInfo(candidate).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? candidate);
        var directoryPath = Path.GetDirectoryName(resolved);
        if (!File.Exists(resolved) ||
            string.IsNullOrEmpty(directoryPath) ||
            !Directory.Exists(Path.Combine(directoryPath, "host", "fxr")))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must be a complete dotnet installation.");
        }
        return resolved;
    }
}
