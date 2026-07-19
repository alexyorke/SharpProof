namespace SharpProof.Analyzer;

internal static class NullableContractAnalyzer
{
    internal static void Analyze(MethodBodyAnalysisContext context, AnalyzerSession session)
    {
        if (context.Snapshot.RootOperation == null) return;

        var completions = CollectNormalCompletions(context);
        if (completions.Length != 0)
        {
            VerifyReturnContracts(context, session, completions);
            VerifyParameterContracts(context, session, completions);
            VerifyMemberContracts(context, session, completions);
        }

        AuditNullForgivingOperators(context, session);
    }

    private static void VerifyReturnContracts(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        ImmutableArray<NormalCompletion> completions)
    {
        var method = context.MethodSymbol;
        if (method.ReturnsVoid || method.ReturnType.SpecialType == SpecialType.System_Void) return;

        var requiresNonNull = NullableFlowFacts.GetMethodBodyReturnState(method) == NullableFlowFactState.NotNull;
        var hasConditionalContract = NullableFlowFacts.TryGetNotNullIfNotNullParameterName(
            method,
            out var inputName);
        if (!requiresNonNull && !hasConditionalContract) return;
        var conditionalContract = "[NotNullIfNotNull(\"" + inputName + "\")]";

        foreach (var completion in completions)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (completion.ResultExpression == null) continue;

            var resultText = Parenthesize(completion.ResultExpression);
            if (requiresNonNull)
                Verify(
                    context,
                    session,
                    completion,
                    resultText + " != null",
                    SharpProofDiagnostics.NullableReturnContractViolationRule,
                    "return",
                    "non-null return",
                    method.Name,
                    "non-null return");

            if (hasConditionalContract &&
                method.Parameters.FirstOrDefault(parameter => parameter.Name == inputName) is
                    { RefKind: not RefKind.Out })
            {
                var escapedInput = EscapeIdentifier(inputName);
                Verify(
                    context,
                    session,
                    completion,
                    "old(" + escapedInput + ") == null || " + resultText + " != null",
                    SharpProofDiagnostics.NullableReturnContractViolationRule,
                    "return-if-input-not-null",
                    conditionalContract,
                    new object[] { method.Name, conditionalContract },
                    CSharpSyntaxFacts.IsNullLiteral(completion.ResultExpression),
                    true);
            }
        }
    }

    private static void VerifyParameterContracts(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        ImmutableArray<NormalCompletion> completions)
    {
        foreach (var parameter in context.MethodSymbol.Parameters)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var target = EscapeIdentifier(parameter.Name);

            if (NullableFlowFacts.HasNotNullPostcondition(parameter))
                foreach (var completion in completions)
                    Verify(
                        context,
                        session,
                        completion,
                        target + " != null",
                        SharpProofDiagnostics.NullableParameterPostconditionViolationRule,
                        "parameter-not-null",
                        "[NotNull]",
                        context.MethodSymbol.Name,
                        parameter.Name,
                        "[NotNull]");

            if (NullableFlowFacts.TryGetNotNullWhenValue(parameter, out var notNullWhen))
            {
                var contract = FormatBooleanAttribute("NotNullWhen", notNullWhen);
                foreach (var completion in completions)
                    if (completion.ResultExpression != null)
                        Verify(
                            context,
                            session,
                            completion,
                            ConditionalImplication(completion.ResultExpression, notNullWhen, target + " != null"),
                            SharpProofDiagnostics.NullableParameterPostconditionViolationRule,
                            "parameter-not-null-when",
                            contract,
                            context.MethodSymbol.Name,
                            parameter.Name,
                            contract);
            }

            if (NullableFlowFacts.TryGetMaybeNullWhenValue(parameter, out var maybeNullWhen) &&
                parameter.NullableAnnotation == NullableAnnotation.NotAnnotated)
            {
                var contract = FormatBooleanAttribute("MaybeNullWhen", maybeNullWhen);
                foreach (var completion in completions)
                    if (completion.ResultExpression != null)
                        Verify(
                            context,
                            session,
                            completion,
                            ConditionalImplication(completion.ResultExpression, !maybeNullWhen, target + " != null"),
                            SharpProofDiagnostics.NullableParameterPostconditionViolationRule,
                            "parameter-non-null-opposite-maybe-null-when",
                            contract,
                            context.MethodSymbol.Name,
                            parameter.Name,
                            contract);
            }
        }
    }

    private static void VerifyMemberContracts(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        ImmutableArray<NormalCompletion> completions)
    {
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
        ImmutableArray<NormalCompletion> completions,
        string targetName,
        bool? expectedResult)
    {
        if (!NullableFlowFacts.TryResolveInstanceMemberTarget(
                context.MethodSymbol.ContainingType,
                targetName,
                out var member))
            return;

        // User-defined getters are not necessarily stable or repeatable. Auto-properties
        // have field-like storage and can use the same assignment proof as fields.
        if (member is IPropertySymbol property &&
            !IsAutoProperty(property, context.CancellationToken))
        {
            foreach (var completion in completions)
                ReportInconclusive(
                    context,
                    session,
                    completion.Location,
                    expectedResult.HasValue ? "member-not-null-when" : "member-not-null",
                    targetName,
                    new SymbolicConditionProofResult(
                        "this." + EscapeIdentifier(member.Name) + " != null",
                        SymbolicTruthValue.Unknown,
                        "property getter stability is not proven"));
            return;
        }

        var target = "this." + EscapeIdentifier(member.Name) + " != null";
        var contract = expectedResult.HasValue
            ? "[MemberNotNullWhen(" + FormatBoolean(expectedResult.Value) + ", \"" +
              targetName + "\")]"
            : "[MemberNotNull(\"" + targetName + "\")]";
        foreach (var completion in completions)
        {
            if (expectedResult.HasValue && completion.ResultExpression == null) continue;
            var condition = expectedResult.HasValue
                ? ConditionalImplication(completion.ResultExpression!, expectedResult.Value, target)
                : target;
            Verify(
                context,
                session,
                completion,
                condition,
                SharpProofDiagnostics.NullableMemberContractViolationRule,
                expectedResult.HasValue ? "member-not-null-when" : "member-not-null",
                contract,
                new object[] { context.MethodSymbol.Name, targetName, contract },
                member is IFieldSymbol &&
                NullableFlowFacts.TryGetMemberType(member, out var memberType) &&
                memberType.NullableAnnotation == NullableAnnotation.Annotated &&
                !HasVisibleAssignmentToMember(context, member),
                false);
        }
    }

    private static bool IsAutoProperty(IPropertySymbol property, CancellationToken cancellationToken)
    {
        foreach (var syntaxReference in property.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (syntaxReference.GetSyntax(cancellationToken) is not PropertyDeclarationSyntax
                {
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

    private static void AuditNullForgivingOperators(MethodBodyAnalysisContext context, AnalyzerSession session)
    {
        var suppressions = context.Node.DescendantNodes(
                node => node == context.Node || node is not LocalFunctionStatementSyntax)
            .OfType<PostfixUnaryExpressionSyntax>()
            .Where(static postfix => postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
            .GroupBy(static postfix => (
                postfix.Span.Start,
                postfix.Span.Length,
                postfix.OperatorToken.Span.Start,
                postfix.OperatorToken.Span.Length))
            .Select(static group => group.First());

        foreach (var suppression in suppressions)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var operand = suppression.Operand;
            var condition = Parenthesize(operand) + " != null";
            if (IsStaticallyNonNullInput(operand, context))
            {
                ReportSuppression(
                    context,
                    session,
                    suppression,
                    condition,
                    SharpProofDiagnostics.UnnecessaryNullForgivingOperatorRule,
                    "declared non-null input and Roslyn flow state prove the operand non-null");
                continue;
            }

            var memberFactInvalidated = HasPotentiallyInvalidatingCallBefore(
                suppression,
                operand,
                context,
                session);
            var proof = ProveAtSyntaxNode(
                context,
                session,
                suppression,
                condition,
                false);
            if (memberFactInvalidated && proof.TruthValue == SymbolicTruthValue.ProvenTrue)
            {
                ReportInconclusive(context, session, suppression.GetLocation(), "null-forgiving", condition, proof);
                continue;
            }

            if (proof.TruthValue == SymbolicTruthValue.Unreachable) continue;

            if (proof.TruthValue is not (SymbolicTruthValue.ProvenTrue or SymbolicTruthValue.ProvenFalse))
            {
                if (proof.CounterexampleWitness.Status == SymbolicWitnessStatus.Exact &&
                    CanUseSuppressionCounterexample(operand, context))
                {
                    ReportSuppression(
                        context,
                        session,
                        suppression,
                        condition,
                        SharpProofDiagnostics.UnsafeNullForgivingOperatorRule,
                        proof.CounterexampleWitness.Reason,
                        proof);
                    continue;
                }

                var roslynStateBeforeSuppression = NullableFlowFacts.GetExpressionStateAtPosition(
                    operand,
                    suppression.SpanStart,
                    context.SemanticModel,
                    context.CancellationToken);
                if (roslynStateBeforeSuppression == NullableFlowFactState.NotNull)
                {
                    ReportSuppression(
                        context,
                        session,
                        suppression,
                        condition,
                        SharpProofDiagnostics.UnnecessaryNullForgivingOperatorRule,
                        "roslyn flow state proves the operand non-null");
                    continue;
                }

                if (roslynStateBeforeSuppression == NullableFlowFactState.MaybeNull &&
                    CanUseSuppressionCounterexample(operand, context))
                {
                    ReportSuppression(
                        context,
                        session,
                        suppression,
                        condition,
                        SharpProofDiagnostics.UnsafeNullForgivingOperatorRule,
                        "Roslyn flow state permits the operand to be null");
                    continue;
                }

                ReportInconclusive(context, session, suppression.GetLocation(), "null-forgiving", condition, proof);
                continue;
            }

            var descriptor = proof.TruthValue == SymbolicTruthValue.ProvenTrue
                ? SharpProofDiagnostics.UnnecessaryNullForgivingOperatorRule
                : SharpProofDiagnostics.UnsafeNullForgivingOperatorRule;
            ReportSuppression(context, session, suppression, condition, descriptor, proof.Reason, proof);
        }
    }

    private static void ReportSuppression(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        PostfixUnaryExpressionSyntax suppression,
        string condition,
        DiagnosticDescriptor descriptor,
        string reason,
        SymbolicConditionProofResult? proof = null)
    {
        var properties = proof == null
            ? CreateProperties(
                context,
                suppression.GetLocation(),
                "null-forgiving",
                condition,
                suppression.Operand.ToString(),
                "Proven",
                reason,
                null)
            : CreateProperties(
                context,
                suppression.GetLocation(),
                "null-forgiving",
                condition,
                suppression.Operand.ToString(),
                proof);
        var diagnostic = Diagnostic.Create(
            descriptor,
            suppression.OperatorToken.GetLocation(),
            properties,
            suppression.Operand.ToString());
        if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
    }

    private static void Verify(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        NormalCompletion completion,
        string condition,
        DiagnosticDescriptor violationDescriptor,
        string kind,
        string contract,
        params object[] messageArguments)
    {
        Verify(
            context,
            session,
            completion,
            condition,
            violationDescriptor,
            kind,
            contract,
            messageArguments,
            false,
            false);
    }

    private static void Verify(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        NormalCompletion completion,
        string condition,
        DiagnosticDescriptor violationDescriptor,
        string kind,
        string contract,
        object[] messageArguments,
        bool unknownIsViolation,
        bool counterexampleIsViolation)
    {
        var proof = condition.IndexOf("old(", StringComparison.Ordinal) >= 0
            ? MethodEnsuresAnalyzer.TryCreateEntrySnapshotProofCondition(
                condition,
                context.MethodSymbol,
                context.SemanticModel,
                completion.QueryNode.SpanStart,
                context.CancellationToken,
                out var symbolicCondition,
                out var initialState,
                out var snapshotFailureReason)
                ? ProveAtSyntaxNode(
                    context,
                    session,
                    completion.QueryNode,
                    condition,
                    symbolicCondition,
                    initialState,
                    completion.IncludeCurrentStatementCompletionFacts)
                : new SymbolicConditionProofResult(
                    condition,
                    SymbolicTruthValue.Unknown,
                    snapshotFailureReason ?? "entry snapshot could not be created")
            : ProveAtSyntaxNode(
                context,
                session,
                completion.QueryNode,
                condition,
                completion.IncludeCurrentStatementCompletionFacts);
        if (proof.TruthValue is SymbolicTruthValue.ProvenTrue or SymbolicTruthValue.Unreachable) return;

        if (proof.TruthValue == SymbolicTruthValue.ProvenFalse ||
            counterexampleIsViolation &&
            proof.CounterexampleWitness.Status == SymbolicWitnessStatus.Exact ||
            unknownIsViolation)
        {
            var properties = CreateProperties(
                context,
                completion.Location,
                kind,
                condition,
                contract,
                proof);
            var diagnostic = Diagnostic.Create(violationDescriptor, completion.Location, properties, messageArguments);
            if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
            return;
        }

        ReportInconclusive(context, session, completion.Location, kind, contract, proof);
    }

    private static bool HasVisibleAssignmentToMember(MethodBodyAnalysisContext context, ISymbol member)
    {
        foreach (var operation in context.Snapshot.VisibleOperations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (operation is not ISimpleAssignmentOperation assignment) continue;

            ISymbol? target = assignment.Target switch
            {
                IFieldReferenceOperation field => field.Field.OriginalDefinition,
                IPropertyReferenceOperation property => property.Property.OriginalDefinition,
                _ => null
            };
            if (target != null && SymbolEq.AreEqual(target, member.OriginalDefinition))
                return true;
        }

        return false;
    }

    private static bool IsStaticallyNonNullInput(
        ExpressionSyntax expression,
        MethodBodyAnalysisContext context)
    {
        return context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol is
                   IParameterSymbol { NullableAnnotation: NullableAnnotation.NotAnnotated } &&
               NullableFlowFacts.GetExpressionState(
                   expression,
                   context.SemanticModel,
                   context.CancellationToken) == NullableFlowFactState.NotNull;
    }

    private static SymbolicConditionProofResult ProveAtSyntaxNode(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        SyntaxNode node,
        string condition,
        bool includeCurrentStatementCompletionFacts)
    {
        var outcome = context.State.TryProveAtNode(
            node,
            condition,
            session.PurityService.SmtAnalysis,
            includeCurrentStatementCompletionFacts,
            context.CancellationToken);
        return AnalyzerSymbolicQueryBoundary.ResolveProof(outcome, condition, context.CancellationToken);
    }

    private static SymbolicConditionProofResult ProveAtSyntaxNode(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        SyntaxNode node,
        string condition,
        SymbolicCondition symbolicCondition,
        SymbolicState initialState,
        bool includeCurrentStatementCompletionFacts)
    {
        var outcome = context.State.TryProveAtNode(
            node,
            condition,
            symbolicCondition,
            initialState,
            session.PurityService.SmtAnalysis,
            includeCurrentStatementCompletionFacts,
            context.CancellationToken);
        return AnalyzerSymbolicQueryBoundary.ResolveProof(outcome, condition, context.CancellationToken);
    }

    private static bool CanUseSuppressionCounterexample(
        ExpressionSyntax expression,
        MethodBodyAnalysisContext context)
    {
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AsExpression)) return true;

        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        return symbol switch
        {
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
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        if (context.SemanticModel.GetSymbolInfo(operand, context.CancellationToken).Symbol is not
            (IFieldSymbol or IPropertySymbol))
            return false;

        foreach (var invocation in context.Snapshot.VisibleOperations
                     .OfType<IInvocationOperation>()
                     .Where(invocation => invocation.Syntax.SpanStart < suppression.SpanStart))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var policy = PurityPolicyResolver.ResolveInvocation(
                invocation.TargetMethod,
                invocation,
                context.SemanticModel.Compilation,
                session.AttributePolicy);
            if (policy.Decision != PurityPolicyDecision.Pure) return true;
        }

        return false;
    }

    private static void ReportInconclusive(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        Location location,
        string kind,
        string contract,
        SymbolicConditionProofResult proof)
    {
        if (!ShouldReportInconclusive(context)) return;

        var properties = CreateProperties(context, location, kind, proof.Condition, contract, proof);
        var diagnostic = Diagnostic.Create(
            SharpProofDiagnostics.NullableVerificationInconclusiveRule,
            location,
            properties,
            context.MethodSymbol.Name,
            contract,
            proof.GetDisplayReason());
        if (!session.Baseline.IsSuppressed(diagnostic)) context.ReportDiagnostic(diagnostic);
    }

    private static bool ShouldReportInconclusive(MethodBodyAnalysisContext context)
    {
        return context.Configuration.ReportNullableInconclusive;
    }

    private static ImmutableDictionary<string, string?> CreateProperties(
        MethodBodyAnalysisContext context,
        Location location,
        string kind,
        string condition,
        string target,
        SymbolicConditionProofResult proof)
    {
        return CreateProperties(
            context,
            location,
            kind,
            condition,
            target,
            proof.Proof.Status.ToString(),
            proof.Reason,
            proof.Proof.UnknownReason.ToString());
    }

    private static ImmutableDictionary<string, string?> CreateProperties(
        MethodBodyAnalysisContext context,
        Location location,
        string kind,
        string condition,
        string target,
        string proofStatus,
        string proofReason,
        string? unknownReason)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.NullableContractKindProperty, kind)
            .Add(SharpProofDiagnostics.NullableContractConditionProperty, condition)
            .Add(SharpProofDiagnostics.NullableContractTargetProperty, target)
            .Add(SharpProofDiagnostics.NullableProofStatusProperty, proofStatus)
            .Add(SharpProofDiagnostics.NullableProofReasonProperty, proofReason);
        return AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            context.MethodSymbol,
            context.Node.SyntaxTree,
            "NullableContract",
            target,
            kind + "@" + location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture),
            location,
            target,
            proofStatus,
            unknownReason,
            condition);
    }

    private static ImmutableArray<NormalCompletion> CollectNormalCompletions(MethodBodyAnalysisContext context)
    {
        var builder = ImmutableArray.CreateBuilder<NormalCompletion>();
        foreach (var operation in context.Snapshot.VisibleOperations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (operation is not IReturnOperation returnOperation ||
                AnalyzerSyntaxHelpers.IsCompilerMarkedUnreachable(
                    operation.Syntax,
                    context.SemanticModel,
                    context.CancellationToken))
                continue;

            var expression = returnOperation.ReturnedValue?.Syntax as ExpressionSyntax;
            builder.Add(new NormalCompletion(
                expression,
                expression?.GetLocation() ?? operation.Syntax.GetLocation(),
                operation.Syntax,
                false));
        }

        if (CSharpSyntaxFacts.TryGetExpressionBody(context.Node, out var expressionBody))
        {
            var hasResultValue = AnalyzerSyntaxHelpers.HasResultValue(context.MethodSymbol);
            builder.Add(new NormalCompletion(
                hasResultValue ? expressionBody : null,
                expressionBody.GetLocation(),
                expressionBody,
                !hasResultValue));
        }
        else if (CSharpSyntaxFacts.GetBlockBody(context.Node) is { } body &&
                 AnalyzerSyntaxHelpers.BodyEndPointIsReachable(body, context.SemanticModel))
            builder.Add(new NormalCompletion(null, body.CloseBraceToken.GetLocation(), body, true));

        return builder
            .GroupBy(static completion => completion.QueryNode.SpanStart)
            .Select(static group => group.First())
            .ToImmutableArray();
    }

    private static string ConditionalImplication(ExpressionSyntax result, bool expected, string consequence)
    {
        return Parenthesize(result) + " != " + FormatBoolean(expected) + " || " + consequence;
    }

    private static string FormatBooleanAttribute(string name, bool value) =>
        "[" + name + "(" + FormatBoolean(value) + ")]";

    private static string FormatBoolean(bool value) => value ? "true" : "false";

    private static string Parenthesize(ExpressionSyntax expression) => "(" + expression.WithoutTrivia() + ")";

    private static string EscapeIdentifier(string identifier) => "@" + identifier;

    private readonly record struct NormalCompletion(
        ExpressionSyntax? ResultExpression,
        Location Location,
        SyntaxNode QueryNode,
        bool IncludeCurrentStatementCompletionFacts);
}
