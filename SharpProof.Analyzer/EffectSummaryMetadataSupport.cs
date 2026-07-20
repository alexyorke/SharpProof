using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

// RS1035 exception: Roslyn exposes metadata references but not the PE method-body
// bytes or full-image hashes required to validate generated effect summaries.
// This is the single, audited boundary that reads trusted reference and
// runtime assembly paths for identity and IL-body-hash validation. Keep analyzer
// file I/O isolated here and covered by architecture tests.
#pragma warning disable RS1035

namespace SharpProof.Analyzer;

internal static class SummaryMethodIdentityMap
{
    internal static ImmutableDictionary<string, ActualMethodIdentity> Load(
        string path,
        bool includeMethodAttributes)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) return ImmutableDictionary<string, ActualMethodIdentity>.Empty;

        var metadataReader = peReader.GetMetadataReader();
        var builder = ImmutableDictionary.CreateBuilder<string, ActualMethodIdentity>(StringComparer.Ordinal);
        var methodBodyHashProvider = new MethodBodyHashProvider(path);
        foreach (var handle in metadataReader.MethodDefinitions)
        {
            var definition = metadataReader.GetMethodDefinition(handle);
            var token = "0x" + MetadataTokens.GetToken(handle).ToString("X8");
            var identity = new ActualMethodIdentity(
                token,
                methodBodyHashProvider,
                definition.RelativeVirtualAddress,
                includeMethodAttributes ? definition.Attributes : 0);
            builder[EcmaStructuralMethodIdentity.GetCanonicalKey(metadataReader, handle)] = identity;
        }

        return builder.ToImmutable();
    }

    internal static bool TryResolve(
        ConcurrentDictionary<string, ImmutableDictionary<string, ActualMethodIdentity>> cache,
        IEnumerable<string> methodKeys,
        string assemblyPath,
        bool includeMethodAttributes,
        out ActualMethodIdentity identity)
    {
        identity = null!;
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath)) return false;

        var methodMap = cache.GetOrAdd(
            assemblyPath,
            path => Load(path, includeMethodAttributes));
        foreach (var key in methodKeys)
            if (methodMap.TryGetValue(key, out var foundIdentity))
            {
                identity = foundIdentity;
                return true;
            }

        return false;
    }

}

internal static class SummaryAssemblyReferenceResolver
{
    internal static string? FindContainingAssemblyReferencePath(
        IMethodSymbol methodSymbol,
        Compilation compilation,
        bool requireMetadataLocation)
    {
        if (requireMetadataLocation &&
            methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            return null;

        return FindAssemblyReferencePath(methodSymbol.ContainingAssembly, compilation);
    }

    internal static string? FindAssemblyReferencePath(
        IAssemblySymbol containingAssembly,
        Compilation compilation)
    {
        foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
        {
            var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
            if (assemblySymbol == null ||
                !SymbolEq.AreEqual(assemblySymbol, containingAssembly))
                continue;

            var referencePath = reference.FilePath;
            if (!string.IsNullOrWhiteSpace(referencePath) && File.Exists(referencePath))
                return referencePath;
        }

        return null;
    }
}

internal static class RuntimeImplementationAssemblyResolver
{
    internal static string? Resolve(
        IEnumerable<string> methodKeys,
        IAssemblySymbol? containingAssembly,
        ConcurrentDictionary<string, string> pathByAssemblyName,
        Func<ImmutableArray<string>, string, bool> containsMethodIdentity)
    {
        var keys = methodKeys.ToImmutableArray();
        if (keys.IsDefaultOrEmpty) return null;

        var coreLibPath = typeof(object).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(coreLibPath) &&
            File.Exists(coreLibPath) &&
            containsMethodIdentity(keys, coreLibPath))
            return coreLibPath;

        var assemblyName = containingAssembly?.Identity.Name;
        if (!string.IsNullOrWhiteSpace(assemblyName) &&
            pathByAssemblyName.TryGetValue(assemblyName!, out var cachedAssemblyPath) &&
            File.Exists(cachedAssemblyPath) &&
            containsMethodIdentity(keys, cachedAssemblyPath))
            return cachedAssemblyPath;

