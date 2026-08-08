namespace SharpProof.Contracts;

internal static class ContractForSymbolMatcher
{
    internal sealed class CompanionDescriptor(
        INamedTypeSymbol type,
        (INamedTypeSymbol Target, bool IsOpen) contractTarget)
    {
        internal INamedTypeSymbol Type { get; } = type;
        internal (INamedTypeSymbol Target, bool IsOpen) ContractTarget { get; } = contractTarget;
        internal INamedTypeSymbol Target => ContractTarget.Target;
    }

    internal sealed class CompanionResolution(
        IMethodSymbol? method,
        ContractBindingFailure failure)
    {
        internal IMethodSymbol? Method { get; } = method;
        internal ContractBindingFailure Failure { get; } = failure;
        internal static CompanionResolution None { get; } = new(null, ContractBindingFailure.None);
        internal static CompanionResolution Success(IMethodSymbol method)
        {
            return new(method, ContractBindingFailure.None);
        }

        internal static CompanionResolution Fail(ContractBindingFailure failure)
        {
            return new(null, failure);
        }
    }

    internal static ImmutableArray<AttributeData> GetAttributes(
        INamedTypeSymbol companion,
        INamedTypeSymbol contractFor)
    {
        return [.. companion.GetAttributes().Where(attribute =>
            SymbolEqualityComparer.Default.Equals(
                attribute.AttributeClass?.OriginalDefinition, contractFor.OriginalDefinition))];
    }

