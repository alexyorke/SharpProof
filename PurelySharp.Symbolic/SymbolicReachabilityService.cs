using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    public static class SymbolicReachabilityService
    {
        public static bool IsSatisfiable(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyPathFeasibility(pathConditions, smtAnalysis).PathFeasibility != Feasibility.Unsatisfiable;
        }

        public static bool IsUnsatisfiable(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyPathFeasibility(pathConditions, smtAnalysis).PathFeasibility == Feasibility.Unsatisfiable;
        }

        public static bool PathConditionsImply(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyImplication(pathConditions, factFormula, smtAnalysis).Outcome == PurityProofOutcome.ProvablyPure;
        }

        public static bool PathConditionsAllowAndImply(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SmtAnalysisService? smtAnalysis)
        {
            return IsSatisfiable(pathConditions, smtAnalysis) &&
                PathConditionsImply(pathConditions, factFormula, smtAnalysis);
        }

        public static List<SmtFormula>? TryCollectBranchConditions(
            IEnumerable<SmtFormula> pathConditions,
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var branchConditions = pathConditions.ToList();
            return CSharpConditionToFormula.TryCollectBranchAssumptions(
                    condition,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    branchConditions)
                ? branchConditions
                : null;
        }

        public static bool IsBranchReachable(
            IEnumerable<SmtFormula> pathConditions,
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return TryCollectBranchConditions(
                    pathConditions,
                    condition,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken) is { } branchConditions &&
                IsSatisfiable(branchConditions, smtAnalysis);
        }

        public static bool IsBranchUnreachable(
            IEnumerable<SmtFormula> pathConditions,
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return TryCollectBranchConditions(
                    pathConditions,
                    condition,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken) is { } branchConditions &&
                IsUnsatisfiable(branchConditions, smtAnalysis);
        }

        public static bool PathConditionsImplyBranch(
            IEnumerable<SmtFormula> pathConditions,
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return IsBranchUnreachable(
                pathConditions,
                condition,
                !branchWhenTrue,
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        public static PurityProofResult ClassifyImplication(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SmtAnalysisService? smtAnalysis)
        {
            using var fallbackSmtAnalysis = smtAnalysis == null ? new SmtAnalysisService(SmtAnalysisOptions.Default) : null;
            return (smtAnalysis ?? fallbackSmtAnalysis!).ClassifyImplication(pathConditions, factFormula);
        }

        public static bool IsFormulaAlwaysFalse(
            SmtFormula formula,
            SmtAnalysisService? smtAnalysis)
        {
            return IsFormulaAlwaysFalse(formula, Array.Empty<SmtFormula>(), smtAnalysis);
        }

        public static bool IsFormulaAlwaysFalse(
            SmtFormula formula,
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyBranchReachability(pathConditions, formula, smtAnalysis).Outcome == PurityProofOutcome.ProvablyPure;
        }

        public static bool IsFormulaAlwaysTrue(
            SmtFormula formula,
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return IsFormulaAlwaysFalse(new SmtUnaryFormula(SmtUnaryOperator.Not, formula), pathConditions, smtAnalysis);
        }

        public static bool? EvaluateConditionTruth(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis,
            IEnumerable<SmtFormula>? pathConditions = null)
        {
            var basePathConditions = pathConditions?.ToList() ?? new List<SmtFormula>();
            if (!CSharpConditionToFormula.TryTranslate(expression, semanticModel, cancellationToken, out var formula) ||
                formula == null)
            {
                if (IsBranchUnreachable(
                        basePathConditions,
                        expression,
                        branchWhenTrue: true,
                        semanticModel,
                        cancellationToken,
                        smtAnalysis))
                {
                    return false;
                }

                if (IsBranchUnreachable(
                        basePathConditions,
                        expression,
                        branchWhenTrue: false,
                        semanticModel,
                        cancellationToken,
                        smtAnalysis))
                {
                    return true;
                }

                return null;
            }

            CSharpConditionToFormula.TryCollectDomainFacts(expression, semanticModel, cancellationToken, basePathConditions);
            if (!IsSatisfiable(basePathConditions, smtAnalysis) ||
                IsFormulaAlwaysFalse(formula, basePathConditions, smtAnalysis))
            {
                return false;
            }

            if (IsFormulaAlwaysFalse(new SmtUnaryFormula(SmtUnaryOperator.Not, formula), basePathConditions, smtAnalysis))
            {
                return true;
            }

            return null;
        }

        public static bool IsNodeReachable(
            SyntaxNode node,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return IsSatisfiable(CollectPathConditionsAt(node, semanticModel, cancellationToken), smtAnalysis);
        }

        public static bool IsNodeUnreachable(
            SyntaxNode node,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return !IsNodeReachable(node, semanticModel, cancellationToken, smtAnalysis);
        }

        public static List<SmtFormula> CollectPathConditionsAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return CollectPathConditionsAt(
                site,
                semanticModel,
                cancellationToken,
                includeCurrentStatementCompletionFacts: false);
        }

        public static ImmutableArray<SmtFormula> CollectAncestorReachabilityConditions(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return SymbolicProgramPointFacts.CollectAncestorReachabilityConditions(
                site,
                semanticModel,
                cancellationToken);
        }

        public static List<SmtFormula> CollectPriorAssignmentFacts(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool includeCurrentStatementCompletionFacts = false)
        {
            return SymbolicProgramPointFacts.CollectPriorAssignmentFacts(
                site,
                semanticModel,
                cancellationToken,
                includeCurrentStatementCompletionFacts);
        }

        public static List<SmtFormula> CollectPathConditionsAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool includeCurrentStatementCompletionFacts)
        {
            var pathConditions = CollectAncestorReachabilityConditions(
                    site,
                    semanticModel,
                    cancellationToken)
                .ToList();
            AddAncestorSwitchArrayLengthCountAliasFacts(site, semanticModel, cancellationToken, pathConditions);
            pathConditions.AddRange(CollectPriorAssignmentFacts(
                site,
                semanticModel,
                cancellationToken,
                includeCurrentStatementCompletionFacts));
            return pathConditions;
        }

        public static SmtFormula[] CollectForInitialEntryPathConditions(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return CollectAncestorReachabilityConditions(forStatement, semanticModel, cancellationToken)
                .Concat(CollectPriorAssignmentFacts(forStatement, semanticModel, cancellationToken))
                .Concat(CollectForInitializerFacts(forStatement, semanticModel, cancellationToken))
                .ToArray();
        }

        public static IEnumerable<SmtFormula> CollectForInitializerFacts(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return SymbolicProgramPointFacts.CollectForInitializerFacts(
                forStatement,
                semanticModel,
                cancellationToken);
        }

        public static ImmutableArray<SmtFormula> CollectLoopBodyInvariantFacts(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return SymbolicProgramPointFacts.CollectLoopBodyInvariantFacts(
                loopStatement,
                semanticModel,
                cancellationToken);
        }

        public static ImmutableArray<SmtFormula> CollectCompletedLoopExitInvariantFacts(
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return SymbolicProgramPointFacts.CollectCompletedLoopExitInvariantFacts(
                statement,
                semanticModel,
                cancellationToken);
        }

        public static PurityProofResult ClassifyBranchReachability(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula branchCondition,
            SmtAnalysisService? smtAnalysis)
        {
            var query = new PurityProofQuery(
                pathConditions.ToArray(),
                new PurityHazard(PurityHazardKind.BranchReachability, branchCondition));

            using var fallbackSmtAnalysis = smtAnalysis == null ? new SmtAnalysisService(SmtAnalysisOptions.Default) : null;
            return (smtAnalysis ?? fallbackSmtAnalysis!).Classify(query);
        }

        public static PurityProofResult ClassifyPathFeasibility(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            using var fallbackSmtAnalysis = smtAnalysis == null ? new SmtAnalysisService(SmtAnalysisOptions.Default) : null;
            return (smtAnalysis ?? fallbackSmtAnalysis!).ClassifyPathFeasibility(pathConditions);
        }

        public static bool TryCreateArrayLengthCountAliasFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula aliasFact,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            if (semanticModel.GetTypeInfo(expression, cancellationToken).Type is IArrayTypeSymbol &&
                CSharpConditionToFormula.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion) &&
                receiverFormula is SmtVariable { Kind: SmtValueKind.Reference } receiverVariable)
            {
                aliasFact = new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtVariable(receiverVariable.Name + ".Length", SmtValueKind.Int),
                    new SmtVariable(receiverVariable.Name + ".Count", SmtValueKind.Int));
                return true;
            }

            aliasFact = new SmtBooleanConstant(true);
            return false;
        }

        private static void AddAncestorSwitchArrayLengthCountAliasFacts(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions)
        {
            foreach (var ancestor in site.Ancestors())
            {
                if (ancestor is SwitchStatementSyntax switchStatement)
                {
                    AddArrayLengthCountAliasFact(
                        switchStatement.Expression,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion: null);
                }
                else if (ancestor is SwitchExpressionSyntax switchExpression)
                {
                    AddArrayLengthCountAliasFact(
                        switchExpression.GoverningExpression,
                        semanticModel,
                        cancellationToken,
                        pathConditions,
                        getSymbolVersion: null);
                }
            }
        }

        private static void AddArrayLengthCountAliasFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (TryCreateArrayLengthCountAliasFact(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var aliasFact,
                    getSymbolVersion))
            {
                pathConditions.Add(aliasFact);
            }
        }
    }
}
