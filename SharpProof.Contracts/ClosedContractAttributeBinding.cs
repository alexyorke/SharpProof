namespace SharpProof.Contracts;

// Projects one compiler-owned value site's closed annotations into owned IR.
// Every annotation is processed in source order; failure publishes no prefix.
internal static class ClosedContractAttributeBinding
{
    internal const int MaximumDiagnosticConditionLength = 512;

    internal static ClosedContractAttributeBindingResult BindValue(
        IrFactory factory,
        ContractSelectionInventory symbols,
        ImmutableArray<AttributeData> attributes,
        ITypeSymbol sourceType,
        RefKind refKind,
        Func<IrTerm?> getValue,
        string valueName,
        BoundContractKind kind,
        bool total,
        Func<SyntaxNode?, OperationId> createOperation)
    {

        RepositoryParameterGuard.RequireInitialized(attributes.IsDefault, nameof(attributes), "Array must be initialized.");
        createOperation = RepositoryParameterGuard.NotNull(createOperation, nameof(createOperation));
        getValue = RepositoryParameterGuard.NotNull(getValue, nameof(getValue));
        valueName = RepositoryParameterGuard.NotNull(valueName, nameof(valueName));
        var clauses = ImmutableList<BoundClosedAttributeValue>.Empty;
        foreach (var attribute in attributes)
        {
            if (symbols.IsRejectedClosedContract(attribute))
            {
                return new([], ContractBindingFailure.InvalidClosedAttribute);
            }

            var validation = ClosedContractAttributeValidator.Validate(
                attribute, sourceType, refKind, symbols, includeUnsigned64: total);
            if (!validation.IsRecognized)
            {
                continue;
            }

            if (total)
            {
                if (!validation.IsValid ||
                    attribute.ApplicationSyntaxReference?.GetSyntax() is not { } syntax)
                {
                    return new([], ContractBindingFailure.InvalidClosedAttribute);
                }
                var value = getValue();
                if (value == null)
                {
                    return new([], ContractBindingFailure.InvalidClosedAttribute);
                }
                var condition = validation.CreateTotalPredicate(factory, value, factory.GetTypeInfo(value.Type));
                if (condition == null)
                {
                    return new([], ContractBindingFailure.UnsupportedExpression);
                }
                var safeCondition = factory.Boolean(true);
                var sourceOperation = createOperation(syntax);
                clauses = clauses.Add(new(kind, condition, safeCondition, sourceOperation,
                    FormatDiagnosticText(attribute, validation, valueName), syntax.GetReference()));
            }
            else
            {
                if (!validation.IsValid)
                {
                    return new([], ContractBindingFailure.InvalidClosedAttribute);
                }
                var value = getValue();
                if (value == null || !validation.SupportsLegacyValue(factory, value))
                {
                    return new([], ContractBindingFailure.InvalidClosedAttribute);
                }
                var diagnosticText = FormatDiagnosticText(attribute, validation, valueName);
                var condition = validation.CreateLegacyPredicate(factory, value);
                clauses = clauses.Add(new(kind, condition, null, createOperation(null), diagnosticText,
                    attribute.ApplicationSyntaxReference));
            }
        }
        return new(clauses.ToImmutableArray(), ContractBindingFailure.None);
    }

