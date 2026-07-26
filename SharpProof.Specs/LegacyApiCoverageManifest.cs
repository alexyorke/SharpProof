namespace SharpProof.Specs;

/// <summary>
/// Records the disposition of a source-pattern family formerly trusted by
/// KnownEffectCatalog. A disposition is an audit result, not executable
/// semantic authority.
/// </summary>
public enum LegacyApiDisposition {
    Ported,
    InapplicableLanguageSubset,
    UnsupportedTargetFramework,
    RejectedUnsoundLegacyClaim
}

public sealed record LegacyApiFamilyCoverage(
    string FamilyId,
    string SourceTypePattern,
    string SourceMemberPattern,
    LegacyApiDisposition Disposition,
    string Rationale,
    string? MappedApiSpecWitness);

public enum CurrentApiSpecOrigin {
    LegacyPort,
    NewSoundSeed
}

public sealed record CurrentApiSpecCoverage(
    string ApiSpecWitness,
    CurrentApiSpecOrigin Origin,
    string Rationale,
    string? LegacyFamilyId);

/// <summary>
/// A finite migration ledger for the 162 source-pattern families removed with
/// KnownEffectCatalog. Entries in this table never participate in analysis.
/// </summary>
public static class LegacyApiCoverageManifest {
    public const int ExpectedLegacyFamilyCount = 162;

    public const string ListAddFamilyId =
        "System.Collections.Generic.List<T>::Add";
    public const string EnumerableEmptyFamilyId =
        "System.Linq.Enumerable::Empty";

    private const string RejectedRationale =
        "The former name-and-shape matcher is not retained as authority; " +
        "the complete effect, allocation, throw, nullness, and value facets " +
        "have not been established by the current evidence and oracle gates.";
    private const string UnsupportedRationale =
        "The member is absent from at least one supported reference surface, " +
        "so no portable ApiSpec can be resolved for all supported targets.";
    private const string InapplicableRationale =
        "The member requires pointer, byref-like, or synchronization semantics " +
        "outside the currently admitted executable IR and proof subset.";

    public static ImmutableArray<LegacyApiFamilyCoverage> Families { get; } =
        CreateFamilies();

    /// <summary>
    /// Covers every BCL row currently shipped by <see cref="ApiSpecTable.Default"/>.
    /// Rows without a legacy family are explicit new sound seeds rather than
    /// fabricated legacy ports.
    /// </summary>
    public static ImmutableArray<CurrentApiSpecCoverage> CurrentBclRows { get; } = [
        NewSeed(
            "bcl.object.ctor",
            "This seed was introduced by the v2 evidence process; the legacy catalog did not model Object..ctor."),
        NewSeed(
            "bcl.string.length",
            "This seed was introduced by the v2 evidence process; the legacy catalog did not model String.Length."),
        NewSeed(
            "bcl.string.concat.string-string",
            "This seed was introduced by the v2 evidence process; the legacy catalog did not model this String.Concat overload."),
        LegacyPort(
            "bcl.list.add",
            ListAddFamilyId,
            "The concrete List<T>.Add row replaces this family with typed symbol resolution and conservative facets."),
        NewSeed(
            "bcl.math.abs.int32",
            "This seed was introduced by the v2 evidence process; Math.Abs was not a legacy catalog family."),
        LegacyPort(
            "bcl.enumerable.empty",
            EnumerableEmptyFamilyId,
            "The concrete Enumerable.Empty<T> row replaces this family with typed symbol resolution and cardinality evidence.")
    ];

