namespace SharpProof.Analyzer;

internal static class OperationAncestors
{
    internal static IEnumerable<IOperation> Of(IOperation operation)
    {
        for (var current = operation.Parent;
             current != null;
             current = current.Parent)
        {
            yield return current;
        }
    }
}
