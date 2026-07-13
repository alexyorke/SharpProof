using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class PropertyReferencePurityRule
{
    private static bool IsArrayLengthProperty(IPropertyReferenceOperation propertyReferenceOperation)
    {
        var propertySymbol = propertyReferenceOperation.Property;
        return propertySymbol.Name == "Length" &&
               propertySymbol.IsReadOnly &&
               propertySymbol.ContainingType?.SpecialType == SpecialType.System_Array;
    }

    private static bool TryCheckFormattableStringFormatPurity(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var propertySymbol = propertyReferenceOperation.Property;
        if (propertySymbol.Name != "Format" ||
            propertySymbol.IsIndexer)
            return false;

        var formattableStringType = context.SemanticModel.Compilation.GetTypeByMetadataName("System.FormattableString");
        if (formattableStringType == null ||
            !SymbolEqualityComparer.Default.Equals(propertySymbol.ContainingType?.OriginalDefinition,
                formattableStringType))
            return false;

        return true;
    }

    private static bool IsCompilerGeneratedArrayForeachCurrent(
        IPropertyReferenceOperation propertyReferenceOperation,
        PurityAnalysisContext context)
    {
        if (propertyReferenceOperation.Property.Name != "Current" ||
            propertyReferenceOperation.Property.ContainingType?.ToDisplayString() != "System.Collections.IEnumerator" ||
            propertyReferenceOperation.Syntax.Parent is not ForEachStatementSyntax forEachStatement)
            return false;

        return ModelExtensions
            .GetTypeInfo(context.SemanticModel, forEachStatement.Expression, context.CancellationToken)
            .Type is IArrayTypeSymbol;
    }
}
