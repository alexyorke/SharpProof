namespace SharpProof.Analyzer;
internal static class MethodEnsuresAnalyzer {
    internal static void AnalyzeSymbolForEnsures(
        MethodBodyAnalysisContext context,
        SmtAnalysisService smtAnalysis) {
        var methodSymbol = context.MethodSymbol;
        if (methodSymbol.DeclaringSyntaxReferences.IsDefaultOrEmpty) return;
        var contracts = ContractConditionHelpers.Collect(
            methodSymbol, "EnsuresAttribute", context.CancellationToken);
        if (contracts.Length == 0) return;
        contracts = ContractConditionHelpers.ReportAndFilterInvalid(contracts, "[Ensures]", context);
        if (contracts.Length == 0) return;
        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context)) {
            foreach (var contract in contracts)
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract,
                    "auto-property getter result is not source-visible for [Ensures] verification",
                    CreateUnsupportedDiagnostic);
            return;
        }
        if (!SupportsEnsuresPostconditions(context.Node, out var unsupportedReason)) {
            foreach (var contract in contracts)
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract, unsupportedReason, CreateUnsupportedDiagnostic);
            return;
        }
        var requiresAssumptions = CollectRequiresAssumptions(methodSymbol, context.CancellationToken);
        var completionSites = MethodCompletionAnalysis.Collect(context);
        if (completionSites.Length == 0) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contract in contracts) {
            if (!RequiresContractHelpers.TryRewriteForMethod(
                    contract.Condition,
                    contract.SourceMethod,
                    methodSymbol,
                    out var implementationCondition)) {
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract, "condition rewrite failure", CreateUnsupportedDiagnostic);
                continue;
            }
            if (!ContractConditionHelpers.TryParse(
                    implementationCondition,
                    out var conditionStatement,
                    out var conditionExpression)) {
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract, "condition parse failure", CreateUnsupportedDiagnostic);
                continue;
            }
            if (!ContractConditionHelpers.TryCreateSpeculativeModel(
                    context.SemanticModel,
                    completionSites[0].QueryNode.SpanStart,
                    conditionStatement,
                    out var speculativeModel)) {
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract, "condition binding failure", CreateUnsupportedDiagnostic);
                continue;
            }
            if (!completionSites.All(static site => site.ResultExpression != null) &&
                RequiresContractHelpers.ContainsResultReference(conditionExpression)) {
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract,
                    "result is not available for [Ensures] on void-returning members or constructors",
                    CreateUnsupportedDiagnostic);
                continue;
            }
            if (ReferencesUserLocalOrUnsupportedParameter(conditionExpression, speculativeModel, methodSymbol, context.CancellationToken)) {
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract,
                    "local variables are not supported in [Ensures] conditions", CreateUnsupportedDiagnostic);
                continue;
            }
            foreach (var completionSite in completionSites) {
                if (!TryRewriteConditionForCompletionSite(
                        implementationCondition,
                        completionSite,
                        NullableFlowFacts.GetMethodBodyReturnState(
                            methodSymbol,
                            methodSymbol.IsAsync),
                        out var rewrittenCondition,
                        out _)) {
                    ContractConditionHelpers.ReportUnsupported(
                        context, methodSymbol, contract, "result placeholder rewrite failed", CreateUnsupportedDiagnostic,
                        additionalLocations: [completionSite.Location]);
                    continue;
                }
                var proofCondition =
                    RequiresContractHelpers.CombineAsImplication(requiresAssumptions, rewrittenCondition);
                var proof = MethodCompletionAnalysis.Prove(context, smtAnalysis, completionSite, proofCondition);
                if (proof.TruthValue == SymbolicTruthValue.ProvenTrue ||
                    proof.TruthValue == SymbolicTruthValue.Unreachable)
                    continue;
                var key = string.Join(
                    ":",
                    contract.Condition,
                    completionSite.QueryNode.SpanStart.ToString(CultureInfo.InvariantCulture),
                    proof.TruthValue.ToString(),
                    proof.UnknownReason.ToString(),
                    proof.Reason);
                if (!seen.Add(key)) continue;
                if (proof.TruthValue == SymbolicTruthValue.ProvenFalse) {
                    context.ReportDiagnostic(
                        CreateNotProvenDiagnostic(methodSymbol, contract.Condition, completionSite, contract.Location));
                    continue;
                }
                ContractConditionHelpers.ReportUnsupported(
                    context, methodSymbol, contract, ContractDiagnosticSupport.FormatUnknownReason(proof, "Ensures"),
                    CreateUnsupportedDiagnostic, completionSite.Location,
                    contract.Location == null ? null : [contract.Location]);
            }
        }
    }
    private static ImmutableArray<ContractAttributeCondition> CollectRequiresAssumptions(
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken) {
        var assumptions = ImmutableArray.CreateBuilder<ContractAttributeCondition>();
        foreach (var contract in RequiresContractHelpers.ValidContracts(methodSymbol, cancellationToken)) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RequiresContractHelpers.TryRewriteForMethod(
                    contract.Condition,
                    contract.SourceMethod,
                    methodSymbol,
                    out var implementationCondition) ||
                !ContractConditionHelpers.TryParse(
                    implementationCondition,
                    out _,
                    out var conditionExpression) ||
                RequiresContractHelpers.ContainsResultReference(conditionExpression))
                continue;
            assumptions.Add(contract with { Condition = implementationCondition });
        }
        return assumptions.ToImmutable();
    }
    private static bool SupportsEnsuresPostconditions(SyntaxNode methodNode, out string reason) {
        if (methodNode is AccessorDeclarationSyntax accessor &&
            (accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
             accessor.IsKind(SyntaxKind.InitAccessorDeclaration) ||
             accessor.IsKind(SyntaxKind.AddAccessorDeclaration) ||
             accessor.IsKind(SyntaxKind.RemoveAccessorDeclaration))) {
            reason = "non-returning accessors are not supported by [Ensures]";
            return false;
        }
        reason = string.Empty;
        return true;
    }
    private static bool ReferencesUserLocalOrUnsupportedParameter(
        ExpressionSyntax conditionExpression,
        SemanticModel speculativeModel,
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken) {
        foreach (var identifier in conditionExpression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()) {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(identifier.Identifier.ValueText, "result", StringComparison.Ordinal)) continue;
            var symbol = speculativeModel.GetSymbolInfo(identifier, cancellationToken).Symbol;
            if (symbol is ILocalSymbol) return true;
            if (symbol is IParameterSymbol parameter &&
                !IsSupportedEnsuresParameter(parameter, methodSymbol))
                return true;
        }
        return false;
    }
    private static bool IsSupportedEnsuresParameter(IParameterSymbol parameter, IMethodSymbol methodSymbol)
        => SymbolEq.AreEqual(parameter.ContainingSymbol?.OriginalDefinition, methodSymbol.OriginalDefinition);
    private static bool TryRewriteConditionForCompletionSite(
        string conditionText,
        MethodNormalCompletion completionSite,
        NullableFlowFactState resultState,
        out string rewrittenCondition,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ExpressionSyntax? rewrittenExpression) {
        rewrittenCondition = conditionText;
        rewrittenExpression = null;
        if (!ContractConditionHelpers.TryParse(conditionText, out _, out var conditionExpression)) return false;
        if (completionSite.ResultExpression == null) {
            rewrittenExpression = conditionExpression;
            return true;
        }
        if (resultState == NullableFlowFactState.NotNull)
            conditionExpression = (ExpressionSyntax)new NullableResultContractRewriter().Visit(conditionExpression)!;
        var rewriter = new ResultPlaceholderRewriter((ExpressionSyntax)completionSite.ResultExpression.WithoutTrivia());
        var rewritten = (ExpressionSyntax)rewriter.Visit(conditionExpression)!;
        rewrittenCondition = rewritten.ToFullString();
        rewrittenExpression = rewritten;
        return true;
    }
    internal static bool TryCreateEntrySnapshotProofCondition(
        string proofCondition,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        int speculativePosition,
        CancellationToken cancellationToken,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SymbolicCondition? symbolicCondition,
        out SymbolicState initialState,
        out string? failureReason) {
        symbolicCondition = null;
        initialState = new SymbolicState();
        failureReason = null;
        if (!ContractConditionHelpers.TryParse(proofCondition, out var proofStatement, out var proofExpression)) {
            failureReason = "condition parse failure";
            return false;
        }
        if (!ContainsOldValueInvocation(proofExpression)) return false;
        if (!ContractConditionHelpers.TryCreateSpeculativeModel(
                semanticModel,
                speculativePosition,
                proofStatement,
                out var speculativeModel)) {
            failureReason = "condition binding failure";
            return false;
        }
        var snapshots = new OldValueSnapshotBuilder(speculativeModel, methodSymbol, cancellationToken);
        var loweringContext = new SymbolicLoweringContext(
            speculativeModel,
            cancellationToken,
            invocationTermLowerer: snapshots.TryLowerInvocationTerm,
            invocationTermTypeResolver: snapshots.ResolveInvocationTermType);
        var lowering = SymbolicSemanticPipeline.LowerCondition(proofExpression, loweringContext);
        if (lowering is not { IsExact: true, Value: { } loweredCondition }) {
            failureReason = snapshots.FailureReason ??
                            "old(...) expression is not supported by the current bounded proof engine";
            return false;
        }
        symbolicCondition = loweredCondition;
        if (!snapshots.HasSnapshots) {
            failureReason = "old(...) expression is not supported by the current bounded proof engine";
            return false;
        }
        initialState = snapshots.CreateInitialState();
        return true;
    }
    private static bool ContainsOldValueInvocation(ExpressionSyntax expression) => expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsOldValueInvocation);
    private static bool IsOldValueInvocation(InvocationExpressionSyntax invocation)
        => invocation.Expression is IdentifierNameSyntax identifier &&
               string.Equals(identifier.Identifier.ValueText, "old", StringComparison.Ordinal);
    private static Diagnostic CreateNotProvenDiagnostic(
        IMethodSymbol methodSymbol,
        string condition,
        MethodNormalCompletion completionSite,
        Location? contractLocation) => Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("EnsuresNotProvenRule"),
            completionSite.Location,
            contractLocation == null ? null : [contractLocation],
            completionSite.DisplayText,
            methodSymbol.Name,
            condition);
    private static Diagnostic CreateUnsupportedDiagnostic(
        IMethodSymbol methodSymbol,
        string condition,
        Location? location,
        string reason,
        IEnumerable<Location>? additionalLocations) => Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("EnsuresUnsupportedRule"),
            location,
            additionalLocations,
            condition,
            methodSymbol.Name,
            reason);
    sealed class ResultPlaceholderRewriter(ExpressionSyntax replacement) : CSharpSyntaxRewriter {
        private readonly ExpressionSyntax _replacement = SyntaxFactory.ParenthesizedExpression(replacement);
        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) {
            if (!string.Equals(node.Identifier.ValueText, "result", StringComparison.Ordinal))
                return base.VisitIdentifierName(node);
            if (CSharpSyntaxFacts.IsMemberOrQualifiedNameRightSide(node))
                return base.VisitIdentifierName(node);
            return _replacement.WithTriviaFrom(node);
        }
    }
    sealed class NullableResultContractRewriter : CSharpSyntaxRewriter {
        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node) {
            if ((node.IsKind(SyntaxKind.EqualsExpression) ||
                 node.IsKind(SyntaxKind.NotEqualsExpression)) &&
                ((IsResult(node.Left) && IsNull(node.Right)) ||
                 (IsNull(node.Left) && IsResult(node.Right))))
                return SyntaxFactory.LiteralExpression(
                        node.IsKind(SyntaxKind.NotEqualsExpression)
                            ? SyntaxKind.TrueLiteralExpression
                            : SyntaxKind.FalseLiteralExpression)
                    .WithTriviaFrom(node);
            return base.VisitBinaryExpression(node);
        }
        public override SyntaxNode? VisitIsPatternExpression(IsPatternExpressionSyntax node) {
            if (!IsResult(node.Expression) ||
                !CSharpSyntaxFacts.TryGetNullPatternPolarity(node.Pattern, out var matchesNonNull))
                return base.VisitIsPatternExpression(node);
            return SyntaxFactory.LiteralExpression(matchesNonNull ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression)
                .WithTriviaFrom(node);
        }
        private static bool IsResult(ExpressionSyntax expression) => expression is IdentifierNameSyntax identifier &&
                   string.Equals(identifier.Identifier.ValueText, "result", StringComparison.Ordinal);
        private static bool IsNull(ExpressionSyntax expression) =>
            expression.IsKind(SyntaxKind.NullLiteralExpression);
    }
    sealed class OldValueSnapshotBuilder(SemanticModel semanticModel, IMethodSymbol methodSymbol, CancellationToken cancellationToken) {
        private readonly CancellationToken _cancellationToken = cancellationToken;
        private readonly IMethodSymbol _methodSymbol = methodSymbol;
        private readonly SemanticModel _semanticModel = semanticModel;
        private readonly List<SymbolicFact> _snapshotFacts = [];
        private readonly Dictionary<string, SymbolicTerm> _snapshotTerms = new(StringComparer.Ordinal);
        private int _nextSnapshotId;
        public string? FailureReason { get; private set; }
        public bool HasSnapshots => _snapshotFacts.Count != 0;
        public bool TryLowerInvocationTerm(
            InvocationExpressionSyntax invocation,
            SymbolicLoweringContext context,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SymbolicTerm? term) {
            _ = context;
            term = null;
            if (!IsOldValueInvocation(invocation)) return false;
            if (invocation.ArgumentList.Arguments.Count != 1) {
                FailureReason = "old(...) requires exactly one argument";
                return false;
            }
            var argument = invocation.ArgumentList.Arguments[0].Expression;
            if (ContainsOldValueInvocation(argument)) {
                FailureReason = "nested old(...) expressions are not supported";
                return false;
            }
            if (RequiresContractHelpers.ContainsResultReference(argument)) {
                FailureReason = "result is not available inside old(...)";
                return false;
            }
            if (ReferencesUserLocalOrUnsupportedParameter(argument, _semanticModel, _methodSymbol, _cancellationToken)) {
                FailureReason = "local variables are not supported inside old(...)";
                return false;
            }
            var key = argument.WithoutTrivia().ToString();
            if (_snapshotTerms.TryGetValue(key, out term)) return true;
            var entryContext = new SymbolicLoweringContext(_semanticModel, _cancellationToken);
            var lowering = SymbolicSemanticPipeline.LowerTerm(argument, entryContext);
            if (lowering is not { IsExact: true, Value: { } entryTerm }) {
                FailureReason = "old(...) expression is not supported by the current bounded proof engine";
                return false;
            }
            term = new SymbolicVariableTerm("__sp_old_" + _nextSnapshotId.ToString(CultureInfo.InvariantCulture), entryTerm.Kind);
            _nextSnapshotId++;
            _snapshotTerms.Add(key, term);
            _snapshotFacts.Add(SymbolicFact.Exact(
                new SymbolicRelationAtom(SymbolicRelationOperator.Equal, term, entryTerm),
                invocation,
                "ir.path.ensures-old-snapshot"));
            return true;
        }
        public ITypeSymbol? ResolveInvocationTermType(InvocationExpressionSyntax invocation) {
            if (!IsOldValueInvocation(invocation) || invocation.ArgumentList.Arguments.Count != 1)
                return null;
            var argument = invocation.ArgumentList.Arguments[0].Expression;
            var typeInfo = _semanticModel.GetTypeInfo(argument, _cancellationToken);
            return typeInfo.ConvertedType ?? typeInfo.Type;
        }
        public SymbolicState CreateInitialState() =>
            new(_snapshotFacts);
    }
}
