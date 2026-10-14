namespace SharpProof.Contracts;

// Keeps specialization failure atomic while callers retain their failure result.
internal static class ContractSpecializationPolicy
{
    internal static bool TrySpecialize<TState, TResult>(
        TState state,
        Func<TState, TResult> specialize,
        out TResult result)
    {
        try
        {
            result = specialize(state);
            return true;
        }
        catch (ArgumentException)
        {
            result = default!;
            return false;
        }
    }
}
