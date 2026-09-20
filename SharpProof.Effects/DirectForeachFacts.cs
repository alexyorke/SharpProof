using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class DirectForeachFacts
{
    internal static bool IsArrayOrString(
        SemanticModel model,
        CommonForEachStatementSyntax syntax)
    {
        var type = model.GetTypeInfo(syntax.Expression).Type;
        if (type is not IArrayTypeSymbol { Rank: 1 } &&
            type?.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        // The direct lowering is exact only when assigning the element to the
        // loop variable needs no conversion. Boxing, casts, and user-defined
        // conversions retain the conservative enumerator path.
        return model.GetForEachStatementInfo(syntax).ElementConversion.IsIdentity;
    }

    internal static IOperation GetCollectionValue(
        IOperation collection)
    {
        return collection is IConversionOperation conversion &&
            conversion.IsImplicit && conversion.Operand is { } operand
                ? operand
                : collection;
    }
}
