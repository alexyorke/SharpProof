using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Smt
{
    internal static class LegacyFormulaCompatibility
    {
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

        internal static bool TryCollectPatternBindingFacts(
            SmtFormula matchedValue,
            ITypeSymbol? matchedValueType,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            return CSharpConditionToFormula.TryCollectPatternBindingFacts(
                matchedValue,
                matchedValueType,
                pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        internal static bool TryTranslatePattern(
            SmtFormula value,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            ITypeSymbol? valueType = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslatePattern(
                value,
                pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                valueType,
                inlineDepth);
        }

        internal static bool TryTranslateCondition(
            ExpressionSyntax condition,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpConditionToFormula.TryTranslate(
                condition,
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
            Func<ISymbol, int>? getSymbolVersion = null,
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
    }
}
