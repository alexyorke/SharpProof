namespace SharpProof.Contracts;

// Applies a caller's binding only after the invocation's unary shape is checked.
internal static class ContractInvocationShape
{
    internal static TResult ApplySingleArgument<TState, TResult>(
        IInvocationOperation invocation,
        TState state,
        Func<IInvocationOperation, TState, TResult> bind,
        TResult rejected)
    {
        if (invocation.Arguments.Length != 1)
        {
            return rejected;
        }
        return bind(invocation, state);
    }
}
