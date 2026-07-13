using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer;

internal static class NullableContractAnalyzer
{
    internal static void Analyze(MethodBodyAnalysisContext context, AnalyzerSession session)
    {
        if (context.State.RootOperation == null) return;

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
                    "[NotNullIfNotNull(\"" + inputName + "\")]",
                    new object[] { method.Name, "[NotNullIfNotNull(\"" + inputName + "\")]" },
                    IsNullLiteral(completion.ResultExpression),
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
                foreach (var completion in completions)
                    if (completion.ResultExpression != null)
                        Verify(
                            context,
                            session,
                            completion,
                            ConditionalImplication(completion.ResultExpression, notNullWhen, target + " != null"),
                            SharpProofDiagnostics.NullableParameterPostconditionViolationRule,
                            "parameter-not-null-when",
                            "[NotNullWhen(" + notNullWhen.ToString().ToLowerInvariant() + ")]",
                            context.MethodSymbol.Name,
                            parameter.Name,
                            "[NotNullWhen(" + notNullWhen.ToString().ToLowerInvariant() + ")]" );

            if (NullableFlowFacts.TryGetMaybeNullWhenValue(parameter, out var maybeNullWhen) &&
                parameter.NullableAnnotation == NullableAnnotation.NotAnnotated)
                foreach (var completion in completions)
                    if (completion.ResultExpression != null)
                        Verify(
                            context,
                            session,
                            completion,
                            ConditionalImplication(completion.ResultExpression, !maybeNullWhen, target + " != null"),
                            SharpProofDiagnostics.NullableParameterPostconditionViolationRule,
                            "parameter-non-null-opposite-maybe-null-when",
                            "[MaybeNullWhen(" + maybeNullWhen.ToString().ToLowerInvariant() + ")]",
                            context.MethodSymbol.Name,
                            parameter.Name,
                            "[MaybeNullWhen(" + maybeNullWhen.ToString().ToLowerInvariant() + ")]" );
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
            ? "[MemberNotNullWhen(" + expectedResult.Value.ToString().ToLowerInvariant() + ", \"" +
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
            ? BaselineDiagnosticProperties.Add(
                ImmutableDictionary<string, string?>.Empty
                    .Add(SharpProofDiagnostics.NullableContractKindProperty, "null-forgiving")
                    .Add(SharpProofDiagnostics.NullableContractConditionProperty, condition)
                    .Add(SharpProofDiagnostics.NullableContractTargetProperty, suppression.Operand.ToString())
                    .Add(SharpProofDiagnostics.NullableProofStatusProperty, "Proven")
                    .Add(SharpProofDiagnostics.NullableProofReasonProperty, reason),
                context.MethodSymbol,
                context.Node.SyntaxTree,
                "NullableContract",
                suppression.Operand.ToString(),
                "null-forgiving@" + suppression.SpanStart.ToString(CultureInfo.InvariantCulture))
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
        foreach (var operation in context.State.VisibleOperations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (operation is not ISimpleAssignmentOperation assignment) continue;

            ISymbol? target = assignment.Target switch
            {
                IFieldReferenceOperation field => field.Field.OriginalDefinition,
                IPropertyReferenceOperation property => property.Property.OriginalDefinition,
                _ => null
            };
            if (target != null && SymbolEqualityComparer.Default.Equals(target, member.OriginalDefinition))
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
        var outcome = context.State.QueryService.TryProveAtSyntaxNode(
            context.SemanticModel,
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
        var outcome = context.State.QueryService.TryProveAtSyntaxNode(
            context.SemanticModel,
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

        foreach (var invocation in context.State.VisibleOperations
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

    private static bool IsNullLiteral(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) expression = parenthesized.Expression;
        return expression.IsKind(SyntaxKind.NullLiteralExpression);
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
        var provider = context.Options.AnalyzerConfigOptionsProvider;
        var treeOptions = provider.GetOptions(context.Node.SyntaxTree);
        if (treeOptions.TryGetValue(ConfigKeys.ReportNullableInconclusive, out var treeValue) &&
            AnalyzerConfiguration.TryParseBool(treeValue, out var treeEnabled))
            return treeEnabled;

        return provider.GlobalOptions.TryGetValue(ConfigKeys.ReportNullableInconclusive, out var globalValue) &&
               AnalyzerConfiguration.TryParseBool(globalValue, out var globalEnabled) && globalEnabled;
    }

    private static ImmutableDictionary<string, string?> CreateProperties(
        MethodBodyAnalysisContext context,
        Location location,
        string kind,
        string condition,
        string target,
        SymbolicConditionProofResult proof)
    {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(SharpProofDiagnostics.NullableContractKindProperty, kind)
            .Add(SharpProofDiagnostics.NullableContractConditionProperty, condition)
            .Add(SharpProofDiagnostics.NullableContractTargetProperty, target)
            .Add(SharpProofDiagnostics.NullableProofStatusProperty, proof.Proof.Status.ToString())
            .Add(SharpProofDiagnostics.NullableProofReasonProperty, proof.Reason);
        properties = BaselineDiagnosticProperties.Add(
            properties,
            context.MethodSymbol,
            context.Node.SyntaxTree,
            "NullableContract",
            target,
            kind + "@" + location.SourceSpan.Start.ToString(CultureInfo.InvariantCulture));
        return ExplainDiagnosticProperties.Add(
            properties,
            location,
            target,
            proof.Proof.Status.ToString(),
            proof.Proof.UnknownReason.ToString(),
            condition);
    }

    private static ImmutableArray<NormalCompletion> CollectNormalCompletions(MethodBodyAnalysisContext context)
    {
        var builder = ImmutableArray.CreateBuilder<NormalCompletion>();
        foreach (var operation in context.State.VisibleOperations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (operation is not IReturnOperation returnOperation ||
                IsCompilerMarkedUnreachable(operation.Syntax, context))
                continue;

            var expression = returnOperation.ReturnedValue?.Syntax as ExpressionSyntax;
            builder.Add(new NormalCompletion(
                expression,
                expression?.GetLocation() ?? operation.Syntax.GetLocation(),
                operation.Syntax,
                false));
        }

        if (TryGetExpressionBody(context.Node, out var expressionBody))
            builder.Add(new NormalCompletion(
                HasResultValue(context.MethodSymbol) ? expressionBody : null,
                expressionBody.GetLocation(),
                expressionBody,
                !HasResultValue(context.MethodSymbol)));
        else if (TryGetBody(context.Node, out var body) &&
                 context.SemanticModel.AnalyzeControlFlow(body) is { Succeeded: true, EndPointIsReachable: true })
            builder.Add(new NormalCompletion(null, body.CloseBraceToken.GetLocation(), body, true));

        return builder
            .GroupBy(static completion => completion.QueryNode.SpanStart)
            .Select(static group => group.First())
            .ToImmutableArray();
    }

    private static bool IsCompilerMarkedUnreachable(SyntaxNode syntax, MethodBodyAnalysisContext context)
    {
        return context.SemanticModel.GetDiagnostics(syntax.Span, context.CancellationToken)
            .Any(static diagnostic => diagnostic.Id == "CS0162");
    }

    private static bool TryGetExpressionBody(SyntaxNode node, out ExpressionSyntax expression)
    {
        expression = node switch
        {
            MethodDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } value } => value,
            ConstructorDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            OperatorDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            ConversionOperatorDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            AccessorDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            PropertyDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            IndexerDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            _ => null!
        };
        return expression != null;
    }

    private static bool TryGetBody(SyntaxNode node, out BlockSyntax body)
    {
        body = node switch
        {
            MethodDeclarationSyntax method => method.Body!,
            LocalFunctionStatementSyntax local => local.Body!,
            ConstructorDeclarationSyntax constructor => constructor.Body!,
            OperatorDeclarationSyntax op => op.Body!,
            ConversionOperatorDeclarationSyntax conversion => conversion.Body!,
            AccessorDeclarationSyntax accessor => accessor.Body!,
            _ => null!
        };
        return body != null;
    }

    private static bool HasResultValue(IMethodSymbol method)
    {
        return method.MethodKind is not (MethodKind.Constructor or MethodKind.StaticConstructor) &&
               !method.ReturnsVoid;
    }

    private static string ConditionalImplication(ExpressionSyntax result, bool expected, string consequence)
    {
        return Parenthesize(result) + " != " + expected.ToString().ToLowerInvariant() + " || " + consequence;
    }

    private static string Parenthesize(ExpressionSyntax expression) => "(" + expression.WithoutTrivia() + ")";

    private static string EscapeIdentifier(string identifier) => "@" + identifier;

    private readonly record struct NormalCompletion(
        ExpressionSyntax? ResultExpression,
        Location Location,
        SyntaxNode QueryNode,
        bool IncludeCurrentStatementCompletionFacts);
}
