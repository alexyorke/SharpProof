using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PurelySharp.Analyzer
{
    internal sealed class ExceptionSummaryCatalog
    {
        private const string SummaryFileName = "PurelySharp.EffectSummary.json";
        private static readonly SymbolDisplayFormat EffectSummaryContainingTypeFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        private static readonly SymbolDisplayFormat EffectSummaryParameterTypeFormat = new SymbolDisplayFormat(
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public static readonly ExceptionSummaryCatalog Empty = new ExceptionSummaryCatalog(
            ImmutableDictionary<string, ImmutableArray<SummaryEntry>>.Empty);

        private static readonly ConcurrentDictionary<string, ActualAssemblyIdentity?> AssemblyIdentityCache =
            new ConcurrentDictionary<string, ActualAssemblyIdentity?>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ImmutableDictionary<string, ActualMethodIdentity>> MethodIdentityCache =
            new ConcurrentDictionary<string, ImmutableDictionary<string, ActualMethodIdentity>>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string?> RuntimeImplementationAssemblyPathCache =
            new ConcurrentDictionary<string, string?>(StringComparer.Ordinal);

        private readonly ImmutableDictionary<string, ImmutableArray<SummaryEntry>> _entriesBySymbol;

        private ExceptionSummaryCatalog(ImmutableDictionary<string, ImmutableArray<SummaryEntry>> entriesBySymbol)
        {
            _entriesBySymbol = entriesBySymbol;
        }

        public static ExceptionSummaryCatalog FromOptions(AnalyzerOptions options, CancellationToken cancellationToken)
        {
            var entriesBySymbol = new Dictionary<string, ImmutableArray<SummaryEntry>.Builder>(StringComparer.Ordinal);
            foreach (var additionalFile in options.AdditionalFiles)
            {
                if (!IsSummaryFile(additionalFile.Path))
                {
                    continue;
                }

                var text = additionalFile.GetText(cancellationToken)?.ToString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                foreach (var entry in ParseEntries(text!))
                {
                    if (!entriesBySymbol.TryGetValue(entry.Symbol, out var builder))
                    {
                        builder = ImmutableArray.CreateBuilder<SummaryEntry>();
                        entriesBySymbol.Add(entry.Symbol, builder);
                    }

                    builder.Add(entry);
                }
            }

            if (entriesBySymbol.Count == 0)
            {
                return Empty;
            }

            return new ExceptionSummaryCatalog(entriesBySymbol.ToImmutableDictionary(
                item => item.Key,
                item => item.Value.ToImmutable(),
                StringComparer.Ordinal));
        }

        public bool TryGetExceptions(IMethodSymbol methodSymbol, out ImmutableArray<string> exceptionTypes)
        {
            return TryGetExceptions(methodSymbol, compilation: null, out exceptionTypes);
        }

        public bool TryGetExceptions(
            IMethodSymbol methodSymbol,
            Compilation? compilation,
            out ImmutableArray<string> exceptionTypes)
        {
            if (!TryGetExceptionInfos(methodSymbol, compilation, out var exceptionInfos))
            {
                exceptionTypes = ImmutableArray<string>.Empty;
                return false;
            }

            exceptionTypes = exceptionInfos
                .Select(info => info.ExceptionType)
                .OrderBy(type => type, StringComparer.Ordinal)
                .ToImmutableArray();
            return exceptionTypes.Length > 0;
        }

        public bool TryGetExceptionInfos(
            IMethodSymbol methodSymbol,
            Compilation? compilation,
            out ImmutableArray<SummaryExceptionInfo> exceptionInfos)
        {
            var matchedExceptionSources = new Dictionary<string, ImmutableSortedSet<string>.Builder>(StringComparer.Ordinal);
            var actualAssemblyIdentity = compilation is null
                ? null
                : TryResolveActualAssemblyIdentity(methodSymbol, compilation);
            var actualMethodIdentity = compilation is null
                ? null
                : TryResolveActualMethodIdentity(methodSymbol, compilation);

            foreach (var key in GetSymbolKeys(methodSymbol))
            {
                if (!_entriesBySymbol.TryGetValue(key, out var entries))
                {
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (!entry.IsTrustedFor(methodSymbol, actualAssemblyIdentity, actualMethodIdentity))
                    {
                        continue;
                    }

                    foreach (var exceptionInfo in entry.ExceptionInfos)
                    {
                        if (!matchedExceptionSources.TryGetValue(exceptionInfo.ExceptionType, out var sources))
                        {
                            sources = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                            matchedExceptionSources.Add(exceptionInfo.ExceptionType, sources);
                        }

                        sources.UnionWith(exceptionInfo.Sources);
                    }
                }
            }

            if (matchedExceptionSources.Count == 0)
            {
                exceptionInfos = ImmutableArray<SummaryExceptionInfo>.Empty;
                return false;
            }

            exceptionInfos = matchedExceptionSources
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new SummaryExceptionInfo(item.Key, item.Value.ToImmutableArray()))
                .ToImmutableArray();
            return true;
        }

        private static bool IsSummaryFile(string path)
        {
            var fileName = Path.GetFileName(path);
            return string.Equals(fileName, SummaryFileName, StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith("." + SummaryFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<SummaryEntry> ParseEntries(string json)
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("Assemblies", out var assembliesElement) ||
                assembliesElement.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var assemblyElement in assembliesElement.EnumerateArray())
            {
                var assemblyIdentity = SummaryAssemblyIdentity.FromJson(assemblyElement);
                if (!assemblyElement.TryGetProperty("Methods", out var methodsElement) ||
                    methodsElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var methodElement in methodsElement.EnumerateArray())
                {
                    var symbol = CompatibilityHelpers.GetTrimmedStringProperty(methodElement, "Symbol");
                    if (symbol == null)
                    {
                        continue;
                    }

                    var exceptionTypes = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                    var exceptionSources = new Dictionary<string, ImmutableSortedSet<string>.Builder>(StringComparer.Ordinal);
                    AddExceptionTypes(exceptionTypes, methodElement, "ThrownExceptionTypes");
                    AddExceptionTypes(exceptionTypes, methodElement, "TransitiveThrownExceptionTypes");
                    AddExceptionSources(exceptionTypes, exceptionSources, methodElement, "ThrownExceptionSourcePaths");
                    AddExceptionSources(exceptionTypes, exceptionSources, methodElement, "TransitiveThrownExceptionSourcePaths");
                    if (exceptionTypes.Count == 0)
                    {
                        continue;
                    }

                    var exceptionInfos = exceptionTypes
                        .Select(exceptionType => new SummaryExceptionInfo(
                            exceptionType,
                            exceptionSources.TryGetValue(exceptionType, out var sources)
                                ? sources.ToImmutableArray()
                                : ImmutableArray<string>.Empty))
                        .ToImmutableArray();
                    yield return new SummaryEntry(
                        symbol,
                        exceptionInfos,
                        assemblyIdentity,
                        SummaryMethodIdentity.FromJson(methodElement));
                }
            }
        }

        private static void AddExceptionTypes(
            ImmutableSortedSet<string>.Builder exceptionTypes,
            JsonElement methodElement,
            string propertyName)
        {
            if (!methodElement.TryGetProperty(propertyName, out var valuesElement) ||
                valuesElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var valueElement in valuesElement.EnumerateArray())
            {
                if (valueElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = valueElement.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    exceptionTypes.Add(value.Trim());
                }
            }
        }

        private static void AddExceptionSources(
            ImmutableSortedSet<string>.Builder exceptionTypes,
            Dictionary<string, ImmutableSortedSet<string>.Builder> exceptionSources,
            JsonElement methodElement,
            string propertyName)
        {
            if (!methodElement.TryGetProperty(propertyName, out var valuesElement) ||
                valuesElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var valueElement in valuesElement.EnumerateArray())
            {
                if (valueElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var exceptionType = CompatibilityHelpers.GetTrimmedStringProperty(valueElement, "ExceptionType");
                var sourcePath = CompatibilityHelpers.GetTrimmedStringProperty(valueElement, "SourcePath");
                if (exceptionType == null)
                {
                    continue;
                }

                exceptionTypes.Add(exceptionType);
                if (sourcePath == null)
                {
                    continue;
                }

                if (!exceptionSources.TryGetValue(exceptionType, out var sources))
                {
                    sources = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
                    exceptionSources.Add(exceptionType, sources);
                }

                sources.Add(sourcePath);
            }
        }

        private static IEnumerable<string> GetSymbolKeys(IMethodSymbol methodSymbol)
        {
            return EffectSummarySymbolKeyFactory.GetMethodSymbolKeys(methodSymbol);
        }

        private static void AddSymbolKey(ImmutableHashSet<string>.Builder keys, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                keys.Add(value.Trim());
            }
        }

        private static string CreateEffectSummaryKey(IMethodSymbol methodSymbol)
        {
            var containingTypeName = methodSymbol.ContainingType.ToDisplayString(EffectSummaryContainingTypeFormat);
            var methodName = methodSymbol.MethodKind == MethodKind.Constructor
                ? ".ctor"
                : methodSymbol.Name;
            var parameterList = string.Join(
                ", ",
                methodSymbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(EffectSummaryParameterTypeFormat)));
            return containingTypeName + "." + methodName + "(" + parameterList + ")";
        }

        private static ActualMethodIdentity? TryResolveActualMethodIdentity(
            IMethodSymbol methodSymbol,
            Compilation compilation)
        {
            var implementationPath = TryResolveRuntimeImplementationAssemblyPath(methodSymbol);
            if (!string.IsNullOrWhiteSpace(implementationPath))
            {
                var path = implementationPath!;
                if (File.Exists(path) &&
                    TryResolveMethodIdentityFromPath(methodSymbol, path, out var implementationIdentity))
                {
                    return implementationIdentity;
                }
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            {
                return null;
            }

            foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                if (assemblySymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(assemblySymbol, methodSymbol.ContainingAssembly))
                {
                    continue;
                }

                var referencePath = reference.FilePath;
                if (string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath))
                {
                    return null;
                }

                var path = referencePath!;
                var methodMap = MethodIdentityCache.GetOrAdd(path, static resolvedPath => LoadMethodIdentities(resolvedPath));
                foreach (var key in GetSymbolKeys(methodSymbol))
                {
                    if (methodMap.TryGetValue(key, out var identity))
                    {
                        return identity;
                    }
                }

                return null;
            }

            return null;
        }

        private static bool TryResolveMethodIdentityFromPath(
            IMethodSymbol methodSymbol,
            string assemblyPath,
            out ActualMethodIdentity identity)
        {
            identity = null!;
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            {
                return false;
            }

            var methodMap = MethodIdentityCache.GetOrAdd(assemblyPath, static path => LoadMethodIdentities(path));
            foreach (var key in GetSymbolKeys(methodSymbol))
            {
                if (methodMap.TryGetValue(key, out var foundIdentity))
                {
                    identity = foundIdentity;
                    return true;
                }
            }

            return false;
        }

        private static ImmutableDictionary<string, ActualMethodIdentity> LoadMethodIdentities(string path)
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return ImmutableDictionary<string, ActualMethodIdentity>.Empty;
            }

            var metadataReader = peReader.GetMetadataReader();
            var builder = ImmutableDictionary.CreateBuilder<string, ActualMethodIdentity>(StringComparer.Ordinal);
            foreach (var handle in metadataReader.MethodDefinitions)
            {
                var definition = metadataReader.GetMethodDefinition(handle);
                string? methodBodySha256 = null;
                if (definition.RelativeVirtualAddress != 0)
                {
                    var body = peReader.GetMethodBody(definition.RelativeVirtualAddress);
                    var il = body.GetILBytes();
                    if (il != null)
                    {
                        methodBodySha256 = ComputeSha256(il);
                    }
                }

                var token = "0x" + MetadataTokens.GetToken(handle).ToString("X8");
                var identity = new ActualMethodIdentity(token, methodBodySha256);
                foreach (var key in GetMethodKeys(metadataReader, handle))
                {
                    builder[key] = identity;
                }
            }

            return builder.ToImmutable();
        }

        private static IEnumerable<string> GetMethodKeys(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var raw = GetMethodSymbol(reader, handle);
            yield return raw;

            var effectSummaryKey = GetEffectSummaryLikeMethodSymbol(reader, handle);
            if (!string.Equals(effectSummaryKey, raw, StringComparison.Ordinal))
            {
                yield return effectSummaryKey;
            }

            var positionalEffectSummaryKey = GetPositionalEffectSummaryLikeMethodSymbol(reader, handle);
            if (!string.Equals(positionalEffectSummaryKey, raw, StringComparison.Ordinal) &&
                !string.Equals(positionalEffectSummaryKey, effectSummaryKey, StringComparison.Ordinal))
            {
                yield return positionalEffectSummaryKey;
            }

            var exactKey = GetExactMethodKey(reader, handle);
            if (!string.Equals(exactKey, raw, StringComparison.Ordinal) &&
                !string.Equals(exactKey, effectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(exactKey, positionalEffectSummaryKey, StringComparison.Ordinal))
            {
                yield return exactKey;
            }

            var positionalExactKey = GetPositionalExactMethodKey(reader, handle);
            if (!string.Equals(positionalExactKey, raw, StringComparison.Ordinal) &&
                !string.Equals(positionalExactKey, effectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(positionalExactKey, positionalEffectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(positionalExactKey, exactKey, StringComparison.Ordinal))
            {
                yield return positionalExactKey;
            }

            var roslynDisplay = GetRoslynLikeMethodSymbol(reader, handle);
            if (!string.Equals(roslynDisplay, raw, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, effectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, positionalEffectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, exactKey, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, positionalExactKey, StringComparison.Ordinal))
            {
                yield return roslynDisplay;
            }
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            return CompatibilityHelpers.ToLowerHex(sha256.ComputeHash(bytes));
        }

        private static string GetMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = GetTypeName(reader, definition.GetDeclaringType());
            var methodName = reader.GetString(definition.Name);
            var signature = DecodeMethodSignature(reader, definition);
            return typeName + "." + methodName + signature;
        }

        private static string GetTypeName(MetadataReader reader, TypeDefinitionHandle handle)
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

        private static string GetTypeReferenceName(MetadataReader reader, TypeReferenceHandle handle)
        {
            var reference = reader.GetTypeReference(handle);
            var name = reader.GetString(reference.Name);
            var ns = reader.GetString(reference.Namespace);
            return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
        }

        private static string DecodeMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), CreateGenericContext(reader, definition));
                return "(" + string.Join(", ", signature.ParameterTypes) + ")";
            }
            catch (BadImageFormatException)
            {
                return "(?)";
            }
        }

        private static string DecodePositionalMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), genericContext: null);
                return "(" + string.Join(", ", signature.ParameterTypes) + ")";
            }
            catch (BadImageFormatException)
            {
                return "(?)";
            }
        }

        private static string DecodeExactMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), CreateGenericContext(reader, definition));
                return "(" + string.Join(", ", signature.ParameterTypes) + ")->" + signature.ReturnType;
            }
            catch (BadImageFormatException)
            {
                return "(?)->?";
            }
        }

        private static string DecodePositionalExactMethodSignature(MetadataReader reader, MethodDefinition definition)
        {
            try
            {
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), genericContext: null);
                return "(" + string.Join(", ", signature.ParameterTypes) + ")->" + signature.ReturnType;
            }
            catch (BadImageFormatException)
            {
                return "(?)->?";
            }
        }

        private static string GetEffectSummaryLikeMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = GetTypeName(reader, definition.GetDeclaringType());
            return typeName + "." + reader.GetString(definition.Name) + DecodeMethodSignature(reader, definition);
        }

        private static string GetPositionalEffectSummaryLikeMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = GetTypeName(reader, definition.GetDeclaringType());
            return typeName + "." + reader.GetString(definition.Name) + DecodePositionalMethodSignature(reader, definition);
        }

        private static string GetExactMethodKey(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
            return typeName + "." + reader.GetString(definition.Name) + DecodeExactMethodSignature(reader, definition);
        }

        private static string GetPositionalExactMethodKey(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
            return typeName + "." + reader.GetString(definition.Name) + DecodePositionalExactMethodSignature(reader, definition);
        }

        private static string NormalizeExactTypeName(string typeName)
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

        private static string GetRoslynLikeMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = GetTypeName(reader, definition.GetDeclaringType());
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

            return typeName + "." + methodName + DecodeMethodSignature(reader, definition);
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

        private sealed class EffectSummaryTypeNameProvider : ISignatureTypeProvider<string, object?>
        {
            private readonly MetadataReader _reader;

            public EffectSummaryTypeNameProvider(MetadataReader reader)
            {
                _reader = reader;
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
                => GetTypeName(metadataReader, handle);

            public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
                => GetTypeReferenceName(metadataReader, handle);

            public string GetTypeFromSpecification(
                MetadataReader metadataReader,
                object? genericContext,
                TypeSpecificationHandle handle,
                byte rawTypeKind)
            {
                return metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
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

        private static ActualAssemblyIdentity? TryResolveActualAssemblyIdentity(
            IMethodSymbol methodSymbol,
            Compilation compilation)
        {
            var implementationPath = TryResolveRuntimeImplementationAssemblyPath(methodSymbol);
            if (!string.IsNullOrWhiteSpace(implementationPath))
            {
                var path = implementationPath!;
                if (File.Exists(path) &&
                    TryResolveMethodIdentityFromPath(methodSymbol, path, out _))
                {
                    return AssemblyIdentityCache.GetOrAdd(path, static resolvedPath => ActualAssemblyIdentity.FromFile(resolvedPath));
                }
            }

            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            {
                return null;
            }

            foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
            {
                var assemblySymbol = compilation.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol;
                if (assemblySymbol == null ||
                    !SymbolEqualityComparer.Default.Equals(assemblySymbol, methodSymbol.ContainingAssembly))
                {
                    continue;
                }

                var referencePath = reference.FilePath;
                if (string.IsNullOrWhiteSpace(referencePath) || !File.Exists(referencePath))
                {
                    return null;
                }

                var path = referencePath!;
                return AssemblyIdentityCache.GetOrAdd(path, static resolvedPath => ActualAssemblyIdentity.FromFile(resolvedPath));
            }

            return null;
        }

        private static string? TryResolveRuntimeImplementationAssemblyPath(IMethodSymbol methodSymbol)
        {
            var cacheKey = CreateEffectSummaryKey(methodSymbol.OriginalDefinition);
            return RuntimeImplementationAssemblyPathCache.GetOrAdd(cacheKey, _ => ResolveRuntimeImplementationAssemblyPath(methodSymbol));
        }

        private static string? ResolveRuntimeImplementationAssemblyPath(IMethodSymbol methodSymbol)
        {
            var coreLibPath = typeof(object).Assembly.Location;
            if (!string.IsNullOrWhiteSpace(coreLibPath) &&
                File.Exists(coreLibPath) &&
                TryResolveMethodIdentityFromPath(methodSymbol, coreLibPath, out _))
            {
                return coreLibPath;
            }

            var assemblyName = methodSymbol.ContainingAssembly?.Identity.Name;
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
                        TryResolveMethodIdentityFromPath(methodSymbol, location, out _))
                    {
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

                if (TryResolveMethodIdentityFromPath(methodSymbol, location, out _))
                {
                    return location;
                }
            }

            foreach (var trustedPlatformAssemblyPath in GetTrustedPlatformAssemblyPaths())
            {
                if (string.Equals(trustedPlatformAssemblyPath, coreLibPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryResolveMethodIdentityFromPath(methodSymbol, trustedPlatformAssemblyPath, out _))
                {
                    return trustedPlatformAssemblyPath;
                }
            }

            return null;
        }

        private static IEnumerable<string> GetTrustedPlatformAssemblyPaths()
        {
            var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return trustedPlatformAssemblies
                    .Split(Path.PathSeparator)
                    .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            }

            var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
            if (string.IsNullOrWhiteSpace(runtimeDirectory) || !Directory.Exists(runtimeDirectory))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(runtimeDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        }

        private sealed class SummaryEntry
        {
            public SummaryEntry(
                string symbol,
                ImmutableArray<SummaryExceptionInfo> exceptionInfos,
                SummaryAssemblyIdentity? assemblyIdentity,
                SummaryMethodIdentity? methodIdentity)
            {
                Symbol = symbol;
                ExceptionInfos = exceptionInfos;
                AssemblyIdentity = assemblyIdentity;
                MethodIdentity = methodIdentity;
            }

            public string Symbol { get; }

            public ImmutableArray<SummaryExceptionInfo> ExceptionInfos { get; }

            public SummaryAssemblyIdentity? AssemblyIdentity { get; }

            public SummaryMethodIdentity? MethodIdentity { get; }

            public bool IsTrustedFor(
                IMethodSymbol methodSymbol,
                ActualAssemblyIdentity? actualAssemblyIdentity,
                ActualMethodIdentity? actualMethodIdentity)
            {
                if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
                {
                    return false;
                }

                return AssemblyIdentity != null &&
                    AssemblyIdentity.IsComplete &&
                    MethodIdentity != null &&
                    MethodIdentity.IsCompleteEnoughFor(actualMethodIdentity) &&
                    actualAssemblyIdentity != null &&
                    actualMethodIdentity != null &&
                    AssemblyIdentity.Matches(actualAssemblyIdentity) &&
                    MethodIdentity.Matches(actualMethodIdentity);
            }
        }

        internal sealed class SummaryExceptionInfo
        {
            public SummaryExceptionInfo(string exceptionType, ImmutableArray<string> sources)
            {
                ExceptionType = exceptionType;
                Sources = sources;
            }

            public string ExceptionType { get; }

            public ImmutableArray<string> Sources { get; }
        }

        private sealed class SummaryAssemblyIdentity
        {
            public SummaryAssemblyIdentity(
                string? assemblyName,
                string? assemblySha256,
                string? moduleVersionId)
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

        private sealed class SummaryMethodIdentity
        {
            public SummaryMethodIdentity(string? metadataToken, string? methodBodySha256)
            {
                MetadataToken = metadataToken;
                MethodBodySha256 = methodBodySha256;
            }

            public string? MetadataToken { get; }

            public string? MethodBodySha256 { get; }

            public bool IsCompleteEnoughFor(ActualMethodIdentity? actualMethodIdentity)
            {
                if (actualMethodIdentity == null || string.IsNullOrWhiteSpace(MetadataToken))
                {
                    return false;
                }

                if (actualMethodIdentity.MethodBodySha256 == null)
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

        private sealed class ActualMethodIdentity
        {
            public ActualMethodIdentity(string metadataToken, string? methodBodySha256)
            {
                MetadataToken = metadataToken;
                MethodBodySha256 = methodBodySha256;
            }

            public string MetadataToken { get; }

            public string? MethodBodySha256 { get; }
        }

        private sealed class ActualAssemblyIdentity
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
    }
}
