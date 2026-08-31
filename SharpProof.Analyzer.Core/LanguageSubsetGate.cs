namespace SharpProof.Analyzer;

internal enum LanguageSubsetAbstentionReason
{
    None,
    UnsupportedCallable,
    MissingOperationRoot,
    UnsupportedOperationKind,
    UnsupportedType,
    UnsupportedOperationShape
}

internal readonly partial record struct LanguageSubsetDecision
{
    internal static LanguageSubsetDecision Supported
    {
        get;
    } =
        new(true, LanguageSubsetAbstentionReason.None, null);

    internal static LanguageSubsetDecision Abstain(
        LanguageSubsetAbstentionReason reason,
        OperationKind? operationKind = null)
    {
        if (reason == LanguageSubsetAbstentionReason.None)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        return new LanguageSubsetDecision(false, reason, operationKind);
    }
}

internal static class LanguageSubsetGate
{
    internal static readonly ImmutableDictionary<OperationKind, bool> OperationKindDecisions =
        Enum.GetValues(typeof(OperationKind)).Cast<OperationKind>().Distinct()
            .ToImmutableDictionary(
                static kind => kind,
                static kind => OperationSupportCatalog.IsSupported(
                    OperationSupportStage.EffectDiscovery,
                    kind));

    internal static LanguageSubsetDecision ClassifyEffects(
        IMethodSymbol method,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        ImmutableArray<IOperation> operationBlocks,
        Func<IMethodSymbol, bool> hasResolvedGenericApiSpec,
        CancellationToken cancellationToken)
    {
        if (!SupportsCallable(method, declaration))
        {
            return LanguageSubsetDecision.Abstain(
                LanguageSubsetAbstentionReason.UnsupportedCallable);
        }

        var roots = operationBlocks.IsDefaultOrEmpty
            ? GetFallbackRoots(declaration, semanticModel, cancellationToken)
            : operationBlocks;
        if (roots.IsDefaultOrEmpty)
        {
            return LanguageSubsetDecision.Abstain(
                LanguageSubsetAbstentionReason.MissingOperationRoot);
        }

        foreach (var root in roots)
        {
            foreach (var operation in root.DescendantsAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The outer callable's language subset must not be decided by
                // an unused local function or lambda. Those callables are
                // analyzed independently when selected/reachable.
                if (operation != root && IsNestedCallableOperation(operation))
                {
                    continue;
                }
                if (!OperationKindDecisions.TryGetValue(operation.Kind, out var supported) || !supported)
                {
                    return LanguageSubsetDecision.Abstain(
                        LanguageSubsetAbstentionReason.UnsupportedOperationKind,
                        operation.Kind);
                }

                if (IsUnsupportedType(operation.Type))
                {
                    return LanguageSubsetDecision.Abstain(
                        LanguageSubsetAbstentionReason.UnsupportedType,
                        operation.Kind);
                }

                if (!SupportsOperationShape(operation, hasResolvedGenericApiSpec))
                {
                    return LanguageSubsetDecision.Abstain(
                        LanguageSubsetAbstentionReason.UnsupportedOperationShape,
                        operation.Kind);
                }
            }
        }

        return LanguageSubsetDecision.Supported;
    }

    private static bool IsNestedCallableOperation(IOperation operation)
    {
        for (var parent = operation.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is ILocalFunctionOperation or IAnonymousFunctionOperation)
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<IOperation> GetFallbackRoots(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var root = semanticModel.GetOperation(declaration, cancellationToken);
        if (root != null)
        {
            return [root];
        }

        var fallback = ContractClauseInventoryBuilder.GetBody(declaration);
        root = fallback == null
            ? null
            : semanticModel.GetOperation(fallback, cancellationToken);
        return root == null ? [] : [root];
    }

    private static bool SupportsCallable(IMethodSymbol method, SyntaxNode declaration)
    {
        if (method.IsAsync ||
            method.TypeParameters.Length != 0 ||
            method.ReturnsByRef ||
            method.ReturnsByRefReadonly ||
            IsUnsupportedType(method.ReturnType) ||
            method.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None ||
                IsUnsupportedType(parameter.Type)) ||
            ContainsUnsafeSyntax(declaration))
        {
            return false;
        }

        if (declaration is TypeDeclarationSyntax)
        {
            return false;
        }

        return method.MethodKind is
            MethodKind.Ordinary or
            MethodKind.AnonymousFunction or
            MethodKind.Constructor or
            MethodKind.StaticConstructor or
            MethodKind.PropertyGet or
            MethodKind.PropertySet or
            MethodKind.EventAdd or
            MethodKind.EventRemove or
            MethodKind.ExplicitInterfaceImplementation;
    }

