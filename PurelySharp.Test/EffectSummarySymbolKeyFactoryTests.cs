using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public class EffectSummarySymbolKeyFactoryTests
    {
        [Test]
        public void MetadataDefinitionExactMethodKey_DistinguishesDuplicateDisplayOperators()
        {
            const string source = """
public readonly struct ConversionFixture
{
    private readonly int _value;

    public ConversionFixture(int value)
    {
        _value = value;
    }

    public static explicit operator int(ConversionFixture value) => value._value;

    public static explicit operator long(ConversionFixture value) => value._value;
}
""";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "EffectSummarySymbolKeyFactoryTests",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var typeSymbol = compilation.GetTypeByMetadataName("ConversionFixture");
            Assert.That(typeSymbol, Is.Not.Null);

            var operators = typeSymbol!
                .GetMembers()
                .OfType<IMethodSymbol>()
                .Where(method => method.Name == "op_Explicit")
                .OrderBy(method => method.ReturnType.ToDisplayString(), StringComparer.Ordinal)
                .ToArray();

            var factoryType = typeof(PurelySharpAnalyzer).Assembly.GetType(
                "PurelySharp.Analyzer.EffectSummarySymbolKeyFactory",
                throwOnError: true)!;
            var helper = factoryType.GetMethod(
                "GetMetadataDefinitionExactMethodKey",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;

            var keys = operators
                .Select(method => (string)helper.Invoke(null, new object[] { method })!)
                .ToArray();

            Assert.That(keys, Has.Length.EqualTo(2));
            Assert.That(keys.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(2));
            Assert.That(keys[0], Does.EndWith(")->int"));
            Assert.That(keys[1], Does.EndWith(")->long"));
        }

        [Test]
        public void GeneratedPurityCatalog_UsesFactoryKey_ForRuntimeImplementationAssemblyResolution()
        {
            const string source = """
using System;

public static class UriFixture
{
    public static bool Probe(string value)
    {
        return Uri.IsWellFormedUriString(value, UriKind.Absolute);
    }
}
""";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityCatalogRuntimeResolution",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;

            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType(
                "PurelySharp.Analyzer.GeneratedPurityCatalog",
                throwOnError: true)!;
            var tryResolveActualAssemblyIdentity = catalogType.GetMethod(
                "TryResolveActualAssemblyIdentity",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var tryResolveActualMethodIdentity = catalogType.GetMethod(
                "TryResolveActualMethodIdentity",
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;

            var actualAssemblyIdentity = tryResolveActualAssemblyIdentity.Invoke(null, new object[] { methodSymbol.OriginalDefinition, compilation })!;
            var actualMethodIdentity = tryResolveActualMethodIdentity.Invoke(null, new object[] { methodSymbol.OriginalDefinition, compilation })!;

            Assert.That(actualAssemblyIdentity, Is.Not.Null);
            Assert.That(actualMethodIdentity, Is.Not.Null);

            var factoryType = typeof(PurelySharpAnalyzer).Assembly.GetType(
                "PurelySharp.Analyzer.EffectSummarySymbolKeyFactory",
                throwOnError: true)!;
            var exactKeyHelper = factoryType.GetMethod(
                "GetMetadataDefinitionExactMethodKey",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
            var exactSymbolKey = (string)exactKeyHelper.Invoke(null, new object[] { methodSymbol.OriginalDefinition })!;

            var summaryJson = $$"""
{
  "GeneratedPurityCatalog": {
    "Entries": [
      {
        "Symbol": "{{exactSymbolKey}}",
        "ExactSymbolKey": "{{exactSymbolKey}}",
        "AssemblyName": "{{GetProperty(actualAssemblyIdentity, "AssemblyName")}}",
        "AssemblySha256": "{{GetProperty(actualAssemblyIdentity, "AssemblySha256")}}",
        "ModuleVersionId": "{{GetProperty(actualAssemblyIdentity, "ModuleVersionId")}}",
        "MetadataToken": "{{GetProperty(actualMethodIdentity, "MetadataToken")}}",
        "MethodBodySha256": {{FormatJsonStringOrNull(GetProperty(actualMethodIdentity, "MethodBodySha256"))}},
        "Classification": "pure",
        "Categories": []
      }
    ]
  }
}
""";

            var analyzerOptions = new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(
                    "Synthetic.PurelySharp.EffectSummary.json",
                    summaryJson)));
            var catalog = fromOptions.Invoke(null, new object[] { analyzerOptions, CancellationToken.None })!;
            var tryGetPurityArgs = new object?[] { methodSymbol.OriginalDefinition, compilation, null };

            var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;

            Assert.That(matched, Is.True, "Generated purity catalog should still resolve Uri.IsWellFormedUriString through the runtime implementation cache key path.");
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

        private static string GetProperty(object value, string propertyName)
        {
            return (string)value.GetType().GetProperty(propertyName)!.GetValue(value)!;
        }

        private static string FormatJsonStringOrNull(string? value)
        {
            return value == null
                ? "null"
                : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
