using Microsoft.CodeAnalysis;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static bool TryLowerBigIntegerStaticValueMember(ISymbol? memberSymbol, out SymbolicTerm term)
    {
        if (memberSymbol is IPropertySymbol property &&
            IsBigIntegerType(property.Type))
        {
            if (string.Equals(property.Name, "Zero", StringComparison.Ordinal))
            {
                term = new SymbolicIntegerConstantTerm(0);
                return true;
            }

            if (string.Equals(property.Name, "One", StringComparison.Ordinal))
            {
                term = new SymbolicIntegerConstantTerm(1);
                return true;
            }

            if (string.Equals(property.Name, "MinusOne", StringComparison.Ordinal))
            {
                term = new SymbolicIntegerConstantTerm(-1);
                return true;
            }
        }

        term = null!;
        return false;
    }

    private static bool IsBigIntegerType(ITypeSymbol type)
    {
        return string.Equals(type.ContainingNamespace?.ToDisplayString(), "System.Numerics",
                   StringComparison.Ordinal) &&
               string.Equals(type.Name, "BigInteger", StringComparison.Ordinal);
    }
}