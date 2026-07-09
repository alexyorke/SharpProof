using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Globalization;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;
using System.Threading;

namespace SharpProof.Analyzer.Engine
{

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
            CompilationPurityService? purityService,
            CancellationToken cancellationToken,
            out ImmutableDictionary<ISymbol, PotentialTargets> mergedDelegateTargetsFromBlocks,
            out ImmutableHashSet<CaptureId> mergedOwnedArrayFlowCapturesFromBlocks,
            out ImmutableHashSet<ISymbol> mergedOwnedLocalArraysFromBlocks,
            out ImmutableDictionary<ISymbol, INamedTypeSymbol> mergedLocalConcreteTypesFromBlocks,
            out SymbolicState mergedPathStateFromBlocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            mergedDelegateTargetsFromBlocks = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
            mergedOwnedArrayFlowCapturesFromBlocks = ImmutableHashSet<CaptureId>.Empty;
            mergedOwnedLocalArraysFromBlocks = ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
            mergedLocalConcreteTypesFromBlocks = ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            mergedPathStateFromBlocks = new SymbolicState();
            // Roslyn 4.x: Create(BlockSyntax|ArrowClause, model) throws ("operation has a non-null parent").
            // Create(BaseMethodDeclarationSyntax|LocalFunctionStatement|ConstructorDeclaration|... , model) is the supported root.
            ControlFlowGraph? cfg = null;
            try
            {
                cfg = ControlFlowGraph.Create(bodyNode, semanticModel);
                LogDebug($"CFG created successfully for node: {bodyNode.Kind()}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDebug($"Error creating ControlFlowGraph for {containingMethodSymbol.ToDisplayString()}: {ex.Message}. Assuming impure.");
                return PurityAnalysisResult.Impure(bodyNode);
            }

            if (cfg == null || cfg.Blocks.IsEmpty)
            {
                LogDebug($"CFG is null or empty for {containingMethodSymbol.ToDisplayString()}. Assuming pure (no operations).");
                return PurityAnalysisResult.Pure;
            }


            LogDebug($"  [CFG] Created CFG with {cfg.Blocks.Length} blocks for {containingMethodSymbol.ToDisplayString()}.");


            var blockStates = new Dictionary<BasicBlock, PurityAnalysisState>(cfg.Blocks.Length);
            var exitBlockStates = new Dictionary<BasicBlock, PurityAnalysisState>(cfg.Blocks.Length);
            var worklist = new Queue<BasicBlock>();
            var inQueue = new HashSet<BasicBlock>();

            if (cfg.Blocks.Any())
            {
                var entryBlock = cfg.Blocks.First();

                LogDebug($"  [CFG] Adding Entry Block #{entryBlock.Ordinal} to worklist.");
                blockStates[entryBlock] = PurityAnalysisState.Pure;
                worklist.Enqueue(entryBlock);
                inQueue.Add(entryBlock);
            }
            else
            {
                LogDebug("  [CFG] CFG has no blocks. Exiting analysis.");
                return PurityAnalysisResult.Pure;
            }


            LogDebug("  [CFG] Starting CFG dataflow analysis worklist loop.");
            int loopIterations = 0;

            LogDebug($"  [CFG] BEFORE WHILE CHECK: worklist.Count = {worklist.Count}, loopIterations = {loopIterations}");
            while (worklist.Count > 0 && loopIterations < cfg.Blocks.Length * 50)
            {

                LogDebug("  [CFG] ENTERED WHILE LOOP.");
                loopIterations++;

                LogDebug($"  [CFG] Worklist count: {worklist.Count}. Iteration: {loopIterations}");
                var currentBlock = worklist.Dequeue();
                inQueue.Remove(currentBlock);
                LogDebug($"  [CFG] Processing CFG Block #{currentBlock.Ordinal}");

                if (!blockStates.TryGetValue(currentBlock, out var stateBefore))
                {
                    stateBefore = PurityAnalysisState.Pure;
                    blockStates[currentBlock] = stateBefore;
                }

                LogDebug($"  [CFG] StateBefore for Block #{currentBlock.Ordinal}: Impure={stateBefore.HasPotentialImpurity}");


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
                    purityService,
                    cancellationToken);

                exitBlockStates[currentBlock] = stateAfter;
                LogDebug($"  [CFG] State after Block #{currentBlock.Ordinal}: Impure={stateAfter.HasPotentialImpurity}");



                LogDebug($"  [CFG] Propagating stateAfter (Impure={stateAfter.HasPotentialImpurity}) to successors of Block #{currentBlock.Ordinal}.");
                if (TryGetConstantBranchDecision(currentBlock.BranchValue, semanticModel, smtAnalysis, cancellationToken, out var takeConditionalSuccessor))
                {
                    var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock.BranchValue);
                    var takenSuccessor = takeConditionalSuccessor
                        ? (trueUsesConditionalSuccessor
                            ? currentBlock.ConditionalSuccessor?.Destination
                            : currentBlock.FallThroughSuccessor?.Destination)
                        : (trueUsesConditionalSuccessor
                            ? currentBlock.FallThroughSuccessor?.Destination
                            : currentBlock.ConditionalSuccessor?.Destination);
                    if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel, takeConditionalSuccessor, smtAnalysis, cancellationToken, out var takenState))
                    {
                        PropagateToSuccessor(takenSuccessor, takenState, blockStates, worklist, inQueue);
                    }
                }
                else
                {
                    var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock.BranchValue);

                    if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel, trueUsesConditionalSuccessor, smtAnalysis, cancellationToken, out var conditionalState))
                    {
                        PropagateToSuccessor(currentBlock.ConditionalSuccessor?.Destination, conditionalState, blockStates, worklist, inQueue);
                    }

                    if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel, !trueUsesConditionalSuccessor, smtAnalysis, cancellationToken, out var fallThroughState))
                    {
                        PropagateToSuccessor(currentBlock.FallThroughSuccessor?.Destination, fallThroughState, blockStates, worklist, inQueue);
                    }
                }

            }

            if (worklist.Count == 0)
            {
                LogDebug("  [CFG] Finished CFG dataflow analysis worklist loop (worklist empty).");
            }
            else
            {
                LogDebug($"  [CFG] WARNING: Exited CFG dataflow loop due to iteration limit ({loopIterations}). Potential incomplete merge; continuing with aggregated block states.");
            }

            mergedDelegateTargetsFromBlocks = MergeDelegateTargetMapsFromBlockStates(exitBlockStates.Values);
            mergedOwnedArrayFlowCapturesFromBlocks = MergeOwnedArrayFlowCapturesFromBlockStates(exitBlockStates.Values);
            mergedOwnedLocalArraysFromBlocks = MergeOwnedLocalArraySymbolsFromBlockStates(exitBlockStates.Values);
            mergedLocalConcreteTypesFromBlocks = MergeLocalConcreteTypesFromBlockStates(exitBlockStates.Values);
            mergedPathStateFromBlocks = MergePathStatesAcrossAll(exitBlockStates.Values.ToArray());

            PurityAnalysisResult finalResult = PurityAnalysisResult.Pure;

            foreach (var exitState in exitBlockStates.Values)
            {
                if (exitState.HasPotentialImpurity)
                {
                    finalResult = exitState.FirstImpureSyntaxNode != null
                        ? PurityAnalysisResult.Impure(exitState.FirstImpureSyntaxNode, exitState.FirstImpurityEvidence)
                        : PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(exitState.FirstImpurityEvidence);
                    LogDebug($"  [CFG] Final Result: IMPURE. Node={finalResult.ImpureSyntaxNode?.Kind()}");
                    return finalResult;
                }
            }

            LogDebug($"  [CFG] Final Result: PURE.");
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
            CompilationPurityService? purityService,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LogDebug($"ApplyTransferFunction START for Block #{block.Ordinal} - Initial State: Impure={stateBefore.HasPotentialImpurity}");

            if (stateBefore.HasPotentialImpurity)
            {
                LogDebug($"ApplyTransferFunction SKIP for Block #{block.Ordinal} - Already impure.");
                return stateBefore;
            }

            var blockSourceNode = block.Operations.FirstOrDefault()?.Syntax ?? block.BranchValue?.Syntax;
            if (stateBefore.PathConditions.Length > 0 &&
                ArePathConditionsUnsatisfiable(stateBefore, stateBefore.PathConditions, smtAnalysis, blockSourceNode))
            {
                LogDebug($"ApplyTransferFunction SKIP for Block #{block.Ordinal} - SMT path conditions are unsatisfiable.");
                return stateBefore;
            }


            var pureAttributeSymbol_block = semanticModel.Compilation.GetTypeByMetadataName("SharpProof.Attributes.PureAttribute");
            var ruleContext = new Rules.PurityAnalysisContext(
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
                smtAnalysis);


            var currentStateInBlock = stateBefore;
            PurityAnalysisResult? deferredRecursiveImpurity = null;
            SyntaxNode? deferredRecursiveSyntax = null;
            foreach (var op in block.Operations)
            {
                if (op == null) continue;

                LogDebug($"    [ATF Block {block.Ordinal}] Checking Op Kind: {op.Kind}, Syntax: {op.Syntax.ToString().Replace("\r\n", " ").Replace("\n", " ")}");

                if (op is IFlowCaptureOperation flowCap)
                {
                    var valResult = CheckSingleOperation(flowCap.Value, ruleContext, currentStateInBlock);
                    currentStateInBlock = currentStateInBlock.WithFlowCaptureResult(flowCap.Id, valResult);
                    if (!valResult.IsPure)
                    {
                        if (IsImpurityProvenUnreachable(valResult, semanticModel, smtAnalysis, cancellationToken))
                        {
                            continue;
                        }

                        LogDebug($"ApplyTransferFunction IMPURE FlowCapture value in Block #{block.Ordinal}");
                        currentStateInBlock = currentStateInBlock.WithImpurity(valResult, flowCap.Syntax);
                        break;
                    }

                    currentStateInBlock = UpdateDelegateMapForOperation(flowCap, ruleContext, currentStateInBlock);
                    continue;
                }

                var opResult = CheckSingleOperation(op, ruleContext, currentStateInBlock);

                if (!opResult.IsPure)
                {
                    if (IsImpurityProvenUnreachable(opResult, semanticModel, smtAnalysis, cancellationToken))
                    {
                        continue;
                    }

                    LogDebug($"ApplyTransferFunction IMPURE DETECTED in Block #{block.Ordinal} by Op: {op.Kind} ({op.Syntax})");

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


                LogDebug($"  [ApplyTF] Before UpdateDelegateMapForOperation: StateImpure={currentStateInBlock.HasPotentialImpurity}, MapCount={currentStateInBlock.DelegateTargetMap.Count}");
                currentStateInBlock = UpdateDelegateMapForOperation(op, ruleContext, currentStateInBlock);
                LogDebug($"  [ApplyTF] After UpdateDelegateMapForOperation: StateImpure={currentStateInBlock.HasPotentialImpurity}, MapCount={currentStateInBlock.DelegateTargetMap.Count}");

            }

            if (!currentStateInBlock.HasPotentialImpurity && deferredRecursiveImpurity.HasValue)
            {
                var fallbackSyntax = deferredRecursiveSyntax ??
                    block.Operations.FirstOrDefault()?.Syntax ??
                    containingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);

                currentStateInBlock = currentStateInBlock.WithImpurity(
                    deferredRecursiveImpurity.Value,
                    fallbackSyntax!);
            }

            if (!currentStateInBlock.HasPotentialImpurity &&
                block.BranchValue != null &&
                TryCreateThrowBranchImpurity(block.BranchValue, ruleContext, currentStateInBlock, out var throwBranchResult))
            {
                currentStateInBlock = currentStateInBlock.WithImpurity(throwBranchResult, throwBranchResult.ImpureSyntaxNode ?? block.BranchValue.Syntax);
            }
            else if (!currentStateInBlock.HasPotentialImpurity &&
                block.BranchValue != null &&
                ShouldAnalyzeStateSensitiveBranchValue(block.BranchValue.Syntax))
            {
                LogDebug($"    [ATF Block {block.Ordinal}] Checking Branch Value Kind: {block.BranchValue.Kind}, Syntax: {block.BranchValue.Syntax.ToString().Replace("\r\n", " ").Replace("\n", " ")}");

                var branchValueResult = CheckSingleOperation(block.BranchValue, ruleContext, currentStateInBlock);
                if (!branchValueResult.IsPure)
                {
                    if (!IsImpurityProvenUnreachable(branchValueResult, semanticModel, smtAnalysis, cancellationToken))
                    {
                        LogDebug($"ApplyTransferFunction IMPURE DETECTED in Block #{block.Ordinal} by Branch Value: {block.BranchValue.Kind} ({block.BranchValue.Syntax})");
                        currentStateInBlock = currentStateInBlock.WithImpurity(branchValueResult, block.BranchValue.Syntax);
                    }
                }
                else
                {
                    currentStateInBlock = UpdateDelegateMapForOperation(block.BranchValue, ruleContext, currentStateInBlock);
                }
            }

            LogDebug($"ApplyTransferFunction END for Block #{block.Ordinal} - Final State: Impure={currentStateInBlock.HasPotentialImpurity}");
            return currentStateInBlock;
        }

        private static bool TryCreateThrowBranchImpurity(
            IOperation branchValue,
            Rules.PurityAnalysisContext context,
            PurityAnalysisState currentState,
            out PurityAnalysisResult result)
        {
            result = PurityAnalysisResult.Pure;

            var throwSyntax = branchValue.Syntax.FirstAncestorOrSelf<ThrowStatementSyntax>() ??
                (SyntaxNode?)branchValue.Syntax.FirstAncestorOrSelf<ThrowExpressionSyntax>();
            if (throwSyntax == null)
            {
                return false;
            }

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
                    ruleName: "ThrowOperationPurityRule",
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
            CompilationPurityService? purityService,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pureAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName("SharpProof.Attributes.PureAttribute");
            var context = new Rules.PurityAnalysisContext(
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
                smtAnalysis);

            var currentState = PurityAnalysisState.Pure;
            var visitedOperations = new HashSet<IOperation>();
            foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
            {
                var operationToAnalyze = operation is IExpressionStatementOperation expressionStatementOperation
                    ? expressionStatementOperation.Operation
                    : operation;
                if (!visitedOperations.Add(operationToAnalyze))
                {
                    continue;
                }

                if (operation is IFlowCaptureOperation flowCaptureOperation)
                {
                    var valueResult = CheckSingleOperation(flowCaptureOperation.Value, context, currentState);
                    currentState = currentState.WithFlowCaptureResult(flowCaptureOperation.Id, valueResult);
                    if (!valueResult.IsPure)
                    {
                        return valueResult;
                    }

                    currentState = UpdateDelegateMapForOperation(flowCaptureOperation, context, currentState);
                    continue;
                }

                var operationResult = CheckSingleOperation(operationToAnalyze, context, currentState);
                if (!operationResult.IsPure)
                {
                    return operationResult;
                }

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
                case Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax blockSyntax
                    when blockSyntax.Statements.Count == 1:
                    return TryGetDirectThrowOnlySyntax(blockSyntax.Statements[0]);
                case Microsoft.CodeAnalysis.CSharp.Syntax.ThrowStatementSyntax throwStatementSyntax:
                    return throwStatementSyntax;
                case Microsoft.CodeAnalysis.CSharp.Syntax.ArrowExpressionClauseSyntax arrowExpressionClauseSyntax
                    when arrowExpressionClauseSyntax.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.ThrowExpressionSyntax throwExpressionSyntax:
                    return throwExpressionSyntax;
                case Microsoft.CodeAnalysis.CSharp.Syntax.ThrowExpressionSyntax directThrowExpressionSyntax:
                    return directThrowExpressionSyntax;
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.ExpressionBody != null:
                    return TryGetDirectThrowOnlySyntax(methodDeclarationSyntax.ExpressionBody);
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.Body != null:
                    return TryGetDirectThrowOnlySyntax(methodDeclarationSyntax.Body);
                case Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.ExpressionBody != null:
                    return TryGetDirectThrowOnlySyntax(localFunctionStatementSyntax.ExpressionBody);
                case Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.Body != null:
                    return TryGetDirectThrowOnlySyntax(localFunctionStatementSyntax.Body);
                case Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax simpleLambdaExpressionSyntax:
                    return TryGetDirectThrowOnlySyntax(simpleLambdaExpressionSyntax.Body);
                case Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpressionSyntax:
                    return TryGetDirectThrowOnlySyntax(parenthesizedLambdaExpressionSyntax.Body);
                case Microsoft.CodeAnalysis.CSharp.Syntax.AnonymousMethodExpressionSyntax anonymousMethodExpressionSyntax
                    when anonymousMethodExpressionSyntax.Block != null:
                    return TryGetDirectThrowOnlySyntax(anonymousMethodExpressionSyntax.Block);
                default:
                    return null;
            }
        }
    }
}
