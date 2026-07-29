namespace SharpProof.Worker;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        if (!TryParseArguments(args, out var requestPath, out var resultPath, out var startEventName))
        {
            Console.Error.WriteLine("Usage: SharpProof.Worker verify --request <request.json> --result <result.json> --start-event <name>");
            return 2;
        }
        if (!OperatingSystem.IsWindows() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
                System.Runtime.InteropServices.Architecture.X64 || System.Runtime.InteropServices.RuntimeInformation.OSArchitecture !=
                System.Runtime.InteropServices.Architecture.X64)
        {
            Console.Error.WriteLine("The SharpProof verifier requires Windows x64.");
            return 125;
        }
        if (!WaitForStart(startEventName))
        {
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
            request = WorkerProtocolJson.DeserializeRequest(await File.ReadAllTextAsync(requestPath).ConfigureAwait(false));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
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
        try
        {
            using var worker = SharpProofWorker.Create(request?.Budgets ?? new WorkerBudgets());
            var response = await worker.VerifyAsync(request!, cancellation.Token).ConfigureAwait(false);
            return await Respond(response).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await Respond(WorkerResultAssembler.Create(
                WorkerResultAssembler.EmptyInputHash, WorkerResultAssembler.EmptyManifest(),
                WorkerRunStatus.Canceled, WorkerRunFailureReason.None, [], [],
                request?.Budgets ?? new WorkerBudgets(), WorkerCacheStatus.Disabled, 0)).ConfigureAwait(false);
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
        string[] args, out string request, out string result, out string startEvent)
    {
        if (args is not ["verify", "--request", var requestValue,
                "--result", var resultValue, "--start-event", var eventValue] ||
            string.IsNullOrWhiteSpace(eventValue))
        {
            request = result = startEvent = string.Empty;
            return false;
        }
        request = Path.GetFullPath(requestValue);
        result = Path.GetFullPath(resultValue);
        startEvent = eventValue;
        return true;
    }
    private static bool WaitForStart(string eventName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var startEvent = EventWaitHandle.OpenExisting(eventName);
            return startEvent.WaitOne(TimeSpan.FromSeconds(30));
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
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
