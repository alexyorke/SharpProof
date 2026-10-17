namespace SharpProof.Analyzer;

internal static class OperationAncestors
{
    internal static IEnumerable<IOperation> Of(IOperation operation)
    {
        ArgumentNullGuard.NotNull(operation, nameof(operation));
        return Enumerate(operation);
    }

    private static IEnumerable<IOperation> Enumerate(IOperation operation)
    {
        if (operation is null)
        {
            throw new System.ArgumentNullException(nameof(operation));
        }
        for (var current = operation.Parent;
             current != null;
             current = current.Parent)
        {
            yield return current;
        }
    }
}
