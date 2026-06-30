using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Analyzer.Engine;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Analyzer
{
    internal static partial class ExceptionFlowAnalyzer
    {
        internal static IEnumerable<SyntaxNode> GetThrowNodes(SyntaxNode methodNode)
        {
            return GetRelevantDescendants<SyntaxNode>(methodNode)
                .Where(node => node is ThrowStatementSyntax || node is ThrowExpressionSyntax);
        }

        internal static IEnumerable<BinaryExpressionSyntax> GetDefiniteDivideByZeroNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var binaryExpression in GetRelevantDescendants<BinaryExpressionSyntax>(methodNode))
            {
                if (!binaryExpression.IsKind(SyntaxKind.DivideExpression) &&
                    !binaryExpression.IsKind(SyntaxKind.ModuloExpression))
                {
                    continue;
                }

                var rightType = semanticModel.GetTypeInfo(binaryExpression.Right, cancellationToken).ConvertedType;
                if (!IsThrowingDivideByZeroType(rightType))
                {
                    continue;
                }

                if (IsDefinitelyZeroExpression(binaryExpression.Right, binaryExpression, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(binaryExpression, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return binaryExpression;
                }
            }
        }

        internal static IEnumerable<SyntaxNode> GetDefiniteNullDereferenceNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            {
                if (node is MemberAccessExpressionSyntax memberAccess &&
                    IsDefinitelyNullExpression(memberAccess.Expression, memberAccess, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(memberAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return memberAccess;
                }
                else if (node is ElementAccessExpressionSyntax elementAccess &&
                    IsDefinitelyNullExpression(elementAccess.Expression, elementAccess, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(elementAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return elementAccess;
                }
                else if (node is InvocationExpressionSyntax invocation &&
                    IsDefinitelyNullExpression(invocation.Expression, invocation, semanticModel, cancellationToken, smtAnalysis) &&
                    IsExceptionPathReachable(invocation, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return invocation;
                }
            }
        }

        internal static IEnumerable<ElementAccessExpressionSyntax> GetDefiniteIndexOutOfRangeNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var elementAccess in GetRelevantDescendants<ElementAccessExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyOutOfRangeBuiltInIndexAccess(elementAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return elementAccess;
                }
            }
        }

        internal static IEnumerable<ElementAccessExpressionSyntax> GetDefiniteArgumentOutOfRangeNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            foreach (var elementAccess in GetRelevantDescendants<ElementAccessExpressionSyntax>(methodNode))
            {
                if (IsDefinitelyOutOfRangeBuiltInRangeAccess(elementAccess, semanticModel, cancellationToken, smtAnalysis))
                {
                    yield return elementAccess;
                }
            }
        }

        internal static ITypeSymbol? GetThrownExceptionType(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            ExpressionSyntax? exceptionExpression = throwNode switch
            {
                ThrowStatementSyntax statement => statement.Expression,
                ThrowExpressionSyntax expression => expression.Expression,
                _ => null
            };

            if (exceptionExpression == null)
            {
                return throwNode is ThrowStatementSyntax statement
                    ? GetRethrownExceptionType(statement, semanticModel, cancellationToken)
                    : null;
            }

            var typeInfo = semanticModel.GetTypeInfo(exceptionExpression, cancellationToken);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        internal static bool IsShadowedByDefinitelyThrowingFinally(SyntaxNode site)
        {
            foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>())
            {
                if (!tryStatement.Span.Contains(site.SpanStart))
                {
                    continue;
                }

                if (tryStatement.Finally == null ||
                    !StatementDefinitelyExits(tryStatement.Finally.Block))
                {
                    continue;
                }

                if (tryStatement.Finally.Block.Span.Contains(site.SpanStart))
                {
                    continue;
                }

                if (tryStatement.Block.Span.Contains(site.SpanStart) ||
                    tryStatement.Catches.Any(catchClause => catchClause.Block.Span.Contains(site.SpanStart)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsIntegralOrDecimalZero(object? value)
        {
            switch (value)
            {
                case byte byteValue:
                    return byteValue == 0;
                case sbyte sbyteValue:
                    return sbyteValue == 0;
                case short shortValue:
                    return shortValue == 0;
                case ushort ushortValue:
                    return ushortValue == 0;
                case int intValue:
                    return intValue == 0;
                case uint uintValue:
                    return uintValue == 0;
                case long longValue:
                    return longValue == 0L;
                case ulong ulongValue:
                    return ulongValue == 0UL;
                case decimal decimalValue:
                    return decimalValue == 0m;
                default:
                    return false;
            }
        }

        private static bool IsThrowingDivideByZeroType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            switch (typeSymbol.SpecialType)
            {
                case SpecialType.System_Byte:
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Decimal:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsDefinitelyZeroExpression(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            return (constantValue.HasValue && IsIntegralOrDecimalZero(constantValue.Value)) ||
                IsKnownByPriorAssignment(expression, useNode, semanticModel, cancellationToken, PathFactKind.Zero) ||
                IsKnownByDominatingIf(expression, useNode, semanticModel, cancellationToken, PathFactKind.Zero, smtAnalysis);
        }

        private static bool IsDefinitelyNullExpression(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is CastExpressionSyntax castExpression)
                {
                    if (IsDefinitelyNullExpression(castExpression.Expression, useNode, semanticModel, cancellationToken, smtAnalysis))
                    {
                        var castType = semanticModel.GetTypeInfo(castExpression, cancellationToken).Type;
                        return IsReferenceType(castType);
                    }

                    return false;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                break;
            }

            if (expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return true;
            }

            if (expression is DefaultExpressionSyntax defaultExpression)
            {
                var defaultType = semanticModel.GetTypeInfo(defaultExpression, cancellationToken).Type;
                return IsReferenceType(defaultType);
            }

            return IsKnownByPriorAssignment(expression, useNode, semanticModel, cancellationToken, PathFactKind.Null) ||
                IsKnownByDominatingIf(expression, useNode, semanticModel, cancellationToken, PathFactKind.Null, smtAnalysis);
        }

        private static bool IsDefinitelyOutOfRangeBuiltInIndexAccess(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken) ||
                IsBuiltInRangeAccessArgument(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken))
            {
                return false;
            }

            if (!TryTranslateBuiltInElementAccessInRangeForExceptionFlow(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(elementAccess, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool IsDefinitelyOutOfRangeBuiltInRangeAccess(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            if (!IsBuiltInSequenceElementAccess(elementAccess, semanticModel, cancellationToken) ||
                !IsBuiltInRangeAccessArgument(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    semanticModel,
                    cancellationToken))
            {
                return false;
            }

            if (!TryTranslateBuiltInRangeAccessInRangeForExceptionFlow(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out var inRangeFormula))
            {
                return false;
            }

            return IsDefinitelyFalseAtUse(elementAccess, inRangeFormula, semanticModel, cancellationToken, smtAnalysis);
        }

        private static bool IsDefinitelyFalseAtUse(
            SyntaxNode useNode,
            SmtFormula formula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            SmtAnalysisService smtAnalysis)
        {
            var outOfRangeFormula = new SmtUnaryFormula(SmtUnaryOperator.Not, formula);

            var pathConditions = CollectPathConditionsForUse(
                useNode,
                CollectLocalAndParameterSymbols(useNode, semanticModel, cancellationToken),
                semanticModel,
                cancellationToken);

            return PathConditionsAreSatisfiable(pathConditions, smtAnalysis) &&
                PathConditionsImplyFact(pathConditions, outOfRangeFormula, smtAnalysis);
        }

        private static bool IsBuiltInSequenceElementAccess(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var argumentCount = elementAccess.ArgumentList.Arguments.Count;
            if (argumentCount == 0)
            {
                return false;
            }

            var receiverTypeInfo = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken);
            var receiverType = receiverTypeInfo.ConvertedType ?? receiverTypeInfo.Type;
            if (receiverType is IArrayTypeSymbol arrayType)
            {
                return arrayType.Rank == argumentCount;
            }

            return argumentCount == 1 &&
                (receiverType?.SpecialType == SpecialType.System_String ||
                 IsBuiltInSpanType(receiverType));
        }

        private static bool IsBuiltInSpanType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() is "System.Span<T>" or "System.ReadOnlySpan<T>";
        }

        private static bool IsBuiltInRangeAccessArgument(
            ExpressionSyntax argumentExpression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            argumentExpression = UnwrapFactExpression(argumentExpression);
            if (argumentExpression is RangeExpressionSyntax)
            {
                return true;
            }

            var typeInfo = semanticModel.GetTypeInfo(argumentExpression, cancellationToken);
            return IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type);
        }

        private static bool TryTranslateBuiltInElementAccessInRangeForExceptionFlow(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula inRangeFormula)
        {
            if (CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out inRangeFormula))
            {
                return true;
            }

            if (!CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula) ||
                lengthFormula is not { Kind: SmtValueKind.Int } ||
                !TryCreateEffectiveSystemIndexVariableFormula(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    elementAccess,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var indexFormula))
            {
                inRangeFormula = null!;
                return false;
            }

            var lowerBound = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                indexFormula,
                new SmtIntegerConstant(0));
            var upperBound = new SmtBinaryFormula(
                SmtBinaryOperator.LessThan,
                indexFormula,
                lengthFormula);
            inRangeFormula = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
            return true;
        }

        private static bool TryTranslateBuiltInRangeAccessInRangeForExceptionFlow(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula inRangeFormula)
        {
            if (CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out inRangeFormula))
            {
                return true;
            }

            if (!CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                    elementAccess.Expression,
                    semanticModel,
                    cancellationToken,
                    out var lengthFormula) ||
                lengthFormula is not { Kind: SmtValueKind.Int } ||
                !TryCreateSystemRangeVariableInRangeFormula(
                    elementAccess.ArgumentList.Arguments[0].Expression,
                    elementAccess,
                    lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out inRangeFormula))
            {
                inRangeFormula = null!;
                return false;
            }

            return true;
        }

        private static bool TryCreateSystemRangeVariableInRangeFormula(
            ExpressionSyntax rangeExpression,
            SyntaxNode useNode,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula inRangeFormula)
        {
            rangeExpression = UnwrapFactExpression(rangeExpression);
            if (!TryResolveCurrentSystemRangeValueExpression(
                    rangeExpression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out var valueExpression))
            {
                inRangeFormula = null!;
                return false;
            }

            valueExpression = UnwrapFactExpression(valueExpression);
            if (valueExpression is not RangeExpressionSyntax resolvedRange ||
                !TryCreateEffectiveRangeEndpointFormula(
                    resolvedRange.LeftOperand,
                    lengthFormula,
                    defaultWhenOmitted: new SmtIntegerConstant(0),
                    semanticModel,
                    cancellationToken,
                    out var startFormula) ||
                !TryCreateEffectiveRangeEndpointFormula(
                    resolvedRange.RightOperand,
                    lengthFormula,
                    defaultWhenOmitted: lengthFormula,
                    semanticModel,
                    cancellationToken,
                    out var endFormula))
            {
                inRangeFormula = null!;
                return false;
            }

            var nonNegativeStart = new SmtBinaryFormula(
                SmtBinaryOperator.GreaterThanOrEqual,
                startFormula,
                new SmtIntegerConstant(0));
            var orderedEndpoints = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                startFormula,
                endFormula);
            var endWithinLength = new SmtBinaryFormula(
                SmtBinaryOperator.LessThanOrEqual,
                endFormula,
                lengthFormula);
            inRangeFormula = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                nonNegativeStart,
                new SmtBinaryFormula(SmtBinaryOperator.And, orderedEndpoints, endWithinLength));
            return true;
        }

        private static bool TryCreateEffectiveRangeEndpointFormula(
            ExpressionSyntax? endpointExpression,
            SmtFormula lengthFormula,
            SmtFormula defaultWhenOmitted,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula endpointFormula)
        {
            if (endpointExpression == null)
            {
                endpointFormula = defaultWhenOmitted;
                return true;
            }

            return TryCreateEffectiveIndexExpressionFormula(
                endpointExpression,
                lengthFormula,
                semanticModel,
                cancellationToken,
                out endpointFormula);
        }

        private static bool TryCreateEffectiveSystemIndexVariableFormula(
            ExpressionSyntax indexExpression,
            SyntaxNode useNode,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula indexFormula)
        {
            indexExpression = UnwrapFactExpression(indexExpression);
            if (!TryResolveCurrentSystemIndexValueExpression(
                    indexExpression,
                    useNode,
                    semanticModel,
                    cancellationToken,
                    out var valueExpression))
            {
                indexFormula = null!;
                return false;
            }

            return TryCreateEffectiveIndexExpressionFormula(
                valueExpression,
                lengthFormula,
                semanticModel,
                cancellationToken,
                out indexFormula);
        }

        private static bool TryResolveCurrentSystemIndexValueExpression(
            ExpressionSyntax indexExpression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ExpressionSyntax valueExpression)
        {
            valueExpression = null!;
            var symbol = GetLocalOrParameterSymbol(indexExpression, semanticModel, cancellationToken);
            if (symbol == null ||
                !IsSystemIndexType(GetTrackedSymbolType(symbol)))
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

                        if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
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

                    if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
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

        private static bool TryResolveCurrentSystemRangeValueExpression(
            ExpressionSyntax rangeExpression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ExpressionSyntax valueExpression)
        {
            valueExpression = null!;
            var symbol = GetLocalOrParameterSymbol(rangeExpression, semanticModel, cancellationToken);
            if (symbol == null ||
                !IsSystemRangeType(GetTrackedSymbolType(symbol)))
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

                        if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
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

                    if (StatementMutatesSymbolExceptLinearAssignment(statement, symbol, semanticModel, cancellationToken))
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

        private static bool StatementMutatesSymbolExceptLinearAssignment(
            StatementSyntax statement,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var node in statement.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (MutatesSymbol(node, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryCreateEffectiveIndexExpressionFormula(
            ExpressionSyntax expression,
            SmtFormula lengthFormula,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out SmtFormula indexFormula)
        {
            expression = UnwrapFactExpression(expression);
            if (expression is PrefixUnaryExpressionSyntax fromEndIndex &&
                fromEndIndex.OperatorToken.IsKind(SyntaxKind.CaretToken))
            {
                if (!CSharpConditionToFormula.TryTranslateValue(
                        fromEndIndex.Operand,
                        semanticModel,
                        cancellationToken,
                        out var fromEndOffset,
                        getSymbolVersion: null) ||
                    fromEndOffset is not { Kind: SmtValueKind.Int })
                {
                    indexFormula = null!;
                    return false;
                }

                indexFormula = new SmtIntegerBinaryTerm(
                    SmtIntegerBinaryOperator.Subtract,
                    lengthFormula,
                    fromEndOffset);
                return true;
            }

            if (!CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var ordinaryIndex,
                    getSymbolVersion: null) ||
                ordinaryIndex is not { Kind: SmtValueKind.Int })
            {
                indexFormula = null!;
                return false;
            }

            indexFormula = ordinaryIndex;
            return true;
        }

        private static bool IsSystemIndexType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol
            {
                Name: "Index",
                ContainingNamespace: { } containingNamespace
            } &&
            containingNamespace.ToDisplayString() == "System";
        }

        private static bool IsSystemRangeType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol
            {
                Name: "Range",
                ContainingNamespace: { } containingNamespace
            } &&
            containingNamespace.ToDisplayString() == "System";
        }

        private static bool IsReferenceType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol == null)
            {
                return false;
            }

            if (typeSymbol is ITypeParameterSymbol typeParameter)
            {
                return IsKnownReferenceTypeParameter(
                    typeParameter,
                    new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default));
            }

            return typeSymbol.IsReferenceType;
        }

        private static bool IsKnownReferenceTypeParameter(
            ITypeParameterSymbol typeParameter,
            HashSet<ITypeParameterSymbol> visited)
        {
            if (!visited.Add(typeParameter))
            {
                return false;
            }

            if (typeParameter.HasReferenceTypeConstraint)
            {
                return true;
            }

            return typeParameter.ConstraintTypes.Any(constraint =>
                constraint.IsReferenceType ||
                constraint is ITypeParameterSymbol nestedTypeParameter &&
                IsKnownReferenceTypeParameter(nestedTypeParameter, visited));
        }

        private static ITypeSymbol? GetRethrownExceptionType(
            ThrowStatementSyntax throwStatement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var catchClause in throwStatement.Ancestors().OfType<CatchClauseSyntax>())
            {
                if (!catchClause.Block.Span.Contains(throwStatement.SpanStart))
                {
                    continue;
                }

                if (catchClause.Declaration == null)
                {
                    return null;
                }

                return semanticModel.GetTypeInfo(catchClause.Declaration.Type, cancellationToken).Type;
            }

            return null;
        }
    }
}
