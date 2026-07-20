namespace SharpProof.Analyzer.Engine.Rules;

internal static class FieldOrPropertyInitializerOperationHelper {
    internal static bool TryGetFieldOrPropertyInitializerOperation(
        IOperation? operation,
        PurityAnalysisContext context,
        out IOperation initializerOperation) {
        ISymbol? receiverSymbol = operation switch {
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            _ => null
        };

        if (receiverSymbol == null) {
            initializerOperation = null!;
            return false;
        }

        foreach (var syntaxReference in receiverSymbol.DeclaringSyntaxReferences) {
            SyntaxNode? initializerSyntax = syntaxReference.GetSyntax(context.CancellationToken) switch {
                VariableDeclaratorSyntax variableDeclarator => variableDeclarator.Initializer?.Value,
                PropertyDeclarationSyntax propertyDeclaration => propertyDeclaration.Initializer?.Value,
                _ => null
            };

            if (initializerSyntax == null) continue;

            var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(initializerSyntax.SyntaxTree);
            var operationFromInitializer = semanticModel.GetOperation(initializerSyntax, context.CancellationToken);
            if (operationFromInitializer != null) {
                initializerOperation = operationFromInitializer;
                return true;
            }
        }

        initializerOperation = null!;
        return false;
    }
}