using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static PurityAnalysisResult AnalyzePurityUsingCFGInternal(
        SyntaxNode bodyNode,
        SemanticModel semanticModel,
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        HashSet<IMethodSymbol> visited,
        IMethodSymbol containingMethodSymbol,
        Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CompilationPurityService? purityService,
        CancellationToken cancellationToken,
        out ImmutableDictionary<ISymbol, PotentialTargets> mergedDelegateTargetsFromBlocks,
        out ImmutableHashSet<CaptureId> mergedOwnedArrayFlowCapturesFromBlocks,
        out ImmutableHashSet<ISymbol> mergedOwnedLocalArraysFromBlocks,
        out ImmutableDictionary<ISymbol, INamedTypeSymbol> mergedLocalConcreteTypesFromBlocks,
        out SymbolicState mergedPathStateFromBlocks)
    {
        cancellationToken.ThrowIfCancellationRequested();
        mergedDelegateTargetsFromBlocks =
            ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
        mergedOwnedArrayFlowCapturesFromBlocks = ImmutableHashSet<CaptureId>.Empty;
        mergedOwnedLocalArraysFromBlocks = ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
        mergedLocalConcreteTypesFromBlocks =
            ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
        mergedPathStateFromBlocks = new SymbolicState();
        // Roslyn 4.x: Create(BlockSyntax|ArrowClause, model) throws ("operation has a non-null parent").
        // Create(BaseMethodDeclarationSyntax|LocalFunctionStatement|ConstructorDeclaration|... , model) is the supported root.
        ControlFlowGraph? cfg = null;
        try
        {
            cfg = ControlFlowGraph.Create(bodyNode, semanticModel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return PurityAnalysisResult.Impure(bodyNode);
        }

        if (cfg == null || cfg.Blocks.IsEmpty) return PurityAnalysisResult.Pure;


        var blockStates = new Dictionary<BasicBlock, PurityAnalysisState>(cfg.Blocks.Length);
        var exitBlockStates = new Dictionary<BasicBlock, PurityAnalysisState>(cfg.Blocks.Length);
        var worklist = new Queue<BasicBlock>();
        var inQueue = new HashSet<BasicBlock>();

        if (cfg.Blocks.Any())
        {
            var entryBlock = cfg.Blocks.First();

            blockStates[entryBlock] = CreateInitialRequiresState(
                containingMethodSymbol,
                bodyNode,
                semanticModel,
                attributePolicy,
                cancellationToken);
            worklist.Enqueue(entryBlock);
            inQueue.Add(entryBlock);
        }
        else
        {
            return PurityAnalysisResult.Pure;
        }


        var loopIterations = 0;

        while (worklist.Count > 0 && loopIterations < cfg.Blocks.Length * 50)
        {
            loopIterations++;

            var currentBlock = worklist.Dequeue();
            inQueue.Remove(currentBlock);

            if (!blockStates.TryGetValue(currentBlock, out var stateBefore))
            {
                stateBefore = PurityAnalysisState.Pure;
                blockStates[currentBlock] = stateBefore;
            }


            var stateAfter = ApplyTransferFunction(
                currentBlock,
                stateBefore,
                semanticModel,
                enforcePureAttributeSymbol,
                allowSynchronizationAttributeSymbol,
                visited,
                containingMethodSymbol,
                purityCache,
                smtAnalysis,
                attributePolicy,
                purityService,
                cancellationToken);

            exitBlockStates[currentBlock] = stateAfter;


            if (TryGetConstantBranchDecision(currentBlock.BranchValue, semanticModel, smtAnalysis, cancellationToken,
                    out var takeConditionalSuccessor))
            {
                var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock.BranchValue);
                var takenSuccessor = takeConditionalSuccessor
                    ? trueUsesConditionalSuccessor
                        ? currentBlock.ConditionalSuccessor?.Destination
                        : currentBlock.FallThroughSuccessor?.Destination
                    : trueUsesConditionalSuccessor
                        ? currentBlock.FallThroughSuccessor?.Destination
                        : currentBlock.ConditionalSuccessor?.Destination;
                if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel,
                        takeConditionalSuccessor, smtAnalysis, cancellationToken, out var takenState))
                    PropagateToSuccessor(takenSuccessor, takenState, blockStates, worklist, inQueue);
            }
            else
            {
                var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock.BranchValue);

                if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel,
                        trueUsesConditionalSuccessor, smtAnalysis, cancellationToken, out var conditionalState))
                    PropagateToSuccessor(currentBlock.ConditionalSuccessor?.Destination, conditionalState, blockStates,
                        worklist, inQueue);

                if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel,
                        !trueUsesConditionalSuccessor, smtAnalysis, cancellationToken, out var fallThroughState))
                    PropagateToSuccessor(currentBlock.FallThroughSuccessor?.Destination, fallThroughState, blockStates,
                        worklist, inQueue);
            }
        }

        if (worklist.Count == 0)
        {
        }

        mergedDelegateTargetsFromBlocks = MergeDelegateTargetMapsFromBlockStates(exitBlockStates.Values);
        mergedOwnedArrayFlowCapturesFromBlocks = MergeOwnedArrayFlowCapturesFromBlockStates(exitBlockStates.Values);
        mergedOwnedLocalArraysFromBlocks = MergeOwnedLocalArraySymbolsFromBlockStates(exitBlockStates.Values);
        mergedLocalConcreteTypesFromBlocks = MergeLocalConcreteTypesFromBlockStates(exitBlockStates.Values);
        var mergedExitSymbolVersions = MergeSmtSymbolVersionsAcrossAll(
            exitBlockStates.Values.Select(static state => state.SmtSymbolVersions));
        mergedPathStateFromBlocks = MergePathStatesAcrossAll(
            exitBlockStates.Values.ToArray(),
            mergedExitSymbolVersions);

        var finalResult = PurityAnalysisResult.Pure;

        foreach (var exitState in exitBlockStates.Values)
            if (exitState.HasPotentialImpurity)
            {
                finalResult = exitState.FirstImpureSyntaxNode != null
                    ? PurityAnalysisResult.Impure(exitState.FirstImpureSyntaxNode, exitState.FirstImpurityEvidence)
                    : PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(exitState.FirstImpurityEvidence);
                return finalResult;
            }

        return finalResult;
    }

    private static PurityAnalysisState ApplyTransferFunction(
        BasicBlock block,
        PurityAnalysisState stateBefore,
        SemanticModel semanticModel,
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        HashSet<IMethodSymbol> visited,
        IMethodSymbol containingMethodSymbol,
        Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CompilationPurityService? purityService,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (stateBefore.HasPotentialImpurity) return stateBefore;

        var blockSourceNode = block.Operations.FirstOrDefault()?.Syntax ?? block.BranchValue?.Syntax;
        if ((!stateBefore.PathState.Facts.IsDefaultOrEmpty ||
             !stateBefore.PathState.PathConditions.IsDefaultOrEmpty) &&
            IsPathStateUnsatisfiable(stateBefore, stateBefore.PathState, smtAnalysis, blockSourceNode))
            return stateBefore;


        var pureAttributeSymbol_block =
            semanticModel.Compilation.GetTypeByMetadataName("SharpProof.Attributes.PureAttribute");
        var ruleContext = new PurityAnalysisContext(
            semanticModel,
            enforcePureAttributeSymbol,
            pureAttributeSymbol_block,
            allowSynchronizationAttributeSymbol,
            visited,
            purityCache,
            containingMethodSymbol,
            _purityRules,
            cancellationToken,
            purityService,
            smtAnalysis,
            attributePolicy);


        var currentStateInBlock = stateBefore;
        PurityAnalysisResult? deferredRecursiveImpurity = null;
        SyntaxNode? deferredRecursiveSyntax = null;
        foreach (var op in block.Operations)
        {
            if (op == null) continue;


            if (op is IFlowCaptureOperation flowCap)
            {
                var valResult = CheckSingleOperation(flowCap.Value, ruleContext, currentStateInBlock);
                currentStateInBlock = currentStateInBlock.WithFlowCaptureResult(flowCap.Id, valResult);
                if (!valResult.IsPure)
                {
                    if (IsImpurityProvenUnreachable(valResult, semanticModel, smtAnalysis, cancellationToken)) continue;

                    currentStateInBlock = currentStateInBlock.WithImpurity(valResult, flowCap.Syntax);
                    break;
                }

                currentStateInBlock = UpdateDelegateMapForOperation(flowCap, ruleContext, currentStateInBlock);
                continue;
            }

            var opResult = CheckSingleOperation(op, ruleContext, currentStateInBlock);

            if (!opResult.IsPure)
            {
                if (IsImpurityProvenUnreachable(opResult, semanticModel, smtAnalysis, cancellationToken)) continue;


                if (IsRecursivePlaceholderImpurity(opResult))
                {
                    deferredRecursiveImpurity ??= opResult.WithEvidence(
                        opResult.Evidence.WithSymbol(containingMethodSymbol.ToDisplayString(_signatureFormat)));
                    deferredRecursiveSyntax ??= op.Syntax;
                    continue;
                }

                currentStateInBlock = currentStateInBlock.WithImpurity(opResult, op.Syntax);
                break;
            }


            currentStateInBlock = UpdateDelegateMapForOperation(op, ruleContext, currentStateInBlock);
        }

        if (!currentStateInBlock.HasPotentialImpurity && deferredRecursiveImpurity.HasValue)
        {
            var fallbackSyntax = deferredRecursiveSyntax ??
                                 block.Operations.FirstOrDefault()?.Syntax ??
                                 containingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                                     ?.GetSyntax(cancellationToken);

            currentStateInBlock = currentStateInBlock.WithImpurity(
                deferredRecursiveImpurity.Value,
                fallbackSyntax!);
        }

        if (!currentStateInBlock.HasPotentialImpurity &&
            block.BranchValue != null &&
            TryCreateThrowBranchImpurity(block.BranchValue, ruleContext, currentStateInBlock,
                out var throwBranchResult))
        {
            currentStateInBlock = currentStateInBlock.WithImpurity(throwBranchResult,
                throwBranchResult.ImpureSyntaxNode ?? block.BranchValue.Syntax);
        }
        else if (!currentStateInBlock.HasPotentialImpurity &&
                 block.BranchValue != null &&
                 ShouldAnalyzeStateSensitiveBranchValue(block.BranchValue.Syntax))
        {
            var branchValueResult = CheckSingleOperation(block.BranchValue, ruleContext, currentStateInBlock);
            if (!branchValueResult.IsPure)
            {
                if (!IsImpurityProvenUnreachable(branchValueResult, semanticModel, smtAnalysis, cancellationToken))
                    currentStateInBlock = currentStateInBlock.WithImpurity(branchValueResult, block.BranchValue.Syntax);
            }
            else
            {
                currentStateInBlock =
                    UpdateDelegateMapForOperation(block.BranchValue, ruleContext, currentStateInBlock);
            }
        }

        return currentStateInBlock;
    }

    private static bool TryCreateThrowBranchImpurity(
        IOperation branchValue,
        PurityAnalysisContext context,
        PurityAnalysisState currentState,
        out PurityAnalysisResult result)
    {
        result = PurityAnalysisResult.Pure;

        var throwSyntax = branchValue.Syntax.FirstAncestorOrSelf<ThrowStatementSyntax>() ??
                          (SyntaxNode?)branchValue.Syntax.FirstAncestorOrSelf<ThrowExpressionSyntax>();
        if (throwSyntax == null) return false;

        var exceptionResult = CheckSingleOperation(branchValue, context, currentState);
        if (!exceptionResult.IsPure)
        {
            result = exceptionResult;
            return true;
        }

        result = PurityAnalysisResult.Impure(
            throwSyntax,
            PurityEvidence.Create(
                "throw",
                "ThrowOperationPurityRule",
                syntaxNode: throwSyntax,
                operationKindOverride: OperationKind.Throw.ToString()));
        return true;
    }

    private static bool IsRecursivePlaceholderImpurity(PurityAnalysisResult result)
    {
        return !result.IsPure &&
               result.Evidence.RuleName == "RecursivePurityAnalysis" &&
               result.Evidence.CatalogSource == "recursive_call";
    }


    private static PurityAnalysisResult AnalyzeOperationSubtreePurity(
        IOperation rootOperation,
        SemanticModel semanticModel,
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        HashSet<IMethodSymbol> visited,
        IMethodSymbol containingMethodSymbol,
        Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
        SmtAnalysisService smtAnalysis,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CompilationPurityService? purityService,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pureAttributeSymbol =
            semanticModel.Compilation.GetTypeByMetadataName("SharpProof.Attributes.PureAttribute");
        var context = new PurityAnalysisContext(
            semanticModel,
            enforcePureAttributeSymbol,
            pureAttributeSymbol,
            allowSynchronizationAttributeSymbol,
            visited,
            purityCache,
            containingMethodSymbol,
            _purityRules,
            cancellationToken,
            purityService,
            smtAnalysis,
            attributePolicy);

        var currentState = CreateInitialRequiresState(
            containingMethodSymbol,
            rootOperation.Syntax,
            semanticModel,
            attributePolicy,
            cancellationToken);
        var visitedOperations = new HashSet<IOperation>();
        foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
        {
            var operationToAnalyze = operation is IExpressionStatementOperation expressionStatementOperation
                ? expressionStatementOperation.Operation
                : operation;
            if (!visitedOperations.Add(operationToAnalyze)) continue;

            if (operation is IFlowCaptureOperation flowCaptureOperation)
            {
                var valueResult = CheckSingleOperation(flowCaptureOperation.Value, context, currentState);
                currentState = currentState.WithFlowCaptureResult(flowCaptureOperation.Id, valueResult);
                if (!valueResult.IsPure) return valueResult;

                currentState = UpdateDelegateMapForOperation(flowCaptureOperation, context, currentState);
                continue;
            }

            var operationResult = CheckSingleOperation(operationToAnalyze, context, currentState);
            if (!operationResult.IsPure) return operationResult;

            currentState = UpdateDelegateMapForOperation(operationToAnalyze, context, currentState);
        }

        return currentState.HasPotentialImpurity
            ? ImpureResult(currentState.FirstImpureSyntaxNode, currentState.FirstImpurityEvidence)
            : PurityAnalysisResult.Pure;
    }

    private static SyntaxNode? TryGetDirectThrowOnlySyntax(SyntaxNode? bodySyntaxNode)
    {
        switch (bodySyntaxNode)
        {
            case BlockSyntax blockSyntax
                when blockSyntax.Statements.Count == 1:
                return TryGetDirectThrowOnlySyntax(blockSyntax.Statements[0]);
            case ThrowStatementSyntax throwStatementSyntax:
                return throwStatementSyntax;
            case ArrowExpressionClauseSyntax arrowExpressionClauseSyntax
                when arrowExpressionClauseSyntax.Expression is ThrowExpressionSyntax throwExpressionSyntax:
                return throwExpressionSyntax;
            case ThrowExpressionSyntax directThrowExpressionSyntax:
                return directThrowExpressionSyntax;
            case MethodDeclarationSyntax methodDeclarationSyntax
                when methodDeclarationSyntax.ExpressionBody != null:
                return TryGetDirectThrowOnlySyntax(methodDeclarationSyntax.ExpressionBody);
            case MethodDeclarationSyntax methodDeclarationSyntax
                when methodDeclarationSyntax.Body != null:
                return TryGetDirectThrowOnlySyntax(methodDeclarationSyntax.Body);
            case LocalFunctionStatementSyntax localFunctionStatementSyntax
                when localFunctionStatementSyntax.ExpressionBody != null:
                return TryGetDirectThrowOnlySyntax(localFunctionStatementSyntax.ExpressionBody);
            case LocalFunctionStatementSyntax localFunctionStatementSyntax
                when localFunctionStatementSyntax.Body != null:
                return TryGetDirectThrowOnlySyntax(localFunctionStatementSyntax.Body);
            case SimpleLambdaExpressionSyntax simpleLambdaExpressionSyntax:
                return TryGetDirectThrowOnlySyntax(simpleLambdaExpressionSyntax.Body);
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpressionSyntax:
                return TryGetDirectThrowOnlySyntax(parenthesizedLambdaExpressionSyntax.Body);
            case AnonymousMethodExpressionSyntax anonymousMethodExpressionSyntax
                when anonymousMethodExpressionSyntax.Block != null:
                return TryGetDirectThrowOnlySyntax(anonymousMethodExpressionSyntax.Block);
            default:
                return null;
        }
    }
}