    private static bool SupportsOperationShape(
        IOperation operation,
        Func<IMethodSymbol, bool> hasResolvedGenericApiSpec)
    {
        return operation switch
        {
            ILoopOperation loop =>
                loop.LoopKind is LoopKind.While or LoopKind.For,
            ISwitchOperation @switch =>
                @switch.Cases.SelectMany(static @case => @case.Clauses)
                    .All(IsConstantSwitchClause),
            IArgumentOperation argument =>
                argument.Parameter?.RefKind is null or RefKind.None,
            IVariableDeclaratorOperation declarator =>
                declarator.Symbol.RefKind == RefKind.None &&
                !IsUnsupportedType(declarator.Symbol.Type),
            ILocalReferenceOperation local =>
                local.Local.RefKind == RefKind.None &&
                !IsUnsupportedType(local.Local.Type),
            IInvocationOperation invocation =>
                SupportsCall(invocation.TargetMethod, hasResolvedGenericApiSpec),
            IObjectCreationOperation creation =>
                creation.Constructor != null &&
                SupportsCall(creation.Constructor, hasResolvedGenericApiSpec),
            IPropertyReferenceOperation property =>
                SupportsProperty(property, hasResolvedGenericApiSpec),
            IConversionOperation conversion =>
                conversion.OperatorMethod == null,
            IInterpolationOperation interpolation =>
                interpolation.Alignment == null &&
                interpolation.FormatString == null,
            IUnaryOperation unary =>
                unary.OperatorMethod == null,
            IBinaryOperation binary =>
                binary.OperatorMethod == null,
            ICompoundAssignmentOperation compound =>
                compound.OperatorMethod == null,
            IIncrementOrDecrementOperation increment =>
                increment.OperatorMethod == null,
            _ => true
        };
    }

    private static bool IsConstantSwitchClause(ICaseClauseOperation clause)
    {
        return clause is IDefaultCaseClauseOperation ||
        clause is ISingleValueCaseClauseOperation { Value.ConstantValue.HasValue: true };
    }

    private static bool SupportsProperty(
        IPropertyReferenceOperation property,
        Func<IMethodSymbol, bool> hasResolvedGenericApiSpec)
    {
        if (!RequiresResolvedGenericApiSpec(property.Property.ContainingType))
        {
            return true;
        }

        return GetAccessedPropertyAccessors(property).All(accessor =>
            accessor != null && hasResolvedGenericApiSpec(accessor));
    }

    private static IEnumerable<IMethodSymbol?> GetAccessedPropertyAccessors(
        IPropertyReferenceOperation property)
    {
        if (property.Parent is INameOfOperation)
        {
            yield break;
        }

        if (property.Parent is ISimpleAssignmentOperation assignment &&
            ReferenceEquals(assignment.Target, property))
        {
            yield return property.Property.SetMethod;
            yield break;
        }

        if ((property.Parent is ICoalesceAssignmentOperation coalesce &&
             ReferenceEquals(coalesce.Target, property)) ||
            (property.Parent is ICompoundAssignmentOperation compound &&
             ReferenceEquals(compound.Target, property)) ||
            (property.Parent is IIncrementOrDecrementOperation increment &&
             ReferenceEquals(increment.Target, property)))
        {
            yield return property.Property.GetMethod;
            yield return property.Property.SetMethod;
            yield break;
        }

        yield return property.Property.GetMethod;
    }

    private static bool SupportsCall(
        IMethodSymbol method,
        Func<IMethodSymbol, bool> hasResolvedGenericApiSpec)
    {
        if (method.MethodKind is
            MethodKind.AnonymousFunction or
            MethodKind.DelegateInvoke or
            MethodKind.FunctionPointerSignature or
            MethodKind.LocalFunction)
        {
            return false;
        }

        if (method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None))
        {
            return false;
        }

        if (!RequiresResolvedGenericApiSpec(method) &&
            !RequiresResolvedGenericApiSpec(method.ContainingType))
        {
            return true;
        }

        return !ContainsOpenType(method) &&
               hasResolvedGenericApiSpec(method);
    }

    private static bool RequiresResolvedGenericApiSpec(ISymbol? symbol)
    {
        return symbol switch
        {
            IMethodSymbol method => method.IsGenericMethod,
            INamedTypeSymbol type => type.IsGenericType,
            _ => false
        };
    }

    private static bool ContainsOpenType(IMethodSymbol method)
    {
        return method.TypeArguments.Any(IsUnsupportedType) ||
        method.Parameters.Any(static parameter => IsUnsupportedType(parameter.Type)) ||
        IsUnsupportedType(method.ReturnType) ||
        method.ContainingType.TypeArguments.Any(IsUnsupportedType);
    }

    private static bool IsUnsupportedType(ITypeSymbol? type)
    {
        return type?.TypeKind is
            TypeKind.Delegate or
            TypeKind.Dynamic or
            TypeKind.FunctionPointer or
            TypeKind.Pointer or
            TypeKind.TypeParameter ||
        type switch
        {
            IArrayTypeSymbol array => IsUnsupportedType(array.ElementType),
            INamedTypeSymbol named =>
                named.IsRefLikeType ||
                named.TypeArguments.Any(IsUnsupportedType),
            _ => false
        };
    }

    private static bool ContainsUnsafeSyntax(SyntaxNode declaration)
    {
        return declaration.DescendantNodesAndSelf().Any(static node => node is UnsafeStatementSyntax) ||
        declaration.AncestorsAndSelf().Any(HasUnsafeModifier);
    }

    private static bool HasUnsafeModifier(SyntaxNode node)
    {
        var modifiers = node switch
        {
            BaseMethodDeclarationSyntax method => method.Modifiers,
            BasePropertyDeclarationSyntax property => property.Modifiers,
            AccessorDeclarationSyntax accessor => accessor.Modifiers,
            LocalFunctionStatementSyntax localFunction => localFunction.Modifiers,
            TypeDeclarationSyntax type => type.Modifiers,
            DelegateDeclarationSyntax @delegate => @delegate.Modifiers,
            _ => default
        };
        return modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.UnsafeKeyword));
    }
}
