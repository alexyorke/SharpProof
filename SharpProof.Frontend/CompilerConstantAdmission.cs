namespace SharpProof.Frontend;

internal static class CompilerConstantAdmission
{
    internal static bool IsCatalogIntegerBoundary(
        IFieldReferenceOperation operation)
    {
        var field = operation.Field;
        if (!field.IsConst || !field.IsStatic ||
            operation.ConstantValue is not { HasValue: true, Value: not null } ||
            field.Type.SpecialType != field.ContainingType?.SpecialType ||
            !CSharpScalarSemantics.TryGetInteger(
                field.Type.SpecialType,
                out var semantics))
        {
            return false;
        }

        var value = Convert.ToInt64(
            operation.ConstantValue.Value,
            CultureInfo.InvariantCulture);
        return field.Name == nameof(int.MinValue) &&
                value == semantics.Minimum ||
            field.Name == nameof(int.MaxValue) &&
                value == semantics.Maximum;
    }

    internal static bool IsLiteralIntegerNegation(IUnaryOperation operation)
    {
        return operation.OperatorKind == UnaryOperatorKind.Minus &&
            operation.Operand is ILiteralOperation &&
            operation.ConstantValue.HasValue &&
            CSharpScalarSemantics.IsSupportedInteger(
                operation.Type?.SpecialType ?? SpecialType.None);
    }
}
