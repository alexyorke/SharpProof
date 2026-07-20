internal sealed class TypeNameProvider(bool eraseGenericInstantiationsForLookup = false)
    : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape)
    {
        var rank = Math.Max(shape.Rank, 1);
        return $"{elementType}[{new string(',', rank - 1)}]";
    }

    public string GetByReferenceType(string elementType) => $"ref {elementType}";

    public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";

    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
    {
        if (eraseGenericInstantiationsForLookup) return genericType;

        return $"{genericType}<{string.Join(", ", typeArguments)}>";
    }

    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";

    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";

    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

    public string GetPinnedType(string elementType) => elementType;

    public string GetPointerType(string elementType) => $"{elementType}*";

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) =>
        typeCode switch
        {
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Byte => "byte",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.Double => "double",
            PrimitiveTypeCode.Int16 => "short",
            PrimitiveTypeCode.Int32 => "int",
            PrimitiveTypeCode.Int64 => "long",
            PrimitiveTypeCode.IntPtr => "nint",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.SByte => "sbyte",
            PrimitiveTypeCode.Single => "float",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.TypedReference => "typedref",
            PrimitiveTypeCode.UInt16 => "ushort",
            PrimitiveTypeCode.UInt32 => "uint",
            PrimitiveTypeCode.UInt64 => "ulong",
            PrimitiveTypeCode.UIntPtr => "nuint",
            PrimitiveTypeCode.Void => "void",
            _ => typeCode.ToString()
        };

    public string GetSZArrayType(string elementType) => $"{elementType}[]";

    public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind) =>
        NormalizeExactTypeName(GetTypeName(metadataReader, handle));

    public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind) =>
        NormalizeExactTypeName(GetTypeReferenceName(metadataReader, handle));

    public string GetTypeFromSpecification(
        MetadataReader metadataReader,
        object? genericContext,
        TypeSpecificationHandle handle,
        byte rawTypeKind) => metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
}

internal sealed record EffectSummaryDocument(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    AssemblyEffectReport[] Assemblies,
    PurityClassificationReport? PurityReport,
    GeneratedPurityCatalogDocument? GeneratedPurityCatalog,
    BclFallbackInventoryReport? BclFallbackInventory)
{
    public int EvidenceSchemaVersion => SharpProofEvidenceSchema.CurrentVersion;
}

internal sealed record AssemblyEffectReport(
    string AssemblyName,
    string AssemblyPath,
    string AssemblySha256,
    string ModuleVersionId,
    int MethodCount,
    int EmittedMethodCount,
    MethodEffectSummary[] Methods)
{
    public EffectSummaryArtifactSource? ArtifactSource { get; init; }

    [JsonIgnore] public MethodEffectSummary[] ClassificationMethods { get; init; } = Array.Empty<MethodEffectSummary>();
}

internal sealed record EffectSummaryArtifactSource(
    string Kind,
    string? Framework,
    string? PackageId,
    string? PackageVersion,
    string? PackageAssemblyRelativePath);

