namespace SharpProof.Frontend;

public static class OperationSubsetClassifier
{
    private static readonly ImmutableArray<OperationKind> s_knownOperationKinds =
        [.. Enum.GetValues(typeof(OperationKind))
            .Cast<OperationKind>()
            .Distinct()
            .OrderBy(static kind => (int)kind)];
    private static readonly ImmutableHashSet<OperationKind>
        s_knownOperationKindSet =
            ImmutableHashSet.CreateRange(s_knownOperationKinds);

    internal static FrontendSubsetClassification Classify(
        OperationSupportStage stage,
        OperationKind kind)
    {
        if (!s_knownOperationKindSet.Contains(kind))
        {
            return FrontendSubsetClassification.Abstain(
                FrontendAbstention.UnknownOperationKind);
        }

        if (CSharpOperationSemantics.IsSupported(
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1024:Use properties where appropriate",
        Justification = "The method is part of the existing snapshot API.")]
    public static ImmutableArray<OperationKind> GetKnownOperationKinds()
    {
        return s_knownOperationKinds;
    }

    public static string CreateSnapshot()
    {
        var builder = new StringBuilder();
        foreach (var kind in GetKnownOperationKinds())
        {
            var classification = Classify(
                OperationSupportStage.ContractExpressionLowering,
                kind);
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
