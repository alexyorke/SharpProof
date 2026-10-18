namespace SharpProof.Contracts;

// Applies a caller's binding only after the invocation's unary shape is checked.
internal static class ContractInvocationShape
{
    internal static TResult ApplySingleArgument<TState, TResult>(
        IInvocationOperation invocation,
        TState state /* SPINT001: all values valid - opaque caller state is forwarded unchanged and permits every value */,
        Func<IInvocationOperation, TState, TResult> bind,
        TResult rejected /* SPINT001: all values valid - the rejection result is opaque and returned unchanged, including null */)
    {
        bind = RepositoryParameterGuard.NotNull(bind, nameof(bind));
        if (invocation.Arguments.Length != 1)
        {
            return rejected;
        }
        return bind(invocation, state);
    }
}
