namespace SharpProof.Effects;

internal static class ManagedMutationFacts
{
    internal static bool HasMutation(IOperation operation)
    {
        return operation.DescendantsAndSelf().Any(static candidate =>
            candidate is IAssignmentOperation or
                IIncrementOrDecrementOperation or
                IDynamicInvocationOperation ||
            candidate is IArgumentOperation
            {
                Parameter.RefKind: not RefKind.None
            } ||
            candidate is IInvocationOperation invocation &&
            (invocation.TargetMethod.MethodKind == MethodKind.LocalFunction ||
             invocation.TargetMethod.ContainingType.TypeKind ==
                TypeKind.Delegate));
    }
}