        if (!string.IsNullOrWhiteSpace(assemblyName))
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal)) continue;

                if (!TryGetAssemblyLocation(assembly, out var location)) continue;
                if (!string.IsNullOrWhiteSpace(location) &&
                    File.Exists(location) &&
                    containsMethodIdentity(keys, location))
                {
                    pathByAssemblyName[assemblyName!] = location;
                    return location;
                }
            }

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!TryGetAssemblyLocation(assembly, out var location)) continue;
            if (string.IsNullOrWhiteSpace(location) ||
                !File.Exists(location) ||
                string.Equals(location, coreLibPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (containsMethodIdentity(keys, location))
            {
                if (!string.IsNullOrWhiteSpace(assemblyName)) pathByAssemblyName[assemblyName!] = location;

                return location;
            }
        }

        foreach (var trustedPlatformAssemblyPath in RuntimeMetadataAssemblyLocator.GetTrustedPlatformAssemblyPaths())
        {
            if (string.Equals(trustedPlatformAssemblyPath, coreLibPath, StringComparison.OrdinalIgnoreCase)) continue;

            if (containsMethodIdentity(keys, trustedPlatformAssemblyPath))
            {
                if (!string.IsNullOrWhiteSpace(assemblyName))
                    pathByAssemblyName[assemblyName!] = trustedPlatformAssemblyPath;

                return trustedPlatformAssemblyPath;
            }
        }

        return null;
    }

    private static bool TryGetAssemblyLocation(System.Reflection.Assembly assembly, out string location)
    {
        location = string.Empty;
        if (assembly.IsDynamic) return false;

        try
        {
            location = assembly.Location;
            return !string.IsNullOrWhiteSpace(location);
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }
}

internal sealed class EffectSummaryIdentityResolver
{
    private readonly ConcurrentDictionary<string, ActualAssemblyIdentity?> _assemblyIdentityCache =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly bool _includeMethodAttributes;
    private readonly Func<IMethodSymbol, string> _methodCacheKeyFactory;

    private readonly ConcurrentDictionary<string, ImmutableDictionary<string, ActualMethodIdentity>>
        _methodIdentityCache =
            new(StringComparer.OrdinalIgnoreCase);

    private readonly bool _requireMetadataLocation;

    private readonly ConcurrentDictionary<string, string> _runtimeImplementationAssemblyPathByAssemblyNameCache =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string?> _runtimeImplementationAssemblyPathCache =
        new(StringComparer.Ordinal);

    internal EffectSummaryIdentityResolver(
        bool includeMethodAttributes,
        bool requireMetadataLocation,
        Func<IMethodSymbol, string> methodCacheKeyFactory)
    {
        _includeMethodAttributes = includeMethodAttributes;
        _requireMetadataLocation = requireMetadataLocation;
        _methodCacheKeyFactory = methodCacheKeyFactory;
    }

    internal ActualMethodIdentity? TryResolveActualMethodIdentity(
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        if (TryResolveValidatedRuntimeImplementation(methodSymbol) is { } implementation)
            return implementation.MethodIdentity;

        var referencePath = SummaryAssemblyReferenceResolver.FindContainingAssemblyReferencePath(
            methodSymbol,
            compilation,
            _requireMetadataLocation);
        return referencePath != null &&
               TryResolveMethodIdentityFromPath(methodSymbol, referencePath, out var identity)
            ? identity
            : null;
    }

    internal ActualAssemblyIdentity? TryResolveActualAssemblyIdentity(
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        if (TryResolveValidatedRuntimeImplementation(methodSymbol) is { } implementation)
            return GetAssemblyIdentity(implementation.AssemblyPath);

        var referencePath = SummaryAssemblyReferenceResolver.FindContainingAssemblyReferencePath(
            methodSymbol,
            compilation,
            _requireMetadataLocation);
        return referencePath == null ? null : GetAssemblyIdentity(referencePath);
    }

