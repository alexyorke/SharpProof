using NUnit.Framework;
using SharpProof.Smt;
using SharpProof.Verify;

namespace SharpProof.Worker.Test;

internal sealed class ThrowingBackend(string message) : ISmtBackend
{
    private readonly string _message = message;
    private int _callCount;

    internal int CallCount => Volatile.Read(ref _callCount);

    public Task<BackendCheckResult> CheckAsync(
        VerificationQuery query,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _callCount);
        throw new AssertionException(_message);
    }
}
