namespace SharpProof.Analyzer;
internal static class NullableContractAnalyzer {
    internal static void Analyze(MethodBodyAnalysisContext context, AnalyzerSession session) {
        if (context.Snapshot.RootOperation == null) return;
        var completions = MethodCompletionAnalysis.Collect(context, distinctByQueryPosition: true);
        if (completions.Length != 0) {
            var contract = DecodeContract(context.MethodSymbol, context.CancellationToken);
            foreach (var obligation in CreateObligations(context, contract, completions))
                Verify(context, session, obligation);
        }
        AuditNullForgivingOperators(context, session);
    }
    private static NullableMethodContract DecodeContract(IMethodSymbol method, CancellationToken cancellationToken) {
        var sources = MethodContractHierarchy.EnumerateSources(method, cancellationToken).ToImmutableArray();
        var returnContracts = ImmutableArray.CreateBuilder<ConditionalReturnContract>();
        var requiresReturn = false;
        if (!method.ReturnsVoid && method.ReturnType.SpecialType != SpecialType.System_Void) {
            requiresReturn = sources.Any(source =>
                NullableFlowFacts.GetMethodBodyReturnState(source, method.IsAsync) == NullableFlowFactState.NotNull);
            var seenOrdinals = new HashSet<int>();
            foreach (var source in sources)
                foreach (var sourceName in NullableFlowFacts.GetNotNullIfNotNullParameterNames(source)) {
                    cancellationToken.ThrowIfCancellationRequested();
                    var sourceParameter = source.Parameters.FirstOrDefault(parameter =>
                        string.Equals(parameter.Name, sourceName, StringComparison.Ordinal));
                    if (sourceParameter == null ||
                        sourceParameter.Ordinal < 0 ||
                        sourceParameter.Ordinal >= method.Parameters.Length ||
                        method.Parameters[sourceParameter.Ordinal].RefKind == RefKind.Out ||
                        !seenOrdinals.Add(sourceParameter.Ordinal))
                        continue;
                    returnContracts.Add(new ConditionalReturnContract(
                        sourceName,
                        method.Parameters[sourceParameter.Ordinal].Name));
                }
        }
        var parameterContracts = ImmutableArray.CreateBuilder<ParameterContract>();
        foreach (var parameter in method.Parameters) {
            cancellationToken.ThrowIfCancellationRequested();
            var requiresNonNull = sources.Any(source =>
                parameter.Ordinal < source.Parameters.Length &&
                NullableFlowFacts.HasNotNullPostcondition(source.Parameters[parameter.Ordinal]));
            var conditional = ImmutableArray.CreateBuilder<ConditionalParameterContract>();
            AddConditionalValues(false);
            AddConditionalValues(true);
            if (requiresNonNull || conditional.Count != 0)
                parameterContracts.Add(new ParameterContract(parameter, requiresNonNull, conditional.ToImmutable()));
            void AddConditionalValues(bool maybeNull) {
                var seen = new HashSet<bool>();
                foreach (var source in sources) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (parameter.Ordinal >= source.Parameters.Length) continue;
                    var sourceParameter = source.Parameters[parameter.Ordinal];
                    var value = false;
                    var found = maybeNull
                        ? sourceParameter.NullableAnnotation == NullableAnnotation.NotAnnotated &&
                          NullableFlowFacts.TryGetMaybeNullWhenValue(sourceParameter, out value)
                        : NullableFlowFacts.TryGetNotNullWhenValue(sourceParameter, out value);
                    if (found && seen.Add(value))
                        conditional.Add(new ConditionalParameterContract(
                            maybeNull ? !value : value,
                            FormatBooleanAttribute(maybeNull ? "MaybeNullWhen" : "NotNullWhen", value)));
                }
            }
        }
        var memberContracts = ImmutableArray.CreateBuilder<MemberContract>();
        if (!method.IsStatic && method.ContainingType != null) {
            AddMembers(null);
            AddMembers(false);
            AddMembers(true);
        }
        return new NullableMethodContract(
            requiresReturn,
            returnContracts.ToImmutable(),
            parameterContracts.ToImmutable(),
            memberContracts.ToImmutable());
        void AddMembers(bool? expectedResult) {
            var seen = new HashSet<ISymbol>(SymbolEq.Default);
            foreach (var source in sources) {
                var targetNames = expectedResult.HasValue
                    ? NullableFlowFacts.GetMemberNotNullWhenTargets(source, expectedResult.Value)
                    : NullableFlowFacts.GetMemberNotNullTargets(source);
                foreach (var targetName in targetNames)
                    if (source.ContainingType != null &&
                        NullableFlowFacts.TryResolveInstanceMemberTarget(
                            source.ContainingType,
                            targetName,
                            out var member) &&
                        seen.Add(member.OriginalDefinition))
                        memberContracts.Add(new MemberContract(
                            targetName,
                            member,
                            source.ContainingType,
                            expectedResult));
            }
        }
    }
    private static IEnumerable<CompletionObligation> CreateObligations(
        MethodBodyAnalysisContext context,
        NullableMethodContract contract,
        ImmutableArray<MethodNormalCompletion> completions) {
        var method = context.MethodSymbol;
        var returnRule = AnalyzerDiagnosticCatalog.Get("NullableReturnContractViolationRule");
        foreach (var completion in completions) {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (completion.ResultExpression == null) continue;
            var result = Parenthesize(completion.ResultExpression);
            if (contract.RequiresNonNullReturn &&
                !NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                    completion.ResultExpression,
                    context.SemanticModel,
                    context.CancellationToken))
                yield return new CompletionObligation(
                    completion,
                    result + " is not null",
                    returnRule,
                    [method.Name, "non-null return"]);
            foreach (var conditional in contract.ConditionalReturns)
                yield return new CompletionObligation(
                    completion,
                    "old(" + EscapeIdentifier(conditional.ImplementationParameterName) + ") is null || " +
                    result + " is not null",
                    returnRule,
                    [method.Name, "[NotNullIfNotNull(\"" + conditional.SourceParameterName + "\")]"],
                    CSharpSyntaxFacts.IsNullLiteral(completion.ResultExpression),
                    true);
        }
        var parameterRule = AnalyzerDiagnosticCatalog.Get("NullableParameterPostconditionViolationRule");
        foreach (var parameterContract in contract.Parameters) {
            var parameter = parameterContract.Parameter;
            var target = EscapeIdentifier(parameter.Name) + " is not null";
            if (parameterContract.RequiresNonNull)
                foreach (var completion in completions)
                    if (!ParameterIsDefinitelyNotNullAtCompletion(context, completion, parameter))
                        yield return new CompletionObligation(
                            completion,
                            target,
                            parameterRule,
                            [method.Name, parameter.Name, "[NotNull]"]);
            foreach (var conditional in parameterContract.Conditional)
                foreach (var completion in completions)
                    if (completion.ResultExpression != null &&
                        ConditionalContractCanApply(context, completion.ResultExpression, conditional.ExpectedResult) &&
                        !ParameterIsDefinitelyNotNullAtCompletion(context, completion, parameter) &&
                        !DelegatedInvocationGuaranteesNonNullOutput(
                            context,
                            completion.ResultExpression,
                            parameter,
                            conditional.ExpectedResult))
                        yield return new CompletionObligation(
                            completion,
                            ConditionalImplication(
                                completion.ResultExpression,
                                conditional.ExpectedResult,
                                target),
                            parameterRule,
                            [method.Name, parameter.Name, conditional.Display]);
        }
        var memberRule = AnalyzerDiagnosticCatalog.Get("NullableMemberContractViolationRule");
        foreach (var memberContract in contract.Members) {
            var member = memberContract.Member;
            if (member is IPropertySymbol property && !IsAutoProperty(property, context.CancellationToken)) {
                context.ReportDiagnostic(Diagnostic.Create(
                    AnalyzerDiagnosticCatalog.Get("NullableContractNotVerifiedRule"),
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(context.Node),
                    method.Name,
                    memberContract.TargetName,
                    "user-defined property getters are not stable storage"));
                continue;
            }
            var target = FormatMemberTarget(method, member, memberContract.ContainingType);
            var expected = memberContract.ExpectedResult;
            var display = expected.HasValue
                ? "[MemberNotNullWhen(" + FormatBoolean(expected.Value) + ", \"" +
                  memberContract.TargetName + "\")]"
                : "[MemberNotNull(\"" + memberContract.TargetName + "\")]";
            var unknownIsViolation = member is IFieldSymbol &&
                NullableFlowFacts.TryGetMemberType(member, out var memberType) &&
                memberType.NullableAnnotation == NullableAnnotation.Annotated &&
                !HasVisibleAssignmentToMember(context, member);
            foreach (var completion in completions) {
                if (expected.HasValue && completion.ResultExpression == null) continue;
                yield return new CompletionObligation(
                    completion,
                    expected.HasValue
                        ? ConditionalImplication(completion.ResultExpression!, expected.Value, target)
                        : target,
                    memberRule,
                    [method.Name, memberContract.TargetName, display],
                    unknownIsViolation);
            }
        }
    }
    private static bool ParameterIsDefinitelyNotNullAtCompletion(
        MethodBodyAnalysisContext context,
        MethodNormalCompletion completion,
        IParameterSymbol parameter) =>
        NullableFlowFacts.GetExpressionStateAtPosition(
            SyntaxFactory.ParseExpression(EscapeIdentifier(parameter.Name)),
            completion.QueryNode.SpanStart,
            context.SemanticModel,
            context.CancellationToken) == NullableFlowFactState.NotNull;
    private static bool ConditionalContractCanApply(
        MethodBodyAnalysisContext context,
        ExpressionSyntax resultExpression,
        bool expectedResult) {
        var constant = context.SemanticModel.GetConstantValue(
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(resultExpression),
            context.CancellationToken);
        return constant is not { HasValue: true, Value: bool value } || value == expectedResult;
    }
    private static bool DelegatedInvocationGuaranteesNonNullOutput(
        MethodBodyAnalysisContext context,
        ExpressionSyntax resultExpression,
        IParameterSymbol callerParameter,
        bool methodReturnValue) {
        resultExpression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(resultExpression);
        if (context.SemanticModel.GetOperation(resultExpression, context.CancellationToken) is not
                IInvocationOperation invocation ||
            invocation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Boolean)
            return false;
        foreach (var argument in invocation.Arguments) {
            if (argument is not
                {
                    ArgumentKind: ArgumentKind.Explicit,
                    Parameter: { RefKind: RefKind.Ref or RefKind.Out } calleeParameter,
                    Syntax: ArgumentSyntax syntax
                } ||
                !SymbolicFrameworkPostconditionLowerer.ArgumentRefKindMatches(calleeParameter, syntax) ||
                !SymbolicFrameworkPostconditionLowerer.IsUniqueOutputArgumentTarget(
                    invocation,
                    argument,
                    context.SemanticModel,
                    context.CancellationToken) ||
                !NullableFlowFacts.TryGetArgumentTargetSymbol(
                    syntax.Expression,
                    context.SemanticModel,
                    context.CancellationToken,
                    out var target) ||
                !SymbolEq.AreEqual(target, callerParameter))
                continue;
            if (NullableFlowFacts.GetParameterOutputState(calleeParameter, methodReturnValue) ==
                NullableFlowFactState.NotNull)
                return true;
            return MethodContractHierarchy
                .EnumerateSources(invocation.TargetMethod, context.CancellationToken)
                .Any(source =>
                    calleeParameter.Ordinal < source.Parameters.Length &&
                    NullableFlowFacts.GetParameterOutputState(
                        source.Parameters[calleeParameter.Ordinal],
                        methodReturnValue) == NullableFlowFactState.NotNull);
        }
        return false;
    }
    private static string FormatMemberTarget(
        IMethodSymbol method,
        ISymbol member,
        INamedTypeSymbol contractContainingType) {
        var receiver = SymbolEq.AreEqual(method.ContainingType, contractContainingType)
            ? "this"
            : "((" + contractContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")this)";
        return receiver + "." + EscapeIdentifier(member.Name) + " is not null";
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
    private static void Verify(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        CompletionObligation obligation) {
        var proof = MethodCompletionAnalysis.Prove(
            context,
            session.SmtAnalysis,
            obligation.Completion,
            obligation.Condition);
        if (proof.TruthValue is SymbolicTruthValue.ProvenTrue or SymbolicTruthValue.Unreachable) return;
        if (proof.TruthValue == SymbolicTruthValue.ProvenFalse ||
            obligation.ExactCounterexampleIsViolation &&
            proof.CounterexampleWitness.Status == SymbolicWitnessStatus.Exact ||
            obligation.UnknownIsViolation) {
            context.ReportDiagnostic(Diagnostic.Create(
                obligation.ViolationDescriptor,
                obligation.Completion.Location,
                obligation.MessageArguments));
            return;
        }
        context.ReportDiagnostic(Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("NullableContractNotVerifiedRule"),
            obligation.Completion.Location,
            context.MethodSymbol.Name,
            obligation.Condition,
            ContractDiagnosticSupport.FormatUnknownReason(proof, "nullable contract")));
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
            var condition = Parenthesize(operand) + " is not null";
            if (IsStaticallyNonNullInput(operand, context)) continue;
            var roslynState = NullableFlowFacts.GetExpressionStateAtPosition(
                operand,
                suppression.SpanStart,
                context.SemanticModel,
                context.CancellationToken);
            if (roslynState == NullableFlowFactState.NotNull) continue;
            var proof = context.State.ProveAtNode(
                suppression,
                condition,
                session.SmtAnalysis,
                false,
                context.CancellationToken);
            if (proof.TruthValue is SymbolicTruthValue.ProvenTrue or SymbolicTruthValue.Unreachable) continue;
            if (proof.TruthValue == SymbolicTruthValue.ProvenFalse) {
                ReportUnsafeSuppression(context, suppression);
                continue;
            }
            if (proof.CounterexampleWitness.Status == SymbolicWitnessStatus.Exact &&
                CanUseSuppressionCounterexample(operand, context)) {
                ReportUnsafeSuppression(context, suppression);
                continue;
            }
            if (roslynState == NullableFlowFactState.MaybeNull &&
                CanUseSuppressionCounterexample(operand, context))
                ReportUnsafeSuppression(context, suppression);
        }
    }
    private static void ReportUnsafeSuppression(
        MethodBodyAnalysisContext context,
        PostfixUnaryExpressionSyntax suppression) =>
        context.ReportDiagnostic(Diagnostic.Create(
            AnalyzerDiagnosticCatalog.Get("UnsafeNullForgivingOperatorRule"),
            suppression.OperatorToken.GetLocation(),
            suppression.Operand.ToString()));
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
    private static bool IsStaticallyNonNullInput(ExpressionSyntax expression, MethodBodyAnalysisContext context) =>
        context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol is
            IParameterSymbol { NullableAnnotation: NullableAnnotation.NotAnnotated } &&
        NullableFlowFacts.GetExpressionState(
            expression,
            context.SemanticModel,
            context.CancellationToken) == NullableFlowFactState.NotNull;
    private static bool CanUseSuppressionCounterexample(
        ExpressionSyntax expression,
        MethodBodyAnalysisContext context) {
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AsExpression)) return true;
        return context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol switch {
            IParameterSymbol parameter => parameter.NullableAnnotation == NullableAnnotation.Annotated,
            ILocalSymbol local => local.NullableAnnotation == NullableAnnotation.Annotated,
            IFieldSymbol field => field.NullableAnnotation == NullableAnnotation.Annotated,
            IPropertySymbol property => property.NullableAnnotation == NullableAnnotation.Annotated,
            _ => false
        };
    }
    private static string ConditionalImplication(ExpressionSyntax result, bool expected, string consequence) =>
        Parenthesize(result) + " != " + FormatBoolean(expected) + " || " + consequence;
    private static string FormatBooleanAttribute(string name, bool value) =>
        "[" + name + "(" + FormatBoolean(value) + ")]";
    private static string FormatBoolean(bool value) => value ? "true" : "false";
    private static string Parenthesize(ExpressionSyntax expression) => "(" + expression.WithoutTrivia() + ")";
    private static string EscapeIdentifier(string identifier) => "@" + identifier;
    private readonly record struct NullableMethodContract(
        bool RequiresNonNullReturn,
        ImmutableArray<ConditionalReturnContract> ConditionalReturns,
        ImmutableArray<ParameterContract> Parameters,
        ImmutableArray<MemberContract> Members);
    private readonly record struct ConditionalReturnContract(
        string SourceParameterName,
        string ImplementationParameterName);
    private readonly record struct ParameterContract(
        IParameterSymbol Parameter,
        bool RequiresNonNull,
        ImmutableArray<ConditionalParameterContract> Conditional);
    private readonly record struct ConditionalParameterContract(bool ExpectedResult, string Display);
    private readonly record struct MemberContract(
        string TargetName,
        ISymbol Member,
        INamedTypeSymbol ContainingType,
        bool? ExpectedResult);
    private readonly record struct CompletionObligation(
        MethodNormalCompletion Completion,
        string Condition,
        DiagnosticDescriptor ViolationDescriptor,
        object[] MessageArguments,
        bool UnknownIsViolation = false,
        bool ExactCounterexampleIsViolation = false);
}
