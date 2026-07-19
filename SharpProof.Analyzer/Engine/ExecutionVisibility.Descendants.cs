namespace SharpProof.Analyzer.Engine;

internal static partial class ExecutionVisibility
{
    public static IEnumerable<IOperation> VisibleDescendants(IOperation rootOperation)
    {
        foreach (var operation in rootOperation.DescendantsAndSelf())
            if (!IsNestedFunctionDescendant(operation, rootOperation))
                yield return operation;
    }


    private static bool IsNestedFunctionDescendant(IOperation operation, IOperation rootOperation)
    {
        if (ReferenceEquals(operation, rootOperation)) return false;

        for (var parent = operation.Parent;
             parent != null && !ReferenceEquals(parent, rootOperation);
             parent = parent.Parent)
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return true;

        return false;
    }
}
