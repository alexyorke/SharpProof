using System;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Analyzer
{
    internal static class AnalyzerSyntaxHelpers
    {
        internal static bool MatchesAttribute(
            AttributeData attribute,
            INamedTypeSymbol? expectedSymbol,
            string attributeTypeName)
        {
            var attributeClass = attribute.AttributeClass;
            return attributeClass != null &&
                ((expectedSymbol != null &&
                  SymbolEqualityComparer.Default.Equals(attributeClass.OriginalDefinition, expectedSymbol)) ||
                 string.Equals(attributeClass.Name, attributeTypeName, StringComparison.Ordinal));
        }

        internal static Location GetCallableDeclarationLocation(SyntaxNode node)
        {
            return node switch
            {
                MethodDeclarationSyntax methodDeclaration => methodDeclaration.Identifier.GetLocation(),
                LocalFunctionStatementSyntax localFunctionStatement => localFunctionStatement.Identifier.GetLocation(),
                ConstructorDeclarationSyntax constructorDeclaration => constructorDeclaration.Identifier.GetLocation(),
                AccessorDeclarationSyntax accessorDeclaration => accessorDeclaration.Keyword.GetLocation(),
                OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.OperatorToken.GetLocation(),
                ConversionOperatorDeclarationSyntax conversionOperatorDeclaration => conversionOperatorDeclaration.Type.GetLocation(),
                _ => node.GetLocation(),
            };
        }

        internal static Location GetCallableDeclarationLocation(
            IMethodSymbol methodSymbol,
            CancellationToken cancellationToken)
        {
            var syntaxReference = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            return syntaxReference == null
                ? methodSymbol.Locations.First()
                : GetCallableDeclarationLocation(syntaxReference.GetSyntax(cancellationToken));
        }
    }
}
