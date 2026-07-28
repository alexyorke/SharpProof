namespace SharpProof.Contracts;

internal static class ContractForSymbolMatcher {
    internal const string AttributeMetadataName = "SharpProof.Attributes.ContractForAttribute";

    internal static ImmutableArray<AttributeData> GetAttributes(INamedTypeSymbol companion, INamedTypeSymbol contractFor) =>
        [.. companion.GetAttributes()
            .Where(attribute => SymbolEqualityComparer.Default.Equals(
                attribute.AttributeClass?.OriginalDefinition, contractFor.OriginalDefinition))];

    internal static bool TryGetTarget(
        AttributeData attribute,
        out (INamedTypeSymbol Target, bool IsOpen) target) {
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Kind != TypedConstantKind.Type ||
            attribute.ConstructorArguments[0].Value is not INamedTypeSymbol targetType ||
            targetType.TypeKind == TypeKind.Error) {
            target = default;
            return false;
        }

        var isOpen = targetType.IsUnboundGenericType;
        target = new(isOpen ? targetType.OriginalDefinition : targetType, isOpen);
        return true;
    }

    internal static bool TargetsType((INamedTypeSymbol Target, bool IsOpen) contractTarget, INamedTypeSymbol target) =>
        SymbolEqualityComparer.Default.Equals(contractTarget.Target,
            contractTarget.IsOpen ? target.OriginalDefinition : target);

    internal static bool CompanionTypeMatches(
        INamedTypeSymbol companion,
        (INamedTypeSymbol Target, bool IsOpen) contractTarget) =>
        companion.TypeKind == TypeKind.Class &&
        companion.IsStatic &&
        (contractTarget.IsOpen
            ? companion.Arity == contractTarget.Target.Arity &&
              TypeParameterListsMatch(contractTarget.Target.TypeParameters, companion.TypeParameters)
            : companion.Arity == 0);

    internal static ImmutableArray<IMethodSymbol> GetOrdinaryMethods(
        INamedTypeSymbol type) =>
        [.. type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(static method =>
                method.MethodKind == MethodKind.Ordinary &&
                !method.IsImplicitlyDeclared)];

    internal static bool MemberSignaturesMatch(
        IMethodSymbol target,
        IMethodSymbol companion) {
        if (!string.Equals(target.Name, companion.Name, StringComparison.Ordinal) ||
            !companion.IsStatic ||
            companion.Arity != target.Arity ||
            companion.ReturnsByRef != target.ReturnsByRef ||
            companion.ReturnsByRefReadonly != target.ReturnsByRefReadonly ||
            !TypesMatch(target.ReturnType, companion.ReturnType, target, companion))
            return false;
        var receiverOffset = target.IsStatic ? 0 : 1;
        if (companion.Parameters.Length != target.Parameters.Length + receiverOffset)
            return false;
        return (target.IsStatic ||
                IsReceiver(target, companion.Parameters[0])) &&
               Enumerable.Range(0, target.Parameters.Length).All(index => ParametersMatch(
                   target.Parameters[index], companion.Parameters[index + receiverOffset])) &&
               TypeParameterListsMatch(target.TypeParameters, companion.TypeParameters);
    }

    private static bool IsReceiver(
        IMethodSymbol target,
        IParameterSymbol receiver) =>
        receiver.RefKind == RefKind.None &&
        receiver.ScopedKind == ScopedKind.None &&
        !receiver.IsParams &&
        !receiver.IsOptional &&
        TypesMatch(target.ContainingType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
            receiver.Type, target, receiver.ContainingSymbol,
            normalizeMappedTypeParameters: true);

    private static bool ParametersMatch(
        IParameterSymbol left,
        IParameterSymbol right) =>
        left.RefKind == right.RefKind &&
        left.ScopedKind == right.ScopedKind &&
        left.IsParams == right.IsParams &&
        left.IsOptional == right.IsOptional &&
        left.HasExplicitDefaultValue == right.HasExplicitDefaultValue &&
        (!left.HasExplicitDefaultValue ||
         Equals(left.ExplicitDefaultValue, right.ExplicitDefaultValue)) &&
        TypesMatch(left.Type, right.Type, left.ContainingSymbol, right.ContainingSymbol);

    private static bool TypeParameterListsMatch(ImmutableArray<ITypeParameterSymbol> left,
        ImmutableArray<ITypeParameterSymbol> right) =>
        left.Length == right.Length &&
        Enumerable.Range(0, left.Length).All(index => TypeParameterConstraintsMatch(left[index], right[index]));

    private static bool TypeParameterConstraintsMatch(
        ITypeParameterSymbol left,
        ITypeParameterSymbol right) {
        if (left.HasConstructorConstraint != right.HasConstructorConstraint ||
            left.HasReferenceTypeConstraint != right.HasReferenceTypeConstraint ||
            left.ReferenceTypeConstraintNullableAnnotation !=
            right.ReferenceTypeConstraintNullableAnnotation ||
            left.HasValueTypeConstraint != right.HasValueTypeConstraint ||
            left.HasNotNullConstraint != right.HasNotNullConstraint ||
            left.HasUnmanagedTypeConstraint != right.HasUnmanagedTypeConstraint ||
            left.AllowsRefLikeType != right.AllowsRefLikeType ||
            left.ConstraintTypes.Length != right.ConstraintTypes.Length)
            return false;
        var unmatched = right.ConstraintTypes.ToList();
        foreach (var leftConstraint in left.ConstraintTypes) {
            var index = unmatched.FindIndex(rightConstraint => TypesMatch(
                leftConstraint, rightConstraint, left.ContainingSymbol, right.ContainingSymbol));
            if (index < 0) return false;
            unmatched.RemoveAt(index);
        }
        return true;
    }

    private static bool TypesMatch(ITypeSymbol? left, ITypeSymbol? right,
        ISymbol leftScope, ISymbol rightScope,
        bool normalizeMappedTypeParameters = false) {
        if (left == null || right == null)
            return left is null && right is null;
        if (GetAnnotation(left, normalizeMappedTypeParameters) !=
                GetAnnotation(right, normalizeMappedTypeParameters) ||
            left.TypeKind != right.TypeKind ||
            left.TypeKind == TypeKind.Error)
            return false;
        if (left is ITypeParameterSymbol leftParameter &&
            right is ITypeParameterSymbol rightParameter)
            return leftParameter.TypeParameterKind == rightParameter.TypeParameterKind &&
                   leftParameter.Ordinal == rightParameter.Ordinal &&
                   OwnersMatch(leftParameter.ContainingSymbol,
                       rightParameter.ContainingSymbol, leftScope, rightScope);
        if (left is IArrayTypeSymbol leftArray &&
            right is IArrayTypeSymbol rightArray)
            return leftArray.Rank == rightArray.Rank &&
                   leftArray.IsSZArray == rightArray.IsSZArray &&
                   TypesMatch(leftArray.ElementType, rightArray.ElementType,
                       leftScope, rightScope,
                       normalizeMappedTypeParameters);
        if (left is IPointerTypeSymbol leftPointer &&
            right is IPointerTypeSymbol rightPointer)
            return TypesMatch(leftPointer.PointedAtType, rightPointer.PointedAtType,
                leftScope, rightScope,
                normalizeMappedTypeParameters);
        if (left is INamedTypeSymbol leftNamed &&
            right is INamedTypeSymbol rightNamed) {
            if (!SymbolEqualityComparer.Default.Equals(leftNamed.OriginalDefinition,
                    rightNamed.OriginalDefinition) ||
                !TypesMatch(leftNamed.ContainingType, rightNamed.ContainingType,
                    leftScope, rightScope,
                    normalizeMappedTypeParameters) ||
                leftNamed.TypeArguments.Length != rightNamed.TypeArguments.Length ||
                leftNamed.IsTupleType != rightNamed.IsTupleType ||
                !Enumerable.Range(0, leftNamed.TypeArguments.Length).All(index => TypesMatch(
                        leftNamed.TypeArguments[index], rightNamed.TypeArguments[index],
                        leftScope, rightScope,
                        normalizeMappedTypeParameters)))
                return false;
            return !leftNamed.IsTupleType ||
                   leftNamed.TupleElements.Length == rightNamed.TupleElements.Length &&
                   Enumerable.Range(0, leftNamed.TupleElements.Length)
                       .All(index => string.Equals(leftNamed.TupleElements[index].Name,
                           rightNamed.TupleElements[index].Name, StringComparison.Ordinal));
        }
        return SymbolEqualityComparer.IncludeNullability.Equals(left, right);
    }

    private static bool OwnersMatch(ISymbol left, ISymbol right,
        ISymbol? leftScope, ISymbol? rightScope) =>
        leftScope != null &&
        rightScope != null &&
        (SymbolEqualityComparer.Default.Equals(left, leftScope)
            ? SymbolEqualityComparer.Default.Equals(right, rightScope)
            : !SymbolEqualityComparer.Default.Equals(right, rightScope) &&
              OwnersMatch(left, right, leftScope.ContainingSymbol,
                  rightScope.ContainingSymbol));

    private static NullableAnnotation GetAnnotation(
        ITypeSymbol type,
        bool normalizeMappedTypeParameters) =>
        normalizeMappedTypeParameters &&
        type is ITypeParameterSymbol &&
        type.NullableAnnotation == NullableAnnotation.None
            ? NullableAnnotation.NotAnnotated
            : type.NullableAnnotation;
}
