using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
                return status is SymbolicProofStatus.ProvenFalse or SymbolicProofStatus.Unreachable;
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
                return status is SymbolicProofStatus.ProvenTrue or SymbolicProofStatus.Unreachable;
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
                CSharpSmtFormulaTranslator.TryCollectDomainFacts(
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
            CSharpSmtFormulaTranslator.TryCollectBranchAssumptions(
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
            if (factFormula == null)
            {
                throw new ArgumentNullException(nameof(factFormula));
            }

            return ClassifyWithFormulaFallback(
                smtAnalysis,
                service => service.ClassifyImplication(pathConditions, factFormula));
        }

        internal static SymbolicIrProofResult ClassifyFormulaReachability(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            var result = ClassifyFormulaPathFeasibility(pathConditions, smtAnalysis);
            return SymbolicIrProofResult.FromReachability(result, CreateBudgetInfo(smtAnalysis));
        }

        internal static SymbolicIrProofResult ClassifyFormulaConditionTruth(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula conditionFormula,
            SmtAnalysisService? smtAnalysis)
        {
            if (conditionFormula == null)
            {
                throw new ArgumentNullException(nameof(conditionFormula));
            }

            var trueProof = ClassifyImplication(pathConditions, conditionFormula, smtAnalysis);
            if (trueProof.Outcome == PurityProofOutcome.ProvablyPure)
            {
                var status = string.Equals(trueProof.Reason, "path_unsatisfiable", StringComparison.Ordinal)
                    ? SymbolicProofStatus.Unreachable
                    : SymbolicProofStatus.ProvenTrue;
                return SymbolicIrProofResult.FromConditionTruth(
                    trueProof,
                    status,
                    CreateBudgetInfo(smtAnalysis));
            }

            var falseProof = ClassifyImplication(
                pathConditions,
                new SmtUnaryFormula(SmtUnaryOperator.Not, conditionFormula),
                smtAnalysis);
            if (falseProof.Outcome == PurityProofOutcome.ProvablyPure)
            {
                var status = string.Equals(falseProof.Reason, "path_unsatisfiable", StringComparison.Ordinal)
                    ? SymbolicProofStatus.Unreachable
                    : SymbolicProofStatus.ProvenFalse;
                return SymbolicIrProofResult.FromConditionTruth(
                    falseProof,
                    status,
                    CreateBudgetInfo(smtAnalysis));
            }

            return SymbolicIrProofResult.FromConditionTruth(
                trueProof,
                SymbolicProofStatus.Unknown,
                CreateBudgetInfo(smtAnalysis));
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
            if (EvaluateConditionTruthWithIr(
                    expression,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis,
                    basePathConditions) is { } irTruth)
            {
                return irTruth;
            }

            if (!CSharpSmtFormulaTranslator.TryTranslate(expression, semanticModel, cancellationToken, out var formula) ||
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

            CSharpSmtFormulaTranslator.TryCollectDomainFacts(expression, semanticModel, cancellationToken, basePathConditions);
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

            var pathConditions = CollectPathConditionsAt(forStatement, semanticModel, cancellationToken);
            if (!CSharpSmtFormulaTranslator.TryTranslate(forStatement.Condition, semanticModel, cancellationToken, out var formula) ||
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

            CSharpSmtFormulaTranslator.TryCollectDomainFacts(forStatement.Condition, semanticModel, cancellationToken, pathConditions);
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
            if (branchCondition == null)
            {
                throw new ArgumentNullException(nameof(branchCondition));
            }

            return ClassifyWithFormulaFallback(
                smtAnalysis,
                service => service.Classify(new PurityProofQuery(
                    pathConditions.ToArray(),
                    new PurityHazard(PurityHazardKind.BranchReachability, branchCondition))));
        }

        internal static PurityProofResult ClassifyPathFeasibility(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyFormulaPathFeasibility(pathConditions, smtAnalysis);
        }

        private static PurityProofResult ClassifyFormulaPathFeasibility(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyWithFormulaFallback(
                smtAnalysis,
                service => service.ClassifyPathFeasibility(pathConditions));
        }

        private static PurityProofResult ClassifyWithFormulaFallback(
            SmtAnalysisService? smtAnalysis,
            Func<SmtAnalysisService, PurityProofResult> classify)
        {
            if (smtAnalysis != null)
            {
                return classify(smtAnalysis);
            }

            using var fallback = new SmtAnalysisService(SmtAnalysisOptions.Default);
            return classify(fallback);
        }

        private static SymbolicBudgetInfo? CreateBudgetInfo(SmtAnalysisService? service)
        {
            if (service == null)
            {
                return null;
            }

            return new SymbolicBudgetInfo(
                service.Options.MaxPathConditions,
                service.Options.MaxExpressionNodes,
                (int)service.Options.QueryTimeout.TotalMilliseconds,
                (int)service.Options.MethodBudget.TotalMilliseconds,
                service.ExecutedQueryCount,
                service.CacheEntryCount);
        }

        internal static bool TryCreateArrayLengthCountAliasFact(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula aliasFact,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            if (semanticModel.GetTypeInfo(expression, cancellationToken).Type is IArrayTypeSymbol &&
                CSharpSmtFormulaTranslator.TryTranslateValue(
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

        internal static bool TryCreateReferenceNullComparison(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool equalToNull,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion) ||
                valueFormula is not { Kind: SmtValueKind.Reference })
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
            if (!CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion) ||
                valueFormula is not { Kind: SmtValueKind.Int })
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
            if (!CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion) ||
                valueFormula is not { Kind: SmtValueKind.Int })
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
            if (!CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion) ||
                valueFormula is not { Kind: SmtValueKind.Int })
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
            return CSharpSmtFormulaTranslator.TryTranslateNullableHasValue(
                expression,
                semanticModel,
                cancellationToken,
                out formula);
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
            if (!CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion) ||
                valueFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            formula = SmtFormulaFactory.CreateIntegerInRange(valueFormula, minValue, maxValue);
            return true;
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
            if (!CSharpSmtFormulaTranslator.TryTranslateValue(
                    leftExpression,
                    semanticModel,
                    cancellationToken,
                    out var leftFormula,
                    getSymbolVersion) ||
                leftFormula is not { Kind: SmtValueKind.Int } ||
                !CSharpSmtFormulaTranslator.TryTranslateValue(
                    rightExpression,
                    semanticModel,
                    cancellationToken,
                    out var rightFormula,
                    getSymbolVersion) ||
                rightFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var resultFormula = SmtFormulaFactory.CreateIntegerBinaryTerm(smtOperator, leftFormula, rightFormula);
            formula = SmtFormulaFactory.CreateIntegerInRange(resultFormula, minValue, maxValue);
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
            if (!CSharpSmtFormulaTranslator.TryTranslateValue(
                    expression,
                    semanticModel,
                    cancellationToken,
                    out var operandFormula,
                    getSymbolVersion) ||
                operandFormula is not { Kind: SmtValueKind.Int })
            {
                return false;
            }

            var resultFormula = SmtFormulaFactory.CreateIntegerUnaryTerm(smtOperator, operandFormula);
            formula = SmtFormulaFactory.CreateIntegerInRange(resultFormula, minValue, maxValue);
            return true;
        }

        internal static bool TryCreateAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            fact = null!;
            if (!SymbolicFactFactory.TryCreateSymbolVariableFormula(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol),
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                    SymbolicTypeFacts.IsReferenceType,
                    out var targetFormula) ||
                !CSharpSmtFormulaTranslator.TryTranslateValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueFormula,
                    getSymbolVersion) ||
                valueFormula == null ||
                !SymbolicFactFactory.CanCompareSmtValues(targetFormula, valueFormula))
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
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            fact = null!;
            if (!SymbolicFactFactory.TryCreateBuiltInLengthFormula(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol),
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    out var targetLengthFormula) ||
                !CSharpSmtFormulaTranslator.TryTranslateBuiltInLengthValue(
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
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            fact = null!;
            if (!SymbolicFactFactory.TryCreateStringContentFormula(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol),
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    out var targetStringFormula) ||
                !CSharpSmtFormulaTranslator.TryTranslateStringValue(
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

        internal static bool TryCreateStringNonNullAssignedValueFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact)
        {
            fact = null!;
            if (SymbolicFactFactory.GetTrackedSymbolType(targetSymbol)?.SpecialType != SpecialType.System_String ||
                !SymbolicFactFactory.TryCreateSymbolVariableFormula(
                    SymbolicFactFactory.GetSmtVariableName(targetSymbol),
                    SymbolicFactFactory.GetTrackedSymbolType(targetSymbol),
                    SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                    SymbolicTypeFacts.IsReferenceType,
                    out var targetReferenceFormula) ||
                targetReferenceFormula is not { Kind: SmtValueKind.Reference } ||
                !CSharpSmtFormulaTranslator.TryCreateStringNonNullFormula(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueNonNullFormula) ||
                valueNonNullFormula == null)
            {
                return false;
            }

            fact = SmtFormulaFactory.CreateEquality(
                SmtFormulaFactory.CreateReferenceNullComparison(targetReferenceFormula, isNull: false),
                valueNonNullFormula);
            return true;
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

            if (CSharpSmtFormulaTranslator.TryTranslateNullableValueParts(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var parts,
                    getSymbolVersion: null,
                    inlineDepth: 0))
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
                         semanticModel,
                         cancellationToken,
                         out var wrappedValueFormula))
            {
                facts.Add(targetHasValue);

                if (SymbolicFactFactory.CanCompareSmtValues(targetValue, wrappedValueFormula))
                {
                    facts.Add(SymbolicFactFactory.CreateAssignedValueFact(targetValue, wrappedValueFormula));
                }
            }
        }

        private static bool TryTranslateNullableWrappedValueForUnderlyingType(
            ExpressionSyntax valueExpression,
            ITypeSymbol underlyingType,
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

            if (CSharpSmtFormulaTranslator.TryTranslateValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var translatedValue,
                    getSymbolVersion: null,
                    inlineDepth: 0) &&
                translatedValue != null)
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
            out SmtFormula fact)
        {
            fact = null!;
            return TryCreateSymbolSmtValue(targetSymbol, out var targetReference) &&
                SymbolicFactFactory.TryCreateReferenceBackedLengthFact(
                    targetReference,
                    valueExpression,
                    CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                    semanticModel,
                    cancellationToken,
                    (expression, model, token) =>
                        CSharpSmtFormulaTranslator.TryTranslateBuiltInLengthValue(
                            expression,
                            model,
                            token,
                            out var formula,
                            getSymbolVersion: null)
                            ? formula
                            : null,
                    out fact);
        }

        internal static bool TryCreateReferenceBackedStringContentFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            out SmtFormula fact)
        {
            fact = null!;
            return TryCreateSymbolSmtValue(targetSymbol, out var targetReference) &&
                SymbolicFactFactory.TryCreateReferenceBackedStringContentFact(
                    targetReference,
                    valueExpression,
                    CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(valueExpression),
                    semanticModel,
                    cancellationToken,
                    (expression, model, token) =>
                        CSharpSmtFormulaTranslator.TryTranslateStringValue(
                            expression,
                            model,
                            token,
                            out var valueString,
                            getSymbolVersion: null) &&
                        valueString != null
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
                    CSharpSmtFormulaTranslator.TryTranslateArrayDimensionLengthValue(
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
                    CSharpSmtFormulaTranslator.TryTranslateArrayDimensionLengthValue(
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
                !CSharpSmtFormulaTranslator.TryTranslateValue(
                    assignment.Right,
                    semanticModel,
                    cancellationToken,
                    out var rightValue,
                    getSymbolVersion: null,
                    inlineDepth: 0))
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

        private static bool TryCreateSymbolSmtValue(ISymbol symbol, out SmtFormula formula)
        {
            return SymbolicFactFactory.TryCreateSymbolVariableFormula(
                SymbolicFactFactory.GetSmtVariableName(symbol),
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
