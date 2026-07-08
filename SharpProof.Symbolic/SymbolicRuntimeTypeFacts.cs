using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic
{
    internal static class SymbolicRuntimeTypeFacts
    {
        internal static bool TryGetExactRuntimeType(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ITypeSymbol exactType,
            int inlineDepth = 0)
        {
            exactType = null!;
            if (inlineDepth > 8)
            {
                return false;
            }

            expression = UnwrapRuntimeTypeExpression(expression);
            if (TryResolveCurrentSimpleValueExpression(
                    expression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out var currentValueExpression))
            {
                return TryGetExactRuntimeType(
                    currentValueExpression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out exactType,
                    inlineDepth + 1);
            }

            var expressionType = GetNaturalExpressionType(expression, semanticModel, cancellationToken);
            if (expressionType != null && IsNonNullableValueType(expressionType))
            {
                exactType = expressionType;
                return true;
            }

            if (expressionType?.TypeKind == TypeKind.Dynamic)
            {
                return false;
            }

            if (expression is CastExpressionSyntax castExpression)
            {
                var targetType = GetExpressionType(castExpression, semanticModel, cancellationToken);
                if (targetType == null ||
                    targetType.TypeKind == TypeKind.Dynamic)
                {
                    return false;
                }

                if (SymbolicTypeFacts.IsReferenceType(targetType))
                {
                    var operandType = GetExpressionType(castExpression.Expression, semanticModel, cancellationToken);
                    if (IsNonNullableValueType(operandType) &&
                        TryGetExactRuntimeType(
                            castExpression.Expression,
                            useNode,
                            semanticModel,
                            cancellationToken,
                            out var boxedValueType,
                            inlineDepth + 1))
                    {
                        exactType = boxedValueType;
                        return true;
                    }

                    if (TryGetExactRuntimeType(
                            castExpression.Expression,
                            useNode,
                            semanticModel,
                            cancellationToken,
                            out var operandExactType,
                            inlineDepth + 1) &&
                        CanCastExactRuntimeTypeToReferenceType(
                            operandExactType,
                            targetType,
                            semanticModel.Compilation))
                    {
                        exactType = operandExactType;
                        return true;
                    }
                }

                if (IsNonNullableValueType(targetType))
                {
                    exactType = targetType;
                    return true;
                }

                return false;
            }

            if (expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or
                ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or AnonymousObjectCreationExpressionSyntax)
            {
                if (expressionType != null && !expressionType.IsAbstract)
                {
                    exactType = expressionType;
                    return true;
                }

                return false;
            }

            if (expression.IsKind(SyntaxKind.StringLiteralExpression) &&
                expressionType?.SpecialType == SpecialType.System_String)
            {
                exactType = expressionType;
                return true;
            }

            return false;
        }

        internal static ITypeSymbol? GetNaturalExpressionType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        internal static bool CanStoreExactRuntimeTypeInArrayElement(
            ITypeSymbol exactRuntimeType,
            ITypeSymbol elementType,
            Compilation compilation)
        {
            if (exactRuntimeType.TypeKind == TypeKind.Dynamic ||
                elementType.TypeKind == TypeKind.Dynamic)
            {
                return true;
            }

            return CanCastExactRuntimeTypeToReferenceType(exactRuntimeType, elementType, compilation);
        }

        internal static bool CanUnboxExactRuntimeTypeToValueType(ITypeSymbol exactRuntimeType, ITypeSymbol targetType)
        {
            if (!IsNonNullableValueType(targetType))
            {
                return false;
            }

            return SymbolEqualityComparer.Default.Equals(exactRuntimeType, targetType);
        }

        internal static bool CanCastExactRuntimeTypeToReferenceType(
            ITypeSymbol exactRuntimeType,
            ITypeSymbol targetType,
            Compilation compilation)
        {
            if (targetType.TypeKind == TypeKind.Dynamic ||
                exactRuntimeType.TypeKind == TypeKind.Dynamic)
            {
                return true;
            }

            if (SymbolicTypeFacts.IsReferenceType(targetType) &&
                targetType.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            var conversion = compilation.ClassifyCommonConversion(exactRuntimeType, targetType);
            return conversion.Exists &&
                (conversion.IsIdentity || conversion.IsImplicit);
        }

        internal static bool TryGetRuntimeTypeTestKey(ITypeSymbol? targetType, out string typeKey)
        {
            if (targetType == null ||
                targetType.TypeKind is TypeKind.Dynamic or TypeKind.Error or TypeKind.TypeParameter ||
                !targetType.IsReferenceType)
            {
                typeKey = null!;
                return false;
            }

            if (targetType.SpecialType == SpecialType.System_Object)
            {
                typeKey = "System.Object";
                return true;
            }

            if (targetType.SpecialType == SpecialType.System_String)
            {
                typeKey = "System.String";
                return true;
            }

            typeKey = targetType
                .WithNullableAnnotation(NullableAnnotation.None)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                .Replace("global::", string.Empty);
            return true;
        }

        private static bool TryResolveCurrentSimpleValueExpression(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ExpressionSyntax valueExpression)
        {
            valueExpression = null!;
            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol == null)
            {
                return false;
            }

            ExpressionSyntax? currentValue = null;
            foreach (var (block, containingStatement) in EnumerateContainingBlocks(useNode).Reverse())
            {
                foreach (var statement in block.Statements)
                {
                    if (ReferenceEquals(statement, containingStatement))
                    {
                        break;
                    }

                    if (statement is LocalDeclarationStatementSyntax localDeclaration)
                    {
                        foreach (var declarator in localDeclaration.Declaration.Variables)
                        {
                            if (semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                                SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                            {
                                currentValue = declarator.Initializer?.Value;
                            }
                        }

                        if (StatementMayMutateSymbol(statement, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                        }

                        continue;
                    }

                    if (statement is ExpressionStatementSyntax
                        {
                            Expression: AssignmentExpressionSyntax assignment
                        } &&
                        ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                    {
                        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                            ExpressionReferencesSymbol(assignment.Right, symbol, semanticModel, cancellationToken))
                        {
                            currentValue = null;
                            continue;
                        }

                        currentValue = assignment.Right;
                        continue;
                    }

                    if (StatementMayMutateSymbol(statement, symbol, semanticModel, cancellationToken))
                    {
                        currentValue = null;
                    }
                }
            }

            if (currentValue == null)
            {
                return false;
            }

            valueExpression = currentValue;
            return true;
        }

        private static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(SyntaxNode node)
        {
            for (SyntaxNode? current = node; current != null; current = current.Parent)
            {
                if (current is StatementSyntax statement &&
                    current.Parent is BlockSyntax block)
                {
                    yield return (block, statement);
                }
            }
        }

        private static bool StatementMayMutateSymbol(
            StatementSyntax statement,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var node in statement.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (NodeMutatesSymbol(node, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NodeMutatesSymbol(
            SyntaxNode node,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return node switch
            {
                AssignmentExpressionSyntax assignment =>
                    ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken) ||
                    TupleAssignmentMutatesSymbol(assignment, symbol, semanticModel, cancellationToken),
                PrefixUnaryExpressionSyntax prefixUnary
                    when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) || prefixUnary.IsKind(SyntaxKind.PreDecrementExpression) =>
                    ExpressionMatchesSymbol(prefixUnary.Operand, symbol, semanticModel, cancellationToken),
                PostfixUnaryExpressionSyntax postfixUnary
                    when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) || postfixUnary.IsKind(SyntaxKind.PostDecrementExpression) =>
                    ExpressionMatchesSymbol(postfixUnary.Operand, symbol, semanticModel, cancellationToken),
                ArgumentSyntax argument when !argument.RefKindKeyword.IsKind(SyntaxKind.None) =>
                    ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken),
                _ => false
            };
        }

        private static bool TupleAssignmentMutatesSymbol(
            AssignmentExpressionSyntax assignment,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (UnwrapRuntimeTypeExpression(assignment.Left) is not TupleExpressionSyntax leftTuple)
            {
                return false;
            }

            return leftTuple.Arguments.Any(argument =>
                ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken));
        }

        private static bool ExpressionReferencesSymbol(
            SyntaxNode root,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !CSharpSyntaxFacts.IsNestedCallableBoundary(candidate)))
            {
                if (node is ExpressionSyntax expression &&
                    ExpressionMatchesSymbol(expression, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExpressionMatchesSymbol(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var expressionSymbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            return expressionSymbol != null && SymbolEqualityComparer.Default.Equals(expressionSymbol, symbol);
        }

        private static ISymbol? GetLocalOrParameterSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = UnwrapRuntimeTypeExpression(expression);
            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol?.OriginalDefinition;
            return symbol is ILocalSymbol or IParameterSymbol
                ? symbol
                : null;
        }

        private static ITypeSymbol? GetExpressionType(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
            return typeInfo.ConvertedType ?? typeInfo.Type;
        }

        private static bool IsNonNullableValueType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol?.IsValueType == true &&
                !IsNullableType(typeSymbol);
        }

        private static bool IsNullableType(ITypeSymbol? typeSymbol)
        {
            return SymbolicTypeFacts.IsNullableType(typeSymbol);
        }

        private static ExpressionSyntax UnwrapRuntimeTypeExpression(ExpressionSyntax expression)
        {
            return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        }
    }
}
