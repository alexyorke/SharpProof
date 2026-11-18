namespace SharpProof.Frontend;

// The part kinds of an admitted interpolated string converted to string.
internal enum InterpolationPart
{
    Text,
    String,
    Character,
    Boolean,
    Integer,
    Reference
}

internal static partial class CSharpOperationSemantics
{
    // An interpolated string whose type is string, with no handler or
    // FormattableString conversion, and whose holes have no alignment or
    // format. Each hole is a string, char, bool, built-in integer or a
    // reference of a class, interface, array or delegate type. Default when
    // any part is outside these shapes.
    internal static ImmutableArray<InterpolationPart> InterpolationParts(IInterpolatedStringOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        if (operation.Type?.SpecialType != SpecialType.System_String ||
            operation.Parent is IInterpolatedStringHandlerCreationOperation ||
            operation.Parent is IConversionOperation conversion &&
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetConversion(conversion).IsInterpolatedString)
        { return default; }
        var parts = ImmutableArray.CreateBuilder<InterpolationPart>(operation.Parts.Length);
        foreach (var part in operation.Parts)
        {
            if (part is IInterpolatedStringTextOperation
                {
                    Text: { Type.SpecialType: SpecialType.System_String, ConstantValue: { HasValue: true, Value: string text } }
                } && Utf16WellFormedness.IsWellFormed(text))
            {
                parts.Add(InterpolationPart.Text);
                continue;
            }
            if (part is not IInterpolationOperation { Alignment: null, FormatString: null, Expression.Type: { } type } ||
                HoleKind(type) is not { } kind)
            { return default; }
            parts.Add(kind);
        }
        return parts.MoveToImmutable();
    }

    private static InterpolationPart? HoleKind(ITypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        switch (type.SpecialType)
        {
            case SpecialType.System_String:
                return InterpolationPart.String;
            case SpecialType.System_Char:
                return InterpolationPart.Character;
            case SpecialType.System_Boolean:
                return InterpolationPart.Boolean;
            case SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64:
                return InterpolationPart.Integer;
            default:
                break;
        }
        return type is not ITypeParameterSymbol && type.IsReferenceType &&
            type.TypeKind is TypeKind.Class or TypeKind.Interface or TypeKind.Array or TypeKind.Delegate
            ? InterpolationPart.Reference : null;
    }
}
