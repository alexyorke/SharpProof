using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicReachabilityService
    {
        private static readonly ConditionalWeakTable<SemanticModel, StructuralPathConditionCache> s_structuralPathConditionCache = new();

        internal static bool IsSatisfiable(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyFormulaReachability(pathConditions, smtAnalysis).Info.Status != SymbolicProofStatus.Unreachable;
        }

        internal static bool IsUnsatisfiable(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyFormulaReachability(pathConditions, smtAnalysis).Info.Status == SymbolicProofStatus.Unreachable;
        }

        internal static bool PathConditionsImply(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SmtAnalysisService? smtAnalysis)
        {
            var status = ClassifyFormulaConditionTruth(pathConditions, factFormula, smtAnalysis).Info.Status;
            return status is SymbolicProofStatus.ProvenTrue or SymbolicProofStatus.Unreachable;
        }

        internal static bool PathConditionsImplyWithIrFirst(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var status = ClassifyFormulaConditionTruthWithIrFirst(
                pathConditions,
                factFormula,
                sourceNode,
                smtAnalysis,
                provenance,
                evidenceKey).Info.Status;
            return status is SymbolicProofStatus.ProvenTrue or SymbolicProofStatus.Unreachable;
        }

        internal static bool PathConditionsAllowAndImply(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SmtAnalysisService? smtAnalysis)
        {
            return IsSatisfiable(pathConditions, smtAnalysis) &&
                PathConditionsImply(pathConditions, factFormula, smtAnalysis);
        }

        internal static bool PathConditionsAllowAndImplyWithIrFirst(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
            if (TryClassifyFormulaConditionTruthWithIr(
                    pathConditionList,
                    factFormula,
                    sourceNode,
                    smtAnalysis,
                    provenance,
                    evidenceKey,
                    out var status) &&
                status == SymbolicProofStatus.ProvenTrue)
            {
                return true;
            }

            return PathConditionsAllowAndImply(pathConditionList, factFormula, smtAnalysis);
        }

        internal static bool PathConditionsAreSatisfiableWithIrFirst(
            IEnumerable<SmtFormula> pathConditions,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
            if (TryClassifyFormulaPathFeasibilityWithIr(
                    pathConditionList,
                    sourceNode,
                    smtAnalysis,
                    provenance,
                    evidenceKey,
                    out var status))
            {
                return status != SymbolicProofStatus.Unreachable;
            }

            return IsSatisfiable(pathConditionList, smtAnalysis);
        }

        internal static bool PathConditionsAreUnsatisfiableWithIrFirst(
            IEnumerable<SmtFormula> pathConditions,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
            if (TryClassifyFormulaPathFeasibilityWithIr(
                    pathConditionList,
                    sourceNode,
                    smtAnalysis,
                    provenance,
                    evidenceKey,
                    out var status))
            {
                return status == SymbolicProofStatus.Unreachable;
            }

            return IsUnsatisfiable(pathConditionList, smtAnalysis);
        }

        internal static bool PathConditionsAreUnsatisfiableWithOptionalIrFirst(
            IEnumerable<SmtFormula> pathConditions,
            SyntaxNode? sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
            if (sourceNode != null &&
                TryClassifyFormulaPathFeasibilityWithIr(
                    pathConditionList,
                    sourceNode,
                    smtAnalysis,
                    provenance,
                    evidenceKey,
                    out var status))
            {
                return status == SymbolicProofStatus.Unreachable;
            }

            return IsUnsatisfiable(pathConditionList, smtAnalysis);
        }

        internal static bool IsFormulaAlwaysFalseWithIrFirst(
            SmtFormula formula,
            IEnumerable<SmtFormula> pathConditions,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
            if (TryClassifyFormulaConditionTruthWithIr(
                    pathConditionList,
                    formula,
                    sourceNode,
                    smtAnalysis,
                    provenance,
                    evidenceKey,
                    out var status))
            {
                if (status is SymbolicProofStatus.ProvenFalse or SymbolicProofStatus.Unreachable)
                {
                    return true;
                }
            }

            return IsFormulaAlwaysFalse(formula, pathConditionList, smtAnalysis);
        }

        internal static bool IsFormulaAlwaysTrueWithIrFirst(
            SmtFormula formula,
            IEnumerable<SmtFormula> pathConditions,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
            if (TryClassifyFormulaConditionTruthWithIr(
                    pathConditionList,
                    formula,
                    sourceNode,
                    smtAnalysis,
                    provenance,
                    evidenceKey,
                    out var status))
            {
                if (status is SymbolicProofStatus.ProvenTrue or SymbolicProofStatus.Unreachable)
                {
                    return true;
                }
            }

            return IsFormulaAlwaysTrue(formula, pathConditionList, smtAnalysis);
        }

        internal static SymbolicIrProofResult ClassifyStateFeasibility(
            SymbolicState state,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyReachability(state);
        }

        internal static SymbolicIrProofResult ClassifyStateFeasibilityWithFormulaFallback(
            SymbolicState state,
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            var stateProof = ClassifyStateFeasibility(state, smtAnalysis);
            return stateProof.Info.Status == SymbolicProofStatus.Unreachable
                ? stateProof
                : ClassifyFormulaReachability(pathConditions, smtAnalysis);
        }

        internal static SymbolicIrProofResult ClassifyStateImplication(
            SymbolicState state,
            SymbolicFact fact,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyImplication(state, fact);
        }

        internal static SymbolicIrProofResult ClassifyStateImplication(
            SymbolicState state,
            SymbolicCondition condition,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyImplication(state, condition);
        }

        internal static SymbolicIrProofResult ClassifyStateBranchFeasibility(
            SymbolicState state,
            SymbolicCondition branchCondition,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyBranchFeasibility(state, branchCondition);
        }

        internal static SymbolicIrProofResult ClassifyStateConditionTruth(
            SymbolicState state,
            SymbolicCondition condition,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyConditionTruth(state, condition);
        }

        internal static SymbolicIrProofResult ClassifyStateHazardTrigger(
            SymbolicState state,
            SymbolicFact triggerPrecondition,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyHazardTrigger(state, triggerPrecondition);
        }

        internal static bool TryCollectBranchState(
            SymbolicState state,
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SymbolicState branchState,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            if (!TryCreateIrBranchCondition(
                    condition,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion,
                    out var symbolicCondition))
            {
                branchState = state;
                return false;
            }

            branchState = state.AddPathCondition(symbolicCondition);
            return true;
        }

        internal static bool TryEncodeStatePathConditions(
            SymbolicState state,
            out ImmutableArray<SmtFormula> pathConditions)
        {
            return new SymbolicProofService(smtAnalysis: null).TryEncode(state, out pathConditions);
        }

        internal static List<SmtFormula>? TryCollectBranchConditions(
            IEnumerable<SmtFormula> pathConditions,
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var branchConditions = pathConditions.ToList();
            return TryAddBranchConditionFacts(
                    condition,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    branchConditions)
                ? branchConditions
                : null;
        }

        internal static bool TryAddBranchConditionFacts(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion = null,
            bool collectDomainFactsBeforeBranchAssumptions = false,
            bool addTranslatedFormulaFallback = false,
            bool addTranslatedFormulaAlways = false)
        {
            var originalCount = pathConditions.Count;

            if (collectDomainFactsBeforeBranchAssumptions)
            {
                TryCollectDomainFacts(
                    condition,
                    semanticModel,
                    cancellationToken,
                    pathConditions,
                    getSymbolVersion);
            }

            var addedIrBranchFact = TryAddIrBranchConditionFact(
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                pathConditions,
                getSymbolVersion);

            var countBeforeBranchAssumptions = pathConditions.Count;
            TryCollectBranchAssumptions(
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                pathConditions,
                    getSymbolVersion);

            var addedBranchFacts = pathConditions.Count != countBeforeBranchAssumptions;
            if ((addTranslatedFormulaAlways ||
                 addTranslatedFormulaFallback && !addedIrBranchFact && !addedBranchFacts) &&
                CSharpSmtFormulaTranslator.TryTranslate(
                    condition,
                    semanticModel,
                    cancellationToken,
                    out var formula,
                    getSymbolVersion) &&
                formula != null)
            {
                pathConditions.Add(branchWhenTrue
                    ? formula
                    : new SmtUnaryFormula(SmtUnaryOperator.Not, formula));
            }

            return pathConditions.Count != originalCount;
        }

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

        internal static void AddUnsatisfiablePathCondition(ICollection<SmtFormula> pathConditions)
        {
            pathConditions.Add(new SmtBooleanConstant(false));
        }

        private static bool TryAddIrBranchConditionFact(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            if (ContainsDivisionOrModulo(condition))
            {
                return false;
            }

            if (!TryCreateIrBranchCondition(
                    condition,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    getSymbolVersion,
                    out var symbolicCondition) ||
                !SymbolicIrFormulaEncoder.TryEncode(symbolicCondition, out var formula))
            {
                return false;
            }

            pathConditions.Add(formula);
            return true;
        }

        private static bool ContainsDivisionOrModulo(ExpressionSyntax expression)
        {
            return expression.DescendantNodesAndSelf()
                .OfType<BinaryExpressionSyntax>()
                .Any(static binary => binary.IsKind(SyntaxKind.DivideExpression) || binary.IsKind(SyntaxKind.ModuloExpression));
        }

        private static bool TryCreateIrBranchCondition(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int>? getSymbolVersion,
            out SymbolicCondition symbolicCondition)
        {
            var context = new SymbolicLoweringContext(
                semanticModel,
                cancellationToken,
                getSymbolVersion);
            if (!SymbolicIrLowerer.TryLowerCondition(condition, context, out symbolicCondition))
            {
                return false;
            }

            if (!branchWhenTrue)
            {
                symbolicCondition = new SymbolicNotCondition(symbolicCondition);
            }

            return true;
        }

        internal static bool IsBranchReachable(
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

        internal static bool IsBranchUnreachable(
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

        internal static bool PathConditionsImplyBranch(
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

        internal static bool PathConditionsImplyBranchWithIrFirst(
            IEnumerable<SmtFormula> pathConditions,
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
            if (TryClassifyBranchConditionTruthWithIr(
                    pathConditionList,
                    condition,
                    branchWhenTrue,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis,
                    provenance,
                    evidenceKey,
                    out var status) &&
                status == SymbolicProofStatus.ProvenTrue)
            {
                return true;
            }

            return PathConditionsImplyBranch(
                pathConditionList,
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                smtAnalysis);
        }

        internal static PurityProofResult ClassifyImplication(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyFormulaImplication(pathConditions, factFormula);
        }

        internal static SymbolicIrProofResult ClassifyFormulaReachability(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyFormulaReachability(pathConditions);
        }

        internal static SymbolicIrProofResult ClassifyFormulaConditionTruth(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula conditionFormula,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyFormulaConditionTruth(pathConditions, conditionFormula);
        }

        internal static SymbolicIrProofResult ClassifyFormulaConditionTruthWithIrFirst(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula conditionFormula,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
            if (SymbolicSmtFormulaLowerer.TryLowerCondition(
                    conditionFormula,
                    sourceNode,
                    provenance,
                    evidenceKey,
                    out var condition) &&
                TryCreateStateFromFormulaPath(pathConditionList, sourceNode, provenance, evidenceKey, out var state))
            {
                var proof = ClassifyStateConditionTruth(state, condition, smtAnalysis);
                if (proof.Info.Status != SymbolicProofStatus.Unknown)
                {
                    return proof;
                }
            }

            return ClassifyFormulaConditionTruth(pathConditionList, conditionFormula, smtAnalysis);
        }

        internal static SymbolicIrProofResult ClassifyFormulaConditionTruthWithIrFallback(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula conditionFormula,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey)
        {
            var pathConditionList = pathConditions as IReadOnlyCollection<SmtFormula> ?? pathConditions.ToArray();
            var formulaProof = ClassifyFormulaConditionTruth(pathConditionList, conditionFormula, smtAnalysis);
            if (formulaProof.Info.Status != SymbolicProofStatus.Unknown)
            {
                return formulaProof;
            }

            if (SymbolicSmtFormulaLowerer.TryLowerCondition(
                    conditionFormula,
                    sourceNode,
                    provenance,
                    evidenceKey,
                    out var condition) &&
                TryCreateStateFromFormulaPath(pathConditionList, sourceNode, provenance, evidenceKey, out var state))
            {
                var proof = ClassifyStateConditionTruth(state, condition, smtAnalysis);
                if (proof.Info.Status != SymbolicProofStatus.Unknown)
                {
                    return proof;
                }
            }

            return formulaProof;
        }

        internal static bool TryClassifyFormulaConditionTruthWithIr(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula conditionFormula,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey,
            out SymbolicProofStatus status)
        {
            status = SymbolicProofStatus.Unknown;
            if (!SymbolicSmtFormulaLowerer.TryLowerCondition(
                    conditionFormula,
                    sourceNode,
                    provenance,
                    evidenceKey,
                    out var condition))
            {
                return false;
            }

            if (!TryCreateStateFromFormulaPath(pathConditions, sourceNode, provenance, evidenceKey, out var state))
            {
                return false;
            }

            status = ClassifyStateConditionTruth(state, condition, smtAnalysis).Info.Status;
            return status != SymbolicProofStatus.Unknown;
        }

        internal static bool TryClassifyFormulaPathFeasibilityWithIr(
            IEnumerable<SmtFormula> pathConditions,
            SyntaxNode sourceNode,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey,
            out SymbolicProofStatus status)
        {
            status = SymbolicProofStatus.Unknown;
            if (!TryCreateStateFromFormulaPath(pathConditions, sourceNode, provenance, evidenceKey, out var state))
            {
                return false;
            }

            status = ClassifyStateFeasibility(state, smtAnalysis).Info.Status;
            return status is SymbolicProofStatus.Reachable or SymbolicProofStatus.Unreachable;
        }

        internal static bool TryClassifyBranchConditionTruthWithIr(
            IEnumerable<SmtFormula> pathConditions,
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis,
            string provenance,
            string evidenceKey,
            out SymbolicProofStatus status)
        {
            status = SymbolicProofStatus.Unknown;
            if (!SymbolicIrLowerer.TryLowerCondition(
                    condition,
                    new SymbolicLoweringContext(semanticModel, cancellationToken),
                    out var symbolicCondition) ||
                !TryCreateStateFromFormulaPath(pathConditions, condition, provenance, evidenceKey, out var state))
            {
                return false;
            }

            if (!branchWhenTrue)
            {
                symbolicCondition = new SymbolicNotCondition(symbolicCondition);
            }

            status = ClassifyStateConditionTruth(state, symbolicCondition, smtAnalysis).Info.Status;
            return status != SymbolicProofStatus.Unknown;
        }

        internal static bool IsFormulaAlwaysFalse(
            SmtFormula formula,
            SmtAnalysisService? smtAnalysis)
        {
            return IsFormulaAlwaysFalse(formula, Array.Empty<SmtFormula>(), smtAnalysis);
        }

        internal static bool IsFormulaAlwaysFalse(
            SmtFormula formula,
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            var status = ClassifyFormulaConditionTruth(pathConditions, formula, smtAnalysis).Info.Status;
            return status is SymbolicProofStatus.ProvenFalse or SymbolicProofStatus.Unreachable;
        }

        internal static bool IsFormulaAlwaysTrue(
            SmtFormula formula,
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return IsFormulaAlwaysFalse(new SmtUnaryFormula(SmtUnaryOperator.Not, formula), pathConditions, smtAnalysis);
        }

        internal static bool? EvaluateConditionTruth(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis,
            IEnumerable<SmtFormula>? pathConditions = null)
        {
            var basePathConditions = pathConditions?.ToList() ?? new List<SmtFormula>();
            if (!CSharpSmtFormulaTranslator.TryTranslate(expression, semanticModel, cancellationToken, out var formula) ||
                formula == null)
            {
                if (!ContainsDivisionOrModulo(expression) &&
                    EvaluateConditionTruthWithIr(
                        expression,
                        semanticModel,
                        cancellationToken,
                        smtAnalysis,
                        basePathConditions) is { } irTruth)
                {
                    return irTruth;
                }

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

            TryCollectDomainFacts(expression, semanticModel, cancellationToken, basePathConditions);
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

        private static bool? EvaluateConditionTruthWithIr(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis,
            IReadOnlyCollection<SmtFormula> pathConditions)
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (!SymbolicIrLowerer.TryLowerCondition(expression, context, out var condition))
            {
                return null;
            }

            var state = CreateStateFromFormulaPath(pathConditions, expression);
            var truth = ClassifyStateConditionTruth(state, condition, smtAnalysis);
            return truth.Info.Status switch
            {
                SymbolicProofStatus.Unreachable => false,
                SymbolicProofStatus.ProvenTrue => true,
                SymbolicProofStatus.ProvenFalse => false,
                _ => null,
            };
        }

        private static SymbolicState CreateStateFromFormulaPath(
            IEnumerable<SmtFormula> pathConditions,
            SyntaxNode sourceNode)
        {
            var state = new SymbolicState();
            foreach (var pathCondition in pathConditions)
            {
                if (SymbolicSmtFormulaLowerer.TryLowerCondition(
                        pathCondition,
                        sourceNode,
                        "legacy_path_condition",
                        "legacy-path-condition",
                        out var condition))
                {
                    state = state.AddPathCondition(condition);
                }
            }

            return state;
        }

        private static bool TryCreateStateFromFormulaPath(
            IEnumerable<SmtFormula> pathConditions,
            SyntaxNode sourceNode,
            string provenance,
            string evidenceKey,
            out SymbolicState state)
        {
            state = new SymbolicState();
            foreach (var pathCondition in pathConditions)
            {
                if (!SymbolicSmtFormulaLowerer.TryLowerCondition(
                        pathCondition,
                        sourceNode,
                        provenance,
                        evidenceKey,
                        out var condition))
                {
                    state = new SymbolicState();
                    return false;
                }

                state = state.AddPathCondition(condition);
            }

            return true;
        }

        internal static bool? EvaluateKnownConditionTruth(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis,
            IReadOnlyCollection<SmtFormula>? pathConditions = null)
        {
            expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
            var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
            if (constantValue.HasValue && constantValue.Value is bool booleanValue)
            {
                return booleanValue;
            }

            if (expression is PrefixUnaryExpressionSyntax prefixUnary &&
                prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
            {
                return EvaluateKnownConditionTruth(
                    prefixUnary.Operand,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis,
                    pathConditions) is { } operandTruth
                    ? !operandTruth
                    : null;
            }

            if (expression is BinaryExpressionSyntax binaryExpression)
            {
                if (binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
                {
                    var left = EvaluateKnownConditionTruth(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                    var right = EvaluateKnownConditionTruth(binaryExpression.Right, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                    if (left == false || right == false)
                    {
                        return false;
                    }

                    if (left == true && right == true)
                    {
                        return true;
                    }
                }
                else if (binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
                {
                    var left = EvaluateKnownConditionTruth(binaryExpression.Left, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                    var right = EvaluateKnownConditionTruth(binaryExpression.Right, semanticModel, cancellationToken, smtAnalysis, pathConditions);
                    if (left == true || right == true)
                    {
                        return true;
                    }

                    if (left == false && right == false)
                    {
                        return false;
                    }
                }
            }

            return EvaluateConditionTruth(
                expression,
                semanticModel,
                cancellationToken,
                smtAnalysis,
                pathConditions);
        }

        internal static List<SmtFormula> CollectPathConditionsAt(
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

        internal static ImmutableArray<SmtFormula> CollectAncestorReachabilityConditions(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return SymbolicProgramPointFacts.CollectAncestorReachabilityConditions(
                site,
                semanticModel,
                cancellationToken);
        }

        internal static List<SmtFormula> CollectPriorAssignmentFacts(
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

        internal static List<SmtFormula> CollectPathConditionsAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool includeCurrentStatementCompletionFacts)
        {
            var key = new PathConditionCacheKey(
                site.SpanStart,
                site.Span.Length,
                site.RawKind,
                includeCurrentStatementCompletionFacts);
            var cache = s_structuralPathConditionCache.GetOrCreateValue(semanticModel);
            if (!cache.Values.TryGetValue(key, out var cached))
            {
                cached = BuildStructuralPathConditionSnapshot(
                    site,
                    semanticModel,
                    cancellationToken);
                cache.Values.TryAdd(key, cached);
            }

            var pathConditions = cached.ToList();
            pathConditions.AddRange(CollectPriorAssignmentFacts(
                site,
                semanticModel,
                cancellationToken,
                includeCurrentStatementCompletionFacts));
            return pathConditions;
        }

        internal static SmtFormula[] CollectForInitialEntryPathConditions(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return CollectAncestorReachabilityConditions(forStatement, semanticModel, cancellationToken)
                .Concat(CollectPriorAssignmentFacts(forStatement, semanticModel, cancellationToken))
                .Concat(CollectForInitializerFacts(forStatement, semanticModel, cancellationToken))
                .ToArray();
        }

        internal static bool IsForInitialEntryConditionAlwaysFalse(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            if (forStatement.Condition == null)
            {
                return false;
            }

            var initialEntryState = SymbolicProgramPointFacts.CollectForInitialEntryState(
                forStatement,
                semanticModel,
                cancellationToken);
            if (SymbolicIrLowerer.TryLowerCondition(
                    forStatement.Condition,
                    new SymbolicLoweringContext(semanticModel, cancellationToken),
                    out var initialEntryCondition))
            {
                var proof = ClassifyStateConditionTruth(initialEntryState, initialEntryCondition, smtAnalysis);
                if (proof.Info.Status is SymbolicProofStatus.ProvenFalse or SymbolicProofStatus.Unreachable)
                {
                    return true;
                }

                if (proof.Info.Status == SymbolicProofStatus.ProvenTrue)
                {
                    return false;
                }
            }

            var pathConditions = CollectPathConditionsAt(forStatement, semanticModel, cancellationToken);
            if (!TryTranslateConditionFormula(forStatement.Condition, semanticModel, cancellationToken, out var formula) ||
                formula == null)
            {
                return EvaluateKnownConditionTruth(
                    forStatement.Condition,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis,
                    pathConditions) == false;
            }

            foreach (var initializerFact in CollectForInitializerFacts(forStatement, semanticModel, cancellationToken))
            {
                pathConditions.Add(initializerFact);
            }

            TryCollectDomainFacts(forStatement.Condition, semanticModel, cancellationToken, pathConditions);
            return IsFormulaAlwaysFalse(formula, pathConditions, smtAnalysis);
        }

        internal static IEnumerable<SmtFormula> CollectForInitializerFacts(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return SymbolicProgramPointFacts.CollectForInitializerFacts(
                forStatement,
                semanticModel,
                cancellationToken);
        }

        internal static ImmutableArray<SmtFormula> CollectLoopBodyInvariantFacts(
            StatementSyntax loopStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return SymbolicProgramPointFacts.CollectLoopBodyInvariantFacts(
                loopStatement,
                semanticModel,
                cancellationToken);
        }

        internal static ImmutableArray<SmtFormula> CollectCompletedLoopExitInvariantFacts(
            StatementSyntax statement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return SymbolicProgramPointFacts.CollectCompletedLoopExitInvariantFacts(
                statement,
                semanticModel,
                cancellationToken);
        }

        internal static PurityProofResult ClassifyBranchReachability(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula branchCondition,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis)
                .ClassifyFormulaBranchReachability(pathConditions, branchCondition);
        }

        internal static PurityProofResult ClassifyPathFeasibility(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis)
                .ClassifyFormulaReachability(pathConditions)
                .RawResult ?? new PurityProofResult(
                    PurityProofOutcome.Unknown,
                    Feasibility.Unknown,
                    Feasibility.Unknown,
                    "unsupported_formula_reachability");
        }

        internal static bool TryTranslateConditionFormula(
            ExpressionSyntax condition,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            SmtFormula? irFormula = null;
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
            if (SymbolicIrLowerer.TryLowerCondition(condition, context, out var symbolicCondition) &&
                SymbolicIrFormulaEncoder.TryEncode(symbolicCondition, out var encodedFormula))
            {
                irFormula = encodedFormula;
            }

            if (CSharpSmtFormulaTranslator.TryTranslate(
                    condition,
                    semanticModel,
                    cancellationToken,
                    out var translatedFormula,
                    getSymbolVersion,
                    inlineDepth) &&
                translatedFormula != null)
            {
                formula = translatedFormula;
                return true;
            }

            formula = irFormula;
            return formula != null;
        }

        internal static bool TryCreateArrayLengthCountAliasFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula aliasFact,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            if (semanticModel.GetTypeInfo(expression, cancellationToken).Type is IArrayTypeSymbol &&
                TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    SmtValueKind.Reference,
                    getSymbolVersion) &&
                receiverFormula is SmtVariable receiverVariable)
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

        internal static bool TryCreateReferenceNullComparison(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool equalToNull,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    SmtValueKind.Reference,
                    getSymbolVersion))
            {
                return false;
            }

            formula = new SmtBinaryFormula(
                equalToNull ? SmtBinaryOperator.Equal : SmtBinaryOperator.NotEqual,
                valueFormula,
                new SmtNullConstant());
            return true;
        }

        internal static bool TryCreateExpressionNumericZeroComparison(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateIntegerValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion))
            {
                return false;
            }

            formula = SmtFormulaFactory.CreateIntegerEqualsZero(valueFormula);
            return true;
        }

        internal static bool TryCreateExpressionNonNegativeComparison(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateIntegerValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion))
            {
                return false;
            }

            formula = SmtFormulaFactory.CreateIntegerGreaterThanOrEqualZero(valueFormula);
            return true;
        }

        internal static bool TryCreateNegativeLengthTrigger(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateIntegerValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion))
            {
                return false;
            }

            formula = SmtFormulaFactory.CreateIntegerLessThanZero(valueFormula);
            return true;
        }

        internal static bool TryCreateNullableHasValueCondition(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (SymbolicIrLowerer.TryLowerNullableHasValueTerm(expression, context, out var hasValueTerm) &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(hasValueTerm, out formula))
            {
                return true;
            }

            return CSharpSmtFormulaTranslator.TryTranslateNullableHasValue(
                expression,
                semanticModel,
                cancellationToken,
                out formula);
        }

        internal static bool TryCreateRuntimeTypeTestCondition(
            ExpressionSyntax expression,
            ITypeSymbol targetType,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey(targetType, out var typeKey))
            {
                return false;
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var value) &&
                value.Kind == SmtValueKind.Reference &&
                SymbolicIrFormulaEncoder.TryEncode(new SymbolicTypeTestAtom(value, typeKey), out formula))
            {
                return true;
            }

            if (!TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    SmtValueKind.Reference,
                    getSymbolVersion))
            {
                return false;
            }

            formula = new SmtRuntimeTypeTestFormula(valueFormula, typeKey);
            return true;
        }

        internal static bool TryCreateIntegerInRangeCondition(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            long minValue,
            long maxValue,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateIntegerValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion))
            {
                return false;
            }

            formula = SmtFormulaFactory.CreateIntegerInRange(valueFormula, minValue, maxValue);
            return true;
        }

        internal static bool TryCreateSubsequenceInRangeCondition(
            ExpressionSyntax receiverExpression,
            ExpressionSyntax startExpression,
            ExpressionSyntax? lengthExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            bool oneArgumentUpperBoundIsInclusive = true,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateBuiltInLengthValue(
                    receiverExpression,
                    semanticModel,
                    cancellationToken,
                    out var receiverLengthFormula) ||
                receiverLengthFormula is not { Kind: SmtValueKind.Int } ||
                !TryTranslateIntegerValue(
                    startExpression,
                    semanticModel,
                    cancellationToken,
                    out var startFormula,
                    getSymbolVersion))
            {
                return false;
            }

            SmtFormula? sliceLengthFormula = null;
            if (lengthExpression != null &&
                !TryTranslateIntegerValue(
                    lengthExpression,
                    semanticModel,
                    cancellationToken,
                    out sliceLengthFormula,
                    getSymbolVersion))
            {
                return false;
            }

            formula = SmtFormulaFactory.CreateSubsequenceInRangeFormula(
                receiverLengthFormula,
                startFormula,
                sliceLengthFormula,
                oneArgumentUpperBoundIsInclusive);
            return true;
        }

        internal static bool TryCreateBuiltInElementAccessInRangeCondition(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            if (TryCreateIrBuiltInElementAccessInRangeCondition(
                    elementAccess,
                    semanticModel,
                    cancellationToken,
                    out formula))
            {
                return true;
            }

            return CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange(
                elementAccess,
                semanticModel,
                cancellationToken,
                out formula);
        }

        private static bool TryCreateIrBuiltInElementAccessInRangeCondition(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula)
        {
            formula = null!;
            if (elementAccess.ArgumentList.Arguments.Count != 1)
            {
                return false;
            }

            var indexExpression = elementAccess.ArgumentList.Arguments[0].Expression;
            if ((semanticModel.GetTypeInfo(indexExpression, cancellationToken).ConvertedType ??
                 semanticModel.GetTypeInfo(indexExpression, cancellationToken).Type)?.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken);
            if (!SymbolicIrLowerer.TryLowerTerm(indexExpression, context, out var index) ||
                index.Kind != SmtValueKind.Int ||
                !TryCreateIrBuiltInElementAccessLengthTerm(elementAccess, semanticModel, cancellationToken, context, out var length))
            {
                return false;
            }

            var inRangeFact = SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    index,
                    length,
                    IncludeLowerBound: true,
                    IncludeUpperBound: true),
                elementAccess,
                "ir.element-access.bounds.in-range");
            return SymbolicIrFormulaEncoder.TryEncode(inRangeFact, out formula);
        }

        private static bool TryCreateIrBuiltInElementAccessLengthTerm(
            ElementAccessExpressionSyntax elementAccess,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SymbolicLoweringContext context,
            out SymbolicTerm length)
        {
            length = null!;
            if (!SymbolicIrLowerer.TryLowerTerm(elementAccess.Expression, context, out var receiver))
            {
                return false;
            }

            var receiverType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
            if (receiverType?.SpecialType == SpecialType.System_String)
            {
                length = receiver.Kind == SmtValueKind.String
                    ? new SymbolicLengthTerm(receiver)
                    : receiver.Kind == SmtValueKind.Reference
                        ? new SymbolicLengthTerm(new SymbolicStringContentTerm(receiver))
                        : null!;
                return length != null;
            }

            if (receiverType is IArrayTypeSymbol { Rank: 1 } ||
                SymbolicTypeFacts.IsBuiltInSpanType(receiverType))
            {
                if (receiver.Kind != SmtValueKind.Reference)
                {
                    return false;
                }

                length = new SymbolicLengthTerm(receiver);
                return true;
            }

            return false;
        }

        internal static bool TryCreateIntegerBinaryInRangeCondition(
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression,
            SmtIntegerBinaryOperator smtOperator,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            long minValue,
            long maxValue,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateIntegerValue(
                    leftExpression,
                    semanticModel,
                    cancellationToken,
                    out var leftFormula,
                    getSymbolVersion) ||
                !TryTranslateIntegerValue(
                    rightExpression,
                    semanticModel,
                    cancellationToken,
                    out var rightFormula,
                    getSymbolVersion))
            {
                return false;
            }

            var resultFormula = SmtFormulaFactory.CreateIntegerBinaryTerm(smtOperator, leftFormula, rightFormula);
            formula = SmtFormulaFactory.CreateIntegerInRange(resultFormula, minValue, maxValue);
            return true;
        }

        internal static bool TryCreateSignedDivisionOverflowCondition(
            ExpressionSyntax leftExpression,
            ExpressionSyntax rightExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            long minValue,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateIntegerValue(
                    leftExpression,
                    semanticModel,
                    cancellationToken,
                    out var leftFormula,
                    getSymbolVersion) ||
                !TryTranslateIntegerValue(
                    rightExpression,
                    semanticModel,
                    cancellationToken,
                    out var rightFormula,
                    getSymbolVersion))
            {
                return false;
            }

            formula = new SmtBinaryFormula(
                SmtBinaryOperator.And,
                new SmtBinaryFormula(SmtBinaryOperator.Equal, leftFormula, new SmtIntegerConstant(minValue)),
                new SmtBinaryFormula(SmtBinaryOperator.Equal, rightFormula, new SmtIntegerConstant(-1)));
            return true;
        }

        internal static bool TryCreateIntegerUnaryInRangeCondition(
            ExpressionSyntax expression,
            SmtIntegerUnaryOperator smtOperator,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            long minValue,
            long maxValue,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateIntegerValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var operandFormula,
                    getSymbolVersion))
            {
                return false;
            }

            var resultFormula = SmtFormulaFactory.CreateIntegerUnaryTerm(smtOperator, operandFormula);
            formula = SmtFormulaFactory.CreateIntegerInRange(resultFormula, minValue, maxValue);
            return true;
        }

        internal static bool TryCreateIntegerIncrementOrDecrementInRangeCondition(
            ExpressionSyntax operandExpression,
            SmtIntegerBinaryOperator smtOperator,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            long minValue,
            long maxValue,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!TryTranslateIntegerValue(
                    operandExpression,
                    semanticModel,
                    cancellationToken,
                    out var operandFormula,
                    getSymbolVersion))
            {
                return false;
            }

            var resultFormula = SmtFormulaFactory.CreateIntegerBinaryTerm(
                smtOperator,
                operandFormula,
                SmtFormulaFactory.CreateIntegerOne());
            formula = SmtFormulaFactory.CreateIntegerInRange(resultFormula, minValue, maxValue);
            return true;
        }

        private static bool TryTranslateValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            SmtValueKind kind,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            SmtFormula? irFormula = null;
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
            if (!ContainsDivisionOrModulo(expression) &&
                SymbolicIrLowerer.TryLowerTerm(expression, context, out var term) &&
                term.Kind == kind &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var encodedFormula))
            {
                irFormula = encodedFormula;
            }

            if (CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var translatedFormula,
                    getSymbolVersion) &&
                translatedFormula is { } &&
                translatedFormula.Kind == kind)
            {
                formula = translatedFormula;
                return true;
            }

            formula = irFormula!;
            return formula != null;
        }

        internal static bool TryTranslateValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            SmtFormula? irFormula = null;
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
            if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term) &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var encodedFormula))
            {
                irFormula = encodedFormula;
            }

            if (CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var translatedFormula,
                    getSymbolVersion,
                    inlineDepth) &&
                translatedFormula != null)
            {
                formula = translatedFormula;
                return true;
            }

            formula = irFormula!;
            return formula != null;
        }

        private static bool TryTranslateComparableValue(
            ExpressionSyntax expression,
            SmtFormula targetFormula,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            if (TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var translatedFormula,
                    targetFormula.Kind,
                    getSymbolVersion) &&
                SymbolicFactFactory.CanCompareSmtValues(targetFormula, translatedFormula))
            {
                formula = translatedFormula;
                return true;
            }

            if (CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var inlineTranslatedFormula,
                    getSymbolVersion,
                    inlineDepth) &&
                inlineTranslatedFormula is { } &&
                SymbolicFactFactory.CanCompareSmtValues(targetFormula, inlineTranslatedFormula))
            {
                formula = inlineTranslatedFormula;
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryTranslateIntegerValue(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            return TryTranslateValue(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                SmtValueKind.Int,
                getSymbolVersion);
        }

        internal static bool TryCreateAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact,
            Func<ISymbol, int>? getSymbolVersion = null,
            Func<ISymbol, int>? getTargetSymbolVersion = null)
        {
            fact = null!;
            if (!SymbolicFactFactory.TryCreateSymbolVariableFormula(
                    GetVersionedSmtVariableName(targetSymbol, getTargetSymbolVersion),
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                    SymbolicTypeFacts.IsReferenceType,
                    out var targetFormula) ||
                !TryTranslateComparableValue(
                    valueExpression,
                    targetFormula,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion))
            {
                return false;
            }

            fact = SymbolicFactFactory.CreateAssignedValueFact(targetFormula, valueFormula);
            return true;
        }

        internal static bool TryCreateBuiltInLengthAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact,
            Func<ISymbol, int>? getSymbolVersion = null,
            Func<ISymbol, int>? getTargetSymbolVersion = null)
        {
            fact = null!;
            if (!SymbolicFactFactory.TryCreateBuiltInLengthFormula(
                    GetVersionedSmtVariableName(targetSymbol, getTargetSymbolVersion),
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    out var targetLengthFormula) ||
                !TryTranslateBuiltInLengthValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueLengthFormula,
                    getSymbolVersion))
            {
                return false;
            }

            fact = SmtFormulaFactory.CreateEquality(targetLengthFormula, valueLengthFormula);
            return true;
        }

        internal static bool TryCreateStringContentAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact,
            Func<ISymbol, int>? getSymbolVersion = null,
            Func<ISymbol, int>? getTargetSymbolVersion = null)
        {
            fact = null!;
            if (!SymbolicFactFactory.TryCreateStringContentFormula(
                    GetVersionedSmtVariableName(targetSymbol, getTargetSymbolVersion),
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    out var targetStringFormula) ||
                !TryTranslateStringValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueStringFormula,
                    getSymbolVersion) ||
                valueStringFormula == null)
            {
                return false;
            }

            fact = SmtFormulaFactory.CreateEquality(targetStringFormula, valueStringFormula);
            return true;
        }

        internal static bool TryTranslateBuiltInLengthValue(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            SmtFormula? irFormula = null;
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
            if (SymbolicIrLowerer.TryLowerTerm(valueExpression, context, out var term) &&
                term is SymbolicLengthTerm &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var encodedFormula))
            {
                irFormula = encodedFormula;
            }

            if (CSharpSmtFormulaTranslator.TryTranslateBuiltInLengthValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion))
            {
                return true;
            }

            formula = irFormula!;
            return formula != null;
        }

        internal static bool TryTranslateStringValue(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            SmtFormula? irFormula = null;
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
            if (SymbolicIrLowerer.TryLowerStringTerm(valueExpression, context, out var stringTerm) &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(stringTerm, out var encodedFormula))
            {
                irFormula = encodedFormula;
            }

            if (CSharpSmtFormulaTranslator.TryTranslateStringValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var translatedFormula,
                    getSymbolVersion) &&
                translatedFormula != null)
            {
                formula = translatedFormula;
                return true;
            }

            formula = irFormula!;
            return formula != null;
        }

        internal static bool TryCreateStringNonNullAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact,
            Func<ISymbol, int>? getSymbolVersion = null,
            Func<ISymbol, int>? getTargetSymbolVersion = null)
        {
            fact = null!;
            if (SymbolicFactFactory.GetTrackedSymbolType(targetSymbol)?.SpecialType != SpecialType.System_String ||
                !SymbolicFactFactory.TryCreateSymbolVariableFormula(
                    GetVersionedSmtVariableName(targetSymbol, getTargetSymbolVersion),
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                    SymbolicTypeFacts.IsReferenceType,
                    out var targetReferenceFormula) ||
                targetReferenceFormula is not { Kind: SmtValueKind.Reference } ||
                !TryCreateStringNonNullFormula(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueNonNullFormula,
                    getSymbolVersion) ||
                valueNonNullFormula == null)
            {
                return false;
            }

            fact = SmtFormulaFactory.CreateEquality(
                SmtFormulaFactory.CreateReferenceNullComparison(targetReferenceFormula, isNull: false),
                valueNonNullFormula);
            return true;
        }

        private static bool TryCreateStringNonNullFormula(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula? formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            var context = new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
            if (SymbolicIrLowerer.TryLowerTerm(expression, context, out var term) &&
                term.Kind == SmtValueKind.Reference &&
                SymbolicIrFormulaEncoder.TryEncodeTerm(term, out var referenceFormula))
            {
                formula = SmtFormulaFactory.CreateReferenceNullComparison(referenceFormula, isNull: false);
                return true;
            }

            return CSharpSmtFormulaTranslator.TryCreateStringNonNullFormula(
                expression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryCreateNotNullIfNotNullAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact,
            Func<ISymbol, int>? getSymbolVersion = null,
            Func<ISymbol, int>? getTargetSymbolVersion = null)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula, getTargetSymbolVersion) ||
                targetFormula is not { Kind: SmtValueKind.Reference } ||
                !TryCreateNotNullIfNotNullResultNonNullFormula(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueNonNullFormula,
                    getSymbolVersion,
                    inlineDepth: 0,
                    requireLocalOrParameterSource: true))
            {
                return false;
            }

            fact = SmtFormulaFactory.CreateEquality(
                SmtFormulaFactory.CreateReferenceNullComparison(targetFormula, isNull: false),
                valueNonNullFormula);
            return true;
        }

        private static bool TryCreateNotNullIfNotNullResultNonNullFormula(
            ExpressionSyntax resultExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0,
            bool requireLocalOrParameterSource = false)
        {
            if (TryCreateIrNotNullIfNotNullResultNonNullFormula(
                    resultExpression,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    requireLocalOrParameterSource))
            {
                return true;
            }

            return CSharpSmtFormulaTranslator.TryCreateNotNullIfNotNullResultNonNullFormula(
                resultExpression,
                semanticModel,
                cancellationToken,
                out formula,
                getSymbolVersion,
                inlineDepth,
                requireLocalOrParameterSource);
        }

        private static bool TryCreateIrNotNullIfNotNullResultNonNullFormula(
            ExpressionSyntax resultExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            bool requireLocalOrParameterSource)
        {
            formula = null!;
            resultExpression = StripParentheses(resultExpression);
            var resultTypeInfo = semanticModel.GetTypeInfo(resultExpression, cancellationToken);
            var resultType = resultTypeInfo.ConvertedType ?? resultTypeInfo.Type;
            if (resultType == null ||
                !SymbolicTypeFacts.IsReferenceLikeType(resultType) ||
                !TryCreateIrNotNullIfNotNullSourceFormula(
                    resultExpression,
                    semanticModel,
                    cancellationToken,
                    out var sourceFormula,
                    getSymbolVersion,
                    requireLocalOrParameterSource))
            {
                return false;
            }

            var sourceNonNull = SmtFormulaFactory.CreateReferenceNullComparison(sourceFormula, isNull: false);
            var fallbackNonNull = new SmtVariable(
                CreateNotNullIfNotNullFallbackVariableName(resultExpression),
                SmtValueKind.Bool);
            formula = new SmtBinaryFormula(SmtBinaryOperator.Or, sourceNonNull, fallbackNonNull);
            return true;
        }

        private static bool TryCreateIrNotNullIfNotNullSourceFormula(
            ExpressionSyntax resultExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            bool requireLocalOrParameterSource)
        {
            formula = null!;
            var operation = semanticModel.GetOperation(resultExpression, cancellationToken);
            if (operation is IInvocationOperation invocationOperation &&
                TryGetNotNullIfNotNullParameterName(invocationOperation.TargetMethod, out var methodParameterName) &&
                TryGetInvocationSourceExpression(invocationOperation, methodParameterName, out var invocationSource) &&
                TryCreateIrNotNullIfNotNullSourceReference(
                    invocationSource,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    requireLocalOrParameterSource))
            {
                return true;
            }

            if (operation is IPropertyReferenceOperation propertyReferenceOperation &&
                TryGetNotNullIfNotNullParameterName(propertyReferenceOperation.Property, out var propertyParameterName) &&
                TryGetPropertySourceExpression(propertyReferenceOperation, propertyParameterName, out var propertySource) &&
                TryCreateIrNotNullIfNotNullSourceReference(
                    propertySource,
                    semanticModel,
                    cancellationToken,
                    out formula,
                    getSymbolVersion,
                    requireLocalOrParameterSource))
            {
                return true;
            }

            return false;
        }

        private static bool TryCreateIrNotNullIfNotNullSourceReference(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion,
            bool requireLocalOrParameterSource)
        {
            formula = null!;
            if (requireLocalOrParameterSource &&
                !IsLocalOrParameterExpression(expression, semanticModel, cancellationToken))
            {
                return false;
            }

            var context = new SymbolicLoweringContext(semanticModel, cancellationToken, getSymbolVersion);
            if (!SymbolicIrLowerer.TryLowerTerm(expression, context, out var sourceTerm) ||
                sourceTerm.Kind != SmtValueKind.Reference ||
                !SymbolicIrFormulaEncoder.TryEncodeTerm(sourceTerm, out formula))
            {
                formula = null!;
                return false;
            }

            return true;
        }

        private static bool TryGetNotNullIfNotNullParameterName(IMethodSymbol methodSymbol, out string parameterName)
        {
            if (TryGetNotNullIfNotNullParameterName(methodSymbol.GetReturnTypeAttributes(), out parameterName))
            {
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(methodSymbol, methodSymbol.OriginalDefinition) &&
                TryGetNotNullIfNotNullParameterName(methodSymbol.OriginalDefinition.GetReturnTypeAttributes(), out parameterName))
            {
                return true;
            }

            parameterName = string.Empty;
            return false;
        }

        private static bool TryGetNotNullIfNotNullParameterName(IPropertySymbol propertySymbol, out string parameterName)
        {
            if (TryGetNotNullIfNotNullParameterName(propertySymbol.GetAttributes(), out parameterName) ||
                TryGetNotNullIfNotNullParameterName(propertySymbol.GetMethod?.GetReturnTypeAttributes() ?? ImmutableArray<AttributeData>.Empty, out parameterName))
            {
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(propertySymbol, propertySymbol.OriginalDefinition) &&
                (TryGetNotNullIfNotNullParameterName(propertySymbol.OriginalDefinition.GetAttributes(), out parameterName) ||
                 TryGetNotNullIfNotNullParameterName(propertySymbol.OriginalDefinition.GetMethod?.GetReturnTypeAttributes() ?? ImmutableArray<AttributeData>.Empty, out parameterName)))
            {
                return true;
            }

            parameterName = string.Empty;
            return false;
        }

        private static bool TryGetNotNullIfNotNullParameterName(
            ImmutableArray<AttributeData> attributes,
            out string parameterName)
        {
            foreach (var attribute in attributes)
            {
                if (!string.Equals(
                        attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty),
                        "System.Diagnostics.CodeAnalysis.NotNullIfNotNullAttribute",
                        StringComparison.Ordinal) ||
                    attribute.ConstructorArguments.Length != 1 ||
                    attribute.ConstructorArguments[0].Value is not string candidate ||
                    string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                parameterName = candidate;
                return true;
            }

            parameterName = string.Empty;
            return false;
        }

        private static bool TryGetInvocationSourceExpression(
            IInvocationOperation invocationOperation,
            string parameterName,
            out ExpressionSyntax expression)
        {
            expression = null!;
            for (var parameterIndex = 0; parameterIndex < invocationOperation.TargetMethod.Parameters.Length; parameterIndex++)
            {
                if (!string.Equals(
                        invocationOperation.TargetMethod.Parameters[parameterIndex].Name,
                        parameterName,
                        StringComparison.Ordinal) ||
                    !TryGetInvocationArgumentExpression(invocationOperation, parameterIndex, out expression))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool TryGetPropertySourceExpression(
            IPropertyReferenceOperation propertyReferenceOperation,
            string parameterName,
            out ExpressionSyntax expression)
        {
            expression = null!;
            if (string.Equals(parameterName, "this", StringComparison.Ordinal) &&
                propertyReferenceOperation.Instance?.Syntax is ExpressionSyntax receiverExpression)
            {
                expression = receiverExpression;
                return true;
            }

            for (var parameterIndex = 0; parameterIndex < propertyReferenceOperation.Property.Parameters.Length; parameterIndex++)
            {
                if (!string.Equals(
                        propertyReferenceOperation.Property.Parameters[parameterIndex].Name,
                        parameterName,
                        StringComparison.Ordinal) ||
                    !TryGetPropertyArgumentExpression(propertyReferenceOperation, parameterIndex, out expression))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool TryGetInvocationArgumentExpression(
            IInvocationOperation invocationOperation,
            int parameterIndex,
            out ExpressionSyntax expression)
        {
            expression = null!;
            if (parameterIndex < 0 ||
                parameterIndex >= invocationOperation.TargetMethod.Parameters.Length)
            {
                return false;
            }

            var parameter = invocationOperation.TargetMethod.Parameters[parameterIndex];
            foreach (var argument in invocationOperation.Arguments)
            {
                if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    expression = argumentExpression;
                    return true;
                }
            }

            if (parameterIndex < invocationOperation.Arguments.Length &&
                invocationOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression)
            {
                expression = fallbackExpression;
                return true;
            }

            return false;
        }

        private static bool TryGetPropertyArgumentExpression(
            IPropertyReferenceOperation propertyReferenceOperation,
            int parameterIndex,
            out ExpressionSyntax expression)
        {
            expression = null!;
            if (parameterIndex < 0 ||
                parameterIndex >= propertyReferenceOperation.Property.Parameters.Length)
            {
                return false;
            }

            var parameter = propertyReferenceOperation.Property.Parameters[parameterIndex];
            foreach (var argument in propertyReferenceOperation.Arguments)
            {
                if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    expression = argumentExpression;
                    return true;
                }
            }

            if (parameterIndex < propertyReferenceOperation.Arguments.Length &&
                propertyReferenceOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression)
            {
                expression = fallbackExpression;
                return true;
            }

            return false;
        }

        private static bool IsLocalOrParameterExpression(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            expression = StripParentheses(expression);
            return expression is IdentifierNameSyntax &&
                semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is ILocalSymbol or IParameterSymbol;
        }

        private static string CreateNotNullIfNotNullFallbackVariableName(ExpressionSyntax expression)
        {
            return "$notNullIfNotNullResultNonNull#" +
                RuntimeHelpers.GetHashCode(expression.SyntaxTree).ToString(CultureInfo.InvariantCulture) +
                "#" +
                expression.SpanStart.ToString(CultureInfo.InvariantCulture) +
                "#" +
                expression.Span.Length.ToString(CultureInfo.InvariantCulture);
        }

        private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return expression;
        }

        internal static bool TryCreateAsExpressionAssignedValueFacts(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out ImmutableArray<SmtFormula> facts,
            Func<ISymbol, int>? getSymbolVersion = null,
            Func<ISymbol, int>? getTargetSymbolVersion = null)
        {
            facts = ImmutableArray<SmtFormula>.Empty;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula, getTargetSymbolVersion) ||
                targetFormula is not { Kind: SmtValueKind.Reference } ||
                !CSharpSmtFormulaTranslator.TryCreateAsExpressionAssignmentFacts(
                    valueExpression,
                    targetFormula,
                    semanticModel,
                    cancellationToken,
                    out facts,
                    getSymbolVersion))
            {
                facts = ImmutableArray<SmtFormula>.Empty;
                return false;
            }

            return facts.Length > 0;
        }

        private static string GetVersionedSmtVariableName(
            ISymbol symbol,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var name = SymbolicFactFactory.GetSmtVariableName(symbol);
            var version = getSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
            return version > 0
                ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
                : name;
        }

        internal static void AddNullableAssignedValueFacts(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (!TryCreateNullableHasValueFormula(targetSymbol, out var targetHasValue) ||
                !TryCreateNullableValueFormula(targetSymbol, out var targetValue))
            {
                return;
            }

            if (TryTranslateNullableValueParts(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var parts,
                    getSymbolVersion: null))
            {
                facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetHasValue, parts.HasValue));

                if (parts.Value != null &&
                    SymbolicFactFactory.CanCompareSmtValues(targetValue, parts.Value))
                {
                    facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetValue, parts.Value));
                }
            }
            else if (SymbolicTypeFacts.TryGetNullableUnderlyingType(SymbolicFactFactory.GetTrackedSymbolType(targetSymbol), out var underlyingType) &&
                     TryTranslateNullableWrappedValueForUnderlyingType(
                         valueExpression,
                         underlyingType,
                         targetValue,
                         semanticModel,
                         cancellationToken,
                         out var wrappedValueFormula))
            {
                facts.Add(targetHasValue);
                facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetValue, wrappedValueFormula));
            }
        }

        internal static bool TryTranslateNullableValueParts(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out NullableValueParts parts,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            if (CSharpSmtFormulaTranslator.TryTranslateNullableValueParts(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var translatedParts,
                    getSymbolVersion,
                    inlineDepth))
            {
                parts = new NullableValueParts(translatedParts.HasValue, translatedParts.Value);
                return true;
            }

            parts = default;
            return false;
        }

        internal readonly struct NullableValueParts
        {
            internal NullableValueParts(SmtFormula hasValue, SmtFormula? value)
            {
                HasValue = hasValue;
                Value = value;
            }

            internal SmtFormula HasValue { get; }

            internal SmtFormula? Value { get; }
        }

        private static bool TryTranslateNullableWrappedValueForUnderlyingType(
            ExpressionSyntax valueExpression,
            ITypeSymbol underlyingType,
            SmtFormula targetValue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula valueFormula)
        {
            valueExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression);
            var typeInfo = semanticModel.GetTypeInfo(valueExpression, cancellationToken);
            if (!SymbolEqualityComparer.Default.Equals(typeInfo.ConvertedType, underlyingType) &&
                !SymbolEqualityComparer.Default.Equals(typeInfo.Type, underlyingType))
            {
                valueFormula = null!;
                return false;
            }

            if (TryTranslateComparableValue(
                    valueExpression,
                    targetValue,
                    semanticModel,
                    cancellationToken,
                    out var translatedValue,
                    getSymbolVersion: null,
                    inlineDepth: 0))
            {
                valueFormula = translatedValue;
                return true;
            }

            valueFormula = null!;
            return false;
        }

        private static bool TryCreateNullableHasValueFormula(ISymbol symbol, out SmtFormula formula)
        {
            if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(SymbolicFactFactory.GetTrackedSymbolType(symbol), out _))
            {
                formula = null!;
                return false;
            }

            formula = SmtFormulaFactory.CreateBoolVariable(SymbolicFactFactory.GetSmtVariableName(symbol) + ".HasValue");
            return true;
        }

        private static bool TryCreateNullableValueFormula(ISymbol symbol, out SmtFormula formula)
        {
            if (!SymbolicTypeFacts.TryGetNullableUnderlyingType(SymbolicFactFactory.GetTrackedSymbolType(symbol), out var underlyingType) ||
                !SymbolicFactFactory.TryGetValueKind(
                    underlyingType,
                    SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                    SymbolicTypeFacts.IsReferenceType,
                    out var kind))
            {
                formula = null!;
                return false;
            }

            formula = SmtFormulaFactory.CreateVariable(SymbolicFactFactory.GetSmtVariableName(symbol) + ".Value", kind);
            return true;
        }

        internal static bool TryCreateReferenceBackedLengthFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact,
            Func<ISymbol, int>? getSymbolVersion = null,
            Func<ISymbol, int>? getTargetSymbolVersion = null)
        {
            fact = null!;
            return TryCreateSymbolSmtValue(targetSymbol, out var targetReference, getTargetSymbolVersion) &&
                SymbolicFactFactory.TryCreateReferenceBackedLengthFact(
                    targetReference,
                    valueExpression,
                    CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                    semanticModel,
                    cancellationToken,
                    (expression, model, token) =>
                        TryTranslateBuiltInLengthValue(
                            expression,
                            model,
                            token,
                            out var formula,
                            getSymbolVersion)
                            ? formula
                            : null,
                    out fact);
        }

        internal static bool TryCreateReferenceBackedStringContentFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact,
            Func<ISymbol, int>? getSymbolVersion = null,
            Func<ISymbol, int>? getTargetSymbolVersion = null)
        {
            fact = null!;
            return TryCreateSymbolSmtValue(targetSymbol, out var targetReference, getTargetSymbolVersion) &&
                SymbolicFactFactory.TryCreateReferenceBackedStringContentFact(
                    targetReference,
                    valueExpression,
                    CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                    semanticModel,
                    cancellationToken,
                    (expression, model, token) =>
                        TryTranslateStringValue(
                            expression,
                            model,
                            token,
                            out var valueString,
                            getSymbolVersion)
                            ? valueString
                            : null,
                    out fact);
        }

        internal static bool TryCreateCollectionExpressionLengthLowerBoundFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            out SmtFormula fact)
        {
            fact = null!;
            return SymbolicFactFactory.TryCreateBuiltInLengthFormula(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol),
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    out var targetLengthFormula) &&
                SymbolicFactFactory.TryCreateCollectionExpressionLengthLowerBoundFact(
                    targetLengthFormula,
                    CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                    out fact);
        }

        internal static void AddReferenceBackedArrayDimensionLengthFacts(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetReference))
            {
                return;
            }

            SymbolicFactFactory.AddReferenceBackedArrayDimensionLengthFacts(
                targetReference,
                valueExpression,
                CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                semanticModel,
                cancellationToken,
                (expression, dimension, model, token) =>
                    TryTranslateArrayDimensionLengthValue(
                        expression,
                        dimension,
                        model,
                        token,
                        out var valueDimensionLength,
                        getSymbolVersion: null,
                        inlineDepth: 0)
                        ? valueDimensionLength
                        : null,
                facts.Add);
        }

        internal static void AddArrayDimensionLengthAssignedValueFacts(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> facts)
        {
            if (SymbolicFactFactory.GetTrackedSymbolType(targetSymbol) is not IArrayTypeSymbol targetArrayType)
            {
                return;
            }

            SymbolicFactFactory.AddArrayDimensionLengthAssignedValueFacts(
                targetArrayType,
                dimension => SymbolicFactFactory.TryCreateArrayDimensionLengthFormula(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol),
                    targetArrayType,
                    dimension,
                    out var targetDimensionLength)
                    ? targetDimensionLength
                    : null,
                valueExpression,
                semanticModel,
                cancellationToken,
                (expression, dimension, model, token) =>
                    TryTranslateArrayDimensionLengthValue(
                        expression,
                        dimension,
                        model,
                        token,
                        out var valueDimensionLength,
                        getSymbolVersion: null,
                        inlineDepth: 0)
                        ? valueDimensionLength
                        : null,
                facts.Add);
        }

        internal static bool TryCreateArrayGetValueIndexesInRangeFormula(
            ExpressionSyntax receiverExpression,
            IArrayTypeSymbol arrayType,
            IReadOnlyList<ExpressionSyntax> indexExpressions,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula inRange)
        {
            inRange = null!;
            if (indexExpressions.Count != arrayType.Rank)
            {
                return false;
            }

            SmtFormula? combined = null;
            for (var dimension = 0; dimension < arrayType.Rank; dimension++)
            {
                if (!TryTranslateIntegerValue(
                        indexExpressions[dimension],
                        semanticModel,
                        cancellationToken,
                        out var indexFormula) ||
                    !TryTranslateArrayGetValueDimensionLength(
                        receiverExpression,
                        arrayType,
                        dimension,
                        semanticModel,
                        cancellationToken,
                        out var lengthFormula) ||
                    lengthFormula is not { Kind: SmtValueKind.Int })
                {
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
                var dimensionInRange = new SmtBinaryFormula(SmtBinaryOperator.And, lowerBound, upperBound);
                combined = combined == null
                    ? dimensionInRange
                    : new SmtBinaryFormula(SmtBinaryOperator.And, combined, dimensionInRange);
            }

            if (combined == null)
            {
                return false;
            }

            inRange = combined;
            return true;
        }

        private static bool TryTranslateArrayGetValueDimensionLength(
            ExpressionSyntax receiverExpression,
            IArrayTypeSymbol arrayType,
            int dimension,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula)
        {
            return TryTranslateArrayDimensionLengthValue(
                receiverExpression,
                dimension,
                semanticModel,
                cancellationToken,
                out lengthFormula);
        }

        internal static bool TryTranslateArrayDimensionLengthValue(
            ExpressionSyntax expression,
            int dimension,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula lengthFormula,
            Func<ISymbol, int>? getSymbolVersion = null,
            int inlineDepth = 0)
        {
            var type = semanticModel.GetTypeInfo(expression, cancellationToken).ConvertedType ??
                semanticModel.GetTypeInfo(expression, cancellationToken).Type;
            if (dimension == 0 &&
                type is IArrayTypeSymbol { Rank: 1 } &&
                TryTranslateBuiltInLengthValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out lengthFormula,
                    getSymbolVersion))
            {
                return true;
            }

            return CSharpSmtFormulaTranslator.TryTranslateArrayDimensionLengthValue(
                expression,
                dimension,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth);
        }

        internal static bool TryCreateCompoundAssignmentFact(
            ISymbol targetSymbol,
            SmtFormula previousValue,
            AssignmentExpressionSyntax assignment,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool rightReferencesTarget,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula) ||
                !TryTranslateIntegerValue(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightValue))
            {
                return false;
            }

            return SymbolicMutationFactFactory.TryCreateCompoundAssignmentFact(
                targetFormula,
                previousValue,
                SmtFormulaReferenceScanner.ContainsVariablePrefix(
                    previousValue,
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol)),
                rightReferencesTarget,
                assignment.Kind(),
                rightValue,
                out fact);
        }

        internal static bool TryCreateIncrementOrDecrementFact(
            ISymbol targetSymbol,
            SmtFormula previousValue,
            int delta,
            out SmtFormula fact)
        {
            fact = null!;
            if (!TryCreateSymbolSmtValue(targetSymbol, out var targetFormula))
            {
                return false;
            }

            return SymbolicMutationFactFactory.TryCreateIncrementOrDecrementFact(
                targetFormula,
                previousValue,
                SmtFormulaReferenceScanner.ContainsVariablePrefix(
                    previousValue,
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol)),
                delta,
                out fact);
        }

        internal static bool TryGetCurrentSymbolValue(
            IReadOnlyList<SmtFormula> facts,
            ISymbol symbol,
            out SmtFormula value)
        {
            value = null!;
            if (!TryCreateSymbolSmtValue(symbol, out var targetFormula))
            {
                return false;
            }

            for (var index = facts.Count - 1; index >= 0; index--)
            {
                if (facts[index] is not SmtBinaryFormula
                    {
                        Operator: SmtBinaryOperator.Equal,
                        Left: var left,
                        Right: var right
                    })
                {
                    continue;
                }

                if (Equals(left, targetFormula) && right.Kind == targetFormula.Kind)
                {
                    value = right;
                    return true;
                }

                if (Equals(right, targetFormula) && left.Kind == targetFormula.Kind)
                {
                    value = left;
                    return true;
                }
            }

            return false;
        }

        private static bool TryCreateSymbolSmtValue(
            ISymbol symbol,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            return SymbolicFactFactory.TryCreateSymbolVariableFormula(
                GetVersionedSmtVariableName(symbol, getSymbolVersion),
                SymbolicFactFactory.GetTrackedSymbolType(symbol),
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                SymbolicTypeFacts.IsReferenceType,
                out formula);
        }

        internal static bool TryCreateSymbolReferenceNullComparison(
            ISymbol symbol,
            bool equalToNull,
            out SmtFormula formula)
        {
            formula = null!;
            if (!SymbolicFactFactory.TryCreateSymbolVariableFormula(
                    SymbolicFactFactory.GetSmtVariableName(symbol),
                    SymbolicFactFactory.GetTrackedSymbolType(symbol),
                    SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                    SymbolicTypeFacts.IsReferenceType,
                    out var valueFormula) ||
                valueFormula is not { Kind: SmtValueKind.Reference })
            {
                return false;
            }

            formula = SmtFormulaFactory.CreateReferenceNullComparison(valueFormula, equalToNull);
            return true;
        }

        internal static bool TryCreateSymbolNumericZeroComparison(
            ISymbol symbol,
            out SmtFormula formula)
        {
            formula = null!;
            var type = SymbolicFactFactory.GetTrackedSymbolType(symbol);
            if (type == null ||
                (!SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType(type) &&
                 type.SpecialType != SpecialType.System_Decimal))
            {
                return false;
            }

            formula = SmtFormulaFactory.CreateIntegerEqualsZero(
                SmtFormulaFactory.CreateIntVariable(SymbolicFactFactory.GetSmtVariableName(symbol)));
            return true;
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

        private static ImmutableArray<SmtFormula> BuildStructuralPathConditionSnapshot(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var pathConditions = CollectAncestorReachabilityConditions(
                    site,
                    semanticModel,
                    cancellationToken)
                .ToList();
            AddAncestorSwitchArrayLengthCountAliasFacts(site, semanticModel, cancellationToken, pathConditions);
            return pathConditions.ToImmutableArray();
        }

        private sealed class StructuralPathConditionCache
        {
            internal ConcurrentDictionary<PathConditionCacheKey, ImmutableArray<SmtFormula>> Values { get; } = new();
        }

        private readonly struct PathConditionCacheKey : IEquatable<PathConditionCacheKey>
        {
            public PathConditionCacheKey(
                int siteStart,
                int siteLength,
                int siteRawKind,
                bool includeCurrentStatementCompletionFacts)
            {
                SiteStart = siteStart;
                SiteLength = siteLength;
                SiteRawKind = siteRawKind;
                IncludeCurrentStatementCompletionFacts = includeCurrentStatementCompletionFacts;
            }

            public int SiteStart { get; }
            public int SiteLength { get; }
            public int SiteRawKind { get; }
            public bool IncludeCurrentStatementCompletionFacts { get; }

            public bool Equals(PathConditionCacheKey other)
            {
                return SiteStart == other.SiteStart &&
                    SiteLength == other.SiteLength &&
                    SiteRawKind == other.SiteRawKind &&
                    IncludeCurrentStatementCompletionFacts == other.IncludeCurrentStatementCompletionFacts;
            }

            public override bool Equals(object? obj)
            {
                return obj is PathConditionCacheKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = SiteStart;
                    hash = (hash * 397) ^ SiteLength;
                    hash = (hash * 397) ^ SiteRawKind;
                    hash = (hash * 397) ^ (IncludeCurrentStatementCompletionFacts ? 1 : 0);
                    return hash;
                }
            }
        }
    }
}
