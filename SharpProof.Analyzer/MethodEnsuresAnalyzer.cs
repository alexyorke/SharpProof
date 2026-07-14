using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer;

internal static class MethodEnsuresAnalyzer
{
    internal static void AnalyzeSymbolForEnsures(
        MethodBodyAnalysisContext context,
        CompilationPurityService purityService,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        var methodSymbol = context.MethodSymbol;

        void Report(Diagnostic diagnostic)
        {
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(context, baseline, diagnostic);
        }

        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true) return;

        var contracts = CollectContracts(methodSymbol, attributePolicy, context.CancellationToken);
        if (contracts.Length == 0) return;

        contracts = ReportAndFilterInvalidContracts(
            contracts,
            context,
            methodSymbol,
            baseline);
        if (contracts.Length == 0) return;

        if (AnalyzerSyntaxHelpers.IsBodylessAutoPropertyGetter(context))
        {
            foreach (var contract in contracts)
            {
                var diagnostic = CreateUnsupportedDiagnostic(
                    methodSymbol,
                    contract.Condition,
                    contract.Location,
                    "auto-property getter result is not source-visible for [Ensures] verification",
                    null);
                Report(diagnostic);
            }

            return;
        }

        if (!SupportsEnsuresPostconditions(context.Node, out var unsupportedReason))
        {
            foreach (var contract in contracts)
            {
                var diagnostic = CreateUnsupportedDiagnostic(
                    methodSymbol,
                    contract.Condition,
                    contract.Location,
                    unsupportedReason,
                    null);
                Report(diagnostic);
            }

            return;
        }

        var requiresAssumptions = CollectRequiresAssumptions(methodSymbol, attributePolicy, context.CancellationToken);
        var completionSites =
            CollectCompletionSites(methodSymbol, context.Node, context.SemanticModel, context.State,
                context.CancellationToken);
        if (completionSites.Length == 0) return;

