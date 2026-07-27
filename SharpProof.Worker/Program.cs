namespace SharpProof.Worker;

internal static class Program {
    internal static async Task<int> Main(string[] args) {
        if (!TryParseArguments(
                args,
                out var requestPath,
                out var resultPath,
                out var startEventName)) {
            Console.Error.WriteLine(
                "Usage: SharpProof.Worker verify --request <request.json> --result <result.json> --start-event <name>");
            return 2;
        }
        if (!OperatingSystem.IsWindows() ||
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture !=
                System.Runtime.InteropServices.Architecture.X64 ||
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture !=
                System.Runtime.InteropServices.Architecture.X64) {
            Console.Error.WriteLine(
                "The SharpProof verifier requires Windows x64.");
            return 125;
        }
        if (!WaitForStart(startEventName)) return 125;
        WorkerVerifyRequest? request;
        try {
            var requestJson = await File.ReadAllTextAsync(requestPath)
                .ConfigureAwait(false);
            request = WorkerProtocolJson.DeserializeRequest(requestJson);
        }
        catch (Exception exception) when (exception is
            IOException or
            UnauthorizedAccessException or
            JsonException) {
            await WriteResponseAtomicAsync(
                resultPath,
                Failure(
                    WorkerRunFailureReason.InvalidRequest,
                    [new WorkerProtocolError {
                        Code = "request.malformed",
                        Message = "The request file is unavailable or malformed."
                    }],
                    new WorkerBudgets())).ConfigureAwait(false);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) => {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try {
            var validation = WorkerProtocolJson.Validate(request);
            if (!validation.IsValid) {
                await WriteResponseAtomicAsync(
                    resultPath,
                    Failure(
                        WorkerRunFailureReason.InvalidRequest,
                        validation.Errors,
                        new WorkerBudgets()))
                    .ConfigureAwait(false);
                return 0;
            }
            using var worker = SharpProofWorker.Create(request!.Budgets);
            var response = await worker.VerifyAsync(
                request,
                cancellation.Token).ConfigureAwait(false);
            await WriteResponseAtomicAsync(resultPath, response)
                .ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) {
            await WriteResponseAtomicAsync(
                resultPath,
                WorkerResultAssembler.Create(
                    WorkerResultAssembler.EmptyInputHash,
                    WorkerResultAssembler.EmptyManifest(),
                    WorkerRunStatus.Canceled,
                    WorkerRunFailureReason.None,
                    [],
                    [],
                    request?.Budgets ?? new WorkerBudgets(),
                    WorkerCacheStatus.Disabled,
                    0)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception) when (exception is not
            OutOfMemoryException and not StackOverflowException) {
            var backendUnavailable = IsBackendUnavailable(exception);
            await WriteResponseAtomicAsync(
                resultPath,
                Failure(
                    backendUnavailable
                        ? WorkerRunFailureReason.BackendUnavailable
                        : WorkerRunFailureReason.InfrastructureFailure,
                    [new WorkerProtocolError {
                        Code = backendUnavailable
                            ? "backend.unavailable"
                            : "worker.infrastructure",
                        Message = backendUnavailable
                            ? "The native SMT backend is unavailable."
                            : "The worker failed before producing a semantic result."
                    }],
                    request?.Budgets ?? new WorkerBudgets()))
                .ConfigureAwait(false);
            return 0;
        }
        finally {
            Console.CancelKeyPress -= handler;
        }
    }

    private static WorkerVerifyResponse Failure(
        WorkerRunFailureReason reason,
        IEnumerable<WorkerProtocolError> errors,
        WorkerBudgets budgets) =>
        WorkerResultAssembler.Create(
            WorkerResultAssembler.EmptyInputHash,
            WorkerResultAssembler.EmptyManifest(),
            WorkerRunStatus.Failed,
            reason,
            [],
            [],
            budgets,
            WorkerCacheStatus.Disabled,
            0,
            errors);

    private static bool TryParseArguments(
        string[] args,
        out string request,
        out string result,
        out string startEvent) {
        request = string.Empty;
        result = string.Empty;
        startEvent = string.Empty;
        if (args.Length != 7 ||
            !string.Equals(args[0], "verify", StringComparison.Ordinal) ||
            !string.Equals(args[1], "--request", StringComparison.Ordinal) ||
            !string.Equals(args[3], "--result", StringComparison.Ordinal) ||
            !string.Equals(args[5], "--start-event", StringComparison.Ordinal))
            return false;
        request = Path.GetFullPath(args[2]);
        result = Path.GetFullPath(args[4]);
        startEvent = args[6];
        return !string.IsNullOrWhiteSpace(startEvent);
    }

    private static bool WaitForStart(string eventName) {
        if (!OperatingSystem.IsWindows()) return false;
        try {
            using var startEvent = EventWaitHandle.OpenExisting(eventName);
            return startEvent.WaitOne(TimeSpan.FromSeconds(30));
        }
        catch (WaitHandleCannotBeOpenedException) {
            return false;
        }
    }

    internal static bool IsBackendUnavailable(Exception exception) {
        for (Exception? current = exception; current != null;
             current = current.InnerException) {
            if (current is DllNotFoundException or
                EntryPointNotFoundException or
                BadImageFormatException or
                FileLoadException or
                FileNotFoundException ||
                string.Equals(
                    current.GetType().FullName,
                    "Microsoft.Z3.Z3Exception",
                    StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static Task WriteResponseAtomicAsync(
        string path,
        WorkerVerifyResponse response) =>
        AtomicFile.WriteUtf8Async(
            path, WorkerProtocolJson.SerializeResponse(response));
}
