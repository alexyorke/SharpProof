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
        var clauses = ImmutableArray.CreateBuilder<BoundClosedAttributeValue>();
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
                clauses.Add(new(kind, condition, safeCondition, sourceOperation,
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
                clauses.Add(new(kind, condition, null, createOperation(null), diagnosticText,
                    attribute.ApplicationSyntaxReference));
            }
        }
        return new(clauses.ToImmutable(), ContractBindingFailure.None);
    }

    internal static string FormatDiagnosticText(
        AttributeData attribute,
        ClosedContractAttributeValidation validation,
        string valueName)
    {
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
    internal IrTerm Condition { get; } = condition;
    internal IrTerm? SafeCondition { get; } = safeCondition;
    internal OperationId SourceOperation { get; } = sourceOperation;
    internal string DiagnosticText { get; } = diagnosticText;
    internal SyntaxReference? SourceSyntax { get; } = sourceSyntax;
}

internal readonly struct ClosedContractAttributeBindingResult(
    ImmutableArray<BoundClosedAttributeValue> clauses,
    ContractBindingFailure failure)
{
    internal ImmutableArray<BoundClosedAttributeValue> Clauses { get; } = clauses;
    internal ContractBindingFailure Failure { get; } = failure;
}
