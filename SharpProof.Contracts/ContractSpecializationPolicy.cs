namespace SharpProof.Contracts;

// Keeps specialization failure atomic while callers retain their failure result.
internal static class ContractSpecializationPolicy
{
    internal static bool TrySpecialize<TState, TResult>(
        TState state /* SPINT001: all values valid - opaque caller state is forwarded unchanged and permits every value */,
        Func<TState, TResult> specialize,
        out TResult result)
    {
        specialize = RepositoryParameterGuard.NotNull(specialize, nameof(specialize));
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
