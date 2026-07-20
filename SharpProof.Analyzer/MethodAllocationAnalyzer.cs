namespace SharpProof.Analyzer;

internal static class MethodAllocationAnalyzer {
    private static readonly SymbolDisplayFormat AllocationSymbolDisplayFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static void AnalyzeSymbolForZeroAllocations(
        MethodBodyAnalysisContext context,
        DiagnosticBaseline baseline,
        SharpProofAttributeIdentityPolicy attributePolicy) {
        var methodSymbol = context.MethodSymbol;

        if (PurityAnalysisEngine.IsMetadataSymbol(methodSymbol)) return;

        var hasZeroAllocationsAttribute = MethodContractHierarchy
            .EnumerateSources(methodSymbol, context.CancellationToken)
            .Any(source => attributePolicy.HasAttribute(source, "ZeroAllocationsAttribute"));
        if (!hasZeroAllocationsAttribute) return;

        if (context.Snapshot.RootOperation == null) return;

        foreach (var allocationSite in CollectAllocationSites(context.Snapshot.VisibleOperations)) {
            var location = allocationSite.Syntax.GetLocation();
            var properties = CreateAllocationProperties(allocationSite, methodSymbol, context.Node.SyntaxTree);
            var diagnostic = Diagnostic.Create(
                AnalyzerDiagnosticCatalog.Get("AllocationInZeroAllocationMethodRule"),
                location,
                null,
                properties,
                new object[] {
                    allocationSite.Syntax.ToString(),
                    methodSymbol.Name
                });
            AnalyzerDiagnosticReporter.ReportIfNotSuppressed(context, baseline, diagnostic);
        }
    }

    internal static bool HasVisibleAllocationSites(MethodAnalysisSnapshot snapshot) =>
        CollectAllocationSites(snapshot.VisibleOperations).Any();

    private static ImmutableDictionary<string, string?> CreateAllocationProperties(
        AllocationSite allocationSite,
        IMethodSymbol methodSymbol,
        SyntaxTree syntaxTree) {
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add("sharpproof.allocation.kind", allocationSite.AllocationKind)
            .Add("sharpproof.allocation.operation_kind", allocationSite.Operation.Kind.ToString());

        if (allocationSite.Symbol != null)
            properties = properties.Add(
                "sharpproof.allocation.symbol",
                allocationSite.Symbol.ToDisplayString(AllocationSymbolDisplayFormat));

        return AnalyzerDiagnosticProperties.AddBaselineAndExplain(
            properties,
            methodSymbol,
            syntaxTree,
            allocationSite.Operation.Kind.ToString(),
            null,
            CreateAllocationEvidenceKey(allocationSite),
            allocationSite.Syntax.GetLocation(),
            "[ZeroAllocations]",
            "violated",
            allocationSite.AllocationKind);
    }

    private static string CreateAllocationEvidenceKey(AllocationSite allocationSite) {
        return DiagnosticEvidenceKey.ForSpanEnd(
            allocationSite.AllocationKind,
            allocationSite.Syntax.SpanStart,
            allocationSite.Syntax.Span.End,
            allocationSite.Symbol?.ToDisplayString(AllocationSymbolDisplayFormat));
    }

