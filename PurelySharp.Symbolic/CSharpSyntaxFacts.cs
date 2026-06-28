using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PurelySharp.Symbolic
{
    internal static class CSharpSyntaxFacts
    {
        public static bool IsNestedCallableBoundary(Microsoft.CodeAnalysis.SyntaxNode node)
        {
            return node is AnonymousFunctionExpressionSyntax ||
                node is LocalFunctionStatementSyntax;
        }
    }
}
