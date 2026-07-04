using System.Collections.Generic;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicTranslatorCompatibility
    {
        internal static bool TryCollectDomainFacts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> formulas,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            return CSharpSmtFormulaTranslator.TryCollectDomainFacts(
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
            return CSharpSmtFormulaTranslator.TryCollectBranchAssumptions(
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
            return CSharpSmtFormulaTranslator.TryCollectPatternBindingFacts(
                matchedValue,
                matchedValueType,
                pattern,
                semanticModel,
                cancellationToken,
                formulas,
                getSymbolVersion);
        }

        internal static bool TryTranslatePatternLegacy(
            SmtFormula value,
            PatternSyntax pattern,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            ITypeSymbol? valueType = null,
            int inlineDepth = 0)
        {
            return CSharpSmtFormulaTranslator.TryTranslatePattern(
                value,
                pattern,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                valueType,
                inlineDepth);
        }

        internal static bool TryTranslateConditionLegacy(
            ExpressionSyntax condition,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpSmtFormulaTranslator.TryTranslate(
                condition,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryTranslateValueLegacy(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            return CSharpSmtFormulaTranslator.TryTranslateValue(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }
    }
}
