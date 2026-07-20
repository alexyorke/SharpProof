namespace SharpProof.Analyzer.Engine.Rules;

internal partial class ReturnStatementPurityRule {
    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState) {
        if (!(operation is IReturnOperation returnOperation)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        if (returnOperation.ReturnedValue == null) return PurityAnalysisEngine.PurityAnalysisResult.Pure;


        if (returnOperation.ReturnedValue != null) {
            var sourceReturnedValue =
                GetSourceReturnedValueOperation(returnOperation, context.SemanticModel, context.CancellationToken) ??
                returnOperation.ReturnedValue;
            var valueResult =
                PurityAnalysisEngine.CheckSingleOperation(returnOperation.ReturnedValue, context, currentState);
            if (!valueResult.IsPure) return valueResult;

            if (IsAwaiterFactoryReturn(
                    context.ContainingMethodSymbol,
                    sourceReturnedValue,
                    context.SemanticModel.Compilation))
                return valueResult;

            if (IsSpanToArrayReturn(sourceReturnedValue, out var spanToArrayMethod))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    returnOperation.ReturnedValue.Syntax,
                    spanToArrayMethod,
                    "returned_span_to_array");

            if (IsAllowedTrustedArrayReturn(
                    sourceReturnedValue,
                    context.SemanticModel,
                    context.CancellationToken,
                    out var trustedArrayReturnSymbol))
                return valueResult;

            if (IsKnownPureArrayFactoryReturn(sourceReturnedValue, context.SemanticModel.Compilation,
                    out var factoryMethod))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    returnOperation.ReturnedValue.Syntax,
                    factoryMethod,
                    "returned_array_factory");

            if (IsPureArrayReturningInvocationReturn(sourceReturnedValue, out var arrayReturningMethod))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    returnOperation.ReturnedValue.Syntax,
                    arrayReturningMethod,
                    "returned_known_pure_array");

            if (IsOwnedLocalArrayReturn(sourceReturnedValue, currentState, out var localSymbol))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    returnOperation.ReturnedValue.Syntax,
                    localSymbol,
                    "owned_local_array_return");

            if (TryFindReturnedDelegateCapture(
                    sourceReturnedValue,
                    context,
                    currentState,
                    ReturnedDelegateCaptureKind.OwnedLocalArray,
                    out var delegateCaptureSyntax,
                    out var delegateCapturedArrayLocal))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    delegateCaptureSyntax,
                    delegateCapturedArrayLocal,
                    "escaping_closure_owned_array_capture");

            if (TryFindReturnedDelegateCapture(
                    sourceReturnedValue,
                    context,
                    currentState,
                    ReturnedDelegateCaptureKind.FreshMutableObject,
                    out var objectDelegateCaptureSyntax,
                    out var objectDelegateCapturedLocal))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    objectDelegateCaptureSyntax,
                    objectDelegateCapturedLocal,
                    "escaping_closure_fresh_mutable_object_capture");

            if (TryGetCallerOwnedArrayViewReturn(
                    sourceReturnedValue,
                    currentState,
                    context.SemanticModel,
                    ArrayViewKind.ReadOnlyCollection,
                    context.CancellationToken,
                    out var readOnlyCollectionMethod))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    returnOperation.ReturnedValue.Syntax,
                    readOnlyCollectionMethod,
                    "returned_array_read_only_view");

            if (IsListAsReadOnlyReturn(sourceReturnedValue, out var listAsReadOnlyMethod))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    returnOperation.ReturnedValue.Syntax,
                    listAsReadOnlyMethod,
                    "returned_list_read_only_view");

            if (TryGetCallerOwnedArrayViewReturn(
                    sourceReturnedValue,
                    currentState,
                    context.SemanticModel,
                    ArrayViewKind.Span,
                    context.CancellationToken,
                    out var spanMethod))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    returnOperation.ReturnedValue.Syntax,
                    spanMethod,
                    "returned_array_span_view");

            if (TryGetCallerOwnedArrayViewReturn(
                    sourceReturnedValue,
                    currentState,
                    context.SemanticModel,
                    ArrayViewKind.Memory,
                    context.CancellationToken,
                    out var memoryConstructor))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    returnOperation.ReturnedValue.Syntax,
                    memoryConstructor,
                    "returned_array_memory_view");

            if (TryFindReturnedInitializerArrayEscape(
                    returnOperation.ReturnedValue,
                    currentState,
                    context.SemanticModel,
                    context.CancellationToken,
                    out var escapeSyntax,
                    out var escapeSymbol,
                    out var catalogSource))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    escapeSyntax,
                    escapeSymbol,
                    catalogSource);

            if (TryFindReturnedInitializerMutableObjectEscape(
                    returnOperation.ReturnedValue,
                    context.SemanticModel,
                    context.CancellationToken,
                    out var nestedObjectEscapeSyntax,
                    out var nestedObjectEscapeSymbol,
                    out var nestedObjectCatalogSource))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    nestedObjectEscapeSyntax,
                    nestedObjectEscapeSymbol,
                    nestedObjectCatalogSource);

            if (TryFindMutableCollectionReturnEscape(
                    returnOperation.ReturnedValue,
                    context.SemanticModel,
                    context.CancellationToken,
                    out var collectionEscapeSyntax,
                    out var collectionEscapeSymbol,
                    out var collectionCatalogSource))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    collectionEscapeSyntax,
                    collectionEscapeSymbol,
                    collectionCatalogSource);

            if (TryFindFreshMutableObjectReturnEscape(
                    returnOperation.ReturnedValue,
                    context.SemanticModel,
                    currentState,
                    context.CancellationToken,
                    out var objectEscapeSyntax,
                    out var objectEscapeSymbol,
                    out var objectCatalogSource))
                return CreateMutableStateEscapeResult(
                    returnOperation,
                    objectEscapeSyntax,
                    objectEscapeSymbol,
                    objectCatalogSource);

            return valueResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CreateMutableStateEscapeResult(
        IReturnOperation returnOperation,
        SyntaxNode escapeSyntax,
        ISymbol escapeSymbol,
        string catalogSource) => PurityAnalysisEngine.PurityAnalysisResult.Impure(
            escapeSyntax,
            PurityResourceStateFacts.CreateReturnEscapeEvidence(
                returnOperation,
                escapeSyntax,
                escapeSymbol,
                nameof(ReturnStatementPurityRule),
                catalogSource));

    private static bool NoReturnEscape(
        out SyntaxNode escapeSyntax,
        out ISymbol escapeSymbol,
        out string catalogSource) {
        escapeSyntax = null!;
        escapeSymbol = null!;
        catalogSource = string.Empty;
        return false;
    }

    private delegate bool ReturnedValueMatcher<TResult>(IOperation? operation, out TResult result);
}
