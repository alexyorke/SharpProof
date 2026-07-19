namespace SharpProof.Analyzer;

internal enum ComplexityGrowthClass
{
    Constant,
    Logarithmic,
    Linear,
    Linearithmic,
    Quadratic,
    Product,
    Max
}

internal static class ComplexityContractFacts
{
    internal static bool TryMap(
        SymbolicComplexityKind kind,
        out ComplexityGrowthClass complexityClass)
    {
        switch (kind)
        {
            case SymbolicComplexityKind.Constant:
                complexityClass = ComplexityGrowthClass.Constant;
                return true;
            case SymbolicComplexityKind.Linear:
                complexityClass = ComplexityGrowthClass.Linear;
                return true;
            case SymbolicComplexityKind.Quadratic:
                complexityClass = ComplexityGrowthClass.Quadratic;
                return true;
            case SymbolicComplexityKind.Product:
                complexityClass = ComplexityGrowthClass.Product;
                return true;
            case SymbolicComplexityKind.Max:
                complexityClass = ComplexityGrowthClass.Max;
                return true;
            default:
                complexityClass = default;
                return false;
        }
    }

    internal static bool TryGetAttributeKindName(SymbolicComplexityKind kind, out string name)
    {
        if (TryMap(kind, out var complexityClass))
        {
            name = complexityClass.ToString();
            return true;
        }

        name = string.Empty;
        return false;
    }
}