    private (string AssemblyPath, ActualMethodIdentity MethodIdentity)? TryResolveValidatedRuntimeImplementation(
        IMethodSymbol methodSymbol)
    {
        var assemblyPath = TryResolveRuntimeImplementationAssemblyPath(methodSymbol);
        return !string.IsNullOrWhiteSpace(assemblyPath) &&
               File.Exists(assemblyPath) &&
               TryResolveMethodIdentityFromPath(methodSymbol, assemblyPath!, out var methodIdentity)
            ? (assemblyPath!, methodIdentity)
            : null;
    }

    internal ActualAssemblyIdentity? GetAssemblyIdentity(string assemblyPath)
    {
        return _assemblyIdentityCache.GetOrAdd(assemblyPath,
            static resolvedPath => ActualAssemblyIdentity.FromFile(resolvedPath));
    }

    internal bool TryResolveMethodIdentityFromPath(
        IMethodSymbol methodSymbol,
        string assemblyPath,
        out ActualMethodIdentity identity)
    {
        return TryResolveMethodIdentityFromPath(
            RoslynStructuralMethodIdentity.GetCanonicalKeys(methodSymbol),
            assemblyPath, out identity);
    }

    internal bool TryResolveMethodIdentityFromPath(
        IEnumerable<string> methodKeys,
        string assemblyPath,
        out ActualMethodIdentity identity)
    {
        return SummaryMethodIdentityMap.TryResolve(
            _methodIdentityCache,
            methodKeys,
            assemblyPath,
            _includeMethodAttributes,
            out identity);
    }

    internal string? TryResolveRuntimeImplementationAssemblyPath(IMethodSymbol methodSymbol)
    {
        var cacheKey = _methodCacheKeyFactory(methodSymbol.OriginalDefinition);
        return _runtimeImplementationAssemblyPathCache.GetOrAdd(
            cacheKey,
            _ => ResolveRuntimeImplementationAssemblyPath(
                RoslynStructuralMethodIdentity.GetCanonicalKeys(methodSymbol),
                methodSymbol.ContainingAssembly));
    }

    internal string? TryResolveRuntimeImplementationAssemblyPath(
        IAssemblySymbol? containingAssembly,
        ImmutableArray<string> methodKeys,
        string cacheKey)
    {
        return _runtimeImplementationAssemblyPathCache.GetOrAdd(
            cacheKey,
            _ => ResolveRuntimeImplementationAssemblyPath(methodKeys, containingAssembly));
    }

    private string? ResolveRuntimeImplementationAssemblyPath(
        IEnumerable<string> methodKeys,
        IAssemblySymbol? containingAssembly)
    {
        return RuntimeImplementationAssemblyResolver.Resolve(
            methodKeys,
            containingAssembly,
            _runtimeImplementationAssemblyPathByAssemblyNameCache,
            (keys, path) => TryResolveMethodIdentityFromPath(keys, path, out _));
    }
}

internal sealed record SummaryAssemblyIdentity(
    string? AssemblyName,
    string? AssemblySha256,
    string? ModuleVersionId)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(AssemblyName) &&
        !string.IsNullOrWhiteSpace(AssemblySha256) &&
        !string.IsNullOrWhiteSpace(ModuleVersionId);


    public EffectSummaryCompatibility GetCompatibility(ActualAssemblyIdentity? actualAssemblyIdentity)
    {
        if (!IsComplete)
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_incomplete_assembly_identity",
                "its assembly identity is incomplete");

        if (actualAssemblyIdentity == null)
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_assembly_identity_unavailable",
                "the current assembly identity could not be resolved");

        if (!string.Equals(AssemblyName, actualAssemblyIdentity.AssemblyName, StringComparison.Ordinal))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_assembly_name_mismatch",
                $"assembly name '{AssemblyName}' does not match '{actualAssemblyIdentity.AssemblyName}'");

        if (!string.Equals(
                AssemblySha256,
                actualAssemblyIdentity.AssemblySha256,
                StringComparison.OrdinalIgnoreCase))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_assembly_hash_mismatch",
                "its assembly SHA-256 does not match the current assembly");

        if (!string.Equals(
                ModuleVersionId,
                actualAssemblyIdentity.ModuleVersionId,
                StringComparison.OrdinalIgnoreCase))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_module_version_mismatch",
                $"module version '{ModuleVersionId}' does not match '{actualAssemblyIdentity.ModuleVersionId}'");

        return EffectSummaryCompatibility.Compatible;
    }

    public static SummaryAssemblyIdentity? FromContract(
        string? assemblyName,
        string? assemblySha256,
        string? moduleVersionId)
    {
        if (string.IsNullOrWhiteSpace(assemblyName) &&
            string.IsNullOrWhiteSpace(assemblySha256) &&
            string.IsNullOrWhiteSpace(moduleVersionId))
            return null;

        return new SummaryAssemblyIdentity(
            assemblyName?.Trim(),
            assemblySha256?.Trim(),
            moduleVersionId?.Trim());
    }
}

