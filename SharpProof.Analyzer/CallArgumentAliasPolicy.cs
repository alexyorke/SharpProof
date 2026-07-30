namespace SharpProof.Analyzer;

internal enum CallArgumentEvaluation
{
    Snapshot,
    CallEntry,
    Unsupported
}

internal static class CallArgumentAliasPolicy
{
    internal static CallArgumentEvaluation Classify(
        RefKind refKind,
        IOperation actual,
        SyntaxNode? argumentSyntax,
        bool isSyntheticReceiver = false)
    {
        if (!IsAliasKind(refKind))
        {
            return CallArgumentEvaluation.Snapshot;
        }

        if (isSyntheticReceiver)
        {
            return IsTrackedStorage(actual)
                ? CallArgumentEvaluation.CallEntry
                : CallArgumentEvaluation.Unsupported;
        }

        if (argumentSyntax is not ArgumentSyntax syntax)
        {
            return CallArgumentEvaluation.Unsupported;
        }

        if (syntax.RefKindKeyword.Kind() is
            SyntaxKind.RefKeyword or SyntaxKind.InKeyword)
        {
            return IsTrackedStorage(actual)
                ? CallArgumentEvaluation.CallEntry
                : CallArgumentEvaluation.Unsupported;
        }

        return IsReadOnlyAliasKind(refKind)
            ? CallArgumentEvaluation.Snapshot
            : CallArgumentEvaluation.Unsupported;
    }

    private static bool IsAliasKind(RefKind refKind)
    {
        return refKind == RefKind.Ref ||
            IsReadOnlyAliasKind(refKind);
    }

    private static bool IsReadOnlyAliasKind(RefKind refKind)
    {
        return refKind is
            RefKind.In or
            RefKind.RefReadOnly or
            RefKind.RefReadOnlyParameter;
    }

    private static bool IsTrackedStorage(IOperation operation)
    {
        operation =
            DefiniteOperationFacts.UnwrapHarmlessValue(operation);
        return operation is
            ILocalReferenceOperation or
            IParameterReferenceOperation;
    }
}
