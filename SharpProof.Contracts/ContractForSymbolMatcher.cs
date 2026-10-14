namespace SharpProof.Contracts;

internal static class ContractForSymbolMatcher
{
    internal enum CompanionRelationshipIssue
    {
        None,
        SelfTarget,
        Cycle
    }

    internal sealed class CompanionDescriptor(
        INamedTypeSymbol type,
        (INamedTypeSymbol Target, bool IsOpen) contractTarget)
    {
        internal INamedTypeSymbol Type { get; } = type;
        internal (INamedTypeSymbol Target, bool IsOpen) ContractTarget { get; } = contractTarget;
        internal INamedTypeSymbol Target => ContractTarget.Target;
    }

    internal static IReadOnlyDictionary<INamedTypeSymbol,
        ImmutableArray<CompanionDescriptor>> BuildCompanionTargetIndex(
        ImmutableArray<CompanionDescriptor> companions)
    {
        var indexed = new Dictionary<INamedTypeSymbol,
            ImmutableArray<CompanionDescriptor>.Builder>(
            SymbolEqualityComparer.Default);
        foreach (var companion in companions)
        {
            var key = companion.ContractTarget.IsOpen
                ? companion.Target.OriginalDefinition
                : companion.Target;
            if (!indexed.TryGetValue(key, out var bucket))
            {
                bucket = ImmutableArray.CreateBuilder<CompanionDescriptor>();
                indexed.Add(key, bucket);
            }
            bucket.Add(companion);
        }

        return indexed.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.ToImmutable(),
            (IEqualityComparer<INamedTypeSymbol>)
                SymbolEqualityComparer.Default);
    }

    internal sealed class CompanionRelationshipInventory(
        ImmutableArray<CompanionDescriptor> accepted,
        HashSet<INamedTypeSymbol> selfTargeting,
        HashSet<INamedTypeSymbol> cyclic)
    {
        private readonly HashSet<INamedTypeSymbol> _selfTargeting =
            selfTargeting;
        private readonly HashSet<INamedTypeSymbol> _cyclic = cyclic;

        internal ImmutableArray<CompanionDescriptor> Accepted { get; } =
            accepted;

        internal CompanionRelationshipIssue GetIssue(
            INamedTypeSymbol companion)
        {
            if (_selfTargeting.Contains(companion))
            {
                return CompanionRelationshipIssue.SelfTarget;
            }
            return _cyclic.Contains(companion)
                ? CompanionRelationshipIssue.Cycle
                : CompanionRelationshipIssue.None;
        }
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

    internal static (int Count, AttributeData? First) GetAttributes(
        INamedTypeSymbol companion,
        INamedTypeSymbol contractFor,
        Func<SyntaxTree, bool>? includeTree = null)
    {
        var count = 0;
        AttributeData? first = null;
        foreach (var attribute in companion.GetAttributes())
        {
            if (attribute.ApplicationSyntaxReference != null &&
                includeTree != null &&
                !includeTree(attribute.ApplicationSyntaxReference.SyntaxTree))
            {
                continue;
            }
            if (!SymbolEqualityComparer.Default.Equals(
                    attribute.AttributeClass?.OriginalDefinition,
                    contractFor.OriginalDefinition))
            {
                continue;
            }

            count++;
            first ??= attribute;
        }
        return (count, first);
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

    private sealed class ValidatedCompanionLayers
    {
        private ValidatedCompanionLayers(ImmutableArray<INamedTypeSymbol> layers)
        {
            Layers = layers;
        }

        internal ImmutableArray<INamedTypeSymbol> Layers { get; }

        internal static bool TryCreate(
            INamedTypeSymbol companion,
            (INamedTypeSymbol Target, bool IsOpen) contractTarget,
            out ValidatedCompanionLayers? match)
        {
            match = null;
            if (!TryValidateCompanionLayers(companion, contractTarget, out var layers))
            {
                return false;
            }

            match = new ValidatedCompanionLayers(layers);
            return true;
        }
    }

    internal static bool CompanionTypeMatches(
        INamedTypeSymbol companion,
        (INamedTypeSymbol Target, bool IsOpen) contractTarget)
    {
        return TryValidateCompanionLayers(companion, contractTarget, out _);
    }

    private static bool TryValidateCompanionLayers(
        INamedTypeSymbol companion,
        (INamedTypeSymbol Target, bool IsOpen) contractTarget,
        out ImmutableArray<INamedTypeSymbol> companionLayers)
    {
        companionLayers = [];
        if (companion is not { TypeKind: TypeKind.Class, IsStatic: true })
        {
            return false;
        }

        companionLayers = GetTypeLayers(companion);
        if (!contractTarget.IsOpen)
        {
            return companionLayers.All(static layer => layer.Arity == 0);
        }

        var targetLayers = GetGenericTypeLayers(contractTarget.Target);
        var targetIndex = 0;
        foreach (var companionLayer in companionLayers)
        {
            if (companionLayer.Arity == 0)
            {
                continue;
            }
            if (targetIndex == targetLayers.Length ||
                !TypeParameterListsMatch(
                    targetLayers[targetIndex++].TypeParameters,
                    companionLayer.TypeParameters))
            {
                return false;
            }
        }
        return targetIndex == targetLayers.Length;
    }

    internal static IEnumerable<IMethodSymbol> GetOrdinaryMethods(
        INamedTypeSymbol type)
    {
        return type.GetMembers().OfType<IMethodSymbol>().Where(static method =>
            method is { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false });
    }

    internal static ImmutableArray<CompanionDescriptor> DiscoverCompanions(
        Compilation compilation,
        CancellationToken cancellationToken = default)
    {
        return ClassifyCompanionRelationships(
                DiscoverCompanionRelationships(compilation, cancellationToken),
                cancellationToken)
            .Accepted;
    }

    internal static ImmutableArray<CompanionDescriptor>
        DiscoverCompanionRelationships(
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
        foreach (var type in ReferencedTypeSymbols.GetAllCached(
                     compilation,
                     contractFor.ContainingAssembly,
                     cancellationToken))
        {
            var attributes = GetAttributes(type, contractFor);
            if (attributes.Count == 1 &&
                attributes.First is { } attribute &&
                TryGetTarget(attribute, out var target))
            {
                result.Add(new CompanionDescriptor(type, target));
            }
        }
        return result.ToImmutable();
    }

    internal static CompanionRelationshipInventory
        ClassifyCompanionRelationships(
            ImmutableArray<CompanionDescriptor> relationships,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var comparer = (IEqualityComparer<INamedTypeSymbol>)
            SymbolEqualityComparer.Default;
        var byType = new Dictionary<INamedTypeSymbol, CompanionDescriptor>(
            comparer);
        foreach (var relationship in relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byType.ContainsKey(relationship.Type))
            {
                byType.Add(relationship.Type, relationship);
            }
        }

        var incoming = byType.Keys.ToDictionary(
            static type => type,
            static _ => 0,
            comparer);
        foreach (var pair in byType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = pair.Key;
            var relationship = pair.Value;
            if (!byType.ContainsKey(relationship.Target))
            {
                continue;
            }
            incoming[relationship.Target]++;
        }

        var pending = new Queue<INamedTypeSymbol>(
            incoming.Where(static pair => pair.Value == 0)
                .Select(static pair => pair.Key));
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = pending.Dequeue();
            var target = byType[source].Target;
            if (!incoming.TryGetValue(target, out var targetIncoming))
            {
                continue;
            }
            targetIncoming--;
            incoming[target] = targetIncoming;
            if (targetIncoming == 0)
            {
                pending.Enqueue(target);
            }
        }

        var accepted = ImmutableArray.CreateBuilder<CompanionDescriptor>();
        var selfTargeting = new HashSet<INamedTypeSymbol>(comparer);
        var cyclic = new HashSet<INamedTypeSymbol>(comparer);
        foreach (var relationship in relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (SymbolEqualityComparer.Default.Equals(
                    relationship.Type,
                    relationship.Target))
            {
                selfTargeting.Add(relationship.Type);
            }
            else if (incoming.TryGetValue(
                         relationship.Type,
                         out var remaining) &&
                     remaining > 0)
            {
                cyclic.Add(relationship.Type);
            }
            else
            {
                accepted.Add(relationship);
            }
        }
        return new CompanionRelationshipInventory(
            accepted.ToImmutable(),
            selfTargeting,
            cyclic);
    }

    internal static CompanionResolution ResolveCompanion(
        IMethodSymbol target,
        IReadOnlyDictionary<INamedTypeSymbol,
            ImmutableArray<CompanionDescriptor>> targetIndex)
    {
        ArgumentNullGuard.NotNull(targetIndex, nameof(targetIndex));
        if (target.MethodKind != MethodKind.Ordinary)
        {
            return CompanionResolution.None;
        }

        var matchingCount = 0;
        CompanionDescriptor? matchingCompanion = null;
        foreach (var candidateCompanion in GetCompanionCandidates(
                     target,
                     targetIndex))
        {
            if (!TargetsType(candidateCompanion.ContractTarget, target.ContainingType))
            {
                continue;
            }

            matchingCount++;
            matchingCompanion ??= candidateCompanion;
        }
        if (matchingCount == 0)
        {
            return CompanionResolution.None;
        }

        if (matchingCount != 1)
        {
            return CompanionResolution.Fail(ContractBindingFailure.AmbiguousCompanion);
        }

        var companion = matchingCompanion!;
        if (!ValidatedCompanionLayers.TryCreate(
                companion.Type,
                companion.ContractTarget,
                out var companionLayers))
        {
            return CompanionResolution.Fail(ContractBindingFailure.CompanionSignatureMismatch);
        }

        var signatureTarget = companion.ContractTarget.IsOpen
            ? target.OriginalDefinition
            : target.ConstructedFrom;
        var namedCount = 0;
        var matchCount = 0;
        IMethodSymbol? matchingMethod = null;
        foreach (var candidate in GetOrdinaryMethods(companion.Type))
        {
            if (!string.Equals(candidate.Name, target.Name, StringComparison.Ordinal))
            {
                continue;
            }

            namedCount++;
            if (!MemberSignaturesMatch(signatureTarget, candidate))
            {
                continue;
            }

            matchCount++;
            matchingMethod ??= candidate;
        }
        if (matchCount == 1)
        {
            return HasUniqueTarget(signatureTarget, matchingMethod!)
                ? SpecializeCompanion(
                    companion,
                    matchingMethod!,
                    target,
                    companionLayers!)
                : CompanionResolution.Fail(ContractBindingFailure.AmbiguousCompanion);
        }

        return CompanionResolution.Fail(matchCount > 1
            ? ContractBindingFailure.AmbiguousCompanion
            : namedCount == 0
                ? ContractBindingFailure.MissingCompanion
                : ContractBindingFailure.CompanionSignatureMismatch);
    }

    private static IEnumerable<CompanionDescriptor> GetCompanionCandidates(
        IMethodSymbol target,
        IReadOnlyDictionary<INamedTypeSymbol,
            ImmutableArray<CompanionDescriptor>> targetIndex)
    {
        return EnumerateIndexed();

        IEnumerable<CompanionDescriptor> EnumerateIndexed()
        {
            var containingType = target.ContainingType;
            if (targetIndex.TryGetValue(containingType, out var exact))
            {
                foreach (var companion in exact)
                {
                    yield return companion;
                }
            }

            var originalDefinition = containingType.OriginalDefinition;
            if (SymbolEqualityComparer.Default.Equals(
                    containingType,
                    originalDefinition) ||
                !targetIndex.TryGetValue(originalDefinition, out var open))
            {
                yield break;
            }

            foreach (var companion in open)
            {
                yield return companion;
            }
        }
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
                target.ReturnsByRefReadonly, target.CallingConvention,
                target.IsVararg, true) !=
            (companion.Name, companion.Arity, companion.ReturnsByRef,
                companion.ReturnsByRefReadonly, companion.CallingConvention,
                companion.IsVararg, companion.IsStatic) ||
            !TypesMatch(target.ReturnType, companion.ReturnType, target, companion) ||
            !CustomModifiersMatch(
                target.ReturnTypeCustomModifiers,
                companion.ReturnTypeCustomModifiers) ||
            !CustomModifiersMatch(
                target.RefCustomModifiers,
                companion.RefCustomModifiers))
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
        if (!receiverMatches)
        {
            return false;
        }
        for (var index = 0; index < target.Parameters.Length; index++)
        {
            if (!ParametersMatch(
                    target.Parameters[index],
                    companion.Parameters[index + offset]))
            {
                return false;
            }
        }
        return TypeParameterListsMatch(target.TypeParameters, companion.TypeParameters);
    }

    private static CompanionResolution SpecializeCompanion(
        CompanionDescriptor companion,
        IMethodSymbol definition,
        IMethodSymbol target,
        ValidatedCompanionLayers companionLayers)
    {
        try
        {
            var type = companion.ContractTarget.IsOpen
                ? ConstructCompanionType(companionLayers, target.ContainingType)
                : companion.Type;
            var method = type.GetMembers(definition.Name).OfType<IMethodSymbol>()
                .First(candidate => SymbolEqualityComparer.Default.Equals(
                    candidate.OriginalDefinition, definition.OriginalDefinition));

            if (method.Arity != 0)
            {
                method = method.Construct(target.TypeArguments.ToArray());
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
        ValidatedCompanionLayers validatedLayers,
        INamedTypeSymbol target)
    {
        var companionLayers = validatedLayers.Layers;
        var targetLayers = GetGenericTypeLayers(target);

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
                : definition.Construct(
                    targetLayers[targetIndex++].TypeArguments.ToArray());
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
        CustomModifiersMatch(left.CustomModifiers, right.CustomModifiers) &&
        ParameterRefCustomModifiersMatch(left, right) &&
        ExplicitDefaultValuesMatch(left, right) &&
        TypesMatch(
            left.Type,
            right.Type,
            left.ContainingSymbol,
            right.ContainingSymbol,
            normalizeMappedTypeParameters: true);
    }

    private static bool ExplicitDefaultValuesMatch(
        IParameterSymbol left,
        IParameterSymbol right)
    {
        if (!left.HasExplicitDefaultValue)
        {
            return true;
        }

        return (left.ExplicitDefaultValue, right.ExplicitDefaultValue) switch
        {
            (float leftValue, float rightValue) =>
                SingleBits(leftValue) == SingleBits(rightValue),
            (double leftValue, double rightValue) =>
                BitConverter.DoubleToInt64Bits(leftValue) ==
                BitConverter.DoubleToInt64Bits(rightValue),
            (decimal leftValue, decimal rightValue) =>
                DecimalBitsMatch(leftValue, rightValue),
            _ => Equals(
                left.ExplicitDefaultValue,
                right.ExplicitDefaultValue)
        };
    }

    private static bool DecimalBitsMatch(decimal left, decimal right)
    {
        var leftBits = decimal.GetBits(left);
        var rightBits = decimal.GetBits(right);
        return leftBits[0] == rightBits[0] &&
            leftBits[1] == rightBits[1] &&
            leftBits[2] == rightBits[2] &&
            leftBits[3] == rightBits[3];
    }

    private static int SingleBits(float value)
    {
        return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
    }

    private static bool ParameterRefCustomModifiersMatch(
        IParameterSymbol left,
        IParameterSymbol right)
    {
        if (!IsCompilerReadOnlyInput(left.RefKind) ||
            !IsCompilerReadOnlyInput(right.RefKind))
        {
            return CustomModifiersMatch(
                left.RefCustomModifiers,
                right.RefCustomModifiers);
        }

        // Roslyn adds a required InAttribute modifier to virtual/abstract `in`
        // parameters but omits it from the equivalent static companion. RefKind
        // already captures that source-level contract, so compare the remaining
        // modifiers exactly.
        return CustomModifiersMatchWithoutCompilerInAttribute(
            left.RefCustomModifiers,
            right.RefCustomModifiers);
    }

    private static bool IsCompilerReadOnlyInput(RefKind refKind)
    {
        return refKind is RefKind.In or RefKind.RefReadOnlyParameter;
    }

    private static bool CustomModifiersMatchWithoutCompilerInAttribute(
        ImmutableArray<CustomModifier> left,
        ImmutableArray<CustomModifier> right)
    {
        return CustomModifiersMatch(
            Normalize(left),
            Normalize(right));

        static ImmutableArray<CustomModifier> Normalize(ImmutableArray<CustomModifier> modifiers)
        {
            return modifiers.Where(static modifier => modifier.IsOptional || !IsCompilerInAttribute(modifier.Modifier))
                .ToImmutableArray();
        }
    }

    private static bool IsCompilerInAttribute(INamedTypeSymbol type)
    {
        return type.ContainingType == null &&
               string.Equals(type.MetadataName, "InAttribute", StringComparison.Ordinal) &&
               type.ContainingNamespace is { Name: "InteropServices" } interopServices &&
               interopServices.ContainingNamespace is { Name: "Runtime" } runtime &&
               runtime.ContainingNamespace is { Name: "System" } system &&
               system.ContainingNamespace is { IsGlobalNamespace: true };
    }

    private static bool TypeParameterListsMatch(
        ImmutableArray<ITypeParameterSymbol> left,
        ImmutableArray<ITypeParameterSymbol> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (var index = 0; index < left.Length; index++)
        {
            if (!TypeParameterConstraintsMatch(left[index], right[index]))
            {
                return false;
            }
        }
        return true;
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

    internal static bool TypesMatch(
        ITypeSymbol? left,
        ITypeSymbol? right,
        ISymbol leftScope,
        ISymbol rightScope,
        bool normalizeMappedTypeParameters = false,
        bool compareRuntimeShape = false,
        Func<ITypeSymbol, ITypeSymbol>? mapTypeParameter = null)
    {
        if (left == null || right == null)
        {
            return left is null && right is null;
        }

        if (compareRuntimeShape && mapTypeParameter != null)
        {
            left = mapTypeParameter(left);
            right = mapTypeParameter(right);
        }

        if (compareRuntimeShape && left.TypeKind != TypeKind.Error &&
            SymbolEqualityComparer.Default.Equals(left, right))
        {
            return true;
        }

        if ((compareRuntimeShape ? NullableAnnotation.None : GetAnnotation(left, normalizeMappedTypeParameters), left.TypeKind) !=
                (compareRuntimeShape ? NullableAnnotation.None : GetAnnotation(right, normalizeMappedTypeParameters), right.TypeKind) ||
            left.TypeKind == TypeKind.Error)
        {
            return false;
        }

        if (left is ITypeParameterSymbol leftParameter &&
            right is ITypeParameterSymbol rightParameter)
        {
            // Constructed target and companion signatures can both contain a
            // caller's type parameter. Its identity already agrees, even though
            // its owner is outside either signature's declaration scope.
            return SymbolEqualityComparer.Default.Equals(leftParameter, rightParameter) ||
                   (leftParameter.TypeParameterKind, leftParameter.Ordinal) ==
                   (rightParameter.TypeParameterKind, rightParameter.Ordinal) &&
                   OwnersMatch(leftParameter.ContainingSymbol,
                       rightParameter.ContainingSymbol, leftScope, rightScope);
        }

        if (left is IArrayTypeSymbol leftArray && right is IArrayTypeSymbol rightArray)
        {
            return (leftArray.Rank, leftArray.IsSZArray) ==
                   (rightArray.Rank, rightArray.IsSZArray) &&
                   ArrayShapePartsMatch(
                       leftArray.Sizes,
                       rightArray.Sizes) &&
                   ArrayShapePartsMatch(
                       leftArray.LowerBounds,
                       rightArray.LowerBounds) &&
                   TypesMatch(leftArray.ElementType, rightArray.ElementType,
                       leftScope, rightScope, normalizeMappedTypeParameters, compareRuntimeShape, mapTypeParameter);
        }

        if (left is IPointerTypeSymbol leftPointer && right is IPointerTypeSymbol rightPointer)
        {
            return TypesMatch(leftPointer.PointedAtType, rightPointer.PointedAtType,
                leftScope, rightScope, normalizeMappedTypeParameters, compareRuntimeShape, mapTypeParameter);
        }

        if (left is IFunctionPointerTypeSymbol leftFunction &&
            right is IFunctionPointerTypeSymbol rightFunction)
        {
            return FunctionPointerSignaturesMatch(
                leftFunction.Signature,
                rightFunction.Signature,
                leftScope,
                rightScope,
                normalizeMappedTypeParameters,
                compareRuntimeShape, mapTypeParameter);
        }

        if (left is not INamedTypeSymbol leftNamed || right is not INamedTypeSymbol rightNamed)
        {
            return SymbolEqualityComparer.IncludeNullability.Equals(left, right);
        }

        if (leftNamed.TypeArguments.Length != rightNamed.TypeArguments.Length ||
            (!compareRuntimeShape && leftNamed.IsTupleType != rightNamed.IsTupleType) ||
            !SymbolEqualityComparer.Default.Equals(
                leftNamed.OriginalDefinition, rightNamed.OriginalDefinition) ||
            !TypesMatch(leftNamed.ContainingType, rightNamed.ContainingType,
                leftScope, rightScope, normalizeMappedTypeParameters, compareRuntimeShape, mapTypeParameter))
        {
            return false;
        }

        // Compare the logical tuple shape before recursing into the nested
        // ValueTuple representation used for tuples with more than seven items.
        if (!compareRuntimeShape && leftNamed.IsTupleType &&
            leftNamed.TupleElements.Length != rightNamed.TupleElements.Length)
        {
            return false;
        }

        for (var index = 0; index < leftNamed.TypeArguments.Length; index++)
        {
            if (!CustomModifiersMatch(
                    leftNamed.GetTypeArgumentCustomModifiers(index),
                    rightNamed.GetTypeArgumentCustomModifiers(index)) ||
                !TypesMatch(
                    leftNamed.TypeArguments[index],
                    rightNamed.TypeArguments[index],
                    leftScope,
                    rightScope,
                    normalizeMappedTypeParameters,
                    compareRuntimeShape, mapTypeParameter))
            {
                return false;
            }
        }

        if (compareRuntimeShape || !leftNamed.IsTupleType)
        {
            return true;
        }
        for (var index = 0; index < leftNamed.TupleElements.Length; index++)
        {
            if (!string.Equals(
                    leftNamed.TupleElements[index].Name,
                    rightNamed.TupleElements[index].Name,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ArrayShapePartsMatch(
        ImmutableArray<int> left,
        ImmutableArray<int> right)
    {
        return left.IsDefaultOrEmpty
            ? right.IsDefaultOrEmpty
            : !right.IsDefault && left.SequenceEqual(right);
    }

    private static bool FunctionPointerSignaturesMatch(
        IMethodSymbol left,
        IMethodSymbol right,
        ISymbol leftScope,
        ISymbol rightScope,
        bool normalizeMappedTypeParameters,
        bool compareRuntimeShape,
        Func<ITypeSymbol, ITypeSymbol>? mapTypeParameter)
    {
        if ((left.CallingConvention, left.ReturnsByRef,
                left.ReturnsByRefReadonly, left.Parameters.Length) !=
            (right.CallingConvention, right.ReturnsByRef,
                right.ReturnsByRefReadonly, right.Parameters.Length) ||
            !TypesMatch(left.ReturnType, right.ReturnType,
                leftScope, rightScope, normalizeMappedTypeParameters, compareRuntimeShape, mapTypeParameter) ||
            !FunctionPointerReturnCustomModifiersMatch(
                left.ReturnTypeCustomModifiers,
                right.ReturnTypeCustomModifiers,
                left.UnmanagedCallingConventionTypes,
                right.UnmanagedCallingConventionTypes) ||
            !CustomModifiersMatch(
                left.RefCustomModifiers,
                right.RefCustomModifiers) ||
            !UnmanagedCallingConventionTypesMatch(
                left.UnmanagedCallingConventionTypes,
                right.UnmanagedCallingConventionTypes))
        {
            return false;
        }
        for (var index = 0; index < left.Parameters.Length; index++)
        {
            if (!FunctionPointerParametersMatch(
                    left.Parameters[index],
                    right.Parameters[index],
                    leftScope,
                    rightScope,
                    normalizeMappedTypeParameters,
                    compareRuntimeShape, mapTypeParameter))
            {
                return false;
            }
        }
        return true;
    }

    private static bool UnmanagedCallingConventionTypesMatch(
        ImmutableArray<INamedTypeSymbol> left,
        ImmutableArray<INamedTypeSymbol> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        var matched = new bool[right.Length];
        foreach (var leftType in left)
        {
            var match = -1;
            for (var index = 0; index < right.Length; index++)
            {
                if (!matched[index] &&
                    SymbolEqualityComparer.Default.Equals(
                        leftType,
                        right[index]))
                {
                    match = index;
                    break;
                }
            }

            if (match < 0)
            {
                return false;
            }

            matched[match] = true;
        }

        return true;
    }

    private static bool FunctionPointerReturnCustomModifiersMatch(
        ImmutableArray<CustomModifier> left,
        ImmutableArray<CustomModifier> right,
        ImmutableArray<INamedTypeSymbol> leftCallingConventions,
        ImmutableArray<INamedTypeSymbol> rightCallingConventions)
    {
        return CustomModifiersMatch(
            left.Where(modifier => !IsCallingConventionModifier(modifier, leftCallingConventions)).ToImmutableArray(),
            right.Where(modifier => !IsCallingConventionModifier(modifier, rightCallingConventions)).ToImmutableArray());
    }

    private static bool IsCallingConventionModifier(
        CustomModifier modifier,
        ImmutableArray<INamedTypeSymbol> callingConventions)
    {
        return callingConventions.Any(type =>
            SymbolEqualityComparer.Default.Equals(
                modifier.Modifier,
                type));
    }

    private static bool FunctionPointerParametersMatch(
        IParameterSymbol left,
        IParameterSymbol right,
        ISymbol leftScope,
        ISymbol rightScope,
        bool normalizeMappedTypeParameters,
        bool compareRuntimeShape,
        Func<ITypeSymbol, ITypeSymbol>? mapTypeParameter)
    {
        return (left.RefKind, left.ScopedKind) ==
                   (right.RefKind, right.ScopedKind) &&
               CustomModifiersMatch(left.CustomModifiers, right.CustomModifiers) &&
               CustomModifiersMatch(left.RefCustomModifiers, right.RefCustomModifiers) &&
               TypesMatch(left.Type, right.Type,
                   leftScope, rightScope, normalizeMappedTypeParameters, compareRuntimeShape, mapTypeParameter);
    }

    internal static bool CustomModifiersMatch(
        ImmutableArray<CustomModifier> left,
        ImmutableArray<CustomModifier> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }
        for (var index = 0; index < left.Length; index++)
        {
            if (left[index].IsOptional != right[index].IsOptional ||
                !SymbolEqualityComparer.Default.Equals(
                    left[index].Modifier,
                    right[index].Modifier))
            {
                return false;
            }
        }
        return true;
    }

    private static bool OwnersMatch(
        ISymbol left,
        ISymbol right,
        ISymbol? leftScope,
        ISymbol? rightScope)
    {
        if (left is INamedTypeSymbol leftType &&
            right is INamedTypeSymbol rightType)
        {
            var leftOwners = GetGenericOwnerLayers(leftScope);
            var rightOwners = GetGenericOwnerLayers(rightScope);
            var leftIndex = GetGenericOwnerIndex(leftOwners, leftType);
            var rightIndex = GetGenericOwnerIndex(rightOwners, rightType);
            return leftIndex >= 0 && leftIndex == rightIndex;
        }

        return leftScope != null &&
        rightScope != null &&
        (SymbolEqualityComparer.Default.Equals(left, leftScope)
            ? SymbolEqualityComparer.Default.Equals(right, rightScope)
            : !SymbolEqualityComparer.Default.Equals(right, rightScope) &&
              OwnersMatch(left, right, leftScope.ContainingSymbol, rightScope.ContainingSymbol));
    }

    private static ImmutableArray<INamedTypeSymbol> GetGenericOwnerLayers(
        ISymbol? scope)
    {
        var containingType = scope as INamedTypeSymbol ?? scope?.ContainingType;
        return containingType == null
            ? []
            : GetGenericTypeLayers(containingType);
    }

    private static int GetGenericOwnerIndex(
        ImmutableArray<INamedTypeSymbol> owners,
        INamedTypeSymbol owner)
    {
        for (var index = 0; index < owners.Length; index++)
        {
            if (SymbolEqualityComparer.Default.Equals(owners[index], owner))
            {
                return index;
            }
        }
        return -1;
    }

    private static NullableAnnotation GetAnnotation(
        ITypeSymbol type,
        bool normalizeMappedTypeParameters)
    {
        return type.NullableAnnotation == NullableAnnotation.None &&
        (normalizeMappedTypeParameters ||
         type is not ITypeParameterSymbol)
            ? NullableAnnotation.NotAnnotated
            : type.NullableAnnotation;
    }
}