    private static ImmutableArray<LegacyApiFamilyCoverage> CreateFamilies() {
        var entries = ImmutableArray.CreateBuilder<LegacyApiFamilyCoverage>(
            ExpectedLegacyFamilyCount);

        AddRejected(entries, "System.BitConverter", [
            "ToInt16", "ToInt32", "ToInt64", "ToUInt16", "ToUInt32",
            "ToUInt64", "ToBoolean", "ToChar", "ToSingle", "ToDouble",
            "ToString", "DoubleToInt64Bits", "Int64BitsToDouble",
            "SingleToInt32Bits", "Int32BitsToSingle", "GetBytes"
        ]);
        AddUnsupported(entries, "System.BitConverter", [
            "ToHalf", "DoubleToUInt64Bits", "UInt64BitsToDouble",
            "SingleToUInt32Bits", "UInt32BitsToSingle", "TryWriteBytes"
        ]);

        AddRejected(entries, "System.Math", [
            "Sin", "Cos", "Tan", "Asin", "Acos", "Atan", "Atan2",
            "Min", "Max", "Sqrt"
        ]);
        AddUnsupported(entries, "System.Math", [
            "BitIncrement", "BitDecrement", "Log2", "Cbrt", "CopySign",
            "ScaleB", "FusedMultiplyAdd", "ILogB"
        ]);
        AddUnsupported(entries, "System.MathF", [
            "BitIncrement", "BitDecrement", "Log2", "Cbrt", "Sin", "Cos",
            "Tan", "Asin", "Acos", "Atan", "CopySign", "Atan2", "ScaleB",
            "FusedMultiplyAdd", "ILogB", "Min", "Max", "Sqrt"
        ]);

        AddRejected(entries, "System.Array", [
            "Copy", "ConstrainedCopy", "Clear", "Fill", "Resize", "Reverse",
            "Clone", "GetLength", "GetLongLength", "GetLowerBound",
            "GetUpperBound", "Rank.get", "CreateInstance", "GetValue",
            "SetValue", "CopyTo", "Empty"
        ]);

        AddRejected(entries, "System.Buffer", [
            "BlockCopy", "ByteLength", "GetByte", "SetByte"
        ]);
        Add(
            entries,
            "System.Buffer",
            "MemoryCopy",
            LegacyApiDisposition.InapplicableLanguageSubset,
            InapplicableRationale);

        AddRejected(entries, "System.Collections.Generic.List<T>", [
            "property-get*", "property-set*", "ctor*"
        ]);
        Add(
            entries,
            "System.Collections.Generic.List<T>",
            "Add",
            LegacyApiDisposition.Ported,
            "A concrete typed ApiSpec now models List<T>.Add conservatively.",
            "bcl.list.add");
        AddRejected(entries, "System.Collections.Generic.Dictionary<TKey, TValue>", [
            "property-get*", "property-set*", "ctor*", "Add"
        ]);

        Add(
            entries,
            "System.Linq.Enumerable",
            "Empty",
            LegacyApiDisposition.Ported,
            "A concrete typed ApiSpec now models Enumerable.Empty<T> conservatively.",
            "bcl.enumerable.empty");
        AddRejected(entries, "System.Object", ["GetType"]);
        AddRejected(entries, "System.String", [
            "IsNullOrEmpty", "IsNullOrWhiteSpace", "Contains(char)",
            "IndexOf(char)", "LastIndexOf(char)", "StartsWith(char)",
            "EndsWith(char)", "Split", "Substring", "Trim", "TrimStart",
            "TrimEnd", "Replace", "ToUpper", "ToUpperInvariant", "ToLower",
            "ToLowerInvariant", "ToCharArray"
        ]);
        Add(
            entries,
            "System.String",
            "CopyTo(Span<char>)",
            LegacyApiDisposition.UnsupportedTargetFramework,
            UnsupportedRationale);

        foreach (var numericType in new[] {
                     "System.SByte", "System.Byte", "System.Int16",
                     "System.UInt16", "System.Int32", "System.UInt32",
                     "System.Int64", "System.IntPtr", "System.Single",
                     "System.Double", "System.Decimal"
                 })
            AddRejected(entries, numericType, ["Parse", "ToString"]);

        AddInapplicable(entries, "System.Threading.Interlocked", [
            "Increment", "Decrement", "Exchange", "Add", "CompareExchange",
            "Read"
        ]);
        AddInapplicable(entries, "System.Threading.Volatile", ["Read", "Write"]);

        foreach (var spanType in new[] {
                     "System.Span<T>", "System.ReadOnlySpan<T>",
                     "System.Memory<T>", "System.ReadOnlyMemory<T>"
                 }) {
            Add(
                entries,
                spanType,
                "Empty.get",
                LegacyApiDisposition.UnsupportedTargetFramework,
                UnsupportedRationale);
            Add(
                entries,
                spanType,
                "ToArray",
                LegacyApiDisposition.UnsupportedTargetFramework,
                UnsupportedRationale);
        }
        foreach (var spanType in new[] {
                     "System.Span<T>", "System.ReadOnlySpan<T>"
                 }) {
            Add(
                entries,
                spanType,
                "CopyTo",
                LegacyApiDisposition.InapplicableLanguageSubset,
                InapplicableRationale);
            Add(
                entries,
                spanType,
                "TryCopyTo",
                LegacyApiDisposition.InapplicableLanguageSubset,
                InapplicableRationale);
        }
        AddInapplicable(entries, "System.Span<T>", ["Fill", "Clear"]);
        AddInapplicable(entries, "System.MemoryExtensions", [
            "Reverse", "Overlaps", "Overlaps(out elementOffset)"
        ]);

        AddUnsupported(entries, "System.Runtime.InteropServices.MemoryMarshal", [
            "TryRead", "Read", "TryWrite", "Write"
        ]);
        AddUnsupported(entries, "System.Runtime.CompilerServices.RuntimeHelpers", [
            "IsReferenceOrContainsReferences", "GetSubArray"
        ]);

        return entries.MoveToImmutable();
    }

    private static void AddRejected(
        ImmutableArray<LegacyApiFamilyCoverage>.Builder entries,
        string type,
        IEnumerable<string> members) =>
        AddMany(
            entries,
            type,
            members,
            LegacyApiDisposition.RejectedUnsoundLegacyClaim,
            RejectedRationale);

    private static void AddUnsupported(
        ImmutableArray<LegacyApiFamilyCoverage>.Builder entries,
        string type,
        IEnumerable<string> members) =>
        AddMany(
            entries,
            type,
            members,
            LegacyApiDisposition.UnsupportedTargetFramework,
            UnsupportedRationale);

    private static void AddInapplicable(
        ImmutableArray<LegacyApiFamilyCoverage>.Builder entries,
        string type,
        IEnumerable<string> members) =>
        AddMany(
            entries,
            type,
            members,
            LegacyApiDisposition.InapplicableLanguageSubset,
            InapplicableRationale);

    private static void AddMany(
        ImmutableArray<LegacyApiFamilyCoverage>.Builder entries,
        string type,
        IEnumerable<string> members,
        LegacyApiDisposition disposition,
        string rationale) {
        foreach (var member in members)
            Add(entries, type, member, disposition, rationale);
    }

    private static void Add(
        ImmutableArray<LegacyApiFamilyCoverage>.Builder entries,
        string type,
        string member,
        LegacyApiDisposition disposition,
        string rationale,
        string? mappedApiSpecWitness = null) =>
        entries.Add(new LegacyApiFamilyCoverage(
            type + "::" + member,
            type,
            member,
            disposition,
            rationale,
            mappedApiSpecWitness));

    private static CurrentApiSpecCoverage NewSeed(
        string witness,
        string rationale) =>
        new(witness, CurrentApiSpecOrigin.NewSoundSeed, rationale, null);

    private static CurrentApiSpecCoverage LegacyPort(
        string witness,
        string familyId,
        string rationale) =>
        new(witness, CurrentApiSpecOrigin.LegacyPort, rationale, familyId);
}
