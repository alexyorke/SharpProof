using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicRuntimeExceptionFacts
    {
        internal static ITypeSymbol? GetThrownExceptionType(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool stopAtUntypedCatch)
        {
            ExpressionSyntax? exceptionExpression = throwNode switch
            {
                ThrowStatementSyntax statement => statement.Expression,
                ThrowExpressionSyntax expression => expression.Expression,
                _ => null
            };

            if (exceptionExpression == null)
            {
                return GetRethrownExceptionType(throwNode, semanticModel, cancellationToken, stopAtUntypedCatch);
            }

            var typeInfo = semanticModel.GetTypeInfo(exceptionExpression, cancellationToken);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        private static ITypeSymbol? GetRethrownExceptionType(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool stopAtUntypedCatch)
        {
            foreach (var catchClause in throwNode.Ancestors().OfType<CatchClauseSyntax>())
            {
                if (!catchClause.Block.Span.Contains(throwNode.SpanStart))
                {
                    continue;
                }

                if (catchClause.Declaration == null)
                {
                    if (stopAtUntypedCatch)
                    {
                        return null;
                    }

                    continue;
                }

                return semanticModel.GetTypeInfo(catchClause.Declaration.Type, cancellationToken).Type;
            }

            return null;
        }
    }
}