internal sealed record SummaryMethodIdentity(string? MetadataToken, string? MethodBodySha256)
{
    public bool MatchesMetadataToken(ActualMethodIdentity? actualMethodIdentity)
    {
        return actualMethodIdentity != null &&
               !string.IsNullOrWhiteSpace(MetadataToken) &&
               string.Equals(MetadataToken, actualMethodIdentity.MetadataToken, StringComparison.OrdinalIgnoreCase);
    }



    public EffectSummaryCompatibility GetCompatibility(ActualMethodIdentity? actualMethodIdentity)
    {
        if (string.IsNullOrWhiteSpace(MetadataToken))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_incomplete_method_identity",
                "its method metadata token is missing");

        if (actualMethodIdentity == null)
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_method_identity_unavailable",
                "the current method identity could not be resolved");

        if (!string.Equals(MetadataToken, actualMethodIdentity.MetadataToken, StringComparison.OrdinalIgnoreCase))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_metadata_token_mismatch",
                $"metadata token '{MetadataToken}' does not match '{actualMethodIdentity.MetadataToken}'");

        if (!actualMethodIdentity.HasMethodBody) return EffectSummaryCompatibility.Compatible;

        if (string.IsNullOrWhiteSpace(MethodBodySha256))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_incomplete_method_identity",
                "its method-body SHA-256 is missing");

        if (!string.Equals(
                MethodBodySha256,
                actualMethodIdentity.MethodBodySha256,
                StringComparison.OrdinalIgnoreCase))
            return EffectSummaryCompatibility.Incompatible(
                "effect_summary_method_body_hash_mismatch",
                "its method-body SHA-256 does not match the current method body");

        return EffectSummaryCompatibility.Compatible;
    }

    public static SummaryMethodIdentity? FromContract(string? metadataToken, string? methodBodySha256)
    {
        if (string.IsNullOrWhiteSpace(metadataToken) && string.IsNullOrWhiteSpace(methodBodySha256)) return null;

        return new SummaryMethodIdentity(metadataToken?.Trim(), methodBodySha256?.Trim());
    }
}

internal sealed class ActualMethodIdentity
{
    private readonly MethodBodyHashProvider? _methodBodyHashProvider;
    private readonly object _methodBodySha256Lock = new();
    private readonly int _relativeVirtualAddress;
    private string? _methodBodySha256;
    private volatile bool _methodBodySha256Computed;

    public ActualMethodIdentity(string metadataToken, string? methodBodySha256, MethodAttributes attributes = 0)
    {
        MetadataToken = metadataToken;
        _methodBodySha256 = methodBodySha256;
        _methodBodySha256Computed = true;
        HasMethodBody = methodBodySha256 != null;
        Attributes = attributes;
    }

    public ActualMethodIdentity(string metadataToken, MethodBodyHashProvider methodBodyHashProvider,
        int relativeVirtualAddress, MethodAttributes attributes = 0)
    {
        MetadataToken = metadataToken;
        _methodBodyHashProvider = methodBodyHashProvider;
        _relativeVirtualAddress = relativeVirtualAddress;
        _methodBodySha256Computed = relativeVirtualAddress == 0;
        HasMethodBody = relativeVirtualAddress != 0;
        Attributes = attributes;
    }

    public string MetadataToken { get; }

    public bool HasMethodBody { get; }

