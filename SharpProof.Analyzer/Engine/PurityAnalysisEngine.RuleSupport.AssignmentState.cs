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
        private static PurityAnalysisState UpdateDelegateMapForOperation(IOperation op, Rules.PurityAnalysisContext context, PurityAnalysisState currentState)
        {

            PurityAnalysisState nextState = currentState;
            var operationToTrack = op is IExpressionStatementOperation expressionStatementOperation
                ? expressionStatementOperation.Operation
                : op;


            if (operationToTrack is ICompoundAssignmentOperation compoundAssignmentOperation)
            {
                var targetOperation = compoundAssignmentOperation.Target;
                var valueOperation = compoundAssignmentOperation.Value;
                var targetSymbol = TryResolveTrackedSymbol(targetOperation, currentState);

                if (targetSymbol is ILocalSymbol compoundLocalSymbol)
                {
                    foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(compoundLocalSymbol, context))
                    {
                        nextState = nextState.WithIncrementedSmtSymbolVersion(writtenLocalSymbol);
                    }
                }
                else if (targetSymbol is IParameterSymbol compoundParameterSymbol)
                {
                    nextState = nextState.WithIncrementedSmtSymbolVersion(compoundParameterSymbol);
                }

                nextState = AddCallerVisibleMutationFact(
                    nextState,
                    targetOperation,
                    currentState,
                    operationToTrack.Syntax);

                if (targetSymbol != null && targetOperation.Type?.TypeKind == TypeKind.Delegate)
                {
                    if (compoundAssignmentOperation.OperatorKind == BinaryOperatorKind.Add)
                    {
                        PurityAnalysisEngine.PotentialTargets? valueTargets = ResolvePotentialTargets(valueOperation, currentState, context.CancellationToken);
                        if (valueTargets != null &&
                            currentState.DelegateTargetMap.TryGetValue(targetSymbol, out var currentTargets))
                        {
                            var mergedTargets = PotentialTargets.Merge(currentTargets, valueTargets.Value);
                            nextState = nextState.WithDelegateTarget(targetSymbol, mergedTargets);
                        }
                        else
                        {
                            nextState = nextState.WithDelegateTarget(targetSymbol, PotentialTargets.Unresolved);
                        }
                    }
                    else
                    {
                        nextState = nextState.WithDelegateTarget(targetSymbol, PotentialTargets.Unresolved);
                    }
                }
            }

            else if (operationToTrack is ICoalesceAssignmentOperation coalesceAssignmentOperation)
            {
                var targetOperation = coalesceAssignmentOperation.Target;
                var valueOperation = coalesceAssignmentOperation.Value;
                var targetSymbol = TryResolveTrackedSymbol(targetOperation, currentState);
                var writtenLocalSymbols = targetSymbol is ILocalSymbol targetLocalSymbol
                    ? EnumerateWrittenLocalSymbols(targetLocalSymbol, context).ToArray()
                    : Array.Empty<ILocalSymbol>();
                if (targetSymbol is IParameterSymbol coalesceParameterSymbol)
                {
                    nextState = nextState.WithIncrementedSmtSymbolVersion(coalesceParameterSymbol);
                }

                if (targetSymbol is ILocalSymbol coalesceLocalSymbol &&
                    currentState.IsDefinitelyNullLocalSymbol(coalesceLocalSymbol))
                {
                    nextState = ApplyWrittenLocalStateUpdates(
                        nextState,
                        writtenLocalSymbols,
                        valueOperation,
                        currentState,
                        context.SemanticModel,
                        context.SemanticModel.Compilation,
                        context.CancellationToken);
                    nextState = ApplyAssignedDelegateTargets(
                        nextState,
                        targetSymbol,
                        targetOperation.Type,
                        valueOperation,
                        writtenLocalSymbols,
                        currentState,
                        context.CancellationToken,
                        "[ATF-DEL-COALESCE]",
                        "coalesce-assigned value targets are unresolved");
                }
            }

            else if (operationToTrack is IDeconstructionAssignmentOperation deconstructionAssignmentOperation)
            {
                nextState = ApplyDeconstructionAssignmentStateUpdates(
                    nextState,
                    deconstructionAssignmentOperation,
                    currentState,
                    context);
            }

            else if (operationToTrack is IAssignmentOperation assignmentOperation)
            {
                var targetOperation = assignmentOperation.Target;
                var valueOperation = assignmentOperation.Value;
                var targetSymbol = TryResolveTrackedSymbol(targetOperation, currentState);
                var writtenLocalSymbols = targetSymbol is ILocalSymbol targetLocalSymbol
                    ? EnumerateWrittenLocalSymbols(targetLocalSymbol, context).ToArray()
                    : Array.Empty<ILocalSymbol>();
                if (targetSymbol is IParameterSymbol assignmentParameterSymbol)
                {
                    nextState = nextState.WithIncrementedSmtSymbolVersion(assignmentParameterSymbol);
                    nextState = AddAssignedValueFact(
                        nextState,
                        assignmentParameterSymbol,
                        valueOperation,
                        currentState,
                        context.SemanticModel,
                        context.CancellationToken);
                }

                nextState = ApplyWrittenLocalStateUpdates(
                    nextState,
                    writtenLocalSymbols,
                    valueOperation,
                    currentState,
                    context.SemanticModel,
                    context.SemanticModel.Compilation,
                    context.CancellationToken);
                nextState = AddCallerVisibleMutationFact(
                    nextState,
                    targetOperation,
                    currentState,
                    operationToTrack.Syntax);
                nextState = ApplyAssignedDelegateTargets(
                    nextState,
                    targetSymbol,
                    targetOperation.Type,
                    valueOperation,
                    writtenLocalSymbols,
                    currentState,
                    context.CancellationToken,
                    "[ATF-DEL-ASSIGN]",
                    "assigned value targets are unresolved");
            }

            else if (operationToTrack is IVariableDeclaratorOperation variableDeclaratorOperation &&
                     variableDeclaratorOperation.Initializer?.Value is { } variableInitializer)
            {
                nextState = AddDeclaredBorrowFact(
                    nextState,
                    variableDeclaratorOperation.Symbol,
                    variableInitializer,
                    context.SemanticModel,
                    context.CancellationToken);
            }

            else if (operationToTrack is IIncrementOrDecrementOperation incrementOrDecrementOperation)
            {
                nextState = AddCallerVisibleMutationFact(
                    nextState,
                    incrementOrDecrementOperation.Target,
                    currentState,
                    operationToTrack.Syntax);
            }

            else if (operationToTrack is IInvocationOperation invocationOperation)
            {
                nextState = AddDisposeInvocationFacts(nextState, invocationOperation, currentState);

                foreach (var argument in invocationOperation.Arguments)
                {
                    if (argument.Parameter?.RefKind is not (RefKind.Ref or RefKind.Out))
                    {
                        continue;
                    }

                    var writtenSymbol = TryResolveTrackedSymbol(SkipImplicitConversions(argument.Value), currentState);
                    if (writtenSymbol is ILocalSymbol localSymbol)
                    {
                        foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(localSymbol, context))
                        {
                            nextState = nextState
                                .WithoutLocalConcreteType(writtenLocalSymbol)
                                .WithoutOwnedLocalArray(writtenLocalSymbol)
                                .WithoutDefinitelyNullLocal(writtenLocalSymbol)
                                .WithIncrementedSmtSymbolVersion(writtenLocalSymbol);

                            if (writtenLocalSymbol.Type?.TypeKind == TypeKind.Delegate)
                            {
                                nextState = nextState.WithDelegateTarget(writtenLocalSymbol, PotentialTargets.Unresolved);
                            }
                        }
                    }
                    else if (writtenSymbol is IParameterSymbol parameterSymbol)
                    {
                        nextState = nextState.WithIncrementedSmtSymbolVersion(parameterSymbol);
                    }
                }
            }

            else if (operationToTrack is IReturnOperation returnOperation)
            {
                nextState = AddReturnedOwnedResourceFacts(nextState, returnOperation, currentState);
            }

            else if (operationToTrack is IUsingOperation usingOperation)
            {
                nextState = AddUsingStatementDisposeFacts(nextState, usingOperation, currentState);
            }

            else if (operationToTrack is IFlowCaptureOperation flowCaptureOperation)
            {
                if (TryResolveTrackedSymbol(flowCaptureOperation.Value, currentState) is ISymbol capturedSymbol)
                {
                    nextState = nextState.WithFlowCaptureSymbol(flowCaptureOperation.Id, capturedSymbol);
                }

                PurityAnalysisEngine.PotentialTargets? valueTargets = ResolvePotentialTargets(flowCaptureOperation.Value, currentState, context.CancellationToken);
                if (valueTargets != null)
                {
                    nextState = nextState.WithFlowCaptureTarget(flowCaptureOperation.Id, valueTargets.Value);
                }

                if (TryResolveKnownConcreteType(flowCaptureOperation.Value, currentState, context.SemanticModel.Compilation, out var concreteType))
                {
                    nextState = nextState.WithFlowCaptureConcreteType(flowCaptureOperation.Id, concreteType);
                }

                if (IsOwnedLocalArrayValue(flowCaptureOperation.Value, currentState, context.SemanticModel.Compilation))
                {
                    nextState = nextState.WithOwnedArrayFlowCapture(flowCaptureOperation.Id, flowCaptureOperation.Syntax);
                }
                else
                {
                    nextState = nextState.WithoutOwnedArrayFlowCapture(flowCaptureOperation.Id);
                }
            }

            else if (operationToTrack is IVariableDeclarationGroupOperation groupOperation)
            {
                foreach (var declaration in groupOperation.Declarations)
                {
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (declarator.Initializer != null)
                        {
                            var initializerValue = declarator.Initializer.Value;
                            ILocalSymbol declaredSymbol = declarator.Symbol;

                            if (TryResolveKnownConcreteType(initializerValue, nextState, context.SemanticModel.Compilation, out var concreteType))
                            {
                                nextState = nextState.WithLocalConcreteType(declaredSymbol, concreteType);
                            }
                            else
                            {
                                nextState = nextState.WithoutLocalConcreteType(declaredSymbol);
                            }

                            if (IsOwnedLocalArrayValue(initializerValue, nextState, context.SemanticModel.Compilation))
                            {
                                nextState = nextState.WithOwnedLocalArray(declaredSymbol);
                                nextState = AddOwnedLocalArrayFacts(
                                    nextState,
                                    declaredSymbol,
                                    initializerValue);
                            }
                            else
                            {
                                nextState = nextState.WithoutOwnedLocalArray(declaredSymbol);
                            }

                            nextState = AddFreshMutableObjectFacts(
                                nextState,
                                declaredSymbol,
                                initializerValue);

                            if (IsDefinitelyNullValue(initializerValue, nextState))
                            {
                                nextState = nextState.WithDefinitelyNullLocal(declaredSymbol);
                            }
                            else
                            {
                                nextState = nextState.WithoutDefinitelyNullLocal(declaredSymbol);
                            }

                            if (declaredSymbol.Type?.TypeKind == TypeKind.Delegate)
                            {
                                PurityAnalysisEngine.PotentialTargets? valueTargets = ResolvePotentialTargets(initializerValue, nextState, context.CancellationToken);
                                if (valueTargets != null)
                                {
                                    nextState = nextState.WithDelegateTarget(declaredSymbol, valueTargets.Value);
                                }
                            }

                            nextState = AddAssignedValueFact(
                                nextState,
                                declaredSymbol,
                                initializerValue,
                                nextState,
                                context.SemanticModel,
                                context.CancellationToken);
                            nextState = AddAssignedAliasFact(
                                nextState,
                                declaredSymbol,
                                initializerValue,
                                nextState);
                            nextState = AddDeclaredBorrowFact(
                                nextState,
                                declaredSymbol,
                                initializerValue,
                                context.SemanticModel,
                                context.CancellationToken);
                            if (!IsUsingResourceDeclarator(declarator))
                            {
                                nextState = AddOwnedDisposableLocalFacts(
                                    nextState,
                                    declaredSymbol,
                                    initializerValue,
                                    context.SemanticModel.Compilation);
                            }
                        }
                    }
                }
            }


            return nextState;
        }

        private static PurityAnalysisState ApplyDeconstructionAssignmentStateUpdates(
            PurityAnalysisState nextState,
            IDeconstructionAssignmentOperation deconstructionAssignmentOperation,
            PurityAnalysisState currentState,
            Rules.PurityAnalysisContext context)
        {
            foreach (var assignment in EnumerateDeconstructionAssignments(
                         deconstructionAssignmentOperation.Target,
                         deconstructionAssignmentOperation.Value))
            {
                var targetSymbol = TryResolveDeconstructionTargetSymbol(
                    assignment.Target,
                    currentState,
                    context.SemanticModel,
                    context.CancellationToken);
                if (targetSymbol is ILocalSymbol localSymbol)
                {
                    var writtenLocalSymbols = EnumerateWrittenLocalSymbols(localSymbol, context).ToArray();
                    nextState = ApplyWrittenLocalStateUpdates(
                        nextState,
                        writtenLocalSymbols,
                        assignment.Value,
                        currentState,
                        context.SemanticModel,
                        context.SemanticModel.Compilation,
                        context.CancellationToken);
                    nextState = ApplyAssignedDelegateTargets(
                        nextState,
                        targetSymbol,
                        assignment.Target.Type,
                        assignment.Value,
                        writtenLocalSymbols,
                        currentState,
                        context.CancellationToken,
                        "[ATF-DEL-DECONSTRUCT]",
                        "deconstructed value targets are unresolved");
                }
                else if (targetSymbol is IParameterSymbol parameterSymbol)
                {
                    nextState = nextState.WithIncrementedSmtSymbolVersion(parameterSymbol);
                    nextState = AddAssignedValueFact(
                        nextState,
                        parameterSymbol,
                        assignment.Value,
                        currentState,
                        context.SemanticModel,
                        context.CancellationToken);
                }

                nextState = AddCallerVisibleMutationFact(
                    nextState,
                    assignment.Target,
                    currentState,
                    deconstructionAssignmentOperation.Syntax);
            }

            return nextState;
        }

        private static IEnumerable<DeconstructionAssignmentElement> EnumerateDeconstructionAssignments(
            IOperation target,
            IOperation value)
        {
            target = SkipImplicitConversions(target) ?? target;
            value = SkipImplicitConversions(value) ?? value;
            if (target is ITupleOperation targetTuple &&
                value is ITupleOperation valueTuple)
            {
                var count = Math.Min(targetTuple.Elements.Length, valueTuple.Elements.Length);
                for (var i = 0; i < count; i++)
                {
                    foreach (var nested in EnumerateDeconstructionAssignments(
                                 targetTuple.Elements[i],
                                 valueTuple.Elements[i]))
                    {
                        yield return nested;
                    }
                }

                yield break;
            }

            yield return new DeconstructionAssignmentElement(target, value);
        }

        private static ISymbol? TryResolveDeconstructionTargetSymbol(
            IOperation targetOperation,
            PurityAnalysisState currentState,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            targetOperation = SkipImplicitConversions(targetOperation) ?? targetOperation;
            if (TryResolveTrackedSymbol(targetOperation, currentState) is { } trackedSymbol)
            {
                return trackedSymbol;
            }

            if (targetOperation is IDeclarationExpressionOperation declarationExpression)
            {
                if (TryResolveTrackedSymbol(declarationExpression.Expression, currentState) is { } declaredTrackedSymbol)
                {
                    return declaredTrackedSymbol;
                }

                if (declarationExpression.Syntax is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation } &&
                    semanticModel.GetDeclaredSymbol(designation, cancellationToken) is { } declaredSymbol)
                {
                    return declaredSymbol;
                }
            }

            if (targetOperation.Syntax is SingleVariableDesignationSyntax singleVariable &&
                semanticModel.GetDeclaredSymbol(singleVariable, cancellationToken) is { } singleVariableSymbol)
            {
                return singleVariableSymbol;
            }

            return targetOperation.Syntax is IdentifierNameSyntax identifier
                ? semanticModel.GetSymbolInfo(identifier, cancellationToken).Symbol
                : null;
        }

    }
}
