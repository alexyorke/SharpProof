namespace SharpProof.Contracts;

internal sealed class ClosedContractAttributeClassification
{
    internal static readonly ClosedContractAttributeClassification NotNull =
        new(ClosedContractAttributeKind.NotNull, "[NotNull]", IsReferenceValue, CreateNotNullPredicate);
    internal static readonly ClosedContractAttributeClassification Positive =
        new(ClosedContractAttributeKind.Positive, "[Positive]", IsIntegerValue, CreatePositivePredicate);
    internal static readonly ClosedContractAttributeClassification InRange =
        new(ClosedContractAttributeKind.InRange, "[InRange]", IsIntegerValue, CreateRangePredicate);

    private readonly Func<IrFactory, IrTerm, bool> _supportsLegacyValue;
    private readonly Func<IrFactory, IrTerm, long, long, IrTerm> _createLegacyPredicate;

    private ClosedContractAttributeClassification(ClosedContractAttributeKind kind, string attributeName,
        Func<IrFactory, IrTerm, bool> supportsLegacyValue,
        Func<IrFactory, IrTerm, long, long, IrTerm> createLegacyPredicate)
    {
        Kind = kind;
        AttributeName = attributeName;
        _supportsLegacyValue = supportsLegacyValue;
        _createLegacyPredicate = createLegacyPredicate;
    }

    internal ClosedContractAttributeKind Kind { get; }
    internal string AttributeName { get; }

    internal bool SupportsLegacyValue(IrFactory factory, IrTerm value)
    {
        return _supportsLegacyValue(factory, value);
    }

    internal IrTerm CreateLegacyPredicate(IrFactory factory, IrTerm value, long minimum, long maximum)
    {
        return _createLegacyPredicate(factory, value, minimum, maximum);
    }

    private static bool IsReferenceValue(IrFactory factory, IrTerm value)
    {
        var type = factory.GetTypeInfo(value.Type);
        return type.Kind is
            IrTypeKind.Reference or
            IrTypeKind.String or
            IrTypeKind.Sequence;
    }

    private static bool IsIntegerValue(IrFactory factory, IrTerm value)
    {
        return value.Type == factory.IntegerType;
    }

    private static IrTerm CreateNotNullPredicate(IrFactory factory, IrTerm value, long _, long __)
    {
        return factory.Binary(IrBinaryOperator.NotEqual, value, factory.Null(value.Type));
    }

    private static IrTerm CreatePositivePredicate(IrFactory factory, IrTerm value, long _, long __)
    {
        return factory.Binary(IrBinaryOperator.GreaterThan, value, factory.Integer(0));
    }

    private static IrTerm CreateRangePredicate(IrFactory factory, IrTerm value, long minimum, long maximum)
    {
        return factory.Binary(
            IrBinaryOperator.AndAlso,
            factory.Binary(IrBinaryOperator.GreaterThanOrEqual, value, factory.Integer(minimum)),
            factory.Binary(IrBinaryOperator.LessThanOrEqual, value, factory.Integer(maximum)));
    }
}
