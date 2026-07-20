namespace SharpProof.Analyzer.Engine.Rules;

internal class UsingStatementPurityRule {
    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState) {
        if (operation is not (IUsingOperation or IUsingDeclarationOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var resourceOperation = operation is IUsingOperation statement ? statement.Resources :
            ((IUsingDeclarationOperation)operation).DeclarationGroup;
        var isAwaitUsing = IsAwaitUsingOperation(operation);
        var disposalSyntax = operation.Syntax;

        var declaredLocals = FindDeclaredLocals(resourceOperation);

        foreach (var local in declaredLocals) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var disposeReceiverType = ResolveDisposeReceiverType(local, operation, context.SemanticModel, currentState,
                isAwaitUsing, context.CancellationToken);
            if (disposeReceiverType == null) continue;

            var disposeResult = CheckDisposeReceiver(
                operation, disposalSyntax, disposeReceiverType, isAwaitUsing,
                WasLocalReassignedBeforeUsing(
                    local, operation, context.SemanticModel, context.CancellationToken), context);
            if (!disposeResult.IsPure) return disposeResult;
        }

        return declaredLocals.Count == 0 && ResolveExpressionDisposeReceiverType(resourceOperation) is { } receiverType
            ? CheckDisposeReceiver(operation, disposalSyntax, receiverType, isAwaitUsing, false, context)
            : PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDisposeReceiver(
        IOperation operation,
        SyntaxNode syntax,
        ITypeSymbol receiverType,
        bool isAwaitUsing,
        bool isUnstable,
        PurityAnalysisContext context) {
        var disposeMethod = DisposalMemberClassifier.FindDisposalMethod(
            receiverType, context.SemanticModel.Compilation, isAwaitUsing);
        if (disposeMethod == null)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                syntax, PurityAnalysisEngine.PurityEvidence.Create(
                    "unknown_external_call", nameof(UsingStatementPurityRule), operation, syntax,
                    receiverType, "missing_disposal_member"));
        if (isUnstable && (receiverType.TypeKind == TypeKind.Interface || IsOverridableDispatchTarget(disposeMethod)))
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                syntax, PurityAnalysisEngine.PurityEvidence.Create(
                "unknown_external_call",
                nameof(UsingStatementPurityRule),
                operation,
                syntax,
                disposeMethod,
                "unstable_using_resource"));
        return CheckImplicitDisposeCallee(disposeMethod, syntax, context, isAwaitUsing);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckImplicitDisposeCallee(
        IMethodSymbol disposeMethod,
        SyntaxNode syntaxNode,
        PurityAnalysisContext context,
        bool isAwaitUsing) {
        var disposeResult = PurityCalleeResolver.GetCalleePurityAtUse(disposeMethod, syntaxNode, context);
        if (!disposeResult.IsPure) return disposeResult;

        return isAwaitUsing ? AwaitPurityRule.CheckAwaitablePatternMembers(disposeMethod.ReturnType, syntaxNode, context) :
            PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private List<ILocalSymbol> FindDeclaredLocals(IOperation? resourceOperation) {
        var locals = new List<ILocalSymbol>();
        if (resourceOperation is IVariableDeclarationGroupOperation declarationGroup)
            foreach (var declaration in declarationGroup.Declarations)
                foreach (var declarator in declaration.Declarators)
                    locals.Add(declarator.Symbol);
        else if (resourceOperation is IVariableDeclaratorOperation declaratorOperation)
            locals.Add(declaratorOperation.Symbol);

        var unwrappedResourceOperation = PurityAnalysisEngine.SkipImplicitConversions(resourceOperation);
        if (unwrappedResourceOperation is ILocalReferenceOperation localReferenceOperation)
            locals.Add(localReferenceOperation.Local);
        return locals;
    }

    private ITypeSymbol? ResolveDisposeReceiverType(ILocalSymbol local, IOperation usingOperation,
        SemanticModel semanticModel, PurityAnalysisEngine.PurityAnalysisState currentState, bool isAwaitUsing,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasDeclaratorInitializer(local, cancellationToken) &&
            currentState.TryGetLocalConcreteType(local, out var concreteType) &&
            DisposalMemberClassifier.FindDisposalMethod(concreteType, semanticModel.Compilation, isAwaitUsing) != null)
            return concreteType;

        var initializerType = TryGetStableObjectCreationInitializerType(
            local, usingOperation, semanticModel, cancellationToken);
        if (initializerType != null &&
            DisposalMemberClassifier.FindDisposalMethod(initializerType, semanticModel.Compilation, isAwaitUsing) != null)
            return initializerType;

        return local.Type;
    }

    private ITypeSymbol? ResolveExpressionDisposeReceiverType(IOperation? resourceOperation) {
        var unwrappedResource = UnwrapConversionsForDisposeReceiver(resourceOperation);
        return unwrappedResource is IObjectCreationOperation objectCreationOperation
            ? objectCreationOperation.Type
            : unwrappedResource?.Type ?? resourceOperation?.Type;
    }

    private IOperation? UnwrapConversionsForDisposeReceiver(IOperation? operation) {
        var current = PurityAnalysisEngine.SkipImplicitConversions(operation);
        while (current is IConversionOperation conversion) {
            var operand = PurityAnalysisEngine.SkipImplicitConversions(conversion.Operand);
            if (operand == null || ReferenceEquals(operand, current)) break;

            current = operand;
        }

        return current;
    }

    private ITypeSymbol? TryGetStableObjectCreationInitializerType(ILocalSymbol local, IOperation usingOperation,
        SemanticModel semanticModel, CancellationToken cancellationToken) {
        var declaratorSyntax = RuleAnalysisHelper.GetVariableDeclaratorSyntax(local, cancellationToken);
        var initializerSyntax = declaratorSyntax?.Initializer?.Value;
        if (initializerSyntax == null) return null;

        if (RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(local, usingOperation.Syntax,
                declaratorSyntax!, semanticModel, cancellationToken)) return null;

        var initializerOperation = semanticModel.GetOperation(initializerSyntax, cancellationToken);
        var unwrappedInitializer = UnwrapConversionsForDisposeReceiver(initializerOperation);
        return unwrappedInitializer is IObjectCreationOperation objectCreationOperation
            ? objectCreationOperation.Type
            : null;
    }

    private static bool HasDeclaratorInitializer(ILocalSymbol local, CancellationToken cancellationToken) =>
        RuleAnalysisHelper.GetVariableDeclaratorSyntax(local, cancellationToken)?.Initializer != null;

    private bool WasLocalReassignedBeforeUsing(ILocalSymbol local, IOperation usingOperation,
        SemanticModel semanticModel, CancellationToken cancellationToken) =>
        RuleAnalysisHelper.GetVariableDeclaratorSyntax(local, cancellationToken) is { } declaratorSyntax &&
               RuleAnalysisHelper.HasAssignmentToLocalBetweenDeclarationAndObservation(local, usingOperation.Syntax,
                   declaratorSyntax, semanticModel, cancellationToken);

    private static bool IsAwaitUsingOperation(IOperation operation) =>
        operation.Syntax switch {
            UsingStatementSyntax usingStatementSyntax => usingStatementSyntax.AwaitKeyword.RawKind != 0,
            LocalDeclarationStatementSyntax localDeclarationStatementSyntax => localDeclarationStatementSyntax
                .AwaitKeyword.RawKind != 0,
            _ => false
        };

    private static bool IsOverridableDispatchTarget(IMethodSymbol methodSymbol) =>
        !methodSymbol.IsStatic && methodSymbol.ContainingType?.IsSealed != true &&
        (methodSymbol.IsVirtual ||
               methodSymbol.IsAbstract ||
               methodSymbol.IsOverride && !methodSymbol.IsSealed);
}
