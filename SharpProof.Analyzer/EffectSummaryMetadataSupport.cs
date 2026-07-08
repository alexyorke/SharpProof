using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace SharpProof.Analyzer
{
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
            if (handle.IsNil)
            {
                return "<module>";
            }

            var definition = reader.GetTypeDefinition(handle);
            var name = reader.GetString(definition.Name);
            var declaringType = definition.GetDeclaringType();
            if (!declaringType.IsNil)
            {
                return GetTypeName(reader, declaringType) + "+" + name;
            }

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
            if (!peReader.HasMetadata)
            {
                return ImmutableDictionary<string, ActualMethodIdentity>.Empty;
            }

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
                {
                    builder[key] = identity;
                }
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
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            {
                return false;
            }

            var methodMap = cache.GetOrAdd(
                assemblyPath,
                path => Load(path, normalizeSignatureTypeNames, includeMethodAttributes));
            foreach (var key in methodKeys)
            {
                if (methodMap.TryGetValue(key, out var foundIdentity))
                {
                    identity = foundIdentity;
                    return true;
                }
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
            var displaySignature = DecodeMethodSignature(reader, definition, normalizeSignatureTypeNames, useGenericContext: true);
            var positionalDisplaySignature = DecodeMethodSignature(reader, definition, normalizeSignatureTypeNames, useGenericContext: false);
            var exactSignature = DecodeExactMethodSignature(reader, definition, normalizeSignatureTypeNames, useGenericContext: true);
            var positionalExactSignature = DecodeExactMethodSignature(reader, definition, normalizeSignatureTypeNames, useGenericContext: false);
            var keys = new[]
            {
                typeName + "." + methodName + displaySignature,
                typeName + "." + methodName + displaySignature,
                typeName + "." + methodName + positionalDisplaySignature,
                exactTypeName + "." + methodName + exactSignature,
                exactTypeName + "." + methodName + positionalExactSignature,
                GetRoslynLikeMethodSymbol(reader, definition, typeName, displaySignature),
            };

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (seen.Add(key))
                {
                    yield return key;
                }
            }
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
                if (genericNames.Length > 0)
                {
                    methodName += "<" + string.Join(", ", genericNames) + ">";
                }
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

            public string GetByReferenceType(string elementType) => "ref " + elementType;

            public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";

            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
                => genericType + "<" + string.Join(", ", typeArguments) + ">";

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

            public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;

            public string GetPinnedType(string elementType) => elementType;

            public string GetPointerType(string elementType) => elementType + "*";

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
                    _ => typeCode.ToString(),
                };
            }

            public string GetSZArrayType(string elementType) => elementType + "[]";

            public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind)
                => NormalizeIfNeeded(SummaryMetadataNames.GetTypeName(metadataReader, handle));

            public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
                => NormalizeIfNeeded(SummaryMetadataNames.GetTypeReferenceName(metadataReader, handle));

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

    internal static class RuntimeImplementationAssemblyResolver
    {
        internal static string? Resolve(
            IEnumerable<string> methodKeys,
            IAssemblySymbol? containingAssembly,
            ConcurrentDictionary<string, string> pathByAssemblyName,
            Func<ImmutableArray<string>, string, bool> containsMethodIdentity)
        {
            var keys = methodKeys.ToImmutableArray();
            if (keys.IsDefaultOrEmpty)
            {
                return null;
            }

            var coreLibPath = typeof(object).Assembly.Location;
            if (!string.IsNullOrWhiteSpace(coreLibPath) &&
                File.Exists(coreLibPath) &&
                containsMethodIdentity(keys, coreLibPath))
            {
                return coreLibPath;
            }

            var assemblyName = containingAssembly?.Identity.Name;
            if (!string.IsNullOrWhiteSpace(assemblyName) &&
                pathByAssemblyName.TryGetValue(assemblyName!, out var cachedAssemblyPath) &&
                File.Exists(cachedAssemblyPath) &&
                containsMethodIdentity(keys, cachedAssemblyPath))
            {
                return cachedAssemblyPath;
            }

            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var location = assembly.Location;
                    if (!string.IsNullOrWhiteSpace(location) &&
                        File.Exists(location) &&
                        containsMethodIdentity(keys, location))
                    {
                        pathByAssemblyName[assemblyName!] = location;
                        return location;
                    }
                }
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var location = assembly.Location;
                if (string.IsNullOrWhiteSpace(location) ||
                    !File.Exists(location) ||
                    string.Equals(location, coreLibPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (containsMethodIdentity(keys, location))
                {
                    if (!string.IsNullOrWhiteSpace(assemblyName))
                    {
                        pathByAssemblyName[assemblyName!] = location;
                    }

                    return location;
                }
            }

            foreach (var trustedPlatformAssemblyPath in RuntimeMetadataAssemblyLocator.GetTrustedPlatformAssemblyPaths())
            {
                if (string.Equals(trustedPlatformAssemblyPath, coreLibPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (containsMethodIdentity(keys, trustedPlatformAssemblyPath))
                {
                    if (!string.IsNullOrWhiteSpace(assemblyName))
                    {
                        pathByAssemblyName[assemblyName!] = trustedPlatformAssemblyPath;
                    }

                    return trustedPlatformAssemblyPath;
                }
            }

            return null;
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
                string.Equals(AssemblySha256, actualAssemblyIdentity.AssemblySha256, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ModuleVersionId, actualAssemblyIdentity.ModuleVersionId, StringComparison.OrdinalIgnoreCase);
        }

        public static SummaryAssemblyIdentity? FromJson(JsonElement assemblyElement)
        {
            var assemblyName = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "AssemblyName");
            var assemblySha256 = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "AssemblySha256");
            var moduleVersionId = CompatibilityHelpers.GetTrimmedStringProperty(assemblyElement, "ModuleVersionId");
            if (string.IsNullOrWhiteSpace(assemblyName) &&
                string.IsNullOrWhiteSpace(assemblySha256) &&
                string.IsNullOrWhiteSpace(moduleVersionId))
            {
                return null;
            }

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
            if (!MatchesMetadataToken(actualMethodIdentity))
            {
                return false;
            }

            if (!actualMethodIdentity!.HasMethodBody)
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(MethodBodySha256);
        }

        public bool Matches(ActualMethodIdentity actualMethodIdentity)
        {
            if (!string.Equals(MetadataToken, actualMethodIdentity.MetadataToken, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (actualMethodIdentity.MethodBodySha256 == null)
            {
                return true;
            }

            return string.Equals(MethodBodySha256, actualMethodIdentity.MethodBodySha256, StringComparison.OrdinalIgnoreCase);
        }

        public static SummaryMethodIdentity? FromJson(JsonElement methodElement)
        {
            var metadataToken = CompatibilityHelpers.GetTrimmedStringProperty(methodElement, "MetadataToken");
            var methodBodySha256 = CompatibilityHelpers.GetTrimmedStringProperty(methodElement, "MethodBodySha256");
            if (string.IsNullOrWhiteSpace(metadataToken) && string.IsNullOrWhiteSpace(methodBodySha256))
            {
                return null;
            }

            return new SummaryMethodIdentity(metadataToken?.Trim(), methodBodySha256?.Trim());
        }
    }

    internal sealed class ActualMethodIdentity
    {
        private readonly object _methodBodySha256Lock = new object();
        private readonly MethodBodyHashProvider? _methodBodyHashProvider;
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

        public ActualMethodIdentity(string metadataToken, MethodBodyHashProvider methodBodyHashProvider, int relativeVirtualAddress, MethodAttributes attributes = 0)
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
                if (_methodBodySha256Computed)
                {
                    return _methodBodySha256;
                }

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
        private readonly object _lock = new object();
        private readonly Dictionary<int, string?> _cache = new Dictionary<int, string?>();
        private byte[]? _assemblyBytes;

        public MethodBodyHashProvider(string assemblyPath)
        {
            _assemblyPath = assemblyPath;
        }

        public string? ComputeMethodBodySha256(int relativeVirtualAddress)
        {
            if (relativeVirtualAddress == 0)
            {
                return null;
            }

            lock (_lock)
            {
                if (_cache.TryGetValue(relativeVirtualAddress, out var cached))
                {
                    return cached;
                }

                var hash = ComputeMethodBodySha256Core(relativeVirtualAddress);
                _cache[relativeVirtualAddress] = hash;
                return hash;
            }
        }

        private string? ComputeMethodBodySha256Core(int relativeVirtualAddress)
        {
            if (string.IsNullOrWhiteSpace(_assemblyPath) ||
                !File.Exists(_assemblyPath))
            {
                return null;
            }

            try
            {
                _assemblyBytes ??= File.ReadAllBytes(_assemblyPath);
                using (var stream = new MemoryStream(_assemblyBytes, writable: false))
                using (var peReader = new PEReader(stream))
                {
                    if (!peReader.HasMetadata)
                    {
                        return null;
                    }

                    var body = peReader.GetMethodBody(relativeVirtualAddress);
                    var il = body.GetILBytes();
                    if (il == null)
                    {
                        return null;
                    }

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
            if (!peReader.HasMetadata)
            {
                return null;
            }

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
            new Lazy<ImmutableArray<string>>(CreateTrustedPlatformAssemblyPaths, LazyThreadSafetyMode.ExecutionAndPublication);

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
            if (string.IsNullOrWhiteSpace(coreAssemblyLocation))
            {
                return ImmutableArray<string>.Empty;
            }

            var runtimeDirectory = Path.GetDirectoryName(coreAssemblyLocation);
            if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
            {
                return ImmutableArray<string>.Empty;
            }

            return Directory
                .EnumerateFiles(runtimeDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .ToImmutableArray();
        }
    }
}