internal sealed record MethodEffectSummary(
    [property: JsonPropertyName("DisplayName"), JsonPropertyOrder(1)] string DisplayName,
    string MetadataToken,
    int RelativeVirtualAddress,
    string? MethodBodySha256,
    string CacheKey,
    string[] Effects,
    string[] RootCandidates,
    string[] TransitiveRootCandidates,
    string[] ThrownExceptionTypes,
    string[] TransitiveThrownExceptionTypes,
    ExceptionProvenance[] ThrownExceptionProvenance,
    ExceptionProvenance[] TransitiveThrownExceptionProvenance,
    [property: JsonIgnore] string[] Calls,
    string[] Fields)
{
    [JsonConstructor]
    private MethodEffectSummary()
        : this(string.Empty, string.Empty, 0, null, string.Empty,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<ExceptionProvenance>(),
            Array.Empty<ExceptionProvenance>(), Array.Empty<string>(), Array.Empty<string>())
    {
    }

    [JsonIgnore] public string Symbol => DisplayName;

    [JsonPropertyOrder(2)]
    public StructuralMethodIdentity Identity { get; init; } = null!;

    [JsonPropertyOrder(3)]
    public string CanonicalKey => Identity.ToCanonicalKey();

    [JsonPropertyName("Calls")]
    public string[] CanonicalCalls { get; init; } = Array.Empty<string>();

    [JsonIgnore]
    public StructuralMethodIdentity[] CallIdentities { get; init; } = Array.Empty<StructuralMethodIdentity>();

    [JsonPropertyOrder(4)]
    public CallSiteSummary[] CallSites { get; init; } = Array.Empty<CallSiteSummary>();

    [JsonPropertyOrder(5)]
    public ThrownExceptionEdgeSummary[] TransitiveThrownExceptionEdges { get; init; } =
        Array.Empty<ThrownExceptionEdgeSummary>();

    [JsonPropertyOrder(6)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool TransitiveThrownExceptionEdgesTruncated { get; init; }

    [JsonPropertyOrder(7)]
    public MethodPurityClassification? PurityClassification { get; init; }

    [JsonIgnore]
    public ExceptionPropagationSite[] ExceptionPropagationSites { get; init; } =
        Array.Empty<ExceptionPropagationSite>();

    [JsonPropertyOrder(8)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NullableContractSummary? NullableContracts { get; init; }

    [JsonIgnore] public bool IsStatic { get; init; }

    [JsonIgnore]
    public InferredMethodSummary InferredSummary => InferredMethodSummary.FromEffectSummary(
        Identity,
        PurityClassification?.Classification,
        Effects,
        PurityClassification?.FreshnessClassification,
        PurityClassification?.EffectVisibilityClassification,
        ThrownExceptionTypes.Concat(TransitiveThrownExceptionTypes),
        PurityClassification?.FirstBlockingCallChain,
        PurityClassification?.Categories);
}

internal sealed record NullableContractSummary(
    bool ReturnNotNull,
    string? ReturnNotNullIfNotNullParameter,
    NullableParameterContractSummary[] Parameters,
    string[] MemberNotNull,
    NullableMemberConditionalContractSummary[] MemberNotNullWhen);

internal sealed record NullableParameterContractSummary(
    int Ordinal,
    string Name,
    bool NotNull,
    bool? NotNullWhen,
    bool? MaybeNullWhen);

internal sealed record NullableMemberConditionalContractSummary(bool When, string Member);

internal sealed record ExceptionProvenance(
    string ExceptionType,
    string? SourcePath,
    StructuralMethodIdentity[] CallChain);

internal sealed record ExceptionPropagationSite(
    StructuralMethodIdentity? CalleeIdentity,
    int InstructionOffset,
    string[] HandlingCatchExceptionTypes,
    bool IsShadowedByDefinitelyThrowingFinally);

internal sealed record ThrownExceptionEdgeSummary(
    string ExceptionType,
    string? SourcePath,
    StructuralMethodIdentity[] CallChain,
    StructuralMethodIdentity? CalleeIdentity,
    int Depth);

internal readonly record struct ThrownExceptionTraversalResult(
    ThrownExceptionEdgeSummary[] Result,
    bool DependsOnCycle,
    bool IsTruncated);

internal sealed record ExceptionPropagationSccIndex(
    IReadOnlyDictionary<StructuralMethodIdentity, StructuralMethodIdentity[]> Graph,
    StructuralMethodIdentity[][] Components,
    IReadOnlyDictionary<StructuralMethodIdentity, int> ComponentByIdentity,
    int[][] Dependencies);

internal sealed class ExceptionPropagationTarjanFrame(
    StructuralMethodIdentity identity,
    StructuralMethodIdentity? parent)
{
    public StructuralMethodIdentity Identity { get; } = identity;

    public StructuralMethodIdentity? Parent { get; } = parent;

    public bool IsEntered { get; set; }

    public int NextNeighborIndex { get; set; }
}

internal sealed record CallSiteSummary([property: JsonIgnore] string DisplayName)
{
    public StructuralMethodIdentity? Identity { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CanonicalKey => Identity?.ToCanonicalKey();

    public bool UsesDynamicDispatch { get; init; }

    public CallSiteArgumentEvidence[] ArgumentEvidence { get; init; } = Array.Empty<CallSiteArgumentEvidence>();
}

internal sealed record CallSiteArgumentEvidence(
    string Target,
    int? ParameterIndex,
    string Type,
    string Value);

internal readonly record struct CallTargetSignature(
    bool HasReceiver,
    string[] ParameterTypes,
    string ReturnType);

internal readonly record struct IlInstruction(
    int Offset,
    OpCode OpCode,
    int OperandOffset,
    int? MetadataToken);

internal sealed record BranchTrackedState(
    List<TrackedStackValue> Stack,
    Dictionary<int, TrackedStackValue> Locals);

internal enum StaticFieldFactKind
{
    Unknown,
    Constant,
    StableIdentity
}

internal readonly record struct StaticFieldFact(
    string Symbol,
    StaticFieldFactKind Kind,
    TrackedStackValue TrackedValue);

internal struct StaticFieldUsage
{
    public int TotalWriteCount;

    public int OwningTypeInitializerWriteCount;

    public bool HasWritesOutsideTypeInitializer;

    public bool HasAddressExposure;
}

internal enum StaticFieldInitializerValueKind
{
    Unknown,
    Constant,
    StableIdentity
}

internal readonly record struct StaticFieldInitializerValue(
    StaticFieldInitializerValueKind Kind,
    TrackedStackValue TrackedValue)
{
    public static StaticFieldInitializerValue Unknown =>
        new(StaticFieldInitializerValueKind.Unknown, TrackedStackValue.Unknown);

    public static StaticFieldInitializerValue Constant =>
        new(StaticFieldInitializerValueKind.Constant, TrackedStackValue.Unknown);

    public static StaticFieldInitializerValue StableIdentity =>
        new(StaticFieldInitializerValueKind.StableIdentity, TrackedStackValue.Unknown);

    public static StaticFieldInitializerValue FromConstantTracked(TrackedStackValue trackedValue)
    {
        return new StaticFieldInitializerValue(StaticFieldInitializerValueKind.Constant, trackedValue);
    }

    public static StaticFieldInitializerValue FromStableIdentityTracked(TrackedStackValue trackedValue)
    {
        return new StaticFieldInitializerValue(StaticFieldInitializerValueKind.StableIdentity, trackedValue);
    }
}

internal readonly record struct TrackedStackValue(
    int? Int32Constant,
    string? KnownStringComparer,
    string? KnownExceptionType)
{
    public static TrackedStackValue Unknown => default;

    public bool IsUnknown =>
        Int32Constant is null &&
        string.IsNullOrWhiteSpace(KnownStringComparer) &&
        string.IsNullOrWhiteSpace(KnownExceptionType);

    public static TrackedStackValue FromInt32(int value)
    {
        return new TrackedStackValue(value, null, null);
    }

    public static TrackedStackValue FromKnownStringComparer(string value)
    {
        return new TrackedStackValue(null, value, null);
    }

    public static TrackedStackValue FromKnownExceptionType(string value)
    {
        return new TrackedStackValue(null, null, value);
    }
}
