using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;

namespace SharpProof.Analyzer
{
    internal static partial class ExceptionFlowAnalyzer
    {
        private static void RemoveFactsInvalidatedByNestedMutations(
            SyntaxNode root,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            foreach (var node in root.DescendantNodesAndSelf(
                         descendIntoChildren: candidate => !ExecutionVisibility.IsNestedCallableBoundary(candidate)))
            {
                if (TryGetMutatedLocalOrParameterSymbol(node, semanticModel, cancellationToken, out var mutatedSymbol))
                {
                    RemoveFactsReferencingSymbol(facts, mutatedSymbol);
                }
            }
        }

        private static bool TryHandleTupleDeconstructionDeclaration(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapFactExpression(assignment.Left) is not DeclarationExpressionSyntax declarationExpression ||
                declarationExpression.Designation is not ParenthesizedVariableDesignationSyntax leftDesignation ||
                UnwrapFactExpression(assignment.Right) is not TupleExpressionSyntax rightTuple ||
                rightTuple.Arguments.Count != leftDesignation.Variables.Count)
            {
                return false;
            }

            var targetSymbols = new List<ISymbol>();
            foreach (var variableDesignation in leftDesignation.Variables)
            {
                if (variableDesignation is not SingleVariableDesignationSyntax singleVariableDesignation ||
                    singleVariableDesignation.Identifier.ValueText == "_" ||
                    semanticModel.GetDeclaredSymbol(singleVariableDesignation, cancellationToken) is not ILocalSymbol localSymbol)
                {
                    return true;
                }

                targetSymbols.Add(localSymbol.OriginalDefinition);
            }

            for (var index = 0; index < targetSymbols.Count; index++)
            {
                AddAssignedValueFacts(
                    targetSymbols[index],
                    rightTuple.Arguments[index].Expression,
                    semanticModel,
                    cancellationToken,
                    facts);
            }

            return true;
        }

        private static bool TryHandleTupleAssignment(
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            List<SmtFormula> facts)
        {
            if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
                UnwrapFactExpression(assignment.Left) is not TupleExpressionSyntax leftTuple)
            {
                return false;
            }

            var targetSymbols = new List<ISymbol>();
            foreach (var argument in leftTuple.Arguments)
            {
                if (GetLocalOrParameterSymbol(argument.Expression, semanticModel, cancellationToken) is { } targetSymbol)
                {
                    targetSymbols.Add(targetSymbol);
                }
            }

            foreach (var targetSymbol in targetSymbols)
            {
                RemoveFactsReferencingSymbol(facts, targetSymbol);
            }

            if (targetSymbols.Count != leftTuple.Arguments.Count ||
                UnwrapFactExpression(assignment.Right) is not TupleExpressionSyntax rightTuple ||
                rightTuple.Arguments.Count != leftTuple.Arguments.Count ||
                rightTuple.Arguments.Any(argument => ExpressionReferencesAnySymbol(argument.Expression, targetSymbols, semanticModel, cancellationToken)))
            {
                return true;
            }

            for (var index = 0; index < leftTuple.Arguments.Count; index++)
            {
                AddAssignedValueFacts(
                    targetSymbols[index],
                    rightTuple.Arguments[index].Expression,
                    semanticModel,
                    cancellationToken,
                    facts);
            }

            return true;
        }

        private static bool TryGetIncrementedOrDecrementedSymbol(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            System.Threading.CancellationToken cancellationToken,
            out ISymbol symbol,
            out int delta)
        {
            expression = UnwrapFactExpression(expression);
            ExpressionSyntax? operand = expression switch
            {
                PrefixUnaryExpressionSyntax prefixUnary
                    when prefixUnary.IsKind(SyntaxKind.PreIncrementExpression) || prefixUnary.IsKind(SyntaxKind.PreDecrementExpression) =>
                    prefixUnary.Operand,
                PostfixUnaryExpressionSyntax postfixUnary
                    when postfixUnary.IsKind(SyntaxKind.PostIncrementExpression) || postfixUnary.IsKind(SyntaxKind.PostDecrementExpression) =>
                    postfixUnary.Operand,
                _ => null
            };

            var expressionSymbol = operand == null
                ? null
                : semanticModel.GetSymbolInfo(operand, cancellationToken).Symbol;
            if (expressionSymbol is not ILocalSymbol && expressionSymbol is not IParameterSymbol)
            {
                symbol = null!;
                delta = 0;
                return false;
            }

            symbol = expressionSymbol.OriginalDefinition;
            delta = expression.IsKind(SyntaxKind.PreIncrementExpression) ||
                expression.IsKind(SyntaxKind.PostIncrementExpression)
                    ? 1
                    : -1;
            return true;
        }

        private static void RemoveFactsReferencingSymbol(List<SmtFormula> facts, ISymbol symbol)
        {
            SmtFormulaReferenceScanner.RemoveFactsReferencingSymbol(facts, symbol);
        }
    }
}