    private static IEnumerable<AllocationSite> CollectAllocationSites(IEnumerable<IOperation> operations) {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var operation in operations) {
            if (!TryCreateAllocationSite(operation, out var allocationSite)) continue;

            var key = allocationSite.Syntax.SpanStart +
                      ":" +
                      allocationSite.Syntax.Span.End +
                      ":" +
                      allocationSite.AllocationKind;
            if (seen.Add(key)) yield return allocationSite;
        }
    }

    private static bool TryCreateAllocationSite(IOperation operation, out AllocationSite allocationSite) {
        allocationSite = default;

        switch (operation) {
            case IObjectCreationOperation objectCreationOperation
                when IsHeapAllocatedObjectType(objectCreationOperation.Type):
                allocationSite = new AllocationSite(
                    objectCreationOperation.Syntax,
                    objectCreationOperation,
                    "object_creation",
                    objectCreationOperation.Constructor ?? (ISymbol?)objectCreationOperation.Type);
                return true;

            case ITypeParameterObjectCreationOperation typeParameterObjectCreationOperation
                when IsHeapAllocatedObjectType(typeParameterObjectCreationOperation.Type):
                allocationSite = new AllocationSite(
                    typeParameterObjectCreationOperation.Syntax,
                    typeParameterObjectCreationOperation,
                    "object_creation",
                    typeParameterObjectCreationOperation.Type);
                return true;

            case IArrayCreationOperation arrayCreationOperation
                when !arrayCreationOperation.IsImplicit || IsImplicitParamsArray(arrayCreationOperation):
                allocationSite = new AllocationSite(
                    arrayCreationOperation.Syntax,
                    arrayCreationOperation,
                    arrayCreationOperation.IsImplicit ? "params_array_creation" : "array_creation",
                    arrayCreationOperation.Type);
                return true;

            case IInterpolatedStringOperation interpolatedStringOperation
                when !interpolatedStringOperation.ConstantValue.HasValue:
                allocationSite = new AllocationSite(
                    interpolatedStringOperation.Syntax,
                    interpolatedStringOperation,
                    "string_construction",
                    interpolatedStringOperation.Type);
                return true;

            case IBinaryOperation binaryOperation
                when IsOutermostNonconstantStringConcatenation(binaryOperation):
                allocationSite = new AllocationSite(
                    binaryOperation.Syntax,
                    binaryOperation,
                    "string_construction",
                    binaryOperation.Type);
                return true;

            case IAnonymousObjectCreationOperation anonymousObjectCreationOperation:
                allocationSite = new AllocationSite(
                    anonymousObjectCreationOperation.Syntax,
                    anonymousObjectCreationOperation,
                    "anonymous_object_creation",
                    anonymousObjectCreationOperation.Type);
                return true;

            case ICollectionExpressionOperation collectionExpressionOperation
                when collectionExpressionOperation.Type != null &&
                     !IsStackOnlyCollectionExpressionTarget(collectionExpressionOperation.Type):
                allocationSite = new AllocationSite(
                    collectionExpressionOperation.Syntax,
                    collectionExpressionOperation,
                    "collection_expression",
                    collectionExpressionOperation.Type);
                return true;

            case IDelegateCreationOperation delegateCreationOperation:
                allocationSite = new AllocationSite(
                    delegateCreationOperation.Syntax,
                    delegateCreationOperation,
                    "delegate_creation",
                    delegateCreationOperation.Type);
                return true;

            case IConversionOperation conversionOperation
                when IsBoxingConversion(conversionOperation):
                allocationSite = new AllocationSite(
                    conversionOperation.Syntax,
                    conversionOperation,
                    "boxing_conversion",
                    conversionOperation.Type);
                return true;

            case IWithOperation withOperation
                when IsHeapAllocatedObjectType(withOperation.Type):
                allocationSite = new AllocationSite(
                    withOperation.Syntax,
                    withOperation,
                    "with_expression",
                    withOperation.Type);
                return true;

            default:
                return false;
        }
    }

    private static bool IsHeapAllocatedObjectType(ITypeSymbol? type) {
        if (type == null) return false;

        if (type.IsReferenceType) return true;

        return type is ITypeParameterSymbol typeParameter && !typeParameter.HasValueTypeConstraint;
    }

    private static bool IsImplicitParamsArray(IArrayCreationOperation arrayCreationOperation) {
        return arrayCreationOperation.IsImplicit &&
               arrayCreationOperation.Parent is IArgumentOperation {
                   ArgumentKind: ArgumentKind.ParamArray
               };
    }

    private static bool IsOutermostNonconstantStringConcatenation(IBinaryOperation operation) {
        if (operation.OperatorKind != BinaryOperatorKind.Add ||
            operation.Type?.SpecialType != SpecialType.System_String ||
            operation.ConstantValue.HasValue)
            return false;

        return operation.Parent is not IBinaryOperation {
            OperatorKind: BinaryOperatorKind.Add,
            Type.SpecialType: SpecialType.System_String
        };
    }

    private static bool IsBoxingConversion(IConversionOperation conversionOperation) {
        if (conversionOperation.Conversion.MethodSymbol != null) return false;

        var sourceType = conversionOperation.Operand?.Type;
        var targetType = conversionOperation.Type;
        if (sourceType == null || targetType == null || !sourceType.IsValueType) return false;

        if (targetType.TypeKind == TypeKind.Dynamic) return true;

        if (targetType.SpecialType == SpecialType.System_Object ||
            targetType.SpecialType == SpecialType.System_ValueType ||
            targetType.SpecialType == SpecialType.System_Enum)
            return true;

        if (targetType.TypeKind == TypeKind.Interface) return true;

        return targetType is ITypeParameterSymbol typeParameter && typeParameter.HasReferenceTypeConstraint;
    }

    private static bool IsStackOnlyCollectionExpressionTarget(ITypeSymbol? type) {
        if (type is not INamedTypeSymbol namedType) return false;

        var originalDefinition = namedType.OriginalDefinition;
        return originalDefinition.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
               "global::System" &&
               (originalDefinition.Name == "Span" || originalDefinition.Name == "ReadOnlySpan");
    }

    private readonly record struct AllocationSite(
        SyntaxNode Syntax,
        IOperation Operation,
        string AllocationKind,
        ISymbol? Symbol);
}
