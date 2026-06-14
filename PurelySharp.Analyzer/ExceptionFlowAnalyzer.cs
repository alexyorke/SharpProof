using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Analyzer
{
    internal static class ExceptionFlowAnalyzer
    {
        private const string UnknownExceptionType = "unknown";

        private static readonly SymbolDisplayFormat ExceptionTypeDisplayFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        public static void AnalyzeSymbolForExceptions(
            SyntaxNodeAnalysisContext context,
            bool reportExceptions,
            ExceptionSummaryCatalog exceptionSummaryCatalog)
        {
            if (!Analyzer.Configuration.AnalyzerConfiguration.GetReportExceptions(
                    context.Options,
                    context.Node.SyntaxTree,
                    reportExceptions))
            {
                return;
            }

            if (!(context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is IMethodSymbol methodSymbol))
            {
                return;
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true)
            {
                return;
            }

            var exceptionEvidence = CollectUncaughtExceptions(
                context.Node,
                context.SemanticModel,
                context.CancellationToken,
                methodSymbol,
                exceptionSummaryCatalog,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
            if (exceptionEvidence.Count == 0)
            {
                return;
            }

            var diagnosticLocation = GetIdentifierLocation(context.Node);
            if (diagnosticLocation == null)
            {
                return;
            }

            var sortedTypes = exceptionEvidence.Types;
            var exceptionList = string.Join(", ", sortedTypes);
            var properties = ImmutableDictionary<string, string?>.Empty
                .Add(PurelySharpDiagnostics.ExceptionTypesProperty, string.Join(";", sortedTypes))
                .Add(PurelySharpDiagnostics.ExceptionCategoriesProperty, exceptionEvidence.FormatCategories())
                .Add(PurelySharpDiagnostics.ExceptionSourcesProperty, exceptionEvidence.FormatSources());

            context.ReportDiagnostic(Diagnostic.Create(
                PurelySharpDiagnostics.ExceptionSummaryRule,
                diagnosticLocation,
                additionalLocations: null,
                properties: properties,
                messageArgs: new object[] { methodSymbol.Name, exceptionList }));
        }

        private static ExceptionEvidenceSet CollectUncaughtExceptions(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            IMethodSymbol methodSymbol,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods)
        {
            visitedMethods.Add(methodSymbol.OriginalDefinition);
            var exceptionEvidence = new ExceptionEvidenceSet();
            foreach (var throwNode in GetThrowNodes(methodNode))
            {
                if (IsInStaticallyUnreachableBranch(throwNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                var exceptionType = GetThrownExceptionType(throwNode, semanticModel, cancellationToken);
                if (IsCaughtWithinMethod(throwNode, exceptionType, methodNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                exceptionEvidence.Add(
                    exceptionType?.ToDisplayString(ExceptionTypeDisplayFormat) ?? UnknownExceptionType,
                    IsRethrow(throwNode) ? "rethrow" : "direct_throw",
                    "throw");
            }

            foreach (var invocation in GetInvocationNodes(methodNode))
            {
                if (IsInStaticallyUnreachableBranch(invocation, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (!(semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol invokedMethod))
                {
                    continue;
                }

                AddUncaughtCalleeExceptions(
                    exceptionEvidence,
                    invocation,
                    invokedMethod,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    exceptionSummaryCatalog,
                    visitedMethods);
            }

            foreach (var creation in GetObjectCreationNodes(methodNode))
            {
                if (IsInStaticallyUnreachableBranch(creation, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (!(semanticModel.GetSymbolInfo(creation, cancellationToken).Symbol is IMethodSymbol constructorSymbol))
                {
                    continue;
                }

                AddUncaughtCalleeExceptions(
                    exceptionEvidence,
                    creation,
                    constructorSymbol,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    exceptionSummaryCatalog,
                    visitedMethods);
            }

            foreach (var propertyAccess in GetPropertyAccessNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(propertyAccess, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (!(semanticModel.GetSymbolInfo(propertyAccess, cancellationToken).Symbol is IPropertySymbol propertySymbol) ||
                    propertySymbol.GetMethod == null)
                {
                    continue;
                }

                AddUncaughtCalleeExceptions(
                    exceptionEvidence,
                    propertyAccess,
                    propertySymbol.GetMethod,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    exceptionSummaryCatalog,
                    visitedMethods);
            }

            foreach (var propertyWrite in GetPropertyWriteNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(propertyWrite, semanticModel, cancellationToken))
                {
                    continue;
                }

                if (!TryGetPropertySetterMethod(propertyWrite, semanticModel, cancellationToken, out var setterMethod))
                {
                    continue;
                }

                AddUncaughtCalleeExceptions(
                    exceptionEvidence,
                    propertyWrite,
                    setterMethod,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    exceptionSummaryCatalog,
                    visitedMethods);
            }

            foreach (var usingDisposeNode in GetUsingDisposeNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(usingDisposeNode.CallSite, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddUncaughtCalleeExceptions(
                    exceptionEvidence,
                    usingDisposeNode.CallSite,
                    usingDisposeNode.Method,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    exceptionSummaryCatalog,
                    visitedMethods);
            }

            foreach (var forEachRuntimeNode in GetForEachRuntimeMethodNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(forEachRuntimeNode.CallSite, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddUncaughtCalleeExceptions(
                    exceptionEvidence,
                    forEachRuntimeNode.CallSite,
                    forEachRuntimeNode.Method,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    exceptionSummaryCatalog,
                    visitedMethods);
            }

            foreach (var operatorNode in GetOperatorAndConversionNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(operatorNode.CallSite, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddUncaughtCalleeExceptions(
                    exceptionEvidence,
                    operatorNode.CallSite,
                    operatorNode.Method,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    exceptionSummaryCatalog,
                    visitedMethods);
            }

            foreach (var delegateInvocationNode in GetLocalDelegateTargetInvocationNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(delegateInvocationNode.CallSite, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddUncaughtCalleeExceptions(
                    exceptionEvidence,
                    delegateInvocationNode.CallSite,
                    delegateInvocationNode.Method,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    exceptionSummaryCatalog,
                    visitedMethods);
            }

            foreach (var handlerConstructorNode in GetInterpolatedStringHandlerConstructorNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(handlerConstructorNode.CallSite, semanticModel, cancellationToken))
                {
                    continue;
                }

                AddUncaughtCalleeExceptions(
                    exceptionEvidence,
                    handlerConstructorNode.CallSite,
                    handlerConstructorNode.Method,
                    methodNode,
                    semanticModel,
                    cancellationToken,
                    exceptionSummaryCatalog,
                    visitedMethods);
            }

            foreach (var divideByZeroNode in GetDefiniteDivideByZeroNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(divideByZeroNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                var exceptionType = semanticModel.Compilation.GetTypeByMetadataName("System.DivideByZeroException");
                if (IsCaughtWithinMethod(divideByZeroNode, exceptionType, methodNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                exceptionEvidence.Add("System.DivideByZeroException", "definite_divide_by_zero", "binary_operator");
            }

            foreach (var nullDereferenceNode in GetDefiniteNullDereferenceNodes(methodNode, semanticModel, cancellationToken))
            {
                if (IsInStaticallyUnreachableBranch(nullDereferenceNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                var exceptionType = semanticModel.Compilation.GetTypeByMetadataName("System.NullReferenceException");
                if (IsCaughtWithinMethod(nullDereferenceNode, exceptionType, methodNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                exceptionEvidence.Add("System.NullReferenceException", "definite_null_dereference", "null_receiver");
            }

            return exceptionEvidence;
        }

        private static void AddUncaughtCalleeExceptions(
            ExceptionEvidenceSet exceptionEvidence,
            SyntaxNode callSite,
            IMethodSymbol callee,
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods)
        {
            foreach (var exception in CollectCalleeExceptions(
                         callee,
                         semanticModel.Compilation,
                         cancellationToken,
                         exceptionSummaryCatalog,
                         visitedMethods))
            {
                if (IsCaughtWithinMethod(callSite, exception.Type, methodNode, semanticModel, cancellationToken))
                {
                    continue;
                }

                exceptionEvidence.Add(exception.DisplayName, exception.Category, exception.Source);
            }
        }

        private static IEnumerable<SyntaxNode> GetThrowNodes(SyntaxNode methodNode)
        {
            return methodNode.DescendantNodes(
                    descendIntoChildren: node => ReferenceEquals(node, methodNode) || !IsNestedCallableBoundary(node))
                .Where(node => node is ThrowStatementSyntax || node is ThrowExpressionSyntax);
        }

        private static IEnumerable<InvocationExpressionSyntax> GetInvocationNodes(SyntaxNode methodNode)
        {
            return methodNode.DescendantNodes(
                    descendIntoChildren: node => ReferenceEquals(node, methodNode) || !IsNestedCallableBoundary(node))
                .OfType<InvocationExpressionSyntax>();
        }

        private static IEnumerable<SyntaxNode> GetObjectCreationNodes(SyntaxNode methodNode)
        {
            return methodNode.DescendantNodes(
                    descendIntoChildren: node => ReferenceEquals(node, methodNode) || !IsNestedCallableBoundary(node))
                .Where(node => node is ObjectCreationExpressionSyntax || node is ImplicitObjectCreationExpressionSyntax);
        }

        private static IEnumerable<BinaryExpressionSyntax> GetDefiniteDivideByZeroNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var binaryExpression in methodNode.DescendantNodes(
                         descendIntoChildren: node => ReferenceEquals(node, methodNode) || !IsNestedCallableBoundary(node))
                         .OfType<BinaryExpressionSyntax>())
            {
                if (!binaryExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DivideExpression) &&
                    !binaryExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ModuloExpression))
                {
                    continue;
                }

                var rightType = semanticModel.GetTypeInfo(binaryExpression.Right, cancellationToken).ConvertedType;
                if (!IsThrowingDivideByZeroType(rightType))
                {
                    continue;
                }

                if (IsDefinitelyZeroExpression(binaryExpression.Right, binaryExpression, semanticModel, cancellationToken))
                {
                    yield return binaryExpression;
                }
            }
        }

        private static IEnumerable<SyntaxNode> GetDefiniteNullDereferenceNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var node in methodNode.DescendantNodes(
                         descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !IsNestedCallableBoundary(candidate)))
            {
                if (node is MemberAccessExpressionSyntax memberAccess &&
                    IsDefinitelyNullExpression(memberAccess.Expression, memberAccess, semanticModel, cancellationToken))
                {
                    yield return memberAccess;
                }
                else if (node is ElementAccessExpressionSyntax elementAccess &&
                    IsDefinitelyNullExpression(elementAccess.Expression, elementAccess, semanticModel, cancellationToken))
                {
                    yield return elementAccess;
                }
                else if (node is InvocationExpressionSyntax invocation &&
                    IsDefinitelyNullExpression(invocation.Expression, invocation, semanticModel, cancellationToken))
                {
                    yield return invocation;
                }
            }
        }

        private static IEnumerable<SyntaxNode> GetPropertyAccessNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var node in methodNode.DescendantNodes(
                         descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !IsNestedCallableBoundary(candidate)))
            {
                if (node is MemberAccessExpressionSyntax memberAccess)
                {
                    if (IsWriteOnlyTarget(memberAccess))
                    {
                        continue;
                    }

                    if (semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol)
                    {
                        yield return memberAccess;
                    }
                }
                else if (node is IdentifierNameSyntax identifierName)
                {
                    if (identifierName.Parent is MemberAccessExpressionSyntax ||
                        IsWriteOnlyTarget(identifierName))
                    {
                        continue;
                    }

                    if (semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is IPropertySymbol)
                    {
                        yield return identifierName;
                    }
                }
                else if (node is ElementAccessExpressionSyntax elementAccess)
                {
                    if (IsWriteOnlyTarget(elementAccess))
                    {
                        continue;
                    }

                    if (semanticModel.GetSymbolInfo(elementAccess, cancellationToken).Symbol is IPropertySymbol)
                    {
                        yield return elementAccess;
                    }
                }
            }
        }

        private static IEnumerable<SyntaxNode> GetPropertyWriteNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var node in methodNode.DescendantNodes(
                         descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !IsNestedCallableBoundary(candidate)))
            {
                if (TryGetPropertySetterMethod(node, semanticModel, cancellationToken, out _))
                {
                    yield return node;
                }
            }
        }

        private static bool IsWriteOnlyTarget(SyntaxNode node)
        {
            return node.Parent is AssignmentExpressionSyntax assignment &&
                ReferenceEquals(assignment.Left, node);
        }

        private static bool IsNestedCallableBoundary(SyntaxNode node)
        {
            return ExecutionVisibility.IsNestedCallableBoundary(node);
        }

        private static ITypeSymbol? GetThrownExceptionType(
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

        private static bool IsRethrow(SyntaxNode throwNode)
        {
            return throwNode is ThrowStatementSyntax statement && statement.Expression == null;
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
            System.Threading.CancellationToken cancellationToken)
        {
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            return (constantValue.HasValue && IsIntegralOrDecimalZero(constantValue.Value)) ||
                IsKnownByPriorAssignment(expression, useNode, semanticModel, cancellationToken, PathFactKind.Zero) ||
                IsKnownByDominatingIf(expression, useNode, semanticModel, cancellationToken, PathFactKind.Zero);
        }

        private static bool IsDefinitelyNullExpression(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
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
                    if (IsDefinitelyNullExpression(castExpression.Expression, useNode, semanticModel, cancellationToken))
                    {
                        var castType = semanticModel.GetTypeInfo(castExpression, cancellationToken).Type;
                        return IsReferenceType(castType);
                    }

                    return false;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                break;
            }

            if (expression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression))
            {
                return true;
            }

            if (expression is DefaultExpressionSyntax defaultExpression)
            {
                var defaultType = semanticModel.GetTypeInfo(defaultExpression, cancellationToken).Type;
                return IsReferenceType(defaultType);
            }

            return IsKnownByPriorAssignment(expression, useNode, semanticModel, cancellationToken, PathFactKind.Null) ||
                IsKnownByDominatingIf(expression, useNode, semanticModel, cancellationToken, PathFactKind.Null);
        }

        private static bool IsKnownByDominatingIf(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            PathFactKind factKind)
        {
            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol == null)
            {
                return false;
            }

            foreach (var ifStatement in useNode.Ancestors().OfType<IfStatementSyntax>())
            {
                if (ifStatement.Statement.Span.Contains(useNode.SpanStart) &&
                    ConditionImpliesFact(ifStatement.Condition, symbol, factKind, branchWhenTrue: true, semanticModel, cancellationToken) &&
                    !IsSymbolAssignedBeforeUse(ifStatement.Statement, useNode.SpanStart, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }

                if (ifStatement.Else?.Statement is { } elseStatement &&
                    elseStatement.Span.Contains(useNode.SpanStart) &&
                    ConditionImpliesFact(ifStatement.Condition, symbol, factKind, branchWhenTrue: false, semanticModel, cancellationToken) &&
                    !IsSymbolAssignedBeforeUse(elseStatement, useNode.SpanStart, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return IsKnownByPrecedingGuardIf(symbol, useNode, semanticModel, cancellationToken, factKind);
        }

        private static bool IsKnownByPriorAssignment(
            ExpressionSyntax expression,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            PathFactKind factKind)
        {
            var symbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            if (symbol == null)
            {
                return false;
            }

            var containingStatement = useNode
                .AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement => statement.Parent is BlockSyntax);
            if (containingStatement?.Parent is not BlockSyntax block)
            {
                return false;
            }

            var matchedAssignment = false;
            foreach (var statement in block.Statements)
            {
                if (ReferenceEquals(statement, containingStatement))
                {
                    break;
                }

                foreach (var candidate in statement.DescendantNodesAndSelf(
                             descendIntoChildren: node => !IsNestedCallableBoundary(node)))
                {
                    if (candidate is AssignmentExpressionSyntax assignment &&
                        ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                    {
                        if (!ExpressionMatchesFact(assignment.Right, factKind, semanticModel, cancellationToken))
                        {
                            return false;
                        }

                        matchedAssignment = true;
                    }
                    else if (candidate is VariableDeclaratorSyntax declarator &&
                             declarator.Initializer != null &&
                             semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is ILocalSymbol localSymbol &&
                             SymbolEqualityComparer.Default.Equals(localSymbol.OriginalDefinition, symbol))
                    {
                        if (!ExpressionMatchesFact(declarator.Initializer.Value, factKind, semanticModel, cancellationToken))
                        {
                            return false;
                        }

                        matchedAssignment = true;
                    }
                    else if (candidate is PrefixUnaryExpressionSyntax prefixUnary &&
                             (prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) || prefixUnary.IsKind(SyntaxKind.PreDecrementExpression)) &&
                             ExpressionMatchesSymbol(prefixUnary.Operand, symbol, semanticModel, cancellationToken))
                    {
                        return false;
                    }
                    else if (candidate is PostfixUnaryExpressionSyntax postfixUnary &&
                             (postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) || postfixUnary.IsKind(SyntaxKind.PostDecrementExpression)) &&
                             ExpressionMatchesSymbol(postfixUnary.Operand, symbol, semanticModel, cancellationToken))
                    {
                        return false;
                    }
                    else if (candidate is ArgumentSyntax argument &&
                             !argument.RefKindKeyword.IsKind(SyntaxKind.None) &&
                             ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken))
                    {
                        return false;
                    }
                }
            }

            return matchedAssignment;
        }

        private static bool IsKnownByPrecedingGuardIf(
            ISymbol symbol,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            PathFactKind factKind)
        {
            var containingStatement = useNode
                .AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement => statement.Parent is BlockSyntax);
            if (containingStatement?.Parent is not BlockSyntax block)
            {
                return false;
            }

            foreach (var statement in block.Statements)
            {
                if (ReferenceEquals(statement, containingStatement))
                {
                    break;
                }

                if (statement is IfStatementSyntax ifStatement &&
                    ifStatement.Else == null &&
                    StatementDefinitelyExits(ifStatement.Statement) &&
                    ConditionImpliesFact(ifStatement.Condition, symbol, factKind, branchWhenTrue: false, semanticModel, cancellationToken) &&
                    !IsSymbolAssignedBetween(block, ifStatement.Span.End, useNode.SpanStart, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static ISymbol? GetLocalOrParameterSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            expression = UnwrapFactExpression(expression);
            var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
            return symbol is ILocalSymbol or IParameterSymbol ? symbol.OriginalDefinition : null;
        }

        private static ExpressionSyntax UnwrapFactExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                return expression;
            }
        }

        private static bool ConditionImpliesFact(
            ExpressionSyntax condition,
            ISymbol symbol,
            PathFactKind factKind,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            condition = UnwrapFactExpression(condition);
            if (condition is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                return ConditionImpliesFact(prefixUnary.Operand, symbol, factKind, !branchWhenTrue, semanticModel, cancellationToken);
            }

            if (condition is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    return branchWhenTrue &&
                        (ConditionImpliesFact(binaryExpression.Left, symbol, factKind, branchWhenTrue: true, semanticModel, cancellationToken) ||
                         ConditionImpliesFact(binaryExpression.Right, symbol, factKind, branchWhenTrue: true, semanticModel, cancellationToken));
                }

                if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    return !branchWhenTrue &&
                        (ConditionImpliesFact(binaryExpression.Left, symbol, factKind, branchWhenTrue: false, semanticModel, cancellationToken) ||
                         ConditionImpliesFact(binaryExpression.Right, symbol, factKind, branchWhenTrue: false, semanticModel, cancellationToken));
                }

                var equalityImpliesFact = binaryExpression.IsKind(SyntaxKind.EqualsExpression) && branchWhenTrue;
                var inequalityImpliesFact = binaryExpression.IsKind(SyntaxKind.NotEqualsExpression) && !branchWhenTrue;
                if (!equalityImpliesFact && !inequalityImpliesFact)
                {
                    return false;
                }

                return IsSymbolComparedToFact(binaryExpression.Left, binaryExpression.Right, symbol, factKind, semanticModel, cancellationToken) ||
                    IsSymbolComparedToFact(binaryExpression.Right, binaryExpression.Left, symbol, factKind, semanticModel, cancellationToken);
            }

            if (condition is IsPatternExpressionSyntax isPatternExpression &&
                ExpressionMatchesSymbol(isPatternExpression.Expression, symbol, semanticModel, cancellationToken) &&
                PatternImpliesFact(isPatternExpression.Pattern, factKind, branchWhenTrue, semanticModel, cancellationToken))
            {
                return true;
            }

            return false;
        }

        private static bool IsSymbolComparedToFact(
            ExpressionSyntax symbolExpression,
            ExpressionSyntax factExpression,
            ISymbol symbol,
            PathFactKind factKind,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return ExpressionMatchesSymbol(symbolExpression, symbol, semanticModel, cancellationToken) &&
                ExpressionMatchesFact(factExpression, factKind, semanticModel, cancellationToken);
        }

        private static bool ExpressionMatchesSymbol(
            ExpressionSyntax expression,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var expressionSymbol = GetLocalOrParameterSymbol(expression, semanticModel, cancellationToken);
            return expressionSymbol != null && SymbolEqualityComparer.Default.Equals(expressionSymbol, symbol);
        }

        private static bool ExpressionMatchesFact(
            ExpressionSyntax expression,
            PathFactKind factKind,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            expression = UnwrapFactExpression(expression);
            if (factKind == PathFactKind.Null)
            {
                return expression.IsKind(SyntaxKind.NullLiteralExpression);
            }

            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            return constantValue.HasValue && IsIntegralOrDecimalZero(constantValue.Value);
        }

        private static bool PatternImpliesFact(
            PatternSyntax pattern,
            PathFactKind factKind,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (pattern is ParenthesizedPatternSyntax parenthesizedPattern)
            {
                return PatternImpliesFact(parenthesizedPattern.Pattern, factKind, branchWhenTrue, semanticModel, cancellationToken);
            }

            if (pattern is ConstantPatternSyntax constantPattern)
            {
                return branchWhenTrue && ExpressionMatchesFact(constantPattern.Expression, factKind, semanticModel, cancellationToken);
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.OperatorToken.IsKind(SyntaxKind.NotKeyword))
            {
                return PatternImpliesFact(unaryPattern.Pattern, factKind, !branchWhenTrue, semanticModel, cancellationToken);
            }

            return false;
        }

        private static bool IsSymbolAssignedBeforeUse(
            SyntaxNode branchRoot,
            int useSpanStart,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return IsSymbolAssignedBetween(branchRoot, branchRoot.SpanStart - 1, useSpanStart, symbol, semanticModel, cancellationToken);
        }

        private static bool IsSymbolAssignedBetween(
            SyntaxNode root,
            int afterSpanStart,
            int beforeSpanStart,
            ISymbol symbol,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var node in root.DescendantNodes(
                         descendIntoChildren: candidate => !IsNestedCallableBoundary(candidate)))
            {
                if (node.SpanStart <= afterSpanStart || node.SpanStart >= beforeSpanStart)
                {
                    continue;
                }

                if (node is AssignmentExpressionSyntax assignment &&
                    ExpressionMatchesSymbol(assignment.Left, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }

                if (node is PrefixUnaryExpressionSyntax prefixUnary &&
                    (prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) || prefixUnary.IsKind(SyntaxKind.PreDecrementExpression)) &&
                    ExpressionMatchesSymbol(prefixUnary.Operand, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }

                if (node is PostfixUnaryExpressionSyntax postfixUnary &&
                    (postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) || postfixUnary.IsKind(SyntaxKind.PostDecrementExpression)) &&
                    ExpressionMatchesSymbol(postfixUnary.Operand, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }

                if (node is ArgumentSyntax argument &&
                    !argument.RefKindKeyword.IsKind(SyntaxKind.None) &&
                    ExpressionMatchesSymbol(argument.Expression, symbol, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StatementDefinitelyExits(StatementSyntax statement)
        {
            switch (statement)
            {
                case ReturnStatementSyntax:
                case ThrowStatementSyntax:
                    return true;
                case BlockSyntax block:
                    return block.Statements.LastOrDefault() is ReturnStatementSyntax or ThrowStatementSyntax;
                default:
                    return false;
            }
        }

        private static bool IsReferenceType(ITypeSymbol? typeSymbol)
        {
            return typeSymbol != null &&
                typeSymbol.TypeKind != TypeKind.TypeParameter &&
                typeSymbol.IsReferenceType;
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

        private static IEnumerable<ExceptionCandidate> CollectSourceCalleeExceptions(
            IMethodSymbol invokedMethod,
            Compilation compilation,
            System.Threading.CancellationToken cancellationToken,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods)
        {
            var originalDefinition = invokedMethod.OriginalDefinition;
            if (!visitedMethods.Add(originalDefinition))
            {
                return Enumerable.Empty<ExceptionCandidate>();
            }

            try
            {
                var syntaxReference = invokedMethod.DeclaringSyntaxReferences.FirstOrDefault()
                    ?? originalDefinition.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxReference == null)
                {
                    return Enumerable.Empty<ExceptionCandidate>();
                }

                var syntax = syntaxReference.GetSyntax(cancellationToken);
                var semanticModel = compilation.GetSemanticModel(syntax.SyntaxTree);
                var exceptions = CollectUncaughtExceptions(
                    syntax,
                    semanticModel,
                    cancellationToken,
                    invokedMethod,
                    exceptionSummaryCatalog,
                    visitedMethods);

                return exceptions.Types
                    .Select(name => new ExceptionCandidate(
                        TryResolveExceptionType(compilation, name),
                        name,
                        "source_callee",
                        invokedMethod.OriginalDefinition.ToDisplayString()))
                    .ToArray();
            }
            finally
            {
                visitedMethods.Remove(originalDefinition);
            }
        }

        private static IEnumerable<ExceptionCandidate> CollectCalleeExceptions(
            IMethodSymbol invokedMethod,
            Compilation compilation,
            System.Threading.CancellationToken cancellationToken,
            ExceptionSummaryCatalog exceptionSummaryCatalog,
            HashSet<IMethodSymbol> visitedMethods)
        {
            foreach (var exception in CollectSourceCalleeExceptions(invokedMethod, compilation, cancellationToken, exceptionSummaryCatalog, visitedMethods))
            {
                yield return exception;
            }

            if (!exceptionSummaryCatalog.TryGetExceptions(invokedMethod, compilation, out var summaryExceptions))
            {
                yield break;
            }

            foreach (var exceptionType in summaryExceptions)
            {
                yield return new ExceptionCandidate(
                    TryResolveExceptionType(compilation, exceptionType),
                    exceptionType,
                    "effect_summary",
                    invokedMethod.OriginalDefinition.ToDisplayString());
            }
        }

        private static ITypeSymbol? TryResolveExceptionType(Compilation compilation, string displayName)
        {
            return displayName == UnknownExceptionType
                ? null
                : compilation.GetTypeByMetadataName(displayName);
        }

        private static bool IsCaughtWithinMethod(
            SyntaxNode throwNode,
            ITypeSymbol? exceptionType,
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var tryStatement in throwNode.Ancestors().OfType<TryStatementSyntax>())
            {
                if (!tryStatement.Span.Contains(throwNode.SpanStart))
                {
                    continue;
                }

                if (!tryStatement.Block.Span.Contains(throwNode.SpanStart))
                {
                    continue;
                }

                if (tryStatement.Catches.Any(catchClause => CatchesException(catchClause, exceptionType, semanticModel, cancellationToken)))
                {
                    return true;
                }

                if (ReferenceEquals(tryStatement, methodNode))
                {
                    break;
                }
            }

            return false;
        }

        private static bool IsInStaticallyUnreachableBranch(
            SyntaxNode node,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return ExecutionVisibility.IsInStaticallyUnreachableBranch(node, semanticModel, cancellationToken);
        }

        private static bool CatchesException(
            CatchClauseSyntax catchClause,
            ITypeSymbol? exceptionType,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (catchClause.Filter != null)
            {
                if (ExecutionVisibility.IsConditionAlwaysFalse(catchClause.Filter.FilterExpression, semanticModel, cancellationToken))
                {
                    return false;
                }

                if (!ExecutionVisibility.IsConditionAlwaysTrue(catchClause.Filter.FilterExpression, semanticModel, cancellationToken))
                {
                    return false;
                }
            }

            if (catchClause.Declaration == null)
            {
                return true;
            }

            if (exceptionType == null)
            {
                return false;
            }

            var catchType = semanticModel.GetTypeInfo(catchClause.Declaration.Type, cancellationToken).Type;
            return catchType != null && IsSameOrDerivedFrom(exceptionType, catchType);
        }

        private static bool IsSameOrDerivedFrom(ITypeSymbol exceptionType, ITypeSymbol catchType)
        {
            for (var current = exceptionType; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, catchType))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetPropertySetterMethod(
            SyntaxNode node,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out IMethodSymbol? setterMethod)
        {
            setterMethod = null;
            if (!IsWriteOnlyTarget(node))
            {
                return false;
            }

            if (semanticModel.GetSymbolInfo(node, cancellationToken).Symbol is not IPropertySymbol propertySymbol ||
                propertySymbol.SetMethod == null)
            {
                return false;
            }

            setterMethod = propertySymbol.SetMethod;
            return true;
        }

        private static IEnumerable<MethodCallCandidate> GetUsingDisposeNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var usingStatement in methodNode.DescendantNodes(
                         descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !IsNestedCallableBoundary(candidate))
                         .OfType<UsingStatementSyntax>())
            {
                var resourceType = GetUsingStatementResourceType(usingStatement, semanticModel, cancellationToken);
                if (resourceType == null)
                {
                    continue;
                }

                foreach (var disposeMethod in GetDisposableMethods(resourceType))
                {
                    yield return new MethodCallCandidate(usingStatement, disposeMethod);
                }
            }

            foreach (var usingDeclaration in methodNode.DescendantNodes(
                         descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !IsNestedCallableBoundary(candidate))
                         .OfType<LocalDeclarationStatementSyntax>()
                         .Where(statement => !statement.UsingKeyword.IsKind(SyntaxKind.None)))
            {
                foreach (var variable in usingDeclaration.Declaration.Variables)
                {
                    var resourceType = GetUsingDeclarationVariableType(variable, semanticModel, cancellationToken);
                    if (resourceType == null)
                    {
                        continue;
                    }

                    foreach (var disposeMethod in GetDisposableMethods(resourceType))
                    {
                        yield return new MethodCallCandidate(usingDeclaration, disposeMethod);
                    }
                }
            }
        }

        private static ITypeSymbol? GetUsingStatementResourceType(
            UsingStatementSyntax usingStatement,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            if (usingStatement.Expression != null)
            {
                return semanticModel.GetTypeInfo(usingStatement.Expression, cancellationToken).ConvertedType;
            }

            if (usingStatement.Declaration == null)
            {
                return null;
            }

            foreach (var variable in usingStatement.Declaration.Variables)
            {
                var type = GetUsingDeclarationVariableType(variable, semanticModel, cancellationToken);
                if (type != null)
                {
                    return type;
                }
            }

            return semanticModel.GetTypeInfo(usingStatement.Declaration.Type, cancellationToken).Type;
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

        private static IEnumerable<MethodCallCandidate> GetForEachRuntimeMethodNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            foreach (var forEachStatement in methodNode.DescendantNodes(
                         descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !IsNestedCallableBoundary(candidate))
                         .OfType<ForEachStatementSyntax>())
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

                foreach (var disposeMethod in GetDisposableMethods(enumeratorType))
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

        private static IEnumerable<IMethodSymbol> GetDisposableMethods(ITypeSymbol typeSymbol)
        {
            foreach (var method in typeSymbol
                         .GetMembers("Dispose")
                         .OfType<IMethodSymbol>()
                         .Where(candidate => candidate.Parameters.Length == 0))
            {
                yield return method;
            }

            foreach (var method in typeSymbol
                         .GetMembers("DisposeAsync")
                         .OfType<IMethodSymbol>()
                         .Where(candidate => candidate.Parameters.Length == 0))
            {
                yield return method;
            }
        }

        private static IEnumerable<MethodCallCandidate> GetOperatorAndConversionNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rootOperation = GetMethodBodyRootOperation(methodNode, semanticModel, cancellationToken);
            if (rootOperation == null)
            {
                yield break;
            }

            foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
            {
                if (TryGetOperatorOrConversionMethod(operation, out var method))
                {
                    var key = method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) +
                        "@" +
                        operation.Syntax.SpanStart.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ":" +
                        operation.Syntax.Span.End.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (seen.Add(key))
                    {
                        yield return new MethodCallCandidate(operation.Syntax, method);
                    }
                }
            }
        }

        private static bool TryGetOperatorOrConversionMethod(
            IOperation operation,
            out IMethodSymbol? method)
        {
            method = null;
            switch (operation)
            {
                case IBinaryOperation binaryOperation when binaryOperation.OperatorMethod != null:
                    method = binaryOperation.OperatorMethod;
                    return true;
                case IUnaryOperation unaryOperation when unaryOperation.OperatorMethod != null:
                    method = unaryOperation.OperatorMethod;
                    return true;
                case IConversionOperation conversionOperation
                    when conversionOperation.Conversion.IsUserDefined && conversionOperation.Conversion.MethodSymbol != null:
                    method = conversionOperation.Conversion.MethodSymbol;
                    return true;
                default:
                    return false;
            }
        }

        private static IOperation? GetMethodBodyRootOperation(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            return methodNode switch
            {
                MethodDeclarationSyntax methodDeclaration when methodDeclaration.Body != null =>
                    semanticModel.GetOperation(methodDeclaration.Body, cancellationToken),
                MethodDeclarationSyntax methodDeclaration when methodDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(methodDeclaration.ExpressionBody.Expression, cancellationToken),
                ConstructorDeclarationSyntax constructorDeclaration when constructorDeclaration.Body != null =>
                    semanticModel.GetOperation(constructorDeclaration.Body, cancellationToken),
                ConstructorDeclarationSyntax constructorDeclaration when constructorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(constructorDeclaration.ExpressionBody.Expression, cancellationToken),
                OperatorDeclarationSyntax operatorDeclaration when operatorDeclaration.Body != null =>
                    semanticModel.GetOperation(operatorDeclaration.Body, cancellationToken),
                OperatorDeclarationSyntax operatorDeclaration when operatorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(operatorDeclaration.ExpressionBody.Expression, cancellationToken),
                AccessorDeclarationSyntax accessorDeclaration when accessorDeclaration.Body != null =>
                    semanticModel.GetOperation(accessorDeclaration.Body, cancellationToken),
                AccessorDeclarationSyntax accessorDeclaration when accessorDeclaration.ExpressionBody != null =>
                    semanticModel.GetOperation(accessorDeclaration.ExpressionBody.Expression, cancellationToken),
                LocalFunctionStatementSyntax localFunction when localFunction.Body != null =>
                    semanticModel.GetOperation(localFunction.Body, cancellationToken),
                LocalFunctionStatementSyntax localFunction when localFunction.ExpressionBody != null =>
                    semanticModel.GetOperation(localFunction.ExpressionBody.Expression, cancellationToken),
                _ => semanticModel.GetOperation(methodNode, cancellationToken)
            };
        }

        private static IEnumerable<MethodCallCandidate> GetLocalDelegateTargetInvocationNodes(
            SyntaxNode methodNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken)
        {
            var knownTargets = new Dictionary<ISymbol, IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var node in methodNode.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !IsNestedCallableBoundary(candidate)))
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
            foreach (var interpolatedString in methodNode.DescendantNodes(
                         descendIntoChildren: candidate => ReferenceEquals(candidate, methodNode) || !IsNestedCallableBoundary(candidate))
                         .OfType<InterpolatedStringExpressionSyntax>())
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

        private static Location? GetIdentifierLocation(SyntaxNode node)
        {
            return node switch
            {
                MethodDeclarationSyntax method => method.Identifier.GetLocation(),
                ConstructorDeclarationSyntax constructor => constructor.Identifier.GetLocation(),
                OperatorDeclarationSyntax op => op.OperatorToken.GetLocation(),
                LocalFunctionStatementSyntax localFunction => localFunction.Identifier.GetLocation(),
                AccessorDeclarationSyntax accessor =>
                    accessor.Parent?.Parent switch
                    {
                        PropertyDeclarationSyntax property => property.Identifier.GetLocation(),
                        IndexerDeclarationSyntax indexer => indexer.ThisKeyword.GetLocation(),
                        _ => accessor.Keyword.GetLocation()
                    } ?? accessor.Keyword.GetLocation(),
                _ => node.GetLocation()
            };
        }

        private sealed class ExceptionCandidate
        {
            public ExceptionCandidate(ITypeSymbol? type, string displayName, string category, string source)
            {
                Type = type;
                DisplayName = displayName;
                Category = category;
                Source = source;
            }

            public ITypeSymbol? Type { get; }

            public string DisplayName { get; }

            public string Category { get; }

            public string Source { get; }
        }

        private sealed class MethodCallCandidate
        {
            public MethodCallCandidate(SyntaxNode callSite, IMethodSymbol method)
            {
                CallSite = callSite;
                Method = method;
            }

            public SyntaxNode CallSite { get; }

            public IMethodSymbol Method { get; }
        }

        private sealed class ExceptionEvidenceSet
        {
            private readonly Dictionary<string, SortedSet<string>> _categoriesByType =
                new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            private readonly Dictionary<string, SortedSet<string>> _sourcesByType =
                new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            public int Count => _categoriesByType.Count;

            public string[] Types => _categoriesByType.Keys.OrderBy(type => type, StringComparer.Ordinal).ToArray();

            public void Add(string exceptionType, string category, string source)
            {
                if (!_categoriesByType.TryGetValue(exceptionType, out var categories))
                {
                    categories = new SortedSet<string>(StringComparer.Ordinal);
                    _categoriesByType.Add(exceptionType, categories);
                }

                categories.Add(category);

                if (!_sourcesByType.TryGetValue(exceptionType, out var sources))
                {
                    sources = new SortedSet<string>(StringComparer.Ordinal);
                    _sourcesByType.Add(exceptionType, sources);
                }

                sources.Add(category + ":" + source);
            }

            public string FormatCategories()
            {
                return string.Join(
                    ";",
                    _categoriesByType.Values
                        .SelectMany(categories => categories)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(category => category, StringComparer.Ordinal));
            }

            public string FormatSources()
            {
                return string.Join(
                    ";",
                    _sourcesByType
                        .OrderBy(item => item.Key, StringComparer.Ordinal)
                        .SelectMany(item => item.Value.Select(source => item.Key + "=" + source)));
            }
        }

        private enum PathFactKind
        {
            Zero,
            Null
        }
    }
}
