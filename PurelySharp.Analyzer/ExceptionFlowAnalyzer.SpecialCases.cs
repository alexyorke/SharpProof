using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PurelySharp.Analyzer
{
    internal static partial class ExceptionFlowAnalyzer
    {
        private static IEnumerable<MethodCallCandidate> GetLocalDelegateTargetInvocationNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var knownTargets = new Dictionary<ISymbol, IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var node in GetRelevantDescendantsAndSelf<SyntaxNode>(methodNode))
            {
                UpdateKnownDelegateTargets(node, semanticModel, cancellationToken, knownTargets);
                if (node is not InvocationExpressionSyntax invocation)
                {
                    continue;
                }

                if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol directMethod &&
                    directMethod.MethodKind != MethodKind.DelegateInvoke)
                {
                    continue;
                }

                if (TryResolveDelegateTarget(invocation, semanticModel, cancellationToken, knownTargets, out var targetMethod))
                {
                    yield return new MethodCallCandidate(invocation, targetMethod);
                }
            }
        }

        private static void UpdateKnownDelegateTargets(
            SyntaxNode node,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IDictionary<ISymbol, IMethodSymbol> knownTargets)
        {
            if (node is LocalDeclarationStatementSyntax localDeclaration)
            {
                foreach (var variable in localDeclaration.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not ILocalSymbol localSymbol)
                    {
                        continue;
                    }

                    if (variable.Initializer?.Value is ExpressionSyntax initializer &&
                        semanticModel.GetSymbolInfo(initializer, cancellationToken).Symbol is IMethodSymbol targetMethod)
                    {
                        knownTargets[localSymbol.OriginalDefinition] = targetMethod;
                    }
                }
            }
            else if (node is AssignmentExpressionSyntax assignment &&
                     TryGetInvokedLocalSymbol(assignment.Left, semanticModel, cancellationToken, out var localSymbol))
            {
                if (assignment.Right is ExpressionSyntax rightExpression &&
                    semanticModel.GetSymbolInfo(rightExpression, cancellationToken).Symbol is IMethodSymbol targetMethod)
                {
                    knownTargets[localSymbol] = targetMethod;
                }
                else
                {
                    knownTargets.Remove(localSymbol);
                }
            }
        }

        private static bool TryResolveDelegateTarget(
            InvocationExpressionSyntax invocation,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IReadOnlyDictionary<ISymbol, IMethodSymbol> knownTargets,
            out IMethodSymbol? targetMethod)
        {
            targetMethod = null;
            if (!TryGetInvokedLocalSymbol(invocation.Expression, semanticModel, cancellationToken, out var localSymbol))
            {
                return false;
            }

            return knownTargets.TryGetValue(localSymbol, out targetMethod);
        }

        private static bool TryGetInvokedLocalSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ISymbol? localSymbol)
        {
            localSymbol = null;
            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            if (symbol is not ILocalSymbol and not IParameterSymbol)
            {
                return false;
            }

            localSymbol = symbol.OriginalDefinition;
            return true;
        }

        private static IEnumerable<MethodCallCandidate> GetInterpolatedStringHandlerConstructorNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var interpolatedString in GetRelevantDescendants<InterpolatedStringExpressionSyntax>(methodNode))
            {
                var typeInfo = semanticModel.GetTypeInfo(interpolatedString, cancellationToken);
                var handlerType = typeInfo.ConvertedType ?? typeInfo.Type;
                if (handlerType == null || !HasInterpolatedStringHandlerAttribute(handlerType))
                {
                    continue;
                }

                var constructor = FindInterpolatedStringHandlerConstructor(handlerType);
                if (constructor == null)
                {
                    continue;
                }

                yield return new MethodCallCandidate(interpolatedString, constructor);
            }
        }

        private static bool HasInterpolatedStringHandlerAttribute(ITypeSymbol typeSymbol)
        {
            return typeSymbol.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Runtime.CompilerServices.InterpolatedStringHandlerAttribute");
        }

        private static IMethodSymbol? FindInterpolatedStringHandlerConstructor(ITypeSymbol typeSymbol)
        {
            return typeSymbol
                .GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => method.MethodKind == MethodKind.Constructor)
                .OrderBy(method => method.Parameters.Length)
                .FirstOrDefault();
        }
    }
}
