using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic;
using static SharpProof.Analyzer.Engine.Rules.ComparerInvocationPurity;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    private static bool TryCheckCompilerGeneratedInterpolatedStringHandlerPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!IsDefaultInterpolatedStringHandlerInvocation(invocationOperation)) return false;

        if (ContainsFormattedOrAlignedInterpolation(invocationOperation.Syntax)) return false;

        result = CheckPureViewInvocationInputs(invocationOperation, context, currentState);
        if (result.IsPure)
        {
        }

        return true;
    }

    private static bool IsDefaultInterpolatedStringHandlerInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null) return false;

        var containingType = targetMethod.ContainingType?.OriginalDefinition.ToDisplayString();
        if (!string.Equals(containingType, "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler",
                StringComparison.Ordinal)) return false;

        return targetMethod.Name is "AppendLiteral" or "AppendFormatted" or "ToStringAndClear";
    }

    private static bool ContainsFormattedOrAlignedInterpolation(SyntaxNode syntax)
    {
        var interpolatedString = syntax.AncestorsAndSelf()
            .OfType<InterpolatedStringExpressionSyntax>()
            .FirstOrDefault();
        if (interpolatedString == null) return false;

        return interpolatedString.Contents
            .OfType<InterpolationSyntax>()
            .Any(interpolation => interpolation.AlignmentClause != null || interpolation.FormatClause != null);
    }

    private static bool IsUntrustedMetadataOnlyMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.DeclaringSyntaxReferences.Length > 0 || methodSymbol.IsAbstract) return false;

        var assemblyName = methodSymbol.ContainingAssembly?.Identity.Name;
        return !GeneratedPurityCatalog.IsFrameworkAssemblyName(assemblyName);
    }

    private static bool TryCheckArrayAsReadOnlyOwnedLocalArrayPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (PurityKnownBclSemantics.IsArrayAsReadOnlyInvocation(invocationOperation))
        {
            var inputResult = CheckPureViewInvocationInputs(invocationOperation, context, currentState);
            if (!inputResult.IsPure) result = inputResult;

            return true;
        }

        return false;
    }

    private static bool TryCheckSpanAndMemoryViewPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (IsArrayAsSpanInvocation(invocationOperation))
        {
            var inputResult = CheckPureViewInvocationInputs(invocationOperation, context, currentState);
            if (!inputResult.IsPure) result = inputResult;

            return true;
        }

        if (RuleAnalysisHelper.IsSemanticallyPureSpanLikeSliceInvocation(invocationOperation))
        {
            var inputResult = CheckPureViewInvocationInputs(invocationOperation, context, currentState);
            if (!inputResult.IsPure) result = inputResult;

            return true;
        }

        return false;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckPureViewInvocationInputs(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (invocationOperation.Instance != null)
        {
            var instanceResult = PurityAnalysisEngine.CheckSingleOperation(
                invocationOperation.Instance,
                context,
                currentState);
            if (!instanceResult.IsPure) return instanceResult;
        }

        foreach (var argument in invocationOperation.Arguments)
        {
            var argumentResult = PurityAnalysisEngine.CheckSingleOperation(
                argument.Value,
                context,
                currentState);
            if (!argumentResult.IsPure) return argumentResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static bool IsArrayAsSpanInvocation(IInvocationOperation invocationOperation)
    {
        var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
        if (targetMethod == null ||
            targetMethod.Name != "AsSpan" ||
            targetMethod.ContainingType?.ToDisplayString() != "System.MemoryExtensions" ||
            targetMethod.Parameters.Length == 0 ||
            targetMethod.Parameters[0].Type is not IArrayTypeSymbol)
            return false;

        return true;
    }

    private static bool IsLinqEnumerableInvocation(IMethodSymbol methodSymbol, Compilation compilation)
    {
        var enumerableType = compilation.GetTypeByMetadataName("System.Linq.Enumerable");
        var definition = GetExtensionDefinition(methodSymbol);
        return enumerableType != null &&
               SymbolEqualityComparer.Default.Equals(definition.ContainingType?.OriginalDefinition, enumerableType);
    }

    private static bool IsLinqSourceLessFactory(IMethodSymbol methodSymbol)
    {
        var definition = GetExtensionDefinition(methodSymbol);
        return definition.ContainingType?.OriginalDefinition.ToDisplayString() == "System.Linq.Enumerable" &&
               definition.Name is "Empty" or "Range" or "Repeat";
    }

    internal static IMethodSymbol GetExtensionDefinition(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ReducedFrom ?? methodSymbol;
    }

    internal static bool ShouldDeferToSpecializedDispatchPurity(IMethodSymbol methodSymbol)
    {
        return TryGetDefaultComparisonCollectionKeyType(methodSymbol, out _) ||
               TryGetDefaultEqualityCollectionElementType(methodSymbol, out _, out _) ||
               IsLinqDefaultEqualityDispatchMethod(methodSymbol) ||
               IsLinqDefaultComparisonDispatchMethod(methodSymbol) ||
               IsNullableDefaultDispatchMethod(methodSymbol) ||
               IsMemoryExtensionsDefaultEqualityDispatchMethod(methodSymbol) ||
               IsHashCodeCombineMethod(methodSymbol) ||
               TryGetEqualityComparerElementType(methodSymbol, out _) ||
               TryGetComparerElementType(methodSymbol, out _);
    }

    private static bool IsLinqDefaultEqualityDispatchMethod(IMethodSymbol methodSymbol)
    {
        var definition = GetExtensionDefinition(methodSymbol);
        return definition.ContainingType?.OriginalDefinition.ToDisplayString() == "System.Linq.Enumerable" &&
               definition.Name is "Contains" or "SequenceEqual" or "Distinct" or "Except" or "Intersect" or "Union" or
                   "GroupBy" or "ToLookup" or "Join" or "GroupJoin";
    }

    private static bool IsLinqDefaultComparisonDispatchMethod(IMethodSymbol methodSymbol)
    {
        var definition = GetExtensionDefinition(methodSymbol);
        return definition.ContainingType?.OriginalDefinition.ToDisplayString() == "System.Linq.Enumerable" &&
               definition.Name is "OrderBy" or "OrderByDescending" or "ThenBy" or "ThenByDescending" or "Min" or "Max";
    }

    private static bool IsNullableDefaultDispatchMethod(IMethodSymbol methodSymbol)
    {
        var definition = methodSymbol.OriginalDefinition;
        return definition.ContainingType?.ToDisplayString() == "System.Nullable" &&
               definition.Name is "Compare" or "Equals";
    }

    private static bool IsMemoryExtensionsDefaultEqualityDispatchMethod(IMethodSymbol methodSymbol)
    {
        var definition = GetExtensionDefinition(methodSymbol);
        return definition.ContainingType?.OriginalDefinition.ToDisplayString() == "System.MemoryExtensions" &&
               definition.Name is "SequenceEqual" or "Contains" or "IndexOf" or "LastIndexOf" or "StartsWith"
                   or "EndsWith";
    }

    private static bool TryCheckUnsafeReadUnalignedPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod?.OriginalDefinition;
        if (methodSymbol?.Name != "ReadUnaligned" ||
            methodSymbol.ContainingType?.ToDisplayString() != "System.Runtime.CompilerServices.Unsafe")
            return false;

        return EnsureInvocationOperandsArePure(invocationOperation, context, currentState, out result);
    }

    private static bool IsPureOutArgumentTarget(IOperation? operation)
    {
        return IsOutArgumentTarget(operation, true);
    }

    private static bool IsDeclarationOrDiscardOutArgumentTarget(IOperation? operation)
    {
        return IsOutArgumentTarget(operation, false);
    }

    private static bool IsOutArgumentTarget(IOperation? operation, bool allowLocalReference)
    {
        operation = PurityAnalysisEngine.SkipImplicitConversions(operation);

        if (operation is IConversionOperation conversionOperation)
            return IsOutArgumentTarget(conversionOperation.Operand, allowLocalReference);

        return (allowLocalReference && operation is ILocalReferenceOperation) ||
               operation is IDeclarationExpressionOperation ||
               operation is IDiscardOperation;
    }

    private static bool IsDeconstructOutArgumentMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.Name != "Deconstruct") return false;

        var parameters = methodSymbol.ReducedFrom?.Parameters ?? methodSymbol.Parameters;
        var startIndex = methodSymbol.ReducedFrom?.IsExtensionMethod == true ? 1 : 0;
        if (parameters.Length <= startIndex) return false;

        for (var index = startIndex; index < parameters.Length; index++)
            if (parameters[index].RefKind != RefKind.Out)
                return false;

        return true;
    }

    private static bool IsDispatchAnalyzedOutArgumentMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.Name != "TryGetValue") return false;

        var typeDefinition = methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        return typeDefinition is
            "System.Collections.Generic.Dictionary<TKey, TValue>" or
            "System.Collections.Generic.HashSet<T>" or
            "System.Collections.Generic.SortedSet<T>" or
            "System.Collections.Generic.SortedDictionary<TKey, TValue>" or
            "System.Collections.Generic.SortedList<TKey, TValue>" or
            "System.Collections.Immutable.ImmutableDictionary<TKey, TValue>" or
            "System.Collections.Immutable.ImmutableHashSet<T>" or
            "System.Collections.Immutable.ImmutableSortedSet<T>" or
            "System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>";
    }

    private static bool IsSemanticallyPureOutArgumentMethod(IMethodSymbol methodSymbol)
    {
        var originalDefinition = methodSymbol.OriginalDefinition;
        return IsBooleanTryParseMethod(originalDefinition) ||
               IsEnumTryParseMethod(originalDefinition);
    }

    private static bool TryCheckStringEnumerableJoinPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod.OriginalDefinition;
        if (!IsStringEnumerableJoinOverload(methodSymbol)) return false;

        var enumerableArgument = invocationOperation.Arguments[1].Value;
        var enumerablePurity = CheckLinqSourceEnumeratorPurity(enumerableArgument, context, currentState);
        if (!enumerablePurity.IsPure)
        {
            result = enumerablePurity;
            return true;
        }

        return true;
    }

    private static bool IsStringEnumerableJoinOverload(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.Name != "Join" ||
            methodSymbol.ContainingType?.SpecialType != SpecialType.System_String ||
            methodSymbol.IsGenericMethod ||
            methodSymbol.Parameters.Length != 2)
            return false;

        if (methodSymbol.Parameters[0].Type.SpecialType != SpecialType.System_String) return false;

        return methodSymbol.Parameters[1].Type is INamedTypeSymbol enumerableType &&
               enumerableType.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T &&
               enumerableType.TypeArguments[0].SpecialType == SpecialType.System_String;
    }

    private static bool IsImmutableHashSetCreateRangeWithComparer(IMethodSymbol methodSymbol)
    {
        return methodSymbol.Name == "CreateRange" &&
               methodSymbol.ContainingType?.OriginalDefinition.Name == "ImmutableHashSet" &&
               methodSymbol.ContainingType?.ContainingNamespace.ToDisplayString() == "System.Collections.Immutable";
    }

    private static bool IsCompilerGeneratedArrayForeachInvocation(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context)
    {
        if (invocationOperation.TargetMethod.Parameters.Length != 0 ||
            !IsArrayForeachSyntax(invocationOperation.Syntax, context))
            return false;

        return invocationOperation.TargetMethod.Name switch
        {
            nameof(IDisposable.Dispose) => invocationOperation.TargetMethod.ContainingType?.SpecialType ==
                                           SpecialType.System_IDisposable,
            "GetEnumerator" => invocationOperation.TargetMethod.ContainingType?.ToDisplayString() ==
                               "System.Collections.IEnumerable",
            "MoveNext" => invocationOperation.TargetMethod.ContainingType?.ToDisplayString() ==
                          "System.Collections.IEnumerator",
            _ => false
        };
    }

    private static bool IsArrayForeachSyntax(SyntaxNode syntax, PurityAnalysisContext context)
    {
        if (!syntax.IsKind(SyntaxKind.IdentifierName) &&
            !syntax.IsKind(SyntaxKind.SimpleMemberAccessExpression) &&
            !syntax.IsKind(SyntaxKind.ElementAccessExpression))
            return false;

        return TryGetForeachCollectionType(syntax.Parent, context.SemanticModel, context.CancellationToken) is
            IArrayTypeSymbol;
    }

    private static ITypeSymbol? TryGetForeachCollectionType(
        SyntaxNode? syntaxNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return syntaxNode switch
        {
            ForEachStatementSyntax forEachStatement =>
                semanticModel.GetTypeInfo(forEachStatement.Expression, cancellationToken).Type,
            ForEachVariableStatementSyntax forEachVariableStatement =>
                semanticModel.GetTypeInfo(forEachVariableStatement.Expression, cancellationToken).Type,
            _ => null
        };
    }


    private static bool TryCheckStringComparisonPurity(
        IInvocationOperation invocationOperation,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod?.OriginalDefinition;
        if (methodSymbol?.ContainingType?.SpecialType != SpecialType.System_String) return false;

        if (methodSymbol.Name == "Contains" &&
            methodSymbol.Parameters.Length == 1 &&
            methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String)
            return true;

        if (methodSymbol.Name is "ToLower" or "ToUpper" &&
            methodSymbol.Parameters.Length == 0)
        {
            result = CreateReflectionEnvironmentSourceImpurity(
                invocationOperation,
                methodSymbol,
                "string_default_culture_casing");
            return true;
        }

        if (methodSymbol.Name is "Contains" or "StartsWith" or "EndsWith" or "Equals" or "IndexOf")
        {
            var comparisonParameterIndex = GetStringComparisonParameterIndex(methodSymbol);
            if (comparisonParameterIndex >= 0 && comparisonParameterIndex < invocationOperation.Arguments.Length)
            {
                if (IsDeterministicStringComparison(invocationOperation.Arguments[comparisonParameterIndex].Value))
                    return true;

                result = CreateReflectionEnvironmentSourceImpurity(
                    invocationOperation,
                    methodSymbol,
                    "string_current_culture_comparison");
                return true;
            }
        }

        if (methodSymbol.Name is "StartsWith" or "EndsWith" &&
            methodSymbol.Parameters.Length == 1 &&
            methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_String)
        {
            result = CreateReflectionEnvironmentSourceImpurity(
                invocationOperation,
                methodSymbol,
                "string_default_culture_comparison");
            return true;
        }

        return false;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CreateReflectionEnvironmentSourceImpurity(
        IInvocationOperation invocationOperation,
        IMethodSymbol methodSymbol,
        string catalogSource)
    {
        return PurityAnalysisEngine.ImpureResult(
            invocationOperation,
            "reflection_environment_source",
            nameof(MethodInvocationPurityRule),
            methodSymbol,
            catalogSource);
    }

    private static bool TryCheckSystemTypeMemberPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        string methodName,
        int parameterCount,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (methodSymbol.Name != methodName ||
            methodSymbol.Parameters.Length != parameterCount ||
            !IsMemberOfMetadataType(methodSymbol, context, "System.Type"))
            return false;

        return EnsureInvocationOperandsArePure(invocationOperation, context, currentState, out result);
    }

    private static bool IsMemberOfMetadataType(
        IMethodSymbol methodSymbol,
        PurityAnalysisContext context,
        string metadataName)
    {
        return context.SemanticModel.Compilation.GetTypeByMetadataName(metadataName) is { } metadataType &&
               SymbolEqualityComparer.Default.Equals(methodSymbol.ContainingType?.OriginalDefinition, metadataType);
    }

    private static bool TryCheckStringComparerInvocationPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (methodSymbol.Name is not ("Compare" or "Equals") ||
            methodSymbol.Parameters.Length != 2 ||
            !IsMemberOfMetadataType(methodSymbol, context, "System.StringComparer") ||
            invocationOperation.Instance == null ||
            !ComparerDispatchHelper.IsTrustedGeneratedPureStringComparerSingleton(invocationOperation.Instance,
                context))
            return false;

        return EnsureInvocationOperandsArePure(invocationOperation, context, currentState, out result);
    }

    private static bool TryCheckMetadataMemberOperandPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        string metadataName,
        Func<IMethodSymbol, bool> matchesMember,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (!IsMemberOfMetadataType(methodSymbol, context, metadataName) ||
            !matchesMember(methodSymbol))
            return false;

        return EnsureInvocationOperandsArePure(invocationOperation, context, currentState, out result);
    }

    private static bool EnsureInvocationOperandsArePure(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = RuleAnalysisHelper.CheckInstanceAndArguments(
            invocationOperation.Instance,
            invocationOperation.Arguments,
            context,
            currentState);
        return true;
    }

    private static bool TryCheckSemanticallyPureParsePurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod?.OriginalDefinition;
        if (methodSymbol == null) return false;

        return IsBooleanParseMethod(methodSymbol) ||
               IsBooleanTryParseMethod(methodSymbol) ||
               IsEnumTryParseMethod(methodSymbol) ||
               TryCheckEnumParsePurity(invocationOperation, methodSymbol, context, currentState, out result) ||
               IsIPAddressParseMethod(methodSymbol);
    }

    private static bool IsBooleanParseMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ContainingType?.SpecialType == SpecialType.System_Boolean &&
               methodSymbol.Name == "Parse" &&
               methodSymbol.Parameters.Length == 1 &&
               SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(methodSymbol.Parameters[0].Type);
    }

    private static bool IsBooleanTryParseMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ContainingType?.SpecialType == SpecialType.System_Boolean &&
               methodSymbol.Name == "TryParse" &&
               methodSymbol.Parameters.Length == 2 &&
               SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(methodSymbol.Parameters[0].Type) &&
               methodSymbol.Parameters[1].RefKind == RefKind.Out &&
               methodSymbol.Parameters[1].Type.SpecialType == SpecialType.System_Boolean;
    }

    private static bool IsEnumTryParseMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ContainingType?.ToDisplayString() != "System.Enum" ||
            methodSymbol.Name != "TryParse" ||
            !methodSymbol.IsGenericMethod ||
            methodSymbol.TypeParameters.Length != 1 ||
            methodSymbol.Parameters.Length is not (2 or 3) ||
            !SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(methodSymbol.Parameters[0].Type))
            return false;

        if (methodSymbol.Parameters.Length == 3 &&
            methodSymbol.Parameters[1].Type.SpecialType != SpecialType.System_Boolean)
            return false;

        var outParameter = methodSymbol.Parameters[methodSymbol.Parameters.Length - 1];
        return outParameter.RefKind == RefKind.Out &&
               SymbolEqualityComparer.Default.Equals(outParameter.Type, methodSymbol.TypeParameters[0]);
    }

    private static bool TryCheckEnumParsePurity(
        IInvocationOperation invocationOperation,
        IMethodSymbol methodSymbol,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (!IsEnumParseMethod(methodSymbol) ||
            invocationOperation.Arguments.Length < 2 ||
            !IsCompileTimeEnumTypeArgument(invocationOperation.Arguments[0].Value))
            return false;

        for (var index = 1; index < invocationOperation.Arguments.Length; index++)
        {
            var argumentResult = PurityAnalysisEngine.CheckSingleOperation(
                invocationOperation.Arguments[index].Value,
                context,
                currentState);
            if (!argumentResult.IsPure)
            {
                result = argumentResult;
                return true;
            }
        }

        return true;
    }

    private static bool IsEnumParseMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol.ContainingType?.ToDisplayString() != "System.Enum" ||
            methodSymbol.Name != "Parse" ||
            methodSymbol.Parameters.Length is not (2 or 3) ||
            methodSymbol.Parameters[0].Type.ToDisplayString() != "System.Type" ||
            !SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(methodSymbol.Parameters[1].Type))
            return false;

        return methodSymbol.Parameters.Length == 2 ||
               methodSymbol.Parameters[2].Type.SpecialType == SpecialType.System_Boolean;
    }

    private static bool IsCompileTimeEnumTypeArgument(IOperation operation)
    {
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        return unwrappedOperation is ITypeOfOperation typeOfOperation &&
               typeOfOperation.TypeOperand.TypeKind == TypeKind.Enum;
    }

    private static bool IsIPAddressParseMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ContainingType?.ToDisplayString() == "System.Net.IPAddress" &&
               methodSymbol.Name == "Parse" &&
               methodSymbol.Parameters.Length == 1 &&
               SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(methodSymbol.Parameters[0].Type);
    }

    private static int GetStringComparisonParameterIndex(IMethodSymbol methodSymbol)
    {
        for (var i = 0; i < methodSymbol.Parameters.Length; i++)
            if (methodSymbol.Parameters[i].Type.ToDisplayString() == "System.StringComparison")
                return i;

        return -1;
    }

    private static bool IsDeterministicStringComparison(IOperation? operation)
    {
        var unwrappedOperation = PurityAnalysisEngine.SkipImplicitConversions(operation);
        return unwrappedOperation?.ConstantValue.HasValue == true &&
               unwrappedOperation.ConstantValue.Value is int comparison &&
               comparison is 2 or 3 or 4 or 5;
    }

    private static bool TryCheckMemoryExtensionsDefaultEqualityDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (!IsMemoryExtensionsDefaultEqualityDispatchMethod(methodSymbol)) return false;

        var definition = GetExtensionDefinition(methodSymbol);
        var elementType = GetFirstTypeArgument(methodSymbol) ?? GetFirstTypeArgument(definition);
        if (elementType == null) return false;

        if (elementType.TypeKind == TypeKind.TypeParameter) return false;

        result = CheckDefaultEqualityDispatchPurity(elementType, invocationOperation, context);
        return true;
    }

    private static ITypeSymbol? GetFirstTypeArgument(IMethodSymbol methodSymbol)
    {
        return methodSymbol.TypeArguments.Length > 0 ? methodSymbol.TypeArguments[0] : null;
    }

    private static bool TryCheckHashCodeCombineDispatchPurity(
        IInvocationOperation invocationOperation,
        PurityAnalysisContext context,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var methodSymbol = invocationOperation.TargetMethod;
        if (!IsHashCodeCombineMethod(methodSymbol)) return false;

        foreach (var typeArgument in methodSymbol.TypeArguments)
        {
            result = CheckDefaultHashDispatchPurity(typeArgument, invocationOperation, context);
            if (!result.IsPure) return true;
        }

        return true;
    }

    private static bool IsHashCodeCombineMethod(IMethodSymbol methodSymbol)
    {
        return methodSymbol.ContainingType?.ToDisplayString() == "System.HashCode" &&
               methodSymbol.Name == "Combine" &&
               methodSymbol.IsGenericMethod &&
               methodSymbol.TypeArguments.Length > 0;
    }


    private static bool IsImmediateFreshArrayLinqSource(
        IOperation sourceOperation,
        Compilation compilation)
    {
        var unwrappedSource = PurityAnalysisEngine.SkipImplicitConversions(sourceOperation) ?? sourceOperation;
        if (unwrappedSource is not IInvocationOperation invocationOperation ||
            invocationOperation.Type is not IArrayTypeSymbol)
            return false;

        var originalDefinition = invocationOperation.TargetMethod.OriginalDefinition;
        return PurityAnalysisEngine.IsTrustedGeneratedFreshOwnedArrayReturningMember(originalDefinition, compilation) ||
               PurityConcreteReceiverResolver.IsTrustedFreshArrayFactoryOperation(unwrappedSource, compilation, out _);
    }
}
