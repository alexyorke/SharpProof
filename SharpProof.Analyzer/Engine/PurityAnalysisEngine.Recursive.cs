using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

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
                _purityRules,
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
            if (result.IsPure)
                if (methodBodyIOperation != null)
                {
                    var postCfgReturnState = mergedNormalExitStateFromCfg;
                    postCfgExitResourceState = postCfgReturnState;
                    foreach (var usingDeclaration in ExecutionVisibility.VisibleDescendants(methodBodyIOperation)
                                 .OfType<IUsingDeclarationOperation>())
                        postCfgExitResourceState = PurityResourceStateFacts.AddUsingDeclarationDisposeFacts(
                            postCfgExitResourceState.Value,
                            usingDeclaration);
                    var postCfgProbeState = postCfgReturnState.WithPathState(
                        SymbolicRuntimeTypeFacts.RetainExactRuntimeTypes(postCfgReturnState.PathState));

                    foreach (var usingOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).Where(op =>
                                 op.Kind == OperationKind.Using || op.Kind == OperationKind.UsingDeclaration))
                    {
                        var usingResult = CheckSingleOperation(usingOp, analysisContext, postCfgProbeState);
                        if (!usingResult.IsPure)
                        {
                            result = usingResult;
                            goto PostCfgChecksDone;
                        }
                    }

                    foreach (var forEachOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation)
                                 .OfType<IForEachLoopOperation>())
                    {
                        if (ShouldSkipPostCfgDirectPurityProbe(forEachOp, semanticModel, activeSmtAnalysis,
                                cancellationToken)) continue;

                        var forEachResult = forEachOp.IsAsynchronous
                            ? LoopPurityRule.CheckForEachAsyncEnumeratorPurity(
                                forEachOp.Collection,
                                analysisContext)
                            : LoopPurityRule.CheckForEachEnumeratorPurity(
                                forEachOp.Collection,
                                analysisContext);
                        if (!forEachResult.IsPure)
                        {
                            result = forEachResult;
                            goto PostCfgChecksDone;
                        }
                    }


                    foreach (var firstThrowOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation)
                                 .OfType<IThrowOperation>())
                    {
                        if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                                firstThrowOp.Syntax,
                                semanticModel,
                                cancellationToken,
                                activeSmtAnalysis))
                            continue;

                        if (firstThrowOp.Exception != null)
                        {
                            var exResult = CheckSingleOperation(firstThrowOp.Exception, analysisContext,
                                PurityAnalysisState.Pure);
                            if (!exResult.IsPure)
                            {
                                result = PurityAnalysisResult.Impure(
                                    exResult.ImpureSyntaxNode ?? firstThrowOp.Syntax,
                                    exResult.Evidence);
                                goto PostCfgChecksDone;
                            }
                        }

                        result = PurityAnalysisResult.Impure(
                            firstThrowOp.Syntax,
                            PurityEvidence.Create(
                                "throw",
                                "ThrowOperationPurityRule",
                                firstThrowOp));
                        goto PostCfgChecksDone;
                    }


                    foreach (var tryOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation)
                                 .OfType<ITryOperation>())
                    {
                        foreach (var catchClause in tryOp.Catches)
                        {
                            var catchResult = AnalyzeOperationSubtreePurity(catchClause, analysisContext);
                            if (!catchResult.IsPure)
                            {
                                result = catchResult;
                                goto PostCfgChecksDone;
                            }
                        }

                        if (tryOp.Finally != null)
                        {
                            var finallyResult = AnalyzeOperationSubtreePurity(tryOp.Finally, analysisContext);
                            if (!finallyResult.IsPure)
                            {
                                result = finallyResult;
                                goto PostCfgChecksDone;
                            }
                        }
                    }


                    foreach (var invocationOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation)
                                 .OfType<IInvocationOperation>())
                    {
                        if (ShouldSkipPostCfgDirectPurityProbe(invocationOp, semanticModel, activeSmtAnalysis,
                                cancellationToken)) continue;

                        var hasSemanticKnownImpureCatalogSource = PurityKnownBclSemantics.TryGetSemanticKnownImpureCatalogSource(
                            invocationOp,
                            out var semanticKnownImpureCatalogSource);
                        if (invocationOp.TargetMethod != null &&
                            !PurityKnownBclSemantics.IsArrayAsReadOnlyInvocation(invocationOp) &&
                            !PurityKnownBclSemantics.IsArrayInterfaceGetEnumeratorInvocation(
                                invocationOp,
                                semanticModel,
                                cancellationToken) &&
                            !IsTransientCharArrayConsumedByStringConstructor(invocationOp, semanticModel))
                        {
                            var targetMethod = invocationOp.TargetMethod.OriginalDefinition;
                            if (hasSemanticKnownImpureCatalogSource)
                            {
                                result = ImpureResult(
                                    invocationOp,
                                    "catalog_hit",
                                    "MethodInvocationPurityRule",
                                    targetMethod,
                                    semanticKnownImpureCatalogSource);
                                goto PostCfgChecksDone;
                            }

                            if (PurityKnownBclSemantics.IsInvariantCultureDeterministicParseInvocation(invocationOp)) continue;

                            var invocationMetadataPurity = GetTrustedMethodPurityMetadata(
                                targetMethod,
                                semanticModel.Compilation);
                            var knownImpureSource = invocationMetadataPurity.KnownImpureMemberSource;
                            var hasConfiguredKnownImpure = invocationMetadataPurity.HasConfiguredKnownImpureMember;
                            var postCfgGeneratedPurity = invocationMetadataPurity.GeneratedPurity;
                            var hasTrustedGeneratedPurityForInvocation =
                                invocationMetadataPurity.HasTrustedGeneratedPurity;

                            if (hasConfiguredKnownImpure)
                            {
                                result = ImpureResult(
                                    invocationOp,
                                    "catalog_hit",
                                    "MethodInvocationPurityRule",
                                    targetMethod,
                                    knownImpureSource);
                                goto PostCfgChecksDone;
                            }

                            if (hasTrustedGeneratedPurityForInvocation &&
                                !MethodInvocationPurityRule.ShouldDeferToSpecializedDispatchPurity(targetMethod))
                            {
                                if (postCfgGeneratedPurity.IsPure) continue;

                                if (!postCfgGeneratedPurity.IsPure)
                                {
                                    var invocationRuleResult = CheckSingleOperation(invocationOp, analysisContext,
                                        postCfgReturnState);
                                    if (invocationRuleResult.IsPure) continue;

                                    result = invocationRuleResult;
                                    goto PostCfgChecksDone;
                                }
                            }

                            if (knownImpureSource != null)
                            {
                                result = ImpureResult(
                                    invocationOp,
                                    "catalog_hit",
                                    "MethodInvocationPurityRule",
                                    targetMethod,
                                    knownImpureSource);
                                goto PostCfgChecksDone;
                            }
                        }
                    }

                    var directThrowOnlySyntax = TryGetDirectThrowOnlySyntax(bodySyntaxNode);
                    if (directThrowOnlySyntax != null)
                    {
                        result = PurityAnalysisResult.Impure(
                            directThrowOnlySyntax,
                            PurityEvidence.Create(
                                "throw",
                                "ThrowOperationPurityRule",
                                syntaxNode: directThrowOnlySyntax));
                        goto PostCfgChecksDone;
                    }


                    foreach (var operation in ExecutionVisibility.VisibleDescendants(methodBodyIOperation))
                    {
                        if (ShouldSkipPostCfgDirectPurityProbe(operation, semanticModel, activeSmtAnalysis,
                                cancellationToken)) continue;

                        if (TryGetOperatorMethodForDirectPurityCheck(
                                operation,
                                includeCompoundAssignments: true,
                                out var operatorMethod) &&
                            operatorMethod != null)
                        {
                            var operatorPurity = PurityCalleeResolver.GetCalleePurity(operatorMethod, analysisContext);

                            if (!operatorPurity.IsPure)
                            {
                                result = PurityAnalysisResult.Impure(operation.Syntax);
                                goto PostCfgChecksDone;
                            }
                        }
                    }
                }

            PostCfgChecksDone:;

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
