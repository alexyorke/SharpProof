namespace SharpProof.Analyzer.Engine.Rules;

internal static class AwaitPurityRule {
    internal static PurityAnalysisEngine.PurityAnalysisResult CheckTyped(IAwaitOperation awaitOperation,
        PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState) {


        var awaitedExpressionResult =
            PurityAnalysisEngine.CheckSingleOperation(awaitOperation.Operation, context, currentState);

        if (!awaitedExpressionResult.IsPure) return awaitedExpressionResult;

        return CheckAwaitPatternMembers(awaitOperation, context);
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckAwaitablePatternMembers(
        ITypeSymbol? awaitableType,
        SyntaxNode awaitSyntax,
        PurityAnalysisContext context) {
        var getAwaiterMethod = awaitableType?
            .GetMembers("GetAwaiter")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.Parameters.Length == 0);
        var awaiterType = getAwaiterMethod?.ReturnType;
        var isCompletedProperty = awaiterType?
            .GetMembers("IsCompleted")
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property => property.Type.SpecialType == SpecialType.System_Boolean);
        var getResultMethod = awaiterType?
            .GetMembers("GetResult")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.Parameters.Length == 0);

        return CheckResolvedAwaitPatternMembers(
            getAwaiterMethod,
            isCompletedProperty,
            getResultMethod,
            awaitSyntax,
            context);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckAwaitPatternMembers(
        IAwaitOperation awaitOperation,
        PurityAnalysisContext context) {
        if (awaitOperation.Syntax is not AwaitExpressionSyntax awaitSyntax)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var awaitInfo = context.SemanticModel.GetAwaitExpressionInfo(awaitSyntax);
        return CheckResolvedAwaitPatternMembers(
            awaitInfo.GetAwaiterMethod,
            awaitInfo.IsCompletedProperty,
            awaitInfo.GetResultMethod,
            awaitOperation.Syntax,
            context);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckResolvedAwaitPatternMembers(
        IMethodSymbol? getAwaiterMethod,
        IPropertySymbol? isCompletedProperty,
        IMethodSymbol? getResultMethod,
        SyntaxNode awaitSyntax,
        PurityAnalysisContext context) {
        var getAwaiterResult = CheckAwaitPatternMethod(getAwaiterMethod, awaitSyntax, context);
        if (!getAwaiterResult.IsPure) return getAwaiterResult;

        var isCompletedResult =
            CheckAwaitPatternMethod(isCompletedProperty?.GetMethod, awaitSyntax, context);
        if (!isCompletedResult.IsPure) return isCompletedResult;

        if (!IsKnownConstantTrueIsCompletedGetter(isCompletedProperty?.GetMethod, context.SemanticModel,
                context.CancellationToken)) {
            var continuationSchedulingResult = CheckAwaitContinuationSchedulingMethods(
                getAwaiterMethod?.ReturnType,
                awaitSyntax,
                context);
            if (!continuationSchedulingResult.IsPure) return continuationSchedulingResult;
        }

        return CheckAwaitPatternMethod(getResultMethod, awaitSyntax, context);
    }

    private static bool IsKnownConstantTrueIsCompletedGetter(
        IMethodSymbol? getter,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (getter == null) return false;

        foreach (var syntaxReference in getter.DeclaringSyntaxReferences) {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(cancellationToken) is not AccessorDeclarationSyntax accessor) continue;

            var expression = accessor.ExpressionBody?.Expression ??
                             accessor.Body?.Statements
                                 .OfType<ReturnStatementSyntax>()
                                 .SingleOrDefault()
                                 ?.Expression;

            if (expression == null) continue;

            var constant = CompilationSyntaxAccess.GetConstantValue(semanticModel, expression, cancellationToken);
            if (constant.HasValue &&
                constant.Value is bool boolValue &&
                boolValue)
                return true;
        }

        return false;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckAwaitContinuationSchedulingMethods(
        ITypeSymbol? awaiterType,
        SyntaxNode awaitSyntax,
        PurityAnalysisContext context) {
        if (awaiterType == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        foreach (var schedulingMethod in EnumerateAwaitContinuationSchedulingMethods(awaiterType,
                     context.SemanticModel.Compilation)) {
            var schedulingResult = CheckAwaitPatternMethod(schedulingMethod, awaitSyntax, context);
            if (!schedulingResult.IsPure) return schedulingResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static IEnumerable<IMethodSymbol> EnumerateAwaitContinuationSchedulingMethods(
        ITypeSymbol awaiterType,
        Compilation compilation) {
        var seen = new HashSet<IMethodSymbol>(SymbolEq.Default);

        foreach (var method in awaiterType.GetMembers()
                     .OfType<IMethodSymbol>()
                     .Where(AwaitableRuntimeMemberClassifier.IsContinuationSchedulingMethod))
            if (seen.Add(method.OriginalDefinition))
                yield return method;

        if (awaiterType is not INamedTypeSymbol namedAwaiterType) yield break;

        foreach (var interfaceName in new[] {
                     "System.Runtime.CompilerServices.INotifyCompletion",
                     "System.Runtime.CompilerServices.ICriticalNotifyCompletion"
                 }) {
            var interfaceType = compilation.GetTypeByMetadataName(interfaceName);
            if (interfaceType == null ||
                (!SymbolEq.AreEqual(namedAwaiterType, interfaceType) &&
                 !namedAwaiterType.AllInterfaces.Contains(interfaceType, SymbolEq.Default)))
                continue;

            foreach (var interfaceMethod in interfaceType.GetMembers()
                         .OfType<IMethodSymbol>()
                         .Where(AwaitableRuntimeMemberClassifier.IsContinuationSchedulingMethod)) {
                var implementation =
                    namedAwaiterType.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;
                if (implementation != null && seen.Add(implementation.OriginalDefinition)) yield return implementation;
            }
        }
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckAwaitPatternMethod(
        IMethodSymbol? methodSymbol,
        SyntaxNode awaitSyntax,
        PurityAnalysisContext context) {
        if (methodSymbol == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        return PurityCalleeResolver.GetCanonicalCalleePurityAtUse(methodSymbol, awaitSyntax, context);
    }

}
