using SharpProof.Worker.Protocol;

namespace SharpProof.Worker;

// Runs one verification request in the current process and always returns a
// response: cancellation and infrastructure failures become typed results.
internal static class WorkerHost
{
    internal static async Task<WorkerVerifyResponse> VerifyAsync(
        WorkerVerifyRequest request,
        CancellationToken cancellationToken,
        WorkerInputSnapshot? preparedInput = null,
        long? operationStarted = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budgets = request.Budgets ?? new WorkerBudgets();
        try
        {
            using var worker = SharpProofWorker.Create(budgets);
            return await worker.VerifyAsync(request, preparedInput, cancellationToken, operationStarted).ConfigureAwait(false);
        }
        catch (AggregateException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not
            OutOfMemoryException and not StackOverflowException)
        {
            return Failure(exception, budgets);
        }
    }

    // VerifyAsync retains the fatal and aggregate catch policy.
    internal static WorkerVerifyResponse Failure(Exception exception, WorkerBudgets budgets)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var backendUnavailable = IsBackendUnavailable(exception);
        var message = exception.GetBaseException().Message;
        var singleLineMessage = new string(message.Select(character =>
            char.IsControl(character) || character is '\u2028' or '\u2029' ? ' ' : character).ToArray());
        return Failure(
            backendUnavailable
                ? WorkerRunFailureReason.BackendUnavailable
                : WorkerRunFailureReason.InfrastructureFailure,
            [new WorkerProtocolError {
                Code = backendUnavailable ? "backend.unavailable" : "worker.infrastructure",
                Message = (backendUnavailable
                    ? "The native SMT backend is unavailable."
                    : "The worker failed before producing a semantic result.") +
                    " " + singleLineMessage
            }],
            budgets);
    }

    internal static WorkerVerifyResponse Failure(
        WorkerRunFailureReason reason,
        IEnumerable<WorkerProtocolError> errors,
        WorkerBudgets budgets)
    {
        return WorkerResultAssembler.Create(
            WorkerResultAssembler.EmptyInputHash, WorkerResultAssembler.EmptyManifest(),
            WorkerRunStatus.Failed, reason, [], [], budgets, WorkerCacheStatus.Disabled, 0, errors);
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
}
