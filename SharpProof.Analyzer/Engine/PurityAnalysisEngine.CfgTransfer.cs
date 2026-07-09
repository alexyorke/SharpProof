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

        internal static PurityAnalysisResult CheckSingleOperation(IOperation operation, Rules.PurityAnalysisContext context, PurityAnalysisState currentState)
        {
            LogDebug($"    [CSO] Enter CheckSingleOperation for Kind: {operation.Kind}, Syntax: '{operation.Syntax.ToString().Trim()}'");
            LogDebug($"    [CSO] Current DFA State: Impure={currentState.HasPotentialImpurity}, MapCount={currentState.DelegateTargetMap.Count}");

            if (currentState.PathConditions.Length > 0 &&
                ArePathConditionsUnsatisfiable(currentState, currentState.PathConditions, context.SmtAnalysis, operation.Syntax))
            {
                LogDebug($"    [CSO] Current SMT path conditions are unsatisfiable. Treating as Pure: {operation.Syntax}");
                return PurityAnalysisResult.Pure;
            }

            if (currentState.PathConditions.Length > 0 &&
                ExecutionVisibility.IsEvaluationPathUnsatisfiableUsingSmt(
                    operation.Syntax,
                    context.SemanticModel,
                    context.CancellationToken,
                    currentState.PathConditions,
                    currentState.GetSmtSymbolVersion,
                    context.SmtAnalysis))
            {
                LogDebug($"    [CSO] Operation evaluation path is SMT-unreachable in current state. Treating as Pure: {operation.Syntax}");
                return PurityAnalysisResult.Pure;
            }

            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                    operation.Syntax,
                    context.SemanticModel,
                    context.CancellationToken,
                    context.SmtAnalysis))
            {
                LogDebug($"    [CSO] Operation is in a statically unreachable branch. Treating as Pure: {operation.Syntax}");
                return PurityAnalysisResult.Pure;
            }

            if (operation is IFlowCaptureReferenceOperation flowRef)
            {
                if (currentState.FlowCaptures.TryGetValue(flowRef.Id, out var capturedPurity))
                {
                    LogDebug($"    [CSO] FlowCaptureReference resolved from CFG state: IsPure={capturedPurity.IsPure}");
                    return capturedPurity;
                }

                LogDebug($"    [CSO] FlowCaptureReference without CFG capture entry. Treating as Pure.");
                return PurityAnalysisResult.Pure;
            }

            if (operation is IFlowCaptureOperation flowCap)
            {
                LogDebug($"    [CSO] FlowCapture: analyzing captured value subtree");
                return CheckSingleOperation(flowCap.Value, context, currentState);
            }


            bool isChecked = false;
            IMethodSymbol? operatorMethod = null;

            if (operation is IBinaryOperation binaryOp && binaryOp.IsChecked)
            {
                LogDebug($"    [CSO] Found Checked Binary Operation: {operation.Syntax}");
                isChecked = true;
                operatorMethod = binaryOp.OperatorMethod;


                var leftResult = CheckSingleOperation(binaryOp.LeftOperand, context, currentState);
                if (!leftResult.IsPure)
                {
                    LogDebug($"    [CSO] Left operand of checked operation is Impure: {binaryOp.LeftOperand.Syntax}");
                    return leftResult;
                }

                var rightResult = CheckSingleOperation(binaryOp.RightOperand, context, currentState);
                if (!rightResult.IsPure)
                {
                    LogDebug($"    [CSO] Right operand of checked operation is Impure: {binaryOp.RightOperand.Syntax}");
                    return rightResult;
                }
            }
            else if (operation is IUnaryOperation unaryOp && unaryOp.IsChecked)
            {
                LogDebug($"    [CSO] Found Checked Unary Operation: {operation.Syntax}");
                isChecked = true;
                operatorMethod = unaryOp.OperatorMethod;


                var operandResult = CheckSingleOperation(unaryOp.Operand, context, currentState);
                if (!operandResult.IsPure)
                {
                    LogDebug($"    [CSO] Operand of checked operation is Impure: {unaryOp.Operand.Syntax}");
                    return operandResult;
                }
            }

            if (isChecked)
            {
                LogDebug($"    [CSO] Processing checked operation: {operation.Syntax}");


                if (operatorMethod != null)
                {

                    if (context.PurityCache.TryGetValue(operatorMethod.OriginalDefinition, out var cachedResult))
                    {
                        if (!cachedResult.IsPure)
                        {
                            LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is IMPURE (cached). Operation is Impure.");
                            return PurityAnalysisResult.Impure(operation.Syntax);
                        }
                        LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is Pure (cached).");
                        return PurityAnalysisResult.Pure;
                    }


                    var hasTrustedGeneratedPurity = TryGetTrustedGeneratedPurityCoverage(
                        operatorMethod,
                        context.SemanticModel.Compilation,
                        out var generatedPurity);

                    if (hasTrustedGeneratedPurity)
                    {
                        if (generatedPurity.IsPure)
                        {
                            LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is trusted pure from generated purity summary.");
                            return PurityAnalysisResult.Pure;
                        }

                        if (!generatedPurity.IsPure)
                        {
                            LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is trusted impure from generated purity summary.");
                            return PurityAnalysisResult.Impure(
                                operation.Syntax,
                                PurityEvidence.Create(
                                        generatedPurity.PrimaryCategory,
                                        syntaxNode: operation.Syntax,
                                        symbol: operatorMethod.OriginalDefinition,
                                        catalogSource: "generated_purity_summary"));
                        }
                    }

                    if (!hasTrustedGeneratedPurity && IsKnownPureBCLMember(operatorMethod, context.SemanticModel.Compilation))
                    {
                        LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is known pure BCL member.");
                        return PurityAnalysisResult.Pure;
                    }

                    if (IsKnownImpure(operatorMethod))
                    {
                        LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is known impure. Operation is Impure.");
                        return PurityAnalysisResult.Impure(operation.Syntax);
                    }


                    var operatorPurity = GetCalleePurity(operatorMethod, context);

                    if (!operatorPurity.IsPure)
                    {
                        LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is IMPURE. Operation is Impure.");
                        return PurityAnalysisResult.Impure(operation.Syntax);
                    }

                    LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is Pure.");
                }

                LogDebug($"    [CSO] Checked operation is Pure.");
                return PurityAnalysisResult.Pure;
            }


            if (operation.Kind == OperationKind.InterpolatedStringText ||
                operation.Kind == OperationKind.Interpolation)
            {
                LogDebug($"    [CSO] {operation.Kind} is parent-handled by InterpolatedStringPurityRule.");
                return PurityAnalysisResult.Pure;
            }

            if (operation.Kind == OperationKind.Discard)
            {
                LogDebug("    [CSO] Discard is parent-handled by assignment, deconstruction, or argument analysis.");
                return PurityAnalysisResult.Pure;
            }

            _firstRuleByOperationKind.TryGetValue(operation.Kind, out var applicableRule);

            if (applicableRule != null)
            {

                LogDebug($"    [CSO] Applying Rule '{applicableRule.GetType().Name}' to Kind: {operation.Kind}, Syntax: '{operation.Syntax.ToString().Trim()}'");

                var ruleResult = applicableRule.CheckPurity(operation, context, currentState);

                LogDebug($"    [CSO] Rule '{applicableRule.GetType().Name}' Result: IsPure={ruleResult.IsPure}");
                if (!ruleResult.IsPure)
                {

                    if (ruleResult.ImpureSyntaxNode == null)
                    {
                        LogDebug($"    [CSO] Rule '{applicableRule.GetType().Name}' returned impure result without syntax node. Using current operation syntax: {operation.Syntax}");

                        return operation.Syntax != null
                               ? PurityAnalysisResult.Impure(operation.Syntax)
                               : PurityAnalysisResult.ImpureUnknownLocation;
                    }
                    LogDebug($"    [CSO] Exit CheckSingleOperation (Impure from rule)");
                    return ruleResult;
                }

                LogDebug($"    [CSO] Exit CheckSingleOperation (Pure from rule)");
                return PurityAnalysisResult.Pure;
            }
            else
            {

                LogDebug($"    [CSO] No rule found for operation kind {operation.Kind}. Defaulting to impure. Syntax: '{operation.Syntax.ToString().Trim()}'");
                LogDebug($"    [CSO] Exit CheckSingleOperation (Impure default)");
                return ImpureResult(operation.Syntax, CreateUnsupportedOperationEvidence(operation));
            }
        }






        private static void PropagateToSuccessor(
            BasicBlock? successor,
            PurityAnalysisState newState,
            Dictionary<BasicBlock, PurityAnalysisState> blockStates,
            Queue<BasicBlock> worklist,
            HashSet<BasicBlock> inQueue)
        {
            if (successor == null) return;


            bool previouslyVisited = blockStates.TryGetValue(successor, out var existingState);
            if (!previouslyVisited)
            {
                existingState = PurityAnalysisState.Pure;
            }


            var mergedState = previouslyVisited ? MergeStates(existingState, newState) : newState;


            bool stateChanged = !previouslyVisited || !mergedState.Equals(existingState);











            if (stateChanged)
            {
                LogDebug($"PropagateToSuccessor: State changed for Block #{successor.Ordinal} from Impure={existingState.HasPotentialImpurity} to Impure={mergedState.HasPotentialImpurity}. Updating state.");
                blockStates[successor] = mergedState;
            }
            else
            {

                if (!previouslyVisited)
                {
                    blockStates[successor] = mergedState;
                }

                LogDebug($"PropagateToSuccessor: State unchanged for Block #{successor.Ordinal} (Impure={existingState.HasPotentialImpurity}).");
            }



            if (stateChanged || !inQueue.Contains(successor))
            {
                if (!inQueue.Contains(successor))
                {
                    LogDebug($"PropagateToSuccessor: Enqueuing Block #{successor.Ordinal} (State Changed: {stateChanged}).");
                    worklist.Enqueue(successor);
                    inQueue.Add(successor);
                }
                else
                {


                    if (stateChanged)
                    {
                        LogDebug($"PropagateToSuccessor: Block #{successor.Ordinal} already in queue, state changed. Will reprocess.");
                    }
                    else
                    {
                        LogDebug($"PropagateToSuccessor: Block #{successor.Ordinal} already in queue, state unchanged.");
                    }
                }
            }
            else
            {
                LogDebug($"PropagateToSuccessor: Block #{successor.Ordinal} already in queue and state unchanged. No enqueue needed.");
            }
        }
    }
}
