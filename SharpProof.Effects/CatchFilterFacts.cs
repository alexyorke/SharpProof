using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class CatchFilterFacts
{
    internal static bool? GetConstantSelection(
        CatchFilterClauseSyntax filter,
        SemanticModel model)
    {
        return model.GetConstantValue(filter.FilterExpression) switch
        {
            { HasValue: true, Value: bool value } => value,
            _ => null
        };
    }
}
