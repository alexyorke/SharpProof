using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
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
    [TestFixture]
    public class ExceptionSummaryCatalogValidationTests
    {
        [Test]
        public async Task Ps0010_EffectSummary_WithMatchingAssemblyIdentity_IsTrusted()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(CreateLibraryCallSource(), CreateEffectSummaryJson(
                coreLib.AssemblyName,
                coreLib.AssemblySha256,
                coreLib.ModuleVersionId));

            var diagnostic = diagnostics.Single(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
        }

        [Test]
        public async Task Ps0010_EffectSummary_WithMismatchedAssemblyIdentity_IsIgnored()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(CreateLibraryCallSource(), CreateEffectSummaryJson(
                coreLib.AssemblyName,
                "0000000000000000000000000000000000000000000000000000000000000000",
                "00000000-0000-0000-0000-000000000000"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummary_WithIncompleteAssemblyIdentity_IsIgnored()
        {
            var coreLib = GetAssemblyIdentity(typeof(ArgumentNullException).Assembly.Location);
            var diagnostics = await GetAnalyzerDiagnosticsAsync(CreateLibraryCallSource(), CreateEffectSummaryJson(
                coreLib.AssemblyName,
                string.Empty,
                coreLib.ModuleVersionId));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

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

        private static string CreateEffectSummaryJson(string assemblyName, string assemblySha256, string moduleVersionId)
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
          "ThrownExceptionTypes": [],
          "TransitiveThrownExceptionTypes": [ "System.ArgumentNullException" ],
          "Calls": [],
          "Fields": []
        }
      ]
    }
  ]
}
""";
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

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source, string effectSummaryJson)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ExceptionSummaryCatalogValidationTests",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var analyzerOptions = new AnalyzerOptions(
                ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText("PurelySharp.EffectSummary.json", effectSummaryJson)),
                new TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_report_exceptions",
                    "true")));

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

        private sealed record AssemblyIdentity(string AssemblyName, string AssemblySha256, string ModuleVersionId);

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