        var queryService = context.State.QueryService;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var contract in contracts)
        {
            if (!ContractConditionHelpers.TryParse(
                    contract.Condition,
                    out var conditionStatement,
                    out var conditionExpression))
            {
                var diagnostic = CreateUnsupportedDiagnostic(
                    methodSymbol,
                    contract.Condition,
                    contract.Location,
                    "condition parse failure",
                    null);
                Report(diagnostic);

                continue;
            }

            if (!ContractConditionHelpers.TryCreateSpeculativeModel(
                    context.SemanticModel,
                    GetSpeculativePosition(completionSites[0]),
                    conditionStatement,
                    out var speculativeModel))
            {
                var diagnostic = CreateUnsupportedDiagnostic(
                    methodSymbol,
                    contract.Condition,
                    contract.Location,
                    "condition binding failure",
                    null);
                Report(diagnostic);

                continue;
            }

            if (!CompletionSitesHaveResult(completionSites) &&
                RequiresContractHelpers.ContainsResultReference(conditionExpression))
            {
                var diagnostic = CreateUnsupportedDiagnostic(
                    methodSymbol,
                    contract.Condition,
                    contract.Location,
                    "result is not available for [Ensures] on void-returning members or constructors",
                    null);
                Report(diagnostic);

                continue;
            }

            if (ReferencesUserLocalOrUnsupportedParameter(conditionExpression, speculativeModel, methodSymbol,
                    context.CancellationToken))
            {
                var diagnostic = CreateUnsupportedDiagnostic(
                    methodSymbol,
                    contract.Condition,
                    contract.Location,
                    "local variables are not supported in [Ensures] conditions",
                    null);
                Report(diagnostic);

                continue;
            }

            foreach (var completionSite in completionSites)
            {
                if (!purityService.SmtAnalysis.Options.IsEnabled)
                {
                    var diagnostic = CreateUnsupportedDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        completionSite.Location,
                        "SMT is disabled for [Ensures] verification",
                        contract.Location == null ? null : new[] { contract.Location });
                    Report(diagnostic);

                    continue;
                }

                if (!TryRewriteConditionForCompletionSite(
                        contract.Condition,
                        completionSite,
                        NullableFlowFacts.GetMethodReturnState(methodSymbol),
                        out var rewrittenCondition,
                        out _))
                {
                    var diagnostic = CreateUnsupportedDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        contract.Location,
                        "result placeholder rewrite failed",
                        new[] { completionSite.Location });
                    Report(diagnostic);

                    continue;
                }

                var proofCondition =
                    RequiresContractHelpers.CombineAsImplication(requiresAssumptions, rewrittenCondition);
                SymbolicConditionProofResult proof;
                if (TryCreateOldAwareProofCondition(
                        proofCondition,
                        methodSymbol,
                        context.SemanticModel,
                        completionSite,
                        context.CancellationToken,
                        out var symbolicCondition,
                        out var initialState,
                        out var oldFailureReason))
                {
                    var proofOutcome = queryService.TryProveAtSyntaxNode(
                        context.SemanticModel,
                        completionSite.QueryNode,
                        proofCondition,
                        symbolicCondition,
                        initialState,
                        purityService.SmtAnalysis,
                        completionSite.IncludeCurrentStatementCompletionFacts,
                        context.CancellationToken);
                    proof = AnalyzerSymbolicQueryBoundary.ResolveProof(
                        proofOutcome,
                        proofCondition,
                        context.CancellationToken);
                }
                else if (oldFailureReason == null)
                {
                    var proofOutcome = queryService.TryProveAtSyntaxNode(
                            context.SemanticModel,
                            completionSite.QueryNode,
                            proofCondition,
                            purityService.SmtAnalysis,
                            completionSite.IncludeCurrentStatementCompletionFacts,
                            context.CancellationToken);
                    proof = AnalyzerSymbolicQueryBoundary.ResolveProof(
                        proofOutcome,
                        proofCondition,
                        context.CancellationToken);
                }
                else
                {
                    proof = new SymbolicConditionProofResult(
                        proofCondition,
                        SymbolicTruthValue.Unknown,
                        oldFailureReason);
                }

                if (proof.TruthValue == SymbolicTruthValue.ProvenTrue ||
                    proof.TruthValue == SymbolicTruthValue.Unreachable)
                    continue;

                var key = string.Join(
                    ":",
                    contract.Condition,
                    completionSite.QueryNode.SpanStart.ToString(CultureInfo.InvariantCulture),
                    proof.TruthValue.ToString(),
                    proof.Proof.UnknownReason.ToString(),
                    proof.Reason);
                if (!seen.Add(key)) continue;

                if (proof.TruthValue == SymbolicTruthValue.ProvenFalse)
                {
                    var diagnostic = CreateNotProvenDiagnostic(
                        methodSymbol,
                        contract.Condition,
                        completionSite,
                        contract.Location,
                        proof);
                    Report(diagnostic);

                    continue;
                }

                var unsupportedDiagnostic = CreateUnsupportedDiagnostic(
                    methodSymbol,
                    contract.Condition,
                    completionSite.Location,
                    ContractDiagnosticSupport.FormatUnknownReason(proof, "Ensures"),
                    contract.Location == null ? null : new[] { contract.Location },
                    proof.AnalysisTruncation);
                Report(unsupportedDiagnostic);
            }
        }
    }

    private static ImmutableArray<EnsuresContract> CollectContracts(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        return ContractConditionHelpers.Collect(
            methodSymbol,
            attributePolicy,
            "EnsuresAttribute",
            static contract => new EnsuresContract(
                contract.Condition,
                contract.Location,
                contract.Argument,
                contract.InvalidReason),
            cancellationToken);
    }

    private static ImmutableArray<RequiresContract> CollectRequiresAssumptions(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        return RequiresContractHelpers.ValidContracts(methodSymbol, attributePolicy, cancellationToken)
            .Where(contract =>
                ContractConditionHelpers.TryParse(contract.Condition, out _, out var conditionExpression) &&
                !RequiresContractHelpers.ContainsResultReference(conditionExpression))
            .ToImmutableArray();
    }

    private static ImmutableArray<EnsuresContract> ReportAndFilterInvalidContracts(
        ImmutableArray<EnsuresContract> contracts,
        MethodBodyAnalysisContext context,
        IMethodSymbol methodSymbol,
        DiagnosticBaseline baseline)
    {
        var validContracts = ImmutableArray.CreateBuilder<EnsuresContract>(contracts.Length);
        foreach (var contract in contracts)
        {
            if (contract.InvalidReason == null)
            {
                validContracts.Add(contract);
                continue;
            }

            var diagnostic = InvalidContractArgumentDiagnostics.Create(
                "[Ensures]",
                contract.Argument,
                contract.InvalidReason,
                contract.Location ?? AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                methodSymbol,
                context.Node.SyntaxTree);
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(
                baseline,
                diagnostic,
                context.ReportDiagnostic);
        }

        return validContracts.ToImmutable();
    }

    private static bool SupportsEnsuresPostconditions(
        SyntaxNode methodNode,
        out string reason)
    {
        if (methodNode is AccessorDeclarationSyntax accessor &&
            (accessor.IsKind(SyntaxKind.SetAccessorDeclaration) ||
             accessor.IsKind(SyntaxKind.InitAccessorDeclaration) ||
             accessor.IsKind(SyntaxKind.AddAccessorDeclaration) ||
             accessor.IsKind(SyntaxKind.RemoveAccessorDeclaration)))
        {
            reason = "non-returning accessors are not supported by [Ensures]";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static ImmutableArray<CompletionSite> CollectCompletionSites(
        IMethodSymbol methodSymbol,
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        MethodBodyAnalysisState analysisState,
        CancellationToken cancellationToken)
    {
        if (analysisState.RootOperation == null) return ImmutableArray<CompletionSite>.Empty;

        var builder = ImmutableArray.CreateBuilder<CompletionSite>();
        foreach (var operation in analysisState.VisibleOperations)
            if (operation is IReturnOperation returnOperation)
            {
                if (AnalyzerSyntaxHelpers.IsCompilerMarkedUnreachable(
                        operation.Syntax,
                        semanticModel,
                        cancellationToken))
                    continue;

                if (returnOperation.ReturnedValue?.Syntax is ExpressionSyntax returnedExpression)
                {
                    builder.Add(new CompletionSite(
                        returnedExpression,
                        returnedExpression.GetLocation(),
                        operation.Syntax,
                        false,
                        returnedExpression.ToString()));
                    continue;
                }

                builder.Add(new CompletionSite(
                    null,
                    operation.Syntax.GetLocation(),
                    operation.Syntax,
                    false,
                    "return"));
            }

        if (CSharpSyntaxFacts.TryGetExpressionBody(methodNode, out var expressionBody))
        {
            var hasResultValue = AnalyzerSyntaxHelpers.HasResultValue(methodSymbol);
            builder.Add(new CompletionSite(
                hasResultValue ? expressionBody : null,
                expressionBody.GetLocation(),
                expressionBody,
                !hasResultValue,
                hasResultValue ? expressionBody.ToString() : "normal completion"));
        }
        else if (CSharpSyntaxFacts.GetBlockBody(methodNode) is { } bodyBlock &&
                 AnalyzerSyntaxHelpers.BodyEndPointIsReachable(bodyBlock, semanticModel))
        {
            builder.Add(new CompletionSite(
                null,
                GetBodyCompletionLocation(bodyBlock),
                bodyBlock,
                true,
                "normal completion"));
        }

        return builder.ToImmutable();
    }

    private static Location GetBodyCompletionLocation(BlockSyntax body)
    {
        return body.CloseBraceToken.GetLocation();
    }

    private static int GetSpeculativePosition(CompletionSite completionSite)
    {
        return completionSite.QueryNode.SpanStart;
    }

    private static bool CompletionSitesHaveResult(ImmutableArray<CompletionSite> completionSites)
    {
        return completionSites.All(static site => site.ResultExpression != null);
    }

    private static bool ReferencesUserLocalOrUnsupportedParameter(
        ExpressionSyntax conditionExpression,
        SemanticModel speculativeModel,
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken)
    {
        foreach (var identifier in conditionExpression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
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

    private static bool IsSupportedEnsuresParameter(
        IParameterSymbol parameter,
        IMethodSymbol methodSymbol)
    {
        return SymbolEqualityComparer.Default.Equals(
            parameter.ContainingSymbol?.OriginalDefinition,
            methodSymbol.OriginalDefinition);
    }

    private static bool TryRewriteConditionForCompletionSite(
        string conditionText,
        CompletionSite completionSite,
        NullableFlowFactState resultState,
        out string rewrittenCondition,
        out ExpressionSyntax rewrittenExpression)
    {
        rewrittenCondition = conditionText;
        rewrittenExpression = null!;
        if (!ContractConditionHelpers.TryParse(conditionText, out _, out var conditionExpression)) return false;

        if (completionSite.ResultExpression == null)
        {
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

    private static bool TryCreateOldAwareProofCondition(
        string proofCondition,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        CompletionSite completionSite,
        CancellationToken cancellationToken,
        out SymbolicCondition symbolicCondition,
        out SymbolicState initialState,
        out string? failureReason)
    {
        return TryCreateEntrySnapshotProofCondition(
            proofCondition,
            methodSymbol,
            semanticModel,
            GetSpeculativePosition(completionSite),
            cancellationToken,
            out symbolicCondition,
            out initialState,
            out failureReason);
    }

    internal static bool TryCreateEntrySnapshotProofCondition(
        string proofCondition,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        int speculativePosition,
        CancellationToken cancellationToken,
        out SymbolicCondition symbolicCondition,
        out SymbolicState initialState,
        out string? failureReason)
    {
        symbolicCondition = null!;
        initialState = new SymbolicState();
        failureReason = null;

        if (!ContractConditionHelpers.TryParse(proofCondition, out var proofStatement, out var proofExpression))
        {
            failureReason = "condition parse failure";
            return false;
        }

        if (!ContainsOldValueInvocation(proofExpression)) return false;

        if (!ContractConditionHelpers.TryCreateSpeculativeModel(
                semanticModel,
                speculativePosition,
                proofStatement,
                out var speculativeModel))
        {
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
        if (lowering is not { IsExact: true, Value: { } loweredCondition })
        {
            failureReason = snapshots.FailureReason ??
                            "old(...) expression is not supported by the current bounded proof engine";
            return false;
        }

        symbolicCondition = loweredCondition;

        if (!snapshots.HasSnapshots)
        {
            failureReason = "old(...) expression is not supported by the current bounded proof engine";
            return false;
        }

        initialState = snapshots.CreateInitialState();
        return true;
    }

    private static bool ContainsOldValueInvocation(ExpressionSyntax expression)
    {
        return expression
            .DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>()
            .Any(IsOldValueInvocation);
    }

    private static bool IsOldValueInvocation(InvocationExpressionSyntax invocation)
    {
        return invocation.Expression is IdentifierNameSyntax identifier &&
               string.Equals(identifier.Identifier.ValueText, "old", StringComparison.Ordinal);
    }

    private static Diagnostic CreateNotProvenDiagnostic(
        IMethodSymbol methodSymbol,
        string condition,
        CompletionSite completionSite,
        Location? contractLocation,
        SymbolicConditionProofResult proof)
    {
        var properties = ContractDiagnosticSupport.AddBaselineProperties(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.EnsuresConditionProperty, condition)
                .Add(SharpProofDiagnostics.EnsuresProofStatusProperty, proof.Proof.Status.ToString())
                .Add(SharpProofDiagnostics.EnsuresFailureReasonProperty, proof.Reason),
            methodSymbol,
            "EnsuresReturnSite",
            condition,
            "not_proven:" +
            condition +
            "@" +
            completionSite.QueryNode.SpanStart.ToString(CultureInfo.InvariantCulture) +
            ":" +
            completionSite.QueryNode.Span.End.ToString(CultureInfo.InvariantCulture) +
            "|" +
            proof.Proof.Status +
            "|" +
            proof.Reason);
        var unknownReasonInfo = SymbolicUnknownReasonTaxonomy.ForEnsures(
            proof.Reason,
            proof.Proof.UnknownReason);
        properties = ContractDiagnosticSupport.AddProofEvidenceProperties(
            properties,
            completionSite.Location,
            condition,
            proof.Proof.Status.ToString(),
            ContractDiagnosticSupport.FormatUnknownReason(proof, "Ensures"),
            proof.AnalysisTruncation,
            unknownReasonInfo);

        return Diagnostic.Create(
            SharpProofDiagnostics.EnsuresNotProvenRule,
            completionSite.Location,
            contractLocation == null ? null : new[] { contractLocation },
            properties,
            completionSite.DisplayText,
            methodSymbol.Name,
            condition);
    }

    private static Diagnostic CreateUnsupportedDiagnostic(
        IMethodSymbol methodSymbol,
        string condition,
        Location? location,
        string reason,
        IEnumerable<Location>? additionalLocations,
        SymbolicAnalysisTruncationInfo? analysisTruncation = null)
    {
        var properties = ContractDiagnosticSupport.AddBaselineProperties(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.EnsuresConditionProperty, condition)
                .Add(SharpProofDiagnostics.EnsuresProofStatusProperty, SymbolicProofStatus.Unknown.ToString())
                .Add(SharpProofDiagnostics.EnsuresUnknownReasonProperty, reason)
                .Add(SharpProofDiagnostics.EnsuresFailureReasonProperty, reason),
            methodSymbol,
            "EnsuresUnsupported",
            condition,
            "unsupported:" + condition + "@" + ContractDiagnosticSupport.FormatLocationKey(location) + "|" + reason);
        properties = ContractDiagnosticSupport.AddProofEvidenceProperties(
            properties,
            location,
            condition,
            SymbolicProofStatus.Unknown.ToString(),
            reason,
            analysisTruncation ?? SymbolicAnalysisTruncationInfo.None,
            SymbolicUnknownReasonTaxonomy.ForEnsures(reason));

        return Diagnostic.Create(
            SharpProofDiagnostics.EnsuresUnsupportedRule,
            location,
            additionalLocations,
            properties,
            condition,
            methodSymbol.Name,
            reason);
    }

    private sealed class ResultPlaceholderRewriter : CSharpSyntaxRewriter
    {
        private readonly ExpressionSyntax _replacement;

        public ResultPlaceholderRewriter(ExpressionSyntax replacement)
        {
            _replacement = SyntaxFactory.ParenthesizedExpression(replacement);
        }

        public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
        {
            if (!string.Equals(node.Identifier.ValueText, "result", StringComparison.Ordinal))
                return base.VisitIdentifierName(node);

            if (CSharpSyntaxFacts.IsMemberOrQualifiedNameRightSide(node))
                return base.VisitIdentifierName(node);

            return _replacement.WithTriviaFrom(node);
        }
    }

    private sealed class NullableResultContractRewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
        {
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

        public override SyntaxNode? VisitIsPatternExpression(IsPatternExpressionSyntax node)
        {
            if (!IsResult(node.Expression) ||
                !CSharpSyntaxFacts.TryGetNullPatternPolarity(node.Pattern, out var matchesNonNull))
                return base.VisitIsPatternExpression(node);

            return SyntaxFactory.LiteralExpression(
                    matchesNonNull ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression)
                .WithTriviaFrom(node);
        }

        private static bool IsResult(ExpressionSyntax expression)
        {
            return expression is IdentifierNameSyntax identifier &&
                   string.Equals(identifier.Identifier.ValueText, "result", StringComparison.Ordinal);
        }

        private static bool IsNull(ExpressionSyntax expression)
        {
            return expression.IsKind(SyntaxKind.NullLiteralExpression);
        }

    }

    private sealed class OldValueSnapshotBuilder
    {
        private readonly CancellationToken _cancellationToken;
        private readonly IMethodSymbol _methodSymbol;
        private readonly SemanticModel _semanticModel;
        private readonly List<SymbolicFact> _snapshotFacts = new();
        private readonly Dictionary<string, SymbolicTerm> _snapshotTerms = new(StringComparer.Ordinal);
        private int _nextSnapshotId;

        public OldValueSnapshotBuilder(
            SemanticModel semanticModel,
            IMethodSymbol methodSymbol,
            CancellationToken cancellationToken)
        {
            _semanticModel = semanticModel;
            _methodSymbol = methodSymbol;
            _cancellationToken = cancellationToken;
        }

        public string? FailureReason { get; private set; }

        public bool HasSnapshots => _snapshotFacts.Count != 0;

        public bool TryLowerInvocationTerm(
            InvocationExpressionSyntax invocation,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            term = null!;
            if (!IsOldValueInvocation(invocation)) return false;

            if (invocation.ArgumentList.Arguments.Count != 1)
            {
                FailureReason = "old(...) requires exactly one argument";
                return false;
            }

            var argument = invocation.ArgumentList.Arguments[0].Expression;
            if (ContainsOldValueInvocation(argument))
            {
                FailureReason = "nested old(...) expressions are not supported";
                return false;
            }

            if (RequiresContractHelpers.ContainsResultReference(argument))
            {
                FailureReason = "result is not available inside old(...)";
                return false;
            }

            if (ReferencesUserLocalOrUnsupportedParameter(
                    argument,
                    _semanticModel,
                    _methodSymbol,
                    _cancellationToken))
            {
                FailureReason = "local variables are not supported inside old(...)";
                return false;
            }

            var key = argument.WithoutTrivia().ToString();
            if (_snapshotTerms.TryGetValue(key, out term)) return true;

            var entryContext = new SymbolicLoweringContext(_semanticModel, _cancellationToken);
            var lowering = SymbolicSemanticPipeline.LowerTerm(argument, entryContext);
            if (lowering is not { IsExact: true, Value: { } entryTerm })
            {
                FailureReason = "old(...) expression is not supported by the current bounded proof engine";
                return false;
            }

            term = new SymbolicVariableTerm(
                "__sp_old_" + _nextSnapshotId.ToString(CultureInfo.InvariantCulture),
                entryTerm.Kind);
            _nextSnapshotId++;
            _snapshotTerms.Add(key, term);
            _snapshotFacts.Add(SymbolicFact.Exact(
                new SymbolicRelationAtom(SymbolicRelationOperator.Equal, term, entryTerm),
                invocation,
                "ir.path.ensures-old-snapshot"));
            return true;
        }

        public ITypeSymbol? ResolveInvocationTermType(InvocationExpressionSyntax invocation)
        {
            if (!IsOldValueInvocation(invocation) || invocation.ArgumentList.Arguments.Count != 1)
                return null;

            var argument = invocation.ArgumentList.Arguments[0].Expression;
            var typeInfo = _semanticModel.GetTypeInfo(argument, _cancellationToken);
            return typeInfo.ConvertedType ?? typeInfo.Type;
        }

        public SymbolicState CreateInitialState()
        {
            return new SymbolicState(_snapshotFacts);
        }
    }

    private readonly record struct EnsuresContract(
        string Condition,
        Location? Location,
        string Argument,
        string? InvalidReason);

    private readonly record struct CompletionSite(
        ExpressionSyntax? ResultExpression,
        Location Location,
        SyntaxNode QueryNode,
        bool IncludeCurrentStatementCompletionFacts,
        string DisplayText);
}
