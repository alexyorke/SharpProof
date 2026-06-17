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
    internal sealed class GeneratedPurityCatalog
    {
        private const string SummaryFileName = "PurelySharp.EffectSummary.json";
        private static readonly AsyncLocal<GeneratedPurityCatalog?> CurrentCatalog = new AsyncLocal<GeneratedPurityCatalog?>();
        private static readonly SymbolDisplayFormat EffectSummaryContainingTypeFormat = new SymbolDisplayFormat(
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);
        private static readonly SymbolDisplayFormat EffectSummaryParameterTypeFormat = new SymbolDisplayFormat(
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        public static readonly GeneratedPurityCatalog Empty = new GeneratedPurityCatalog(
            ImmutableDictionary<string, ImmutableArray<SummaryEntry>>.Empty);

        private static readonly ConcurrentDictionary<string, ActualAssemblyIdentity?> AssemblyIdentityCache =
            new ConcurrentDictionary<string, ActualAssemblyIdentity?>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ImmutableDictionary<string, ActualMethodIdentity>> MethodIdentityCache =
            new ConcurrentDictionary<string, ImmutableDictionary<string, ActualMethodIdentity>>(StringComparer.OrdinalIgnoreCase);

        private readonly ImmutableDictionary<string, ImmutableArray<SummaryEntry>> _entriesBySymbol;

        private GeneratedPurityCatalog(ImmutableDictionary<string, ImmutableArray<SummaryEntry>> entriesBySymbol)
        {
            _entriesBySymbol = entriesBySymbol;
        }

        public static GeneratedPurityCatalog Current => CurrentCatalog.Value ?? Empty;

        public static GeneratedPurityCatalog FromOptions(AnalyzerOptions options, CancellationToken cancellationToken)
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

            var builtInSummaryDirectory = Path.GetDirectoryName(typeof(GeneratedPurityCatalog).Assembly.Location);
            if (!string.IsNullOrWhiteSpace(builtInSummaryDirectory))
            {
                var builtInSummaryPath = Path.Combine(builtInSummaryDirectory, SummaryFileName);
                if (File.Exists(builtInSummaryPath))
                {
                    foreach (var entry in ParseEntries(File.ReadAllText(builtInSummaryPath)))
                    {
                        if (!entriesBySymbol.TryGetValue(entry.Symbol, out var builder))
                        {
                            builder = ImmutableArray.CreateBuilder<SummaryEntry>();
                            entriesBySymbol.Add(entry.Symbol, builder);
                        }

                        builder.Add(entry);
                    }
                }
            }

            if (entriesBySymbol.Count == 0)
            {
                return Empty;
            }

            return new GeneratedPurityCatalog(entriesBySymbol.ToImmutableDictionary(
                item => item.Key,
                item => item.Value.ToImmutable(),
                StringComparer.Ordinal));
        }

        public static IDisposable UseCurrent(GeneratedPurityCatalog catalog)
        {
            return new Scope(CurrentCatalog.Value, catalog);
        }

        public bool TryGetPurity(IMethodSymbol methodSymbol, Compilation compilation, out PurityEntry classification)
        {
            classification = default;
            if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata != true)
            {
                return false;
            }

            var actualAssemblyIdentity = TryResolveActualAssemblyIdentity(methodSymbol, compilation);
            var actualMethodIdentity = TryResolveActualMethodIdentity(methodSymbol, compilation);
            if (actualAssemblyIdentity == null || actualMethodIdentity == null)
            {
                return false;
            }

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

                    classification = entry.Classification;
                    return true;
                }
            }

            return false;
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
            if (document.RootElement.TryGetProperty("GeneratedPurityCatalog", out var generatedCatalogElement) &&
                generatedCatalogElement.ValueKind == JsonValueKind.Object &&
                generatedCatalogElement.TryGetProperty("Entries", out var entriesElement) &&
                entriesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entryElement in entriesElement.EnumerateArray())
                {
                    var symbol = CompatibilityHelpers.GetTrimmedStringProperty(entryElement, "ExactSymbolKey") ??
                        CompatibilityHelpers.GetTrimmedStringProperty(entryElement, "Symbol");
                    var classification = CompatibilityHelpers.GetTrimmedStringProperty(entryElement, "Classification");
                    if (string.IsNullOrWhiteSpace(symbol) || string.IsNullOrWhiteSpace(classification))
                    {
                        continue;
                    }

                    var categories = ReadStringArray(entryElement, "Categories");
                    var primaryCategory = CompatibilityHelpers.GetTrimmedStringProperty(entryElement, "PrimaryCategory");
                    yield return new SummaryEntry(
                        symbol.Trim(),
                        new PurityEntry(
                            classification.Trim(),
                            categories,
                            string.IsNullOrWhiteSpace(primaryCategory)
                                ? categories.FirstOrDefault() ?? "generated_purity_summary"
                                : primaryCategory.Trim()),
                        SummaryAssemblyIdentity.FromFlatJson(entryElement),
                        SummaryMethodIdentity.FromFlatJson(entryElement));
                }

                yield break;
            }

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
                    if (symbol == null ||
                        !methodElement.TryGetProperty("PurityClassification", out var purityElement) ||
                        purityElement.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var classification = CompatibilityHelpers.GetTrimmedStringProperty(purityElement, "Classification");
                    if (string.IsNullOrWhiteSpace(classification) ||
                        string.Equals(classification, "conservative_unknown", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var categories = ReadStringArray(purityElement, "Categories");
                    yield return new SummaryEntry(
                        symbol,
                        new PurityEntry(
                            classification.Trim(),
                            categories,
                            categories.FirstOrDefault() ?? "generated_purity_summary"),
                        assemblyIdentity,
                        SummaryMethodIdentity.FromJson(methodElement));
                }
            }
        }

        private static ImmutableArray<string> ReadStringArray(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var valuesElement) ||
                valuesElement.ValueKind != JsonValueKind.Array)
            {
                return ImmutableArray<string>.Empty;
            }

            var builder = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
            foreach (var valueElement in valuesElement.EnumerateArray())
            {
                if (valueElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var value = valueElement.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    builder.Add(value.Trim());
                }
            }

            return builder.ToImmutableArray();
        }

        private static IEnumerable<string> GetSymbolKeys(IMethodSymbol methodSymbol)
        {
            var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
            AddSymbolKey(keys, methodSymbol.OriginalDefinition.ToDisplayString());
            AddSymbolKey(keys, methodSymbol.ToDisplayString());
            AddSymbolKey(keys, CreateEffectSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateEffectSummaryKey(methodSymbol));
            AddSymbolKey(keys, CreateExactSummaryKey(methodSymbol.OriginalDefinition));
            AddSymbolKey(keys, CreateExactSummaryKey(methodSymbol));

            if (methodSymbol.IsGenericMethod)
            {
                AddSymbolKey(keys, methodSymbol.ConstructedFrom.ToDisplayString());
                AddSymbolKey(keys, CreateEffectSummaryKey(methodSymbol.ConstructedFrom));
                AddSymbolKey(keys, CreateExactSummaryKey(methodSymbol.ConstructedFrom));
            }

            return keys;
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

        private static string CreateExactSummaryKey(IMethodSymbol methodSymbol)
        {
            var containingTypeName = methodSymbol.ContainingType.ToDisplayString(EffectSummaryContainingTypeFormat);
            var methodName = methodSymbol.MethodKind == MethodKind.Constructor
                ? ".ctor"
                : methodSymbol.Name;
            var parameterList = string.Join(
                ", ",
                methodSymbol.Parameters.Select(parameter => parameter.Type.ToDisplayString(EffectSummaryParameterTypeFormat)));
            var returnType = methodSymbol.MethodKind == MethodKind.Constructor
                ? "void"
                : methodSymbol.ReturnType.ToDisplayString(EffectSummaryParameterTypeFormat);
            return containingTypeName + "." + methodName + "(" + parameterList + ")->" + returnType;
        }

        private static ActualMethodIdentity? TryResolveActualMethodIdentity(IMethodSymbol methodSymbol, Compilation compilation)
        {
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

                var methodMap = MethodIdentityCache.GetOrAdd(referencePath, static path => LoadMethodIdentities(path));
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

            var exactKey = GetExactMethodKey(reader, handle);
            if (!string.Equals(exactKey, raw, StringComparison.Ordinal) &&
                !string.Equals(exactKey, effectSummaryKey, StringComparison.Ordinal))
            {
                yield return exactKey;
            }

            var roslynDisplay = GetRoslynLikeMethodSymbol(reader, handle);
            if (!string.Equals(roslynDisplay, raw, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, effectSummaryKey, StringComparison.Ordinal) &&
                !string.Equals(roslynDisplay, exactKey, StringComparison.Ordinal))
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

        private static string GetEffectSummaryLikeMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = GetTypeName(reader, definition.GetDeclaringType());
            return typeName + "." + reader.GetString(definition.Name) + DecodeMethodSignature(reader, definition);
        }

        private static string GetExactMethodKey(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
            return typeName + "." + reader.GetString(definition.Name) + DecodeExactMethodSignature(reader, definition);
        }

        private static string NormalizeExactTypeName(string typeName)
        {
            return typeName switch
            {
                "System.Boolean" => "bool",
                "System.Byte" => "byte",
                "System.Char" => "char",
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
            public EffectSummaryTypeNameProvider(MetadataReader reader)
            {
            }

            public string GetArrayType(string elementType, ArrayShape shape)
            {
                var rank = Math.Max(shape.Rank, 1);
                return elementType + "[" + new string(',', rank - 1) + "]";
            }

            public string GetByReferenceType(string elementType) => "ref " + elementType;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "delegate*";
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
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
            public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
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
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind) => GetTypeName(metadataReader, handle);
            public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind) => GetTypeReferenceName(metadataReader, handle);
            public string GetTypeFromSpecification(MetadataReader metadataReader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
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

        private static ActualAssemblyIdentity? TryResolveActualAssemblyIdentity(IMethodSymbol methodSymbol, Compilation compilation)
        {
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

                return AssemblyIdentityCache.GetOrAdd(referencePath, static path => ActualAssemblyIdentity.FromFile(path));
            }

            return null;
        }

        private sealed class SummaryEntry
        {
            public SummaryEntry(
                string symbol,
                PurityEntry classification,
                SummaryAssemblyIdentity? assemblyIdentity,
                SummaryMethodIdentity? methodIdentity)
            {
                Symbol = symbol;
                Classification = classification;
                AssemblyIdentity = assemblyIdentity;
                MethodIdentity = methodIdentity;
            }

            public string Symbol { get; }
            public PurityEntry Classification { get; }
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

        internal readonly struct PurityEntry
        {
            public PurityEntry(string classification, ImmutableArray<string> categories, string primaryCategory)
            {
                Classification = classification;
                Categories = categories;
                PrimaryCategory = primaryCategory;
            }

            public string Classification { get; }
            public ImmutableArray<string> Categories { get; }
            public string PrimaryCategory { get; }
            public bool IsPure => string.Equals(Classification, "pure", StringComparison.Ordinal);
            public bool IsImpure => string.Equals(Classification, "impure", StringComparison.Ordinal);
        }

        private sealed class SummaryAssemblyIdentity
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

            public static SummaryAssemblyIdentity? FromFlatJson(JsonElement entryElement)
            {
                return FromJson(entryElement);
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

            public static SummaryMethodIdentity? FromFlatJson(JsonElement entryElement)
            {
                return FromJson(entryElement);
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

        private sealed class Scope : IDisposable
        {
            private readonly GeneratedPurityCatalog? _previous;

            public Scope(GeneratedPurityCatalog? previous, GeneratedPurityCatalog current)
            {
                _previous = previous;
                CurrentCatalog.Value = current;
            }

            public void Dispose()
            {
                CurrentCatalog.Value = _previous;
            }
        }
    }
}
