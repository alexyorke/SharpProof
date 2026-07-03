using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    internal sealed class SymbolicInvariantService
    {
        public SymbolicInvariantSnapshot GetInvariantsAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default,
            bool includeCurrentStatementCompletionFacts = false)
        {
            var formulas = CollectInvariantsAt(
                site,
                semanticModel,
                cancellationToken,
                includeCurrentStatementCompletionFacts);

            return new SymbolicInvariantSnapshot(site.SpanStart, formulas);
        }

        public SymbolicProgramPointAnalysis AnalyzeAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            SmtAnalysisService? smtAnalysis = null,
            CancellationToken cancellationToken = default,
            bool includeCurrentStatementCompletionFacts = false)
        {
            var formulas = CollectInvariantsAt(
                site,
                semanticModel,
                cancellationToken,
                includeCurrentStatementCompletionFacts);
            var pathState = SymbolicProgramPointFacts.CollectAncestorReachabilityState(
                site,
                semanticModel,
                cancellationToken);
            return CreateAnalysis(site.SpanStart, formulas, pathState, smtAnalysis);
        }

        public SymbolicInvariantSnapshot GetForInitialEntryInvariants(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            var formulas = SymbolicReachabilityService.CollectForInitialEntryPathConditions(
                forStatement,
                semanticModel,
                cancellationToken);

            return new SymbolicInvariantSnapshot(forStatement.SpanStart, formulas);
        }

        public SymbolicProgramPointAnalysis AnalyzeForInitialEntry(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            SmtAnalysisService? smtAnalysis = null,
            CancellationToken cancellationToken = default)
        {
            var formulas = SymbolicReachabilityService.CollectForInitialEntryPathConditions(
                forStatement,
                semanticModel,
                cancellationToken);

            return CreateAnalysis(forStatement.SpanStart, formulas, new SymbolicState(), smtAnalysis);
        }

        public SymbolicInvariantImplicationResult ProveImplicationAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            SmtFormula condition,
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
            SmtFormula condition,
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

            var conditionText = condition.ToString() ?? string.Empty;
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

            var trueProof = SymbolicReachabilityService.ClassifyImplication(
                analysis.PathConditions,
                condition,
                smtAnalysis);
            if (trueProof.Outcome == PurityProofOutcome.ProvablyPure)
            {
                return new SymbolicInvariantImplicationResult(
                    analysis.SpanStart,
                    conditionText,
                    SymbolicTruthValue.ProvenTrue,
                    trueProof.Reason,
                    analysis.Reachability,
                    analysis.ReachabilityReason,
                    SymbolicSmtDiagnostics.FromService(smtAnalysis));
            }

            var falseProof = SymbolicReachabilityService.ClassifyImplication(
                analysis.PathConditions,
                new SmtUnaryFormula(SmtUnaryOperator.Not, condition),
                smtAnalysis);
            if (falseProof.Outcome == PurityProofOutcome.ProvablyPure)
            {
                return new SymbolicInvariantImplicationResult(
                    analysis.SpanStart,
                    conditionText,
                    SymbolicTruthValue.ProvenFalse,
                    falseProof.Reason,
                    analysis.Reachability,
                    analysis.ReachabilityReason,
                    SymbolicSmtDiagnostics.FromService(smtAnalysis));
            }

            return new SymbolicInvariantImplicationResult(
                analysis.SpanStart,
                conditionText,
                SymbolicTruthValue.Unknown,
                falseProof.Reason,
                analysis.Reachability,
                analysis.ReachabilityReason,
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
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
            return pathConditions.Count == 0
                ? "true"
                : ConjoinPathConditions(pathConditions).ToString() ?? "true";
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
            SmtAnalysisService? smtAnalysis)
        {
            if (formulas.Count == 0)
            {
                return new SymbolicProgramPointAnalysis(
                    spanStart,
                    formulas,
                    pathState,
                    SymbolicReachability.Reachable,
                    "no_path_conditions",
                    SymbolicSmtDiagnostics.FromService(smtAnalysis));
            }

            var proof = smtAnalysis == null ? null : SymbolicReachabilityService.ClassifyPathFeasibility(formulas, smtAnalysis);
            var reachability = proof?.PathFeasibility switch
            {
                Feasibility.Satisfiable => SymbolicReachability.Reachable,
                Feasibility.Unsatisfiable => SymbolicReachability.Unreachable,
                Feasibility.Unknown => SymbolicReachability.Unknown,
                _ => SymbolicReachability.NotChecked,
            };

            return new SymbolicProgramPointAnalysis(
                spanStart,
                formulas,
                pathState,
                reachability,
                proof?.Reason ?? "reachability_not_checked",
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }
    }

    internal sealed class SymbolicInvariantSnapshot
    {
        public SymbolicInvariantSnapshot(int spanStart, IReadOnlyList<SmtFormula> formulas)
        {
            SpanStart = spanStart;
            Formulas = formulas;
            Facts = formulas.Select(static fact => fact.ToString() ?? string.Empty).ToArray();
            MergedInvariant = SymbolicInvariantService.ConjoinPathConditions(formulas);
            MergedInvariantText = SymbolicInvariantService.FormatMergedInvariant(formulas);
        }

        public int SpanStart { get; }

        public IReadOnlyList<SmtFormula> Formulas { get; }

        public IReadOnlyList<string> Facts { get; }

        public SmtFormula MergedInvariant { get; }

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
            SymbolicSmtDiagnostics? smtDiagnostics = null)
        {
            SpanStart = spanStart;
            PathConditions = pathConditions;
            PathState = pathState ?? new SymbolicState();
            Facts = pathConditions.Select(static fact => fact.ToString() ?? string.Empty).ToArray();
            MergedInvariant = SymbolicInvariantService.ConjoinPathConditions(pathConditions);
            MergedInvariantText = SymbolicInvariantService.FormatMergedInvariant(pathConditions);
            Reachability = reachability;
            ReachabilityReason = reachabilityReason;
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public int SpanStart { get; }

        public IReadOnlyList<SmtFormula> PathConditions { get; }

        public SymbolicState PathState { get; }

        public IReadOnlyList<string> Facts { get; }

        public SmtFormula MergedInvariant { get; }

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
