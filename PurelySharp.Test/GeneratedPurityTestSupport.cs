using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace PurelySharp.Test
{
    internal static class GeneratedPurityTestSupport
    {
        public static AnalyzerOptions CreateAnalyzerOptions(
            ImmutableArray<AdditionalText> additionalFiles,
            ImmutableDictionary<string, string>? globalOptions = null)
        {
            var analyzerGlobalOptions = globalOptions ?? ImmutableDictionary<string, string>.Empty;
            if (!additionalFiles.IsDefaultOrEmpty &&
                !analyzerGlobalOptions.ContainsKey("purelysharp_enable_effect_summary_json"))
            {
                analyzerGlobalOptions = analyzerGlobalOptions.Add(
                    "purelysharp_enable_effect_summary_json",
                    "true");
            }

            return new AnalyzerOptions(
                additionalFiles,
                new TestAnalyzerConfigOptionsProvider(analyzerGlobalOptions));
        }

        public static ImmutableArray<AdditionalText> CreateSyntheticGeneratedPurityAdditionalFiles(
            params (string AssemblyPath, string FileName, string ActualMethodLookupSymbol, string DisplaySymbol, string Classification, string CategoriesJson)[] entries)
        {
            return entries
                .Select(entry => (AdditionalText)new AnalyzerTestHost.InMemoryAdditionalText(
                    entry.FileName,
                    CreatePuritySummaryJson(
                        entry.AssemblyPath,
                        entry.ActualMethodLookupSymbol,
                        entry.Classification,
                        entry.CategoriesJson,
                        entry.DisplaySymbol)))
                .ToImmutableArray();
        }

        public static string CreatePuritySummaryJson(
            string assemblyPath,
            string actualMethodLookupSymbol,
            string classification,
            string categoriesJson,
            string? symbolOverride = null)
        {
            var assemblyIdentity = GetAssemblyIdentity(assemblyPath);
            var methodIdentity = GetMethodIdentity(assemblyPath, actualMethodLookupSymbol);
            var symbol = symbolOverride ?? actualMethodLookupSymbol;

            return $$"""
{
  "SchemaVersion": 2,
  "GeneratedPurityCatalog": {
    "SchemaVersion": 1,
    "Entries": [
      {
        "Symbol": "{{symbol}}",
        "ExactSymbolKey": "{{methodIdentity.ExactSymbolKey}}",
        "CacheKey": "generated-purity-test",
        "AssemblyName": "{{assemblyIdentity.AssemblyName}}",
        "AssemblyPath": "{{assemblyPath.Replace("\\", "\\\\")}}",
        "AssemblySha256": "{{assemblyIdentity.AssemblySha256}}",
        "ModuleVersionId": "{{assemblyIdentity.ModuleVersionId}}",
        "MetadataToken": "{{methodIdentity.MetadataToken}}",
        "MethodBodySha256": {{FormatJsonStringOrNull(methodIdentity.MethodBodySha256)}},
        "Classification": "{{classification}}",
        "Categories": {{categoriesJson}},
        "FirstBlockingCallChain": [],
        "HasFreshArrayAllocationEvidence": false,
        "HasFreshObjectAllocationEvidence": false,
        "HasUnsupportedEffects": false,
        "FreshnessClassification": "none"
      }
    ]
  },
  "Assemblies": [
    {
      "AssemblyName": "{{assemblyIdentity.AssemblyName}}",
      "AssemblyPath": "{{assemblyPath.Replace("\\", "\\\\")}}",
      "AssemblySha256": "{{assemblyIdentity.AssemblySha256}}",
      "ModuleVersionId": "{{assemblyIdentity.ModuleVersionId}}",
      "MethodCount": 1,
      "EmittedMethodCount": 1,
      "Methods": [
        {
          "Symbol": "{{symbol}}",
          "ExactSymbolKey": "{{methodIdentity.ExactSymbolKey}}",
          "MetadataToken": "{{methodIdentity.MetadataToken}}",
          "RelativeVirtualAddress": 0,
          "MethodBodySha256": {{FormatJsonStringOrNull(methodIdentity.MethodBodySha256)}},
          "CacheKey": "generated-purity-test",
          "Effects": [],
          "RootCandidates": [],
          "TransitiveRootCandidates": [],
          "ThrownExceptionTypes": [],
          "TransitiveThrownExceptionTypes": [],
          "Calls": [],
          "Fields": [],
          "PurityClassification": {
            "Classification": "{{classification}}",
            "Categories": {{categoriesJson}},
            "FirstBlockingCallChain": [],
            "HasFreshArrayAllocationEvidence": false,
            "HasFreshObjectAllocationEvidence": false,
            "HasUnsupportedEffects": false,
            "FreshnessClassification": "none"
          }
        }
      ]
    }
  ]
}
""";
        }

        private static MethodIdentity GetMethodIdentity(string assemblyPath, string symbol)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();

            foreach (var handle in metadataReader.MethodDefinitions)
            {
                var methodSymbol = GetMethodSymbol(metadataReader, handle);
                if (!string.Equals(methodSymbol, symbol, StringComparison.Ordinal))
                {
                    continue;
                }

                var definition = metadataReader.GetMethodDefinition(handle);
                string? methodBodySha256 = null;
                if (definition.RelativeVirtualAddress != 0)
                {
                    var body = peReader.GetMethodBody(definition.RelativeVirtualAddress);
                    var il = body.GetILBytes();
                    if (il != null)
                    {
                        using var sha256 = SHA256.Create();
                        methodBodySha256 = Convert.ToHexString(sha256.ComputeHash(il)).ToLowerInvariant();
                    }
                }

                return new MethodIdentity(
                    $"0x{MetadataTokens.GetToken(handle):X8}",
                    methodBodySha256,
                    GetMethodExactSymbolKey(metadataReader, handle));
            }

            throw new InvalidOperationException("Method symbol did not resolve in assembly: " + symbol);
        }

        private static AssemblyIdentity GetAssemblyIdentity(string assemblyPath)
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();
            var assemblyName = metadataReader.IsAssembly
                ? metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name)
                : Path.GetFileNameWithoutExtension(assemblyPath);
            var moduleVersionId = metadataReader.GetGuid(metadataReader.GetModuleDefinition().Mvid).ToString("D");
            stream.Position = 0;
            using var sha256 = SHA256.Create();
            var assemblySha256 = Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
            return new AssemblyIdentity(assemblyName, assemblySha256, moduleVersionId);
        }

        private static string GetMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = GetTypeName(reader, definition.GetDeclaringType());
            var methodName = reader.GetString(definition.Name);
            var signature = DecodeMethodSignature(reader, definition);
            return typeName + "." + methodName + signature;
        }

        private static string GetMethodExactSymbolKey(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
            var methodName = reader.GetString(definition.Name);
            var signature = DecodeExactMethodSignature(reader, definition);
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

        private static string DecodeMethodSignature(MetadataReader reader, MethodDefinition definition)
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
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), genericContext: null);
                return "(" +
                    string.Join(", ", signature.ParameterTypes.Select(NormalizeExactSignatureTypeName)) +
                    ")->" +
                    NormalizeExactSignatureTypeName(signature.ReturnType);
            }
            catch (BadImageFormatException)
            {
                return "(?)->?";
            }
        }

        private static string NormalizeExactSignatureTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return typeName;
            }

            if (typeName.EndsWith("[]", StringComparison.Ordinal))
            {
                return NormalizeExactSignatureTypeName(typeName[..^2]) + "[]";
            }

            if (typeName.EndsWith("*", StringComparison.Ordinal))
            {
                return NormalizeExactSignatureTypeName(typeName[..^1]) + "*";
            }

            if (typeName.StartsWith("ref readonly ", StringComparison.Ordinal))
            {
                return "ref readonly " + NormalizeExactSignatureTypeName(typeName["ref readonly ".Length..]);
            }

            if (typeName.StartsWith("ref ", StringComparison.Ordinal))
            {
                return "ref " + NormalizeExactSignatureTypeName(typeName["ref ".Length..]);
            }

            var genericStart = typeName.IndexOf('<');
            if (genericStart >= 0 && typeName.EndsWith(">", StringComparison.Ordinal))
            {
                var genericType = typeName[..genericStart];
                var genericArguments = SplitTopLevelArguments(typeName[(genericStart + 1)..^1])
                    .Select(NormalizeExactSignatureTypeName);
                return NormalizeExactTypeName(genericType) + "<" + string.Join(", ", genericArguments) + ">";
            }

            return NormalizeExactTypeName(typeName);
        }

        private static ImmutableArray<string> SplitTopLevelArguments(string arguments)
        {
            if (string.IsNullOrEmpty(arguments))
            {
                return ImmutableArray<string>.Empty;
            }

            var builder = ImmutableArray.CreateBuilder<string>();
            var depth = 0;
            var start = 0;
            for (var index = 0; index < arguments.Length; index++)
            {
                switch (arguments[index])
                {
                    case '<':
                        depth++;
                        break;
                    case '>':
                        depth--;
                        break;
                    case ',' when depth == 0:
                        builder.Add(arguments[start..index].Trim());
                        start = index + 1;
                        break;
                }
            }

            builder.Add(arguments[start..].Trim());
            return builder.ToImmutable();
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

        private static string FormatJsonStringOrNull(string? value)
        {
            return value == null ? "null" : "\"" + value + "\"";
        }

        private sealed record AssemblyIdentity(string AssemblyName, string AssemblySha256, string ModuleVersionId);
        private sealed record MethodIdentity(string MetadataToken, string? MethodBodySha256, string ExactSymbolKey);

        private sealed class EffectSummaryTypeNameProvider : ISignatureTypeProvider<string, object?>
        {
            private readonly MetadataReader _reader;

            public EffectSummaryTypeNameProvider(MetadataReader reader)
            {
                _reader = reader;
            }

            public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
            public string GetByReferenceType(string elementType) => "ref " + elementType;
            public string GetFunctionPointerType(MethodSignature<string> signature) => "methodptr";
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
            public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
            public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired) => unmodifiedType;
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
                _ => typeCode.ToString()
            };
            public string GetSZArrayType(string elementType) => elementType + "[]";
            public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
                => GetTypeName(reader, handle);
            public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            {
                var reference = reader.GetTypeReference(handle);
                var name = reader.GetString(reference.Name);
                var ns = reader.GetString(reference.Namespace);
                return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            }
            public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }

        private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private readonly AnalyzerConfigOptions _globalOptions;
            private readonly AnalyzerConfigOptions _emptyOptions = new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

            public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
            {
                _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
            }

            public override AnalyzerConfigOptions GlobalOptions => _globalOptions;
            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _emptyOptions;
            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _emptyOptions;
        }

        private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            private readonly ImmutableDictionary<string, string> _values;

            public TestAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
            {
                _values = values;
            }

            public override bool TryGetValue(string key, out string value)
            {
                if (_values.TryGetValue(key, out var found))
                {
                    value = found;
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
