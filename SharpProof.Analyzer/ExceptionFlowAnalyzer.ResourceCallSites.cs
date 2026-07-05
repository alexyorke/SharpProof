using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Analyzer
{
    internal static partial class ExceptionFlowAnalyzer
    {
        private static IEnumerable<MethodCallCandidate> GetUsingDisposeNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var usingStatement in GetRelevantDescendants<UsingStatementSyntax>(methodNode))
            {
                foreach (var resource in GetUsingStatementResources(usingStatement, semanticModel, cancellationToken))
                {
                    foreach (var disposeMethod in GetDisposableMethods(
                                 resource.Type,
                                 includeAsyncDispose: usingStatement.AwaitKeyword.RawKind != 0))
                    {
                        yield return new MethodCallCandidate(
                            usingStatement,
                            disposeMethod,
                            CreateUsingDisposeGuard(resource.Expression, resource.Type));
                    }
                }
            }

            foreach (var usingDeclaration in GetRelevantDescendants<LocalDeclarationStatementSyntax>(methodNode)
                         .Where(statement => !statement.UsingKeyword.IsKind(SyntaxKind.None)))
            {
                foreach (var variable in usingDeclaration.Declaration.Variables)
                {
                    var resourceType = GetUsingDeclarationVariableType(variable, semanticModel, cancellationToken);
                    if (resourceType == null)
                    {
                        continue;
                    }

                    foreach (var disposeMethod in GetDisposableMethods(
                                 resourceType,
                                 includeAsyncDispose: usingDeclaration.AwaitKeyword.RawKind != 0))
                    {
                        yield return new MethodCallCandidate(
                            usingDeclaration,
                            disposeMethod,
                            CreateUsingDisposeGuard(
                                variable.Initializer?.Value,
                                resourceType));
                    }
                }
            }
        }

        private static IEnumerable<UsingResource> GetUsingStatementResources(
            UsingStatementSyntax usingStatement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (usingStatement.Expression != null)
            {
                var expressionType = semanticModel.GetTypeInfo(usingStatement.Expression, cancellationToken).ConvertedType;
                if (expressionType != null)
                {
                    yield return new UsingResource(usingStatement.Expression, expressionType);
                }

                yield break;
            }

            if (usingStatement.Declaration == null)
            {
                yield break;
            }

            foreach (var variable in usingStatement.Declaration.Variables)
            {
                var type = GetUsingDeclarationVariableType(variable, semanticModel, cancellationToken);
                if (type != null)
                {
                    yield return new UsingResource(variable.Initializer?.Value, type);
                }
            }
        }

        private static ITypeSymbol? GetUsingDeclarationVariableType(
            VariableDeclaratorSyntax variable,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is ILocalSymbol localSymbol)
            {
                return localSymbol.Type;
            }

            return variable.Initializer == null
                ? null
                : semanticModel.GetTypeInfo(variable.Initializer.Value, cancellationToken).ConvertedType;
        }

        private static UsingDisposeGuard? CreateUsingDisposeGuard(
            ExpressionSyntax? resourceExpression,
            ITypeSymbol resourceType)
        {
            return resourceExpression != null && IsReferenceType(resourceType)
                ? new UsingDisposeGuard(resourceExpression)
                : null;
        }

        private readonly struct UsingResource
        {
            public UsingResource(ExpressionSyntax? expression, ITypeSymbol type)
            {
                Expression = expression;
                Type = type;
            }

            public ExpressionSyntax? Expression { get; }

            public ITypeSymbol Type { get; }
        }

        private static IEnumerable<MethodCallCandidate> GetForEachRuntimeMethodNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var forEachStatement in GetRelevantDescendants<ForEachStatementSyntax>(methodNode))
            {
                var collectionType = semanticModel.GetTypeInfo(forEachStatement.Expression, cancellationToken).ConvertedType;
                if (collectionType == null)
                {
                    continue;
                }

                var enumeratorMethod = FindGetEnumeratorMethod(collectionType);
                if (enumeratorMethod == null)
                {
                    continue;
                }

                yield return new MethodCallCandidate(forEachStatement.Expression, enumeratorMethod);

                var enumeratorType = enumeratorMethod.ReturnType;
                if (FindParameterlessMethod(enumeratorType, "MoveNext") is { } moveNextMethod)
                {
                    yield return new MethodCallCandidate(forEachStatement, moveNextMethod);
                }

                if (FindPropertyGetter(enumeratorType, "Current") is { } currentGetter)
                {
                    yield return new MethodCallCandidate(forEachStatement, currentGetter);
                }

                foreach (var disposeMethod in GetDisposableMethods(
                             enumeratorType,
                             includeAsyncDispose: forEachStatement.AwaitKeyword.RawKind != 0))
                {
                    yield return new MethodCallCandidate(forEachStatement, disposeMethod);
                }
            }
        }

        private static IMethodSymbol? FindGetEnumeratorMethod(ITypeSymbol collectionType)
        {
            return collectionType
                .GetMembers("GetEnumerator")
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method => method.Parameters.Length == 0);
        }

        private static IMethodSymbol? FindParameterlessMethod(ITypeSymbol typeSymbol, string methodName)
        {
            return typeSymbol
                .GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .FirstOrDefault(method => method.Parameters.Length == 0);
        }

        private static IMethodSymbol? FindPropertyGetter(ITypeSymbol typeSymbol, string propertyName)
        {
            return typeSymbol
                .GetMembers(propertyName)
                .OfType<IPropertySymbol>()
                .Select(property => property.GetMethod)
                .FirstOrDefault(method => method != null);
        }

        private static IEnumerable<IMethodSymbol> GetDisposableMethods(ITypeSymbol typeSymbol, bool includeAsyncDispose)
        {
            foreach (var method in typeSymbol
                         .GetMembers("Dispose")
                         .OfType<IMethodSymbol>()
                         .Where(candidate => candidate.Parameters.Length == 0))
            {
                yield return method;
            }

            if (!includeAsyncDispose)
            {
                yield break;
            }

            foreach (var method in typeSymbol
                         .GetMembers("DisposeAsync")
                         .OfType<IMethodSymbol>()
                         .Where(candidate => candidate.Parameters.Length == 0))
            {
                yield return method;
            }
        }
    }
}
