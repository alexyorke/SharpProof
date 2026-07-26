namespace SharpProof.Worker;

internal static class Program {
    internal static async Task<int> Main(string[] args) {
        if (!TryParseArguments(
                args,
                out var requestPath,
                out var resultPath)) {
            Console.Error.WriteLine(
                "Usage: SharpProof.Worker verify --request <request.json> --result <result.json>");
            return 2;
        }
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
                new WorkerVerifyResponse {
                    Errors = [new WorkerProtocolError {
                        Code = "request.malformed",
                        Message = "The request file is unavailable or malformed."
                    }]
                }).ConfigureAwait(false);
            return 2;
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
                    new WorkerVerifyResponse {
                        Errors = [.. validation.Errors]
                    }).ConfigureAwait(false);
                return 2;
            }
            using var worker = SharpProofWorker.Create(request!.Budgets);
            var response = await worker.VerifyAsync(
                request,
                cancellation.Token).ConfigureAwait(false);
            await WriteResponseAtomicAsync(resultPath, response)
                .ConfigureAwait(false);
            return response.Errors.Length == 0 ? 0 : 3;
        }
        catch (OperationCanceledException) {
            return 4;
        }
        finally {
            Console.CancelKeyPress -= handler;
        }
    }

    private static bool TryParseArguments(
        string[] args,
        out string request,
        out string result) {
        request = string.Empty;
        result = string.Empty;
        if (args.Length != 5 ||
            !string.Equals(args[0], "verify", StringComparison.Ordinal) ||
            !string.Equals(args[1], "--request", StringComparison.Ordinal) ||
            !string.Equals(args[3], "--result", StringComparison.Ordinal))
            return false;
        request = Path.GetFullPath(args[2]);
        result = Path.GetFullPath(args[4]);
        return true;
    }

    private static async Task WriteResponseAtomicAsync(
        string path,
        WorkerVerifyResponse response) {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(
            Path.GetDirectoryName(fullPath) ??
            throw new InvalidOperationException(
                "The result path has no directory."));
        var temporary = fullPath + "." +
                        Guid.NewGuid().ToString("N") + ".tmp";
        try {
            await File.WriteAllTextAsync(
                temporary,
                WorkerProtocolJson.SerializeResponse(response),
                new UTF8Encoding(false)).ConfigureAwait(false);
            File.Move(temporary, fullPath, overwrite: true);
        }
        finally {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
