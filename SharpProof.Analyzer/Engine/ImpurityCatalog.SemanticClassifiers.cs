using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;

namespace SharpProof.Analyzer.Engine;

internal static partial class ImpurityCatalog
{
    private static bool IsSemanticallyPureMathMember(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol methodSymbol ||
            !methodSymbol.IsStatic ||
            methodSymbol.MethodKind != MethodKind.Ordinary ||
            methodSymbol.ReturnsVoid ||
            methodSymbol.ReturnsByRef ||
            methodSymbol.ReturnsByRefReadonly ||
            methodSymbol.TypeArguments.Length != 0 ||
            methodSymbol.Parameters.Any(parameter => parameter.RefKind != RefKind.None))
            return false;

        var containingType = methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        if (!string.Equals(containingType, "System.Math", StringComparison.Ordinal) &&
            !string.Equals(containingType, "System.MathF", StringComparison.Ordinal))
            return false;

        if (!IsSemanticallyPureMathType(methodSymbol.ReturnType)) return false;

        return methodSymbol.Parameters.All(parameter => IsSemanticallyPureMathType(parameter.Type));
    }

    private static bool IsSemanticallyPureMathType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol.TypeKind == TypeKind.Enum) return true;

        if (typeSymbol.SpecialType != SpecialType.System_Char &&
            SymbolicTypeFacts.IsBuiltInIntegralType(typeSymbol))
            return true;

        switch (typeSymbol.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Single:
            case SpecialType.System_Double:
            case SpecialType.System_Decimal:
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
                return true;
        }

        var displayName = typeSymbol.ToDisplayString();
        return string.Equals(displayName, "System.Half", StringComparison.Ordinal) ||
               string.Equals(displayName, "System.Int128", StringComparison.Ordinal) ||
               string.Equals(displayName, "System.UInt128", StringComparison.Ordinal);
    }
}
