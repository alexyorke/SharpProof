using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Analyzer.Engine.Rules
{

    internal class DelegateCreationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.DelegateCreation);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is IDelegateCreationOperation delegateCreation))
            {

                PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Unexpected operation kind {operation.Kind}. Assuming impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);
            }


            IOperation target = delegateCreation.Target;

            if (!IsEscapingDelegateCreation(delegateCreation))
            {
                if (target is IMethodReferenceOperation nonEscapingMethodReference &&
                    nonEscapingMethodReference.Instance != null)
                {
                    var instanceResult = PurityAnalysisEngine.CheckSingleOperation(nonEscapingMethodReference.Instance, context, currentState);
                    if (!instanceResult.IsPure)
                    {
                        return instanceResult;
                    }
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (target is IAnonymousFunctionOperation anonymousFunction)
            {
                PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Found AnonymousFunctionOperation. Analyzing its body.");


                IMethodSymbol lambdaSymbol = anonymousFunction.Symbol;
                if (lambdaSymbol != null)
                {
                    PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Recursively checking lambda: {lambdaSymbol.ToDisplayString()}");

                    var bodyResult = PurityAnalysisEngine.GetCalleePurity(lambdaSymbol, context);

                    PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Lambda body analysis result: IsPure={bodyResult.IsPure}");
                    if (bodyResult.IsPure &&
                        IsEscapingDelegateCreation(delegateCreation) &&
                        TryFindCapturedLocalMutation(anonymousFunction, out var mutationSyntax, out var mutatedLocal))
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Escaping lambda mutates captured local '{mutatedLocal.Name}'. Treating as impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            mutationSyntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_escape",
                                nameof(DelegateCreationPurityRule),
                                delegateCreation,
                                syntaxNode: mutationSyntax,
                                symbol: mutatedLocal,
                                catalogSource: "escaping_closure_mutation"));
                    }

                    return bodyResult.IsPure
                        ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                        : bodyResult.WithCallee(lambdaSymbol, delegateCreation.Syntax);
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Could not get symbol for anonymous function. Assuming impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(anonymousFunction.Syntax);
                }
            }
            else if (target is IFlowAnonymousFunctionOperation flowAnonymousFunction)
            {
                PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Found FlowAnonymousFunctionOperation. Analyzing its body.");

                IMethodSymbol lambdaSymbol = flowAnonymousFunction.Symbol;
                if (lambdaSymbol != null)
                {
                    PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Recursively checking flow lambda: {lambdaSymbol.ToDisplayString()}");

                    var bodyResult = PurityAnalysisEngine.GetCalleePurity(lambdaSymbol, context);

                    PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Flow lambda body analysis result: IsPure={bodyResult.IsPure}");
                    if (bodyResult.IsPure &&
                        IsEscapingDelegateCreation(delegateCreation) &&
                        TryFindCapturedLocalMutation(flowAnonymousFunction, out var mutationSyntax, out var mutatedLocal))
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Escaping flow lambda mutates captured local '{mutatedLocal.Name}'. Treating as impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            mutationSyntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_escape",
                                nameof(DelegateCreationPurityRule),
                                delegateCreation,
                                syntaxNode: mutationSyntax,
                                symbol: mutatedLocal,
                                catalogSource: "escaping_closure_mutation"));
                    }

                    return bodyResult.IsPure
                        ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                        : bodyResult.WithCallee(lambdaSymbol, delegateCreation.Syntax);
                }
                else
                {
                    PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Could not get symbol for flow anonymous function. Assuming impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(flowAnonymousFunction.Syntax);
                }
            }
            else if (target is IMethodReferenceOperation methodReference)
            {

                PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Found MethodReferenceOperation: {methodReference.Method.ToDisplayString()}. Analyzing target method.");
                IMethodSymbol targetMethodSymbol = methodReference.Method;

                if (methodReference.Instance != null)
                {
                    var instanceResult = PurityAnalysisEngine.CheckSingleOperation(methodReference.Instance, context, currentState);
                    if (!instanceResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Method group receiver is impure: {methodReference.Instance.Syntax}");
                        return instanceResult;
                    }
                }

                var potentialTargets = PurityAnalysisEngine.ResolvePotentialTargets(delegateCreation, currentState, context.SemanticModel);
                if (potentialTargets == null || potentialTargets.Value.IsUnresolved)
                {
                    PurityAnalysisEngine.LogDebug("    [DelegateCreationRule] Delegate target could dispatch to unresolved runtime target. Assuming impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        delegateCreation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unresolved_delegate_target",
                            nameof(DelegateCreationPurityRule),
                            delegateCreation,
                            symbol: targetMethodSymbol));
                }

                foreach (var targetMethod in potentialTargets.Value.MethodSymbols)
                {
                    var methodResult = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);

                    PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Referenced method analysis result for {targetMethod.ToDisplayString()}: IsPure={methodResult.IsPure}");
                    if (!methodResult.IsPure)
                    {
                        return methodResult.WithCallee(targetMethod, delegateCreation.Syntax);
                    }

                    if (IsEscapingDelegateCreation(delegateCreation) &&
                        targetMethod.MethodKind == MethodKind.LocalFunction &&
                        TryFindLocalFunctionCapturedLocalMutation(targetMethod, context, out var mutationSyntax, out var mutatedLocal))
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Escaping local function delegate mutates captured local '{mutatedLocal.Name}'. Treating as impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            mutationSyntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_escape",
                                nameof(DelegateCreationPurityRule),
                                delegateCreation,
                                syntaxNode: mutationSyntax,
                                symbol: mutatedLocal,
                                catalogSource: "escaping_closure_mutation"));
                    }
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }
            else
            {

                PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Unexpected DelegateCreation target kind: {target.Kind}. Assuming impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(target.Syntax);
            }
        }

        private static bool IsEscapingDelegateCreation(IDelegateCreationOperation delegateCreation)
        {
            IOperation? parent = delegateCreation.Parent;
            while (parent is IConversionOperation or IFlowCaptureOperation)
            {
                parent = parent.Parent;
            }

            return parent is IReturnOperation ||
                parent is IArgumentOperation ||
                parent is IAssignmentOperation assignment && IsNonLocalAssignmentTarget(assignment.Target) ||
                parent is IVariableInitializerOperation variableInitializer &&
                variableInitializer.Parent is IVariableDeclaratorOperation variableDeclarator &&
                variableDeclarator.Symbol is IFieldSymbol;
        }

        private static bool IsNonLocalAssignmentTarget(IOperation? targetOperation)
        {
            var unwrappedTarget = PurityAnalysisEngine.SkipImplicitConversions(targetOperation);
            return unwrappedTarget is IFieldReferenceOperation or IPropertyReferenceOperation;
        }

        private static bool TryFindCapturedLocalMutation(
            IOperation anonymousFunctionOperation,
            out SyntaxNode mutationSyntax,
            out ILocalSymbol mutatedLocal)
        {
            var lambdaSpan = anonymousFunctionOperation.Syntax.Span;
            foreach (var operation in anonymousFunctionOperation.DescendantsAndSelf())
            {
                switch (operation)
                {
                    case IAssignmentOperation assignmentOperation
                        when TryGetMutatedCapturedLocal(assignmentOperation.Target, lambdaSpan, out mutatedLocal):
                        mutationSyntax = assignmentOperation.Target.Syntax;
                        return true;

                    case ICompoundAssignmentOperation compoundAssignmentOperation
                        when TryGetMutatedCapturedLocal(compoundAssignmentOperation.Target, lambdaSpan, out mutatedLocal):
                        mutationSyntax = compoundAssignmentOperation.Target.Syntax;
                        return true;

                    case IIncrementOrDecrementOperation incrementOrDecrementOperation
                        when TryGetMutatedCapturedLocal(incrementOrDecrementOperation.Target, lambdaSpan, out mutatedLocal):
                        mutationSyntax = incrementOrDecrementOperation.Target.Syntax;
                        return true;

                    case IDeconstructionAssignmentOperation deconstructionAssignmentOperation
                        when TryGetMutatedCapturedLocal(deconstructionAssignmentOperation.Target, lambdaSpan, out mutatedLocal):
                        mutationSyntax = deconstructionAssignmentOperation.Target.Syntax;
                        return true;

                    case IInvocationOperation invocationOperation:
                        foreach (var argument in invocationOperation.Arguments)
                        {
                            if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                                TryGetMutatedCapturedLocal(argument.Value, lambdaSpan, out mutatedLocal))
                            {
                                mutationSyntax = argument.Value.Syntax;
                                return true;
                            }
                        }

                        break;
                }
            }

            mutationSyntax = null!;
            mutatedLocal = null!;
            return false;
        }

        private static bool TryGetMutatedCapturedLocal(
            IOperation? targetOperation,
            Microsoft.CodeAnalysis.Text.TextSpan lambdaSpan,
            out ILocalSymbol localSymbol)
        {
            var unwrappedTarget = PurityAnalysisEngine.SkipImplicitConversions(targetOperation);
            if (unwrappedTarget is ILocalReferenceOperation localReference &&
                IsDeclaredOutsideSpan(localReference.Local, lambdaSpan))
            {
                localSymbol = localReference.Local;
                return true;
            }

            if (unwrappedTarget is ITupleOperation tupleOperation)
            {
                foreach (var element in tupleOperation.Elements)
                {
                    if (TryGetMutatedCapturedLocal(element, lambdaSpan, out localSymbol))
                    {
                        return true;
                    }
                }
            }

            localSymbol = null!;
            return false;
        }

        private static bool TryFindLocalFunctionCapturedLocalMutation(
            IMethodSymbol methodSymbol,
            PurityAnalysisContext context,
            out SyntaxNode mutationSyntax,
            out ILocalSymbol mutatedLocal)
        {
            foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax(context.CancellationToken);
                var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(syntax.SyntaxTree);
                var operation = semanticModel.GetOperation(syntax, context.CancellationToken);
                if (operation != null &&
                    TryFindCapturedLocalMutation(operation, out mutationSyntax, out mutatedLocal))
                {
                    return true;
                }
            }

            mutationSyntax = null!;
            mutatedLocal = null!;
            return false;
        }

        private static bool IsDeclaredOutsideSpan(ILocalSymbol localSymbol, Microsoft.CodeAnalysis.Text.TextSpan span)
        {
            var syntaxReferences = localSymbol.DeclaringSyntaxReferences;
            return syntaxReferences.Length > 0 &&
                syntaxReferences
                    .Select(reference => reference.GetSyntax().Span)
                    .All(declarationSpan => declarationSpan.Start < span.Start || declarationSpan.End > span.End);
        }
    }
}
