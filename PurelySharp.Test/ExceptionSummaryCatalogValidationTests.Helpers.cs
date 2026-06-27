using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
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
            var syntaxTree = CSharpSyntaxTree.ParseText(CreateLibraryCallSource(), new CSharpParseOptions(LanguageVersion.Preview));
            return CSharpCompilation.Create(
                "ExceptionSummaryCatalogValidationTests",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
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
            return $$"""
{
  "SchemaVersion": 1,
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
          "Symbol": "{{symbol}}",
          "MetadataToken": "{{metadataToken}}",
          "RelativeVirtualAddress": 0,
          "MethodBodySha256": {{methodBodySha256Json}},
          "CacheKey": "validation-test",
          "Effects": [],
          "RootCandidates": [],
          "TransitiveRootCandidates": [],
          "ThrownExceptionTypes": {{thrownExceptionTypesJson}},
          "TransitiveThrownExceptionTypes": {{transitiveThrownExceptionTypesJson}},
          "ThrownExceptionSourcePaths": {{thrownExceptionSourcePathsJson}},
          "TransitiveThrownExceptionSourcePaths": {{transitiveThrownExceptionSourcePathsJson}},
          "ThrownExceptionEdges": {{thrownExceptionEdgesJson}},
          "TransitiveThrownExceptionEdges": {{transitiveThrownExceptionEdgesJson}},
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

            Assert.That(actual.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo(expected.Properties[PurelySharpDiagnostics.ExceptionTypesProperty]));
            Assert.That(actual.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo(expected.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty]));
            Assert.That(actual.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo(expected.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty]));

            var expectedHasSymbol = expected.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionSymbolProperty, out var expectedSymbol);
            var actualHasSymbol = actual.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionSymbolProperty, out var actualSymbol);
            Assert.That(actualHasSymbol, Is.EqualTo(expectedHasSymbol));
            if (expectedHasSymbol)
            {
                Assert.That(actualSymbol, Is.EqualTo(expectedSymbol));
            }

            var expectedHasEdges = expected.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionEdgesProperty, out var expectedEdges);
            var actualHasEdges = actual.Properties.TryGetValue(PurelySharpDiagnostics.ExceptionEdgesProperty, out var actualEdges);
            Assert.That(actualHasEdges, Is.EqualTo(expectedHasEdges));
            if (expectedHasEdges)
            {
                Assert.That(actualEdges, Is.EqualTo(expectedEdges));
            }
        }

        private static string CreateMalformedEffectSummaryJson(string assemblyName, string assemblySha256, string moduleVersionId)
        {
            return $$"""
{
  "SchemaVersion": 1,
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
                return "(" + string.Join(", ", signature.ParameterTypes) + ")->" + signature.ReturnType;
            }
            catch (BadImageFormatException)
            {
                return "(?)->?";
            }
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

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            string effectSummaryJson,
            ImmutableArray<MetadataReference> additionalReferences,
            string additionalFilePath = "PurelySharp.EffectSummary.json")
        {
            return await GetAnalyzerDiagnosticsAsync(
                source,
                new[] { (additionalFilePath, effectSummaryJson) },
                additionalReferences);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            string effectSummaryJson,
            string additionalFilePath = "PurelySharp.EffectSummary.json")
        {
            return await GetAnalyzerDiagnosticsAsync(
                source,
                effectSummaryJson,
                ImmutableArray<MetadataReference>.Empty,
                additionalFilePath);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            params (string Path, string Text)[] effectSummaryFiles)
        {
            return await GetAnalyzerDiagnosticsAsync(
                source,
                effectSummaryFiles,
                ImmutableArray<MetadataReference>.Empty);
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            (string Path, string Text)[] effectSummaryFiles,
            ImmutableArray<MetadataReference> additionalReferences)
        {
            return await GetAnalyzerDiagnosticsAsync(
                source,
                effectSummaryFiles,
                additionalReferences,
                null);
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
                GetTrustedPlatformReferences().AddRange(additionalReferences),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzerGlobalOptions = globalOptions ?? ImmutableDictionary<string, string>.Empty;
            if (!analyzerGlobalOptions.ContainsKey("purelysharp_report_exceptions"))
            {
                analyzerGlobalOptions = analyzerGlobalOptions.Add(
                    "purelysharp_report_exceptions",
                    "true");
            }

            if (!analyzerGlobalOptions.ContainsKey("purelysharp_checked_exceptions"))
            {
                analyzerGlobalOptions = analyzerGlobalOptions.Add(
                    "purelysharp_checked_exceptions",
                    "true");
            }

            var analyzerOptions = new AnalyzerOptions(
                effectSummaryFiles
                    .Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text))
                    .ToImmutableArray(),
                new TestAnalyzerConfigOptionsProvider(analyzerGlobalOptions));

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new PurelySharpAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    analyzerOptions,
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        private static ImmutableDictionary<string, string> CreateEffectSummaryJsonEnabledGlobalOptions(
            ImmutableDictionary<string, string>? globalOptions = null)
        {
            var analyzerGlobalOptions = globalOptions ?? ImmutableDictionary<string, string>.Empty;
            return analyzerGlobalOptions.SetItem("purelysharp_enable_effect_summary_json", "true");
        }

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsWithEffectSummaryJsonEnabledAsync(
            string source,
            string effectSummaryJson,
            ImmutableArray<MetadataReference> additionalReferences,
            string additionalFilePath = "PurelySharp.EffectSummary.json")
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
            string additionalFilePath = "PurelySharp.EffectSummary.json")
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
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            await using var stream = File.Create(assemblyPath);
            var emitResult = compilation.Emit(stream);
            if (!emitResult.Success)
            {
                throw new AssertionException(string.Join(
                    Environment.NewLine,
                    emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));
            }

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
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(GetEffectSummaryToolDllPath());
            startInfo.ArgumentList.Add("--assembly");
            startInfo.ArgumentList.Add(assemblyPath);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(outputPath);
            if (includeTransitiveRoots)
            {
                startInfo.ArgumentList.Add("--transitive-roots");
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start effect summary tool.");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new AssertionException(
                    "Effect summary tool failed." + Environment.NewLine +
                    standardOutput + Environment.NewLine +
                    standardError);
            }

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
                UseShellExecute = false,
            };
            startInfo.ArgumentList.Add(GetEffectSummaryToolDllPath());
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
            if (includeTransitiveRoots)
            {
                startInfo.ArgumentList.Add("--transitive-roots");
            }

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start effect summary tool.");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new AssertionException(
                    "Effect summary tool failed." + Environment.NewLine +
                    standardOutput + Environment.NewLine +
                    standardError);
            }

            return await File.ReadAllTextAsync(outputPath);
        }

        private static string GetEffectSummaryToolDllPath()
        {
            lock (EffectSummaryToolBuildLock)
            {
                if (!string.IsNullOrWhiteSpace(s_effectSummaryToolDllPath) && File.Exists(s_effectSummaryToolDllPath))
                {
                    return s_effectSummaryToolDllPath;
                }

                var repositoryRoot = GetRepositoryRoot();
                var projectPath = Path.Combine(repositoryRoot, "Tools", "PurelySharp.EffectSummary", "PurelySharp.EffectSummary.csproj");
                var dllPath = Path.Combine(repositoryRoot, "Tools", "PurelySharp.EffectSummary", "bin", "Debug", "net8.0", "PurelySharp.EffectSummary.dll");
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                startInfo.ArgumentList.Add("build");
                startInfo.ArgumentList.Add(projectPath);
                startInfo.ArgumentList.Add("-m:20");
                startInfo.ArgumentList.Add("--no-restore");

                using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to build effect summary tool.");
                var standardOutput = process.StandardOutput.ReadToEnd();
                var standardError = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0 || !File.Exists(dllPath))
                {
                    throw new AssertionException(
                        "Effect summary tool build failed." + Environment.NewLine +
                        standardOutput + Environment.NewLine +
                        standardError);
                }

                s_effectSummaryToolDllPath = dllPath;
                return s_effectSummaryToolDllPath;
            }
        }

        private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }

        private static string GetRepositoryRoot()
        {
            return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", ".."));
        }

        private static void AssertEffectSummaryException(
            ImmutableArray<Diagnostic> diagnostics,
            string methodName,
            string exceptionType)
        {
            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .Single(d => d.GetMessage().Contains("'" + methodName + "'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo(exceptionType));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
        }

        private static int GetCount(object instance)
        {
            return (int)instance.GetType().GetProperty("Count")!.GetValue(instance)!;
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
            public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(", ", typeArguments) + ">";
            public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
            public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
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
            public string GetTypeFromDefinition(MetadataReader metadataReader, TypeDefinitionHandle handle, byte rawTypeKind)
                => NormalizeExactTypeName(GetTypeName(metadataReader, handle));
            public string GetTypeFromReference(MetadataReader metadataReader, TypeReferenceHandle handle, byte rawTypeKind)
                => NormalizeExactTypeName(GetTypeReferenceName(metadataReader, handle));
            public string GetTypeFromSpecification(MetadataReader metadataReader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
                => metadataReader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
        }

        private sealed record AssemblyIdentity(string AssemblyPath, string AssemblyName, string AssemblySha256, string ModuleVersionId);
        private sealed record MethodIdentity(string MetadataToken, string? MethodBodySha256, string ExactSymbolKey, string Symbol);

        private sealed class InMemoryAdditionalText : AdditionalText
        {
            private readonly SourceText _text;

            public InMemoryAdditionalText(string path, string text)
            {
                Path = path;
                _text = SourceText.From(text);
            }

            public override string Path { get; }

            public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default)
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
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }

                return ValueTask.CompletedTask;
            }
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
