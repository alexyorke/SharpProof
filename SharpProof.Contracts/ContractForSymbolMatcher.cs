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

    internal static bool CompanionTypeMatches(
        INamedTypeSymbol companion,
        (INamedTypeSymbol Target, bool IsOpen) contractTarget)
    {
        return companion is { TypeKind: TypeKind.Class, IsStatic: true } &&
        (contractTarget.IsOpen
            ? companion.Arity == contractTarget.Target.Arity &&
              TypeParameterListsMatch(contractTarget.Target.TypeParameters, companion.TypeParameters)
            : companion.Arity == 0);
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
        foreach (var type in GetAllTypes(compilation.Assembly.GlobalNamespace, cancellationToken))
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
        if (!string.Equals(target.Name, companion.Name, StringComparison.Ordinal) ||
            !companion.IsStatic ||
            companion.Arity != target.Arity ||
            companion.ReturnsByRef != target.ReturnsByRef ||
            companion.ReturnsByRefReadonly != target.ReturnsByRefReadonly ||
            !TypesMatch(target.ReturnType, companion.ReturnType, target, companion))
        {
            return false;
        }

        var offset = target.IsStatic ? 0 : 1;
        return companion.Parameters.Length == target.Parameters.Length + offset &&
               (target.IsStatic || IsReceiver(target, companion.Parameters[0])) &&
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
                ? companion.Type.Construct([.. target.ContainingType.TypeArguments])
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

            return CompanionResolution.Success(ContractClauseInventoryBuilder.NormalizeCallable(method));
        }
        catch (ArgumentException)
        {
            return CompanionResolution.Fail(ContractBindingFailure.CompanionSignatureMismatch);
        }
    }

    private static bool HasUniqueTarget(IMethodSymbol target, IMethodSymbol companion)
    {
        return GetOrdinaryMethods(target.ContainingType)
            .Count(candidate => MemberSignaturesMatch(candidate, companion)) == 1;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllTypes(
        INamespaceOrTypeSymbol container,
        CancellationToken cancellationToken)
    {
        foreach (var type in container.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return type;
            foreach (var nested in GetAllTypes(type, cancellationToken))
            {
                yield return nested;
            }
        }
        if (container is not INamespaceSymbol @namespace)
        {
            yield break;
        }

        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in GetAllTypes(child, cancellationToken))
            {
                yield return type;
            }
        }
    }

    private static bool IsReceiver(IMethodSymbol target, IParameterSymbol receiver)
    {
        return receiver is { RefKind: RefKind.None, ScopedKind: ScopedKind.None, IsParams: false, IsOptional: false } &&
        TypesMatch(target.ContainingType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
            receiver.Type, target, receiver.ContainingSymbol, normalizeMappedTypeParameters: true);
    }

    private static bool ParametersMatch(IParameterSymbol left, IParameterSymbol right)
    {
        return left.RefKind == right.RefKind &&
        left.ScopedKind == right.ScopedKind &&
        left.IsParams == right.IsParams &&
        left.IsOptional == right.IsOptional &&
        left.HasExplicitDefaultValue == right.HasExplicitDefaultValue &&
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
        if (left.HasConstructorConstraint != right.HasConstructorConstraint ||
            left.HasReferenceTypeConstraint != right.HasReferenceTypeConstraint ||
            left.ReferenceTypeConstraintNullableAnnotation != right.ReferenceTypeConstraintNullableAnnotation ||
            left.HasValueTypeConstraint != right.HasValueTypeConstraint ||
            left.HasNotNullConstraint != right.HasNotNullConstraint ||
            left.HasUnmanagedTypeConstraint != right.HasUnmanagedTypeConstraint ||
            left.AllowsRefLikeType != right.AllowsRefLikeType ||
            left.ConstraintTypes.Length != right.ConstraintTypes.Length)
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

        if (GetAnnotation(left, normalizeMappedTypeParameters) !=
                GetAnnotation(right, normalizeMappedTypeParameters) ||
            left.TypeKind != right.TypeKind ||
            left.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (left is ITypeParameterSymbol leftParameter &&
            right is ITypeParameterSymbol rightParameter)
        {
            return leftParameter.TypeParameterKind == rightParameter.TypeParameterKind &&
                   leftParameter.Ordinal == rightParameter.Ordinal &&
                   OwnersMatch(leftParameter.ContainingSymbol,
                       rightParameter.ContainingSymbol, leftScope, rightScope);
        }

        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        {
            return leftArray.Rank == rightArray.Rank &&
                   leftArray.IsSZArray == rightArray.IsSZArray &&
                   TypesMatch(leftArray.ElementType, rightArray.ElementType,
                       leftScope, rightScope, normalizeMappedTypeParameters);
        }

        if (left is IPointerTypeSymbol leftPointer && right is IPointerTypeSymbol rightPointer)
        {
            return TypesMatch(leftPointer.PointedAtType, rightPointer.PointedAtType,
                leftScope, rightScope, normalizeMappedTypeParameters);
        }

        if (left is not INamedTypeSymbol leftNamed || right is not INamedTypeSymbol rightNamed)
        {
            return SymbolEqualityComparer.IncludeNullability.Equals(left, right);
        }

        if (!SymbolEqualityComparer.Default.Equals(
                leftNamed.OriginalDefinition, rightNamed.OriginalDefinition) ||
            !TypesMatch(leftNamed.ContainingType, rightNamed.ContainingType,
                leftScope, rightScope, normalizeMappedTypeParameters) ||
            leftNamed.TypeArguments.Length != rightNamed.TypeArguments.Length ||
            leftNamed.IsTupleType != rightNamed.IsTupleType ||
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
