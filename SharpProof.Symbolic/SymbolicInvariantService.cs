using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;

namespace SharpProof.Symbolic
{
    internal sealed class SymbolicInvariantService
    {
        public SymbolicInvariantSnapshot GetInvariantsAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default,
            bool includeCurrentStatementCompletionFacts = false)
        {
            IReadOnlyList<SmtFormula> formulas = CollectInvariantsAt(
                site,
                semanticModel,
                cancellationToken,
                includeCurrentStatementCompletionFacts);
            var facts = FormatFacts(formulas);
            var mergedInvariantText = FormatMergedInvariant(formulas);

            return new SymbolicInvariantSnapshot(site.SpanStart, facts, mergedInvariantText);
        }

        public SymbolicProgramPointAnalysis AnalyzeAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            SmtAnalysisService? smtAnalysis = null,
            CancellationToken cancellationToken = default,
            bool includeCurrentStatementCompletionFacts = false)
        {
            IReadOnlyList<SmtFormula> formulas = CollectInvariantsAt(
                site,
                semanticModel,
                cancellationToken,
                includeCurrentStatementCompletionFacts);
            var pathState = SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                semanticModel,
                cancellationToken);
            pathState = SymbolicProgramPointFacts.MergeStates(
                pathState,
                SymbolicProgramPointFacts.CollectPriorAssignmentState(
                    site,
                    semanticModel,
                    cancellationToken,
                    includeCurrentStatementCompletionFacts));
            formulas = MergeEncodedStatePathConditions(formulas, pathState);
            return CreateAnalysis(site.SpanStart, formulas, pathState, smtAnalysis, site);
        }

        public SymbolicProgramPointAnalysis AnalyzeForInitialEntry(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            SmtAnalysisService? smtAnalysis = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SmtFormula> formulas = SymbolicReachabilityService.CollectForInitialEntryPathConditions(
                forStatement,
                semanticModel,
                cancellationToken);
            var pathState = SymbolicProgramPointFacts.CollectForInitialEntryState(
                forStatement,
                semanticModel,
                cancellationToken);
            formulas = MergeEncodedStatePathConditions(formulas, pathState);

            return CreateAnalysis(forStatement.SpanStart, formulas, pathState, smtAnalysis, forStatement);
        }

        private static IReadOnlyList<SmtFormula> MergeEncodedStatePathConditions(
            IReadOnlyList<SmtFormula> formulas,
            SymbolicState pathState)
        {
            if (pathState == null ||
                pathState.PathConditions.Length == 0 ||
                !SymbolicReachabilityService.TryEncodeStatePathConditions(pathState, out var encodedPathConditions) ||
                encodedPathConditions.IsDefaultOrEmpty)
            {
                return formulas;
            }

            var merged = new List<SmtFormula>(formulas.Count + encodedPathConditions.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var formula in formulas)
            {
                if (formula == null)
                {
                    continue;
                }

                var key = formula.ToString() ?? string.Empty;
                if (seen.Add(key))
                {
                    merged.Add(formula);
                }
            }

            foreach (var formula in encodedPathConditions)
            {
                if (formula == null)
                {
                    continue;
                }

                var key = formula.ToString() ?? string.Empty;
                if (seen.Add(key))
                {
                    merged.Add(formula);
                }
            }

            return merged;
        }

        public SymbolicInvariantImplicationResult ProveImplicationAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            SymbolicCondition condition,
            SmtAnalysisService? smtAnalysis,
            CancellationToken cancellationToken = default,
            bool includeCurrentStatementCompletionFacts = false)
        {
            if (site == null)
            {
                throw new ArgumentNullException(nameof(site));
            }

            if (semanticModel == null)
            {
                throw new ArgumentNullException(nameof(semanticModel));
            }

            var analysis = AnalyzeAt(
                site,
                semanticModel,
                smtAnalysis,
                cancellationToken,
                includeCurrentStatementCompletionFacts);
            return ProveImplication(analysis, condition, smtAnalysis);
        }

        public static SymbolicInvariantImplicationResult ProveImplication(
            SymbolicProgramPointAnalysis analysis,
            SymbolicCondition condition,
            SmtAnalysisService? smtAnalysis)
        {
            if (analysis == null)
            {
                throw new ArgumentNullException(nameof(analysis));
            }

            if (condition == null)
            {
                throw new ArgumentNullException(nameof(condition));
            }

            var conditionText = FormatCondition(condition);
            if (smtAnalysis == null)
            {
                return new SymbolicInvariantImplicationResult(
                    analysis.SpanStart,
                    conditionText,
                    SymbolicTruthValue.Unknown,
                    "smt_required",
                    analysis.Reachability,
                    analysis.ReachabilityReason,
                    analysis.SmtDiagnostics);
            }

            if (analysis.Reachability == SymbolicReachability.Unreachable)
            {
                return new SymbolicInvariantImplicationResult(
                    analysis.SpanStart,
                    conditionText,
                    SymbolicTruthValue.Unreachable,
                    analysis.ReachabilityReason,
                    analysis.Reachability,
                    analysis.ReachabilityReason,
                    SymbolicSmtDiagnostics.FromService(smtAnalysis));
            }

            var truthProof = SymbolicReachabilityService.ClassifyStateConditionTruth(
                analysis.PathState,
                condition,
                smtAnalysis);
            if (truthProof.Info.Status == SymbolicProofStatus.ProvenTrue)
            {
                return new SymbolicInvariantImplicationResult(
                    analysis.SpanStart,
                    conditionText,
                    SymbolicTruthValue.ProvenTrue,
                    truthProof.Info.Reason,
                    analysis.Reachability,
                    analysis.ReachabilityReason,
                    SymbolicSmtDiagnostics.FromService(smtAnalysis));
            }

            if (truthProof.Info.Status == SymbolicProofStatus.ProvenFalse)
            {
                return new SymbolicInvariantImplicationResult(
                    analysis.SpanStart,
                    conditionText,
                    SymbolicTruthValue.ProvenFalse,
                    truthProof.Info.Reason,
                    analysis.Reachability,
                    analysis.ReachabilityReason,
                    SymbolicSmtDiagnostics.FromService(smtAnalysis));
            }

            if (SymbolicIrFormulaEncoder.TryEncode(condition, out var conditionFormula))
            {
                var formulaTruth = SymbolicReachabilityService.ClassifyFormulaConditionTruthWithIrFallback(
                    analysis.PathConditions,
                    conditionFormula,
                    analysis.SourceNode,
                    smtAnalysis,
                    "invariant.implication",
                    "invariant-implication");
                if (formulaTruth.Info.Status == SymbolicProofStatus.ProvenTrue)
                {
                    return new SymbolicInvariantImplicationResult(
                        analysis.SpanStart,
                        conditionText,
                        SymbolicTruthValue.ProvenTrue,
                        formulaTruth.Info.Reason,
                        analysis.Reachability,
                        analysis.ReachabilityReason,
                        SymbolicSmtDiagnostics.FromService(smtAnalysis));
                }

                if (formulaTruth.Info.Status == SymbolicProofStatus.ProvenFalse)
                {
                    return new SymbolicInvariantImplicationResult(
                        analysis.SpanStart,
                        conditionText,
                        SymbolicTruthValue.ProvenFalse,
                        formulaTruth.Info.Reason,
                        analysis.Reachability,
                        analysis.ReachabilityReason,
                        SymbolicSmtDiagnostics.FromService(smtAnalysis));
                }

                return new SymbolicInvariantImplicationResult(
                    analysis.SpanStart,
                    conditionText,
                    SymbolicTruthValue.Unknown,
                    formulaTruth.Info.Reason,
                    analysis.Reachability,
                    analysis.ReachabilityReason,
                    SymbolicSmtDiagnostics.FromService(smtAnalysis));
            }

            return new SymbolicInvariantImplicationResult(
                analysis.SpanStart,
                conditionText,
                SymbolicTruthValue.Unknown,
                truthProof.Info.Reason,
                analysis.Reachability,
                analysis.ReachabilityReason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }

        private static string FormatCondition(SymbolicCondition condition)
        {
            return SymbolicIrFormulaEncoder.TryEncode(condition, out var formula)
                ? SymbolicFormulaDisplay.Format(formula)
                : condition.ToString() ?? string.Empty;
        }

        internal static SmtFormula ConjoinPathConditions(IReadOnlyList<SmtFormula> pathConditions)
        {
            if (pathConditions.Count == 0)
            {
                return new SmtBooleanConstant(true);
            }

            var merged = pathConditions[0];
            for (var index = 1; index < pathConditions.Count; index++)
            {
                merged = new SmtBinaryFormula(SmtBinaryOperator.And, merged, pathConditions[index]);
            }

            return merged;
        }

        internal static string FormatMergedInvariant(IReadOnlyList<SmtFormula> pathConditions)
        {
            return SymbolicFormulaDisplay.FormatMergedInvariant(pathConditions);
        }

        public static SymbolicInvariantFactSummary MergeInvariantFacts(IEnumerable<IEnumerable<string>> factSets)
        {
            if (factSets == null)
            {
                throw new ArgumentNullException(nameof(factSets));
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var facts = new List<string>();
            foreach (var factSet in factSets)
            {
                if (factSet == null)
                {
                    continue;
                }

                foreach (var fact in factSet)
                {
                    if (!string.IsNullOrWhiteSpace(fact) && seen.Add(fact))
                    {
                        facts.Add(fact);
                    }
                }
            }

            return new SymbolicInvariantFactSummary(facts);
        }

        public static string FormatMergedInvariantFacts(IReadOnlyList<string> facts)
        {
            if (facts == null)
            {
                throw new ArgumentNullException(nameof(facts));
            }

            if (facts.Count == 0)
            {
                return "true";
            }

            if (facts.Count == 1)
            {
                return facts[0];
            }

            return string.Join(" && ", facts.Select(static fact => "(" + fact + ")"));
        }

        private static IReadOnlyList<string> FormatFacts(IEnumerable<SmtFormula> formulas)
        {
            return formulas.Select(static fact => SymbolicFormulaDisplay.Format(fact)).ToArray();
        }

        private static SmtFormula[] CollectInvariantsAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool includeCurrentStatementCompletionFacts = false)
        {
            return SymbolicReachabilityService
                .CollectPathConditionsAt(
                    site,
                    semanticModel,
                    cancellationToken,
                    includeCurrentStatementCompletionFacts)
                .ToArray();
        }

        private static SymbolicProgramPointAnalysis CreateAnalysis(
            int spanStart,
            IReadOnlyList<SmtFormula> formulas,
            SymbolicState pathState,
            SmtAnalysisService? smtAnalysis,
            SyntaxNode sourceNode)
        {
            if (formulas.Count == 0 &&
                pathState.IsContradictory)
            {
                formulas = new[] { new SmtBooleanConstant(false) };
            }

            var stateProof = smtAnalysis == null || !HasPathStateFacts(pathState)
                ? null
                : SymbolicReachabilityService.ClassifyStateFeasibility(pathState, smtAnalysis);
            if (stateProof?.Info.Status == SymbolicProofStatus.Unreachable)
            {
                return new SymbolicProgramPointAnalysis(
                    spanStart,
                    formulas,
                    pathState,
                    SymbolicReachability.Unreachable,
                    stateProof.Info.Reason,
                    SymbolicSmtDiagnostics.FromService(smtAnalysis),
                    sourceNode);
            }

            if (formulas.Count == 0)
            {
                if (stateProof != null)
                {
                    return new SymbolicProgramPointAnalysis(
                        spanStart,
                        formulas,
                        pathState,
                        MapReachability(stateProof.Info.Status),
                        stateProof.Info.Reason,
                        SymbolicSmtDiagnostics.FromService(smtAnalysis),
                        sourceNode);
                }

                return new SymbolicProgramPointAnalysis(
                    spanStart,
                    formulas,
                    pathState,
                    SymbolicReachability.Reachable,
                    "no_path_conditions",
                    SymbolicSmtDiagnostics.FromService(smtAnalysis),
                    sourceNode);
            }

            var proof = smtAnalysis == null
                ? null
                : SymbolicReachabilityService.ClassifyStateFeasibilityWithFormulaFallback(
                    pathState,
                    formulas,
                    smtAnalysis);

            return new SymbolicProgramPointAnalysis(
                spanStart,
                formulas,
                pathState,
                proof == null ? SymbolicReachability.NotChecked : MapReachability(proof.Info.Status),
                proof?.Info.Reason ?? "reachability_not_checked",
                SymbolicSmtDiagnostics.FromService(smtAnalysis),
                sourceNode);
        }

        private static bool HasPathStateFacts(SymbolicState pathState)
        {
            return pathState.Facts.Length != 0 || pathState.PathConditions.Length != 0;
        }

        private static SymbolicReachability MapReachability(SymbolicProofStatus status)
        {
            return status switch
            {
                SymbolicProofStatus.Reachable => SymbolicReachability.Reachable,
                SymbolicProofStatus.Unreachable => SymbolicReachability.Unreachable,
                SymbolicProofStatus.Unknown => SymbolicReachability.Unknown,
                _ => SymbolicReachability.NotChecked,
            };
        }
    }

    internal sealed class SymbolicInvariantSnapshot
    {
        internal SymbolicInvariantSnapshot(
            int spanStart,
            IReadOnlyList<string> facts,
            string mergedInvariantText)
        {
            SpanStart = spanStart;
            Facts = facts ?? throw new ArgumentNullException(nameof(facts));
            MergedInvariantText = mergedInvariantText ?? throw new ArgumentNullException(nameof(mergedInvariantText));
        }

        public int SpanStart { get; }

        public IReadOnlyList<string> Facts { get; }

        public string MergedInvariantText { get; }
    }

    public sealed class SymbolicInvariantFactSummary
    {
        public SymbolicInvariantFactSummary(IReadOnlyList<string> facts)
        {
            Facts = facts ?? throw new ArgumentNullException(nameof(facts));
            MergedInvariantText = SymbolicInvariantService.FormatMergedInvariantFacts(facts);
        }

        public IReadOnlyList<string> Facts { get; }

        public string MergedInvariantText { get; }
    }

    internal sealed class SymbolicInvariantImplicationResult
    {
        public SymbolicInvariantImplicationResult(
            int spanStart,
            string condition,
            SymbolicTruthValue truthValue,
            string reason,
            SymbolicReachability reachability,
            string reachabilityReason,
            SymbolicSmtDiagnostics smtDiagnostics)
        {
            SpanStart = spanStart;
            Condition = condition ?? throw new ArgumentNullException(nameof(condition));
            TruthValue = truthValue;
            Reason = reason ?? string.Empty;
            Reachability = reachability;
            ReachabilityReason = reachabilityReason ?? string.Empty;
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public int SpanStart { get; }

        public string Condition { get; }

        public SymbolicTruthValue TruthValue { get; }

        public string Reason { get; }

        public SymbolicReachability Reachability { get; }

        public string ReachabilityReason { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }
    }

    internal sealed class SymbolicProgramPointAnalysis
    {
        public SymbolicProgramPointAnalysis(
            int spanStart,
            IReadOnlyList<SmtFormula> pathConditions,
            SymbolicState? pathState,
            SymbolicReachability reachability,
            string reachabilityReason,
            SymbolicSmtDiagnostics? smtDiagnostics,
            SyntaxNode sourceNode)
        {
            SpanStart = spanStart;
            PathConditions = pathConditions;
            PathState = pathState ?? new SymbolicState();
            SourceNode = sourceNode ?? throw new ArgumentNullException(nameof(sourceNode));
            Facts = pathConditions.Select(SymbolicFormulaDisplay.Format).ToArray();
            MergedInvariantText = SymbolicFormulaDisplay.FormatMergedInvariant(pathConditions);
            Reachability = reachability;
            ReachabilityReason = reachabilityReason;
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public int SpanStart { get; }

        internal IReadOnlyList<SmtFormula> PathConditions { get; }

        public SymbolicState PathState { get; }

        internal SyntaxNode SourceNode { get; }

        public IReadOnlyList<string> Facts { get; }

        public string MergedInvariantText { get; }

        public SymbolicReachability Reachability { get; }

        public string ReachabilityReason { get; }

        public SymbolicSmtDiagnostics SmtDiagnostics { get; }
    }

    public sealed class SymbolicSmtDiagnostics
    {
        public static readonly SymbolicSmtDiagnostics NotConfigured = new(
            isConfigured: false,
            mode: SmtAnalysisMode.Off,
            isEnabled: false,
            queryTimeoutMs: 0,
            methodBudgetMs: 0,
            maxPathConditions: 0,
            maxExpressionNodes: 0,
            executedQueryCount: 0,
            cacheEntryCount: 0);

        public SymbolicSmtDiagnostics(
            bool isConfigured,
            SmtAnalysisMode mode,
            bool isEnabled,
            int queryTimeoutMs,
            int methodBudgetMs,
            int maxPathConditions,
            int maxExpressionNodes,
            int executedQueryCount,
            int cacheEntryCount)
        {
            IsConfigured = isConfigured;
            Mode = mode;
            IsEnabled = isEnabled;
            QueryTimeoutMs = queryTimeoutMs;
            MethodBudgetMs = methodBudgetMs;
            MaxPathConditions = maxPathConditions;
            MaxExpressionNodes = maxExpressionNodes;
            ExecutedQueryCount = executedQueryCount;
            CacheEntryCount = cacheEntryCount;
        }

        public bool IsConfigured { get; }

        public SmtAnalysisMode Mode { get; }

        public bool IsEnabled { get; }

        public int QueryTimeoutMs { get; }

        public int MethodBudgetMs { get; }

        public int MaxPathConditions { get; }

        public int MaxExpressionNodes { get; }

        public int ExecutedQueryCount { get; }

        public int CacheEntryCount { get; }

        public static SymbolicSmtDiagnostics FromService(SmtAnalysisService? smtAnalysis)
        {
            if (smtAnalysis == null)
            {
                return NotConfigured;
            }

            return new SymbolicSmtDiagnostics(
                true,
                smtAnalysis.Options.Mode,
                smtAnalysis.Options.IsEnabled,
                checked((int)smtAnalysis.Options.QueryTimeout.TotalMilliseconds),
                checked((int)smtAnalysis.Options.MethodBudget.TotalMilliseconds),
                smtAnalysis.Options.MaxPathConditions,
                smtAnalysis.Options.MaxExpressionNodes,
                smtAnalysis.ExecutedQueryCount,
                smtAnalysis.CacheEntryCount);
        }
    }

    public enum SymbolicReachability
    {
        NotChecked,
        Unknown,
        Reachable,
        Unreachable,
    }
}
