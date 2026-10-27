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
        internal INamedTypeSymbol Type { get; } = (type ?? throw new System.ArgumentNullException(nameof(type)));
        internal (INamedTypeSymbol Target, bool IsOpen) ContractTarget { get; } = contractTarget;
        internal INamedTypeSymbol Target => ContractTarget.Target;
    }

    internal static ImmutableDictionary<INamedTypeSymbol,
        ImmutableArray<CompanionDescriptor>> BuildCompanionTargetIndex(
        ImmutableArray<CompanionDescriptor> companions)
    {

        RepositoryParameterGuard.RequireInitialized(companions.IsDefault, nameof(companions), "Array must be initialized.");
        var buckets = ImmutableDictionary.CreateBuilder<INamedTypeSymbol,
            ImmutableArray<CompanionDescriptor>.Builder>(SymbolEqualityComparer.Default);
        foreach (var companion in companions)
        {
            var key = companion.ContractTarget.IsOpen
                ? companion.Target.OriginalDefinition
                : companion.Target;
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = ImmutableArray.CreateBuilder<CompanionDescriptor>();
                buckets.Add(key, bucket);
            }
            bucket.Add(companion);
        }
        var indexed = ImmutableDictionary.CreateBuilder<INamedTypeSymbol,
            ImmutableArray<CompanionDescriptor>>(SymbolEqualityComparer.Default);
        foreach (var pair in buckets)
        {
            indexed.Add(pair.Key, pair.Value.ToImmutable());
        }
        return indexed.ToImmutable();
    }

    internal sealed class CompanionRelationshipInventory(
        ImmutableArray<CompanionDescriptor> accepted,
        ImmutableHashSet<INamedTypeSymbol> selfTargeting,
        ImmutableHashSet<INamedTypeSymbol> cyclic)
    {
        private readonly ImmutableHashSet<INamedTypeSymbol> _selfTargeting =
            (selfTargeting ?? throw new System.ArgumentNullException(nameof(selfTargeting)));
        private readonly ImmutableHashSet<INamedTypeSymbol> _cyclic = (cyclic ?? throw new System.ArgumentNullException(nameof(cyclic)));

        internal ImmutableArray<CompanionDescriptor> Accepted { get; } =
            (accepted.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(accepted)) : accepted);

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
            method = RepositoryParameterGuard.NotNull(method, nameof(method));
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
        companion = RepositoryParameterGuard.NotNull(companion, nameof(companion));
        contractFor = RepositoryParameterGuard.NotNull(contractFor, nameof(contractFor));

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
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
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
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
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

            RepositoryParameterGuard.RequireInitialized(layers.IsDefault, nameof(layers), "Array must be initialized.");
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
        companion = RepositoryParameterGuard.NotNull(companion, nameof(companion));
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
            return companionLayers.All(static layer => layer is null ? throw new System.ArgumentNullException(nameof(layer)) : (layer.Arity == 0));
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
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.GetMembers().OfType<IMethodSymbol>().Where(static method =>
            method is null ? throw new System.ArgumentNullException(nameof(method)) : (method is { MethodKind: MethodKind.Ordinary, IsImplicitlyDeclared: false }));
    }

    internal static ImmutableArray<CompanionDescriptor> DiscoverCompanions(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        return ClassifyCompanionRelationships(
                DiscoverCompanionRelationships(compilation, cancellationToken),
                cancellationToken)
            .Accepted;
    }

    internal static ImmutableArray<CompanionDescriptor>
        DiscoverCompanionRelationships(
            Compilation compilation,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));

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
            CancellationToken cancellationToken)
    {

        RepositoryParameterGuard.RequireInitialized(relationships.IsDefault, nameof(relationships), "Array must be initialized.");
        cancellationToken.ThrowIfCancellationRequested();
        var comparer = (IEqualityComparer<INamedTypeSymbol>)
            SymbolEqualityComparer.Default;
        var byType = ImmutableDictionary.CreateBuilder<INamedTypeSymbol, CompanionDescriptor>(comparer);
        var byTypeOrder = ImmutableArray.CreateBuilder<KeyValuePair<INamedTypeSymbol, CompanionDescriptor>>();
        foreach (var relationship in relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byType.ContainsKey(relationship.Type))
            {
                byType.AddUnique(relationship.Type, relationship);
                byTypeOrder.Add(new(relationship.Type, relationship));
            }
        }

        var incoming = ImmutableDictionaryCapture.Create(byTypeOrder.Select(static pair => pair.Key),
            static type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type),
            static _ => 0,
            comparer).ToBuilder();
        foreach (var pair in byTypeOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = pair.Key;
            var relationship = pair.Value;
            if (!byType.ContainsKey(relationship.Target))
            {
                continue;
            }
            incoming[relationship.Target] = incoming[relationship.Target] + 1;
        }

        var pending = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        pending.AddRange(byTypeOrder.Where(pair => incoming[pair.Key] == 0).Select(static pair => pair.Key));
        for (var pendingIndex = 0; pendingIndex < pending.Count; pendingIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = pending[pendingIndex];
            var target = byType[source].Target;
            if (!incoming.TryGetValue(target, out var targetIncoming))
            {
                continue;
            }
            targetIncoming--;
            incoming[target] = targetIncoming;
            if (targetIncoming == 0)
            {
                pending.Add(target);
            }
        }

        var accepted = ImmutableArray.CreateBuilder<CompanionDescriptor>();
        var selfTargeting = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(comparer);
        var cyclic = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(comparer);
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
            selfTargeting.ToImmutable(),
            cyclic.ToImmutable());
    }

    internal static CompanionResolution ResolveCompanion(
        ImmutableArray<CompanionDescriptor> companions,
        IMethodSymbol target,
        ImmutableDictionary<INamedTypeSymbol, ImmutableArray<CompanionDescriptor>>? targetIndex = null)
    {

        RepositoryParameterGuard.RequireInitialized(companions.IsDefault, nameof(companions), "Array must be initialized.");
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        if (target.MethodKind != MethodKind.Ordinary)
        {
            return CompanionResolution.None;
        }
        return ResolveCompanion(target, targetIndex ?? BuildCompanionTargetIndex(companions));
    }

    internal static CompanionResolution ResolveCompanion(
        IMethodSymbol target,
        ImmutableDictionary<INamedTypeSymbol,
            ImmutableArray<CompanionDescriptor>> targetIndex)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
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
        ImmutableDictionary<INamedTypeSymbol,
            ImmutableArray<CompanionDescriptor>> targetIndex)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        targetIndex = RepositoryParameterGuard.NotNull(targetIndex, nameof(targetIndex));
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
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return companions.Any(companion => companion is null ? throw new System.ArgumentNullException(nameof(companion)) : (SymbolEqualityComparer.Default.Equals(
            companion.Type.OriginalDefinition, type.OriginalDefinition)));
    }

    internal static bool MemberSignaturesMatch(
        IMethodSymbol target,
        IMethodSymbol companion)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        companion = RepositoryParameterGuard.NotNull(companion, nameof(companion));
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
        companion = RepositoryParameterGuard.NotNull(companion, nameof(companion));
        companionLayers = RepositoryParameterGuard.NotNull(companionLayers, nameof(companionLayers));
        definition = RepositoryParameterGuard.NotNull(definition, nameof(definition));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return ContractSpecializationPolicy.TrySpecialize(
            (companion, definition, target, companionLayers),
            static state =>
            {
                var type = state.companion.ContractTarget.IsOpen
                    ? ConstructCompanionType(state.companionLayers, state.target.ContainingType)
                    : state.companion.Type;
                var selectedDefinition = state.definition;
                var method = type.GetMembers(selectedDefinition.Name).OfType<IMethodSymbol>()
                    .First(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (SymbolEqualityComparer.Default.Equals(
                        candidate.OriginalDefinition, selectedDefinition.OriginalDefinition)));

                if (method.Arity != 0)
                {
                    method = method.Construct(state.target.TypeArguments, state.target.TypeArgumentNullableAnnotations);
                }

                return (!RequiresConstructedSignatureCheck(state.target) ||
                        MemberSignaturesMatch(state.target, method))
                    ? CompanionResolution.Success(
                        ContractClauseInventoryBuilder.NormalizeCallable(method))
                    : CompanionResolution.Fail(
                        ContractBindingFailure.CompanionSignatureMismatch);
            }, out CompanionResolution resolution)
            ? resolution
            : CompanionResolution.Fail(ContractBindingFailure.CompanionSignatureMismatch);
    }

    private static INamedTypeSymbol ConstructCompanionType(
        ValidatedCompanionLayers validatedLayers,
        INamedTypeSymbol target)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        validatedLayers = RepositoryParameterGuard.NotNull(validatedLayers, nameof(validatedLayers));
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
                        candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (SymbolEqualityComparer.Default.Equals(
                            candidate.OriginalDefinition,
                            companionLayers[index].OriginalDefinition))) ??
                  throw new ArgumentException(
                      "The nested companion type could not be specialized.");
            if (definition.Arity == 0)
            {
                constructed = definition;
            }
            else
            {
                var targetLayer = targetLayers[targetIndex++];
                constructed = definition.Construct(targetLayer.TypeArguments, targetLayer.TypeArgumentNullableAnnotations);
            }
        }

        return constructed ?? throw new ArgumentException(
            "The companion type could not be specialized.");
    }

    private static bool RequiresConstructedSignatureCheck(
        IMethodSymbol target)
    {
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return !SymbolEqualityComparer.Default.Equals(
                   target, target.OriginalDefinition) ||
               GetTypeLayers(target.ContainingType).Any(layer =>
                   layer is null ? throw new System.ArgumentNullException(nameof(layer)) : (!SymbolEqualityComparer.Default.Equals(
                       layer, layer.OriginalDefinition)));
    }

    private static ImmutableArray<INamedTypeSymbol> GetGenericTypeLayers(
        INamedTypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return [.. GetTypeLayers(type).Where(static layer => layer is null ? throw new System.ArgumentNullException(nameof(layer)) : (layer.Arity > 0))];
    }

    private static ImmutableArray<INamedTypeSymbol> GetTypeLayers(
        INamedTypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var layers = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = type;
             current != null;
             current = current.ContainingType)
        {
            layers.Add(current);
        }
        return layers.AsEnumerable().Reverse().ToImmutableArray();
    }

    private static bool HasUniqueTarget(IMethodSymbol target, IMethodSymbol companion)
    {
        companion = RepositoryParameterGuard.NotNull(companion, nameof(companion));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return GetOrdinaryMethods(target.ContainingType)
            .Count(candidate => candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (MemberSignaturesMatch(candidate, companion))) == 1;
    }

    private static bool IsReceiver(IMethodSymbol target, IParameterSymbol receiver)
    {
        receiver = RepositoryParameterGuard.NotNull(receiver, nameof(receiver));
        target = RepositoryParameterGuard.NotNull(target, nameof(target));
        return receiver is { RefKind: RefKind.None, ScopedKind: ScopedKind.None, IsParams: false, IsOptional: false } &&
        TypesMatch(target.ContainingType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
            receiver.Type, target, receiver.ContainingSymbol, normalizeMappedTypeParameters: true);
    }

    private static bool ParametersMatch(IParameterSymbol left, IParameterSymbol right)
    {
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
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
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
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
        var leftBits = decimal.GetBits(left).ToImmutableArray();
        var rightBits = decimal.GetBits(right).ToImmutableArray();
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

            RepositoryParameterGuard.RequireInitialized(modifiers.IsDefault, nameof(modifiers), "Array must be initialized.");
            return modifiers.Where(static modifier => modifier is null ? throw new System.ArgumentNullException(nameof(modifier)) : (modifier.IsOptional || !IsCompilerInAttribute(modifier.Modifier)))
                .ToImmutableArray();
        }
    }

    private static bool IsCompilerInAttribute(INamedTypeSymbol type)
    {
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
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

        var unmatched = right.ConstraintTypes.ToImmutableList().ToBuilder();
        foreach (var constraint in left.ConstraintTypes)
        {
            var index = unmatched.FindIndex(candidate =>
                candidate is null ? throw new System.ArgumentNullException(nameof(candidate)) : (TypesMatch(constraint, candidate, left.ContainingSymbol, right.ContainingSymbol)));
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

        var matched = ImmutableHashSet.CreateBuilder<int>();
        foreach (var leftType in left)
        {
            var match = -1;
            for (var index = 0; index < right.Length; index++)
            {
                if (!matched.Contains(index) &&
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

            matched.Add(match);
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
            left.Where(modifier => modifier is null ? throw new System.ArgumentNullException(nameof(modifier)) : (!IsCallingConventionModifier(modifier, leftCallingConventions))).ToImmutableArray(),
            right.Where(modifier => modifier is null ? throw new System.ArgumentNullException(nameof(modifier)) : (!IsCallingConventionModifier(modifier, rightCallingConventions))).ToImmutableArray());
    }

    private static bool IsCallingConventionModifier(
        CustomModifier modifier,
        ImmutableArray<INamedTypeSymbol> callingConventions)
    {
        modifier = RepositoryParameterGuard.NotNull(modifier, nameof(modifier));
        return callingConventions.Any(type =>
            type is null ? throw new System.ArgumentNullException(nameof(type)) : (SymbolEqualityComparer.Default.Equals(
                modifier.Modifier,
                type)));
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
        left = RepositoryParameterGuard.NotNull(left, nameof(left));
        leftScope = RepositoryParameterGuard.NotNull(leftScope, nameof(leftScope));
        right = RepositoryParameterGuard.NotNull(right, nameof(right));
        rightScope = RepositoryParameterGuard.NotNull(rightScope, nameof(rightScope));
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
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return type.NullableAnnotation == NullableAnnotation.None &&
        (normalizeMappedTypeParameters ||
         type is not ITypeParameterSymbol)
            ? NullableAnnotation.NotAnnotated
            : type.NullableAnnotation;
    }
}
