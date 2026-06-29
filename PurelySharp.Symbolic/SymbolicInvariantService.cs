using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PurelySharp.Symbolic.Smt;
using SearchLib.Purity;
using SearchLib.Smt;

namespace PurelySharp.Symbolic
{
    public sealed class SymbolicInvariantService
    {
        public SymbolicInvariantSnapshot GetInvariantsAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            var formulas = CollectInvariantsAt(site, semanticModel, cancellationToken);

            return new SymbolicInvariantSnapshot(site.SpanStart, formulas);
        }

        public SymbolicProgramPointAnalysis AnalyzeAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            SmtAnalysisService? smtAnalysis = null,
            CancellationToken cancellationToken = default)
        {
            var formulas = CollectInvariantsAt(site, semanticModel, cancellationToken);
            return CreateAnalysis(site.SpanStart, formulas, smtAnalysis);
        }

        public SymbolicInvariantSnapshot GetForInitialEntryInvariants(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            CancellationToken cancellationToken = default)
        {
            var formulas = SymbolicProgramPointFacts
                .CollectAncestorReachabilityConditions(forStatement, semanticModel, cancellationToken)
                .Concat(SymbolicProgramPointFacts
                    .CollectPriorAssignmentFacts(forStatement, semanticModel, cancellationToken))
                .Concat(SymbolicProgramPointFacts.CollectForInitializerFacts(forStatement, semanticModel, cancellationToken))
                .ToArray();

            return new SymbolicInvariantSnapshot(forStatement.SpanStart, formulas);
        }

        public SymbolicProgramPointAnalysis AnalyzeForInitialEntry(
            ForStatementSyntax forStatement,
            SemanticModel semanticModel,
            SmtAnalysisService? smtAnalysis = null,
            CancellationToken cancellationToken = default)
        {
            var formulas = SymbolicProgramPointFacts
                .CollectAncestorReachabilityConditions(forStatement, semanticModel, cancellationToken)
                .Concat(SymbolicProgramPointFacts
                    .CollectPriorAssignmentFacts(forStatement, semanticModel, cancellationToken))
                .Concat(SymbolicProgramPointFacts.CollectForInitializerFacts(forStatement, semanticModel, cancellationToken))
                .ToArray();

            return CreateAnalysis(forStatement.SpanStart, formulas, smtAnalysis);
        }

        public static SmtFormula ConjoinPathConditions(IReadOnlyList<SmtFormula> pathConditions)
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

        public static string FormatMergedInvariant(IReadOnlyList<SmtFormula> pathConditions)
        {
            return pathConditions.Count == 0
                ? "true"
                : ConjoinPathConditions(pathConditions).ToString() ?? "true";
        }

        private static SmtFormula[] CollectInvariantsAt(
            SyntaxNode site,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return SymbolicProgramPointFacts
                .CollectAncestorReachabilityConditions(site, semanticModel, cancellationToken)
                .Concat(SymbolicProgramPointFacts
                    .CollectPriorAssignmentFacts(site, semanticModel, cancellationToken))
                .ToArray();
        }

        private static SymbolicProgramPointAnalysis CreateAnalysis(
            int spanStart,
            IReadOnlyList<SmtFormula> formulas,
            SmtAnalysisService? smtAnalysis)
        {
            if (formulas.Count == 0)
            {
                return new SymbolicProgramPointAnalysis(
                    spanStart,
                    formulas,
                    SymbolicReachability.Reachable,
                    "no_path_conditions",
                    SymbolicSmtDiagnostics.FromService(smtAnalysis));
            }

            var proof = smtAnalysis?.ClassifyPathFeasibility(formulas);
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
                reachability,
                proof?.Reason ?? "reachability_not_checked",
                SymbolicSmtDiagnostics.FromService(smtAnalysis));
        }
    }

    public sealed class SymbolicInvariantSnapshot
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

    public sealed class SymbolicProgramPointAnalysis
    {
        public SymbolicProgramPointAnalysis(
            int spanStart,
            IReadOnlyList<SmtFormula> pathConditions,
            SymbolicReachability reachability,
            string reachabilityReason,
            SymbolicSmtDiagnostics? smtDiagnostics = null)
        {
            SpanStart = spanStart;
            PathConditions = pathConditions;
            Facts = pathConditions.Select(static fact => fact.ToString() ?? string.Empty).ToArray();
            MergedInvariant = SymbolicInvariantService.ConjoinPathConditions(pathConditions);
            MergedInvariantText = SymbolicInvariantService.FormatMergedInvariant(pathConditions);
            Reachability = reachability;
            ReachabilityReason = reachabilityReason;
            SmtDiagnostics = smtDiagnostics ?? SymbolicSmtDiagnostics.NotConfigured;
        }

        public int SpanStart { get; }

        public IReadOnlyList<SmtFormula> PathConditions { get; }

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
