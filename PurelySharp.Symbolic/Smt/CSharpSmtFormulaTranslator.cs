using System;
using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace PurelySharp.Symbolic.Smt
{
    internal static class CSharpSmtFormulaTranslator
    {
        internal static bool TryTranslate(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslate(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryTranslateValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslateValue(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryTranslateStringValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslateStringValue(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryTranslateNullableHasValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslateNullableHasValue(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryTranslateNullableValueParts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out CSharpConditionToFormula.NullableSmtValueParts parts,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslateNullableValueParts(
                expression,
                semanticModel,
                cancellationToken,
                out parts,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryCollectDomainFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            return CSharpConditionToFormula.TryCollectDomainFacts(
                expression,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        internal static bool TryCollectBranchAssumptions(
            ExpressionSyntax expression,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            return CSharpConditionToFormula.TryCollectBranchAssumptions(
                expression,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        internal static bool TryTranslateBuiltInLengthValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslateBuiltInLengthValue(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryTranslateArrayDimensionLengthValue(
            ExpressionSyntax expression,
            int dimension,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslateArrayDimensionLengthValue(
                expression,
                dimension,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryTranslateBuiltInElementAccessInRange(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslateBuiltInElementAccessInRange(
                elementAccess,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryCreateStringNonNullFormula(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryCreateStringNonNullFormula(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryCreateAsExpressionAssignmentFacts(
            ExpressionSyntax valueExpression,
            SmtFormula targetFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<SmtFormula> facts,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryCreateAsExpressionAssignmentFacts(
                valueExpression,
                targetFormula,
                semanticModel,
                cancellationToken,
                out facts,
                getSymbolVersion,
                inlineDepth);
        }

        internal static SmtFormula CreateSubsequenceInRangeFormula(
            SmtFormula sourceLength,
            SmtFormula start,
            SmtFormula? count,
            bool oneArgumentUpperBoundIsInclusive)
        {
            return CSharpConditionToFormula.CreateSubsequenceInRangeFormula(
                sourceLength,
                start,
                count,
                oneArgumentUpperBoundIsInclusive);
        }
    }
}
