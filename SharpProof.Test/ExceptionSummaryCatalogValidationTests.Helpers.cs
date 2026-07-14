using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Identity;

namespace SharpProof.Test;

public partial class ExceptionSummaryCatalogValidationTests
{
    private static string CreateLibraryCallSource()
    {
        return """
               using System;

               public class TestClass
               {
                   public void TestMethod(object value)
                   {
                       ArgumentNullException.ThrowIfNull(value, nameof(value));
                   }
               }
               """;
    }

    private static CSharpCompilation CreateLibraryCallCompilation()
    {
        var syntaxTree =
            CSharpSyntaxTree.ParseText(CreateLibraryCallSource(), new CSharpParseOptions(LanguageVersion.Preview));
        return CSharpCompilation.Create(
            "ExceptionSummaryCatalogValidationTests",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static string CreateEffectSummaryJson(
        AssemblyIdentity assemblyIdentity,
        string symbol,
        string? assemblySha256 = null,
        string? moduleVersionId = null,
        string? metadataToken = null,
        string? methodBodySha256 = null,
        string? actualMethodLookupSymbol = null,
        string? thrownExceptionTypesJson = null,
        string? transitiveThrownExceptionTypesJson = null,
        string? thrownExceptionSourcePathsJson = null,
        string? transitiveThrownExceptionSourcePathsJson = null,
        string? thrownExceptionEdgesJson = null,
        string? transitiveThrownExceptionEdgesJson = null)
    {
        var methodIdentity = GetMethodIdentity(assemblyIdentity.AssemblyPath, actualMethodLookupSymbol ?? symbol);
        thrownExceptionTypesJson ??= "[]";
        transitiveThrownExceptionTypesJson ??= """[ "System.ArgumentNullException" ]""";
        thrownExceptionSourcePathsJson ??= "[]";
        transitiveThrownExceptionSourcePathsJson ??= "[]";
        thrownExceptionEdgesJson ??= "[]";
        transitiveThrownExceptionEdgesJson ??= "[]";
        assemblySha256 ??= assemblyIdentity.AssemblySha256;
        moduleVersionId ??= assemblyIdentity.ModuleVersionId;
        metadataToken ??= methodIdentity.MetadataToken;
        methodBodySha256 ??= methodIdentity.MethodBodySha256;
        var methodBodySha256Json = methodBodySha256 == null ? "null" : "\"" + methodBodySha256 + "\"";
        var thrownProvenanceJson = NormalizeProvenanceJson(
            thrownExceptionSourcePathsJson,
            methodIdentity.Identity);
        var transitiveProvenanceJson = NormalizeProvenanceJson(
            transitiveThrownExceptionSourcePathsJson,
            methodIdentity.Identity);
        var transitiveEdgesV5Json = MergeAndNormalizeEdgeJson(
            thrownExceptionEdgesJson,
            transitiveThrownExceptionEdgesJson,
            methodIdentity.Identity);
        return $$"""
                 {
                   "SchemaVersion": 5,
                   "EvidenceSchemaVersion": 2,
                   "EvidenceSchemaCompatibility": "exact-v2",
                   "Assemblies": [
                     {
                       "AssemblyName": "{{assemblyIdentity.AssemblyName}}",
                       "AssemblyPath": "runtime",
                       "AssemblySha256": "{{assemblySha256}}",
                       "ModuleVersionId": "{{moduleVersionId}}",
                       "MethodCount": 1,
                       "EmittedMethodCount": 1,
                       "Methods": [
                         {
                           "DisplayName": "{{symbol}}",
                           "Identity": {{methodIdentity.IdentityJson}},
                           "CanonicalKey": "{{methodIdentity.CanonicalKey}}",
                           "MetadataToken": "{{metadataToken}}",
                           "RelativeVirtualAddress": 0,
                           "MethodBodySha256": {{methodBodySha256Json}},
                           "CacheKey": "validation-test",
                           "Effects": [],
                           "RootCandidates": [],
                           "TransitiveRootCandidates": [],
                           "ThrownExceptionTypes": {{thrownExceptionTypesJson}},
                           "TransitiveThrownExceptionTypes": {{transitiveThrownExceptionTypesJson}},
                           "ThrownExceptionProvenance": {{thrownProvenanceJson}},
                           "TransitiveThrownExceptionProvenance": {{transitiveProvenanceJson}},
                           "TransitiveThrownExceptionEdges": {{transitiveEdgesV5Json}},
                           "Calls": [],
                           "Fields": []
                         }
                       ]
                     }
                   ]
                 }
                 """;
    }

    private static void AssertMatchingExceptionDiagnostics(
        ImmutableArray<Diagnostic> expectedDiagnostics,
        ImmutableArray<Diagnostic> actualDiagnostics,
        string diagnosticId)
    {
        var expected = expectedDiagnostics.Single(d => d.Id == diagnosticId);
        var actual = actualDiagnostics.Single(d => d.Id == diagnosticId);

        Assert.That(actual.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
            Is.EqualTo(expected.Properties[SharpProofDiagnostics.ExceptionTypesProperty]));
        Assert.That(actual.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo(expected.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty]));
        Assert.That(actual.Properties[SharpProofDiagnostics.ExceptionSourcesProperty],
            Is.EqualTo(expected.Properties[SharpProofDiagnostics.ExceptionSourcesProperty]));

        var expectedHasSymbol =
            expected.Properties.TryGetValue(SharpProofDiagnostics.ExceptionSymbolProperty, out var expectedSymbol);
        var actualHasSymbol =
            actual.Properties.TryGetValue(SharpProofDiagnostics.ExceptionSymbolProperty, out var actualSymbol);
        Assert.That(actualHasSymbol, Is.EqualTo(expectedHasSymbol));
        if (expectedHasSymbol) Assert.That(actualSymbol, Is.EqualTo(expectedSymbol));

        var expectedHasEdges =
            expected.Properties.TryGetValue(SharpProofDiagnostics.ExceptionEdgesProperty, out var expectedEdges);
        var actualHasEdges =
            actual.Properties.TryGetValue(SharpProofDiagnostics.ExceptionEdgesProperty, out var actualEdges);
        Assert.That(actualHasEdges, Is.EqualTo(expectedHasEdges));
        if (expectedHasEdges) Assert.That(actualEdges, Is.EqualTo(expectedEdges));
    }

    private static string CreateMalformedEffectSummaryJson(string assemblyName, string assemblySha256,
        string moduleVersionId)
    {
        return $$"""
                 {
                   "SchemaVersion": 5,
                   "EvidenceSchemaVersion": 2,
                   "EvidenceSchemaCompatibility": "exact-v2",
                   "Assemblies": [
                     {
                       "AssemblyName": "{{assemblyName}}",
                       "AssemblyPath": "runtime",
                       "AssemblySha256": "{{assemblySha256}}",
                       "ModuleVersionId": "{{moduleVersionId}}",
                       "MethodCount": 1,
                       "EmittedMethodCount": 1,
                       "Methods": [
                         {
                           "Symbol": "System.ArgumentNullException.ThrowIfNull(object, string)",
                           "MetadataToken": "0x06000001",
                           "RelativeVirtualAddress": 0,
                           "MethodBodySha256": null,
                           "CacheKey": "validation-test",
                           "Effects": [],
                           "RootCandidates": [],
                           "TransitiveRootCandidates": [],
                           "ThrownExceptionTypes": "System.ArgumentNullException",
                           "TransitiveThrownExceptionTypes": [],
                           "Calls": [],
                           "Fields": []
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
            if (!string.Equals(methodSymbol, symbol, StringComparison.Ordinal)) continue;

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
                EcmaStructuralMethodIdentityAdapter.Create(metadataReader, handle),
                GetMethodExactSymbolKey(metadataReader, handle),
                methodSymbol);
        }

        throw new AssertionException("Method symbol did not resolve in assembly: " + symbol);
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

        return new AssemblyIdentity(assemblyPath, assemblyName, assemblySha256, moduleVersionId);
    }

    private static string GetMethodSymbol(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var definition = reader.GetMethodDefinition(handle);
        var typeName = GeneratedPurityTestSupport.GetTypeName(reader, definition.GetDeclaringType());
        var methodName = reader.GetString(definition.Name);
        var signature = DecodeMethodSignature(reader, definition);
        return typeName + "." + methodName + signature;
    }

    private static string GetMethodExactSymbolKey(MetadataReader reader, MethodDefinitionHandle handle)
    {
        var definition = reader.GetMethodDefinition(handle);
        var typeName =
            GeneratedPurityTestSupport.NormalizeExactTypeName(
                GeneratedPurityTestSupport.GetTypeName(reader, definition.GetDeclaringType()));
        var methodName = reader.GetString(definition.Name);
        var signature = DecodeExactMethodSignature(reader, definition);
        return typeName + "." + methodName + signature;
    }

    private static string NormalizeProvenanceJson(
        string json,
        StructuralMethodIdentity defaultIdentity)
    {
        var array = JsonNode.Parse(json) as JsonArray ?? new JsonArray();
        foreach (var item in array.OfType<JsonObject>())
            item["CallChain"] ??= JsonSerializer.SerializeToNode(new[] { defaultIdentity });
        return array.ToJsonString();
    }

    private static string MergeAndNormalizeEdgeJson(
        string directJson,
        string transitiveJson,
        StructuralMethodIdentity defaultIdentity)
    {
        var result = new JsonArray();
        AddEdges(JsonNode.Parse(directJson) as JsonArray, result, defaultIdentity);
        AddEdges(JsonNode.Parse(transitiveJson) as JsonArray, result, defaultIdentity);
        return result.ToJsonString();
    }

    private static void AddEdges(
        JsonArray? source,
        JsonArray destination,
        StructuralMethodIdentity defaultIdentity)
    {
        if (source == null) return;

        foreach (var sourceItem in source.OfType<JsonObject>())
        {
            var item = (JsonObject)sourceItem.DeepClone();
            item["CallChain"] ??= JsonSerializer.SerializeToNode(new[] { defaultIdentity });
            if (item["CalleeIdentity"] == null &&
                item["CalleeExactSymbolKey"]?.GetValue<string>() is { Length: > 0 } legacyCallee)
            {
                item["CalleeIdentity"] = JsonSerializer.SerializeToNode(
                    new StructuralMethodIdentity(
                        "SharpProof.Test.LegacyProvenance",
                        "ordinary",
                        legacyCallee,
                        0,
                        Array.Empty<StructuralParameterIdentity>(),
                        "named:System.Void",
                        "none"));
            }

            item.Remove("CalleeExactSymbolKey");
            item.Remove("CalleeSymbol");
            destination.Add(item);
        }
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
            var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(), null);
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
            var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(), null);
            return "(" + string.Join(", ", signature.ParameterTypes) + ")->" + signature.ReturnType;
        }
        catch (BadImageFormatException)
        {
            return "(?)->?";
        }
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        (string Path, string Text)[] effectSummaryFiles,
        ImmutableArray<MetadataReference> additionalReferences,
        ImmutableDictionary<string, string>? globalOptions)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "ExceptionSummaryCatalogValidationTests",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences().AddRange(additionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzerGlobalOptions = globalOptions ?? ImmutableDictionary<string, string>.Empty;
        if (!analyzerGlobalOptions.ContainsKey("sharpproof_report_exceptions"))
            analyzerGlobalOptions = analyzerGlobalOptions.Add(
                "sharpproof_report_exceptions",
                "true");

        if (!analyzerGlobalOptions.ContainsKey("sharpproof_checked_exceptions"))
            analyzerGlobalOptions = analyzerGlobalOptions.Add(
                "sharpproof_checked_exceptions",
                "true");

        if (!analyzerGlobalOptions.ContainsKey("sharpproof_attribute_stub_namespaces"))
            analyzerGlobalOptions = analyzerGlobalOptions.Add(
                "sharpproof_attribute_stub_namespaces",
                "<global>");

        var analyzerOptions = new AnalyzerOptions(
            effectSummaryFiles
                .Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text))
                .ToImmutableArray(),
            new TestAnalyzerConfigOptionsProvider(analyzerGlobalOptions));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new SharpProofAnalyzer()),
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                null,
                true,
                false,
                false));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static ImmutableDictionary<string, string> CreateEffectSummaryJsonEnabledGlobalOptions(
        ImmutableDictionary<string, string>? globalOptions = null)
    {
        var analyzerGlobalOptions = globalOptions ?? ImmutableDictionary<string, string>.Empty;
        return analyzerGlobalOptions.SetItem("sharpproof_enable_effect_summary_json", "true");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
        string source,
        string effectSummaryJson,
        ImmutableArray<MetadataReference> additionalReferences,
        string additionalFilePath = "SharpProof.EffectSummary.json")
    {
        return await GetAnalyzerDiagnosticsAsync(
            source,
            new[] { (additionalFilePath, effectSummaryJson) },
            additionalReferences,
            CreateEffectSummaryJsonEnabledGlobalOptions());
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
        string source,
        string effectSummaryJson,
        string additionalFilePath = "SharpProof.EffectSummary.json")
    {
        return await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            source,
            effectSummaryJson,
            ImmutableArray<MetadataReference>.Empty,
            additionalFilePath);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
        string source,
        params (string Path, string Text)[] effectSummaryFiles)
    {
        return await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            source,
            effectSummaryFiles,
            ImmutableArray<MetadataReference>.Empty);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
        string source,
        (string Path, string Text)[] effectSummaryFiles,
        ImmutableArray<MetadataReference> additionalReferences)
    {
        return await GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            source,
            effectSummaryFiles,
            additionalReferences,
            null);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
        string source,
        (string Path, string Text)[] effectSummaryFiles,
        ImmutableArray<MetadataReference> additionalReferences,
        ImmutableDictionary<string, string>? globalOptions)
    {
        return await GetAnalyzerDiagnosticsAsync(
            source,
            effectSummaryFiles,
            additionalReferences,
            CreateEffectSummaryJsonEnabledGlobalOptions(globalOptions));
    }

    private static async Task<FixtureAssembly> CreateFixtureAssemblyAsync(string assemblyName, string source)
    {
        var tempDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            "exception-summary-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var assemblyPath = Path.Combine(tempDirectory, assemblyName + ".dll");

        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        await using var stream = File.Create(assemblyPath);
        var emitResult = compilation.Emit(stream);
        if (!emitResult.Success)
            throw new AssertionException(string.Join(
                Environment.NewLine,
                emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return new FixtureAssembly(tempDirectory, assemblyPath);
    }

    private static async Task<string> RunEffectSummaryJsonAsync(string assemblyPath, bool includeTransitiveRoots)
    {
        var outputPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, Guid.NewGuid().ToString("N") + ".json");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(EffectSummaryToolTests.GetEffectSummaryToolDllPath());
        startInfo.ArgumentList.Add("--assembly");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        if (includeTransitiveRoots) startInfo.ArgumentList.Add("--transitive-roots");

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start effect summary tool.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new AssertionException(
                "Effect summary tool failed." + Environment.NewLine +
                standardOutput + Environment.NewLine +
                standardError);

        return await File.ReadAllTextAsync(outputPath);
    }

    private static async Task<string> RunFilteredEffectSummaryJsonAsync(
        string assemblyPath,
        bool includeTransitiveRoots,
        int maxDepth,
        params string[] symbolPrefixes)
    {
        var outputPath = Path.Combine(Path.GetDirectoryName(assemblyPath)!, Guid.NewGuid().ToString("N") + ".json");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = GetRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(EffectSummaryToolTests.GetEffectSummaryToolDllPath());
        startInfo.ArgumentList.Add("--assembly");
        startInfo.ArgumentList.Add(assemblyPath);
        foreach (var symbolPrefix in symbolPrefixes)
        {
            startInfo.ArgumentList.Add("--symbol-prefix");
            startInfo.ArgumentList.Add(symbolPrefix);
        }

        startInfo.ArgumentList.Add("--include-callees");
        startInfo.ArgumentList.Add("--max-depth");
        startInfo.ArgumentList.Add(maxDepth.ToString());
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(outputPath);
        if (includeTransitiveRoots) startInfo.ArgumentList.Add("--transitive-roots");

        using var process = Process.Start(startInfo) ??
                            throw new InvalidOperationException("Failed to start effect summary tool.");
        var standardOutput = await process.StandardOutput.ReadToEndAsync();
        var standardError = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
            throw new AssertionException(
                "Effect summary tool failed." + Environment.NewLine +
                standardOutput + Environment.NewLine +
                standardError);

        return await File.ReadAllTextAsync(outputPath);
    }

    private static string GetRepositoryRoot()
    {
        return AnalyzerTestHost.GetRepositoryRoot();
    }

    private static void AssertEffectSummaryException(
        ImmutableArray<Diagnostic> diagnostics,
        string methodName,
        string exceptionType)
    {
        var diagnostic = diagnostics
            .Where(d => d.Id == SharpProofDiagnostics.ExceptionSummaryId)
            .Single(d => d.GetMessage().Contains("'" + methodName + "'", StringComparison.Ordinal));

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty], Is.EqualTo(exceptionType));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
            Is.EqualTo("effect_summary"));
    }

    private static int GetCount(object instance)
    {
        return (int)instance.GetType().GetProperty("Count")!.GetValue(instance)!;
    }

    private sealed class EffectSummaryTypeNameProvider : ISignatureTypeProvider<string, object?>
    {
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
            return "!!" + index;
        }

        public string GetGenericTypeParameter(object? genericContext, int index)
        {
            return "!" + index;
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
            return GeneratedPurityTestSupport.NormalizeExactTypeName(
                GeneratedPurityTestSupport.GetTypeName(metadataReader, handle));
        }

        public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            return GeneratedPurityTestSupport.NormalizeExactTypeName(GetTypeReferenceName(metadataReader, handle));
        }

        public string GetTypeFromSpecification(MetadataReader metadataReader, object? genericContext,
            TypeSpecificationHandle handle, byte rawTypeKind)
        {
            return metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }
    }

    private sealed record AssemblyIdentity(
        string AssemblyPath,
        string AssemblyName,
        string AssemblySha256,
        string ModuleVersionId);

    private sealed record MethodIdentity(
        string MetadataToken,
        string? MethodBodySha256,
        StructuralMethodIdentity Identity,
        string ExactSymbolKey,
        string Symbol)
    {
        internal string IdentityJson => JsonSerializer.Serialize(Identity);

        internal string CanonicalKey => Identity.ToCanonicalKey();
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string text)
        {
            Path = path;
            _text = SourceText.From(text);
        }

        public override string Path { get; }

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            return _text;
        }
    }

    private sealed class FixtureAssembly : IAsyncDisposable
    {
        public FixtureAssembly(string directoryPath, string assemblyPath)
        {
            DirectoryPath = directoryPath;
            AssemblyPath = assemblyPath;
        }

        public string DirectoryPath { get; }

        public string AssemblyPath { get; }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);

            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _emptyOptions =
            new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

        public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
        {
            GlobalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _emptyOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return _emptyOptions;
        }
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
