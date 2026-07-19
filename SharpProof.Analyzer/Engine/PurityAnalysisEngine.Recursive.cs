namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    internal static PurityAnalysisResult DeterminePurityRecursiveInternal(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        HashSet<IMethodSymbol> visited,
        Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken,
        CompilationPurityService? purityService = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var activeSmtAnalysis = smtAnalysis ?? throw new ArgumentNullException(nameof(smtAnalysis));
        var indent = new string(' ', visited.Count * 2);


        if (purityCache.TryGetValue(methodSymbol, out var cachedResult)) return cachedResult;


        if (!visited.Add(methodSymbol))
        {
            var recursiveResult = PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(
                PurityEvidence.Create(
                    "unsupported_operation",
                    "RecursivePurityAnalysis",
                    symbol: methodSymbol,
                    catalogSource: "recursive_call"));
            purityCache[methodSymbol] = recursiveResult;
            return recursiveResult;
        }

        try
        {
            var declaringSyntax = GetDeclaringSyntax(methodSymbol, cancellationToken);

            var policy = PurityPolicyResolver.Resolve(methodSymbol, semanticModel.Compilation, attributePolicy);
            if (policy.Decision == PurityPolicyDecision.Impure && policy.Winner != null)
            {
                var winner = policy.Winner;
                var policyResult = ImpureResult(
                    declaringSyntax,
                    winner.Category,
                    winner.Source is "configured_impure_member" or "configured_impure_namespace_or_type"
                        ? "KnownImpureMethod"
                        : "MethodInvocationPurityRule",
                    symbol: methodSymbol,
                    catalogSource: winner.CatalogSource);
                purityCache[methodSymbol] = policyResult;
                return policyResult;
            }

            if (policy.Decision == PurityPolicyDecision.Pure)
            {
                purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                return PurityAnalysisResult.Pure;
            }

            var trustedMetadataPurity = GetTrustedMethodPurityMetadata(methodSymbol, semanticModel.Compilation);
            var hasTrustedGeneratedPurity = trustedMetadataPurity.HasTrustedGeneratedPurity;
            var generatedPurity = trustedMetadataPurity.GeneratedPurity;


            var bodySyntaxNode = GetBodySyntaxNode(methodSymbol, cancellationToken);


            if (methodSymbol.ReturnsByRef)
            {
                SyntaxNode? locationSyntax = declaringSyntax?.DescendantNodesAndSelf()
                    .OfType<RefTypeSyntax>()
                    .FirstOrDefault();

                locationSyntax ??= declaringSyntax?.DescendantNodesAndSelf()
                    .FirstOrDefault(n => n is IdentifierNameSyntax ins && ins.Identifier.ValueText == methodSymbol.Name)
                    ?.Parent;

                var escapeSyntax = locationSyntax ?? bodySyntaxNode;
                purityCache[methodSymbol] = escapeSyntax == null
                    ? ImpureResult(bodySyntaxNode)
                    : PurityAnalysisResult.Impure(
                        escapeSyntax,
                        PurityResourceStateFacts.CreateByRefReturnEscapeEvidence(methodSymbol, escapeSyntax));
                return purityCache[methodSymbol];
            }


            if (methodSymbol.IsExtern)
            {
                var externResult = ImpureResult(
                    declaringSyntax,
                    "unknown_external_call",
                    "MethodInvocationPurityRule",
                    methodSymbol,
                    "extern");
                purityCache[methodSymbol] = externResult;
                return externResult;
            }

            if (methodSymbol.IsAbstract || bodySyntaxNode == null)
            {
                if (IsBodylessSourceMemberAssumedPure(methodSymbol))
                {
                    purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                    return PurityAnalysisResult.Pure;
                }

                if (hasTrustedGeneratedPurity && !generatedPurity.IsPure)
                {
                    var generatedNoBodyResult = ImpureResult(
                        declaringSyntax,
                        generatedPurity.PrimaryCategory,
                        "MethodInvocationPurityRule",
                        methodSymbol,
                        "generated_purity_summary");
                    purityCache[methodSymbol] = generatedNoBodyResult;
                    return generatedNoBodyResult;
                }

                if (TryCreateBclFallbackImpurity(
                        methodSymbol,
                        declaringSyntax,
                        null,
                        "MethodInvocationPurityRule",
                        out var bclFallbackNoBodyResult))
                {
                    purityCache[methodSymbol] = bclFallbackNoBodyResult;
                    return bclFallbackNoBodyResult;
                }

                var noBodyResult = ImpureResult(
                    declaringSyntax,
                    "unknown_external_call",
                    "MethodInvocationPurityRule",
                    methodSymbol,
                    "no_body");
                purityCache[methodSymbol] = noBodyResult;
                return noBodyResult;
            }


            IOperation? methodBodyIOperation = null;
            if (bodySyntaxNode != null)
                try
                {
                    methodBodyIOperation = semanticModel.GetOperation(bodySyntaxNode, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    methodBodyIOperation = null;
                }

            var analysisContext = new PurityAnalysisContext(
                semanticModel,
                enforcePureAttributeSymbol,
                semanticModel.Compilation.GetTypeByMetadataName("SharpProof.Attributes.PureAttribute"),
                allowSynchronizationAttributeSymbol,
                visited,
                purityCache,
                methodSymbol,
                cancellationToken,
                purityService,
                activeSmtAnalysis,
                attributePolicy);
            var result = PurityAnalysisResult.Pure;
            var mergedNormalExitStateFromCfg = PurityAnalysisState.Pure;
            if (bodySyntaxNode != null)
            {
                var requiresNestedBodyFallback = methodBodyIOperation?.Parent != null;
                if (requiresNestedBodyFallback && methodBodyIOperation != null)
                    result = AnalyzeOperationSubtreePurity(
                        methodBodyIOperation,
                        analysisContext);
                else
                    result = AnalyzePurityUsingCFGInternal(
                        bodySyntaxNode,
                        analysisContext,
                        out mergedNormalExitStateFromCfg);
            }


            PurityAnalysisState? postCfgExitResourceState = null;
            if (result.IsPure && methodBodyIOperation != null)
            {
                postCfgExitResourceState = ExecutionVisibility.VisibleDescendants(methodBodyIOperation)
                    .OfType<IUsingDeclarationOperation>().Aggregate(
                    mergedNormalExitStateFromCfg,
                    static (state, declaration) => PurityResourceStateFacts.AddUsingDeclarationDisposeFacts(
                        state,
                        declaration));
            }

            if (result.IsPure &&
                postCfgExitResourceState.HasValue &&
                PurityResourceStateFacts.TryCreateMissingOwnedResourceDisposalResult(
                    postCfgExitResourceState.Value,
                    methodSymbol,
                    semanticModel,
                    cancellationToken,
                    out var missingDisposeResult))
                result = missingDisposeResult;

            purityCache[methodSymbol] = result;
            return result;
        }
        finally
        {
            visited.Remove(methodSymbol);
        }
    }

    private static bool IsBodylessSourceMemberAssumedPure(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ContainingType?.Locations.Any(static location => !location.IsInMetadata) != true)
            return false;

        return methodSymbol.MethodKind switch
        {
            MethodKind.PropertyGet =>
                methodSymbol.DeclaringSyntaxReferences.Length > 0 &&
                !methodSymbol.IsAbstract &&
                methodSymbol.ContainingType.TypeKind != TypeKind.Interface,
            MethodKind.Constructor or MethodKind.StaticConstructor => !methodSymbol.IsExtern,
            _ => false
        };
    }
}
