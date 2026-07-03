using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using PurelySharp.Analyzer.Engine;
using PurelySharp.Symbolic.Ir;

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

                    if (bodyResult.IsPure &&
                        IsEscapingDelegateCreation(delegateCreation) &&
                        TryFindCapturedOwnedLocalArray(
                            anonymousFunction,
                            currentState,
                            context.SemanticModel,
                            out var captureSyntax,
                            out var capturedArrayLocal))
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Escaping lambda captures owned local array '{capturedArrayLocal.Name}'. Treating as impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            captureSyntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_escape",
                                nameof(DelegateCreationPurityRule),
                                delegateCreation,
                                syntaxNode: captureSyntax,
                                symbol: capturedArrayLocal,
                                catalogSource: "escaping_closure_owned_array_capture"));
                    }

                    if (bodyResult.IsPure &&
                        IsEscapingDelegateCreation(delegateCreation) &&
                        TryFindCapturedFreshMutableObject(
                            anonymousFunction,
                            currentState,
                            delegateCreation.Syntax,
                            context.SemanticModel,
                            out var objectCaptureSyntax,
                            out var capturedObjectLocal))
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Escaping lambda captures fresh mutable local '{capturedObjectLocal.Name}'. Treating as impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            objectCaptureSyntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_escape",
                                nameof(DelegateCreationPurityRule),
                                delegateCreation,
                                syntaxNode: objectCaptureSyntax,
                                symbol: capturedObjectLocal,
                                catalogSource: "escaping_closure_fresh_mutable_object_capture"));
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

                    if (bodyResult.IsPure &&
                        IsEscapingDelegateCreation(delegateCreation) &&
                        TryFindCapturedOwnedLocalArray(
                            flowAnonymousFunction,
                            currentState,
                            context.SemanticModel,
                            out var captureSyntax,
                            out var capturedArrayLocal))
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Escaping flow lambda captures owned local array '{capturedArrayLocal.Name}'. Treating as impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            captureSyntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_escape",
                                nameof(DelegateCreationPurityRule),
                                delegateCreation,
                                syntaxNode: captureSyntax,
                                symbol: capturedArrayLocal,
                                catalogSource: "escaping_closure_owned_array_capture"));
                    }

                    if (bodyResult.IsPure &&
                        IsEscapingDelegateCreation(delegateCreation) &&
                        TryFindCapturedFreshMutableObject(
                            flowAnonymousFunction,
                            currentState,
                            delegateCreation.Syntax,
                            context.SemanticModel,
                            out var objectCaptureSyntax,
                            out var capturedObjectLocal))
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Escaping flow lambda captures fresh mutable local '{capturedObjectLocal.Name}'. Treating as impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            objectCaptureSyntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_escape",
                                nameof(DelegateCreationPurityRule),
                                delegateCreation,
                                syntaxNode: objectCaptureSyntax,
                                symbol: capturedObjectLocal,
                                catalogSource: "escaping_closure_fresh_mutable_object_capture"));
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

                    if (IsEscapingDelegateCreation(delegateCreation) &&
                        targetMethod.MethodKind == MethodKind.LocalFunction &&
                        TryFindLocalFunctionCapturedOwnedLocalArray(targetMethod, context, currentState, out var captureSyntax, out var capturedArrayLocal))
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Escaping local function delegate captures owned local array '{capturedArrayLocal.Name}'. Treating as impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            captureSyntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_escape",
                                nameof(DelegateCreationPurityRule),
                                delegateCreation,
                                syntaxNode: captureSyntax,
                                symbol: capturedArrayLocal,
                                catalogSource: "escaping_closure_owned_array_capture"));
                    }

                    if (IsEscapingDelegateCreation(delegateCreation) &&
                        targetMethod.MethodKind == MethodKind.LocalFunction &&
                            TryFindLocalFunctionCapturedFreshMutableObject(
                                targetMethod,
                                currentState,
                                delegateCreation.Syntax,
                                context,
                            out var objectCaptureSyntax,
                            out var capturedObjectLocal))
                    {
                        PurityAnalysisEngine.LogDebug($"    [DelegateCreationRule] Escaping local function delegate captures fresh mutable local '{capturedObjectLocal.Name}'. Treating as impure.");
                        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            objectCaptureSyntax,
                            PurityAnalysisEngine.PurityEvidence.Create(
                                "mutable_state_escape",
                                nameof(DelegateCreationPurityRule),
                                delegateCreation,
                                syntaxNode: objectCaptureSyntax,
                                symbol: capturedObjectLocal,
                                catalogSource: "escaping_closure_fresh_mutable_object_capture"));
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

        internal static bool TryFindCapturedOwnedLocalArray(
            IOperation anonymousFunctionOperation,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SemanticModel semanticModel,
            out SyntaxNode captureSyntax,
            out ILocalSymbol capturedLocal)
        {
            var lambdaSpan = anonymousFunctionOperation.Syntax.Span;
            foreach (var operation in anonymousFunctionOperation.DescendantsAndSelf())
            {
                if (TryGetCapturedOwnedLocalArray(operation, lambdaSpan, currentState, out capturedLocal))
                {
                    captureSyntax = operation.Syntax;
                    return true;
                }
            }

            if (TryFindCapturedOwnedLocalArrayBySyntax(
                    anonymousFunctionOperation.Syntax,
                    lambdaSpan,
                    currentState,
                    semanticModel,
                    out captureSyntax,
                    out capturedLocal))
            {
                return true;
            }

            captureSyntax = null!;
            capturedLocal = null!;
            return false;
        }

        internal static bool TryFindLocalFunctionCapturedOwnedLocalArray(
            IMethodSymbol methodSymbol,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out SyntaxNode captureSyntax,
            out ILocalSymbol capturedLocal)
        {
            foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax(context.CancellationToken);
                var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(syntax.SyntaxTree);
                var operation = semanticModel.GetOperation(syntax, context.CancellationToken);
                if (operation != null &&
                    TryFindCapturedOwnedLocalArray(
                        operation,
                        currentState,
                        semanticModel,
                        out captureSyntax,
                        out capturedLocal))
                {
                    return true;
                }
            }

            captureSyntax = null!;
            capturedLocal = null!;
            return false;
        }

        private static bool TryFindCapturedOwnedLocalArrayBySyntax(
            SyntaxNode anonymousFunctionSyntax,
            Microsoft.CodeAnalysis.Text.TextSpan lambdaSpan,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SemanticModel semanticModel,
            out SyntaxNode captureSyntax,
            out ILocalSymbol capturedLocal)
        {
            foreach (var identifierName in anonymousFunctionSyntax.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol localSymbol &&
                    localSymbol.Type is IArrayTypeSymbol &&
                    IsDeclaredOutsideSpan(localSymbol, lambdaSpan) &&
                    (PurityAnalysisEngine.HasSymbolicOwnedFactForSymbol(localSymbol, currentState) ||
                     currentState.IsOwnedLocalArraySymbol(localSymbol)))
                {
                    captureSyntax = identifierName;
                    capturedLocal = localSymbol;
                    return true;
                }
            }

            captureSyntax = null!;
            capturedLocal = null!;
            return false;
        }

        private static bool TryGetCapturedOwnedLocalArray(
            IOperation? operation,
            Microsoft.CodeAnalysis.Text.TextSpan lambdaSpan,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            out ILocalSymbol localSymbol)
        {
            var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
            if (unwrappedOperation is ILocalReferenceOperation localReference &&
                localReference.Local.Type is IArrayTypeSymbol &&
                (PurityAnalysisEngine.HasSymbolicOwnedFactForSymbol(localReference.Local, currentState) ||
                 currentState.IsOwnedLocalArraySymbol(localReference.Local)) &&
                IsDeclaredOutsideSpan(localReference.Local, lambdaSpan))
            {
                localSymbol = localReference.Local;
                return true;
            }

            localSymbol = null!;
            return false;
        }

        internal static bool TryFindCapturedFreshMutableObject(
            IOperation anonymousFunctionOperation,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SyntaxNode delegateCreationSyntax,
            SemanticModel semanticModel,
            out SyntaxNode captureSyntax,
            out ILocalSymbol capturedLocal)
        {
            var lambdaSpan = anonymousFunctionOperation.Syntax.Span;
            foreach (var operation in anonymousFunctionOperation.DescendantsAndSelf())
            {
                if (TryGetCapturedFreshMutableObject(
                    operation,
                    lambdaSpan,
                    currentState,
                    delegateCreationSyntax,
                    semanticModel,
                    out capturedLocal))
                {
                    captureSyntax = operation.Syntax;
                    return true;
                }
            }

            if (TryFindCapturedFreshMutableObjectBySyntax(
                    anonymousFunctionOperation.Syntax,
                    lambdaSpan,
                    currentState,
                    semanticModel,
                    out captureSyntax,
                    out capturedLocal))
            {
                return true;
            }

            captureSyntax = null!;
            capturedLocal = null!;
            return false;
        }

        internal static bool TryFindLocalFunctionCapturedFreshMutableObject(
            IMethodSymbol methodSymbol,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SyntaxNode delegateCreationSyntax,
            PurityAnalysisContext context,
            out SyntaxNode captureSyntax,
            out ILocalSymbol capturedLocal)
        {
            foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax(context.CancellationToken);
                var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(syntax.SyntaxTree);
                var operation = semanticModel.GetOperation(syntax, context.CancellationToken);
                if (operation != null &&
                    TryFindCapturedFreshMutableObject(
                        operation,
                        currentState,
                        delegateCreationSyntax,
                        semanticModel,
                        out captureSyntax,
                        out capturedLocal))
                {
                    return true;
                }
            }

            captureSyntax = null!;
            capturedLocal = null!;
            return false;
        }

        private static bool TryFindCapturedFreshMutableObjectBySyntax(
            SyntaxNode anonymousFunctionSyntax,
            Microsoft.CodeAnalysis.Text.TextSpan lambdaSpan,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SemanticModel semanticModel,
            out SyntaxNode captureSyntax,
            out ILocalSymbol capturedLocal)
        {
            foreach (var identifierName in anonymousFunctionSyntax.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol localSymbol &&
                    IsDeclaredOutsideSpan(localSymbol, lambdaSpan) &&
                    RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(localSymbol.Type) &&
                    PurityAnalysisEngine.HasSymbolicOwnedFactForSymbol(localSymbol, currentState))
                {
                    captureSyntax = identifierName;
                    capturedLocal = localSymbol;
                    return true;
                }

                foreach (var fact in currentState.PathState.Facts)
                {
                    if (fact.Polarity &&
                        fact.Confidence == SymbolicFactConfidence.Exact &&
                        fact.Atom is SymbolicOwnershipAtom { Escaped: false } &&
                        fact.Symbol is ILocalSymbol factLocal &&
                        identifierName.Identifier.ValueText == factLocal.Name &&
                        IsDeclaredOutsideSpan(factLocal, lambdaSpan) &&
                        RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(factLocal.Type))
                    {
                        captureSyntax = identifierName;
                        capturedLocal = factLocal;
                        return true;
                    }
                }
            }

            captureSyntax = null!;
            capturedLocal = null!;
            return false;
        }

        private static bool TryGetCapturedFreshMutableObject(
            IOperation? operation,
            Microsoft.CodeAnalysis.Text.TextSpan lambdaSpan,
            PurityAnalysisEngine.PurityAnalysisState currentState,
            SyntaxNode delegateCreationSyntax,
            SemanticModel semanticModel,
            out ILocalSymbol localSymbol)
        {
            var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
            if (unwrappedOperation is IFieldReferenceOperation fieldReference &&
                TryGetCapturedFreshMutableObject(
                    fieldReference.Instance,
                    lambdaSpan,
                    currentState,
                    delegateCreationSyntax,
                    semanticModel,
                    out localSymbol))
            {
                return true;
            }

            if (unwrappedOperation is IPropertyReferenceOperation propertyReference &&
                TryGetCapturedFreshMutableObject(
                    propertyReference.Instance,
                    lambdaSpan,
                    currentState,
                    delegateCreationSyntax,
                    semanticModel,
                    out localSymbol))
            {
                return true;
            }

            if (PurityAnalysisEngine.TryResolveTrackedSymbol(unwrappedOperation, currentState) is ILocalSymbol resolvedLocal &&
                IsDeclaredOutsideSpan(resolvedLocal, lambdaSpan) &&
                RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(resolvedLocal.Type) &&
                PurityAnalysisEngine.HasSymbolicOwnedFactForSymbol(resolvedLocal, currentState))
            {
                localSymbol = resolvedLocal;
                return true;
            }

            if (unwrappedOperation is ILocalReferenceOperation localReferenceFallback &&
                IsDeclaredOutsideSpan(localReferenceFallback.Local, lambdaSpan) &&
                HasStableFreshMutableObjectInitializer(localReferenceFallback.Local, delegateCreationSyntax, semanticModel))
            {
                localSymbol = localReferenceFallback.Local;
                return true;
            }

            localSymbol = null!;
            return false;
        }

        private static bool HasStableFreshMutableObjectInitializer(
            ILocalSymbol localSymbol,
            SyntaxNode delegateCreationSyntax,
            SemanticModel semanticModel)
        {
            return HasStableFreshMutableObjectInitializer(
                localSymbol,
                delegateCreationSyntax,
                semanticModel,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default));
        }

        private static bool HasStableFreshMutableObjectInitializer(
            ILocalSymbol localSymbol,
            SyntaxNode delegateCreationSyntax,
            SemanticModel semanticModel,
            HashSet<ILocalSymbol> visitedLocals)
        {
            var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            var initializerSyntax = declaratorSyntax?.Initializer?.Value;
            if (declaratorSyntax == null ||
                initializerSyntax == null ||
                !visitedLocals.Add(localSymbol))
            {
                return false;
            }

            if (HasAssignmentToLocalBetweenDeclarationAndEscape(localSymbol, delegateCreationSyntax, declaratorSyntax, semanticModel))
            {
                return false;
            }

            var initializerOperation = PurityAnalysisEngine.SkipImplicitConversions(semanticModel.GetOperation(initializerSyntax));
            if (initializerOperation is IObjectCreationOperation objectCreationOperation)
            {
                return IsFreshMutableEscapingReferenceType(objectCreationOperation.Type);
            }

            return initializerOperation is ILocalReferenceOperation aliasReference &&
                   IsDeclaredOutsideSpan(aliasReference.Local, delegateCreationSyntax.Span) &&
                   RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(aliasReference.Local.Type) &&
                   HasStableFreshMutableObjectInitializer(
                       aliasReference.Local,
                       delegateCreationSyntax,
                       semanticModel,
                       visitedLocals);
        }

        private static bool HasAssignmentToLocalBetweenDeclarationAndEscape(
            ILocalSymbol localSymbol,
            SyntaxNode delegateCreationSyntax,
            VariableDeclaratorSyntax declaratorSyntax,
            SemanticModel semanticModel)
        {
            var containingBlock = delegateCreationSyntax.FirstAncestorOrSelf<BlockSyntax>();
            if (containingBlock == null)
            {
                return false;
            }

            var start = declaratorSyntax.Span.End;
            var end = delegateCreationSyntax.SpanStart;
            if (end <= start)
            {
                return false;
            }

            foreach (var assignment in containingBlock.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.SpanStart < start || assignment.SpanStart >= end)
                {
                    continue;
                }

                var assignedSymbol = semanticModel.GetSymbolInfo(assignment.Left).Symbol;
                if (SymbolEqualityComparer.Default.Equals(assignedSymbol, localSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFreshMutableEscapingReferenceType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType ||
                namedType.TypeKind == TypeKind.Delegate ||
                namedType.IsValueType ||
                namedType.SpecialType == SpecialType.System_String ||
                namedType.DeclaringSyntaxReferences.Length == 0)
            {
                return false;
            }

            return namedType.GetMembers().Any(member =>
                member switch
                {
                    IFieldSymbol field => !field.IsStatic &&
                                          !field.IsReadOnly &&
                                          field.DeclaredAccessibility != Accessibility.Private,
                    IPropertySymbol property => !property.IsStatic &&
                                                property.SetMethod != null &&
                                                !property.SetMethod.IsInitOnly &&
                                                property.SetMethod.DeclaredAccessibility != Accessibility.Private,
                    _ => false
                });
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
