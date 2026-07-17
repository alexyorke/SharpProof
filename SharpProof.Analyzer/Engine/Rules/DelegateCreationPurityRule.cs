using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine.Rules;

internal class DelegateCreationPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.DelegateCreation);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (!(operation is IDelegateCreationOperation delegateCreation))
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);


        var targetClassification = DelegateTargetClassifier.Classify(delegateCreation.Target);
        var target = targetClassification.Operation;

        if (!IsEscapingDelegateCreation(delegateCreation))
        {
            if (target is IMethodReferenceOperation nonEscapingMethodReference)
            {
                if (nonEscapingMethodReference.Instance != null)
                {
                    var instanceResult =
                        PurityAnalysisEngine.CheckSingleOperation(nonEscapingMethodReference.Instance, context,
                            currentState);
                    if (!instanceResult.IsPure) return instanceResult;
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (targetClassification.Kind == DelegateTargetKind.AnonymousFunction)
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;

            var targetResult = PurityAnalysisEngine.CheckSingleOperation(target, context, currentState);
            if (!targetResult.IsPure) return targetResult;

            return targetClassification.Kind == DelegateTargetKind.ExistingDelegate
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    target.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unresolved_delegate_target",
                        nameof(DelegateCreationPurityRule),
                        delegateCreation,
                        target.Syntax));
        }

        if (target is IAnonymousFunctionOperation anonymousFunction)
            return CheckEscapingAnonymousFunction(
                delegateCreation, anonymousFunction, anonymousFunction.Symbol, context, currentState);

        if (target is IFlowAnonymousFunctionOperation flowAnonymousFunction)
            return CheckEscapingAnonymousFunction(
                delegateCreation, flowAnonymousFunction, flowAnonymousFunction.Symbol, context, currentState);

        if (target is IMethodReferenceOperation methodReference)
        {
            var targetMethodSymbol = methodReference.Method;

            if (methodReference.Instance != null)
            {
                var instanceResult =
                    PurityAnalysisEngine.CheckSingleOperation(methodReference.Instance, context, currentState);
                if (!instanceResult.IsPure) return instanceResult;
            }

            var potentialTargets = PurityAnalysisEngine.ResolvePotentialTargets(
                delegateCreation,
                currentState,
                context.CancellationToken,
                context.SemanticModel);
            if (potentialTargets == null || potentialTargets.Value.IsUnresolved)
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    delegateCreation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unresolved_delegate_target",
                        nameof(DelegateCreationPurityRule),
                        delegateCreation,
                        symbol: targetMethodSymbol));

            foreach (var targetMethod in potentialTargets.Value.MethodSymbols)
            {
                var methodResult = PurityCalleeResolver.GetCalleePurityAtUse(targetMethod, delegateCreation.Syntax, context);
                if (!methodResult.IsPure) return methodResult;

                if (targetMethod.MethodKind != MethodKind.LocalFunction) continue;

                if (TryFindLocalFunctionCapturedLocalMutation(targetMethod, context, out var mutationSyntax,
                        out var mutatedLocal))
                    return CreateMutableStateEscapeResult(
                        delegateCreation, mutationSyntax, mutatedLocal, "escaping_closure_mutation");

                if (TryFindLocalFunctionCapturedOwnedLocalArray(targetMethod, context, currentState,
                        out var captureSyntax, out var capturedArrayLocal))
                    return CreateMutableStateEscapeResult(
                        delegateCreation, captureSyntax, capturedArrayLocal, "escaping_closure_owned_array_capture");

                if (TryFindLocalFunctionCapturedFreshMutableObject(
                        targetMethod,
                        currentState,
                        delegateCreation.Syntax,
                        context,
                        out var objectCaptureSyntax,
                        out var capturedObjectLocal))
                    return CreateMutableStateEscapeResult(
                        delegateCreation,
                        objectCaptureSyntax,
                        capturedObjectLocal,
                        "escaping_closure_fresh_mutable_object_capture");
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Impure(target.Syntax);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckEscapingAnonymousFunction(
        IDelegateCreationOperation delegateCreation,
        IOperation anonymousFunction,
        IMethodSymbol? lambdaSymbol,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (lambdaSymbol == null)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(anonymousFunction.Syntax);

        var bodyResult = PurityCalleeResolver.GetCalleePurityAtUse(lambdaSymbol, delegateCreation.Syntax, context);
        if (!bodyResult.IsPure) return bodyResult;

        if (TryFindCapturedLocalMutation(
                anonymousFunction,
                context.CancellationToken,
                out var mutationSyntax,
                out var mutatedLocal))
            return CreateMutableStateEscapeResult(
                delegateCreation, mutationSyntax, mutatedLocal, "escaping_closure_mutation");

        if (TryFindCapturedOwnedLocalArray(
                anonymousFunction,
                currentState,
                context.SemanticModel,
                context.CancellationToken,
                out var captureSyntax,
                out var capturedArrayLocal))
            return CreateMutableStateEscapeResult(
                delegateCreation, captureSyntax, capturedArrayLocal, "escaping_closure_owned_array_capture");

        if (TryFindCapturedFreshMutableObject(
                anonymousFunction,
                currentState,
                delegateCreation.Syntax,
                context.SemanticModel,
                context.CancellationToken,
                out var objectCaptureSyntax,
                out var capturedObjectLocal))
            return CreateMutableStateEscapeResult(
                delegateCreation,
                objectCaptureSyntax,
                capturedObjectLocal,
                "escaping_closure_fresh_mutable_object_capture");

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CreateMutableStateEscapeResult(
        IDelegateCreationOperation delegateCreation,
        SyntaxNode syntax,
        ISymbol symbol,
        string detail)
    {
        return PurityAnalysisEngine.PurityAnalysisResult.Impure(
            syntax,
            PurityAnalysisEngine.PurityEvidence.Create(
                "mutable_state_escape",
                nameof(DelegateCreationPurityRule),
                delegateCreation,
                syntax,
                symbol,
                detail));
    }

    private static bool IsEscapingDelegateCreation(IDelegateCreationOperation delegateCreation)
    {
        var parent = delegateCreation.Parent;
        while (parent is IConversionOperation or IFlowCaptureOperation) parent = parent.Parent;

        return parent is IReturnOperation ||
               parent is IArgumentOperation ||
               (parent is IAssignmentOperation assignment && IsNonLocalAssignmentTarget(assignment.Target)) ||
               (parent is IVariableInitializerOperation variableInitializer &&
                variableInitializer.Parent is IVariableDeclaratorOperation variableDeclarator &&
                variableDeclarator.Symbol is IFieldSymbol);
    }

    private static bool IsNonLocalAssignmentTarget(IOperation? targetOperation)
    {
        var unwrappedTarget = PurityAnalysisEngine.SkipImplicitConversions(targetOperation);
        return unwrappedTarget is IFieldReferenceOperation or IPropertyReferenceOperation;
    }

    private static bool TryFindCapturedLocalMutation(
        IOperation anonymousFunctionOperation,
        CancellationToken cancellationToken,
        out SyntaxNode mutationSyntax,
        out ILocalSymbol mutatedLocal)
    {
        var lambdaSpan = anonymousFunctionOperation.Syntax.Span;
        foreach (var operation in anonymousFunctionOperation.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (operation)
            {
                case IAssignmentOperation assignmentOperation
                    when TryGetMutatedCapturedLocal(assignmentOperation.Target, lambdaSpan, cancellationToken,
                        out mutatedLocal):
                    mutationSyntax = assignmentOperation.Target.Syntax;
                    return true;

                case ICompoundAssignmentOperation compoundAssignmentOperation
                    when TryGetMutatedCapturedLocal(compoundAssignmentOperation.Target, lambdaSpan, cancellationToken,
                        out mutatedLocal):
                    mutationSyntax = compoundAssignmentOperation.Target.Syntax;
                    return true;

                case IIncrementOrDecrementOperation incrementOrDecrementOperation
                    when TryGetMutatedCapturedLocal(incrementOrDecrementOperation.Target, lambdaSpan, cancellationToken,
                        out mutatedLocal):
                    mutationSyntax = incrementOrDecrementOperation.Target.Syntax;
                    return true;

                case IDeconstructionAssignmentOperation deconstructionAssignmentOperation
                    when TryGetMutatedCapturedLocal(deconstructionAssignmentOperation.Target, lambdaSpan,
                        cancellationToken, out mutatedLocal):
                    mutationSyntax = deconstructionAssignmentOperation.Target.Syntax;
                    return true;

                case IInvocationOperation invocationOperation:
                    foreach (var argument in invocationOperation.Arguments)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out &&
                            TryGetMutatedCapturedLocal(argument.Value, lambdaSpan, cancellationToken, out mutatedLocal))
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
        TextSpan lambdaSpan,
        CancellationToken cancellationToken,
        out ILocalSymbol localSymbol)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedTarget = PurityAnalysisEngine.SkipImplicitConversions(targetOperation);
        if (unwrappedTarget is ILocalReferenceOperation localReference &&
            IsDeclaredOutsideSpan(localReference.Local, lambdaSpan, cancellationToken))
        {
            localSymbol = localReference.Local;
            return true;
        }

        if (unwrappedTarget is ITupleOperation tupleOperation)
            foreach (var element in tupleOperation.Elements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryGetMutatedCapturedLocal(element, lambdaSpan, cancellationToken, out localSymbol)) return true;
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
                TryFindCapturedLocalMutation(operation, context.CancellationToken, out mutationSyntax,
                    out mutatedLocal))
                return true;
        }

        mutationSyntax = null!;
        mutatedLocal = null!;
        return false;
    }

    internal static bool TryFindCapturedOwnedLocalArray(
        IOperation anonymousFunctionOperation,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SyntaxNode captureSyntax,
        out ILocalSymbol capturedLocal)
    {
        return TryFindCapturedOwnedLocal(
            anonymousFunctionOperation, currentState, null, semanticModel, cancellationToken,
            freshMutableObject: false, out captureSyntax, out capturedLocal);
    }

    internal static bool TryFindLocalFunctionCapturedOwnedLocalArray(
        IMethodSymbol methodSymbol,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out SyntaxNode captureSyntax,
        out ILocalSymbol capturedLocal)
    {
        foreach (var (operation, semanticModel) in EnumerateLocalFunctionOperations(methodSymbol, context))
            if (TryFindCapturedOwnedLocalArray(
                    operation,
                    currentState,
                    semanticModel,
                    context.CancellationToken,
                    out captureSyntax,
                    out capturedLocal))
                return true;

        captureSyntax = null!;
        capturedLocal = null!;
        return false;
    }

    private static bool TryFindCapturedOwnedLocalBySyntax(
        SyntaxNode anonymousFunctionSyntax,
        TextSpan lambdaSpan,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool freshMutableObject,
        out SyntaxNode captureSyntax,
        out ILocalSymbol capturedLocal)
    {
        foreach (var identifierName in anonymousFunctionSyntax.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is ILocalSymbol localSymbol &&
                IsDeclaredOutsideSpan(localSymbol, lambdaSpan, cancellationToken) &&
                (freshMutableObject
                    ? RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(localSymbol.Type) &&
                      PuritySymbolicStateFacts.HasSymbolicOwnedFactForSymbol(localSymbol, currentState)
                    : localSymbol.Type is IArrayTypeSymbol &&
                      (PuritySymbolicStateFacts.HasSymbolicOwnedFactForSymbol(localSymbol, currentState) ||
                       currentState.IsOwnedLocalArraySymbol(localSymbol))))
            {
                captureSyntax = identifierName;
                capturedLocal = localSymbol;
                return true;
            }

            if (!freshMutableObject) continue;

            foreach (var fact in currentState.PathState.Facts)
                if (fact.Polarity &&
                    fact.Confidence == SymbolicFactConfidence.Exact &&
                    fact.Atom is SymbolicOwnershipAtom { Escaped: false } &&
                    fact.Symbol is ILocalSymbol factLocal &&
                    identifierName.Identifier.ValueText == factLocal.Name &&
                    IsDeclaredOutsideSpan(factLocal, lambdaSpan, cancellationToken) &&
                    RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(factLocal.Type))
                {
                    captureSyntax = identifierName;
                    capturedLocal = factLocal;
                    return true;
                }
        }

        captureSyntax = null!;
        capturedLocal = null!;
        return false;
    }

    private static bool TryGetCapturedOwnedLocalArray(
        IOperation? operation,
        TextSpan lambdaSpan,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        CancellationToken cancellationToken,
        out ILocalSymbol localSymbol)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        if (unwrappedOperation is ILocalReferenceOperation localReference &&
            localReference.Local.Type is IArrayTypeSymbol &&
            (PuritySymbolicStateFacts.HasSymbolicOwnedFactForSymbol(localReference.Local, currentState) ||
             currentState.IsOwnedLocalArraySymbol(localReference.Local)) &&
            IsDeclaredOutsideSpan(localReference.Local, lambdaSpan, cancellationToken))
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
        CancellationToken cancellationToken,
        out SyntaxNode captureSyntax,
        out ILocalSymbol capturedLocal)
    {
        return TryFindCapturedOwnedLocal(
            anonymousFunctionOperation, currentState, delegateCreationSyntax, semanticModel, cancellationToken,
            freshMutableObject: true, out captureSyntax, out capturedLocal);
    }

    private static bool TryFindCapturedOwnedLocal(
        IOperation anonymousFunctionOperation, PurityAnalysisEngine.PurityAnalysisState currentState,
        SyntaxNode? delegateCreationSyntax, SemanticModel semanticModel, CancellationToken cancellationToken,
        bool freshMutableObject, out SyntaxNode captureSyntax, out ILocalSymbol capturedLocal)
    {
        var lambdaSpan = anonymousFunctionOperation.Syntax.Span;
        foreach (var operation in anonymousFunctionOperation.DescendantsAndSelf())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var captured = freshMutableObject
                ? TryGetCapturedFreshMutableObject(
                    operation, lambdaSpan, currentState, delegateCreationSyntax!, semanticModel, cancellationToken,
                    out capturedLocal)
                : TryGetCapturedOwnedLocalArray(
                    operation, lambdaSpan, currentState, cancellationToken, out capturedLocal);
            if (captured)
            {
                captureSyntax = operation.Syntax;
                return true;
            }
        }

        return TryFindCapturedOwnedLocalBySyntax(
            anonymousFunctionOperation.Syntax,
            lambdaSpan,
            currentState,
            semanticModel,
            cancellationToken,
            freshMutableObject,
            out captureSyntax,
            out capturedLocal);
    }

    internal static bool TryFindLocalFunctionCapturedFreshMutableObject(
        IMethodSymbol methodSymbol,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        SyntaxNode delegateCreationSyntax,
        PurityAnalysisContext context,
        out SyntaxNode captureSyntax,
        out ILocalSymbol capturedLocal)
    {
        foreach (var (operation, semanticModel) in EnumerateLocalFunctionOperations(methodSymbol, context))
            if (TryFindCapturedFreshMutableObject(
                    operation,
                    currentState,
                    delegateCreationSyntax,
                    semanticModel,
                    context.CancellationToken,
                    out captureSyntax,
                    out capturedLocal))
                return true;

        captureSyntax = null!;
        capturedLocal = null!;
        return false;
    }

    private static IEnumerable<(IOperation Operation, SemanticModel SemanticModel)> EnumerateLocalFunctionOperations(
        IMethodSymbol methodSymbol, PurityAnalysisContext context)
    {
        foreach (var syntaxReference in methodSymbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax(context.CancellationToken);
            var semanticModel = context.SemanticModel.Compilation.GetSemanticModel(syntax.SyntaxTree);
            if (semanticModel.GetOperation(syntax, context.CancellationToken) is { } operation)
                yield return (operation, semanticModel);
        }
    }

    private static bool TryGetCapturedFreshMutableObject(
        IOperation? operation,
        TextSpan lambdaSpan,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        SyntaxNode delegateCreationSyntax,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ILocalSymbol localSymbol)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        if (unwrappedOperation is IFieldReferenceOperation fieldReference &&
            TryGetCapturedFreshMutableObject(
                fieldReference.Instance,
                lambdaSpan,
                currentState,
                delegateCreationSyntax,
                semanticModel,
                cancellationToken,
                out localSymbol))
            return true;

        if (unwrappedOperation is IPropertyReferenceOperation propertyReference &&
            TryGetCapturedFreshMutableObject(
                propertyReference.Instance,
                lambdaSpan,
                currentState,
                delegateCreationSyntax,
                semanticModel,
                cancellationToken,
                out localSymbol))
            return true;

        if (PurityAnalysisEngine.TryResolveTrackedSymbol(unwrappedOperation,
                currentState) is ILocalSymbol resolvedLocal &&
            IsDeclaredOutsideSpan(resolvedLocal, lambdaSpan, cancellationToken) &&
            RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(resolvedLocal.Type) &&
            PuritySymbolicStateFacts.HasSymbolicOwnedFactForSymbol(resolvedLocal, currentState))
        {
            localSymbol = resolvedLocal;
            return true;
        }

        if (unwrappedOperation is ILocalReferenceOperation localReferenceFallback &&
            IsDeclaredOutsideSpan(localReferenceFallback.Local, lambdaSpan, cancellationToken) &&
            HasStableFreshMutableObjectInitializer(localReferenceFallback.Local, delegateCreationSyntax, semanticModel,
                cancellationToken))
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
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return HasStableFreshMutableObjectInitializer(
            localSymbol,
            delegateCreationSyntax,
            semanticModel,
            new HashSet<ILocalSymbol>(SymbolEq.Default),
            cancellationToken);
    }

    private static bool HasStableFreshMutableObjectInitializer(
        ILocalSymbol localSymbol,
        SyntaxNode delegateCreationSyntax,
        SemanticModel semanticModel,
        HashSet<ILocalSymbol> visitedLocals,
        CancellationToken cancellationToken)
    {
        if (!RuleAnalysisHelper.TryGetStableLocalInitializer(
                localSymbol,
                delegateCreationSyntax,
                semanticModel,
                visitedLocals,
                cancellationToken,
                out var initializerSyntax,
                out var initializerOperation))
            return false;

        if (initializerOperation is IObjectCreationOperation objectCreationOperation)
            return RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type);

        return initializerOperation is ILocalReferenceOperation aliasReference &&
               IsDeclaredOutsideSpan(aliasReference.Local, delegateCreationSyntax.Span, cancellationToken) &&
               RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(aliasReference.Local.Type) &&
               HasStableFreshMutableObjectInitializer(
                   aliasReference.Local,
                   delegateCreationSyntax,
                   semanticModel,
                   visitedLocals,
                   cancellationToken);
    }

    private static bool IsDeclaredOutsideSpan(ILocalSymbol localSymbol, TextSpan span,
        CancellationToken cancellationToken)
    {
        var syntaxReferences = localSymbol.DeclaringSyntaxReferences;
        return syntaxReferences.Length > 0 &&
               syntaxReferences
                   .Select(reference => reference.GetSyntax(cancellationToken).Span)
                   .All(declarationSpan => declarationSpan.Start < span.Start || declarationSpan.End > span.End);
    }
}