    internal static bool TryGetTarget(
        AttributeData attribute,
        out (INamedTypeSymbol Target, bool IsOpen) target)
    {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0] is not
            {
                Kind: TypedConstantKind.Type,
                Value: INamedTypeSymbol type
            } ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Interface))
        {
            target = default;
            return false;
        }
        target = new(type.IsUnboundGenericType ? type.OriginalDefinition : type, type.IsUnboundGenericType);
        return true;
    }

    internal static bool TargetsType(
        (INamedTypeSymbol Target, bool IsOpen) contractTarget,
        INamedTypeSymbol target)
    {
        return SymbolEqualityComparer.Default.Equals(
            contractTarget.Target, contractTarget.IsOpen ? target.OriginalDefinition : target);
    }

    internal static bool TargetsOverlap(
        (INamedTypeSymbol Target, bool IsOpen) left,
        (INamedTypeSymbol Target, bool IsOpen) right)
    {
        return SymbolEqualityComparer.Default.Equals(
                   left.Target.OriginalDefinition,
                   right.Target.OriginalDefinition) &&
               (left.IsOpen || right.IsOpen ||
                SymbolEqualityComparer.Default.Equals(left.Target, right.Target));
    }

    internal static bool CompanionTypeMatches(
        INamedTypeSymbol companion,
        (INamedTypeSymbol Target, bool IsOpen) contractTarget)
    {
        if (companion is not { TypeKind: TypeKind.Class, IsStatic: true })
        {
            return false;
        }

        var companionLayers = GetGenericTypeLayers(companion);
        if (!contractTarget.IsOpen)
        {
            return companionLayers.All(static layer => layer.Arity == 0);
        }

        var targetLayers = GetGenericTypeLayers(contractTarget.Target);
        return targetLayers.Length == companionLayers.Length &&
               targetLayers.Select((layer, index) =>
                       TypeParameterListsMatch(
                           layer.TypeParameters,
                           companionLayers[index].TypeParameters))
                   .All(static matches => matches);
    }

    internal static ImmutableArray<IMethodSymbol> GetOrdinaryMethods(INamedTypeSymbol type)
    {
        return [.. type.GetMembers().OfType<IMethodSymbol>().Where(static method =>
            method is { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false })];
    }

    internal static ImmutableArray<CompanionDescriptor> DiscoverCompanions(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contractFor = ContractSelectionInventory.ForCompilation(compilation).ContractFor;
        if (contractFor == null)
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<CompanionDescriptor>();
        foreach (var type in ReferencedTypeSymbols.GetAll(
                     compilation,
                     cancellationToken))
        {
            var attributes = GetAttributes(type, contractFor);
            if (attributes.Length == 1 && TryGetTarget(attributes[0], out var target))
            {
                result.Add(new CompanionDescriptor(type, target));
            }
        }
        return result.ToImmutable();
    }

    internal static CompanionResolution ResolveCompanion(
        ImmutableArray<CompanionDescriptor> companions,
        IMethodSymbol target)
    {
        if (target.MethodKind != MethodKind.Ordinary)
        {
            return CompanionResolution.None;
        }

        var matching = companions.Where(companion =>
            TargetsType(companion.ContractTarget, target.ContainingType)).ToImmutableArray();
        if (matching.IsDefaultOrEmpty)
        {
            return CompanionResolution.None;
        }

        if (matching.Length != 1)
        {
            return CompanionResolution.Fail(ContractBindingFailure.AmbiguousCompanion);
        }

        var companion = matching[0];
        if (!CompanionTypeMatches(companion.Type, companion.ContractTarget))
        {
            return CompanionResolution.Fail(ContractBindingFailure.CompanionSignatureMismatch);
        }

        var signatureTarget = companion.ContractTarget.IsOpen
            ? target.OriginalDefinition
            : target.ConstructedFrom;
        var named = GetOrdinaryMethods(companion.Type)
            .Where(candidate => string.Equals(candidate.Name, target.Name, StringComparison.Ordinal))
            .ToImmutableArray();
        var matches = named.Where(candidate =>
            MemberSignaturesMatch(signatureTarget, candidate)).ToImmutableArray();
        if (matches.Length == 1)
        {
            return HasUniqueTarget(signatureTarget, matches[0])
                ? SpecializeCompanion(companion, matches[0], target)
                : CompanionResolution.Fail(ContractBindingFailure.AmbiguousCompanion);
        }

        return CompanionResolution.Fail(matches.Length > 1
            ? ContractBindingFailure.AmbiguousCompanion
            : named.IsDefaultOrEmpty
                ? ContractBindingFailure.MissingCompanion
                : ContractBindingFailure.CompanionSignatureMismatch);
    }

    internal static bool IsCompanionType(
        ImmutableArray<CompanionDescriptor> companions,
        INamedTypeSymbol type)
    {
        return companions.Any(companion => SymbolEqualityComparer.Default.Equals(
            companion.Type.OriginalDefinition, type.OriginalDefinition));
    }

    internal static bool MemberSignaturesMatch(
        IMethodSymbol target,
        IMethodSymbol companion)
    {
        if ((target.Name, target.Arity, target.ReturnsByRef,
                target.ReturnsByRefReadonly, true) !=
            (companion.Name, companion.Arity, companion.ReturnsByRef,
                companion.ReturnsByRefReadonly, companion.IsStatic) ||
            !TypesMatch(target.ReturnType, companion.ReturnType, target, companion))
        {
            return false;
        }

        var offset = target.IsStatic ? 0 : 1;
        if (companion.Parameters.Length != target.Parameters.Length + offset)
        {
            return false;
        }

        var receiverMatches =
            target.IsStatic || IsReceiver(target, companion.Parameters[0]);
        return receiverMatches &&
               target.Parameters.Select((parameter, index) =>
                       ParametersMatch(parameter, companion.Parameters[index + offset]))
                   .All(static matches => matches) &&
               TypeParameterListsMatch(target.TypeParameters, companion.TypeParameters);
    }

    private static CompanionResolution SpecializeCompanion(
        CompanionDescriptor companion,
        IMethodSymbol definition,
        IMethodSymbol target)
    {
        try
        {
            var type = companion.ContractTarget.IsOpen
                ? ConstructCompanionType(companion.Type, target.ContainingType)
                : companion.Type;
            var method = type.GetMembers(definition.Name).OfType<IMethodSymbol>()
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition, definition.OriginalDefinition));
            if (method == null)
            {
                return CompanionResolution.Fail(ContractBindingFailure.CompanionSignatureMismatch);
            }

            if (method.Arity != 0)
            {
                method = method.Construct([.. target.TypeArguments]);
            }

            return (!RequiresConstructedSignatureCheck(target) ||
                    MemberSignaturesMatch(target, method))
                ? CompanionResolution.Success(
                    ContractClauseInventoryBuilder.NormalizeCallable(method))
                : CompanionResolution.Fail(
                    ContractBindingFailure.CompanionSignatureMismatch);
        }
        catch (ArgumentException)
        {
            return CompanionResolution.Fail(ContractBindingFailure.CompanionSignatureMismatch);
        }
    }

    private static INamedTypeSymbol ConstructCompanionType(
        INamedTypeSymbol companion,
        INamedTypeSymbol target)
    {
        var companionLayers = GetTypeLayers(companion);
        var targetLayers = GetGenericTypeLayers(target);
        if (companionLayers.Count(static layer => layer.Arity > 0) !=
            targetLayers.Length)
        {
            throw new ArgumentException("Companion generic layers do not match the target.");
        }

        INamedTypeSymbol? constructed = null;
        var targetIndex = 0;
        for (var index = 0; index < companionLayers.Length; index++)
        {
            var definition = constructed == null
                ? companionLayers[index]
                : constructed.GetTypeMembers(
                        companionLayers[index].Name,
                        companionLayers[index].Arity)
                    .FirstOrDefault(candidate =>
                        SymbolEqualityComparer.Default.Equals(
                            candidate.OriginalDefinition,
                            companionLayers[index].OriginalDefinition)) ??
                  throw new ArgumentException(
                      "The nested companion type could not be specialized.");
            constructed = definition.Arity == 0
                ? definition
                : definition.Construct([
                    .. targetLayers[targetIndex++].TypeArguments
                ]);
        }

        return constructed ?? throw new ArgumentException(
            "The companion type could not be specialized.");
    }

    private static bool RequiresConstructedSignatureCheck(
        IMethodSymbol target)
    {
        return !SymbolEqualityComparer.Default.Equals(
                   target, target.OriginalDefinition) ||
               GetTypeLayers(target.ContainingType).Any(layer =>
                   !SymbolEqualityComparer.Default.Equals(
                       layer, layer.OriginalDefinition));
    }

    private static ImmutableArray<INamedTypeSymbol> GetGenericTypeLayers(
        INamedTypeSymbol type)
    {
        return [.. GetTypeLayers(type).Where(static layer => layer.Arity > 0)];
    }

    private static ImmutableArray<INamedTypeSymbol> GetTypeLayers(
        INamedTypeSymbol type)
    {
        var layers = new Stack<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type;
             current != null;
             current = current.ContainingType)
        {
            layers.Push(current);
        }
        return [.. layers];
    }

    private static bool HasUniqueTarget(IMethodSymbol target, IMethodSymbol companion)
    {
        return GetOrdinaryMethods(target.ContainingType)
            .Count(candidate => MemberSignaturesMatch(candidate, companion)) == 1;
    }

    private static bool IsReceiver(IMethodSymbol target, IParameterSymbol receiver)
    {
        return receiver is { RefKind: RefKind.None, ScopedKind: ScopedKind.None, IsParams: false, IsOptional: false } &&
        TypesMatch(target.ContainingType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
            receiver.Type, target, receiver.ContainingSymbol, normalizeMappedTypeParameters: true);
    }

    private static bool ParametersMatch(IParameterSymbol left, IParameterSymbol right)
    {
        return (left.RefKind, left.ScopedKind, left.IsParams,
                   left.IsOptional, left.HasExplicitDefaultValue) ==
               (right.RefKind, right.ScopedKind, right.IsParams,
                   right.IsOptional, right.HasExplicitDefaultValue) &&
        (!left.HasExplicitDefaultValue || Equals(left.ExplicitDefaultValue, right.ExplicitDefaultValue)) &&
        TypesMatch(left.Type, right.Type, left.ContainingSymbol, right.ContainingSymbol);
    }

    private static bool TypeParameterListsMatch(
        ImmutableArray<ITypeParameterSymbol> left,
        ImmutableArray<ITypeParameterSymbol> right)
    {
        return left.Length == right.Length &&
        left.Select((parameter, index) =>
                TypeParameterConstraintsMatch(parameter, right[index]))
            .All(static matches => matches);
    }

    private static bool TypeParameterConstraintsMatch(
        ITypeParameterSymbol left,
        ITypeParameterSymbol right)
    {
        if ((left.HasConstructorConstraint, left.HasReferenceTypeConstraint,
                left.ReferenceTypeConstraintNullableAnnotation,
                left.HasValueTypeConstraint, left.HasNotNullConstraint,
                left.HasUnmanagedTypeConstraint, left.AllowsRefLikeType,
                left.ConstraintTypes.Length) !=
            (right.HasConstructorConstraint, right.HasReferenceTypeConstraint,
                right.ReferenceTypeConstraintNullableAnnotation,
                right.HasValueTypeConstraint, right.HasNotNullConstraint,
                right.HasUnmanagedTypeConstraint, right.AllowsRefLikeType,
                right.ConstraintTypes.Length))
        {
            return false;
        }

        var unmatched = right.ConstraintTypes.ToList();
        foreach (var constraint in left.ConstraintTypes)
        {
            var index = unmatched.FindIndex(candidate =>
                TypesMatch(constraint, candidate, left.ContainingSymbol, right.ContainingSymbol));
            if (index < 0)
            {
                return false;
            }

            unmatched.RemoveAt(index);
        }
        return true;
    }

    private static bool TypesMatch(
        ITypeSymbol? left,
        ITypeSymbol? right,
        ISymbol leftScope,
        ISymbol rightScope,
        bool normalizeMappedTypeParameters = false)
    {
        if (left == null || right == null)
        {
            return left is null && right is null;
        }

        if ((GetAnnotation(left, normalizeMappedTypeParameters), left.TypeKind) !=
                (GetAnnotation(right, normalizeMappedTypeParameters), right.TypeKind) ||
            left.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (left is ITypeParameterSymbol leftParameter &&
            right is ITypeParameterSymbol rightParameter)
        {
            return (leftParameter.TypeParameterKind, leftParameter.Ordinal) ==
                   (rightParameter.TypeParameterKind, rightParameter.Ordinal) &&
                   OwnersMatch(leftParameter.ContainingSymbol,
                       rightParameter.ContainingSymbol, leftScope, rightScope);
        }

        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        {
            return (leftArray.Rank, leftArray.IsSZArray) ==
                   (rightArray.Rank, rightArray.IsSZArray) &&
                   TypesMatch(leftArray.ElementType, rightArray.ElementType,
                       leftScope, rightScope, normalizeMappedTypeParameters);
        }

        if (left is IPointerTypeSymbol leftPointer && right is IPointerTypeSymbol rightPointer)
        {
            return TypesMatch(leftPointer.PointedAtType, rightPointer.PointedAtType,
                leftScope, rightScope, normalizeMappedTypeParameters);
        }

        if (left is IFunctionPointerTypeSymbol leftFunction &&
            right is IFunctionPointerTypeSymbol rightFunction)
        {
            return FunctionPointerSignaturesMatch(
                leftFunction.Signature,
                rightFunction.Signature,
                leftScope,
                rightScope,
                normalizeMappedTypeParameters);
        }

        if (left is not INamedTypeSymbol leftNamed || right is not INamedTypeSymbol rightNamed)
        {
            return SymbolEqualityComparer.IncludeNullability.Equals(left, right);
        }

        if ((leftNamed.TypeArguments.Length, leftNamed.IsTupleType) !=
                (rightNamed.TypeArguments.Length, rightNamed.IsTupleType) ||
            !SymbolEqualityComparer.Default.Equals(
                leftNamed.OriginalDefinition, rightNamed.OriginalDefinition) ||
            !TypesMatch(leftNamed.ContainingType, rightNamed.ContainingType,
                leftScope, rightScope, normalizeMappedTypeParameters) ||
            !leftNamed.TypeArguments.Select((argument, index) =>
                    TypesMatch(argument, rightNamed.TypeArguments[index],
                        leftScope, rightScope, normalizeMappedTypeParameters))
                .All(static matches => matches))
        {
            return false;
        }

        return !leftNamed.IsTupleType ||
               leftNamed.TupleElements.Length == rightNamed.TupleElements.Length &&
               leftNamed.TupleElements.Select((element, index) =>
                       string.Equals(element.Name,
                           rightNamed.TupleElements[index].Name, StringComparison.Ordinal))
                   .All(static matches => matches);
    }

    private static bool FunctionPointerSignaturesMatch(
        IMethodSymbol left,
        IMethodSymbol right,
        ISymbol leftScope,
        ISymbol rightScope,
        bool normalizeMappedTypeParameters)
    {
        return (left.CallingConvention, left.ReturnsByRef,
                   left.ReturnsByRefReadonly, left.Parameters.Length) ==
               (right.CallingConvention, right.ReturnsByRef,
                   right.ReturnsByRefReadonly, right.Parameters.Length) &&
               TypesMatch(left.ReturnType, right.ReturnType,
                   leftScope, rightScope, normalizeMappedTypeParameters) &&
               CustomModifiersMatch(
                   left.ReturnTypeCustomModifiers,
                   right.ReturnTypeCustomModifiers) &&
               CustomModifiersMatch(
                   left.RefCustomModifiers,
                   right.RefCustomModifiers) &&
               left.UnmanagedCallingConventionTypes.Length ==
                   right.UnmanagedCallingConventionTypes.Length &&
               left.UnmanagedCallingConventionTypes.Select((type, index) =>
                       SymbolEqualityComparer.Default.Equals(
                           type,
                           right.UnmanagedCallingConventionTypes[index]))
                   .All(static matches => matches) &&
               left.Parameters.Select((parameter, index) =>
                       FunctionPointerParametersMatch(
                           parameter,
                           right.Parameters[index],
                           leftScope,
                           rightScope,
                           normalizeMappedTypeParameters))
                   .All(static matches => matches);
    }

    private static bool FunctionPointerParametersMatch(
        IParameterSymbol left,
        IParameterSymbol right,
        ISymbol leftScope,
        ISymbol rightScope,
        bool normalizeMappedTypeParameters)
    {
        return (left.RefKind, left.ScopedKind) ==
                   (right.RefKind, right.ScopedKind) &&
               CustomModifiersMatch(left.CustomModifiers, right.CustomModifiers) &&
               CustomModifiersMatch(left.RefCustomModifiers, right.RefCustomModifiers) &&
               TypesMatch(left.Type, right.Type,
                   leftScope, rightScope, normalizeMappedTypeParameters);
    }

    private static bool CustomModifiersMatch(
        ImmutableArray<CustomModifier> left,
        ImmutableArray<CustomModifier> right)
    {
        return left.Length == right.Length &&
               left.Select((modifier, index) =>
                       modifier.IsOptional == right[index].IsOptional &&
                       SymbolEqualityComparer.Default.Equals(
                           modifier.Modifier, right[index].Modifier))
                   .All(static matches => matches);
    }

    private static bool OwnersMatch(
        ISymbol left,
        ISymbol right,
        ISymbol? leftScope,
        ISymbol? rightScope)
    {
        return leftScope != null &&
        rightScope != null &&
        (SymbolEqualityComparer.Default.Equals(left, leftScope)
            ? SymbolEqualityComparer.Default.Equals(right, rightScope)
            : !SymbolEqualityComparer.Default.Equals(right, rightScope) &&
              OwnersMatch(left, right, leftScope.ContainingSymbol, rightScope.ContainingSymbol));
    }

    private static NullableAnnotation GetAnnotation(
        ITypeSymbol type,
        bool normalizeMappedTypeParameters)
    {
        return normalizeMappedTypeParameters &&
        type is ITypeParameterSymbol &&
        type.NullableAnnotation == NullableAnnotation.None
            ? NullableAnnotation.NotAnnotated
            : type.NullableAnnotation;
    }
}
