using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Identity;

namespace SharpProof.Test;

[TestFixture]
public class EffectSummarySymbolKeyFactoryTests
{
    [TestCase("ref")]
    [TestCase("out")]
    [TestCase("in")]
    public void CompatibleCanonicalKeys_IncludeMemberReferenceByRefFallback(string modifier)
    {
        var body = modifier == "out" ? "value = 0;" : string.Empty;
        var source = $$"""
                       public static class Fixture
                       {
                           public static void Target({{modifier}} int value) { {{body}} }
                       }
                       """;
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "ByRefCompatibility",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var method = compilation.GetTypeByMetadataName("Fixture")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var keys = RoslynStructuralMethodIdentity.GetCompatibleCanonicalKeys(method);
        var collapsed = RoslynStructuralMethodIdentity.Create(method)
            .WithUnavailableParameterRefKindsCollapsed()
            .ToCanonicalKey();

        Assert.That(keys, Does.Contain(collapsed));
    }

    [Test]
    public void StructuralMethodIdentity_DistinguishesDuplicateDisplayOperators()
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
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var typeSymbol = compilation.GetTypeByMetadataName("ConversionFixture");
        Assert.That(typeSymbol, Is.Not.Null);

        var operators = typeSymbol!
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.Name == "op_Explicit")
            .OrderBy(method => method.ReturnType.ToDisplayString(), StringComparer.Ordinal)
            .ToArray();

        var keys = operators
            .Select(RoslynStructuralMethodIdentity.Create)
            .ToArray();

        Assert.That(keys, Has.Length.EqualTo(2));
        Assert.That(keys.Distinct().Count(), Is.EqualTo(2));
        Assert.That(keys[0].ReturnType, Is.EqualTo("named:System.Int32"));
        Assert.That(keys[1].ReturnType, Is.EqualTo("named:System.Int64"));
        Assert.That(keys.Select(static identity => identity.ToCanonicalKey()),
            Has.All.StartsWith(StructuralMethodIdentity.KeyPrefix + "|"));
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
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var invocation = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Single();
        var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;

        var identityResolver = new EffectSummaryIdentityResolver(
            true,
            false,
            RoslynStructuralMethodIdentity.GetCanonicalKey);
        var catalogType = typeof(SharpProofAnalyzer).Assembly.GetType(
            "SharpProof.Analyzer.EffectSummaryCatalog",
            true)!;
        var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
        var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;

        var actualAssemblyIdentity = identityResolver.TryResolveActualAssemblyIdentity(
            methodSymbol.OriginalDefinition,
            compilation);
        var actualMethodIdentity = identityResolver.TryResolveActualMethodIdentity(
            methodSymbol.OriginalDefinition,
            compilation);

        Assert.That(actualAssemblyIdentity, Is.Not.Null);
        Assert.That(actualMethodIdentity, Is.Not.Null);
        var resolvedAssemblyIdentity = actualAssemblyIdentity!;
        var resolvedMethodIdentity = actualMethodIdentity!;

        var structuralIdentity = RoslynStructuralMethodIdentity.Create(methodSymbol.OriginalDefinition);
        var canonicalKey = structuralIdentity.ToCanonicalKey();
        var identityJson = JsonSerializer.Serialize(structuralIdentity);

        var summaryJson = $$"""
                            {
                              "SchemaVersion": 5,
                              "EvidenceSchemaVersion": 2,
                              "EvidenceSchemaCompatibility": "exact-v2",
                              "GeneratedPurityCatalog": {
                                "SchemaVersion": 5,
                                "Entries": [
                                  {
                                    "DisplayName": "{{methodSymbol.ToDisplayString()}}",
                                    "Identity": {{identityJson}},
                                    "CanonicalKey": "{{canonicalKey}}",
                                    "AssemblyName": "{{GetProperty(resolvedAssemblyIdentity, "AssemblyName")}}",
                                    "AssemblySha256": "{{GetProperty(resolvedAssemblyIdentity, "AssemblySha256")}}",
                                    "ModuleVersionId": "{{GetProperty(resolvedAssemblyIdentity, "ModuleVersionId")}}",
                                    "MetadataToken": "{{GetProperty(resolvedMethodIdentity, "MetadataToken")}}",
                                    "MethodBodySha256": {{FormatJsonStringOrNull(GetProperty(resolvedMethodIdentity, "MethodBodySha256"))}},
                                    "Classification": "pure",
                                    "Categories": []
                                  }
                                ]
                              }
                            }
                            """;

        var analyzerOptions = new AnalyzerOptions(ImmutableArray.Create<AdditionalText>(
            new AnalyzerTestHost.InMemoryAdditionalText(
                "Synthetic.SharpProof.EffectSummary.json",
                summaryJson)));
        var catalog = fromOptions.Invoke(null, new object[] { analyzerOptions, CancellationToken.None })!;
        var tryGetPurityArgs = new object?[] { methodSymbol.OriginalDefinition, compilation, null };

        var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;

        Assert.That(matched, Is.True,
            "Generated purity catalog should still resolve Uri.IsWellFormedUriString through the runtime implementation cache key path.");
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