    // Projects compiler-owned parameter annotations onto caller-owned IR values.
    // Reference image authentication and target enrollment remain with callers.
    internal static TotalMetadataRequiresBindingResult BindMetadataValueSites(
        IrFactory factory,
        ContractSelectionInventory symbols,
        IEnumerable<ContractMetadataValueSite> sites,
        CancellationToken cancellationToken)
    {
        factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        symbols = ArgumentNullGuard.NotNull(symbols, nameof(symbols));
        sites = ArgumentNullGuard.NotNull(sites, nameof(sites));
        var clauses = ImmutableList<BoundTotalMetadataRequires>.Empty;
        foreach (var site in sites)
        {
            var parameter = site.Parameter;
            var type = parameter.Type;
            var refKind = parameter.RefKind;
            var attributes = parameter.GetAttributes();
            cancellationToken.ThrowIfCancellationRequested();
            var kinds = ImmutableHashSet<ClosedContractAttributeKind>.Empty;
            foreach (var attribute in attributes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (symbols.IsRejectedClosedContract(attribute))
                { return new([], ContractBindingFailure.InvalidClosedAttribute); }
                var validation = ClosedContractAttributeValidator.Validate(attribute, type, refKind,
                    symbols, includeUnsigned64: true);
                if (!validation.IsRecognized)
                { continue; }
                var constructor = attribute.AttributeConstructor;
                var expectedArguments = validation.Kind == ClosedContractAttributeKind.InRange ? 2 : 0;
                if (!validation.IsValid || kinds.Contains(validation.Kind))
                { return new([], ContractBindingFailure.InvalidClosedAttribute); }
                kinds = kinds.Add(validation.Kind);
                if (constructor == null ||
                    constructor.MethodKind != MethodKind.Constructor || constructor.IsStatic ||
                    !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, attribute.AttributeClass) ||
                    constructor.Parameters.Length != expectedArguments ||
                    constructor.Parameters.Any(static constructorParameter => constructorParameter is null ? throw new System.ArgumentNullException(nameof(constructorParameter)) : (constructorParameter.RefKind != RefKind.None ||
                        constructorParameter.Type.SpecialType != SpecialType.System_Int64)) ||
                    attribute.ConstructorArguments.Length != expectedArguments || attribute.NamedArguments.Length != 0 ||
                    attribute.ConstructorArguments.Any(static argument => argument.Kind == TypedConstantKind.Error))
                { return new([], ContractBindingFailure.InvalidClosedAttribute); }
                var value = site.CreateValue();
                var condition = validation.CreateTotalPredicate(factory, value, factory.GetTypeInfo(value.Type));
                if (condition == null)
                { return new([], ContractBindingFailure.UnsupportedExpression); }
                clauses = clauses.Add(new(parameter.Ordinal, attribute, validation, condition, factory.Boolean(true)));
            }
        }
        return new(clauses.ToImmutableArray(), ContractBindingFailure.None);
    }

    internal static string FormatDiagnosticText(
        AttributeData attribute,
        ClosedContractAttributeValidation validation,
        string valueName)
    {
        RepositoryParameterGuard.RequireInitialized(!validation.IsRecognized, nameof(validation), "Value must be initialized.");

        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax();
        if (syntax != null)
        {
            if ((long)syntax.Span.Length + valueName.Length + 3 > MaximumDiagnosticConditionLength)
            {
                return "[condition exceeds the display limit]";
            }
            return "[" + syntax + "] " + valueName;
        }
        var attributeText = validation.Kind == ClosedContractAttributeKind.InRange
            ? "[InRange(" + validation.Minimum.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " +
                validation.Maximum.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")]"
            : validation.AttributeName;
        return (long)attributeText.Length + valueName.Length + 1 > MaximumDiagnosticConditionLength
            ? "[condition exceeds the display limit]"
            : attributeText + " " + valueName;
    }
}

internal readonly struct BoundClosedAttributeValue(
    BoundContractKind kind,
    IrTerm condition,
    IrTerm? safeCondition,
    OperationId sourceOperation,
    string diagnosticText,
    SyntaxReference? sourceSyntax)
{
    internal BoundContractKind Kind { get; } = kind;
    internal IrTerm Condition { get; } = (condition ?? throw new System.ArgumentNullException(nameof(condition)));
    internal IrTerm? SafeCondition { get; } = safeCondition;
    internal OperationId SourceOperation { get; } = (sourceOperation.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(sourceOperation)) : sourceOperation);
    internal string DiagnosticText { get; } = (diagnosticText ?? throw new System.ArgumentNullException(nameof(diagnosticText)));
    internal SyntaxReference? SourceSyntax { get; } = sourceSyntax;
}

internal readonly struct ClosedContractAttributeBindingResult(
    ImmutableArray<BoundClosedAttributeValue> clauses,
    ContractBindingFailure failure)
{
    internal ImmutableArray<BoundClosedAttributeValue> Clauses { get; } = (clauses.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(clauses)) : clauses);
    internal ContractBindingFailure Failure { get; } = failure;
}

internal readonly struct ContractMetadataValueSite(
    IParameterSymbol parameter,
    Func<IrTerm> createValue)
{
    internal IParameterSymbol Parameter { get; } =
        ArgumentNullGuard.NotNull(parameter, nameof(parameter));
    internal Func<IrTerm> CreateValue { get; } =
        ArgumentNullGuard.NotNull(createValue, nameof(createValue));
}
