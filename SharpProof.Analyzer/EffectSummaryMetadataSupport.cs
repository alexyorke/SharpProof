using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;

// RS1035 exception: Roslyn exposes metadata references but not the PE method-body
// bytes or full-image hashes required to validate generated effect summaries.
// This adapter is the single, audited boundary that reads trusted reference and
// runtime assembly paths for identity and IL-body-hash validation. Keep analyzer
// file I/O isolated here and covered by architecture tests.
#pragma warning disable RS1035

namespace SharpProof.Analyzer;

internal static class SummaryMetadataNames
{
    internal static string NormalizeExactTypeName(string typeName)
    {
        return typeName switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.Char" => "char",
            "System.Decimal" => "decimal",
            "System.Double" => "double",
            "System.Int16" => "short",
            "System.Int32" => "int",
            "System.Int64" => "long",
            "System.IntPtr" => "nint",
            "System.Object" => "object",
            "System.SByte" => "sbyte",
            "System.Single" => "float",
            "System.String" => "string",
            "System.UInt16" => "ushort",
            "System.UInt32" => "uint",
            "System.UInt64" => "ulong",
            "System.UIntPtr" => "nuint",
            "System.Void" => "void",
            _ => typeName
        };
    }

    internal static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        if (handle.IsNil) return "<module>";

        var definition = reader.GetTypeDefinition(handle);
        var name = reader.GetString(definition.Name);
        var declaringType = definition.GetDeclaringType();
        if (!declaringType.IsNil) return GetTypeName(reader, declaringType) + "+" + name;

        var ns = reader.GetString(definition.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    internal static string GetTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
    {
        var reference = reader.GetTypeReference(handle);
        var name = reader.GetString(reference.Name);
        var ns = reader.GetString(reference.Namespace);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }
}

internal static class SummaryMethodIdentityMap
{
    internal static ImmutableDictionary<string, ActualMethodIdentity> Load(
        string path,
        bool normalizeSignatureTypeNames,
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
            foreach (var key in GetMethodKeys(metadataReader, handle, normalizeSignatureTypeNames))
                builder[key] = identity;
        }