    public string? MethodBodySha256
    {
        get
        {
            if (_methodBodySha256Computed) return _methodBodySha256;

            lock (_methodBodySha256Lock)
            {
                if (!_methodBodySha256Computed)
                {
                    _methodBodySha256 = _methodBodyHashProvider?.ComputeMethodBodySha256(_relativeVirtualAddress);
                    _methodBodySha256Computed = true;
                }
            }

            return _methodBodySha256;
        }
    }

    public MethodAttributes Attributes { get; }

    public bool CanBeOverridden =>
        Attributes.HasFlag(MethodAttributes.Virtual) &&
        !Attributes.HasFlag(MethodAttributes.Final) &&
        !Attributes.HasFlag(MethodAttributes.Static);
}

internal sealed class MethodBodyHashProvider(string assemblyPath)
{
    private readonly string _assemblyPath = assemblyPath;
    private readonly Dictionary<int, string?> _cache = new();
    private readonly object _lock = new();
    private byte[]? _assemblyBytes;

    public string? ComputeMethodBodySha256(int relativeVirtualAddress)
    {
        if (relativeVirtualAddress == 0) return null;

        lock (_lock)
        {
            if (_cache.TryGetValue(relativeVirtualAddress, out var cached)) return cached;

            var hash = ComputeMethodBodySha256Core(relativeVirtualAddress);
            _cache[relativeVirtualAddress] = hash;
            return hash;
        }
    }

    private string? ComputeMethodBodySha256Core(int relativeVirtualAddress)
    {
        if (string.IsNullOrWhiteSpace(_assemblyPath) ||
            !File.Exists(_assemblyPath))
            return null;

        try
        {
            _assemblyBytes ??= File.ReadAllBytes(_assemblyPath);
            using (var stream = new MemoryStream(_assemblyBytes, false))
            using (var peReader = new PEReader(stream))
            {
                if (!peReader.HasMetadata) return null;

                var body = peReader.GetMethodBody(relativeVirtualAddress);
                var il = body.GetILBytes();
                if (il == null) return null;

                using var sha256 = SHA256.Create();
                return LowerHexEncoding.Encode(sha256.ComputeHash(il));
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }
}

internal sealed record ActualAssemblyIdentity(
    string AssemblyName,
    string AssemblySha256,
    string ModuleVersionId,
    string? AssemblyPath = null)
{
    public static ActualAssemblyIdentity? FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        if (!peReader.HasMetadata) return null;

        var metadataReader = peReader.GetMetadataReader();
        var assemblyName = metadataReader.IsAssembly
            ? metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name)
            : Path.GetFileNameWithoutExtension(path);
        var moduleVersionId = metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid).ToString("D");
        var assemblySha256 = ComputeSha256(path);

        return new ActualAssemblyIdentity(assemblyName, assemblySha256, moduleVersionId, Path.GetFullPath(path));
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return LowerHexEncoding.Encode(sha256.ComputeHash(stream));
    }
}

internal static class RuntimeMetadataAssemblyLocator
{
    private static readonly Lazy<ImmutableArray<string>> TrustedPlatformAssemblyPaths =
        new(CreateTrustedPlatformAssemblyPaths, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IEnumerable<string> GetTrustedPlatformAssemblyPaths()
    {
        return TrustedPlatformAssemblyPaths.Value;
    }

    private static ImmutableArray<string> CreateTrustedPlatformAssemblyPaths()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            var trustedPlatformAssembliesValue = trustedPlatformAssemblies!;
            return trustedPlatformAssembliesValue
                .Split(Path.PathSeparator)
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToImmutableArray();
        }

        var coreAssemblyLocation = typeof(object).Assembly.Location;
        if (string.IsNullOrWhiteSpace(coreAssemblyLocation)) return ImmutableArray<string>.Empty;

        var runtimeDirectory = Path.GetDirectoryName(coreAssemblyLocation);
        if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
            return ImmutableArray<string>.Empty;

        return Directory
            .EnumerateFiles(runtimeDirectory, "*.dll", SearchOption.TopDirectoryOnly)
            .ToImmutableArray();
    }
}

#pragma warning restore RS1035
