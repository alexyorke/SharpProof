namespace SharpProof.Analyzer;

internal static partial class CommonBugAnalyzer
{
    private static readonly ImmutableHashSet<string> MaybeNullResultMethods =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "ElementAtOrDefault",
            "Find",
            "FindLast",
            "FirstOrDefault",
            "GetValueOrDefault",
            "LastOrDefault",
            "SingleOrDefault");

    private static readonly ImmutableHashSet<string> DeferredQueryOperators =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "GroupBy",
            "OrderBy",
            "OrderByDescending",
            "Select",
            "SelectMany",
            "ThenBy",
            "ThenByDescending",
            "Where");

    private static readonly ImmutableHashSet<string> PostMaterializationOperators =
        DeferredQueryOperators.Union(
            ImmutableHashSet.Create(StringComparer.Ordinal, "Any", "Count", "First", "FirstOrDefault"));

    private static void AnalyzeNullLinqSerializationAndDeployment(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        foreach (var operation in context.Snapshot.VisibleOperations)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            if (operation is IInvocationOperation invocation)
            {
                AnalyzeMaybeNullDereference(context, session, invocation);
                AnalyzePrematureMaterialization(context, session, invocation);
                AnalyzeDeferredQueryLambda(context, session, invocation);
                AnalyzeSerializationInvocation(context, session, invocation);
            }
        }

        AnalyzeUncheckedAllocationLengths(context, session);
    }

    private static void AnalyzeMaybeNullDereference(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IInvocationOperation invocation)
    {
        if (!MaybeNullResultMethods.Contains(invocation.TargetMethod.Name) ||
            invocation.Type == null ||
            (!invocation.Type.IsReferenceType && !IsNullableValueType(invocation.Type)) ||
            !IsImmediatelyDereferenced(invocation))
            return;

        Report(
            context,
            session,
            SharpProofDiagnostics.MaybeNullResultDereferenceRule,
            invocation.Syntax.GetLocation(),
            "maybe_null_result_dereference",
            invocation.TargetMethod.Name);
    }

    private static bool IsImmediatelyDereferenced(IInvocationOperation invocation)
    {
        IOperation current = invocation;
        while (current.Parent is IConversionOperation conversion && conversion.Operand == current)
            current = conversion;

        return current.Parent switch
        {
            IPropertyReferenceOperation property when Unwrap(property.Instance) == current => true,
            IFieldReferenceOperation field when Unwrap(field.Instance) == current => true,
            IInvocationOperation call when Unwrap(call.Instance) == current => true,
            IArrayElementReferenceOperation element when Unwrap(element.ArrayReference) == current => true,
            _ => false
        };
    }

    private static void AnalyzePrematureMaterialization(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IInvocationOperation invocation)
    {
        if (!IsLinqMethod(invocation.TargetMethod, "System.Linq.Enumerable") ||
            !PostMaterializationOperators.Contains(invocation.TargetMethod.Name))
            return;

        var materialized = Unwrap(GetInvocationSource(invocation)) as IInvocationOperation;
        if (materialized == null ||
            materialized.TargetMethod.Name is not ("ToList" or "ToArray") ||
            !ImplementsIQueryable(Unwrap(GetInvocationSource(materialized))?.Type))
            return;

        Report(
            context,
            session,
            SharpProofDiagnostics.PrematureQueryMaterializationRule,
            materialized.Syntax.GetLocation(),
            "premature_query_materialization",
            materialized.TargetMethod.Name,
            invocation.TargetMethod.Name);
    }

    private static void AnalyzeDeferredQueryLambda(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IInvocationOperation invocation)
    {
        var isEnumerable = IsLinqMethod(invocation.TargetMethod, "System.Linq.Enumerable");
        var isQueryable = IsLinqMethod(invocation.TargetMethod, "System.Linq.Queryable");
        if ((!isEnumerable && !isQueryable) || !DeferredQueryOperators.Contains(invocation.TargetMethod.Name))
            return;

        foreach (var lambda in invocation.Arguments
                     .SelectMany(static argument => argument.Value.DescendantsAndSelf())
                     .OfType<IAnonymousFunctionOperation>())
        {
            foreach (var mutation in lambda.Body.DescendantsAndSelf())
                if (TryGetMutationTarget(mutation, out var target) &&
                    TryGetSharedStateName(target, lambda, out _))
                    Report(
                        context,
                        session,
                        SharpProofDiagnostics.DeferredQuerySideEffectRule,
                        mutation.Syntax.GetLocation(),
                        "deferred_query_side_effect",
                        invocation.TargetMethod.Name,
                        mutation.Syntax.ToString());

            if (!isQueryable) continue;
            foreach (var sourceCall in lambda.Body.DescendantsAndSelf().OfType<IInvocationOperation>())
            {
                if (sourceCall.TargetMethod.DeclaringSyntaxReferences.IsDefaultOrEmpty ||
                    sourceCall.TargetMethod.MethodKind == MethodKind.LocalFunction)
                    continue;

                Report(
                    context,
                    session,
                    SharpProofDiagnostics.QueryTranslationRiskRule,
                    sourceCall.Syntax.GetLocation(),
                    "query_translation_risk",
                    invocation.TargetMethod.Name,
                    sourceCall.TargetMethod.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
            }
        }
    }

    private static void AnalyzeSerializationInvocation(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        IInvocationOperation invocation)
    {
        var serializer = GetSerializerKind(invocation.TargetMethod);
        if (serializer == SerializerKind.None) return;

        var valueArgument = invocation.Arguments.FirstOrDefault(argument =>
                                string.Equals(argument.Parameter?.Name, "value", StringComparison.Ordinal)) ??
                            invocation.Arguments.FirstOrDefault();
        var serializedType = valueArgument?.Value.Type;
        if (serializedType == null) return;

        AnalyzeSerializerAttributeMismatch(context, session, serializer, serializedType);
        if (serializer != SerializerKind.SystemTextJson || HasExplicitSerializationPolicy(invocation)) return;

        var sourceRoot = GetSerializableSourceType(serializedType);
        var jsonIgnoreAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName(
            "System.Text.Json.Serialization.JsonIgnoreAttribute");
        if (sourceRoot == null ||
            !ContainsSerializableCycle(sourceRoot, jsonIgnoreAttribute, context.CancellationToken))
            return;

        Report(
            context,
            session,
            SharpProofDiagnostics.SerializationCycleRiskRule,
            invocation.Syntax.GetLocation(),
            "serialization_cycle_risk",
            sourceRoot.ToDisplayString());
    }

    private static void AnalyzeSerializerAttributeMismatch(
        MethodBodyAnalysisContext context,
        AnalyzerSession session,
        SerializerKind serializer,
        ITypeSymbol serializedType)
    {
        var root = GetSerializableSourceType(serializedType);
        if (root == null) return;

        var wrongAttribute = serializer == SerializerKind.SystemTextJson
            ? "Newtonsoft.Json.JsonIgnoreAttribute"
            : "System.Text.Json.Serialization.JsonIgnoreAttribute";
        var wrongAttributeType = context.SemanticModel.Compilation.GetTypeByMetadataName(wrongAttribute);
        foreach (var member in root.GetMembers().Where(static member => member is IFieldSymbol or IPropertySymbol))
        {
            var attribute = FindAttribute(member, wrongAttributeType);
            var location = attribute?.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation();
            if (location == null) continue;

            Report(
                context,
                session,
                SharpProofDiagnostics.SerializerAttributeMismatchRule,
                location,
                "serializer_attribute_mismatch",
                serializer == SerializerKind.SystemTextJson ? "System.Text.Json" : "Newtonsoft.Json",
                wrongAttribute,
                member.Name);
        }
    }

    private static bool HasExplicitSerializationPolicy(IInvocationOperation invocation)
    {
        return invocation.Arguments.Any(argument =>
            !argument.IsImplicit && IsSerializationPolicyType(argument.Parameter?.Type));
    }

    private static bool IsSerializationPolicyType(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol namedType &&
               (namedType.Name == "JsonSerializerOptions" &&
                namedType.ContainingNamespace?.ToDisplayString() == "System.Text.Json" ||
                namedType.Name == "JsonSerializerContext" &&
                namedType.ContainingNamespace?.ToDisplayString() == "System.Text.Json.Serialization" ||
                namedType.Name == "JsonTypeInfo" &&
                namedType.ContainingNamespace?.ToDisplayString() ==
                "System.Text.Json.Serialization.Metadata");
    }

    private static bool ContainsSerializableCycle(
        INamedTypeSymbol root,
        INamedTypeSymbol? jsonIgnoreAttribute,
        CancellationToken cancellationToken)
    {
        var path = new HashSet<INamedTypeSymbol>(SymbolEq.Default);
        return Visit(root, root, path, 0);

        bool Visit(
            INamedTypeSymbol current,
            INamedTypeSymbol target,
            HashSet<INamedTypeSymbol> currentPath,
            int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth >= 8 || !currentPath.Add(current.OriginalDefinition)) return false;

            foreach (var memberType in GetSerializableMemberTypes(current, jsonIgnoreAttribute))
            {
                var next = GetSerializableSourceType(memberType);
                if (next == null) continue;
                if (SymbolEq.AreEqual(next.OriginalDefinition, target.OriginalDefinition))
                    return true;
                if (Visit(next, target, currentPath, depth + 1)) return true;
            }

            currentPath.Remove(current.OriginalDefinition);
            return false;
        }
    }

    private static IEnumerable<ITypeSymbol> GetSerializableMemberTypes(
        INamedTypeSymbol type,
        INamedTypeSymbol? jsonIgnoreAttribute)
    {
        foreach (var member in type.GetMembers())
            switch (member)
            {
                case IPropertySymbol
                    {
                        IsStatic: false,
                        IsIndexer: false,
                        DeclaredAccessibility: Accessibility.Public,
                        GetMethod: not null
                    } property when FindAttribute(property, jsonIgnoreAttribute) == null:
                    yield return UnwrapCollectionType(property.Type);
                    break;
                case IFieldSymbol
                    {
                        IsStatic: false,
                        DeclaredAccessibility: Accessibility.Public
                    } field when FindAttribute(field, jsonIgnoreAttribute) == null:
                    yield return UnwrapCollectionType(field.Type);
                    break;
            }
    }

    private static ITypeSymbol UnwrapCollectionType(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol array) return array.ElementType;
        if (type is INamedTypeSymbol { TypeArguments.Length: 1 } namedType &&
            namedType.AllInterfaces.Any(candidate =>
                candidate.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T))
            return namedType.TypeArguments[0];

        return type;
    }

    private static INamedTypeSymbol? GetSerializableSourceType(ITypeSymbol type)
    {
        type = UnwrapCollectionType(type);
        return type is INamedTypeSymbol namedType &&
               namedType.SpecialType != SpecialType.System_String &&
               !namedType.DeclaringSyntaxReferences.IsDefaultOrEmpty
            ? namedType
            : null;
    }

    private static void AnalyzeUncheckedAllocationLengths(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        if (context.SemanticModel.Compilation.Options is CSharpCompilationOptions { CheckOverflow: true }) return;

        foreach (var binary in context.Node.DescendantNodes()
                     .OfType<BinaryExpressionSyntax>()
                     .Where(expression => expression.IsKind(SyntaxKind.MultiplyExpression) &&
                                          IsAllocationLengthExpression(expression) &&
                                          !IsInsideNestedSyntaxCallable(expression, context.Node) &&
                                          !IsInsideCheckedContext(expression)))
        {
            if (context.SemanticModel.GetConstantValue(binary, context.CancellationToken).HasValue ||
                context.SemanticModel.GetOperation(binary, context.CancellationToken) is not IBinaryOperation
                {
                    Type: { } resultType
                } ||
                !IsBoundedIntegralType(resultType))
                continue;

            Report(
                context,
                session,
                SharpProofDiagnostics.UncheckedAllocationArithmeticRule,
                binary.GetLocation(),
                "unchecked_allocation_arithmetic",
                binary.ToString());
        }
    }

    private static bool IsAllocationLengthExpression(BinaryExpressionSyntax expression)
    {
        for (SyntaxNode? current = expression; current?.Parent != null; current = current.Parent)
        {
            if (current.Parent is ArrayRankSpecifierSyntax rank && rank.Sizes.Contains((ExpressionSyntax)current))
                return rank.Parent?.Parent is ArrayCreationExpressionSyntax or StackAllocArrayCreationExpressionSyntax;
            if (current.Parent is not ParenthesizedExpressionSyntax and not CastExpressionSyntax) return false;
        }

        return false;
    }

    private static bool IsInsideCheckedContext(SyntaxNode node)
    {
        return node.Ancestors().Any(ancestor => ancestor.IsKind(SyntaxKind.CheckedExpression) ||
                                                ancestor.IsKind(SyntaxKind.CheckedStatement));
    }

    private static bool IsBoundedIntegralType(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or
            SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or
            SpecialType.System_IntPtr or SpecialType.System_UIntPtr;
    }

    private static IOperation? GetInvocationSource(IInvocationOperation invocation)
    {
        return invocation.Instance ?? invocation.Arguments.FirstOrDefault()?.Value;
    }

    private static bool IsLinqMethod(IMethodSymbol method, string containingType)
    {
        return string.Equals(method.ContainingType.ToDisplayString(), containingType, StringComparison.Ordinal);
    }

    private static bool ImplementsIQueryable(ITypeSymbol? type)
    {
        return type is INamedTypeSymbol namedType &&
               (namedType.ToDisplayString() is "System.Linq.IQueryable" ||
                namedType.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>" ||
                namedType.AllInterfaces.Any(candidate =>
                    candidate.ToDisplayString() == "System.Linq.IQueryable" ||
                    candidate.OriginalDefinition.ToDisplayString() == "System.Linq.IQueryable<T>"));
    }

    private static SerializerKind GetSerializerKind(IMethodSymbol method)
    {
        if (method.Name == "Serialize" && method.ContainingType.ToDisplayString() == "System.Text.Json.JsonSerializer")
            return SerializerKind.SystemTextJson;
        if (method.Name == "SerializeObject" && method.ContainingType.ToDisplayString() == "Newtonsoft.Json.JsonConvert")
            return SerializerKind.NewtonsoftJson;
        return SerializerKind.None;
    }

    private enum SerializerKind
    {
        None,
        SystemTextJson,
        NewtonsoftJson
    }
}
