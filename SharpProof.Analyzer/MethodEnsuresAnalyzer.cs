using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer;

internal static class MethodEnsuresAnalyzer
{
    internal static void AnalyzeSymbolForEnsures(
        SyntaxNodeAnalysisContext context,
        CompilationPurityService purityService,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy)
    {
        if (context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not IMethodSymbol
            methodSymbol) return;

        if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true) return;

        var contracts = CollectContracts(methodSymbol, attributePolicy, context.CancellationToken);
        if (contracts.Length == 0) return;

        contracts = ReportAndFilterInvalidContracts(
            contracts,
            context,
            methodSymbol,
            baseline);
        if (contracts.Length == 0) return;

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
                if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
            }

            return;
        }

        var requiresAssumptions = CollectRequiresAssumptions(methodSymbol, attributePolicy, context.CancellationToken);
        var completionSites =
            CollectCompletionSites(methodSymbol, context.Node, context.SemanticModel, context.CancellationToken);
        if (completionSites.Length == 0) return;

        var queryService = new SymbolicQueryService();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var contract in contracts)
        {
            if (!TryParseCondition(contract.Condition, out var conditionStatement, out var conditionExpression))
            {
                var diagnostic = CreateUnsupportedDiagnostic(
                    methodSymbol,
                    contract.Condition,
                    contract.Location,
                    "condition parse failure",
                    null);
                if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

                continue;
            }

            if (!TryCreateSpeculativeConditionModel(
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
                if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

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
                if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

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
                if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

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
                    if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

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
                    if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

                    continue;
                }

                var proofCondition =
                    RequiresContractHelpers.CombineAsImplication(requiresAssumptions, rewrittenCondition);
                var proof = TryCreateOldAwareProofCondition(
                    proofCondition,
                    methodSymbol,
                    context.SemanticModel,
                    completionSite,
                    context.CancellationToken,
                    out var symbolicCondition,
                    out var initialState,
                    out var oldFailureReason)
                    ? queryService.ProveAtSyntaxNode(
                        context.SemanticModel,
                        completionSite.QueryNode,
                        proofCondition,
                        symbolicCondition,
                        initialState,
                        purityService.SmtAnalysis,
                        completionSite.IncludeCurrentStatementCompletionFacts,
                        context.CancellationToken)
                    : oldFailureReason == null
                        ? queryService.ProveAtSyntaxNode(
                            context.SemanticModel,
                            completionSite.QueryNode,
                            proofCondition,
                            purityService.SmtAnalysis,
                            completionSite.IncludeCurrentStatementCompletionFacts,
                            context.CancellationToken)
                        : new SymbolicConditionProofResult(
                            proofCondition,
                            SymbolicTruthValue.Unknown,
                            oldFailureReason);

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
                    if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);

                    continue;
                }

                var unsupportedDiagnostic = CreateUnsupportedDiagnostic(
                    methodSymbol,
                    contract.Condition,
                    completionSite.Location,
                    FormatUnknownReason(proof),
                    contract.Location == null ? null : new[] { contract.Location },
                    proof.AnalysisTruncation);
                if (!baseline.IsSuppressed(unsupportedDiagnostic)) context.ReportDiagnostic(unsupportedDiagnostic);
            }
        }
    }

    private static ImmutableArray<EnsuresContract> CollectContracts(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        var builder = ImmutableArray.CreateBuilder<EnsuresContract>();
        foreach (var attribute in methodSymbol.GetAttributes())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!attributePolicy.IsAccepted(attribute, "EnsuresAttribute")) continue;

            var condition = attribute.ConstructorArguments.Length == 1
                ? attribute.ConstructorArguments[0].Value as string
                : null;
            var location = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken).GetLocation();
            var invalidReason = GetInvalidContractReason(attribute, condition);
            builder.Add(new EnsuresContract(
                condition ?? string.Empty,
                location,
                GetAttributeArgumentText(attribute, cancellationToken),
                invalidReason));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<RequiresContract> CollectRequiresAssumptions(
        IMethodSymbol methodSymbol,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        return RequiresContractHelpers.ValidContracts(methodSymbol, attributePolicy, cancellationToken)
            .Where(contract =>
                RequiresContractHelpers.TryParseCondition(contract.Condition, out _, out var conditionExpression) &&
                !RequiresContractHelpers.ContainsResultReference(conditionExpression))
            .ToImmutableArray();
    }

    private static ImmutableArray<EnsuresContract> ReportAndFilterInvalidContracts(
        ImmutableArray<EnsuresContract> contracts,
        SyntaxNodeAnalysisContext context,
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
            if (!baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
        }

        return validContracts.ToImmutable();
    }

    private static string? GetInvalidContractReason(AttributeData attribute, string? condition)
    {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value is not string)
            return "expected a string condition";

        return string.IsNullOrWhiteSpace(condition)
            ? "condition must not be empty"
            : null;
    }

    private static string GetAttributeArgumentText(AttributeData attribute, CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is AttributeSyntax attributeSyntax)
            return attributeSyntax.ArgumentList?.Arguments.FirstOrDefault()?.ToString() ?? "<missing>";

        return "<missing>";
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
        CancellationToken cancellationToken)
    {
        var rootOperation =
            MethodBodyOperationResolver.GetMethodBodyRootOperation(methodNode, semanticModel, cancellationToken, true);
        if (rootOperation == null) return ImmutableArray<CompletionSite>.Empty;

        var builder = ImmutableArray.CreateBuilder<CompletionSite>();
        foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
            if (operation is IReturnOperation returnOperation)
            {
                if (IsCompilerMarkedUnreachable(operation.Syntax, semanticModel, cancellationToken)) continue;

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

        if (TryGetExpressionBody(methodNode, out var expressionBody))
        {
            var hasResultValue = HasResultValue(methodSymbol);
            builder.Add(new CompletionSite(
                hasResultValue ? expressionBody : null,
                expressionBody.GetLocation(),
                expressionBody,
                !hasResultValue,
                hasResultValue ? expressionBody.ToString() : "normal completion"));
        }
        else if (TryGetBodyBlock(methodNode, out var bodyBlock) &&
                 BodyEndPointIsReachable(bodyBlock, semanticModel))
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

    private static bool IsCompilerMarkedUnreachable(
        SyntaxNode syntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return semanticModel.GetDiagnostics(syntax.Span, cancellationToken)
            .Any(diagnostic => diagnostic.Id == "CS0162");
    }

    private static bool HasResultValue(IMethodSymbol methodSymbol)
    {
        return methodSymbol.MethodKind != MethodKind.Constructor &&
               methodSymbol.MethodKind != MethodKind.StaticConstructor &&
               !methodSymbol.ReturnsVoid;
    }

    private static bool TryGetBodyBlock(SyntaxNode methodNode, out BlockSyntax block)
    {
        block = methodNode switch
        {
            MethodDeclarationSyntax { Body: { } body } => body,
            ConstructorDeclarationSyntax { Body: { } body } => body,
            DestructorDeclarationSyntax { Body: { } body } => body,
            OperatorDeclarationSyntax { Body: { } body } => body,
            ConversionOperatorDeclarationSyntax { Body: { } body } => body,
            AccessorDeclarationSyntax { Body: { } body } => body,
            LocalFunctionStatementSyntax { Body: { } body } => body,
            _ => null!
        };

        return block != null;
    }

    private static bool BodyEndPointIsReachable(BlockSyntax body, SemanticModel semanticModel)
    {
        var controlFlow = semanticModel.AnalyzeControlFlow(body);
        return controlFlow == null ||
               !controlFlow.Succeeded ||
               controlFlow.EndPointIsReachable;
    }

    private static Location GetBodyCompletionLocation(BlockSyntax body)
    {
        return body.CloseBraceToken.GetLocation();
    }

    private static bool TryParseCondition(
        string conditionText,
        out IfStatementSyntax conditionStatement,
        out ExpressionSyntax conditionExpression)
    {
        var statement = SyntaxFactory.ParseStatement("if (" + conditionText + ") { }");
        if (statement.ContainsDiagnostics || statement is not IfStatementSyntax ifStatement)
        {
            conditionStatement = null!;
            conditionExpression = null!;
            return false;
        }

        conditionStatement = ifStatement;
        conditionExpression = ifStatement.Condition;
        return true;
    }

    private static bool TryCreateSpeculativeConditionModel(
        SemanticModel semanticModel,
        int position,
        IfStatementSyntax ifStatement,
        out SemanticModel speculativeModel)
    {
        if (semanticModel.TryGetSpeculativeSemanticModel(position, ifStatement, out var model) &&
            model != null)
        {
            speculativeModel = model;
            return true;
        }

        speculativeModel = null!;
        return false;
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
        if (!TryParseCondition(conditionText, out _, out var conditionExpression)) return false;

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
        symbolicCondition = null!;
        initialState = new SymbolicState();
        failureReason = null;

        if (!TryParseCondition(proofCondition, out var proofStatement, out var proofExpression))
        {
            failureReason = "condition parse failure";
            return false;
        }

        if (!ContainsOldValueInvocation(proofExpression)) return false;

        if (!TryCreateSpeculativeConditionModel(
                semanticModel,
                GetSpeculativePosition(completionSite),
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
            invocationTermLowerer: snapshots.TryLowerInvocationTerm);
        if (!SymbolicIrLowerer.TryLowerCondition(proofExpression, loweringContext, out symbolicCondition))
        {
            failureReason = snapshots.FailureReason ??
                            "old(...) expression is not supported by the current bounded proof engine";
            return false;
        }

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
        var properties = AddBaselineProperties(
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
        if (unknownReasonInfo.IsUnknown)
            properties = UnknownReasonDiagnosticProperties.Add(properties, unknownReasonInfo);
        properties = AnalysisTruncationDiagnosticProperties.Add(properties, proof.AnalysisTruncation);
        properties = ExplainDiagnosticProperties.Add(
            properties,
            completionSite.Location,
            condition,
            proof.Proof.Status.ToString(),
            FormatUnknownReason(proof),
            condition);

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
        var properties = AddBaselineProperties(
            ImmutableDictionary<string, string?>.Empty
                .Add(SharpProofDiagnostics.EnsuresConditionProperty, condition)
                .Add(SharpProofDiagnostics.EnsuresProofStatusProperty, SymbolicProofStatus.Unknown.ToString())
                .Add(SharpProofDiagnostics.EnsuresUnknownReasonProperty, reason)
                .Add(SharpProofDiagnostics.EnsuresFailureReasonProperty, reason),
            methodSymbol,
            "EnsuresUnsupported",
            condition,
            "unsupported:" + condition + "@" + FormatLocationKey(location) + "|" + reason);
        properties = UnknownReasonDiagnosticProperties.Add(
            properties,
            SymbolicUnknownReasonTaxonomy.ForEnsures(reason));
        properties = AnalysisTruncationDiagnosticProperties.Add(
            properties,
            analysisTruncation ?? SymbolicAnalysisTruncationInfo.None);
        properties = ExplainDiagnosticProperties.Add(
            properties,
            location,
            condition,
            SymbolicProofStatus.Unknown.ToString(),
            reason,
            condition);

        return Diagnostic.Create(
            SharpProofDiagnostics.EnsuresUnsupportedRule,
            location,
            additionalLocations,
            properties,
            condition,
            methodSymbol.Name,
            reason);
    }

    private static ImmutableDictionary<string, string?> AddBaselineProperties(
        ImmutableDictionary<string, string?> properties,
        IMethodSymbol methodSymbol,
        string operationKind,
        string contractText,
        string evidenceKey)
    {
        var syntaxTree = methodSymbol.Locations.FirstOrDefault(location => location.SourceTree != null)?.SourceTree;
        return syntaxTree == null
            ? properties
            : BaselineDiagnosticProperties.Add(
                properties,
                methodSymbol,
                syntaxTree,
                operationKind,
                contractText,
                evidenceKey);
    }

    private static string FormatLocationKey(Location? location)
    {
        return location == null
            ? "none"
            : location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture) +
              ":" +
              location.SourceSpan.End.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatUnknownReason(SymbolicConditionProofResult proof)
    {
        if (proof.Proof.UnknownReason != SymbolicUnknownReason.None &&
            proof.Proof.UnknownReason != SymbolicUnknownReason.Unknown)
            return proof.Proof.UnknownReason.ToString();

        return proof.Reason switch
        {
            "condition_parse_failure" => "condition parse failure",
            "condition_binding_failure" => "condition binding failure",
            "condition_not_supported" => "condition is not supported by the current bounded proof engine",
            "smt_required" => "SMT is required for [Ensures] verification",
            _ when string.IsNullOrWhiteSpace(proof.Reason) => "unknown",
            _ => proof.Reason.Replace('_', ' ')
        };
    }

    private static bool TryGetExpressionBody(SyntaxNode methodNode, out ExpressionSyntax expression)
    {
        expression = methodNode switch
        {
            MethodDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
            ConstructorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
            OperatorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
            ConversionOperatorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
            AccessorDeclarationSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
            LocalFunctionStatementSyntax { ExpressionBody: { } expressionBody } => expressionBody.Expression,
            _ => null!
        };
        return expression != null;
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

            if (node.Parent is MemberAccessExpressionSyntax memberAccess &&
                ReferenceEquals(memberAccess.Name, node))
                return base.VisitIdentifierName(node);

            if (node.Parent is QualifiedNameSyntax qualifiedName &&
                ReferenceEquals(qualifiedName.Right, node))
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
            if (!IsResult(node.Expression) || !TryGetNullPatternPolarity(node.Pattern, out var matchesNonNull))
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

        private static bool TryGetNullPatternPolarity(PatternSyntax pattern, out bool matchesNonNull)
        {
            if (pattern is ConstantPatternSyntax { Expression.RawKind: (int)SyntaxKind.NullLiteralExpression })
            {
                matchesNonNull = false;
                return true;
            }

            if (pattern is UnaryPatternSyntax unaryPattern &&
                unaryPattern.IsKind(SyntaxKind.NotPattern) &&
                TryGetNullPatternPolarity(unaryPattern.Pattern, out var nestedMatchesNonNull))
            {
                matchesNonNull = !nestedMatchesNonNull;
                return true;
            }

            matchesNonNull = false;
            return false;
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
            if (!SymbolicIrLowerer.TryLowerTerm(argument, entryContext, out var entryTerm))
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
