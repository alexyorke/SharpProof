using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class ExecutableBodySyntax
{
    internal static SyntaxNode? Get(SyntaxNode declaration)
    {
        return declaration switch
        {
            BaseMethodDeclarationSyntax method =>
                (SyntaxNode?)method.Body ?? method.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor =>
                (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax local =>
                (SyntaxNode?)local.Body ?? local.ExpressionBody?.Expression,
            BlockSyntax block => block,
            _ => null
        };
    }
}
