namespace SharpProof.Analyzer;
internal static class NullableContractAnalyzer {
    internal static void Analyze(MethodBodyAnalysisContext context, AnalyzerSession session) {
        if (context.Snapshot.RootOperation == null) return;
        var completions = MethodCompletionAnalysis.Collect(context, distinctByQueryPosition: true);
        if (completions.Length != 0) {
            VerifyReturnContracts(context, session, completions);
            VerifyParameterContracts(context, session, completions);
            VerifyMemberContracts(context, session, completions);
        }
        AuditNullForgivingOperators(context, session);
    }
    private static void VerifyReturnContracts(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        ImmutableArray<MethodNormalCompletion> completions) {
        var method = context.MethodSymbol;
        if (method.ReturnsVoid || method.ReturnType.SpecialType == SpecialType.System_Void) return;
        var requiresNonNull = NullableFlowFacts.GetMethodBodyReturnState(method) == NullableFlowFactState.NotNull;
        var hasConditionalContract = NullableFlowFacts.TryGetNotNullIfNotNullParameterName(method, out var inputName);
        if (!requiresNonNull && !hasConditionalContract) return;
        var conditionalContract = "[NotNullIfNotNull(\"" + inputName + "\")]";
        foreach (var completion in completions) {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (completion.ResultExpression == null) continue;
            var resultText = Parenthesize(completion.ResultExpression);
            if (requiresNonNull)
                Verify(
                    context,
                    session,
                    completion,
                    resultText + " != null",
                    AnalyzerDiagnosticCatalog.Get("NullableReturnContractViolationRule"),
                    method.Name,
                    "non-null return");
            if (hasConditionalContract &&
                method.Parameters.FirstOrDefault(parameter => parameter.Name == inputName) is { RefKind: not RefKind.Out }) {
                var escapedInput = EscapeIdentifier(inputName);
                Verify(
                    context,
                    session,
                    completion,
                    "old(" + escapedInput + ") == null || " + resultText + " != null",
                    AnalyzerDiagnosticCatalog.Get("NullableReturnContractViolationRule"),
                    [method.Name, conditionalContract],
                    CSharpSyntaxFacts.IsNullLiteral(completion.ResultExpression),
                    true);
            }
        }
    }
    private static void VerifyParameterContracts(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        ImmutableArray<MethodNormalCompletion> completions) {
        foreach (var parameter in context.MethodSymbol.Parameters) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var target = EscapeIdentifier(parameter.Name);
            if (NullableFlowFacts.HasNotNullPostcondition(parameter))
                foreach (var completion in completions)
                    Verify(
                        context,
                        session,
                        completion,
                        target + " != null",
                        AnalyzerDiagnosticCatalog.Get("NullableParameterPostconditionViolationRule"),
                        context.MethodSymbol.Name,
                        parameter.Name,
                        "[NotNull]");
            if (NullableFlowFacts.TryGetNotNullWhenValue(parameter, out var notNullWhen)) {
                var contract = FormatBooleanAttribute("NotNullWhen", notNullWhen);
                foreach (var completion in completions)
                    if (completion.ResultExpression != null)
                        Verify(
                            context,
                            session,
                            completion,
                            ConditionalImplication(completion.ResultExpression, notNullWhen, target + " != null"),
                            AnalyzerDiagnosticCatalog.Get("NullableParameterPostconditionViolationRule"),
                            context.MethodSymbol.Name,
                            parameter.Name,
                            contract);
            }
            if (NullableFlowFacts.TryGetMaybeNullWhenValue(parameter, out var maybeNullWhen) &&
                parameter.NullableAnnotation == NullableAnnotation.NotAnnotated) {
                var contract = FormatBooleanAttribute("MaybeNullWhen", maybeNullWhen);
                foreach (var completion in completions)
                    if (completion.ResultExpression != null)
                        Verify(
                            context,
                            session,
                            completion,
                            ConditionalImplication(completion.ResultExpression, !maybeNullWhen, target + " != null"),
                            AnalyzerDiagnosticCatalog.Get("NullableParameterPostconditionViolationRule"),
                            context.MethodSymbol.Name,
                            parameter.Name,
                            contract);
            }
        }
    }
    private static void VerifyMemberContracts(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        ImmutableArray<MethodNormalCompletion> completions) {
        var method = context.MethodSymbol;
        if (method.IsStatic || method.ContainingType == null) return;
        foreach (var targetName in NullableFlowFacts.GetMemberNotNullTargets(method))
            VerifyMemberTarget(context, session, completions, targetName, null);
        foreach (var expectedResult in new[] { false, true })
            foreach (var targetName in NullableFlowFacts.GetMemberNotNullWhenTargets(method, expectedResult))
                VerifyMemberTarget(context, session, completions, targetName, expectedResult);
    }
    private static void VerifyMemberTarget(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        ImmutableArray<MethodNormalCompletion> completions,
        string targetName,
        bool? expectedResult) {
        if (!NullableFlowFacts.TryResolveInstanceMemberTarget(context.MethodSymbol.ContainingType, targetName, out var member))
            return;
        // User-defined getters are not necessarily stable or repeatable. Auto-properties
        // have field-like storage and can use the same assignment proof as fields.
        if (member is IPropertySymbol property &&
            !IsAutoProperty(property, context.CancellationToken)) {
            return;
        }
        var target = "this." + EscapeIdentifier(member.Name) + " != null";
        var contract = expectedResult.HasValue
            ? "[MemberNotNullWhen(" + FormatBoolean(expectedResult.Value) + ", \"" +
              targetName + "\")]"
            : "[MemberNotNull(\"" + targetName + "\")]";
        foreach (var completion in completions) {
            if (expectedResult.HasValue && completion.ResultExpression == null) continue;
            var condition = expectedResult.HasValue
                ? ConditionalImplication(completion.ResultExpression!, expectedResult.Value, target)
                : target;
            Verify(
                context,
                session,
                completion,
                condition,
                AnalyzerDiagnosticCatalog.Get("NullableMemberContractViolationRule"),
                [context.MethodSymbol.Name, targetName, contract],
                member is IFieldSymbol &&
                NullableFlowFacts.TryGetMemberType(member, out var memberType) &&
                memberType.NullableAnnotation == NullableAnnotation.Annotated &&
                !HasVisibleAssignmentToMember(context, member),
                false);
        }
    }
    private static bool IsAutoProperty(IPropertySymbol property, CancellationToken cancellationToken) {
        foreach (var syntaxReference in property.DeclaringSyntaxReferences) {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(cancellationToken) is not PropertyDeclarationSyntax {
                ExpressionBody: null,
                AccessorList.Accessors: var accessors
            })
                continue;
            if (accessors.Any(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration)) &&
                accessors.All(static accessor => accessor.Body == null && accessor.ExpressionBody == null))
                return true;
        }
        return false;
    }
    private static void AuditNullForgivingOperators(MethodBodyAnalysisContext context, AnalyzerSession session) {
        var suppressions = context.Node.DescendantNodes(node => node == context.Node || node is not LocalFunctionStatementSyntax)
            .OfType<PostfixUnaryExpressionSyntax>()
            .Where(static postfix => postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            .GroupBy(static postfix => (
                postfix.Span.Start,
                postfix.Span.Length,
                postfix.OperatorToken.Span.Start,
                postfix.OperatorToken.Span.Length))
            .Select(static group => group.First());
        foreach (var suppression in suppressions) {
            context.CancellationToken.ThrowIfCancellationRequested();
            var operand = suppression.Operand;
            var condition = Parenthesize(operand) + " != null";
            if (IsStaticallyNonNullInput(operand, context)) continue;
            var memberFactInvalidated = HasPotentiallyInvalidatingCallBefore(suppression, operand, context);
            var proof = context.State.ProveAtNode(
                suppression,
                condition,
                session.ProofService.SmtAnalysis,
                false,
                context.CancellationToken);
            if (memberFactInvalidated && proof.TruthValue == SymbolicTruthValue.ProvenTrue) {
                continue;
            }
            if (proof.TruthValue == SymbolicTruthValue.Unreachable) continue;
            if (proof.TruthValue is not (SymbolicTruthValue.ProvenTrue or SymbolicTruthValue.ProvenFalse)) {
                if (proof.CounterexampleWitness.Status == SymbolicWitnessStatus.Exact &&
                    CanUseSuppressionCounterexample(operand, context)) {
                    ReportUnsafeSuppression(context, suppression);
                    continue;
                }
                var roslynStateBeforeSuppression = NullableFlowFacts.GetExpressionStateAtPosition(
                    operand,
                    suppression.SpanStart,
                    context.SemanticModel,
                    context.CancellationToken);
                if (roslynStateBeforeSuppression == NullableFlowFactState.NotNull) continue;
                if (roslynStateBeforeSuppression == NullableFlowFactState.MaybeNull &&
                    CanUseSuppressionCounterexample(operand, context)) {
                    ReportUnsafeSuppression(context, suppression);
                    continue;
                }
                continue;
            }
            if (proof.TruthValue == SymbolicTruthValue.ProvenFalse)
                ReportUnsafeSuppression(context, suppression);
        }
    }
    private static void ReportUnsafeSuppression(MethodBodyAnalysisContext context, PostfixUnaryExpressionSyntax suppression)
        => context.ReportDiagnostic(Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("UnsafeNullForgivingOperatorRule"),
            suppression.OperatorToken.GetLocation(),
            suppression.Operand.ToString()));
    private static void Verify(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        MethodNormalCompletion completion,
        string condition,
        DiagnosticDescriptor violationDescriptor,
        params object[] messageArguments)
            => Verify(context, session, completion, condition, violationDescriptor, messageArguments, false, false);
    private static void Verify(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        MethodNormalCompletion completion,
        string condition,
        DiagnosticDescriptor violationDescriptor,
        object[] messageArguments,
        bool unknownIsViolation,
        bool counterexampleIsViolation) {
        var proof = MethodCompletionAnalysis.Prove(context, session.ProofService.SmtAnalysis, completion, condition);
        if (proof.TruthValue is SymbolicTruthValue.ProvenTrue or SymbolicTruthValue.Unreachable) return;
        if (proof.TruthValue == SymbolicTruthValue.ProvenFalse ||
            counterexampleIsViolation &&
            proof.CounterexampleWitness.Status == SymbolicWitnessStatus.Exact ||
            unknownIsViolation) {
            context.ReportDiagnostic(Diagnostic.Create(violationDescriptor, completion.Location, messageArguments));
        }
    }
    private static bool HasVisibleAssignmentToMember(MethodBodyAnalysisContext context, ISymbol member) {
        foreach (var operation in context.Snapshot.VisibleOperations) {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (operation is not ISimpleAssignmentOperation assignment) continue;
            ISymbol? target = assignment.Target switch {
                IFieldReferenceOperation field => field.Field.OriginalDefinition,
                IPropertyReferenceOperation property => property.Property.OriginalDefinition,
                _ => null
            };
            if (target != null && SymbolEq.AreEqual(target, member.OriginalDefinition))
                return true;
        }
        return false;
    }
    private static bool IsStaticallyNonNullInput(ExpressionSyntax expression, MethodBodyAnalysisContext context)
        => context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol is
                   IParameterSymbol { NullableAnnotation: NullableAnnotation.NotAnnotated } &&
               NullableFlowFacts.GetExpressionState(expression, context.SemanticModel,
                   context.CancellationToken) == NullableFlowFactState.NotNull;
    private static bool CanUseSuppressionCounterexample(ExpressionSyntax expression, MethodBodyAnalysisContext context) {
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AsExpression)) return true;
        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        return symbol switch {
            IParameterSymbol parameter => parameter.NullableAnnotation == NullableAnnotation.Annotated,
            ILocalSymbol local => local.NullableAnnotation == NullableAnnotation.Annotated,
            IFieldSymbol field => field.NullableAnnotation == NullableAnnotation.Annotated,
            IPropertySymbol property => property.NullableAnnotation == NullableAnnotation.Annotated,
            _ => false
        };
    }
    private static bool HasPotentiallyInvalidatingCallBefore(
        PostfixUnaryExpressionSyntax suppression,
        ExpressionSyntax operand,
        MethodBodyAnalysisContext context) {
        if (context.SemanticModel.GetSymbolInfo(operand, context.CancellationToken).Symbol is not
            (IFieldSymbol or IPropertySymbol))
            return false;
        foreach (var invocation in context.Snapshot.VisibleOperations
                     .OfType<IInvocationOperation>()
                     .Where(invocation => invocation.Syntax.SpanStart < suppression.SpanStart)) {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (invocation.TargetMethod.DeclaringSyntaxReferences.IsDefaultOrEmpty) return true;
        }
        return false;
    }
    private static string ConditionalImplication(ExpressionSyntax result, bool expected, string consequence) =>
        Parenthesize(result) + " != " + FormatBoolean(expected) + " || " + consequence;
    private static string FormatBooleanAttribute(string name, bool value) =>
        "[" + name + "(" + FormatBoolean(value) + ")]";
    private static string FormatBoolean(bool value) => value ? "true" : "false";
    private static string Parenthesize(ExpressionSyntax expression) => "(" + expression.WithoutTrivia() + ")";
    private static string EscapeIdentifier(string identifier) => "@" + identifier;
}
