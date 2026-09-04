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
            if (!await WaitForStartAsync(TimeSpan.FromSeconds(30))
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

        async Task<int> Respond(WorkerVerifyResponse response)
        {
            await WriteResponseAtomicAsync(resultPath, response).ConfigureAwait(false);
            return 0;
        }
        WorkerVerifyRequest? request;
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
        using var cancellation = new CancellationTokenSource();
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

    private static async Task<bool> WaitForStartAsync(TimeSpan timeout)
    {
        using var timeoutBoundary = new CancellationTokenSource(timeout);
        try
        {
            var line = await Console.In.ReadLineAsync(timeoutBoundary.Token)
                .ConfigureAwait(false);
            return string.Equals(
                line,
                LinuxWorkerProcess.StartMessage,
                StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
    internal static bool IsBackendUnavailable(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is DllNotFoundException or EntryPointNotFoundException or
                BadImageFormatException ||
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
