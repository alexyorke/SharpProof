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
            return ClassifyPathFeasibility(pathConditions, smtAnalysis).PathFeasibility != Feasibility.Unsatisfiable;
        }

        internal static bool IsUnsatisfiable(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyPathFeasibility(pathConditions, smtAnalysis).PathFeasibility == Feasibility.Unsatisfiable;
        }

        internal static bool PathConditionsImply(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SmtAnalysisService? smtAnalysis)
        {
            return ClassifyImplication(pathConditions, factFormula, smtAnalysis).Outcome == PurityProofOutcome.ProvablyPure;
        }

        internal static bool PathConditionsAllowAndImply(
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

        internal static PurityProofResult ClassifyImplication(
            IEnumerable<SmtFormula> pathConditions,
            SmtFormula factFormula,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyFormulaImplication(
                pathConditions,
                factFormula);
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
            return ClassifyBranchReachability(pathConditions, formula, smtAnalysis).Outcome == PurityProofOutcome.ProvablyPure;
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

        internal static bool IsNodeReachable(
            SyntaxNode node,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return IsSatisfiable(CollectPathConditionsAt(node, semanticModel, cancellationToken), smtAnalysis);
        }

        internal static bool IsNodeUnreachable(
            SyntaxNode node,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            SmtAnalysisService? smtAnalysis)
        {
            return !IsNodeReachable(node, semanticModel, cancellationToken, smtAnalysis);
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
            return new SymbolicProofService(smtAnalysis).ClassifyFormulaBranchReachability(
                pathConditions,
                branchCondition);
        }

        internal static PurityProofResult ClassifyPathFeasibility(
            IEnumerable<SmtFormula> pathConditions,
            SmtAnalysisService? smtAnalysis)
        {
            return new SymbolicProofService(smtAnalysis).ClassifyFormulaPathFeasibility(pathConditions);
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
