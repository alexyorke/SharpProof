namespace SharpProof.Contracts;

internal enum ClosedContractAttributeKind
{
    None,
    NotNull,
    Positive,
    InRange
}

internal readonly struct ClosedContractAttributeValidation(
    ClosedContractAttributeClassification classification,
    string? invalidReason = null,
    long minimum = default,
    long maximum = default)
{
    private readonly ClosedContractAttributeClassification? _classification =
        ArgumentNullGuard.NotNull(classification, nameof(classification));

    internal ClosedContractAttributeKind Kind => _classification?.Kind ?? ClosedContractAttributeKind.None;
    internal string? InvalidReason { get; } = invalidReason;
    internal long Minimum { get; } = minimum;
    internal long Maximum { get; } = maximum;
    internal bool IsRecognized => _classification != null;
    internal bool IsValid => IsRecognized && InvalidReason == null;

    internal string AttributeName
    {
        get
        {
            if (_classification == null)
            {
                throw new InvalidOperationException("The attribute is not a closed contract.");
            }
            return _classification.AttributeName;
        }
    }

    internal IrTerm? CreateTotalPredicate(IrFactory factory, IrTerm value, IrTypeInfo type)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return _classification?.CreateTotalPredicate(factory, value, type, Minimum, Maximum);
    }
}

internal static class ClosedContractAttributeValidator
{
    internal static IEnumerable<(
        ITypeSymbol Type,
        RefKind RefKind,
        ImmutableArray<AttributeData> Attributes,
        Location Fallback,
        int ParameterIndex,
        bool IsReturn)> EnumerateValueSites(
        IMethodSymbol method,
        bool includeReturn)
    {
        ArgumentNullGuard.NotNull(method, nameof(method));
        for (var index = 0; index < method.Parameters.Length; index++)
        {
            var parameter = method.Parameters[index];
            yield return (
                parameter.Type,
                parameter.RefKind,
                parameter.GetAttributes(),
                parameter.Locations.FirstOrDefault() ?? Location.None,
                index,
                false);
        }

        if (includeReturn)
        {
            yield return (
                method.ReturnType,
                RefKind.None,
                method.GetReturnTypeAttributes(),
                method.Locations.FirstOrDefault() ?? Location.None,
                -1,
                true);
        }
    }

    internal static ClosedContractAttributeValidation Validate(
        AttributeData attribute,
        ITypeSymbol type,
        RefKind refKind,
        ContractSelectionInventory symbols,
        bool includeUnsigned64 = false)
    {
        ArgumentNullGuard.NotNull(attribute, nameof(attribute));
        ArgumentNullGuard.NotNull(type, nameof(type));
        ArgumentNullGuard.NotNull(symbols, nameof(symbols));
        var classification = symbols.GetClosedContractClassification(attribute);
        if (classification == null)
        {
            return default;
        }

        if (refKind == RefKind.Out)
        {
            return Invalid(
                classification,
                "expected an incoming parameter value; out parameters have no entry value");
        }

        return classification.Kind switch
        {
            ClosedContractAttributeKind.NotNull when !CanBeNull(type) =>
                Invalid(classification, "expected a nullable or reference-capable value"),
            ClosedContractAttributeKind.Positive when !IsSupportedInteger(type, includeUnsigned64) =>
                Invalid(classification, "expected a supported integral value"),
            ClosedContractAttributeKind.InRange =>
                ValidateRange(attribute, type, includeUnsigned64),
            _ => new ClosedContractAttributeValidation(classification)
        };
    }

    private static ClosedContractAttributeValidation ValidateRange(
        AttributeData attribute,
        ITypeSymbol type,
        bool includeUnsigned64)
    {
        if (!IsSupportedInteger(type, includeUnsigned64) ||
            attribute.ConstructorArguments.Length != 2 ||
            attribute.ConstructorArguments[0].Value is not long minimum ||
            attribute.ConstructorArguments[1].Value is not long maximum ||
            minimum > maximum)
        {
            return Invalid(
                ClosedContractAttributeClassification.InRange,
                "expected a supported integral value and ordered bounds");
        }

        return new ClosedContractAttributeValidation(
            ClosedContractAttributeClassification.InRange,
            minimum: minimum,
            maximum: maximum);
    }

    private static ClosedContractAttributeValidation Invalid(
        ClosedContractAttributeClassification classification,
        string reason)
    {
        classification = RepositoryParameterGuard.NotNull(classification, nameof(classification));
        reason = RepositoryParameterGuard.NotNull(reason, nameof(reason));
        return new ClosedContractAttributeValidation(classification, reason);
    }

