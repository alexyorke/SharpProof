namespace SharpProof.Frontend;

public static class OperationSubsetClassifier
{
    public static FrontendSubsetClassification Classify(OperationKind kind)
    {
        if (!Enum.IsDefined(typeof(OperationKind), kind))
        {
            return FrontendSubsetClassification.Abstain(
                FrontendAbstention.UnknownOperationKind);
        }

        return kind switch
        {
            OperationKind.Literal or
            OperationKind.LocalReference or
            OperationKind.ParameterReference or
            OperationKind.InstanceReference or
            OperationKind.DefaultValue or
            OperationKind.UnaryOperator or
            OperationKind.BinaryOperator or
            OperationKind.Conversion or
            OperationKind.Conditional or
            OperationKind.IsNull or
            OperationKind.PropertyReference or
            OperationKind.ArrayElementReference =>
                FrontendSubsetClassification.Exact,
            OperationKind.Invalid or OperationKind.None =>
                FrontendSubsetClassification.Abstain(
                    FrontendAbstention.InvalidOperation),
            _ => FrontendSubsetClassification.Abstain(
                FrontendAbstention.UnsupportedOperationKind)
        };
    }

    public static ImmutableArray<OperationKind> GetKnownOperationKinds()
    {
        return [.. Enum.GetValues(typeof(OperationKind))
            .Cast<OperationKind>()
            .Distinct()
            .OrderBy(static kind => (int)kind)];
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
