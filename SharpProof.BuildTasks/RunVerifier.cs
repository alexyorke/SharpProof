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
    private const int StructuredSemanticFailureExitCode = 6;
    // The worker launcher can legitimately spend the full publication lease
    // timeout after its worker budget expires. Keep that wait outside the
    // worker budget, followed by the existing bounded room for finalization
    // and authenticated cleanup. Direct task callers retain their original
    // deadline semantics.
    private const int WorkerLauncherPublicationWaitMilliseconds = 30000;
    private const int WorkerLauncherFinalizationReserveMilliseconds = 5000;
    private const int WorkerLauncherProcessReserveMilliseconds =
        WorkerLauncherPublicationWaitMilliseconds +
        WorkerLauncherFinalizationReserveMilliseconds;
    private const int CleanupAuthenticationWaitMilliseconds = 5000;
    internal const int MaximumCapturedOutputCharacters = 1_048_576;
    internal const int OutputDrainPollingMilliseconds = 25;
    private const int MaximumProtocolLineCharacters = 160;
    private const string ProcessGroupLauncher = "/usr/bin/setsid";
    private static readonly ConcurrentDictionary<long, CleanupAnchor>
        RetainedCleanupAnchors = new();
    private static readonly (string Severity, string Code, string Marker)[]
        LegacyDiagnosticMarkers =
        [
            ("warning", VerifierDiagnosticCodes.IncompleteSelectedCallable,
                $": warning {VerifierDiagnosticCodes.IncompleteSelectedCallable}: "),
            ("warning", VerifierDiagnosticCodes.AssumptionsDeclared,
                $": warning {VerifierDiagnosticCodes.AssumptionsDeclared}: "),
            ("error", VerifierDiagnosticCodes.IncompleteSelectedCallable,
                $": error {VerifierDiagnosticCodes.IncompleteSelectedCallable}: "),
            ("error", VerifierDiagnosticCodes.AssumptionsDeclared,
                $": error {VerifierDiagnosticCodes.AssumptionsDeclared}: ")
        ];
    private static long s_nextCleanupAnchor;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _cancellationSignal = new();
    private readonly ManualResetEventSlim _outputLimitSignal = new();
    private Process? _process;
    private int _processGroupId;
    private int _processGroupPidFd = -1;
    private System.Threading.Tasks.TaskCompletionSource<bool>?
        _supervisorArmedSignal;
    private System.Threading.Tasks.Task<BoundedProcessOutput>?
        _supervisorOutputCompletion;

    internal Func<int, int>? OpenPidFdOverride { get; set; }
    internal Func<Process?, int, int, bool>? TryTerminateOverride { get; set; }
    internal Action<string>? ContainmentAuthenticationFailureOverride { get; set; }
    internal Action? PreLaunchSetupOverride { get; set; }

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
            lock (_gate)
            {
                return _process != null && !_process.HasExited;
            }
        }
    }

    public void Dispose()
    {
        _cancellationSignal.Dispose();
        _outputLimitSignal.Dispose();
        _process?.Dispose();
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
        var supervisorNonce = string.Empty;
        var retainCleanupAnchor = false;
        HasStructuredError = false;
        ExitCode = 0;
        var containmentFailed = false;
        _outputLimitSignal.Reset();
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
            // grace as its own final deadline. Keep the full process deadline
            // for that invocation so the reserve remains available for
            // containment and output drain. Direct task callers do not have
            // that inner deadline and retain the task's original timeout.
            var verifierTimeout = workerLauncherBudget
                ? processTimeout
                : processTimeout - LauncherProcessReserveMilliseconds;
            PreLaunchSetupOverride?.Invoke();
            var resolvedExecutable = ResolveDotNetHost(Executable);
            var executableIdentity = GetFileIdentity(resolvedExecutable);
            var supervisorAssembly = ResolveSupervisorAssemblyRequired();
            var supervisorIdentity = GetFileIdentity(supervisorAssembly);
            if (GetFileIdentity(resolvedExecutable) != executableIdentity ||
                GetFileIdentity(supervisorAssembly) != supervisorIdentity)
            {
                throw new InvalidOperationException(
                    "SharpProof verifier runtime changed after validation.");
            }
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
                supervisorAssembly);
            process.StartInfo.ArgumentList.Add(Program.SupervisorArgument);
            process.StartInfo.ArgumentList.Add(resolvedExecutable);
            foreach (var argument in Arguments)
            {
                process.StartInfo.ArgumentList.Add(argument.ItemSpec);
            }
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
                    _outputLimitSignal,
                    supervisorArmedSignal,
                    supervisorCleanupSignal);
                standardError = ReadBoundedOutputAsync(
                    process.StandardError,
                    supervisorNonce: null,
                    _outputLimitSignal);
                _supervisorOutputCompletion = standardOutput;
                process.StandardInput.WriteLine(
                    LinuxWorkerProcess.StartMessage + " " + supervisorNonce);
                process.StandardInput.Close();
            }
            var processStopwatch = Stopwatch.StartNew();
            var timedOut = !WaitForExitOrCancellation(
                process,
                Math.Min(
                    verifierTimeout,
                    RemainingMilliseconds(
                        processStopwatch,
                        processTimeout)));
            var canceled = _cancellationSignal.IsSet;
            if (timedOut)
            {
                TerminateAfterTimeout(
                    process,
                    processGroupId,
                    processStopwatch,
                    processTimeout,
                    ref retainCleanupAnchor,
                    ref containmentFailed);
                canceled = _cancellationSignal.IsSet;
                if (!canceled && !_outputLimitSignal.IsSet)
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
                    _outputLimitSignal.IsSet);
            var interrupted = _cancellationSignal.IsSet ||
                _outputLimitSignal.IsSet;
            if (!outputCompleted)
            {
                timedOut = true;
                TerminateAfterTimeout(
                    process,
                    processGroupId,
                    processStopwatch,
                    processTimeout,
                    ref retainCleanupAnchor,
                    ref containmentFailed);
            }
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
            if (_outputLimitSignal.IsSet ||
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
                : timedOut
                    ? 124
                    : process.ExitCode;
        }
        catch (Exception exception)
        {
            var contained = TryTerminate(
                process,
                processGroupId,
                LauncherProcessReserveMilliseconds);
            retainCleanupAnchor = !contained &&
                process is { HasExited: false };
            ExitCode = -1;
            Log.LogMessage(
                MessageImportance.High,
                "SharpProof verifier launch failed: {0}",
                exception.Message);
        }
        finally
        {
            var processGroupPidFd = -1;
            lock (_gate)
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
            }
            if (processGroupPidFd >= 0)
            {
                if (retainCleanupAnchor && process != null)
                {
                    Action<string>? authenticationFailure =
                        _cancellationSignal.IsSet
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
                    _ = LinuxNativeMethods.Close(processGroupPidFd);
                }
            }
            process?.Dispose();
        }
        // The launcher reserves exit 6 for a completed semantic policy
        // failure. A diagnostic observed before any other nonzero exit is
        // partial and must not suppress the target's infrastructure error.
        HasStructuredError &=
            ExitCode == StructuredSemanticFailureExitCode;
        return true;
    }

    internal static string CreateSupervisorNonce()
    {
        return Convert.ToHexString(
            RandomNumberGenerator.GetBytes(32));
    }

    internal static bool WaitForOutputCompletion(
        System.Threading.Tasks.Task outputCompletion,
        int timeoutMilliseconds,
        Func<bool> isInterrupted,
        Func<int, bool>? waitOverride = null)
    {
        ArgumentNullException.ThrowIfNull(outputCompletion);
        ArgumentNullException.ThrowIfNull(isInterrupted);
        if (timeoutMilliseconds <= 0)
        {
            return outputCompletion.IsCompleted;
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
                return outputCompletion.IsCompleted;
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
        return NormalizedSupervisorProtocolLines(output).Contains(
            expected,
            StringComparer.Ordinal);
    }

    internal static (bool Armed, bool Cleanup) FindSupervisorProtocolRecords(
        string output,
        string nonce)
    {
        var armedExpected = LinuxWorkerProcess.ArmedMessage + " " + nonce;
        var cleanupExpected = LinuxWorkerProcess.CleanupMessage + " " + nonce;
        var armed = false;
        var cleanup = false;
        foreach (var normalized in NormalizedSupervisorProtocolLines(output))
        {
            if (string.Equals(
                    normalized,
                    armedExpected,
                    StringComparison.Ordinal))
            {
                armed = true;
            }
            else if (string.Equals(
                         normalized,
                         cleanupExpected,
                         StringComparison.Ordinal))
            {
                cleanup = true;
            }
            if (armed && cleanup)
            {
                break;
            }
        }
        return (armed, cleanup);
    }

    private static IEnumerable<string> NormalizedSupervisorProtocolLines(
        string output)
    {
        foreach (var line in output.Split('\n'))
        {
            yield return line.EndsWith('\r') ? line[..^1] : line;
        }
    }

    internal static bool ShouldDeferSupervisorAuthentication(
        bool authenticationRequired,
        bool outputCompleted)
    {
        return authenticationRequired && !outputCompleted;
    }

    internal static async System.Threading.Tasks.Task<BoundedProcessOutput>
        ReadBoundedOutputAsync(
            TextReader reader,
            string? supervisorNonce,
            ManualResetEventSlim outputLimitSignal,
            System.Threading.Tasks.TaskCompletionSource<bool>?
                supervisorArmedSignal = null,
            System.Threading.Tasks.TaskCompletionSource<bool>?
                supervisorCleanupSignal = null)
    {
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
                outputLimitSignal.Set();
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
                            LinuxWorkerProcess.ArmedMessage + " " + supervisorNonce,
                            StringComparison.Ordinal);
                        supervisorArmed |= armedRecord;
                        if (armedRecord)
                        {
                            supervisorArmedSignal?.TrySetResult(true);
                        }
                        var cleanupRecord = string.Equals(
                            line,
                            LinuxWorkerProcess.CleanupMessage + " " + supervisorNonce,
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
        var records = supervisorNonce == null
            ? (Armed: false, Cleanup: false)
            : FindSupervisorProtocolRecords(text, supervisorNonce);
        return new BoundedProcessOutput(
            text,
            LimitExceeded: false,
            SupervisorArmed: records.Armed,
            CleanupAuthenticated: records.Cleanup);
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
        var token = Interlocked.Increment(ref s_nextCleanupAnchor);
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
                _ = LinuxNativeMethods.Close(anchor.ProcessGroupPidFd);
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

    private static int RemainingMilliseconds(
        Stopwatch stopwatch,
        int timeoutMilliseconds)
    {
        var remaining = timeoutMilliseconds - stopwatch.ElapsedMilliseconds;
        return remaining <= 0
            ? 0
            : (int)Math.Min(remaining, int.MaxValue);
    }

    private void TerminateAfterTimeout(
        Process process,
        int processGroupId,
        Stopwatch processStopwatch,
        int processTimeout,
        ref bool retainCleanupAnchor,
        ref bool containmentFailed)
    {
        var processWasAlive = !process.HasExited;
        var contained = TryTerminate(
            process,
            processGroupId,
            RemainingMilliseconds(processStopwatch, processTimeout));
        retainCleanupAnchor |= processWasAlive;
        containmentFailed |= !contained;
    }

    private bool WaitForExitOrCancellation(
        Process process,
        int timeoutMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!_cancellationSignal.IsSet && !_outputLimitSignal.IsSet)
        {
            var remaining = RemainingMilliseconds(
                stopwatch,
                timeoutMilliseconds);
            if (remaining == 0)
            {
                return process.HasExited;
            }
            if (process.WaitForExit(
                    Math.Min(remaining, OutputDrainPollingMilliseconds)))
            {
                return true;
            }
        }
        return process.HasExited;
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
        if (Arguments.Length < 4 ||
            !IsWorkerLauncherPath(Arguments[0].ItemSpec) ||
            !string.Equals(
                Arguments[1].ItemSpec,
                "verify",
                StringComparison.Ordinal))
        {
            return false;
        }

        for (var index = 2; index + 1 < Arguments.Length; index += 2)
        {
            if (string.Equals(
                    Arguments[index].ItemSpec,
                    "--project-wall-ms",
                    StringComparison.Ordinal) &&
                int.TryParse(
                    Arguments[index + 1].ItemSpec,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var projectWallTimeMilliseconds) &&
                projectWallTimeMilliseconds == ProjectWallTimeMilliseconds)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWorkerLauncherPath(string path)
    {
        try
        {
            var assemblyDirectory = Path.GetDirectoryName(
                typeof(RunVerifier).Assembly.Location);
            if (assemblyDirectory == null)
            {
                return false;
            }
            var candidate = Path.GetFullPath(path);
            var trusted = Path.Combine(
                assemblyDirectory,
                "SharpProof.Worker.Launcher.dll");
            return string.Equals(candidate, trusted, StringComparison.Ordinal) ||
                string.Equals(
                    Path.GetFileName(candidate),
                    Path.GetFileName(trusted),
                    StringComparison.Ordinal) &&
                File.Exists(candidate);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (PathTooLongException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
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
        lock (_gate)
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
                return process.ExitCode == 125;
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
                LinuxProcessControlConstants.SignalTerminate) == 0;
            var boundedWait = Math.Min(
                RemainingMilliseconds(
                    terminationStopwatch,
                    terminationWaitMilliseconds),
                LauncherProcessReserveMilliseconds);
            if (terminateSent && boundedWait > 0 &&
                process.WaitForExit(boundedWait))
            {
                return process.ExitCode != 125;
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
                    LauncherProcessReserveMilliseconds),
                supervisorPidFd: _processGroupPidFd);
            if (SendPidFdSignal(
                    _processGroupPidFd,
                    LinuxProcessControlConstants.SignalStop) == 0)
            {
                // The stopped session leader keeps this process-group identity
                // live while the group-directed signal is delivered.
                _ = LinuxProcessControl.Kill(
                    -processGroupId,
                    LinuxProcessControlConstants.SignalKill);
                _ = SendPidFdSignal(
                    _processGroupPidFd,
                    LinuxProcessControlConstants.SignalKill);
            }
            else if (Marshal.GetLastPInvokeError() !=
                         LinuxProcessControlConstants.ProcessNotFound)
            {
                _ = SendPidFdSignal(
                    _processGroupPidFd,
                    LinuxProcessControlConstants.SignalKill);
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
            checked((int)LinuxNativeMethods.OpenPidFd(processId));
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
        return checked((int)LinuxNativeMethods.SendPidFdSignal(
            descriptor,
            signal));
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

            LogStructuredDiagnostic(diagnostic);
        }
    }

    private void LogStructuredDiagnostic(VerifierDiagnostic diagnostic)
    {
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
            return;
        }

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

    private static bool TryParseLegacyDiagnostic(
        string line,
        out VerifierDiagnostic diagnostic)
    {
        diagnostic = null!;
        var selectedIndex = -1;
        foreach (var candidate in LegacyDiagnosticMarkers)
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
        var trusted = !string.IsNullOrWhiteSpace(disclosedHost)
            ? ValidateDotNetInstallation(disclosedHost)
            : ValidateDotNetInstallation(ResolveDotNetFromPath());
        if (string.Equals(
                executable,
                "dotnet",
                StringComparison.Ordinal))
        {
            return trusted;
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
        lock (_gate)
        {
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

    private static string ResolveDotNetFromPath()
    {
        foreach (var value in (Environment.GetEnvironmentVariable("PATH") ??
                     string.Empty).Split(
                     [Path.PathSeparator],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = value.Trim().Trim('"');
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

    private static string GetFileIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

}
