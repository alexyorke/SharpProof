namespace SharpProof.Analyzer.Engine.Rules;

internal static class ComparerDispatchHelper
{
    internal static IMethodSymbol? ResolveDefaultComparisonImplementation(ITypeSymbol keyType)
    {
        if (DispatchedMemberResolution.TryGetIComparableCompareToImplementation(
                keyType,
                out var genericImplementation))
            return genericImplementation;

        return DispatchedMemberResolution.TryGetIComparableObjectCompareToImplementation(
            keyType,
            out var objectImplementation)
            ? objectImplementation
            : null;
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckDefaultComparisonPurity(
        ITypeSymbol keyType,
        SyntaxNode useSyntax,
        PurityAnalysisContext context,
        Func<PurityAnalysisEngine.PurityAnalysisResult> createUnknownResult)
    {
        if (IsBuiltinValueComparerKey(keyType))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var implementation = ResolveDefaultComparisonImplementation(keyType);
        return implementation == null
            ? createUnknownResult()
            : PurityCalleeResolver.GetCanonicalCalleePurityAtUse(implementation, useSyntax, context);
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckSubtypeConstructorComparerPurity(
        INamedTypeSymbol receiverType,
        PurityAnalysisContext context,
        Func<IOperation, PurityAnalysisEngine.PurityAnalysisResult> checkComparerValuePurity)
    {
        foreach (var constructor in receiverType.InstanceConstructors)
        foreach (var syntaxReference in constructor.DeclaringSyntaxReferences)
        {
            if (syntaxReference.GetSyntax(context.CancellationToken) is not ConstructorDeclarationSyntax
                { Initializer: { } initializer })
                continue;

            foreach (var argument in initializer.ArgumentList.Arguments)
            {
                var argumentOperation = CompilationSyntaxAccess.GetOperation(
                    context.SemanticModel,
                    argument.Expression,
                    context.CancellationToken);
                var value = PurityAnalysisEngine.SkipImplicitConversions(argumentOperation) ?? argumentOperation;
                if (value?.Type == null || !IsComparerOrDerivedInterface(value.Type)) continue;

                var comparerResult = checkComparerValuePurity(value);
                if (!comparerResult.IsPure) return comparerResult;
            }
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckKnownConstructionComparerPurity(
        IOperation? receiverOperation,
        PurityAnalysisContext context,
        Func<ITypeSymbol?, bool> isCollectionType,
        Func<ITypeSymbol, bool> isComparerParameterType,
        Func<IOperation, PurityAnalysisEngine.PurityAnalysisResult> checkComparerValuePurity)
    {
        var unwrappedReceiver = PurityAnalysisEngine.SkipImplicitConversions(receiverOperation) ?? receiverOperation;
        if (unwrappedReceiver is IObjectCreationOperation objectCreationOperation)
            return CheckObjectCreationComparerPurity(
                objectCreationOperation,
                context,
                isCollectionType,
                isComparerParameterType,
                checkComparerValuePurity);

        if (FieldOrPropertyInitializerOperationHelper.TryGetFieldOrPropertyInitializerOperation(
                unwrappedReceiver,
                context,
                out var initializerOperation) &&
            PurityAnalysisEngine.SkipImplicitConversions(initializerOperation) is IObjectCreationOperation
                initializerObjectCreation)
            return CheckObjectCreationComparerPurity(
                initializerObjectCreation,
                context,
                isCollectionType,
                isComparerParameterType,
                checkComparerValuePurity);

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckObjectCreationComparerPurity(
        IObjectCreationOperation objectCreationOperation,
        PurityAnalysisContext context,
        Func<ITypeSymbol?, bool> isCollectionType,
        Func<ITypeSymbol, bool> isComparerParameterType,
        Func<IOperation, PurityAnalysisEngine.PurityAnalysisResult> checkComparerValuePurity)
    {
        if (!isCollectionType(objectCreationOperation.Type)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        foreach (var argument in objectCreationOperation.Arguments)
        {
            var value = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
            if (!IsComparerArgument(value, argument.Parameter?.Type, isComparerParameterType)) continue;

            var comparerArgumentResult =
                PurityAnalysisEngine.CheckSingleOperation(value, context,
                    PurityAnalysisEngine.PurityAnalysisState.Pure);
            if (!comparerArgumentResult.IsPure) return comparerArgumentResult;

            var comparerResult = checkComparerValuePurity(value);
            if (!comparerResult.IsPure) return comparerResult;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static bool IsComparerArgument(
        IOperation? value,
        ITypeSymbol? parameterType,
        Func<ITypeSymbol, bool> isComparerParameterType)
    {
        return value?.Type != null &&
               parameterType is INamedTypeSymbol namedParameterType &&
               (isComparerParameterType(namedParameterType) || IsComparerOrDerivedInterface(value.Type));
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckComparerValuePurity(
        IOperation value,
        PurityAnalysisContext context,
        SyntaxNode impureCalleeSyntax,
        IOperation unresolvedDispatchOperation,
        string ruleName,
        ISymbol? unresolvedDispatchSymbol)
    {
        value = PurityAnalysisEngine.SkipImplicitConversions(value) ?? value;
        var comparerType = value.Type;
        if (comparerType == null || IsNullOrDefaultComparerValue(value))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (IsTrustedGeneratedPureDefaultComparerSingleton(value, context) ||
            IsTrustedGeneratedPureStringComparerSingleton(value, context))
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var foundImplementation = false;
        foreach (var comparisonMethod in EnumerateComparerImplementations(comparerType))
        {
            foundImplementation = true;
            var comparisonPurity = PurityCalleeResolver.GetCalleePurityAtUse(comparisonMethod, impureCalleeSyntax, context);
            if (!comparisonPurity.IsPure) return comparisonPurity;
        }

        if (!foundImplementation && IsUnresolvedComparerDispatch(comparerType))
            return PurityAnalysisEngine.ImpureResult(
                unresolvedDispatchOperation,
                "unknown_external_call",
                ruleName,
                PurityAnalysisEngine.TryResolveSymbol(value) ?? unresolvedDispatchSymbol);

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    internal static bool IsNullOrDefaultComparerArgument(IArgumentOperation argument)
    {
        var value = PurityAnalysisEngine.SkipImplicitConversions(argument.Value) ?? argument.Value;
        return IsNullOrDefaultComparerValue(value) || IsDefaultComparerSingleton(value);
    }

    internal static bool IsTrustedGeneratedPureStringComparerSingleton(
        IOperation value,
        PurityAnalysisContext context)
    {
        if (!TryGetStaticMetadataPropertyGetter(value, null, out var containingType, out var getterSymbol))
            return false;

        if (containingType.OriginalDefinition.ToDisplayString() != "System.StringComparer") return false;

        return IsTrustedGeneratedPureMetadataGetter(getterSymbol, context);
    }

    internal static IEnumerable<IMethodSymbol> EnumerateComparerImplementations(ITypeSymbol comparerType)
    {
        if (comparerType is not INamedTypeSymbol namedComparerType) yield break;

        var seen = new HashSet<IMethodSymbol>(SymbolEq.Default);
        foreach (var interfaceType in namedComparerType.AllInterfaces)
        {
            if (!IsComparerInterface(interfaceType)) continue;

            foreach (var interfaceMethod in interfaceType.GetMembers().OfType<IMethodSymbol>())
            {
                var implementation =
                    namedComparerType.FindImplementationForInterfaceMember(interfaceMethod) as IMethodSymbol;
                if (implementation == null || implementation.DeclaringSyntaxReferences.Length == 0) continue;

                if (seen.Add(implementation.OriginalDefinition)) yield return implementation;
            }
        }
    }

    internal static bool IsUnresolvedComparerDispatch(ITypeSymbol comparerType)
    {
        if (comparerType is ITypeParameterSymbol typeParameter)
            return typeParameter.ConstraintTypes
                .OfType<INamedTypeSymbol>()
                .Any(IsComparerOrDerivedInterface);

        if (comparerType is not INamedTypeSymbol namedComparerType) return false;

        if (IsComparerInterface(namedComparerType)) return true;

        if (namedComparerType.TypeKind != TypeKind.Interface && !namedComparerType.IsAbstract) return false;

        return IsComparerOrDerivedInterface(namedComparerType);
    }

    internal static bool IsComparerOrDerivedInterface(ITypeSymbol typeSymbol)
    {
        return typeSymbol is INamedTypeSymbol namedType &&
               (IsComparerInterface(namedType) || namedType.AllInterfaces.Any(IsComparerInterface));
    }

    internal static bool IsBuiltinValueComparerKey(ITypeSymbol keyType)
    {
        if (keyType.TypeKind == TypeKind.Enum) return true;

        return keyType.SpecialType is
            SpecialType.System_Boolean or
            SpecialType.System_Byte or
            SpecialType.System_SByte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal or
            SpecialType.System_Char or
            SpecialType.System_String;
    }

    private static bool IsComparerInterface(INamedTypeSymbol typeSymbol)
    {
        var displayString = typeSymbol.OriginalDefinition.ToDisplayString();
        return displayString == "System.Collections.Generic.IEqualityComparer<T>" ||
               displayString == "System.Collections.Generic.IComparer<T>";
    }

    private static bool IsNullOrDefaultComparerValue(IOperation value)
    {
        value = PurityAnalysisEngine.SkipImplicitConversions(value) ?? value;

        if (value.ConstantValue.HasValue && value.ConstantValue.Value == null) return true;

        return value is IDefaultValueOperation;
    }

    private static bool IsDefaultComparerSingleton(IOperation value)
    {
        return value is IPropertyReferenceOperation propertyReference &&
               propertyReference.Property.Name == "Default" &&
               propertyReference.Property.ContainingType is INamedTypeSymbol containingType &&
               containingType.OriginalDefinition.ToDisplayString() is
                   "System.Collections.Generic.EqualityComparer<T>" or
                   "System.Collections.Generic.Comparer<T>";
    }

    private static bool IsTrustedGeneratedPureDefaultComparerSingleton(
        IOperation value,
        PurityAnalysisContext context)
    {
        if (!TryGetStaticMetadataPropertyGetter(value, "Default", out var containingType, out var getterSymbol))
            return false;

        var containingTypeDisplay = containingType.OriginalDefinition.ToDisplayString();
        if (containingTypeDisplay is not "System.Collections.Generic.EqualityComparer<T>" and
            not "System.Collections.Generic.Comparer<T>")
            return false;

        return IsTrustedGeneratedPureMetadataGetter(getterSymbol, context);
    }

    private static bool TryGetStaticMetadataPropertyGetter(
        IOperation value,
        string? propertyName,
        out INamedTypeSymbol containingType,
        out IMethodSymbol getterSymbol)
    {
        value = PurityAnalysisEngine.SkipImplicitConversions(value) ?? value;
        if (value is IPropertyReferenceOperation
            {
                Property:
                {
                    IsStatic: true,
                    Name: var candidatePropertyName,
                    ContainingType: { } candidateContainingType,
                    GetMethod: { } candidateGetterSymbol
                }
            } &&
            (propertyName == null || candidatePropertyName == propertyName) &&
            PurityAnalysisEngine.IsMetadataSymbol(candidateGetterSymbol))
        {
            containingType = candidateContainingType;
            getterSymbol = candidateGetterSymbol;
            return true;
        }

        containingType = null!;
        getterSymbol = null!;
        return false;
    }

    private static bool IsTrustedGeneratedPureMetadataGetter(
        IMethodSymbol getterSymbol,
        PurityAnalysisContext context)
    {
        return PurityAnalysisEngine.TryGetTrustedDefinitiveGeneratedPurity(
                   getterSymbol,
                   context.SemanticModel.Compilation,
                   out var generatedPurity) &&
               generatedPurity.IsPure;
    }
}
