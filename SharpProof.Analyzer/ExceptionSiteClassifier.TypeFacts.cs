namespace SharpProof.Analyzer;

internal static partial class ExceptionSiteClassifier
{
    internal static bool IsReferenceType(ITypeSymbol? typeSymbol) =>
        SymbolicTypeFacts.IsReferenceType(typeSymbol);
}
