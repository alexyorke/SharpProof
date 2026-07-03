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

        internal static SymbolicIrProofResult ClassifyStateFeasibility(
            SymbolicState state,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyReachability(state);
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

        public static List<SmtFormula>? TryCollectBranchConditions(
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
                CSharpConditionToFormula.TryCollectDomainFacts(
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
            CSharpConditionToFormula.TryCollectBranchAssumptions(
                condition,
                branchWhenTrue,
                semanticModel,
                cancellationToken,
                pathConditions,
                    getSymbolVersion);

            var addedBranchFacts = pathConditions.Count != countBeforeBranchAssumptions;
            if ((addTranslatedFormulaAlways ||
                 addTranslatedFormulaFallback && !addedIrBranchFact && !addedBranchFacts) &&
                CSharpConditionToFormula.TryTranslate(
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

        private static bool TryAddIrBranchConditionFact(
            ExpressionSyntax condition,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            ICollection<SmtFormula> pathConditions,
            Func<ISymbol, int>? getSymbolVersion)
        {
            var context = new SymbolicLoweringContext(
                semanticModel,
                cancellationToken,
                getSymbolVersion);
            if (!SymbolicIrLowerer.TryLowerCondition(condition, context, out var symbolicCondition))
            {
                return false;
            }

            if (!branchWhenTrue)
            {
                symbolicCondition = new SymbolicNotCondition(symbolicCondition);
            }

            if (!SymbolicIrFormulaEncoder.TryEncode(symbolicCondition, out var formula))
            {
                return false;
            }

            pathConditions.Add(formula);
            return true;
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

        public static bool? EvaluateKnownConditionTruth(
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

        public static bool IsForInitialEntryConditionAlwaysFalse(
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
            if (!CSharpConditionToFormula.TryTranslate(forStatement.Condition, semanticModel, cancellationToken, out var formula) ||
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

            CSharpConditionToFormula.TryCollectDomainFacts(forStatement.Condition, semanticModel, cancellationToken, pathConditions);
            return IsFormulaAlwaysFalse(formula, pathConditions, smtAnalysis);
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

        public static bool TryCreateReferenceNullComparison(
            ExpressionSyntax expression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool equalToNull,
            out SmtFormula formula,
            Func<ISymbol, int>? getSymbolVersion = null)
        {
            formula = null!;
            if (!CSharpConditionToFormula.TryTranslateValue(
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
            public ConcurrentDictionary<PathConditionCacheKey, ImmutableArray<SmtFormula>> Values { get; } = new();
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
