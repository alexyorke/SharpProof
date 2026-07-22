namespace SharpProof.Symbolic;

internal sealed class SymbolicComplexityCallModel(
    Compilation _compilation,
    SymbolicComplexityCostModel _costModel,
    Func<IMethodSymbol, SyntaxNode, SemanticModel, MethodAnalysisSummary> _analyzeMethod,
    CancellationToken _cancellationToken) {
    private IMethodSymbol? ResolveInvocationTargetMethod(
        InvocationExpressionSyntax invocationSyntax,
        SemanticModel semanticModel,
        out IInvocationOperation? invocationOperation) {
        invocationOperation =
            semanticModel.GetOperation(invocationSyntax, _cancellationToken) as IInvocationOperation;
        var invocationSymbolInfo = semanticModel.GetSymbolInfo(invocationSyntax, _cancellationToken);
        var expressionSymbolInfo = semanticModel.GetSymbolInfo(invocationSyntax.Expression, _cancellationToken);
        return invocationOperation?.TargetMethod ??
               invocationSymbolInfo.Symbol as IMethodSymbol ??
               expressionSymbolInfo.Symbol as IMethodSymbol ??
               invocationSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault() ??
               expressionSymbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }
    internal ComplexityArtifacts AnalyzeTopLevelInvocations(SyntaxNode bodyNode, SemanticModel semanticModel, IMethodSymbol currentMethod) {
        var invocationCosts = new List<ComplexityArtifacts>();
        foreach (var invocation in EnumerateTopLevelInvocationTargets(bodyNode, semanticModel)) {
            var (invocationSyntax, invocationOperation, targetMethod) = invocation;
            if (targetMethod == null)
                return ComplexityArtifacts.Unknown(SymbolicComplexityUnknownReason.UnknownCallee, invocationSyntax);

            invocationCosts.Add(AnalyzeMethodCall(
                targetMethod,
                invocationOperation,
                invocationSyntax,
                semanticModel,
                currentMethod,
                invocationOperation != null
                    ? GetArgumentSyntaxes(targetMethod, invocationOperation.Arguments)
                    : [.. invocationSyntax.ArgumentList.Arguments.Select(static argument => (SyntaxNode)argument.Expression)],
                invocationSyntax.Expression is MemberAccessExpressionSyntax memberAccess
                    ? memberAccess.Expression
                    : null));
        }
        return SymbolicComplexityAlgebra.CombineSequence(invocationCosts);
    }
    private IEnumerable<(
        InvocationExpressionSyntax Syntax,
        IInvocationOperation? Operation,
        IMethodSymbol? TargetMethod)> EnumerateTopLevelInvocationTargets(SyntaxNode bodyNode, SemanticModel semanticModel) {
        foreach (var invocationSyntax in bodyNode.DescendantNodes(static candidate =>
                         !CSharpSyntaxFacts.IsNestedLocalCallableBoundary(candidate))
                     .OfType<InvocationExpressionSyntax>()) {
            var targetMethod =
                ResolveInvocationTargetMethod(invocationSyntax, semanticModel, out var invocationOperation);
            yield return (invocationSyntax, invocationOperation, targetMethod);
        }
    }
    internal static ImmutableArray<SyntaxNode> GetArgumentSyntaxes(IMethodSymbol method, ImmutableArray<IArgumentOperation> arguments) {
        if (arguments.IsDefaultOrEmpty) return [];

        // Callee factors are parameter-ordinal based. Roslyn includes implicit optional and
        // expanded params arguments in the operation list, while source ArgumentList syntax does not.
        return [.. arguments
            .OrderBy(argument => argument.Parameter?.Ordinal ?? method.Parameters.Length)
            .Select(static argument => argument.Value.Syntax)];
    }
    internal ComplexityArtifacts AnalyzeMethodCall(
        IMethodSymbol methodSymbol,
        IOperation? operation,
        SyntaxNode syntax,
        SemanticModel semanticModel,
        IMethodSymbol currentMethod,
        ImmutableArray<SyntaxNode> argumentSyntaxes,
        SyntaxNode? receiverSyntax) {
        if (TryGetKnownMethodCost(methodSymbol, out var knownCost))
            return ComplexityArtifacts.FromCost(
                knownCost,
                calleeSummaries: new[] {
                    SymbolicComplexityAlgebra.CreateCalleeInfo(
                        methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        knownCost,
                        currentMethod)
                });

        if (operation != null &&
            SymbolicDispatchFacts.ShouldTreatAsDynamicDispatch(methodSymbol, operation))
            return CreateUnknownCalleeArtifacts(methodSymbol, SymbolicComplexityUnknownReason.DynamicDispatch, syntax);

        if (!SymbolicMethodSourceResolver.IsBackedBySource(methodSymbol))
            return CreateUnknownCalleeArtifacts(methodSymbol, SymbolicComplexityUnknownReason.ExternalCallee, syntax);

        if (!TryResolveSourceMethod(methodSymbol, out var declaration, out var bodyNode, out var sourceModel))
            return CreateUnknownCalleeArtifacts(methodSymbol, SymbolicComplexityUnknownReason.UnknownCallee, syntax);

        var calleeSummary = _analyzeMethod(methodSymbol, bodyNode, sourceModel);
        var substitutionResult = SubstituteCalleeCost(calleeSummary.Cost, argumentSyntaxes, receiverSyntax, semanticModel, currentMethod);
        var calleeInfo = SymbolicComplexityAlgebra.CreateCalleeInfo(
            methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
            substitutionResult.Cost,
            currentMethod);
        var drivers = new List<SymbolicComplexityDriverInfo>(substitutionResult.Drivers.Count + 1);
        drivers.AddRange(substitutionResult.Drivers);
        if (!substitutionResult.Cost.IsConstant)
            drivers.Add(SymbolicComplexityAlgebra.CreateDriver(
                "Call",
                "call to " + calleeInfo.MethodDisplayName + " contributes " + calleeInfo.ComplexityText,
                syntax));

        return ComplexityArtifacts.FromCost(
            substitutionResult.Cost,
            drivers.Concat(calleeSummary.Drivers),
            substitutionResult.UnknownReasons.Concat(calleeSummary.UnknownReasons),
            new[] { calleeInfo }.Concat(calleeSummary.CalleeSummaries));
    }
    private ComplexityArtifacts CreateUnknownCalleeArtifacts(
        IMethodSymbol methodSymbol,
        SymbolicComplexityUnknownReason reason,
        SyntaxNode syntax) => ComplexityArtifacts.Unknown(
            reason,
            syntax,
            parts: null,
            calleeSummaries: new[] {
                new SymbolicComplexityCalleeInfo(
                    methodSymbol.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    "Unknown",
                    SymbolicComplexityKind.Unknown,
                    true,
                    reason)
            });
    private bool TryResolveSourceMethod(
        IMethodSymbol methodSymbol,
        out SyntaxNode declaration,
        out SyntaxNode bodyNode,
        out SemanticModel semanticModel) {
        if (SymbolicMethodSourceResolver.TryResolve(
                _compilation,
                methodSymbol,
                static _ => true,
                false,
                _cancellationToken,
                out declaration,
                out var body,
                out semanticModel) &&
            body != null) {
            bodyNode = body;
            return true;
        }
        bodyNode = null!;
        return false;
    }
    private static bool TryGetKnownMethodCost(IMethodSymbol methodSymbol, out SymbolicCostExpression cost) {
        cost = SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.Unknown);
        if (methodSymbol.MethodKind == MethodKind.PropertyGet &&
            methodSymbol.AssociatedSymbol is IPropertySymbol property) {
            if (property.IsIndexer &&
                methodSymbol.Parameters.Length <= 1) {
                cost = SymbolicCostExpression.Constant();
                return true;
            }
            if ((string.Equals(property.Name, "Length", StringComparison.Ordinal) ||
                 string.Equals(property.Name, "Count", StringComparison.Ordinal)) &&
                SymbolicComplexityCostModel.IsKnownSizedType(property.ContainingType)) {
                cost = SymbolicCostExpression.Constant();
                return true;
            }
        }
        return false;
    }
    private SubstitutionResult SubstituteCalleeCost(
        SymbolicCostExpression cost,
        ImmutableArray<SyntaxNode> argumentSyntaxes,
        SyntaxNode? receiverSyntax,
        SemanticModel callerSemanticModel,
        IMethodSymbol callerMethod) {
        if (cost.IsUnknown || cost.IsRecursiveUnknown)
            return new SubstitutionResult(cost, Array.Empty<SymbolicComplexityDriverInfo>(),
                Array.Empty<SymbolicComplexityUnknownReason>());

        SymbolicCostExpression? ResolveFactor(string key) {
            if (TryParseParameterKey(key, out var parameterIndex, out var projection)) {
                if (parameterIndex < 0 || parameterIndex >= argumentSyntaxes.Length)
                    return SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);

                return _costModel.TryCreate(
                    argumentSyntaxes[parameterIndex] as ExpressionSyntax,
                    callerSemanticModel,
                    callerMethod,
                    projection,
                    true,
                    out var expressionCost)
                    ? expressionCost
                    : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);
            }
            if (string.Equals(key, "$this.length", StringComparison.Ordinal))
                return _costModel.TryCreate(
                    receiverSyntax as ExpressionSyntax,
                    callerSemanticModel,
                    callerMethod,
                    CostProjection.LengthOrCount,
                    false,
                    out var receiverCost)
                    ? receiverCost
                    : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);

            if (string.Equals(key, "$this", StringComparison.Ordinal))
                return _costModel.TryCreate(
                    receiverSyntax as ExpressionSyntax,
                    callerSemanticModel,
                    callerMethod,
                    CostProjection.Value,
                    true,
                    out var receiverCost)
                    ? receiverCost
                    : SymbolicCostExpression.Unknown(SymbolicComplexityUnknownReason.UnknownCallee);

            return null;
        }
        var substituted = cost.Substitute(ResolveFactor);
        var reasons = substituted.IsUnknown
            ? [
                substituted.UnknownReason == SymbolicComplexityUnknownReason.None
                    ? SymbolicComplexityUnknownReason.UnknownCallee
                    : substituted.UnknownReason
            ]
            : Array.Empty<SymbolicComplexityUnknownReason>();
        return new SubstitutionResult(substituted, Array.Empty<SymbolicComplexityDriverInfo>(), reasons);
    }
    private static bool TryParseParameterKey(string key, out int parameterIndex, out CostProjection projection) {
        parameterIndex = -1;
        projection = CostProjection.Value;
        if (!key.StartsWith("$p", StringComparison.Ordinal)) return false;

        var suffixStart = key.IndexOf(':');
        if (suffixStart < 0) return false;

        if (!int.TryParse(key.Substring(2, suffixStart - 2), NumberStyles.None, CultureInfo.InvariantCulture,
            out parameterIndex)) return false;

        var suffix = key.Substring(suffixStart + 1);
        projection = string.Equals(suffix, "length", StringComparison.Ordinal)
            ? CostProjection.LengthOrCount
            : CostProjection.Value;
        return true;
    }
}
