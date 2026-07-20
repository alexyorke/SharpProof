namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowEngine {
    private enum ExceptionSiteDisposition {
        Escapes,
        Caught,
        Unreachable,
        ShadowedByFinally
    }

    private sealed class ExceptionSiteAssessment(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        private readonly Dictionary<SyntaxNode, SymbolicState> _pathStates = new();

        internal ExceptionSiteDisposition Assess(
            SyntaxNode site,
            ExceptionFlowAnalyzer.UsingDisposeGuard? usingGuard,
            Func<ITypeSymbol?> resolveType,
            out ITypeSymbol? exceptionType) {
            exceptionType = null;
            var pathState = GetPathState(site);
            var reachabilityState = usingGuard?.ResourceExpression is { } receiver
                ? SymbolicStateFactBuilder.AddReferenceNullCondition(
                    pathState,
                    receiver,
                    false,
                    semanticModel,
                    cancellationToken,
                    "analyzer.exception-flow.non-null")
                : pathState;
            if (!IsReachable(reachabilityState))
                return ExceptionSiteDisposition.Unreachable;
            if (IsShadowedByFinally(site, pathState))
                return ExceptionSiteDisposition.ShadowedByFinally;
            exceptionType = resolveType();
            return IsCaught(site, exceptionType)
                ? ExceptionSiteDisposition.Caught
                : ExceptionSiteDisposition.Escapes;
        }

        private SymbolicState GetPathState(SyntaxNode site) {
            if (_pathStates.TryGetValue(site, out var state)) return state;
            var initialState = RequiresEntryStateBuilder.CreateForUse(
                site,
                semanticModel,
                attributePolicy,
                cancellationToken);
            state = SymbolicReachabilityService.CollectPathStateAt(
                site,
                semanticModel,
                cancellationToken,
                initialState);
            _pathStates.Add(site, state);
            return state;
        }

        private bool IsReachable(SymbolicState state) =>
            new SymbolicProofService(smtAnalysis).ClassifyReachability(state).Status !=
            SymbolicProofStatus.Unreachable;

        private bool IsShadowedByFinally(SyntaxNode site, SymbolicState pathState) {
            foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>()) {
                if (tryStatement.Finally?.Block is not { } finallyBlock ||
                    finallyBlock.Span.Contains(site.SpanStart) ||
                    !tryStatement.Block.Span.Contains(site.SpanStart) &&
                    !tryStatement.Catches.Any(catchClause =>
                        catchClause.Block.Span.Contains(site.SpanStart) ||
                        catchClause.Filter?.Span.Contains(site.SpanStart) == true))
                    continue;
                if (SymbolicControlFlowFacts.StatementDefinitelyExits(
                        finallyBlock,
                        semanticModel,
                        cancellationToken))
                    return true;
                using var completionScope = SymbolicAnalysisLimitContext.PushIsolated(
                    SymbolicAnalysisLimitContext.Limits,
                    finallyBlock);
                var state = SymbolicCfgProgramPointStateCollector.CollectCompletedStatementState(
                    finallyBlock,
                    pathState,
                    semanticModel,
                    cancellationToken).Value!;
                if (!completionScope.Snapshot().IsTruncated && !IsReachable(state))
                    return true;
            }
            return false;
        }

        private bool IsCaught(SyntaxNode site, ITypeSymbol? exceptionType) {
            if (site.Ancestors().OfType<CatchFilterClauseSyntax>().Any()) return true;
            foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>()) {
                if (!tryStatement.Block.Span.Contains(site.SpanStart)) continue;
                if (tryStatement.Catches.Any(catchClause =>
                        Catches(catchClause, exceptionType, site)))
                    return true;
                if (ReferenceEquals(tryStatement, methodNode)) break;
            }
            return false;
        }

        private bool Catches(CatchClauseSyntax clause, ITypeSymbol? exceptionType, SyntaxNode site) {
            if (clause.Declaration != null) {
                if (exceptionType == null) return false;
                var catchType = semanticModel.GetTypeInfo(clause.Declaration.Type, cancellationToken).Type;
                if (catchType == null || !TypeHierarchyEnumeration.IsSameOrDerivedFrom(exceptionType, catchType))
                    return false;
            }
            if (clause.Filter?.FilterExpression is not { } filter) return true;
            var constant = semanticModel.GetConstantValue(filter, cancellationToken);
            if (constant.HasValue && constant.Value is bool value) return value;
            var lowering = SymbolicSemanticPipeline.LowerCondition(
                filter,
                new SymbolicLoweringContext(semanticModel, cancellationToken));
            return lowering is { IsExact: true, Value: { } condition } &&
                   new SymbolicProofService(smtAnalysis)
                       .ClassifyConditionTruth(GetPathState(site), condition).Status ==
                   SymbolicProofStatus.ProvenTrue;
        }
    }
}