        return builder.ToImmutable();
    }

    internal static bool TryResolve(
        ConcurrentDictionary<string, ImmutableDictionary<string, ActualMethodIdentity>> cache,
        IEnumerable<string> methodKeys,
        string assemblyPath,
        bool normalizeSignatureTypeNames,
        bool includeMethodAttributes,
        out ActualMethodIdentity identity)
    {
        identity = null!;
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath)) return false;

        var methodMap = cache.GetOrAdd(
            assemblyPath,
            path => Load(path, normalizeSignatureTypeNames, includeMethodAttributes));
        foreach (var key in methodKeys)
            if (methodMap.TryGetValue(key, out var foundIdentity))
            {
                identity = foundIdentity;
                return true;
            }

        return false;
    }

    private static IEnumerable<string> GetMethodKeys(
        MetadataReader reader,
        MethodDefinitionHandle handle,
        bool normalizeSignatureTypeNames)
    {
        var definition = reader.GetMethodDefinition(handle);
        var typeName = SummaryMetadataNames.GetTypeName(reader, definition.GetDeclaringType());
        var exactTypeName = SummaryMetadataNames.NormalizeExactTypeName(typeName);
        var methodName = reader.GetString(definition.Name);
        var displaySignature = DecodeMethodSignature(reader, definition, normalizeSignatureTypeNames, true);
        var positionalDisplaySignature = DecodeMethodSignature(reader, definition, normalizeSignatureTypeNames, false);
        var exactSignature = DecodeExactMethodSignature(reader, definition, normalizeSignatureTypeNames, true);
        var positionalExactSignature =
            DecodeExactMethodSignature(reader, definition, normalizeSignatureTypeNames, false);
        var keys = new[]
        {
            typeName + "." + methodName + displaySignature,
            typeName + "." + methodName + displaySignature,
            typeName + "." + methodName + positionalDisplaySignature,
            exactTypeName + "." + methodName + exactSignature,
            exactTypeName + "." + methodName + positionalExactSignature,
            GetRoslynLikeMethodSymbol(reader, definition, typeName, displaySignature)
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
            if (seen.Add(key))
                yield return key;
    }

    private static string DecodeMethodSignature(
        MetadataReader reader,
        MethodDefinition definition,
        bool normalizeTypeNames,
        bool useGenericContext)
    {
        try
        {
            var signature = definition.DecodeSignature(
                new SummarySignatureTypeNameProvider(normalizeTypeNames),
                useGenericContext ? CreateGenericContext(reader, definition) : null);
            return "(" + string.Join(", ", signature.ParameterTypes) + ")";
        }
        catch (BadImageFormatException)
        {
            return "(?)";
        }
    }

    private static string DecodeExactMethodSignature(
        MetadataReader reader,
        MethodDefinition definition,
        bool normalizeTypeNames,
        bool useGenericContext)
    {
        try
        {
            var signature = definition.DecodeSignature(
                new SummarySignatureTypeNameProvider(normalizeTypeNames),
                useGenericContext ? CreateGenericContext(reader, definition) : null);
            return "(" + string.Join(", ", signature.ParameterTypes) + ")->" + signature.ReturnType;
        }
        catch (BadImageFormatException)
        {
            return "(?)->?";
        }
    }

    private static string GetRoslynLikeMethodSymbol(
        MetadataReader reader,
        MethodDefinition definition,
        string typeName,
        string displaySignature)
    {
        var rawMethodName = reader.GetString(definition.Name);
        var methodName = rawMethodName;

        if (string.Equals(rawMethodName, ".ctor", StringComparison.Ordinal))
        {
            var lastSeparator = typeName.LastIndexOfAny(new[] { '.', '+' });
            methodName = lastSeparator >= 0 ? typeName.Substring(lastSeparator + 1) : typeName;
        }
        else if (rawMethodName.StartsWith("get_", StringComparison.Ordinal))
        {
            methodName = rawMethodName.Substring(4) + ".get";
        }
        else if (rawMethodName.StartsWith("set_", StringComparison.Ordinal))
        {
            methodName = rawMethodName.Substring(4) + ".set";
        }
        else
        {
            var genericNames = definition.GetGenericParameters()
                .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToArray();
            if (genericNames.Length > 0) methodName += "<" + string.Join(", ", genericNames) + ">";
        }

        return typeName + "." + methodName + displaySignature;
    }

    private static GenericContext CreateGenericContext(MetadataReader reader, MethodDefinition definition)
    {
        var typeDefinition = reader.GetTypeDefinition(definition.GetDeclaringType());
        var typeParameters = typeDefinition.GetGenericParameters()
            .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
            .ToImmutableArray();
        var methodParameters = definition.GetGenericParameters()
            .Select(handle => reader.GetString(reader.GetGenericParameter(handle).Name))
            .ToImmutableArray();
        return new GenericContext(typeParameters, methodParameters);
    }

    private sealed class SummarySignatureTypeNameProvider : ISignatureTypeProvider<string, object?>
    {
        private readonly bool _normalizeTypeNames;

        public SummarySignatureTypeNameProvider(bool normalizeTypeNames)
        {
            _normalizeTypeNames = normalizeTypeNames;
        }

        public string GetArrayType(string elementType, ArrayShape shape)
        {
            var rank = Math.Max(shape.Rank, 1);
            return elementType + "[" + new string(',', rank - 1) + "]";
        }

        public string GetByReferenceType(string elementType)
        {
            return "ref " + elementType;
        }

        public string GetFunctionPointerType(MethodSignature<string> signature)
        {
            return "delegate*";
        }

        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        {
            return genericType + "<" + string.Join(", ", typeArguments) + ">";
        }

        public string GetGenericMethodParameter(object? genericContext, int index)
        {
            var context = genericContext as GenericContext;
            return context != null && index >= 0 && index < context.MethodParameters.Length
                ? context.MethodParameters[index]
                : "!!" + index;
        }

        public string GetGenericTypeParameter(object? genericContext, int index)
        {
            var context = genericContext as GenericContext;
            return context != null && index >= 0 && index < context.TypeParameters.Length
                ? context.TypeParameters[index]
                : "!" + index;
        }

        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        {
            return unmodifiedType;
        }

        public string GetPinnedType(string elementType)
        {
            return elementType;
        }

        public string GetPointerType(string elementType)
        {
            return elementType + "*";
        }

        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            return typeCode switch
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
        }

        public string GetSZArrayType(string elementType)
        {
            return elementType + "[]";
        }

        public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            return NormalizeIfNeeded(SummaryMetadataNames.GetTypeName(metadataReader, handle));
        }

        public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            return NormalizeIfNeeded(SummaryMetadataNames.GetTypeReferenceName(metadataReader, handle));
        }

        public string GetTypeFromSpecification(
            MetadataReader metadataReader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            return metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }

        private string NormalizeIfNeeded(string typeName)
        {
            return _normalizeTypeNames
                ? SummaryMetadataNames.NormalizeExactTypeName(typeName)
                : typeName;
        }
    }

    private sealed class GenericContext
    {
        public GenericContext(ImmutableArray<string> typeParameters, ImmutableArray<string> methodParameters)
        {
            TypeParameters = typeParameters;
            MethodParameters = methodParameters;
        }

        public ImmutableArray<string> TypeParameters { get; }

        public ImmutableArray<string> MethodParameters { get; }
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
                !SymbolEqualityComparer.Default.Equals(assemblySymbol, containingAssembly))
                continue;

            var referencePath = reference.FilePath;
            return string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath)
                ? null
                : referencePath;
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

                var location = assembly.Location;
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
            var location = assembly.Location;
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

    private readonly bool _normalizeSignatureTypeNames;
    private readonly bool _requireMetadataLocation;

    private readonly ConcurrentDictionary<string, string> _runtimeImplementationAssemblyPathByAssemblyNameCache =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, string?> _runtimeImplementationAssemblyPathCache =
        new(StringComparer.Ordinal);

    internal EffectSummaryIdentityResolver(
        bool normalizeSignatureTypeNames,
        bool includeMethodAttributes,
        bool requireMetadataLocation,
        Func<IMethodSymbol, string> methodCacheKeyFactory)
    {
        _normalizeSignatureTypeNames = normalizeSignatureTypeNames;
        _includeMethodAttributes = includeMethodAttributes;
        _requireMetadataLocation = requireMetadataLocation;
        _methodCacheKeyFactory = methodCacheKeyFactory;
    }

    internal ActualMethodIdentity? TryResolveActualMethodIdentity(
        IMethodSymbol methodSymbol,
        Compilation compilation)
    {
        var implementationPath = TryResolveRuntimeImplementationAssemblyPath(methodSymbol);
        if (!string.IsNullOrWhiteSpace(implementationPath))
        {
            var path = implementationPath!;
            if (File.Exists(path) &&
                TryResolveMethodIdentityFromPath(methodSymbol, path, out var implementationIdentity))
                return implementationIdentity;
        }

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
        var implementationPath = TryResolveRuntimeImplementationAssemblyPath(methodSymbol);
        if (!string.IsNullOrWhiteSpace(implementationPath))
        {
            var path = implementationPath!;
            if (File.Exists(path) &&
                TryResolveMethodIdentityFromPath(methodSymbol, path, out _))
                return GetAssemblyIdentity(path);
        }

        var referencePath = SummaryAssemblyReferenceResolver.FindContainingAssemblyReferencePath(
            methodSymbol,
            compilation,
            _requireMetadataLocation);
        return referencePath == null ? null : GetAssemblyIdentity(referencePath);
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
        return TryResolveMethodIdentityFromPath(EffectSummarySymbolKeyFactory.GetMethodSymbolKeys(methodSymbol),
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
            _normalizeSignatureTypeNames,
            _includeMethodAttributes,
            out identity);
    }

    internal string? TryResolveRuntimeImplementationAssemblyPath(IMethodSymbol methodSymbol)
    {
        var cacheKey = _methodCacheKeyFactory(methodSymbol.OriginalDefinition);
        return _runtimeImplementationAssemblyPathCache.GetOrAdd(
            cacheKey,
            _ => ResolveRuntimeImplementationAssemblyPath(
                EffectSummarySymbolKeyFactory.GetMethodSymbolKeys(methodSymbol),
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

internal sealed class SummaryAssemblyIdentity
{
    public SummaryAssemblyIdentity(string? assemblyName, string? assemblySha256, string? moduleVersionId)
    {
        AssemblyName = assemblyName;
        AssemblySha256 = assemblySha256;
        ModuleVersionId = moduleVersionId;
    }

    public string? AssemblyName { get; }

    public string? AssemblySha256 { get; }

    public string? ModuleVersionId { get; }

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(AssemblyName) &&
        !string.IsNullOrWhiteSpace(AssemblySha256) &&
        !string.IsNullOrWhiteSpace(ModuleVersionId);

    public bool Matches(ActualAssemblyIdentity actualAssemblyIdentity)
    {
        return string.Equals(AssemblyName, actualAssemblyIdentity.AssemblyName, StringComparison.Ordinal) &&
               string.Equals(AssemblySha256, actualAssemblyIdentity.AssemblySha256,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ModuleVersionId, actualAssemblyIdentity.ModuleVersionId,
                   StringComparison.OrdinalIgnoreCase);
    }

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

    public static SummaryAssemblyIdentity? FromJson(JsonElement assemblyElement)
    {
        var assemblyName = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "AssemblyName");
        var assemblySha256 = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "AssemblySha256");
        var moduleVersionId = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "ModuleVersionId");
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

internal sealed class SummaryMethodIdentity
{
    public SummaryMethodIdentity(string? metadataToken, string? methodBodySha256)
    {
        MetadataToken = metadataToken;
        MethodBodySha256 = methodBodySha256;
    }

    public string? MetadataToken { get; }

    public string? MethodBodySha256 { get; }

    public bool MatchesMetadataToken(ActualMethodIdentity? actualMethodIdentity)
    {
        return actualMethodIdentity != null &&
               !string.IsNullOrWhiteSpace(MetadataToken) &&
               string.Equals(MetadataToken, actualMethodIdentity.MetadataToken, StringComparison.OrdinalIgnoreCase);
    }

    public bool IsCompleteEnoughFor(ActualMethodIdentity? actualMethodIdentity)
    {
        if (!MatchesMetadataToken(actualMethodIdentity)) return false;

        if (!actualMethodIdentity!.HasMethodBody) return true;

        return !string.IsNullOrWhiteSpace(MethodBodySha256);
    }

    public bool Matches(ActualMethodIdentity actualMethodIdentity)
    {
        if (!string.Equals(MetadataToken, actualMethodIdentity.MetadataToken, StringComparison.OrdinalIgnoreCase))
            return false;

        if (actualMethodIdentity.MethodBodySha256 == null) return true;

        return string.Equals(MethodBodySha256, actualMethodIdentity.MethodBodySha256,
            StringComparison.OrdinalIgnoreCase);
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

    public static SummaryMethodIdentity? FromJson(JsonElement methodElement)
    {
        var metadataToken = CompatibilityHelpers.GetTrimmedStringProperty(methodElement, "MetadataToken");
        var methodBodySha256 = CompatibilityHelpers.GetTrimmedStringProperty(methodElement, "MethodBodySha256");
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
    private bool _methodBodySha256Computed;

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

internal sealed class MethodBodyHashProvider
{
    private readonly string _assemblyPath;
    private readonly Dictionary<int, string?> _cache = new();
    private readonly object _lock = new();
    private byte[]? _assemblyBytes;

    public MethodBodyHashProvider(string assemblyPath)
    {
        _assemblyPath = assemblyPath;
    }

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
                return CompatibilityHelpers.ToLowerHex(sha256.ComputeHash(il));
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

internal sealed class ActualAssemblyIdentity
{
    public ActualAssemblyIdentity(string assemblyName, string assemblySha256, string moduleVersionId)
    {
        AssemblyName = assemblyName;
        AssemblySha256 = assemblySha256;
        ModuleVersionId = moduleVersionId;
    }

    public string AssemblyName { get; }

    public string AssemblySha256 { get; }

    public string ModuleVersionId { get; }

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

        return new ActualAssemblyIdentity(assemblyName, assemblySha256, moduleVersionId);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return CompatibilityHelpers.ToLowerHex(sha256.ComputeHash(stream));
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
