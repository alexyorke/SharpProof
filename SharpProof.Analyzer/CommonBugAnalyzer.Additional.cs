namespace SharpProof.Analyzer;

internal static partial class CommonBugAnalyzer {
    private static readonly ImmutableHashSet<BinaryOperatorKind> SuspiciousIdenticalOperators =
        ImmutableHashSet.Create(
            BinaryOperatorKind.Equals,
            BinaryOperatorKind.NotEquals,
            BinaryOperatorKind.LessThan,
            BinaryOperatorKind.LessThanOrEqual,
            BinaryOperatorKind.GreaterThan,
            BinaryOperatorKind.GreaterThanOrEqual,
            BinaryOperatorKind.Subtract,
            BinaryOperatorKind.Divide,
            BinaryOperatorKind.Remainder);

    private static void AnalyzeAdditionalCommonBugs(
        MethodBodyAnalysisContext context,
        AnalyzerSession session) {
        AnalyzeIdenticalOperands(context, session);
        AnalyzeContainerOwnedDisposal(context, session);
        AnalyzeUnconsumedDeferredQueries(context, session);
    }

    private static void AnalyzeIdenticalOperands(
        MethodBodyAnalysisContext context,
        AnalyzerSession session) {
        foreach (var binary in context.Snapshot.VisibleOperations.OfType<IBinaryOperation>()) {
            if (!SuspiciousIdenticalOperators.Contains(binary.OperatorKind) ||
                binary.OperatorMethod != null ||
                IsFloatingPoint(binary.LeftOperand.Type) ||
                IsFloatingPoint(binary.RightOperand.Type))
                continue;

            var left = GetStableLocalOrParameter(binary.LeftOperand);
            var right = GetStableLocalOrParameter(binary.RightOperand);
            if (left == null || !SymbolEq.AreEqual(left, right)) continue;

            Report(
                context,
                session,
                AnalyzerDiagnosticCatalog.Get("IdenticalOperandsRule"),
                binary.Syntax.GetLocation(),
                "identical_operands",
                binary.OperatorKind.ToString(),
                left.Name);
        }
    }

    private static bool IsFloatingPoint(ITypeSymbol? type) =>
        type?.SpecialType is SpecialType.System_Single or SpecialType.System_Double;

    private static ISymbol? GetStableLocalOrParameter(IOperation operation) {
        operation = Unwrap(operation)!;
        return operation switch {
            ILocalReferenceOperation local => local.Local,
            IParameterReferenceOperation parameter => parameter.Parameter,
            _ => null
        };
    }

    private static void AnalyzeContainerOwnedDisposal(
        MethodBodyAnalysisContext context,
        AnalyzerSession session) {
        foreach (var operation in context.Snapshot.VisibleOperations)
            switch (operation) {
                case IUsingOperation usingOperation:
                    ReportResolvedServiceInUsing(context, session, usingOperation.Resources);
                    break;
                case IUsingDeclarationOperation usingDeclaration:
                    ReportResolvedServiceInUsing(context, session, usingDeclaration.DeclarationGroup);
                    break;
                case IInvocationOperation disposal
                    when disposal.TargetMethod.Name is "Dispose" or "DisposeAsync" &&
                         Unwrap(disposal.Instance) is IInvocationOperation resolver &&
                         IsServiceResolution(resolver):
                    Report(
                        context,
                        session,
                        AnalyzerDiagnosticCatalog.Get("ContainerOwnedServiceDisposedRule"),
                        disposal.Syntax.GetLocation(),
                        "container_owned_service_disposed",
                        resolver.TargetMethod.Name);
                    break;
            }
    }

    private static void ReportResolvedServiceInUsing(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IOperation resources) {
        foreach (var resolver in resources.DescendantsAndSelf().OfType<IInvocationOperation>())
            if (IsServiceResolution(resolver))
                Report(
                    context,
                    session,
                    AnalyzerDiagnosticCatalog.Get("ContainerOwnedServiceDisposedRule"),
                    resolver.Syntax.GetLocation(),
                    "container_owned_service_disposed",
                    resolver.TargetMethod.Name);
    }

    private static bool IsServiceResolution(IInvocationOperation invocation) {
        if (invocation.TargetMethod.Name is not ("GetService" or "GetRequiredService")) return false;

        var containingType = invocation.TargetMethod.ContainingType.ToDisplayString();
        return containingType == "System.IServiceProvider" ||
               invocation.TargetMethod.ContainingNamespace?.ToDisplayString() ==
               "Microsoft.Extensions.DependencyInjection";
    }

    private static void AnalyzeUnconsumedDeferredQueries(
        MethodBodyAnalysisContext context,
        AnalyzerSession session) {
        var operations = context.Snapshot.VisibleOperations;
        foreach (var statement in operations.OfType<IExpressionStatementOperation>())
            if (Unwrap(statement.Operation) is IInvocationOperation invocation && IsDeferredQuery(invocation))
                Report(
                    context,
                    session,
                    AnalyzerDiagnosticCatalog.Get("UnconsumedDeferredQueryRule"),
                    invocation.Syntax.GetLocation(),
                    "unconsumed_deferred_query",
                    invocation.TargetMethod.Name);

        foreach (var declarator in operations.OfType<IVariableDeclaratorOperation>()) {
            if (declarator.Initializer?.Value is not { } initializer ||
                Unwrap(initializer) is not IInvocationOperation invocation ||
                !IsDeferredQuery(invocation))
                continue;

            var consumed = operations.OfType<ILocalReferenceOperation>().Any(reference =>
                SymbolEq.AreEqual(reference.Local, declarator.Symbol));
            if (consumed) continue;

            Report(
                context,
                session,
                AnalyzerDiagnosticCatalog.Get("UnconsumedDeferredQueryRule"),
                initializer.Syntax.GetLocation(),
                "unconsumed_deferred_query",
                invocation.TargetMethod.Name);
        }
    }

    private static bool IsDeferredQuery(IInvocationOperation invocation) {
        return DeferredQueryOperators.Contains(invocation.TargetMethod.Name) &&
               (IsLinqMethod(invocation.TargetMethod, "System.Linq.Enumerable") ||
                IsLinqMethod(invocation.TargetMethod, "System.Linq.Queryable"));
    }
}
