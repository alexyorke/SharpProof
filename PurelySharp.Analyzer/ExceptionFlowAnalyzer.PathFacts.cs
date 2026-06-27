using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Analyzer.Engine;
using PurelySharp.Analyzer.Engine.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Analyzer
{
    internal static partial class ExceptionFlowAnalyzer
    {
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

            if (!TryCreateFactFormula(symbol, factKind, out var factFormula) || factFormula == null)
            {
                return false;
            }

            var pathConditions = new List<SmtFormula>();
            foreach (var ifStatement in useNode.Ancestors().OfType<IfStatementSyntax>())
            {
                if (ifStatement.Statement.Span.Contains(useNode.SpanStart) &&
                    !IsSymbolAssignedBeforeUse(ifStatement.Statement, useNode.SpanStart, symbol, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: true, semanticModel, cancellationToken, pathConditions);
                }

                if (ifStatement.Else?.Statement is { } elseStatement &&
                    elseStatement.Span.Contains(useNode.SpanStart) &&
                    !IsSymbolAssignedBeforeUse(elseStatement, useNode.SpanStart, symbol, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, pathConditions);
                }
            }

            AddPrecedingGuardConditions(symbol, useNode, semanticModel, cancellationToken, pathConditions);
            return pathConditions.Count > 0 && PathConditionsImplyFact(pathConditions, factFormula);
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
                             descendIntoChildren: node => !ExecutionVisibility.IsNestedCallableBoundary(node)))
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

        private static void AddPrecedingGuardConditions(
            ISymbol symbol,
            SyntaxNode useNode,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            var containingStatement = useNode
                .AncestorsAndSelf()
                .OfType<StatementSyntax>()
                .FirstOrDefault(statement => statement.Parent is BlockSyntax);
            if (containingStatement?.Parent is not BlockSyntax block)
            {
                return;
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
                    !IsSymbolAssignedBetween(block, ifStatement.Span.End, useNode.SpanStart, symbol, semanticModel, cancellationToken))
                {
                    TryAddPathCondition(ifStatement.Condition, branchWhenTrue: false, semanticModel, cancellationToken, pathConditions);
                }
            }
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

        private static bool TryCreateFactFormula(
            ISymbol symbol,
            PathFactKind factKind,
            out SmtFormula? factFormula)
        {
            factFormula = null;
            var variableName = GetSmtVariableName(symbol);
            switch (symbol)
            {
                case ILocalSymbol localSymbol:
                    return TryCreateFactFormula(localSymbol.Type, variableName, factKind, out factFormula);
                case IParameterSymbol parameterSymbol:
                    return TryCreateFactFormula(parameterSymbol.Type, variableName, factKind, out factFormula);
                default:
                    return false;
            }
        }

        private static bool TryCreateFactFormula(
            ITypeSymbol typeSymbol,
            string variableName,
            PathFactKind factKind,
            out SmtFormula? factFormula)
        {
            factFormula = null;
            if (factKind == PathFactKind.Null)
            {
                if (!IsReferenceType(typeSymbol))
                {
                    return false;
                }

                factFormula = new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtVariable(variableName, SmtValueKind.Reference),
                    new SmtNullConstant());
                return true;
            }

            if (!IsSearchLibIntegralType(typeSymbol))
            {
                return false;
            }

            factFormula = new SmtBinaryFormula(
                SmtBinaryOperator.Equal,
                new SmtVariable(variableName, SmtValueKind.Int),
                new SmtIntegerConstant(0));
            return true;
        }

        private static bool IsSearchLibIntegralType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64;
        }

        private static string GetSmtVariableName(ISymbol symbol)
        {
            var firstLocation = symbol.Locations.FirstOrDefault();
            var start = firstLocation?.SourceSpan.Start ?? 0;
            return symbol.Name + "#" + start.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void TryAddPathCondition(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            if (!CSharpConditionToFormula.TryTranslate(condition, semanticModel, cancellationToken, out var formula) ||
                formula == null)
            {
                return;
            }

            if (!branchWhenTrue)
            {
                formula = new SmtUnaryFormula(SmtUnaryOperator.Not, formula);
            }

            pathConditions.Add(formula);
        }

        private static bool PathConditionsImplyFact(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula)
        {
            var query = new PurityProofQuery(
                pathConditions.ToArray(),
                new PurityHazard(
                    PurityHazardKind.BranchReachability,
                    new SmtUnaryFormula(SmtUnaryOperator.Not, factFormula)));

            using var search = new PurityProofSearch();
            var proofResult = search.Classify(query, SmtTimeout);
            return proofResult.Outcome == PurityProofOutcome.ProvablyPure;
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
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
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
    }
}
