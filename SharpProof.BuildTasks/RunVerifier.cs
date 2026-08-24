using System.ComponentModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using SharpProof.Host;

namespace SharpProof.BuildTasks;

public sealed partial class RunVerifier : Microsoft.Build.Utilities.Task,
    ICancelableTask, IDisposable
{
    internal const int LauncherProcessReserveMilliseconds = 1000;
    // The supervisor and worker launcher run from this instrumentable
    // assembly. Keep additional bounded room for their final timeout
    // publication and authenticated cleanup when coverage or a heavily loaded
    // host slows managed startup. Direct task callers retain their original
    // deadline semantics.
    private const int WorkerLauncherProcessReserveMilliseconds = 5000;
    private const int CleanupAuthenticationWaitMilliseconds = 5000;
    internal const int MaximumCapturedOutputCharacters = 1_048_576;
    internal const int OutputDrainPollingMilliseconds = 25;
    private const int MaximumProtocolLineCharacters = 160;
    private const int PidFdSendSignalSystemCall = 424;
    private const int PidFdOpenSystemCall = 434;
    private const int SignalTerminate = 15;
    private const int SignalStop = 19;
    private const int SignalKill = 9;
    private const string ProcessGroupLauncher = "/usr/bin/setsid";
    private const string ProcessGateStartMessage = "SharpProof.Start/1";
    private const string SupervisorArmedMessage = "SharpProof.Armed/1";
    private const string SupervisorCleanupMessage = "SharpProof.Cleanup/1";
    private static readonly ConcurrentDictionary<long, CleanupAnchor>
        RetainedCleanupAnchors = new();
    private static long _nextCleanupAnchor;
    private readonly object _synchronization = new();
    private readonly ManualResetEventSlim _cancellationSignal = new();
    private Process? _process;
    private int _processGroupId;
    private int _processGroupPidFd = -1;
    private System.Threading.Tasks.TaskCompletionSource<bool>?
        _supervisorArmedSignal;
    private System.Threading.Tasks.Task<BoundedProcessOutput>?
        _supervisorOutputCompletion;
    private int _terminalCause;
    private bool _canceled;
    private bool _disposed;
    private bool _executionActive;

    internal Func<int, int>? OpenPidFdOverride { get; set; }
    internal Func<Process?, int, int, bool>? TryTerminateOverride { get; set; }
    internal Action<string>? ContainmentAuthenticationFailureOverride { get; set; }
    internal Action? ArmedExecutionOverride { get; set; }

    internal static int RetainedCleanupAnchorCount =>
        RetainedCleanupAnchors.Count;

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

    public int ProjectWallTimeMilliseconds { get; set; } = 300000;

    public int TerminationGraceMilliseconds { get; set; } = 1000;

    [Output]
    public int ExitCode { get; set; }

    [Output]
    public bool HasStructuredError { get; set; }

    internal bool HasActiveProcess
    {
        get
        {
            lock (_synchronization)
            {
                return _process != null && !_process.HasExited;
            }
        }
    }

    public void Dispose()
    {
        Process? process = null;
        ManualResetEventSlim? cancellationSignal = null;
        lock (_synchronization)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _canceled = true;
            TrySetTerminalCause(VerifierTerminalCause.Canceled);
            // Keep the signal alive for any Execute/Cancel caller that already
            // captured it.  Dispose is intentionally deferred until this task's
            // active execution has released the process.
            _cancellationSignal.Set();
            if (!_executionActive)
            {
                process = _process;
                _process = null;
                _processGroupId = 0;
                _processGroupPidFd = -1;
                cancellationSignal = _cancellationSignal;
            }
        }

        process?.Dispose();
        cancellationSignal?.Dispose();
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The MSBuild boundary reports every launch failure as a classified task result.")]
    public override bool Execute()
    {
        Process? process = null;
        var processGroupId = 0;
        System.Threading.Tasks.Task<BoundedProcessOutput>? standardOutput = null;
        System.Threading.Tasks.Task<BoundedProcessOutput>? standardError = null;
        var supervisorArmedSignal =
            new System.Threading.Tasks.TaskCompletionSource<bool>(
                System.Threading.Tasks.TaskCreationOptions
                    .RunContinuationsAsynchronously);
        var supervisorCleanupSignal =
            new System.Threading.Tasks.TaskCompletionSource<bool>(
                System.Threading.Tasks.TaskCreationOptions
                    .RunContinuationsAsynchronously);
        var outputLimitReached = 0;
        Action signalOutputLimit = () =>
            Volatile.Write(ref outputLimitReached, 1);
        Func<bool> isOutputLimitReached = () =>
            Volatile.Read(ref outputLimitReached) != 0;
        var supervisorNonce = string.Empty;
        var retainCleanupAnchor = false;
        HasStructuredError = false;
        ExitCode = 0;
        var containmentFailed = false;
        var processStopwatch = Stopwatch.StartNew();
        lock (_synchronization)
        {
            if (_disposed)
            {
                return false;
            }

            _executionActive = true;
            if (!_canceled)
            {
                _cancellationSignal.Reset();
                Volatile.Write(
                    ref _terminalCause,
                    (int)VerifierTerminalCause.None);
            }
        }
        try
        {
            ContainerContract.ValidateRequired();
            var processTimeout = ComputeProcessTimeout(
                ProjectWallTimeMilliseconds,
                TerminationGraceMilliseconds);
            var workerLauncherBudget = HasWorkerLauncherBudgetArguments();
            if (workerLauncherBudget)
            {
                processTimeout = checked(processTimeout +
                    WorkerLauncherProcessReserveMilliseconds -
                    LauncherProcessReserveMilliseconds);
            }
            // The verifier launcher uses the project timeout plus termination
            // grace as its own deadline. Keep the additional process reserve
            // available for containment and output drain even when startup
            // has already consumed part of the total task budget.
            var cleanupReserve = workerLauncherBudget
                ? WorkerLauncherProcessReserveMilliseconds
                : LauncherProcessReserveMilliseconds;
            var verifierTimeout = processTimeout - cleanupReserve;
            var resolvedExecutable = ResolveDotNetHost(Executable);
            supervisorNonce = CreateSupervisorNonce();
            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ResolveProcessGroupLauncherRequired(),
                    WorkingDirectory = Path.GetFullPath(WorkingDirectory),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                }
            };
            process.StartInfo.ArgumentList.Add(resolvedExecutable);
            process.StartInfo.ArgumentList.Add(
                ResolveSupervisorAssemblyRequired());
            process.StartInfo.ArgumentList.Add("--supervise-verifier");
            process.StartInfo.ArgumentList.Add(resolvedExecutable);
            foreach (var argument in Arguments)
            {
                process.StartInfo.ArgumentList.Add(argument.ItemSpec);
            }
            lock (_synchronization)
            {
                if (_canceled)
                {
                    ExitCode = -1;
                    return true;
                }
                if (!process.Start())
                {
                    throw new InvalidOperationException(
                        "The SharpProof verifier process could not be started.");
                }
                processGroupId = process.Id;
                int processGroupPidFd;
                try
                {
                    processGroupPidFd = OpenPidFdRequired(processGroupId);
                }
                catch
                {
                    TerminateBootstrapProcess(process);
                    throw;
                }
                _process = process;
                _processGroupId = processGroupId;
                _processGroupPidFd = processGroupPidFd;
                _supervisorArmedSignal = supervisorArmedSignal;
                standardOutput = ReadBoundedOutputAsync(
                    process.StandardOutput,
                    supervisorNonce,
                    signalOutputLimit,
                    supervisorArmedSignal,
                    supervisorCleanupSignal,
                    () => TrySetTerminalCause(
                        VerifierTerminalCause.OutputLimit));
                standardError = ReadBoundedOutputAsync(
                    process.StandardError,
                    supervisorNonce: null,
                    signalOutputLimit,
                    outputLimitReached: () => TrySetTerminalCause(
                        VerifierTerminalCause.OutputLimit));
                _supervisorOutputCompletion = standardOutput;
                process.StandardInput.WriteLine(
                    ProcessGateStartMessage + " " + supervisorNonce);
                process.StandardInput.Close();
            }
            var processExited = WaitForExitOrCancellation(
                process,
                isOutputLimitReached,
                Math.Min(
                    verifierTimeout,
                    ForegroundRemainingMilliseconds(
                        processStopwatch,
                        processTimeout,
                        cleanupReserve)));
            if (!processExited)
            {
                var processWasAlive = !process.HasExited;
                var contained = TryTerminate(
                    process,
                    processGroupId,
                    RemainingMilliseconds(
                        processStopwatch,
                        processTimeout));
                retainCleanupAnchor |= processWasAlive;
                if (!contained)
                {
                    containmentFailed = true;
                }
                if (!_cancellationSignal.IsSet &&
                    !isOutputLimitReached())
                {
                    _ = process.WaitForExit(RemainingMilliseconds(
                        processStopwatch,
                        processTimeout));
                }
            }
            var outputCompleted = WaitForOutputCompletion(
                System.Threading.Tasks.Task.WhenAll(
                    standardOutput,
                    standardError),
                RemainingMilliseconds(
                    processStopwatch,
                    processTimeout),
                () => _cancellationSignal.IsSet ||
                    isOutputLimitReached(),
                timeoutReached: () => TrySetTerminalCause(
                    VerifierTerminalCause.Timeout));
            var interrupted = _cancellationSignal.IsSet ||
                isOutputLimitReached();
            if (!outputCompleted)
            {
                var processWasAlive = !process.HasExited;
                var contained = TryTerminate(
                    process,
                    processGroupId,
                    RemainingMilliseconds(
                        processStopwatch,
                        processTimeout));
                retainCleanupAnchor |= processWasAlive;
                containmentFailed |= !contained;
            }
            else
            {
                TrySetTerminalCause(VerifierTerminalCause.Completed);
            }
            var terminalCause = (VerifierTerminalCause)Volatile.Read(
                ref _terminalCause);
            var canceled = terminalCause == VerifierTerminalCause.Canceled;
            var timedOut = terminalCause is
                VerifierTerminalCause.Timeout or
                VerifierTerminalCause.OutputLimit;
            var outputResult = standardOutput.IsCompletedSuccessfully
                ? standardOutput.Result
                : null;
            var errorResult = standardError.IsCompletedSuccessfully
                ? standardError.Result
                : null;
            var output = outputResult?.Text ?? string.Empty;
            var error = errorResult?.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(output))
            {
                Log.LogMessage(MessageImportance.High, "{0}", output);
            }
            if (!string.IsNullOrWhiteSpace(error))
            {
                LogStandardError(error);
            }
            if (isOutputLimitReached() ||
                outputResult?.LimitExceeded == true ||
                errorResult?.LimitExceeded == true)
            {
                Log.LogError(
                    "SharpProof verifier output exceeded the bounded " +
                    "diagnostic capture limit.");
            }
            var supervisorArmed = outputResult?.SupervisorArmed == true ||
                supervisorArmedSignal.Task.IsCompletedSuccessfully;
            var authenticationRequired = supervisorArmed ||
                process.HasExited && process.ExitCode != 125;
            var deferAuthentication =
                canceled ||
                ShouldDeferSupervisorAuthentication(
                    authenticationRequired,
                    interrupted,
                    outputCompleted);
            if (deferAuthentication)
            {
                retainCleanupAnchor = true;
            }
            else if (!RequireSupervisorCleanupReceipt(
                    outputResult?.CleanupAuthenticated == true,
                    authenticationRequired))
            {
                containmentFailed = true;
            }
            ExitCode = containmentFailed
                ? -1
                : canceled
                    ? 143
                    : timedOut
                        ? 124
                        : process.ExitCode;
        }
        catch (Exception exception)
        {
            TrySetTerminalCause(VerifierTerminalCause.Faulted);
            var contained = TryTerminate(
                process,
                processGroupId,
                LauncherProcessReserveMilliseconds);
            retainCleanupAnchor |= ShouldRetainCleanupAfterFailure(
                processGroupId > 0,
                supervisorArmedSignal.Task.IsCompletedSuccessfully,
                contained);
            ExitCode = -1;
            Log.LogMessage(
                MessageImportance.High,
                "SharpProof verifier launch failed: {0}",
                exception.Message);
        }
        finally
        {
            var processGroupPidFd = -1;
            ManualResetEventSlim? cancellationSignal = null;
            lock (_synchronization)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _processGroupId = 0;
                    processGroupPidFd = _processGroupPidFd;
                    _processGroupPidFd = -1;
                    _supervisorArmedSignal = null;
                    _supervisorOutputCompletion = null;
                }
                _executionActive = false;
                if (_disposed)
                {
                    cancellationSignal = _cancellationSignal;
                }
            }
            if (processGroupPidFd >= 0)
            {
                if (retainCleanupAnchor && process != null)
                {
                    Action<string>? authenticationFailure =
                        (VerifierTerminalCause)Volatile.Read(
                            ref _terminalCause) ==
                            VerifierTerminalCause.Canceled
                            ? null
                            : HandleContainmentAuthenticationFailure;
                    RetainCleanupAnchor(
                        process,
                        processGroupPidFd,
                        standardOutput,
                        standardError,
                        supervisorNonce,
                        supervisorCleanupSignal.Task,
                        authenticationFailure);
                    process = null;
                }
                else
                {
                    _ = NativeMethods.Close(processGroupPidFd);
                }
            }
            process?.Dispose();
            cancellationSignal?.Dispose();
        }
        return true;
    }

    internal static string CreateSupervisorNonce()
    {
        return Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32)).ToUpperInvariant();
    }

    internal static bool WaitForOutputCompletion(
        System.Threading.Tasks.Task outputCompletion,
        int timeoutMilliseconds,
        Func<bool> isInterrupted,
        Func<int, bool>? waitOverride = null,
        Action? timeoutReached = null)
    {
        ArgumentNullException.ThrowIfNull(outputCompletion);
        ArgumentNullException.ThrowIfNull(isInterrupted);
        if (timeoutMilliseconds <= 0)
        {
            var completedAtDeadline = outputCompletion.IsCompleted;
            if (!completedAtDeadline)
            {
                timeoutReached?.Invoke();
            }
            return completedAtDeadline;
        }

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (outputCompletion.IsCompleted)
            {
                return true;
            }
            if (isInterrupted())
            {
                return false;
            }

            var remaining = RemainingMilliseconds(
                stopwatch,
                timeoutMilliseconds);
            if (remaining <= 0)
            {
                var completedAtDeadline = outputCompletion.IsCompleted;
                if (!completedAtDeadline)
                {
                    timeoutReached?.Invoke();
                }
                return completedAtDeadline;
            }

            var slice = Math.Min(
                OutputDrainPollingMilliseconds,
                remaining);
            var completed = waitOverride == null
                ? outputCompletion.Wait(slice)
                : waitOverride(slice);
            if (completed)
            {
                return true;
            }
        }
    }

    internal static SupervisorReadiness WaitForSupervisorReadiness(
        System.Threading.Tasks.Task armed,
        System.Threading.Tasks.Task outputCompletion,
        Func<bool> hasExited,
        int timeoutMilliseconds,
        Func<int, bool>? waitOverride = null)
    {
        ArgumentNullException.ThrowIfNull(armed);
        ArgumentNullException.ThrowIfNull(outputCompletion);
        ArgumentNullException.ThrowIfNull(hasExited);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (armed.IsCompletedSuccessfully)
            {
                return SupervisorReadiness.Armed;
            }
            if (hasExited() && outputCompletion.IsCompletedSuccessfully)
            {
                return armed.IsCompletedSuccessfully
                    ? SupervisorReadiness.Armed
                    : SupervisorReadiness.ExitedBeforeArmed;
            }

            var remaining = RemainingMilliseconds(
                stopwatch,
                timeoutMilliseconds);
            if (remaining <= 0)
            {
                return SupervisorReadiness.NotReady;
            }

            var slice = Math.Min(OutputDrainPollingMilliseconds, remaining);
            _ = waitOverride == null
                ? armed.Wait(slice)
                : waitOverride(slice);
        }
    }

    internal static bool HasSupervisorProtocolRecord(
        string output,
        string message,
        string nonce)
    {
        var expected = message + " " + nonce;
        return output.Split('\n').Any(line =>
            string.Equals(
                line.EndsWith('\r') ? line[..^1] : line,
                expected,
                StringComparison.Ordinal));
    }

    internal static bool ShouldDeferSupervisorAuthentication(
        bool authenticationRequired,
        bool interrupted,
        bool outputCompleted)
    {
        _ = interrupted;
        return authenticationRequired && !outputCompleted;
    }

    internal static bool ShouldRetainCleanupAfterFailure(
        bool processStarted,
        bool supervisorArmed,
        bool containmentSucceeded)
    {
        return processStarted &&
            (supervisorArmed || !containmentSucceeded);
    }

    internal static bool SupervisorExitCompletesTermination(
        SupervisorReadiness readiness,
        int exitCode)
    {
        return readiness == SupervisorReadiness.Armed ||
            readiness == SupervisorReadiness.ExitedBeforeArmed &&
            exitCode == 125;
    }

    internal static async System.Threading.Tasks.Task<BoundedProcessOutput>
        ReadBoundedOutputAsync(
            TextReader reader,
            string? supervisorNonce,
            Action signalOutputLimit,
            System.Threading.Tasks.TaskCompletionSource<bool>?
                supervisorArmedSignal = null,
            System.Threading.Tasks.TaskCompletionSource<bool>?
                supervisorCleanupSignal = null,
            Action? outputLimitReached = null)
    {
        ArgumentNullException.ThrowIfNull(signalOutputLimit);
        var captured = new StringBuilder();
        var protocolLine = new StringBuilder();
        var protocolLineTooLong = false;
        var limitExceeded = false;
        var supervisorArmed = false;
        var cleanupAuthenticated = false;
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(
                buffer,
                0,
                buffer.Length).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }
            var remaining = MaximumCapturedOutputCharacters -
                captured.Length;
            if (remaining > 0)
            {
                captured.Append(buffer, 0, Math.Min(remaining, count));
            }
            if (count > remaining)
            {
                limitExceeded = true;
                outputLimitReached?.Invoke();
                signalOutputLimit();
            }
            if (supervisorNonce == null)
            {
                continue;
            }
            for (var index = 0; index < count; index++)
            {
                var character = buffer[index];
                if (character == '\n')
                {
                    if (!protocolLineTooLong)
                    {
                        var line = protocolLine.ToString();
                        if (line.EndsWith('\r'))
                        {
                            line = line[..^1];
                        }
                        var armedRecord = string.Equals(
                            line,
                            SupervisorArmedMessage + " " + supervisorNonce,
                            StringComparison.Ordinal);
                        supervisorArmed |= armedRecord;
                        if (armedRecord)
                        {
                            supervisorArmedSignal?.TrySetResult(true);
                        }
                        var cleanupRecord = string.Equals(
                            line,
                            SupervisorCleanupMessage + " " + supervisorNonce,
                            StringComparison.Ordinal);
                        cleanupAuthenticated |= cleanupRecord;
                        if (cleanupRecord)
                        {
                            supervisorCleanupSignal?.TrySetResult(true);
                        }
                    }
                    protocolLine.Clear();
                    protocolLineTooLong = false;
                    continue;
                }
                if (!protocolLineTooLong)
                {
                    if (protocolLine.Length < MaximumProtocolLineCharacters)
                    {
                        protocolLine.Append(character);
                    }
                    else
                    {
                        protocolLine.Clear();
                        protocolLineTooLong = true;
                    }
                }
            }
        }
        return new BoundedProcessOutput(
            captured.ToString(),
            limitExceeded,
            supervisorArmed,
            cleanupAuthenticated);
    }

    internal bool RequireSupervisorCleanupReceipt(
        bool cleanupAuthenticated,
        bool authenticationRequired)
    {
        if (!authenticationRequired || cleanupAuthenticated)
        {
            return true;
        }
        HandleContainmentAuthenticationFailure(
            "The SharpProof verifier containment supervisor exited " +
            "without an authenticated cleanup receipt.");
        return false;
    }

    private void HandleContainmentAuthenticationFailure(string message)
    {
        if (ContainmentAuthenticationFailureOverride is { } handler)
        {
            handler(message);
            return;
        }
        Environment.FailFast(message);
    }

    internal static void RetainCleanupAnchorForTest(Process process)
    {
        RetainCleanupAnchor(process, -1, null, null, null, null, null);
    }

    internal static void RetainCleanupAnchorForTest(
        Process process,
        System.Threading.Tasks.Task<string>? standardOutput,
        string? supervisorNonce,
        Action<string>? authenticationFailure)
    {
        var boundedOutput = standardOutput == null
            ? null
            : ConvertTestOutputAsync(standardOutput, supervisorNonce);
        RetainCleanupAnchor(
            process,
            -1,
            boundedOutput,
            null,
            supervisorNonce,
            null,
            authenticationFailure);
    }

    private static async System.Threading.Tasks.Task<BoundedProcessOutput>
        ConvertTestOutputAsync(
            System.Threading.Tasks.Task<string> output,
            string? supervisorNonce)
    {
        var text = await output.ConfigureAwait(false);
        return new BoundedProcessOutput(
            text,
            LimitExceeded: false,
            SupervisorArmed: supervisorNonce != null &&
                HasSupervisorProtocolRecord(
                    text,
                    SupervisorArmedMessage,
                    supervisorNonce),
            CleanupAuthenticated: supervisorNonce != null &&
                HasSupervisorProtocolRecord(
                    text,
                    SupervisorCleanupMessage,
                    supervisorNonce));
    }

    private static void RetainCleanupAnchor(
        Process process,
        int processGroupPidFd,
        System.Threading.Tasks.Task<BoundedProcessOutput>? standardOutput,
        System.Threading.Tasks.Task<BoundedProcessOutput>? standardError,
        string? supervisorNonce = null,
        System.Threading.Tasks.Task? supervisorCleanupSignal = null,
        Action<string>? authenticationFailure = null)
    {
        var token = Interlocked.Increment(ref _nextCleanupAnchor);
        var anchor = new CleanupAnchor(
            process,
            processGroupPidFd,
            standardOutput,
            standardError,
            supervisorNonce,
            supervisorCleanupSignal,
            authenticationFailure);
        if (!RetainedCleanupAnchors.TryAdd(token, anchor))
        {
            throw new InvalidOperationException(
                "SharpProof could not retain its cleanup anchor.");
        }
        ObserveFault(anchor.StandardOutput);
        ObserveFault(anchor.StandardError);
        _ = ObserveCleanupAnchorAsync(token, anchor);
    }

    private static async System.Threading.Tasks.Task
        ObserveCleanupAnchorAsync(long token, CleanupAnchor anchor)
    {
        try
        {
            await anchor.Process.WaitForExitAsync().ConfigureAwait(false);
            if (anchor.SupervisorNonce != null &&
                anchor.AuthenticationFailure != null)
            {
                var authenticated = anchor.StandardOutput != null &&
                    await AwaitCleanupAuthenticationAfterSupervisorExit(
                        anchor.StandardOutput,
                        anchor.SupervisorCleanupSignal).ConfigureAwait(false);
                if (!authenticated)
                {
                    anchor.AuthenticationFailure(
                        "The retained SharpProof verifier containment " +
                        "supervisor exited without an authenticated " +
                        "cleanup receipt.");
                }
            }
        }
        catch (InvalidOperationException) { }
        finally
        {
            if (anchor.ProcessGroupPidFd >= 0)
            {
                _ = NativeMethods.Close(anchor.ProcessGroupPidFd);
            }
            anchor.Process.Dispose();
            _ = RetainedCleanupAnchors.TryRemove(token, out _);
        }
    }

    private static async System.Threading.Tasks.Task<bool>
        AwaitCleanupAuthenticationAfterSupervisorExit(
            System.Threading.Tasks.Task<BoundedProcessOutput> output,
            System.Threading.Tasks.Task? supervisorCleanupSignal)
    {
        var delay = System.Threading.Tasks.Task.Delay(
            CleanupAuthenticationWaitMilliseconds);
        var completed = supervisorCleanupSignal == null
            ? await System.Threading.Tasks.Task.WhenAny(output, delay)
                .ConfigureAwait(false)
            : await System.Threading.Tasks.Task.WhenAny(
                output,
                supervisorCleanupSignal,
                delay).ConfigureAwait(false);
        if (supervisorCleanupSignal != null &&
            ReferenceEquals(completed, supervisorCleanupSignal))
        {
            return true;
        }
        if (!ReferenceEquals(completed, output) ||
            !output.IsCompletedSuccessfully)
        {
            return false;
        }
        var outputResult = await output.ConfigureAwait(false);
        return outputResult.SupervisorArmed &&
            outputResult.CleanupAuthenticated;
    }

    private static void ObserveFault(
        System.Threading.Tasks.Task<BoundedProcessOutput>? task)
    {
        if (task == null)
        {
            return;
        }
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted |
                TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed record CleanupAnchor(
        Process Process,
        int ProcessGroupPidFd,
        System.Threading.Tasks.Task<BoundedProcessOutput>? StandardOutput,
        System.Threading.Tasks.Task<BoundedProcessOutput>? StandardError,
        string? SupervisorNonce,
        System.Threading.Tasks.Task? SupervisorCleanupSignal,
        Action<string>? AuthenticationFailure);

    internal sealed record BoundedProcessOutput(
        string Text,
        bool LimitExceeded,
        bool SupervisorArmed,
        bool CleanupAuthenticated);

    internal static int ComputeProcessTimeout(
        int projectWallTimeMilliseconds,
        int terminationGraceMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            projectWallTimeMilliseconds,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            terminationGraceMilliseconds,
            1);
        return checked(
            projectWallTimeMilliseconds +
            terminationGraceMilliseconds +
            LauncherProcessReserveMilliseconds);
    }

    internal static int ComputeForegroundTimeout(
        int processTimeoutMilliseconds,
        int cleanupReserveMilliseconds,
        int elapsedMilliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            processTimeoutMilliseconds,
            1);
        ArgumentOutOfRangeException.ThrowIfLessThan(
            cleanupReserveMilliseconds,
            1);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedMilliseconds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            cleanupReserveMilliseconds,
            processTimeoutMilliseconds);

        var remaining = processTimeoutMilliseconds - elapsedMilliseconds;
        var foregroundBudget = processTimeoutMilliseconds -
            cleanupReserveMilliseconds;
        return remaining <= cleanupReserveMilliseconds
            ? 0
            : Math.Min(foregroundBudget, remaining -
                cleanupReserveMilliseconds);
    }

    private static int ForegroundRemainingMilliseconds(
        Stopwatch stopwatch,
        int processTimeoutMilliseconds,
        int cleanupReserveMilliseconds)
    {
        var elapsed = stopwatch.ElapsedMilliseconds >= int.MaxValue
            ? int.MaxValue
            : (int)stopwatch.ElapsedMilliseconds;
        return ComputeForegroundTimeout(
            processTimeoutMilliseconds,
            cleanupReserveMilliseconds,
            elapsed);
    }

    private static int RemainingMilliseconds(
        Stopwatch stopwatch,
        int timeoutMilliseconds)
    {
        var remaining = timeoutMilliseconds - stopwatch.ElapsedMilliseconds;
        return remaining <= 0
            ? 0
            : (int)Math.Min(remaining, int.MaxValue);
    }

    private bool WaitForExitOrCancellation(
        Process process,
        Func<bool> isOutputLimitReached,
        int timeoutMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(isOutputLimitReached);
        var stopwatch = Stopwatch.StartNew();
        while (!_cancellationSignal.IsSet && !isOutputLimitReached())
        {
            if (_supervisorArmedSignal?.Task.IsCompletedSuccessfully == true &&
                ArmedExecutionOverride is { } armedExecutionOverride)
            {
                ArmedExecutionOverride = null;
                armedExecutionOverride();
            }
            var remaining = RemainingMilliseconds(
                stopwatch,
                timeoutMilliseconds);
            if (remaining == 0)
            {
                if (process.HasExited)
                {
                    return true;
                }
                TrySetTerminalCause(VerifierTerminalCause.Timeout);
                return false;
            }
            if (process.WaitForExit(Math.Min(remaining, 25)))
            {
                return true;
            }
        }
        return process.HasExited;
    }

    private bool TrySetTerminalCause(VerifierTerminalCause cause)
    {
        return Interlocked.CompareExchange(
            ref _terminalCause,
            (int)cause,
            (int)VerifierTerminalCause.None) ==
            (int)VerifierTerminalCause.None;
    }

    private static string ResolveProcessGroupLauncherRequired()
    {
        if (!File.Exists(ProcessGroupLauncher))
        {
            throw new InvalidOperationException(
                "SharpProof could not establish the verifier process boundary.");
        }
        return LinuxPathIdentity.Canonicalize(ProcessGroupLauncher);
    }

    private bool HasWorkerLauncherBudgetArguments()
    {
        return Arguments.Any(static argument =>
            string.Equals(
                argument.ItemSpec,
                "--project-wall-ms",
                StringComparison.Ordinal));
    }

    private static string ResolveSupervisorAssemblyRequired()
    {
        var assembly = typeof(RunVerifier).Assembly.Location;
        if (!File.Exists(assembly) ||
            !File.Exists(Path.ChangeExtension(
                assembly,
                ".runtimeconfig.json")))
        {
            throw new InvalidOperationException(
                "SharpProof could not establish the verifier supervisor.");
        }
        return LinuxPathIdentity.Canonicalize(assembly);
    }

    private static void TerminateBootstrapProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            _ = process.WaitForExit(1000);
        }
        catch (InvalidOperationException) { }
        catch (Win32Exception) { }
    }

    private bool TryTerminate(
        Process? process,
        int processGroupId,
        int terminationWaitMilliseconds)
    {
        if (TryTerminateOverride is { } terminateOverride)
        {
            return terminateOverride(
                process,
                processGroupId,
                terminationWaitMilliseconds);
        }
        if (process == null)
        {
            return true;
        }
        lock (_synchronization)
        {
            if (!ReferenceEquals(_process, process) ||
                _processGroupId != processGroupId ||
                _processGroupPidFd < 0)
            {
                return true;
            }

            var terminationStopwatch = Stopwatch.StartNew();
            if (_supervisorArmedSignal == null ||
                _supervisorOutputCompletion == null)
            {
                HandleContainmentAuthenticationFailure(
                    "SharpProof verifier supervisor readiness was not " +
                    "published before termination.");
                return false;
            }
            var readiness = WaitForSupervisorReadiness(
                _supervisorArmedSignal.Task,
                _supervisorOutputCompletion,
                () => process.HasExited,
                Math.Min(
                    terminationWaitMilliseconds,
                    LauncherProcessReserveMilliseconds));
            if (readiness == SupervisorReadiness.ExitedBeforeArmed)
            {
                return SupervisorExitCompletesTermination(
                    readiness,
                    process.ExitCode);
            }
            if (readiness != SupervisorReadiness.Armed)
            {
                HandleContainmentAuthenticationFailure(
                    "SharpProof verifier supervisor readiness could not be " +
                    "authenticated before termination.");
                return false;
            }

            var terminateSent = SendPidFdSignal(
                _processGroupPidFd,
                SignalTerminate) == 0;
            var boundedWait = Math.Min(
                RemainingMilliseconds(
                    terminationStopwatch,
                    terminationWaitMilliseconds),
                LauncherProcessReserveMilliseconds);
            if (terminateSent && boundedWait > 0 &&
                process.WaitForExit(boundedWait))
            {
                return SupervisorExitCompletesTermination(
                    readiness,
                    process.ExitCode);
            }
            if (terminateSent && !process.HasExited)
            {
                // The supervisor remains the subreaper while it retries its
                // individually bounded cleanup batches. The caller retains
                // the live supervisor as a cleanup anchor; killing it here
                // would reparent session-escaping descendants beyond the
                // containment boundary.
                return true;
            }
            var cleanup = VerifierProcessSupervisor.StopDescendants(
                processGroupId,
                Math.Min(RemainingMilliseconds(
                    terminationStopwatch,
                    terminationWaitMilliseconds),
                    LauncherProcessReserveMilliseconds));
            if (SendPidFdSignal(_processGroupPidFd, SignalStop) == 0)
            {
                // The stopped session leader keeps this process-group identity
                // live while the group-directed signal is delivered.
                _ = NativeMethods.Kill(-processGroupId, SignalKill);
                _ = SendPidFdSignal(_processGroupPidFd, SignalKill);
            }
            else if (Marshal.GetLastPInvokeError() != 3)
            {
                _ = SendPidFdSignal(_processGroupPidFd, SignalKill);
            }
            return cleanup.Complete;
        }
    }

    private int OpenPidFdRequired(int processId)
    {
        if (!OperatingSystem.IsLinux() ||
            RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            throw new PlatformNotSupportedException(
                "SharpProof verifier containment requires Linux amd64.");
        }
        var descriptor = OpenPidFdOverride?.Invoke(processId) ??
            checked((int)NativeMethods.SystemCall2(
                PidFdOpenSystemCall,
                processId,
                0));
        if (descriptor < 0)
        {
            throw new InvalidOperationException(
                "SharpProof could not pin the verifier process boundary " +
                $"(errno {Marshal.GetLastPInvokeError()}).");
        }
        return descriptor;
    }

    private static int SendPidFdSignal(int descriptor, int signal)
    {
        return checked((int)NativeMethods.SystemCall4(
            PidFdSendSignalSystemCall,
            descriptor,
            signal,
            0,
            0));
    }

    internal void LogStandardError(string standardError)
    {
        using var reader = new StringReader(standardError);
        while (reader.ReadLine() is { } line)
        {
            VerifierDiagnostic diagnostic;
            if (VerifierDiagnosticTransport.TryDeserialize(
                    line,
                    out var structured))
            {
                diagnostic = structured;
            }
            else if (!TryParseLegacyDiagnostic(
                         line,
                         out diagnostic))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Log.LogMessage(MessageImportance.High, "{0}", line);
                }
                continue;
            }

            if (diagnostic.Severity == "error")
            {
                HasStructuredError = true;
                Log.LogError(
                    string.Empty,
                    diagnostic.Code,
                    string.Empty,
                    diagnostic.File,
                    diagnostic.Line,
                    diagnostic.Column,
                    0,
                    0,
                    diagnostic.Message);
            }
            else
            {
                Log.LogWarning(
                    string.Empty,
                    diagnostic.Code,
                    string.Empty,
                    diagnostic.File,
                    diagnostic.Line,
                    diagnostic.Column,
                    0,
                    0,
                    diagnostic.Message);
            }
        }
    }

    private static bool TryParseLegacyDiagnostic(
        string line,
        out VerifierDiagnostic diagnostic)
    {
        (string Severity, string Code, string Marker)[] markers =
        {
            ("warning", "SP0047", ": warning SP0047: "),
            ("warning", "SP0048", ": warning SP0048: "),
            ("error", "SP0047", ": error SP0047: "),
            ("error", "SP0048", ": error SP0048: ")
        };
        diagnostic = null!;
        var selectedIndex = -1;
        foreach (var candidate in markers)
        {
            var marker = line.LastIndexOf(
                candidate.Marker,
                StringComparison.Ordinal);
            while (marker > 0)
            {
                var location = line.Substring(0, marker);
                if (marker > selectedIndex &&
                    TryParseLocation(
                        location,
                        out var file,
                        out var lineNumber,
                        out var columnNumber))
                {
                    selectedIndex = marker;
                    diagnostic = new VerifierDiagnostic(
                        candidate.Severity,
                        candidate.Code,
                        file,
                        lineNumber,
                        columnNumber,
                        line.Substring(marker + candidate.Marker.Length));
                    break;
                }
                marker = line.LastIndexOf(
                    candidate.Marker,
                    marker - 1,
                    StringComparison.Ordinal);
            }
        }
        return selectedIndex >= 0;
    }

    private static bool TryParseLocation(
        string location,
        out string file,
        out int lineNumber,
        out int columnNumber)
    {
        file = string.Empty;
        lineNumber = 0;
        columnNumber = 0;
        if (string.Equals(location, "SharpProof", StringComparison.Ordinal))
        {
            return true;
        }

        if (!location.EndsWith(')'))
        {
            return false;
        }
        var openParenthesis = location.LastIndexOf('(');
        var comma = location.LastIndexOf(',');
        if (openParenthesis <= 0 || comma <= openParenthesis ||
            !int.TryParse(
                location.AsSpan(
                    openParenthesis + 1,
                    comma - openParenthesis - 1),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out lineNumber) ||
            !int.TryParse(
                location.AsSpan(
                    comma + 1,
                    location.Length - comma - 2),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out columnNumber))
        {
            lineNumber = 0;
            columnNumber = 0;
            return false;
        }

        file = location.Substring(0, openParenthesis);
        return true;
    }

    internal static string ResolveDotNetHost(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must name the direct dotnet muxer.");
        }

        var disclosedHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        var currentProcess = Environment.ProcessPath;
        var currentMuxer = !string.IsNullOrWhiteSpace(currentProcess) &&
            Path.IsPathRooted(currentProcess) &&
            string.Equals(
                Path.GetFileName(currentProcess),
                "dotnet",
                StringComparison.Ordinal)
            ? ValidateDotNetInstallation(currentProcess)
            : null;
        var disclosedMuxer = !string.IsNullOrWhiteSpace(disclosedHost)
            ? ValidateDotNetInstallation(disclosedHost)
            : null;
        if (currentMuxer != null &&
            disclosedMuxer != null &&
            !LinuxPathIdentity.AreSameExistingFile(currentMuxer, disclosedMuxer))
        {
            throw new InvalidOperationException(
                "DOTNET_HOST_PATH must match the current dotnet muxer.");
        }
        var trusted = currentMuxer ?? disclosedMuxer ??
            ValidateDotNetInstallation(ResolveDotNetFromPath());
        if (string.Equals(
                executable,
                "dotnet",
                StringComparison.Ordinal))
        {
            return trusted;
        }
        if (!Path.IsPathRooted(executable) ||
            !string.Equals(
                Path.GetFileName(executable),
                "dotnet",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must name the direct dotnet muxer.");
        }
        var configured = ValidateDotNetInstallation(executable);
        if (!LinuxPathIdentity.AreSameExistingFile(configured, trusted))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must match the trusted current dotnet muxer.");
        }
        return configured;
    }

    public void Cancel()
    {
        Process? process;
        int processGroupId;
        lock (_synchronization)
        {
            if (_disposed)
            {
                return;
            }
            _canceled = true;
            TrySetTerminalCause(VerifierTerminalCause.Canceled);
            _cancellationSignal.Set();
            process = _process;
            processGroupId = _processGroupId;
        }
        if (process == null)
        {
            return;
        }
        _ = TryTerminate(
            process,
            processGroupId,
            LauncherProcessReserveMilliseconds);
    }

    internal enum SupervisorReadiness
    {
        Armed,
        ExitedBeforeArmed,
        NotReady
    }

    private enum VerifierTerminalCause
    {
        None,
        Completed,
        Canceled,
        OutputLimit,
        Timeout,
        Faulted
    }

    private static string ResolveDotNetFromPath()
    {
        foreach (var value in (Environment.GetEnvironmentVariable("PATH") ??
                     string.Empty).Split(
                     [Path.PathSeparator],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            // PATH is already a parsed environment value. Treat each field as
            // an opaque directory name; trimming would silently redirect legal
            // installations whose names end in whitespace or quotes.
            var directory = value;
            if (string.IsNullOrWhiteSpace(directory) ||
                directory == "." ||
                !Path.IsPathRooted(directory))
            {
                continue;
            }
            var candidate = Path.Combine(directory, "dotnet");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new InvalidOperationException(
            "SharpProof could not resolve a trusted dotnet muxer from PATH.");
    }

    private static string ValidateDotNetInstallation(string candidate)
    {
        if (!Path.IsPathRooted(candidate) ||
            !string.Equals(
                Path.GetFileName(candidate),
                "dotnet",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must name the direct dotnet muxer.");
        }
        var resolved = LinuxPathIdentity.Canonicalize(candidate);
        var directoryPath = Path.GetDirectoryName(resolved);
        if (!File.Exists(resolved) ||
            string.IsNullOrEmpty(directoryPath) ||
            !Directory.Exists(Path.Combine(directoryPath, "host", "fxr")))
        {
            throw new InvalidOperationException(
                "SharpProof verifier host must be a complete dotnet installation.");
        }
        LinuxPathIdentity.Canonicalize(
            Path.Combine(directoryPath, "host", "fxr"));
        return resolved;
    }

    private static partial class NativeMethods
    {
        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Close(int descriptor);

        [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial int Kill(int processId, int signal);

        [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial nint SystemCall2(
            nint number,
            int argument1,
            uint argument2);

        [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        internal static partial nint SystemCall4(
            nint number,
            int argument1,
            int argument2,
            nint argument3,
            uint argument4);
    }

}