    private static bool IsSupportedInteger(ITypeSymbol type, bool includeUnsigned64)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return CSharpOperationSemantics.IsSupportedInteger(type.SpecialType) ||
            includeUnsigned64 && type.SpecialType == SpecialType.System_UInt64;
    }

    private static bool CanBeNull(ITypeSymbol type)
    {
        if (type.IsReferenceType)
        {
            return true;
        }

        return type switch
        {
            INamedTypeSymbol named =>
                named.OriginalDefinition.SpecialType ==
                    SpecialType.System_Nullable_T,
            ITypeParameterSymbol parameter =>
                !parameter.HasValueTypeConstraint &&
                !parameter.HasUnmanagedTypeConstraint,
            _ => false
        };
    }
}

internal sealed class ClosedContractAttributeClassification
{
    internal static readonly ClosedContractAttributeClassification NotNull =
        new(ClosedContractAttributeKind.NotNull, "[NotNull]",
            IsTotalReferenceValue, CreateTotalNotNullPredicate);
    internal static readonly ClosedContractAttributeClassification Positive =
        new(ClosedContractAttributeKind.Positive, "[Positive]",
            IsTotalIntegerValue, CreateTotalPositivePredicate);
    internal static readonly ClosedContractAttributeClassification InRange =
        new(ClosedContractAttributeKind.InRange, "[InRange]",
            IsTotalIntegerValue, CreateTotalRangePredicate);

    private readonly Func<IrTypeInfo, bool> _supportsTotalValue;
    private readonly Func<IrFactory, IrTerm, IrTypeInfo, long, long, IrTerm> _createTotalPredicate;

    private ClosedContractAttributeClassification(ClosedContractAttributeKind kind, string attributeName,
        Func<IrTypeInfo, bool> supportsTotalValue,
        Func<IrFactory, IrTerm, IrTypeInfo, long, long, IrTerm> createTotalPredicate)
    {
        attributeName = RepositoryParameterGuard.NotNull(attributeName, nameof(attributeName));
        createTotalPredicate = RepositoryParameterGuard.NotNull(createTotalPredicate, nameof(createTotalPredicate));
        supportsTotalValue = RepositoryParameterGuard.NotNull(supportsTotalValue, nameof(supportsTotalValue));
        Kind = kind;
        AttributeName = attributeName;
        _supportsTotalValue = supportsTotalValue;
        _createTotalPredicate = createTotalPredicate;
    }

    internal ClosedContractAttributeKind Kind { get; }
    internal string AttributeName { get; }

    internal IrTerm? CreateTotalPredicate(IrFactory factory, IrTerm value, IrTypeInfo type, long minimum, long maximum)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return _supportsTotalValue(type)
            ? _createTotalPredicate(factory, value, type, minimum, maximum)
            : null;
    }

    private static bool IsTotalReferenceValue(IrTypeInfo type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.Kind is IrTypeKind.Reference or IrTypeKind.String or IrTypeKind.Sequence;
    }

    private static bool IsTotalIntegerValue(IrTypeInfo type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.Kind == IrTypeKind.Integer && type.Width > 0;
    }

    private static IrTerm CreateTotalNotNullPredicate(IrFactory factory, IrTerm value, IrTypeInfo type, long __, long ___)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));

        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return factory.Binary(IrBinaryOperator.NotEqual, value, factory.Null(value.Type));
    }

    private static IrTerm CreateTotalPositivePredicate(IrFactory factory, IrTerm value, IrTypeInfo type, long _, long __)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return CompareTotalBound(factory, value, type, IrBinaryOperator.GreaterThan, 0);
    }

    private static IrTerm CreateTotalRangePredicate(IrFactory factory, IrTerm value, IrTypeInfo type, long minimum, long maximum)
    {
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        return factory.Binary(IrBinaryOperator.AndAlso,
            CompareTotalBound(factory, value, type, IrBinaryOperator.GreaterThanOrEqual, minimum),
            CompareTotalBound(factory, value, type, IrBinaryOperator.LessThanOrEqual, maximum));
    }

    private static IrTerm CompareTotalBound(IrFactory factory, IrTerm value, IrTypeInfo type, IrBinaryOperator op, long bound)
    {
        // Fold bounds outside the scalar domain; never truncate an attribute's long bound.
        factory = RepositoryParameterGuard.NotNull(factory, nameof(factory));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        value = RepositoryParameterGuard.NotNull(value, nameof(value));
        var minimum = type.Signed ? type.Width == 64 ? long.MinValue : -(1L << (type.Width - 1)) : 0L;
        var maximum = type.Signed ? type.Width == 64 ? (ulong)long.MaxValue : (1UL << (type.Width - 1)) - 1 :
            type.Width == 64 ? ulong.MaxValue : (1UL << type.Width) - 1;
        if (bound < minimum)
        { return factory.Boolean(op != IrBinaryOperator.LessThanOrEqual); }
        if (bound >= 0 && (ulong)bound > maximum)
        { return factory.Boolean(op == IrBinaryOperator.LessThanOrEqual); }
        return factory.Binary(op, value, factory.Integer(value.Type, bound));
    }

}
