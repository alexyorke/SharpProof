namespace SharpProof.Frontend;

public static class OperationSubsetClassifier
{
    private static readonly ImmutableArray<OperationKind> s_knownOperationKinds =
        [.. Enum.GetValues(typeof(OperationKind))
            .Cast<OperationKind>()
            .Distinct()
            .OrderBy(static kind => (int)kind)];

    public static FrontendSubsetClassification Classify(OperationKind kind)
    {
        return Classify(
            OperationSupportStage.ContractExpressionLowering,
            kind);
    }

    internal static FrontendSubsetClassification Classify(
        OperationSupportStage stage,
        OperationKind kind)
    {
        if (!Enum.IsDefined(typeof(OperationKind), kind))
        {
            return FrontendSubsetClassification.Abstain(
                FrontendAbstention.UnknownOperationKind);
        }

        if (OperationSupportCatalog.IsSupported(
                stage,
                kind))
        {
            return FrontendSubsetClassification.Exact;
        }

        return kind is OperationKind.Invalid or OperationKind.None
            ?
                FrontendSubsetClassification.Abstain(
                    FrontendAbstention.InvalidOperation)
            : FrontendSubsetClassification.Abstain(
                    FrontendAbstention.UnsupportedOperationKind);
    }

    public static ImmutableArray<OperationKind> GetKnownOperationKinds()
    {
        return s_knownOperationKinds;
    }

    public static string CreateSnapshot()
    {
        var builder = new StringBuilder();
        foreach (var kind in GetKnownOperationKinds())
        {
            var classification = Classify(kind);
            builder.Append(((int)kind).ToString(CultureInfo.InvariantCulture));
            builder.Append('|');
            builder.Append(Enum.GetName(typeof(OperationKind), kind));
            builder.Append('|');
            builder.Append(classification.Decision);
            builder.Append('|');
            builder.Append(classification.Abstention);
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
