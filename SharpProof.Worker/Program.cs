using System.Runtime.InteropServices;
using SharpProof.Host;

namespace SharpProof.Worker;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        if (!TryParseArguments(
                args,
                out var requestPath,
                out var resultPath,
                out var parentProcessId))
        {
            Console.Error.WriteLine(
                "Usage: SharpProof.Worker verify --request <request.json> --result <result.json> " +
                "--start-stdin --parent-pid <pid>");
            return 2;
        }
        try
        {
            ContainerContract.ValidateRequired();
            LinuxWorkerProcess.EnterChildBoundaryRequired(parentProcessId);
            if (!await WaitForStartAsync(Console.In, TimeSpan.FromSeconds(30))
                    .ConfigureAwait(false))
            {
                return 125;
            }
        }
        catch (Exception exception) when (exception is
            BadImageFormatException or DllNotFoundException or
                EntryPointNotFoundException or FileLoadException or
                FileNotFoundException or
            ArgumentException or IOException or InvalidDataException or
                InvalidOperationException or PlatformNotSupportedException or
                UnauthorizedAccessException)
        {
            Console.Error.WriteLine(
                "The SharpProof worker containment boundary is unavailable: " +
                exception.Message);
            return 125;
        }

        WorkerVerifyRequest? request = null;
        async Task<int> Respond(WorkerVerifyResponse response)
        {
            try
            {
                await WriteResponseAtomicAsync(resultPath, response).ConfigureAwait(false);
                return 0;
            }
            catch (InvalidDataException exception)
            {
                // A manifest can be valid while the fully expanded response is
                // not representable inside the bounded JSON envelope. Publish
                // a compact, manifest-independent infrastructure failure rather
                // than committing an unreadable oversized response.
                Console.Error.WriteLine(
                    "The SharpProof worker response exceeded its size limit: " +
                    exception.Message);
                try
                {
                    await WriteResponseAtomicAsync(
                        resultPath,
                        Failure(
                            WorkerRunFailureReason.InfrastructureFailure,
                            [new WorkerProtocolError {
                                Code = "worker.response_too_large",
                                Message = "The worker response exceeded the JSON size limit."
                            }],
                            request?.Budgets ?? new WorkerBudgets()))
                        .ConfigureAwait(false);
                }
                catch (Exception fallbackException) when (fallbackException is
                    IOException or UnauthorizedAccessException or ArgumentException or
                    InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Console.Error.WriteLine(
                        "The SharpProof worker could not publish its bounded failure response: " +
                        fallbackException.Message);
                }

                return 3;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    ArgumentException or InvalidOperationException or
                    System.ComponentModel.Win32Exception)
            {
                Console.Error.WriteLine(
                    "The SharpProof worker could not publish its response: " +
                    exception.Message);
                return 3;
            }
        }
        try
        {
            request = WorkerProtocolJson.DeserializeRequest(
                await WorkerProtocolJson.ReadUtf8FileAsync(requestPath).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or InvalidDataException or
                UnauthorizedAccessException or JsonException)
        {
            return await Respond(Failure(WorkerRunFailureReason.InvalidRequest,
                [new WorkerProtocolError {
                    Code = "request.malformed", Message = "The request file is unavailable or malformed."
                }], new WorkerBudgets())).ConfigureAwait(false);
        }
        using var cancellation = new CancellationGate();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        using var terminate = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                cancellation.Cancel();
            });
        try
        {
            var budgets = request?.Budgets ?? new WorkerBudgets();
            using var worker = SharpProofWorker.Create(budgets);
            var response = await worker.VerifyAsync(request!, cancellation.Token).ConfigureAwait(false);
            return await Respond(response).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await Respond(WorkerResultAssembler.Create(
                WorkerResultAssembler.EmptyInputHash, WorkerResultAssembler.EmptyManifest(),
                WorkerRunStatus.Canceled, WorkerRunFailureReason.None, [], [],
                request?.Budgets ?? new WorkerBudgets(), WorkerCacheStatus.Disabled, 0,
                [new WorkerProtocolError {
                    Code = "worker.canceled",
                    Message = "The worker was canceled before producing manifest-bound evidence."
                }])).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not
            OutOfMemoryException and not StackOverflowException)
        {
            var backendUnavailable = IsBackendUnavailable(exception);
            return await Respond(Failure(backendUnavailable ? WorkerRunFailureReason.BackendUnavailable :
                WorkerRunFailureReason.InfrastructureFailure, [new WorkerProtocolError {
                    Code = backendUnavailable ? "backend.unavailable" : "worker.infrastructure",
                    Message = (backendUnavailable ? "The native SMT backend is unavailable." :
                        "The worker failed before producing a semantic result.") + " " + exception.GetBaseException().Message
                }], request?.Budgets ?? new WorkerBudgets())).ConfigureAwait(false);
        }
        finally { Console.CancelKeyPress -= handler; }
    }
    private static WorkerVerifyResponse Failure(WorkerRunFailureReason reason,
        IEnumerable<WorkerProtocolError> errors, WorkerBudgets budgets)
    {
        return WorkerResultAssembler.Create(
            WorkerResultAssembler.EmptyInputHash, WorkerResultAssembler.EmptyManifest(),
            WorkerRunStatus.Failed, reason, [], [], budgets, WorkerCacheStatus.Disabled, 0, errors);
    }

    private sealed class CancellationGate : IDisposable
    {
        private readonly object _synchronization = new();
        private readonly CancellationTokenSource _source = new();
        private bool _disposing;
        private int _callbacks;

        internal CancellationToken Token => _source.Token;

        internal void Cancel()
        {
            lock (_synchronization)
            {
                if (_disposing)
                {
                    return;
                }

                _callbacks++;
            }

            try
            {
                _source.Cancel();
            }
            finally
            {
                lock (_synchronization)
                {
                    _callbacks--;
                    if (_callbacks == 0)
                    {
                        Monitor.PulseAll(_synchronization);
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_synchronization)
            {
                _disposing = true;
                while (_callbacks != 0)
                {
                    Monitor.Wait(_synchronization);
                }
            }

            _source.Dispose();
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string request,
        out string result,
        out int parentProcessId)
    {
        if (args is not ["verify", "--request", var requestValue,
                "--result", var resultValue, "--start-stdin",
                "--parent-pid", var parentProcessValue] ||
            !int.TryParse(
                parentProcessValue,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out parentProcessId) ||
            parentProcessId <= 0)
        {
            request = result = string.Empty;
            parentProcessId = 0;
            return false;
        }
        try
        {
            request = Path.GetFullPath(requestValue);
            result = Path.GetFullPath(resultValue);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IOException or NotSupportedException)
        {
            request = result = string.Empty;
            parentProcessId = 0;
            return false;
        }
        return !string.Equals(request, result, StringComparison.Ordinal);
    }

    internal static async Task<bool> WaitForStartAsync(
        TextReader input,
        TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        // Console.In can execute ReadLineAsync synchronously before returning
        // its task. Run the synchronous read on a background thread so a
        // wedged redirected stdin cannot defeat the startup deadline.
        var read = Task.Run(input.ReadLine);
        var completed = await Task.WhenAny(read, Task.Delay(timeout))
            .ConfigureAwait(false);
        if (!ReferenceEquals(completed, read) ||
            read.Status != TaskStatus.RanToCompletion)
        {
            return false;
        }

        var line = read.Result;
        return string.Equals(
            line,
            LinuxWorkerProcess.StartMessage,
            StringComparison.Ordinal);
    }
    internal static bool IsBackendUnavailable(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException or FileLoadException or FileNotFoundException ||
                string.Equals(current.GetType().FullName, "Microsoft.Z3.Z3Exception", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
    private static Task WriteResponseAtomicAsync(string path, WorkerVerifyResponse response)
    {
        return AtomicFile.WriteUtf8Async(path, WorkerProtocolJson.SerializeResponse(response));
    }
}
