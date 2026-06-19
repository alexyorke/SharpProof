using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    [TestFixture]
    public class DiagnosticEvidenceTests
    {
        [Test]
        public async Task Ps0002_KnownImpureCatalogHit_IncludesStructuredEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Console.WriteLine(""impure"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Invocation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownImpureMethod_IncludesConfigCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return CustomApi();
    }

    private int CustomApi()
    {
        return 42;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_methods",
                    "TestClass.CustomApi()"));

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("config_known_impure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.CustomApi"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownImpureTargetMethod_IncludesConfigCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return 42;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_methods",
                    "TestClass.TestMethod()"));

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("KnownImpureMethod"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("config_known_impure"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.TestMethod"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownImpureTypeProperty_IncludesNamespaceOrTypeCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class Boundary
{
    public int Value => 1;
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Boundary boundary)
    {
        return boundary.Value;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_types",
                    "Boundary"));

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("Boundary.Value"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownImpureTypeOverridesKnownPureBclHeuristic()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return Math.Abs(value);
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "purelysharp_known_impure_types",
                    "System.Math"));

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_impure_namespace_or_type"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Math.Abs"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownPureMethodOverridesConfiguredImpureType()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return Math.Abs(value);
    }
}",
                ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_known_impure_types", "System.Math")
                    .Add("purelysharp_known_pure_methods", "System.Math.Abs(int)"));

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Configured pure member should override a configured impure type for the same member.");
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownPurePropertyOverridesConfiguredImpureNamespace()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IPAddress TestMethod()
    {
        return IPAddress.Loopback;
    }
}",
                ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_known_impure_namespaces", "System.Net")
                    .Add("purelysharp_known_pure_methods", "System.Net.IPAddress.Loopback.get"));

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Configured pure property should override a configured impure namespace for the same member.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_Sha256HashDataReadOnlySpan()
        {
            const string source = @"
using System;
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public byte[] TestMethod(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var tryGetPurityArgs = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should keep SHA256.HashData(ReadOnlySpan<byte>) out of the impure cryptography namespace fallback.");
            Assert.That(matched, Is.True, "Generated purity catalog should trust the exact SHA256.ReadOnlySpan<byte> overload.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_UriIsWellFormedUriString_FromRuntimeImplementationAssembly()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string value)
    {
        return Uri.IsWellFormedUriString(value, UriKind.Absolute);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var tryGetPurityArgs = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Uri.IsWellFormedUriString even when the symbol resolves through a facade assembly.");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Uri.IsWellFormedUriString to its runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_UriEscapeAndUnescapeDataString_FromRuntimeImplementationAssembly()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return Uri.UnescapeDataString(Uri.EscapeDataString(value));
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Select(invocation => (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!)
                .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = invocations.Select(symbol =>
            {
                var args = new object?[] { symbol.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Uri.EscapeDataString and Uri.UnescapeDataString when the symbol resolves through a facade assembly.");
            Assert.That(matched, Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve both Uri.EscapeDataString and Uri.UnescapeDataString to their runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_UriToStringAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(Uri value)
    {
        return value.ToString();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "value.ToString()");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve System.Uri.ToString().");
            Assert.That(classification, Is.EqualTo("impure"),
                "System.Uri.ToString() mutates cached instance state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_OperatingSystemIsWindows_FromRuntimeImplementationAssembly()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return OperatingSystem.IsWindows();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var tryGetPurityArgs = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow OperatingSystem.IsWindows.");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve OperatingSystem.IsWindows to its runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_AppContextTargetFrameworkName_Getter()
        {
            const string source = @"
#nullable enable
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod()
    {
        return AppContext.TargetFrameworkName;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "AppContext.TargetFrameworkName");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var getter = propertySymbol.GetMethod!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var tryGetPurityArgs = new object?[] { getter.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, tryGetPurityArgs)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow AppContext.TargetFrameworkName.");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve AppContext.TargetFrameworkName.get to its runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentStablePureGetters()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.Is64BitProcess && Environment.Is64BitOperatingSystem
            ? Environment.NewLine
            : string.Empty;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccesses = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Environment.Is64BitProcess" ||
                    node.ToString() == "Environment.Is64BitOperatingSystem" ||
                    node.ToString() == "Environment.NewLine")
                .Select(node => (IPropertySymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .OrderBy(symbol => symbol.Name, StringComparer.Ordinal)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = memberAccesses.Select(symbol =>
            {
                var args = new object?[] { symbol.GetMethod!.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Environment.Is64BitProcess, Environment.Is64BitOperatingSystem, and Environment.NewLine.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true }),
                "Generated purity catalog should resolve the stable Environment getters to their runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_StaticCachePureGetters()
        {
            const string source = @"
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        _ = Comparer<int>.Default;
        _ = EqualityComparer<int>.Default;
        _ = Task.CompletedTask;
        _ = Encoding.ASCII;
        _ = CultureInfo.InvariantCulture;
        return 5;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccesses = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Comparer<int>.Default" ||
                    node.ToString() == "EqualityComparer<int>.Default" ||
                    node.ToString() == "Task.CompletedTask" ||
                    node.ToString() == "Encoding.ASCII" ||
                    node.ToString() == "CultureInfo.InvariantCulture")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = memberAccesses.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(classifications["Comparer<int>.Default"].matched, Is.True);
            Assert.That(classifications["Comparer<int>.Default"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["EqualityComparer<int>.Default"].matched, Is.True);
            Assert.That(classifications["EqualityComparer<int>.Default"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["Task.CompletedTask"].matched, Is.True);
            Assert.That(classifications["Task.CompletedTask"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["Encoding.ASCII"].matched, Is.True);
            Assert.That(classifications["Encoding.ASCII"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["CultureInfo.InvariantCulture"].matched, Is.True);
            Assert.That(classifications["CultureInfo.InvariantCulture"].classification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CancellationTokenNoneAsPureEvidence()
        {
            const string source = @"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        _ = CancellationToken.None;
        return true;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "CancellationToken.None");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = matched
                ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                : string.Empty;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow CancellationToken.None even under the System.Threading namespace fallback.");
            Assert.That(matched, Is.True);
            Assert.That(classification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_IPAddressIsLoopbackAsPureEvidence()
        {
            const string source = @"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(IPAddress address)
    {
        return IPAddress.IsLoopback(address);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "IPAddress.IsLoopback(address)");
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var method = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;
            var args = new object?[] { method.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = matched
                ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                : string.Empty;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow IPAddress.IsLoopback(System.Net.IPAddress).");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve IPAddress.IsLoopback(System.Net.IPAddress).");
            Assert.That(classification, Is.EqualTo("pure"),
                "IPAddress.IsLoopback should classify pure once loopback singleton reads are treated as safe cache reads.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_IPEndPointConstructorAsImpureEvidence()
        {
            const string source = @"
using System.Net;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IPEndPoint TestMethod(IPAddress address)
    {
        return new IPEndPoint(address, 80);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var objectCreation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(node => node.ToString() == "new IPEndPoint(address, 80)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(objectCreation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve System.Net.IPEndPoint..ctor(System.Net.IPAddress, int).");
            Assert.That(classification, Is.EqualTo("impure"),
                "System.Net.IPEndPoint..ctor(System.Net.IPAddress, int) writes caller-visible object state and validates the port.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_AggregateExceptionMembersAsImpureEvidence()
        {
            const string source = @"
using System;
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public AggregateException Create(IEnumerable<Exception> values)
    {
        return new AggregateException(values);
    }

    [EnforcePure]
    public AggregateException Flatten(AggregateException value)
    {
        return value.Flatten();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .Where(node =>
                    node is ObjectCreationExpressionSyntax objectCreation &&
                    objectCreation.ToString() == "new AggregateException(values)" ||
                    node is InvocationExpressionSyntax invocation &&
                    invocation.ToString() == "value.Flatten()")
                .Select(node => node switch
                {
                    ObjectCreationExpressionSyntax objectCreation => semanticModel.GetSymbolInfo(objectCreation).Symbol as IMethodSymbol,
                    InvocationExpressionSyntax invocation => semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol,
                    _ => null,
                })
                .Where(method => method is not null)
                .Select(method => method!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = trackedMethods
                .ToDictionary(
                    method => method.ToDisplayString(),
                    method =>
                    {
                        var args = new object?[] { method.OriginalDefinition, compilation, null };
                        var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                        var purityEntry = args[2]!;
                        var classification = matched
                            ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                            : string.Empty;
                        return (matched, classification);
                    });

            Assert.That(
                diagnostics.Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId).Select(candidate => candidate.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }),
                "AggregateException constructor and Flatten should now diagnose via reviewed generated runtime evidence.");
            Assert.That(classifications["System.AggregateException.AggregateException(System.Collections.Generic.IEnumerable<System.Exception>)"].matched, Is.True,
                "Generated purity catalog should resolve AggregateException(IEnumerable<Exception>).");
            Assert.That(classifications["System.AggregateException.AggregateException(System.Collections.Generic.IEnumerable<System.Exception>)"].classification, Is.EqualTo("impure"),
                "AggregateException(IEnumerable<Exception>) should classify impure from reviewed runtime evidence.");
            Assert.That(classifications["System.AggregateException.Flatten()"].matched, Is.True,
                "Generated purity catalog should resolve AggregateException.Flatten().");
            Assert.That(classifications["System.AggregateException.Flatten()"].classification, Is.EqualTo("impure"),
                "AggregateException.Flatten() should classify impure from reviewed runtime evidence.");
        }

        [Test]
        public void GeneratedPurityCatalog_Resolves_NullableComparisonAsConservativeUnknown()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(int? left, int? right)
    {
        _ = Nullable.Compare(left, right);
        return Nullable.Equals(left, right);
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Nullable.Compare(left, right)" ||
                    node.ToString() == "Nullable.Equals(left, right)")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = invocations.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var method = (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(classifications["Nullable.Compare(left, right)"].matched, Is.True);
            Assert.That(classifications["Nullable.Compare(left, right)"].classification, Is.EqualTo("conservative_unknown"));
            Assert.That(classifications["Nullable.Equals(left, right)"].matched, Is.True);
            Assert.That(classifications["Nullable.Equals(left, right)"].classification, Is.EqualTo("conservative_unknown"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_NullableGetValueOrDefaultAsPureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int? value, int fallback)
    {
        return value.GetValueOrDefault() + value.GetValueOrDefault(fallback);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocations = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "value.GetValueOrDefault()" ||
                    node.ToString() == "value.GetValueOrDefault(fallback)")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = invocations.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var method = (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Nullable<T>.GetValueOrDefault overloads.");
            Assert.That(classifications["value.GetValueOrDefault()"].matched, Is.True);
            Assert.That(classifications["value.GetValueOrDefault()"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["value.GetValueOrDefault(fallback)"].matched, Is.True);
            Assert.That(classifications["value.GetValueOrDefault(fallback)"].classification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ExceptionStateAccessorsAsPureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Exception error)
    {
        _ = error.Message;
        _ = error.InnerException;
        return error.HResult;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccesses = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "error.Message" ||
                    node.ToString() == "error.InnerException" ||
                    node.ToString() == "error.HResult")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var currentProperty = catalogType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var currentCatalog = currentProperty.GetValue(null)!;
            var classifications = memberAccesses.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var property = (IPropertySymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { property.GetMethod!.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    var currentArgs = new object?[] { property.GetMethod!.OriginalDefinition, compilation, null };
                    var currentMatched = (bool)tryGetPurity.Invoke(currentCatalog, currentArgs)!;
                    var currentPurityEntry = currentArgs[2]!;
                    var currentClassification = currentMatched
                        ? (string)currentPurityEntry.GetType().GetProperty("Classification")!.GetValue(currentPurityEntry)!
                        : string.Empty;
                    return (matched, classification, currentMatched, currentClassification);
                });

            Assert.That(classifications["error.Message"].matched, Is.True);
            Assert.That(classifications["error.Message"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["error.Message"].currentMatched, Is.True);
            Assert.That(classifications["error.Message"].currentClassification, Is.EqualTo("pure"));
            Assert.That(classifications["error.InnerException"].matched, Is.True);
            Assert.That(classifications["error.InnerException"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["error.InnerException"].currentMatched, Is.True);
            Assert.That(classifications["error.InnerException"].currentClassification, Is.EqualTo("pure"));
            Assert.That(classifications["error.HResult"].matched, Is.True);
            Assert.That(classifications["error.HResult"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["error.HResult"].currentMatched, Is.True);
            Assert.That(classifications["error.HResult"].currentClassification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArgumentGuardHelpersAsPureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(string text, int number, object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrEmpty(text);
        ArgumentOutOfRangeException.ThrowIfNegative(number);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "ArgumentNullException.ThrowIfNull(value)" ||
                    node.ToString() == "ArgumentException.ThrowIfNullOrEmpty(text)" ||
                    node.ToString() == "ArgumentOutOfRangeException.ThrowIfNegative(number)")
                .Select(node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .Select(symbol => symbol.OriginalDefinition)
                .OrderBy(symbol => symbol.ToDisplayString(), StringComparer.Ordinal)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the argument guard helpers backed by runtime summaries.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true }),
                "Generated purity catalog should resolve the tracked argument guard helpers to their runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentCurrentDirectoryAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.CurrentDirectory;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.CurrentDirectory");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.CurrentDirectory.get.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.CurrentDirectory depends on process/OS state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DirectoryGetCurrentDirectoryAsImpureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Directory.GetCurrentDirectory();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Directory.GetCurrentDirectory()");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Directory.GetCurrentDirectory"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Directory.GetCurrentDirectory().");
            Assert.That(classification, Is.EqualTo("impure"),
                "Directory.GetCurrentDirectory depends on process/OS state through Environment.CurrentDirectory and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DirectoryExistsAsImpureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string path)
    {
        return Directory.Exists(path);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Directory.Exists(path)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Directory.Exists"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Directory.Exists(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Directory.Exists depends on ambient file-system and path state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_FileExistsAsImpureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(string path)
    {
        return File.Exists(path);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "File.Exists(path)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.File.Exists"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve File.Exists(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "File.Exists depends on ambient file-system state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DirectoryCreateDirectoryAsImpureEvidence()
        {
            const string source = @"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DirectoryInfo TestMethod(string path)
    {
        return Directory.CreateDirectory(path);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Directory.CreateDirectory(path)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Directory.CreateDirectory"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Directory.CreateDirectory(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Directory.CreateDirectory mutates ambient file-system state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_FileSystemPathGettersAsMixedEvidence()
        {
            const string source = @"
#nullable enable
using System.IO;
using System.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DirectoryInfo? ParentMethod(DirectoryInfo directory)
    {
        return directory.Parent;
    }

    [EnforcePure]
    public string? DirectoryNameMethod(FileInfo file)
    {
        return file.DirectoryName;
    }

    [EnforcePure]
    public string NameMethod(DirectoryInfo directory)
    {
        return directory.Name;
    }

    [EnforcePure]
    public string FileNameMethod(FileInfo file)
    {
        return file.Name;
    }

    [EnforcePure]
    public string ExtensionMethod(FileInfo file)
    {
        return file.Extension;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedProperties = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "directory.Parent" ||
                    node.ToString() == "file.DirectoryName" ||
                    node.ToString() == "directory.Name" ||
                    node.ToString() == "file.Name" ||
                    node.ToString() == "file.Extension")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var resolutions = trackedProperties.Select(property =>
            {
                var getter = ((IPropertySymbol)semanticModel.GetSymbolInfo(property).Symbol!).GetMethod!;
                var args = new object?[] { getter.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                var entry = args[2]!;
                var classification = matched
                    ? (string)entry.GetType().GetProperty("Classification")!.GetValue(entry)!
                    : string.Empty;
                return (property: property.ToString(), matched, classification);
            }).ToDictionary(result => result.property, result => (result.matched, result.classification), StringComparer.Ordinal);

            Assert.That(purityDiagnostics, Has.Length.EqualTo(3));
            Assert.That(
                purityDiagnostics.All(diagnostic =>
                    string.Equals(
                        diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty],
                        "generated_purity_summary",
                        StringComparison.Ordinal)),
                Is.True);

            Assert.That(resolutions["directory.Parent"].matched, Is.True);
            Assert.That(resolutions["directory.Parent"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["file.DirectoryName"].matched, Is.True);
            Assert.That(resolutions["file.DirectoryName"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["directory.Name"].matched, Is.True);
            Assert.That(resolutions["directory.Name"].classification, Is.EqualTo("impure"));
            Assert.That(resolutions["file.Name"].matched, Is.True);
            Assert.That(resolutions["file.Name"].classification, Is.EqualTo("impure"));
            Assert.That(resolutions["file.Extension"].matched, Is.True);
            Assert.That(resolutions["file.Extension"].classification, Is.EqualTo("impure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentGetFolderPathAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.None);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.GetFolderPath.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.GetFolderPath depends on OS profile/folder state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentGetEnvironmentVariableAsImpureEvidence()
        {
            const string source = @"
#nullable enable
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string? TestMethod()
    {
        return Environment.GetEnvironmentVariable(""PATH"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.GetEnvironmentVariable(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.GetEnvironmentVariable depends on ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentGetEnvironmentVariablesAsImpureEvidence()
        {
            const string source = @"
using System;
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IDictionary TestMethod()
    {
        return Environment.GetEnvironmentVariables();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.GetEnvironmentVariables().");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.GetEnvironmentVariables() depends on ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentGetEnvironmentVariablesWithTargetAsImpureEvidence()
        {
            const string source = @"
using System;
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IDictionary TestMethod()
    {
        return Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.GetEnvironmentVariables(EnvironmentVariableTarget).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.GetEnvironmentVariables(EnvironmentVariableTarget) depends on ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentExpandEnvironmentVariablesAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.ExpandEnvironmentVariables(""%PATH%"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single();
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.ExpandEnvironmentVariables(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.ExpandEnvironmentVariables(string) depends on ambient environment state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentCommandLineAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.CommandLine;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.CommandLine");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.CommandLine.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.CommandLine depends on ambient process state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentVersionAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Version TestMethod()
    {
        return Environment.Version;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.Version");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.Version.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.Version depends on runtime assembly metadata and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentProcessIdAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Environment.ProcessId;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.ProcessId");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.ProcessId.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.ProcessId depends on process/runtime state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentSystemDirectoryAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.SystemDirectory;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.SystemDirectory");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.SystemDirectory.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.SystemDirectory is OS-dependent path state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentUserInteractiveAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Environment.UserInteractive;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.UserInteractive");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.UserInteractive.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.UserInteractive depends on ambient process/UI state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EnvironmentUserNameAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Environment.UserName;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Environment.UserName");
            var propertySymbol = (IPropertySymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { propertySymbol.GetMethod!.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Environment.UserName.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Environment.UserName depends on ambient identity state and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_VersionPureMembers()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        var left = new Version(1, 2, 3, 4);
        var right = new Version(1, 2, 3, 4);
        return left.CompareTo(right) == 0 &&
            left.Equals(right) &&
            left.Major == 1;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var constructor = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .First(node => node.ToString() == "new Version(1, 2, 3, 4)"))
                .Symbol!;
            var compareTo = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .First(node => node.ToString() == "left.CompareTo(right)"))
                .Symbol!;
            var equals = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .First(node => node.ToString() == "left.Equals(right)"))
                .Symbol!;
            var majorGetter = ((IPropertySymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .First(node => node.ToString() == "left.Major"))
                .Symbol!).GetMethod!;
            var trackedMethods = new[]
            {
                constructor.OriginalDefinition,
                compareTo.OriginalDefinition,
                equals.OriginalDefinition,
                majorGetter.OriginalDefinition,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Version constructors, comparisons, and getters.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true, true }),
                "Generated purity catalog should resolve the tracked Version members to their runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_TypeGetTypeFromHandleAsPureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type TestMethod()
    {
        return Type.GetTypeFromHandle(default(RuntimeTypeHandle));
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "Type.GetTypeFromHandle(default(RuntimeTypeHandle))");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Type.GetTypeFromHandle(RuntimeTypeHandle).");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Type.GetTypeFromHandle(RuntimeTypeHandle) to its runtime implementation assembly.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ObjectGetTypeAndTypeToStringAsPureMetadataEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(object value)
    {
        return value.GetType().ToString();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "value.GetType()" ||
                    node.ToString() == "value.GetType().ToString()")
                .Select(node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow object.GetType() and Type.ToString() as metadata-only reads.");
            Assert.That(matched, Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve object.GetType() and Type.ToString() from runtime metadata evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ObjectReferenceEqualsTupleFactoriesAndArraySegmentConstructors()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values, object left, object right)
    {
        var same = object.ReferenceEquals(left, right);
        var whole = new ArraySegment<int>(values);
        var prefix = new ArraySegment<int>(values, 0, 1);
        var tuple = Tuple.Create(1, 2);
        var valueTuple = ValueTuple.Create(1, 2);
        return same ? 1 : values.Length;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "object.ReferenceEquals(left, right)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Single(node => node.ToString() == "new ArraySegment<int>(values)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Single(node => node.ToString() == "new ArraySegment<int>(values, 0, 1)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "Tuple.Create(1, 2)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "ValueTuple.Create(1, 2)"))
                    .Symbol!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow object.ReferenceEquals, ArraySegment constructors, Tuple.Create, and ValueTuple.Create.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true, true, true }),
                "Generated purity catalog should resolve the tracked ReferenceEquals, ArraySegment, Tuple, and ValueTuple members.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_PureCoreConstructorsAndValueTypes()
        {
            const string source = @"
using System;
using System.IO;
using System.Runtime.CompilerServices;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var argument = new ArgumentException(""bad argument"", ""value"");
        var divideByZero = new DivideByZeroException();
        var flags = new FlagsAttribute();
        var format = new FormatException(""bad format"");
        var index = new Index(2, false);
        var endOfStream = new EndOfStreamException();
        var invalidOperation = new InvalidOperationException(""bad operation"");
        var notImplemented = new NotImplementedException();
        var notSupported = new NotSupportedException(""unsupported"");
        var obsolete = new ObsoleteAttribute(""legacy"");
        var overflow = new OverflowException();
        var platformNotSupported = new PlatformNotSupportedException();
        var range = new Range(new Index(0, false), new Index(1, false));
        var callerArgument = new CallerArgumentExpressionAttribute(""value"");
        var methodImpl = new MethodImplAttribute(MethodImplOptions.AggressiveInlining);
        var serializable = new SerializableAttribute();
        var pointer = new UIntPtr(1u);
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "new ArgumentException(\"bad argument\", \"value\")",
                "new DivideByZeroException()",
                "new FlagsAttribute()",
                "new FormatException(\"bad format\")",
                "new Index(2, false)",
                "new EndOfStreamException()",
                "new InvalidOperationException(\"bad operation\")",
                "new NotImplementedException()",
                "new NotSupportedException(\"unsupported\")",
                "new ObsoleteAttribute(\"legacy\")",
                "new OverflowException()",
                "new PlatformNotSupportedException()",
                "new Range(new Index(0, false), new Index(1, false))",
                "new CallerArgumentExpressionAttribute(\"value\")",
                "new MethodImplAttribute(MethodImplOptions.AggressiveInlining)",
                "new SerializableAttribute()",
                "new UIntPtr(1u)",
            };
            var trackedMethods = trackedExpressions
                .Select(expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<ObjectCreationExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return (IMethodSymbol)symbol!;
                })
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the probed core constructors and value-type constructors.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedExpressions.Length).ToArray()),
                "Generated purity catalog should resolve the tracked core constructor members.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_AdditionalPureExceptionConstructors()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var argumentNull = new ArgumentNullException(""value"");
        var disposed = new ObjectDisposedException(""stream"");
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "new ArgumentNullException(\"value\")",
                "new ObjectDisposedException(\"stream\")",
            };
            var trackedMethods = trackedExpressions
                .Select(expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<ObjectCreationExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return (IMethodSymbol)symbol!;
                })
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the tracked exception constructors.");
            Assert.That(matched, Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve ArgumentNullException(string) and ObjectDisposedException(string).");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DateTimeStableGetterMembers()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(DateTime value)
    {
        var day = value.Day;
        var dayOfWeek = value.DayOfWeek;
        var dayOfYear = value.DayOfYear;
        var hour = value.Hour;
        var kind = value.Kind;
        var millisecond = value.Millisecond;
        var minute = value.Minute;
        var month = value.Month;
        var second = value.Second;
        var ticks = value.Ticks;
        var timeOfDay = value.TimeOfDay;
        return day;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "value.Day",
                "value.DayOfWeek",
                "value.DayOfYear",
                "value.Hour",
                "value.Kind",
                "value.Millisecond",
                "value.Minute",
                "value.Month",
                "value.Second",
                "value.Ticks",
                "value.TimeOfDay",
            };
            var trackedMethods = trackedExpressions
                .Select(expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return ((IPropertySymbol)symbol!).GetMethod!;
                })
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the tracked DateTime stable getters.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedExpressions.Length).ToArray()),
                "Generated purity catalog should resolve the tracked DateTime getter members.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_StringBuilderLengthAndHttpResponseSuccessStatusCode()
        {
            const string source = @"
using System.Net.Http;
using System.Text;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int BuilderLengthMethod(StringBuilder builder)
    {
        return builder.Length;
    }

    [EnforcePure]
    public bool HttpResponseMethod(HttpResponseMessage response)
    {
        return response.IsSuccessStatusCode;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "builder.Length",
                "response.IsSuccessStatusCode",
            };
            var trackedMethods = trackedExpressions
                .Select(expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return ((IPropertySymbol)symbol!).GetMethod!;
                })
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var resolutions = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                var purityEntry = args[2]!;
                var classification = matched
                    ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                    : string.Empty;
                return (matched, classification);
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow StringBuilder.Length and HttpResponseMessage.IsSuccessStatusCode.");
            Assert.That(resolutions.Select(result => result.matched).ToArray(), Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve StringBuilder.Length and HttpResponseMessage.IsSuccessStatusCode.");
            Assert.That(resolutions.Select(result => result.classification).ToArray(), Is.EqualTo(new[] { "pure", "pure" }),
                "Generated purity catalog should classify StringBuilder.Length and HttpResponseMessage.IsSuccessStatusCode as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_BooleanCompareAndCharClassificationHelpers()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(bool left, bool right, char value, char other)
    {
        var compare = left.CompareTo(right);
        var codePoint = char.ConvertToUtf32(value, other);
        var numeric = char.GetNumericValue(value);
        var isControl = char.IsControl(value);
        var isDigit = char.IsDigit(value);
        var isLetter = char.IsLetter(value);
        var isLower = char.IsLower(value);
        var isNumber = char.IsNumber(value);
        var isPunctuation = char.IsPunctuation(value);
        var isSeparator = char.IsSeparator(value);
        var isSymbol = char.IsSymbol(value);
        var isUpper = char.IsUpper(value);
        var isWhiteSpace = char.IsWhiteSpace(value);
        var lowerInvariant = char.ToLowerInvariant(value);
        var upperInvariant = char.ToUpperInvariant(value);
        return compare;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedExpressions = new[]
            {
                "left.CompareTo(right)",
                "char.ConvertToUtf32(value, other)",
                "char.GetNumericValue(value)",
                "char.IsControl(value)",
                "char.IsDigit(value)",
                "char.IsLetter(value)",
                "char.IsLower(value)",
                "char.IsNumber(value)",
                "char.IsPunctuation(value)",
                "char.IsSeparator(value)",
                "char.IsSymbol(value)",
                "char.IsUpper(value)",
                "char.IsWhiteSpace(value)",
                "char.ToLowerInvariant(value)",
                "char.ToUpperInvariant(value)",
            };
            var classifications = trackedExpressions.ToDictionary(
                expressionText => expressionText,
                expressionText =>
                {
                    var symbol = semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == expressionText))
                        .Symbol;
                    Assert.That(symbol, Is.Not.Null, expressionText);
                    return (IMethodSymbol)symbol!;
                });
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var resolutions = classifications.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var args = new object?[] { pair.Value.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2];
                    var classification = matched
                        ? (string)purityEntry!.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });
            var pureTrackedExpressions = trackedExpressions
                .Where(expressionText => expressionText != "char.ConvertToUtf32(value, other)")
                .ToArray();

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("char.ConvertToUtf32(char, char)"));
            foreach (var expressionText in pureTrackedExpressions)
            {
                Assert.That(resolutions[expressionText].matched, Is.True, "Generated purity catalog should resolve " + expressionText + ".");
                Assert.That(resolutions[expressionText].classification, Is.EqualTo("pure"),
                    expressionText + " should remain a generated pure helper.");
            }

            Assert.That(resolutions["char.ConvertToUtf32(value, other)"].matched, Is.True,
                "Generated purity catalog should resolve char.ConvertToUtf32(value, other).");
            Assert.That(resolutions["char.ConvertToUtf32(value, other)"].classification, Is.EqualTo("impure"),
                "char.ConvertToUtf32(char, char) now flows through generated impure evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CharConvertFromUtf32AsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(int codePoint)
    {
        return char.ConvertFromUtf32(codePoint);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "char.ConvertFromUtf32(codePoint)");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("char.ConvertFromUtf32(int)"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve char.ConvertFromUtf32(int).");
            Assert.That(classification, Is.EqualTo("impure"),
                "char.ConvertFromUtf32 can throw for invalid code points and should remain generated impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_IndexAndHashCodeHelpers()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        HashCode hash = default;
        var copy = new HashCode();
        var end = Index.End;
        var start = Index.Start;
        return hash.ToHashCode() + copy.ToHashCode();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var hashCode = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "hash.ToHashCode()"))
                .Symbol!;
            var copyHashCode = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "copy.ToHashCode()"))
                .Symbol!;
            var hashCodeConstructor = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .Single(node => node.ToString() == "new HashCode()"))
                .Symbol!;
            var endGetter = ((IPropertySymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Single(node => node.ToString() == "Index.End"))
                .Symbol!).GetMethod!;
            var startGetter = ((IPropertySymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<MemberAccessExpressionSyntax>()
                    .Single(node => node.ToString() == "Index.Start"))
                .Symbol!).GetMethod!;
            var trackedMethods = new[]
            {
                hashCodeConstructor.OriginalDefinition,
                hashCode.OriginalDefinition,
                copyHashCode.OriginalDefinition,
                endGetter.OriginalDefinition,
                startGetter.OriginalDefinition,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the implicit HashCode constructor, HashCode.ToHashCode, and Index getters.");
            Assert.That(matched, Is.EqualTo(new[] { true, true, true, true, true }),
                "Generated purity catalog should resolve the tracked index and hash helpers, including new HashCode().");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SpanAndMemoryMarshalHelpers()
        {
            const string source = @"
using System;
using System.Runtime.InteropServices;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ReadOnlySpan<int> readOnly, Span<int> writable)
    {
        var head = readOnly.Slice(0, 0);
        var readOnlyBytes = MemoryMarshal.AsBytes(readOnly);
        var writableBytes = MemoryMarshal.AsBytes(writable);
        return readOnly.Length + writable.Length + head.Length + readOnlyBytes.Length + writableBytes.Length + (readOnly.IsEmpty ? 0 : 1) + (writable.IsEmpty ? 0 : 1);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "readOnly.Slice(0, 0)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "MemoryMarshal.AsBytes(readOnly)"))
                    .Symbol!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "MemoryMarshal.AsBytes(writable)"))
                    .Symbol!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "readOnly.Length"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "writable.Length"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "readOnly.IsEmpty"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "writable.IsEmpty"))
                    .Symbol!).GetMethod!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the tracked span and MemoryMarshal helpers.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve the tracked span and MemoryMarshal helpers.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ReadOnlySequenceHelpersAndSlice()
        {
            const string source = @"
using System.Buffers;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ReadOnlySequence<int> value)
    {
        var start = value.Start;
        var end = value.End;
        var slice = value.Slice(1L);
        return value.IsEmpty ? 0 : value.Length > slice.Length ? 1 : 2;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "value.Start"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "value.End"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "value.Length"))
                    .Symbol!).GetMethod!,
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "value.IsEmpty"))
                    .Symbol!).GetMethod!,
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "value.Slice(1L)"))
                    .Symbol!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow the tracked ReadOnlySequence helpers and Slice(long).");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve the tracked ReadOnlySequence helpers and Slice(long).");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ListCapacity()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(List<int> values)
    {
        return values.Capacity;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                ((IPropertySymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Single(node => node.ToString() == "values.Capacity"))
                    .Symbol!).GetMethod!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow List<T>.Capacity.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve List<T>.Capacity.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ListFindIndex()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(List<int> values)
    {
        return values.FindIndex(static value => value > 0);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.FindIndex(static value => value > 0)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow List<T>.FindIndex.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve List<T>.FindIndex.");
            Assert.That(classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify List<T>.FindIndex as pure.");
        }

        [Test]
        public void GeneratedPurityCatalog_Resolves_QueueTryPeek()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Queue<int> values)
    {
        return values.TryPeek(out var value);
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.TryPeek(out var value)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Queue<T>.TryPeek.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify Queue<T>.TryPeek as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_EmailAddressAttributeConstructor()
        {
            const string source = @"
using System;
using System.ComponentModel.DataAnnotations;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var attribute = new EmailAddressAttribute();
        return attribute is null ? 0 : 1;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<ObjectCreationExpressionSyntax>()
                        .Single(node => node.ToString() == "new EmailAddressAttribute()"))
                    .Symbol!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow EmailAddressAttribute..ctor().");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve EmailAddressAttribute..ctor().");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DateTimeToFileTimeAndMemberwiseCloneAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class CloneableSample
{
    [EnforcePure]
    public object CloneSelf()
    {
        return MemberwiseClone();
    }
}

public class TestClass
{
    [EnforcePure]
    public long ToFileTimeMethod(DateTime value)
    {
        return value.ToFileTime();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "MemberwiseClone()" ||
                    node.ToString() == "value.ToFileTime()")
                .Select(node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var resolutions = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                var purityEntry = args[2]!;
                var classification = matched
                    ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                    : string.Empty;
                return (matched, classification);
            }).ToArray();
            var impuritySymbols = purityDiagnostics
                .Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty])
                .ToArray();

            Assert.That(purityDiagnostics, Has.Length.EqualTo(2));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "MethodInvocationPurityRule" }));
            Assert.That(impuritySymbols, Has.Some.Contain("System.DateTime.ToFileTime()"));
            Assert.That(impuritySymbols, Has.Some.Contain("MemberwiseClone"));
            Assert.That(resolutions.Select(result => result.matched).ToArray(), Is.EqualTo(new[] { true, true }),
                "Generated purity catalog should resolve DateTime.ToFileTime and MemberwiseClone.");
            Assert.That(resolutions.Select(result => result.classification).ToArray(), Is.EqualTo(new[] { "impure", "impure" }),
                "Generated purity catalog should classify DateTime.ToFileTime and MemberwiseClone as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CoreComponentModelAttributeConstructors()
        {
            const string source = @"
using System;
using System.ComponentModel;
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        var browsable = new BrowsableAttribute(true);
        var description = new DescriptionAttribute(""sample"");
        var conditional = new ConditionalAttribute(""DEBUG"");
        return (browsable is null ? 0 : 1) + (description is null ? 0 : 1) + (conditional is null ? 0 : 1);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "new BrowsableAttribute(true)" ||
                    node.ToString() == "new DescriptionAttribute(\"sample\")" ||
                    node.ToString() == "new ConditionalAttribute(\"DEBUG\")")
                .Select(node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow these component-model attribute constructors.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve BrowsableAttribute, DescriptionAttribute, and ConditionalAttribute constructors.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_RegularExpressionAttributeConstructorAsImpureEvidence()
        {
            const string source = @"
using System;
using System.ComponentModel.DataAnnotations;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public RegularExpressionAttribute TestMethod()
    {
        return new RegularExpressionAttribute(""^[a-z]+$"");
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<ObjectCreationExpressionSyntax>()
                    .Single(node => node.ToString() == "new RegularExpressionAttribute(\"^[a-z]+$\")"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(
                diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty],
                Does.Contain("System.ComponentModel.DataAnnotations.RegularExpressionAttribute.RegularExpressionAttribute(string)"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve RegularExpressionAttribute..ctor(string).");
            Assert.That(classification, Is.EqualTo("impure"),
                "RegularExpressionAttribute..ctor(string) now resolves through generated impure runtime evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_CoreDataAnnotationsConstructorsAsImpureEvidence()
        {
            const string source = @"
using System;
using System.ComponentModel.DataAnnotations;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public RequiredAttribute RequiredMethod()
    {
        return new RequiredAttribute();
    }

    [EnforcePure]
    public StringLengthAttribute StringLengthMethod()
    {
        return new StringLengthAttribute(10);
    }

    [EnforcePure]
    public RangeAttribute RangeMethod()
    {
        return new RangeAttribute(0d, 1d);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "new RequiredAttribute()" ||
                    node.ToString() == "new StringLengthAttribute(10)" ||
                    node.ToString() == "new RangeAttribute(0d, 1d)")
                .ToDictionary(
                    node => node.ToString(),
                    node => (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!);
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var resolutions = trackedMethods.ToDictionary(
                pair => pair.Key,
                pair =>
                {
                    var args = new object?[] { pair.Value.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });
            var impuritySymbols = purityDiagnostics
                .Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty])
                .ToArray();

            Assert.That(purityDiagnostics, Has.Length.EqualTo(3));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "generated_purity_summary" }));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "ObjectCreationPurityRule" }));
            Assert.That(impuritySymbols, Has.Some.Contain("System.ComponentModel.DataAnnotations.RequiredAttribute.RequiredAttribute()"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.ComponentModel.DataAnnotations.StringLengthAttribute.StringLengthAttribute(int)"));
            Assert.That(impuritySymbols, Has.Some.Contain("System.ComponentModel.DataAnnotations.RangeAttribute.RangeAttribute(double, double)"));
            Assert.That(resolutions["new RequiredAttribute()"].matched, Is.True,
                "Generated purity catalog should resolve RequiredAttribute..ctor().");
            Assert.That(resolutions["new RequiredAttribute()"].classification, Is.EqualTo("impure"),
                "RequiredAttribute..ctor() now resolves through generated impure runtime evidence.");
            Assert.That(resolutions["new StringLengthAttribute(10)"].matched, Is.True,
                "Generated purity catalog should resolve StringLengthAttribute..ctor(int).");
            Assert.That(resolutions["new StringLengthAttribute(10)"].classification, Is.EqualTo("impure"),
                "StringLengthAttribute..ctor(int) now resolves through generated impure runtime evidence.");
            Assert.That(resolutions["new RangeAttribute(0d, 1d)"].matched, Is.True,
                "Generated purity catalog should resolve RangeAttribute..ctor(double, double).");
            Assert.That(resolutions["new RangeAttribute(0d, 1d)"].classification, Is.EqualTo("impure"),
                "RangeAttribute..ctor(double, double) now resolves through generated impure runtime evidence.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DecimalNegate()
        {
            const string source = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(decimal value)
    {
        var negated = decimal.Negate(value);
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new IMethodSymbol[]
            {
                (IMethodSymbol)semanticModel.GetSymbolInfo(
                    syntaxTree.GetRoot()
                        .DescendantNodes()
                        .OfType<InvocationExpressionSyntax>()
                        .Single(node => node.ToString() == "decimal.Negate(value)"))
                    .Symbol!,
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve decimal.Negate.");
            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow decimal.Negate.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_DecimalComparisonAndConversionsAsMixedEvidence()
        {
            const string source = @"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Compare(decimal left, decimal right)
    {
        return decimal.Compare(left, right);
    }

    [EnforcePure]
    public double Convert(decimal value)
    {
        return decimal.ToDouble(value);
    }

    [EnforcePure]
    public int Narrow(decimal value)
    {
        return decimal.ToInt32(value);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "decimal.Compare(left, right)" ||
                    node.ToString() == "decimal.ToDouble(value)" ||
                    node.ToString() == "decimal.ToInt32(value)")
                .Select(node => (invocation: node.ToString(), method: (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!))
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var resolutions = trackedMethods
                .Select(trackedMethod =>
                {
                    var args = new object?[] { trackedMethod.method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2];
                    var classification = matched
                        ? (string)purityEntry!.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (trackedMethod.invocation, matched, classification);
                })
                .ToDictionary(
                    result => result.invocation,
                    result => (result.matched, result.classification),
                    StringComparer.Ordinal);

            Assert.That(purityDiagnostics, Has.Length.EqualTo(1));
            Assert.That(
                purityDiagnostics[0].Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty],
                Is.EqualTo("generated_purity_summary"));
            Assert.That(
                purityDiagnostics[0].Properties[PurelySharpDiagnostics.ImpuritySymbolProperty],
                Does.Contain("decimal.ToInt32(decimal)"));

            Assert.That(resolutions["decimal.Compare(left, right)"].matched, Is.True);
            Assert.That(resolutions["decimal.Compare(left, right)"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["decimal.ToDouble(value)"].matched, Is.True);
            Assert.That(resolutions["decimal.ToDouble(value)"].classification, Is.EqualTo("pure"));
            Assert.That(resolutions["decimal.ToInt32(value)"].matched, Is.True);
            Assert.That(resolutions["decimal.ToInt32(value)"].classification, Is.EqualTo("impure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayFindAndFindIndex()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int FindMethod(int[] values)
    {
        return Array.Find(values, static value => value > 0);
    }

    [EnforcePure]
    public int FindIndexMethod(int[] values)
    {
        return Array.FindIndex(values, static value => value > 0);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocationNodes = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Array.Find(values, static value => value > 0)" ||
                    node.ToString() == "Array.FindIndex(values, static value => value > 0)")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = invocationNodes.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var method = (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Count(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.EqualTo(1),
                "Runtime-derived array summaries should keep Array.Find impure while allowing Array.FindIndex through generated purity evidence.");
            Assert.That(classifications["Array.Find(values, static value => value > 0)"].matched, Is.True,
                "Generated purity catalog should resolve Array.Find.");
            Assert.That(classifications["Array.Find(values, static value => value > 0)"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify Array.Find as impure.");
            Assert.That(classifications["Array.FindIndex(values, static value => value > 0)"].matched, Is.True,
                "Generated purity catalog should resolve Array.FindIndex.");
            Assert.That(classifications["Array.FindIndex(values, static value => value > 0)"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify Array.FindIndex as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayExistsAndTrueForAll()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool ExistsMethod(int[] values)
    {
        return Array.Exists(values, static value => value > 0);
    }

    [EnforcePure]
    public bool TrueForAllMethod(int[] values)
    {
        return Array.TrueForAll(values, static value => value > 0);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocationNodes = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Array.Exists(values, static value => value > 0)" ||
                    node.ToString() == "Array.TrueForAll(values, static value => value > 0)")
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = invocationNodes.ToDictionary(
                node => node.ToString(),
                node =>
                {
                    var method = (IMethodSymbol)semanticModel.GetSymbolInfo(node).Symbol!;
                    var args = new object?[] { method.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = matched
                        ? (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!
                        : string.Empty;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Generated array predicate summaries should allow Exists and TrueForAll without the manual pure catalog.");
            Assert.That(classifications["Array.Exists(values, static value => value > 0)"].matched, Is.True,
                "Generated purity catalog should resolve Array.Exists.");
            Assert.That(classifications["Array.Exists(values, static value => value > 0)"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify Array.Exists as pure.");
            Assert.That(classifications["Array.TrueForAll(values, static value => value > 0)"].matched, Is.True,
                "Generated purity catalog should resolve Array.TrueForAll.");
            Assert.That(classifications["Array.TrueForAll(values, static value => value > 0)"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify Array.TrueForAll as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayIndexOfAndLength()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values, object target)
    {
        return Array.IndexOf(values, target) + values.Length;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<SyntaxNode>()
                .Where(node =>
                    node is InvocationExpressionSyntax invocation && invocation.ToString() == "Array.IndexOf(values, target)" ||
                    node is MemberAccessExpressionSyntax memberAccess && memberAccess.ToString() == "values.Length")
                .Select(node => node switch
                {
                    InvocationExpressionSyntax => semanticModel.GetSymbolInfo(node).Symbol as IMethodSymbol,
                    MemberAccessExpressionSyntax => (semanticModel.GetSymbolInfo(node).Symbol as IPropertySymbol)?.GetMethod,
                    _ => null,
                })
                .Where(method => method is not null)
                .Select(method => method!)
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(trackedMethods, Has.Length.EqualTo(2));
            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Array.IndexOf and Array.Length.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve Array.IndexOf and Array.Length.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayGetLengthAsImpureEvidence()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values)
    {
        return values.GetLength(0);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.GetLength(0)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Generated runtime summary should report Array.GetLength as potentially throwing.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Array.GetLength.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify Array.GetLength as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayGetEnumerator()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values)
    {
        _ = values.GetEnumerator();
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.GetEnumerator()"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Array.GetEnumerator.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Array.GetEnumerator.");
            Assert.That(classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify Array.GetEnumerator as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ContractHelpers()
        {
            const string source = @"
using System.Diagnostics.Contracts;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(bool condition)
    {
        Contract.Requires(condition);
        Contract.Ensures(condition);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Where(node =>
                    node.ToString() == "Contract.Requires(condition)" ||
                    node.ToString() == "Contract.Ensures(condition)")
                .Select(node => semanticModel.GetSymbolInfo(node).Symbol)
                .OfType<IMethodSymbol>()
                .ToArray();
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var matched = trackedMethods.Select(method =>
            {
                var args = new object?[] { method.OriginalDefinition, compilation, null };
                return (bool)tryGetPurity.Invoke(catalog, args)!;
            }).ToArray();

            Assert.That(trackedMethods, Has.Length.EqualTo(2));
            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow Contract.Requires and Contract.Ensures.");
            Assert.That(matched, Is.EqualTo(Enumerable.Repeat(true, trackedMethods.Length).ToArray()),
                "Generated purity catalog should resolve Contract.Requires and Contract.Ensures.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_ArrayBinarySearch()
        {
            const string source = @"
using System;
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values, object target, IComparer comparer)
    {
        return Array.BinarySearch(values, target, comparer);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "Array.BinarySearch(values, target, comparer)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Runtime-derived array binary search summary should no longer allow Array.BinarySearch as a hard-coded pure helper.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve Array.BinarySearch.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify Array.BinarySearch as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SortedSetGetViewBetween()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedSet<int> values, int lower, int upper)
    {
        values.GetViewBetween(lower, upper);
        return 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethod = (IMethodSymbol)semanticModel.GetSymbolInfo(
                syntaxTree.GetRoot()
                    .DescendantNodes()
                    .OfType<InvocationExpressionSyntax>()
                    .Single(node => node.ToString() == "values.GetViewBetween(lower, upper)"))
                .Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { trackedMethod.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Runtime-derived SortedSet summary should no longer allow GetViewBetween as a hard-coded pure helper.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve SortedSet<T>.GetViewBetween.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Generated purity catalog should classify SortedSet<T>.GetViewBetween as impure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SortedListAndLinkedListReadHelpers()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedList<int, int> values, int key, LinkedListNode<int> node)
    {
        return values.IndexOfKey(key) + node.Value;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.SortedList<TKey, TValue>.IndexOfKey(TKey)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.IndexOfKey(key)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.LinkedListNode<T>.Value.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "node.Value"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow SortedList<TKey, TValue>.IndexOfKey and LinkedListNode<T>.Value.");
            Assert.That(classifications["System.Collections.Generic.SortedList<TKey, TValue>.IndexOfKey(TKey)"].matched, Is.True,
                "Generated purity catalog should resolve SortedList<TKey, TValue>.IndexOfKey.");
            Assert.That(classifications["System.Collections.Generic.SortedList<TKey, TValue>.IndexOfKey(TKey)"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify SortedList<TKey, TValue>.IndexOfKey as pure.");
            Assert.That(classifications["System.Collections.Generic.LinkedListNode<T>.Value.get"].matched, Is.True,
                "Generated purity catalog should resolve LinkedListNode<T>.Value.get.");
            Assert.That(classifications["System.Collections.Generic.LinkedListNode<T>.Value.get"].classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify LinkedListNode<T>.Value.get as pure.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_KeyValuePairCtorAndAccessors()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass<TKey, TValue>
{
    [EnforcePure]
    public TValue TestMethod(KeyValuePair<TKey, TValue> pair, TKey key, TValue value)
    {
        var created = new KeyValuePair<TKey, TValue>(key, value);
        _ = pair.Key;
        return created.Value;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.KeyValuePair<TKey, TValue>.KeyValuePair(TKey, TValue)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<ObjectCreationExpressionSyntax>()
                            .Single(node => node.ToString() == "new KeyValuePair<TKey, TValue>(key, value)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.KeyValuePair<TKey, TValue>.Key.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "pair.Key"))
                        .Symbol!).GetMethod!),
                (
                    "System.Collections.Generic.KeyValuePair<TKey, TValue>.Value.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "created.Value"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow KeyValuePair<TKey, TValue> ctor and accessors.");
            foreach (var label in classifications.Keys)
            {
                Assert.That(classifications[label].matched, Is.True,
                    "Generated purity catalog should resolve " + label + ".");
                Assert.That(classifications[label].classification, Is.EqualTo("pure"),
                    "Generated purity catalog should classify " + label + " as pure.");
            }
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SortedDictionaryLookupHelpers()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(SortedDictionary<int, string> values, int key, string target)
    {
        return values.ContainsKey(key) &&
            values.ContainsValue(target) &&
            values.TryGetValue(key, out var resolved) &&
            resolved == target;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsKey(TKey)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.ContainsKey(key)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsValue(TValue)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.ContainsValue(target)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.SortedDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.TryGetValue(key, out var resolved)"))
                        .Symbol!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = trackedMethods.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "SortedDictionary lookup helpers should stay semantically pure for builtin keys and values after removing the static pure catalog entries.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsKey(TKey)"].matched, Is.True,
                "Generated purity catalog should resolve SortedDictionary<TKey, TValue>.ContainsKey.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsKey(TKey)"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should capture the runtime summary classification for SortedDictionary<TKey, TValue>.ContainsKey.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsValue(TValue)"].matched, Is.True,
                "Generated purity catalog should resolve SortedDictionary<TKey, TValue>.ContainsValue.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.ContainsValue(TValue)"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should capture the runtime summary classification for SortedDictionary<TKey, TValue>.ContainsValue.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)"].matched, Is.True,
                "Generated purity catalog should resolve SortedDictionary<TKey, TValue>.TryGetValue.");
            Assert.That(classifications["System.Collections.Generic.SortedDictionary<TKey, TValue>.TryGetValue(TKey, out TValue)"].classification, Is.EqualTo("impure"),
                "Generated purity catalog should capture the runtime summary classification for SortedDictionary<TKey, TValue>.TryGetValue.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_InterfaceCollectionLookupHelpers()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ICollection<int> collection, IList<int> list, int value)
    {
        return collection.Contains(value) && list.IndexOf(value) >= 0 && collection.Count >= 0;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, ISymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.ICollection<T>.Contains(T)",
                    (ISymbol)(IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "collection.Contains(value)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.IList<T>.IndexOf(T)",
                    (ISymbol)(IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "list.IndexOf(value)"))
                        .Symbol!),
                (
                    "System.Collections.Generic.ICollection<T>.Count.get",
                    (ISymbol)((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "collection.Count"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var methodSymbol = (IMethodSymbol)entry.Symbol;
                    var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Unknown ICollection<T> contract dispatch should become conservative once the static pure fallback is removed.");
            Assert.That(classifications["System.Collections.Generic.ICollection<T>.Contains(T)"].matched, Is.True,
                "Generated purity catalog should resolve ICollection<T>.Contains.");
            Assert.That(classifications["System.Collections.Generic.ICollection<T>.Contains(T)"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify ICollection<T>.Contains as conservative_unknown.");
            Assert.That(classifications["System.Collections.Generic.ICollection<T>.Count.get"].matched, Is.True,
                "Generated purity catalog should resolve ICollection<T>.Count.get.");
            Assert.That(classifications["System.Collections.Generic.ICollection<T>.Count.get"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify ICollection<T>.Count.get as conservative_unknown.");
            Assert.That(classifications["System.Collections.Generic.IList<T>.IndexOf(T)"].matched, Is.True,
                "Generated purity catalog should resolve IList<T>.IndexOf.");
            Assert.That(classifications["System.Collections.Generic.IList<T>.IndexOf(T)"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify IList<T>.IndexOf as conservative_unknown.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_InterfaceEnumeratorContracts()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(IEnumerable<int> values, IEnumerator<int> enumerator)
    {
        _ = values.GetEnumerator();
        return enumerator.Current;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMembers = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Generic.IEnumerable<T>.GetEnumerator()",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.GetEnumerator()"))
                        .Symbol!),
                (
                    "System.Collections.Generic.IEnumerator<T>.Current.get",
                    ((IPropertySymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<MemberAccessExpressionSyntax>()
                            .Single(node => node.ToString() == "enumerator.Current"))
                        .Symbol!).GetMethod!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = trackedMembers.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "Unknown IEnumerable<T>/IEnumerator<T> contract dispatch should become conservative once the static pure fallback is removed.");
            Assert.That(classifications["System.Collections.Generic.IEnumerable<T>.GetEnumerator()"].matched, Is.True,
                "Generated purity catalog should resolve IEnumerable<T>.GetEnumerator.");
            Assert.That(classifications["System.Collections.Generic.IEnumerable<T>.GetEnumerator()"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify IEnumerable<T>.GetEnumerator as conservative_unknown.");
            Assert.That(classifications["System.Collections.Generic.IEnumerator<T>.Current.get"].matched, Is.True,
                "Generated purity catalog should resolve IEnumerator<T>.Current.get.");
            Assert.That(classifications["System.Collections.Generic.IEnumerator<T>.Current.get"].classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify IEnumerator<T>.Current.get as conservative_unknown.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_HashtableCompareInfoAndSortedListHelpersAsMixedEvidence()
        {
            const string source = @"
using System.Collections;
using System.Globalization;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool ContainsKey(Hashtable values, object key)
    {
        return values.ContainsKey(key);
    }

    [EnforcePure]
    public int Compare(CompareInfo compareInfo, string left, string right)
    {
        return compareInfo.Compare(left, right);
    }

    [EnforcePure]
    public object GetKey(SortedList values, int index)
    {
        return values.GetKey(index);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var purityDiagnostics = diagnostics
                .Where(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .ToArray();
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var trackedMethods = new (string Label, IMethodSymbol Symbol)[]
            {
                (
                    "System.Collections.Hashtable.ContainsKey(object)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.ContainsKey(key)"))
                        .Symbol!),
                (
                    "System.Globalization.CompareInfo.Compare(string, string)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "compareInfo.Compare(left, right)"))
                        .Symbol!),
                (
                    "System.Collections.SortedList.GetKey(int)",
                    (IMethodSymbol)semanticModel.GetSymbolInfo(
                        syntaxTree.GetRoot()
                            .DescendantNodes()
                            .OfType<InvocationExpressionSyntax>()
                            .Single(node => node.ToString() == "values.GetKey(index)"))
                        .Symbol!),
            };
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var classifications = trackedMethods.ToDictionary(
                entry => entry.Label,
                entry =>
                {
                    var args = new object?[] { entry.Symbol.OriginalDefinition, compilation, null };
                    var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
                    var purityEntry = args[2]!;
                    var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;
                    return (matched, classification);
                });

            Assert.That(purityDiagnostics, Has.Length.EqualTo(2));
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty]).Distinct().ToArray(),
                Is.EqualTo(new[] { "unknown_external_call" }),
                "Open virtual dispatch on overridable Hashtable/SortedList members should remain conservative even though the base methods have reviewed generated summaries.");
            Assert.That(
                purityDiagnostics.Select(diagnostic => diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty]).OrderBy(symbol => symbol, StringComparer.Ordinal).ToArray(),
                Is.EqualTo(new[]
                {
                    "virtual System.Collections.Hashtable.ContainsKey(object)",
                    "virtual System.Collections.SortedList.GetKey(int)",
                }));
            Assert.That(
                purityDiagnostics.All(diagnostic => !diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.ImpurityCatalogSourceProperty)),
                Is.True,
                "These diagnostics should come from conservative open virtual dispatch, not from directly trusting the base-method runtime summary at the call site.");

            Assert.That(classifications["System.Collections.Hashtable.ContainsKey(object)"].matched, Is.True);
            Assert.That(classifications["System.Collections.Hashtable.ContainsKey(object)"].classification, Is.EqualTo("impure"));
            Assert.That(classifications["System.Globalization.CompareInfo.Compare(string, string)"].matched, Is.True);
            Assert.That(classifications["System.Globalization.CompareInfo.Compare(string, string)"].classification, Is.EqualTo("pure"));
            Assert.That(classifications["System.Collections.SortedList.GetKey(int)"].matched, Is.True);
            Assert.That(classifications["System.Collections.SortedList.GetKey(int)"].classification, Is.EqualTo("impure"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_KeyedCollectionContains()
        {
            const string source = @"
using System.Collections.ObjectModel;
using PurelySharp.Attributes;

public sealed class NameCollection : KeyedCollection<string, string>
{
    protected override string GetKeyForItem(string item) => item;
}

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(NameCollection values, string key)
    {
        return values.Contains(key);
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString() == "values.Contains(key)");
            var methodSymbol = (IMethodSymbol)semanticModel.GetSymbolInfo(invocation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.True,
                "KeyedCollection<TKey, TItem>.Contains should no longer be globally trusted as pure when it dispatches through runtime hooks.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve KeyedCollection<TKey, TItem>.Contains(TKey).");
            Assert.That(classification, Is.EqualTo("conservative_unknown"),
                "Generated purity catalog should classify KeyedCollection<TKey, TItem>.Contains(TKey) as conservative_unknown.");
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_SortedDictionaryCount()
        {
            const string source = @"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedDictionary<int, string> values)
    {
        return values.Count;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "values.Count");
            var propertySymbol = (IPropertySymbol)semanticModel.GetSymbolInfo(memberAccess).Symbol!;
            var getter = propertySymbol.GetMethod!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { getter.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow SortedDictionary<TKey, TValue>.Count.");
            Assert.That(matched, Is.True,
                "Generated purity catalog should resolve SortedDictionary<TKey, TValue>.Count.get.");
            Assert.That(classification, Is.EqualTo("pure"),
                "Generated purity catalog should classify SortedDictionary<TKey, TValue>.Count.get as pure.");
        }

        [Test]
        public async Task Ps0002_ConservativeUnknownGeneratedPurity_SuppressesKnownPureMethodFallback()
        {
            const string source = @"
using System;
using System.Security.Cryptography;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}";

            const string metadataSymbol = "System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan`1<byte>, System.ReadOnlySpan`1<byte>)";
            const string displaySymbol = "System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan<byte>, System.ReadOnlySpan<byte>)";
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.Cryptography.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(CryptographicOperations).Assembly.Location,
                            metadataSymbol,
                            "conservative_unknown",
                            "[\"dynamic_dispatch\"]",
                            displaySymbol))));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Security.Cryptography.CryptographicOperations.FixedTimeEquals"));
        }

        [Test]
        public async Task Ps0002_ConservativeUnknownGeneratedPurity_SuppressesKnownPurePropertyFallback()
        {
            const string source = @"
using System.Globalization;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public CultureInfo TestMethod()
    {
        return CultureInfo.InvariantCulture;
    }
}";

            const string symbol = "System.Globalization.CultureInfo.get_InvariantCulture()";
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.Globalization.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(CultureInfo).Assembly.Location,
                            symbol,
                            "conservative_unknown",
                            "[\"dynamic_dispatch\"]"))));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unknown_external_call"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("no_body"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("CultureInfo.InvariantCulture.get"));
        }

        [Test]
        public async Task Ps0002_ConservativeUnknownGeneratedPurity_SuppressesKnownPureConstructorFallback()
        {
            const string source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        ReadOnlySpan<char> value = ""alpha"".AsSpan();
        return new string(value);
    }
}";

            const string metadataSymbol = "System.String..ctor(System.ReadOnlySpan`1<char>)";
            const string displaySymbol = "string.String(System.ReadOnlySpan<char>)";
            var diagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                additionalFiles: ImmutableArray.Create<AdditionalText>(
                    new InMemoryAdditionalText(
                        "Synthetic.StringConstructor.PurelySharp.EffectSummary.json",
                        CreatePuritySummaryJson(
                            typeof(string).Assembly.Location,
                            metadataSymbol,
                            "conservative_unknown",
                            "[\"dynamic_dispatch\"]",
                            displaySymbol))));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.ImpurityCategoryProperty), Is.True);
        }

        [Test]
        public async Task Ps0002_AppContextSetSwitch_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        AppContext.SetSwitch(""System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"", true);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.AppContext.SetSwitch"));
        }

        [Test]
        public async Task Ps0002_ListFindLast_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(List<int> values)
    {
        return values.FindLast(static value => value > 0);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("caller_visible_memory_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.List<T>.FindLast(System.Predicate<T>)"));
        }

        [Test]
        public async Task Ps0002_QueueTryPeek_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod(Queue<int> values)
    {
        return values.TryPeek(out var value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("caller_visible_memory_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.Queue<T>.TryPeek(out T)"));
        }

        [Test]
        public async Task Ps0002_ArrayFind_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int[] values)
    {
        return Array.Find(values, static value => value > 0);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("caller_visible_memory_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.Find"));
        }

        [Test]
        public async Task Ps0002_ArrayBinarySearch_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Collections;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Array values, object target, IComparer comparer)
    {
        return Array.BinarySearch(values, target, comparer);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Array.BinarySearch"));
        }

        [Test]
        public async Task Ps0002_SortedSetGetViewBetween_NoLongerUsesManualPureFallback()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(SortedSet<int> values, int lower, int upper)
    {
        values.GetViewBetween(lower, upper);
        return 0;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.SortedSet<int>.GetViewBetween"));
        }

        [Test]
        public async Task Ps0002_AppDomainCurrentDomain_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public AppDomain TestMethod()
    {
        return AppDomain.CurrentDomain;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.AppDomain.CurrentDomain.get"));
        }

        [Test]
        public async Task Ps0002_AppDomainBaseDirectoryOnParameter_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(AppDomain domain)
    {
        return domain.BaseDirectory;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.AppDomain.BaseDirectory.get"));
        }

        [Test]
        public async Task Ps0002_AppDomainFriendlyNameOnParameter_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(AppDomain domain)
    {
        return domain.FriendlyName;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.AppDomain.FriendlyName.get"));
        }

        [Test]
        public async Task Ps0002_StopwatchGetTimestamp_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        return Stopwatch.GetTimestamp();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch.GetTimestamp"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_StopwatchConstructorAsPureEvidence()
        {
            const string source = @"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Stopwatch TestMethod()
    {
        return new Stopwatch();
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var objectCreation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ObjectCreationExpressionSyntax>()
                .Single(node => node.ToString() == "new Stopwatch()");
            var methodSymbol = (IMethodSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(objectCreation).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetPurity = catalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(
                diagnostics.Any(candidate => candidate.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Trusted generated purity should allow new Stopwatch() when the constructor only initializes fresh-owned state.");
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Stopwatch..ctor().");
            Assert.That(classification, Is.EqualTo("pure"));
        }

        [Test]
        public async Task Ps0002_StopwatchElapsedTicksOnParameter_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod(Stopwatch stopwatch)
    {
        return stopwatch.ElapsedTicks;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch.ElapsedTicks.get"));
        }

        [Test]
        public async Task Ps0002_StopwatchStartOnParameter_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Stopwatch stopwatch)
    {
        stopwatch.Start();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch.Start"));
        }

        [Test]
        public async Task GeneratedPurityCatalog_Resolves_StopwatchStaticFieldFromStaticConstructorEvidence()
        {
            const string source = @"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        return Stopwatch.Frequency;
    }
}";

            var diagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "GeneratedPurityProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences().Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var memberAccess = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Single(node => node.ToString() == "Stopwatch.Frequency");
            var fieldSymbol = (IFieldSymbol)compilation.GetSemanticModel(syntaxTree).GetSymbolInfo(memberAccess).Symbol!;
            var catalogType = typeof(PurelySharpAnalyzer).Assembly.GetType("PurelySharp.Analyzer.GeneratedPurityCatalog", throwOnError: true)!;
            var fromOptions = catalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;
            var tryGetFieldPurity = catalogType.GetMethod("TryGetFieldPurity", BindingFlags.Public | BindingFlags.Instance)!;
            var catalog = fromOptions.Invoke(null, new object[] { new AnalyzerOptions(ImmutableArray<AdditionalText>.Empty), CancellationToken.None })!;
            var args = new object?[] { fieldSymbol.OriginalDefinition, compilation, null };
            var matched = (bool)tryGetFieldPurity.Invoke(catalog, args)!;
            var purityEntry = args[2]!;
            var classification = (string)purityEntry.GetType().GetProperty("Classification")!.GetValue(purityEntry)!;

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("FieldReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(matched, Is.True, "Generated purity catalog should resolve Stopwatch static fields from the runtime static constructor.");
            Assert.That(classification, Is.EqualTo("impure"),
                "Stopwatch static fields should derive impurity from the runtime static constructor.");
        }

        [Test]
        public async Task Ps0002_StopwatchFrequency_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public long TestMethod()
    {
        return Stopwatch.Frequency;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("FieldReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch"));
        }

        [Test]
        public async Task Ps0002_StopwatchIsHighResolution_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Diagnostics;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public bool TestMethod()
    {
        return Stopwatch.IsHighResolution;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("FieldReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Diagnostics.Stopwatch"));
        }

        [Test]
        public async Task Ps0002_TimeProviderSystem_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeProvider TestMethod()
    {
        return TimeProvider.System;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("global_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.TimeProvider.System.get"));
        }

        [Test]
        public async Task Ps0002_TimeProviderLocalTimeZoneOnParameter_UsesDynamicDispatchEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeZoneInfo TestMethod(TimeProvider provider)
    {
        return provider.LocalTimeZone;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties.ContainsKey(PurelySharpDiagnostics.ImpurityCatalogSourceProperty), Is.False);
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.TimeProvider.LocalTimeZone.get"));
        }

        [Test]
        public async Task Ps0002_TimeZoneInfoFindSystemTimeZoneById_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public TimeZoneInfo TestMethod()
    {
        return TimeZoneInfo.FindSystemTimeZoneById(""UTC"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.TimeZoneInfo.FindSystemTimeZoneById"));
        }

        [Test]
        public async Task Ps0002_GuidNewGuid_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Guid TestMethod()
    {
        return Guid.NewGuid();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Guid.NewGuid"));
        }

        [Test]
        public async Task Ps0002_PathGetFullPath_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(string value)
    {
        return Path.GetFullPath(value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Path.GetFullPath"));
        }

        [Test]
        public async Task Ps0002_PathGetTempPath_UsesGeneratedPurityCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.IO;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return Path.GetTempPath();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("impure_callee"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generated_purity_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.IO.Path.GetTempPath"));
        }

        [Test]
        public void InvariantCultureDeterministicParseHelper_Recognizes_TimeSpanSpanParseExact()
        {
            const string source = @"
#nullable enable
using System;
using System.Globalization;

public static class TestClass
{
    public static TimeSpan TestMethod(string value)
    {
        ReadOnlySpan<char> span = value.AsSpan();
        return TimeSpan.ParseExact(span, ""c"", CultureInfo.InvariantCulture, TimeSpanStyles.None);
    }
}";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "DeterministicParseProbe",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var invocation = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<InvocationExpressionSyntax>()
                .Single(node => node.ToString().Contains("TimeSpan.ParseExact", StringComparison.Ordinal));
            var operation = (IInvocationOperation)semanticModel.GetOperation(invocation)!;
            var engineType = typeof(PurelySharpAnalyzer).Assembly.GetType(
                "PurelySharp.Analyzer.Engine.PurityAnalysisEngine",
                throwOnError: true)!;
            var helper = engineType.GetMethod(
                "IsInvariantCultureDeterministicParseInvocation",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var matched = (bool)helper.Invoke(null, new object[] { operation })!;

            Assert.That(matched, Is.True, "The deterministic parse helper should recognize span ParseExact with InvariantCulture and None styles.");
        }

        [Test]
        public async Task Ps0002_CurrentCultureNumericParse_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public double TestMethod(string value)
    {
        return double.Parse(value);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("double.Parse"));
        }

        [Test]
        public async Task Ps0002_CurrentCultureNumericFormat_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(double value)
    {
        return value.ToString(""N"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("double.ToString"));
        }

        [Test]
        public async Task Ps0002_CurrentCultureDateParse_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public DateOnly TestMethod(string value)
    {
        return DateOnly.ParseExact(value, ""d"");
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.DateOnly.ParseExact"));
        }

        [Test]
        public async Task Ps0002_CurrentCultureDateFormat_UsesSemanticCatalogSource()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(DateTime value)
    {
        return value.ToLongDateString();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("current_culture_semantic_rule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.DateTime.ToLongDateString"));
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownPureGenericMethodOverridesConfiguredImpureType()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        return Array.Empty<int>();
    }
}",
                ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_known_impure_types", "System.Array")
                    .Add("purelysharp_known_pure_methods", "System.Array.Empty<T>()"));

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Configured pure generic method should override a configured impure type for the same member.");
        }

        [Test]
        public async Task Ps0002_ConfiguredKnownPureGenericValueTypePropertyOverridesConfiguredImpureType()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(KeyValuePair<int, int> pair)
    {
        return pair.Key;
    }
}",
                ImmutableDictionary<string, string>.Empty
                    .Add("purelysharp_known_impure_types", "System.Collections.Generic.KeyValuePair<TKey, TValue>")
                    .Add("purelysharp_known_pure_methods", "System.Collections.Generic.KeyValuePair<TKey, TValue>.Key.get"));

            Assert.That(
                diagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId),
                Is.False,
                "Configured pure generic value-type property should override a configured impure type for the same member.");
        }

        [Test]
        public async Task Ps0002_ImpureCallee_IncludesCalleeChain()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void Caller()
    {
        Callee();
    }

    [EnforcePure]
    public void Callee()
    {
        Console.WriteLine(""impure"");
    }
}");

            var callerDiagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'Caller'", StringComparison.Ordinal));

            Assert.That(callerDiagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(callerDiagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.Callee"));
            Assert.That(callerDiagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_UnresolvedDelegateTarget_IncludesDistinctCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(Action action)
    {
        action();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unresolved_delegate_target"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Action.Invoke"));
        }

        [Test]
        public async Task Ps0002_DynamicDispatch_IncludesDistinctCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod(dynamic value)
    {
        return value.ToString();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("DynamicInvocation"));
        }

        [Test]
        public async Task Ps0002_DynamicBinaryOperation_IncludesDynamicDispatchCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(dynamic value)
    {
        return value + 1;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("BinaryOperationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Binary"));
        }

        [Test]
        public async Task Ps0002_DynamicUnaryOperation_IncludesDynamicDispatchCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(dynamic value)
    {
        return -value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("dynamic_dispatch"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("UnaryOperationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Unary"));
        }

        [Test]
        public async Task Ps0002_SourceExternCall_IncludesUnknownExternalCallCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Runtime.InteropServices;
using PurelySharp.Attributes;

public static class NativeMethods
{
    [DllImport(""native.dll"")]
    public static extern int ReadValue();
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return NativeMethods.ReadValue();
    }
}");

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unknown_external_call"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("NativeMethods.ReadValue"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("extern"));
        }

        [Test]
        public async Task Ps0002_MutableStateWrite_IncludesDistinctCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    private int _value;

    [EnforcePure]
    public void TestMethod()
    {
        _value = 1;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("AssignmentPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass._value"));
        }

        [Test]
        public async Task Ps0002_AssignmentRhsImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        int value;
        value = Console.Read();
        return value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_MutableStateRead_IncludesDistinctCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    private static int s_value;

    [EnforcePure]
    public int TestMethod()
    {
        return s_value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_read"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("FieldReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.s_value"));
        }

        [Test]
        public async Task Ps0002_StaticPropertyGetterImpurity_PreservesGetterEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    private static int Value
    {
        get
        {
            Console.WriteLine(""impure"");
            return 1;
        }
    }

    [EnforcePure]
    public int TestMethod()
    {
        return Value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.Value.get"));
        }

        [Test]
        public async Task Ps0002_StaticConstructorTrigger_PreservesConstructorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class Config
{
    static Config()
    {
        Console.WriteLine(""impure"");
    }

    public static int Value => 1;
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Config.Value;
    }
}");

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_MethodArgumentImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Math.Abs(Console.Read());
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_LinqArgumentImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Collections.Generic;
using System.Linq;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public IEnumerable<int> TestMethod(IEnumerable<int> values)
    {
        return values.Skip(Console.Read());
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_DirectThrowOnly_IncludesThrowCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        throw new InvalidOperationException();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ThrowOperationPurityRule"));
        }

        [Test]
        public async Task Ps0002_ThrowExceptionExpressionImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        throw new InvalidOperationException(Console.ReadLine());
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.ReadLine"));
        }

        [Test]
        public async Task Ps0002_UnsafePointerOperation_IncludesUnsafePointerCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public unsafe int TestMethod()
    {
        int value = 1;
        int* pointer = &value;
        return *pointer;
    }
}", allowUnsafe: true);

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unsafe_pointer"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("UnsupportedOperation"));
        }

        [Test]
        public async Task Ps0002_MutualRecursivePurityConservativeDiagnostic_IncludesStructuredEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int Fibonacci(int n)
    {
        if (n <= 1)
        {
            return n;
        }

        return Bounce(n - 1) + Bounce(n - 2);
    }

    private int Bounce(int n)
    {
        if (n <= 1)
        {
            return n;
        }

        return Fibonacci(n);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unsupported_operation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("RecursivePurityAnalysis"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("recursive_call"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.Fibonacci"));
        }

        [Test]
        public async Task Ps0002_ImplicitIndexerWithImpureLengthGetter_PreservesRealCalleeEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public sealed class Bag
{
    public int Length
    {
        get
        {
            Console.WriteLine(""length"");
            return 3;
        }
    }

    public int this[int index] => index + 10;
}

public sealed class TestClass
{
    [EnforcePure]
    public int TestMethod(Bag bag)
    {
        return bag[^1];
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("Invocation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("Bag.Length.get"));
        }

        [Test]
        public async Task Ps0002_MutualRecursionWithRealImpurity_PreservesRealCalleeEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void A()
    {
        B();
    }

    [EnforcePure]
    public void B()
    {
        A();
        Console.WriteLine(""impure"");
    }
}");

            var diagnostic = diagnostics
                .Where(diagnostic => diagnostic.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(diagnostic => diagnostic.GetMessage().Contains("'A'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.B"));
        }

        [Test]
        public async Task Ps0002_EnvironmentProperty_IncludesReflectionEnvironmentCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return Environment.TickCount;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("reflection_environment_source"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("PropertyReferencePurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Environment.TickCount"));
        }

        [Test]
        public async Task Ps0002_ReflectionCall_IncludesReflectionEnvironmentCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public Type? TestMethod(string typeName)
    {
        return Type.GetType(typeName);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("reflection_environment_source"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Type.GetType"));
        }

        [Test]
        public async Task Ps0002_LockStatement_IncludesSynchronizationCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    private readonly object _gate = new object();

    [EnforcePure]
    public void TestMethod()
    {
        lock (_gate)
        {
        }
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("synchronization"));
        }

        [Test]
        public async Task Ps0002_MonitorCall_IncludesSynchronizationCategory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Threading;
using PurelySharp.Attributes;

public class TestClass
{
    private static readonly object Gate = new object();

    [EnforcePure]
    public void TestMethod()
    {
        Monitor.Enter(Gate);
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("synchronization"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Threading.Monitor.Enter"));
        }

        [Test]
        public async Task Ps0002_MutableCollectionCreation_IncludesCatalogEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System.Collections.Generic;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public List<int> TestMethod()
    {
        return new List<int>();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("known_mutable_collection"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Collections.Generic.List<int>"));
        }

        [Test]
        public async Task Ps0002_VariableInitializerImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        int value = Console.Read();
        return value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_SpreadOperandImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using System.Collections.Immutable;
using PurelySharp.Attributes;

public class TestClass
{
    private static ImmutableArray<int> GetValues()
    {
        Console.WriteLine(""side effect"");
        return ImmutableArray<int>.Empty;
    }

    [EnforcePure]
    public ImmutableArray<int> Extend()
    {
        return [.. GetValues(), 42];
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.GetValues"));
        }

        [Test]
        public async Task Ps0002_DirectArrayCreation_IncludesArrayCreationEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        return new int[1];
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ArrayCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("array_creation"));
        }

        [Test]
        public async Task Ps0002_GenericTypeConstruction_IncludesObjectCreationEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass<T> where T : new()
{
    [EnforcePure]
    public T TestMethod()
    {
        return new T();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("unsupported_operation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("ObjectCreationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityOperationKindProperty], Is.EqualTo("TypeParameterObjectCreation"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("generic_type_construction"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("T"));
        }

        [Test]
        public async Task Ps0002_ArrayElementImpureArrayReference_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        return GetValues()[0];
    }

    [EnforcePure]
    private int[] GetValues()
    {
        Console.WriteLine(""impure"");
        return new int[1];
    }
}");

            var diagnostic = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.PurityNotVerifiedId)
                .Single(d => d.GetMessage().Contains("'TestMethod'", StringComparison.Ordinal));

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.GetValues"));
        }

        [Test]
        public async Task Ps0002_ArrayInitializerElementImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        int[] values = new[] { Console.Read() };
        return values;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_ArrayDimensionImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod()
    {
        int[] values = new int[Console.Read()];
        return values.Length;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_UserDefinedConversionImpurity_PreservesOperatorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public readonly struct Wrapped
{
    public static explicit operator int(Wrapped value)
    {
        Console.WriteLine(""side effect"");
        return 1;
    }
}

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Wrapped value)
    {
        return (int)value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_UserDefinedBinaryOperatorImpurity_PreservesOperatorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public readonly struct Wrapped
{
    public static Wrapped operator +(Wrapped left, Wrapped right)
    {
        Console.WriteLine(""side effect"");
        return left;
    }
}

public class TestClass
{
    [EnforcePure]
    public Wrapped TestMethod(Wrapped left, Wrapped right)
    {
        return left + right;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_UserDefinedUnaryOperatorImpurity_PreservesOperatorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public readonly struct Wrapped
{
    public static Wrapped operator -(Wrapped value)
    {
        Console.WriteLine(""side effect"");
        return value;
    }
}

public class TestClass
{
    [EnforcePure]
    public Wrapped TestMethod(Wrapped value)
    {
        return -value;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
        }

        [Test]
        public async Task Ps0002_UsingDisposeImpurity_PreservesDisposeEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public sealed class Resource : IDisposable
{
    public void Dispose()
    {
        Console.WriteLine(""side effect"");
    }
}

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        using (var resource = new Resource())
        {
        }
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("Resource.Dispose"));
        }

        [Test]
        public async Task Ps0002_ConstructorInitializerImpurity_PreservesConstructorEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class BaseType
{
    public BaseType()
    {
        Console.WriteLine(""side effect"");
    }
}

public class DerivedType : BaseType
{
    [EnforcePure]
    public DerivedType()
        : base()
    {
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("BaseType.BaseType"));
        }

        [Test]
        public async Task Ps0002_DelegateCreationImpurity_IncludesTargetCalleeChain()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    public static void ImpureTarget()
    {
        Console.WriteLine(""side effect"");
    }

    [EnforcePure]
    public void TestMethod()
    {
        Action action = ImpureTarget;
        action();
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.WriteLine"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCalleeChainProperty], Does.Contain("TestClass.ImpureTarget"));
        }

        [Test]
        public async Task Ps0002_EventAssignment_IncludesMutableStateWriteEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    public event EventHandler? Changed;

    private void Handler(object? sender, EventArgs args)
    {
    }

    [EnforcePure]
    public void TestMethod()
    {
        Changed += Handler;
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("EventAssignmentPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("event_subscription"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("TestClass.Changed"));
        }

        [Test]
        public async Task Ps0002_InterpolatedStringExpressionImpurity_PreservesOriginalEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public string TestMethod()
    {
        return $""{Console.Read()}"";
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("catalog_hit"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("MethodInvocationPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpuritySymbolProperty], Does.Contain("System.Console.Read"));
        }

        [Test]
        public async Task Ps0002_ArrayCollectionExpression_IncludesTargetEvidence()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public int[] TestMethod()
    {
        return [1, 2, 3];
    }
}");

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.PurityNotVerifiedId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCategoryProperty], Is.EqualTo("mutable_state_write"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityRuleProperty], Is.EqualTo("CollectionExpressionPurityRule"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ImpurityCatalogSourceProperty], Is.EqualTo("collection_expression_target"));
        }

        [Test]
        public async Task Ps0009_IsOnlyEmittedWhenExplanationsAreEnabled()
        {
            var source = @"
using System;
using PurelySharp.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Console.WriteLine(""impure"");
    }
}";

            var defaultDiagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var explanationDiagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_emit_explanations", "true"));

            Assert.That(defaultDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityExplanationId), Is.False);
            Assert.That(explanationDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.PurityExplanationId), Is.True);
        }

        [Test]
        public async Task Ps0010_ExceptionSummary_IsOptIn()
        {
            var source = @"
using System;

public class TestClass
{
    public void TestMethod()
    {
        throw new InvalidOperationException();
    }
}";

            var defaultDiagnostics = await GetAnalyzerDiagnosticsAsync(source);
            var exceptionDiagnostics = await GetAnalyzerDiagnosticsAsync(
                source,
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(defaultDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
            Assert.That(exceptionDiagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.True);
        }

        [Test]
        public async Task Ps0010_DirectThrows_ReportsExceptionTypes()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public string TestMethod(string? value)
    {
        if (value == null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return value.Length > 0 ? value : throw new InvalidOperationException();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = SingleDiagnostic(diagnostics, PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("System.ArgumentNullException"));
            Assert.That(diagnostic.GetMessage(), Does.Contain("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException;System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_CaughtThrow_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NestedLambdaThrow_IsNotReportedOnOuterMethod()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Func<int> TestMethod()
    {
        return () => throw new InvalidOperationException();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_SourceCalleeThrow_PropagatesToCaller()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void Caller()
    {
        Callee();
    }

    private void Callee()
    {
        throw new InvalidOperationException();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Length, Is.EqualTo(2));
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Caller'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Callee'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourceCalleeThrow_CaughtByCaller_IsSuppressedOnCaller()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void Caller()
    {
        try
        {
            Callee();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Callee()
    {
        throw new InvalidOperationException();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Any(d => d.GetMessage().Contains("'Caller'", StringComparison.Ordinal)), Is.False);
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Callee'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourceConstructorThrow_PropagatesToFactory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Widget Create()
    {
        return new Widget();
    }
}

public class Widget
{
    public Widget()
    {
        throw new InvalidOperationException();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Create'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'.ctor'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourceConstructorThrow_CaughtAtCreation_IsSuppressedOnFactory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Widget? Create()
    {
        try
        {
            return new Widget();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

public class Widget
{
    public Widget()
    {
        throw new InvalidOperationException();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Any(d => d.GetMessage().Contains("'Create'", StringComparison.Ordinal)), Is.False);
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'.ctor'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourcePropertyGetterThrow_PropagatesToReader()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int Read(Widget widget)
    {
        return widget.Value;
    }
}

public class Widget
{
    public int Value
    {
        get
        {
            throw new InvalidOperationException();
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'Read'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'get_Value'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_SourcePropertyGetterThrow_CaughtAtRead_IsSuppressedOnReader()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int Read(Widget widget)
    {
        try
        {
            return widget.Value;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}

public class Widget
{
    public int Value
    {
        get
        {
            throw new InvalidOperationException();
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var exceptionDiagnostics = diagnostics
                .Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId)
                .ToArray();

            Assert.That(exceptionDiagnostics.Any(d => d.GetMessage().Contains("'Read'", StringComparison.Ordinal)), Is.False);
            Assert.That(exceptionDiagnostics.Single(d => d.GetMessage().Contains("'get_Value'", StringComparison.Ordinal)).Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_EffectSummaryLibraryCall_PropagatesToCaller()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(ArgumentNullException).Assembly.Location,
                        "System.ArgumentNullException.ThrowIfNull(object, string)",
                        Array.Empty<string>(),
                        "System.ArgumentNullException"))));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.ArgumentNullException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("effect_summary"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Does.Contain("System.ArgumentNullException=effect_summary:System.ArgumentNullException.ThrowIfNull"));
        }

        [Test]
        public async Task Ps0010_EffectSummaryLibraryCall_CaughtAtCallSite_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod(object value)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(value);
        }
        catch (ArgumentNullException)
        {
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(ArgumentNullException).Assembly.Location,
                        "System.ArgumentNullException.ThrowIfNull(object, string)",
                        Array.Empty<string>(),
                        "System.ArgumentNullException"))));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_EffectSummaryConstructor_PropagatesToFactory()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public Uri Create(string value)
    {
        return new Uri(value);
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(Uri).Assembly.Location,
                        "System.Uri..ctor(string)",
                        new[] { "System.UriFormatException" }))));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'Create'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.UriFormatException"));
        }

        [Test]
        public async Task Ps0010_EffectSummaryPropertyGetter_PropagatesToReader()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public string Read()
    {
        return Environment.CurrentDirectory;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"),
                additionalFiles: ImmutableArray.Create<AdditionalText>(new InMemoryAdditionalText(
                    "PurelySharp.EffectSummary.json",
                    CreateEffectSummaryJson(
                        typeof(Environment).Assembly.Location,
                        "System.Environment.get_CurrentDirectory()",
                        new[] { "System.InvalidOperationException" }))));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'Read'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_RethrowTypedCatch_ReportsCaughtExceptionType()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            Dangerous();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
    }

    private void Dangerous()
    {
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
        }

        [Test]
        public async Task Ps0010_RethrowTypedCatch_CaughtByOuterTry_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        try
        {
            try
            {
                Dangerous();
            }
            catch (InvalidOperationException)
            {
                throw;
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Dangerous()
    {
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_ConstantIntegerDivideByZero_ReportsDivideByZeroException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value / 0;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
        }

        [Test]
        public async Task Ps0010_ConstantDecimalModuloByZero_Caught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public decimal TestMethod(decimal value)
    {
        try
        {
            return value % 0m;
        }
        catch (DivideByZeroException)
        {
            return 0m;
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_FloatingPointDivideByZero_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public double TestMethod(double value)
    {
        return value / 0.0;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_IfBranchZeroDivisor_ReportsDivideByZeroException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor == 0)
        {
            return value / divisor;
        }

        return 0;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_IfElseNonZeroCondition_ReportsDivideByZeroExceptionInElse()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(int value, int divisor)
    {
        if (divisor != 0)
        {
            return 0;
        }
        else
        {
            return value % divisor;
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.DivideByZeroException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_divide_by_zero"));
        }

        [Test]
        public async Task Ps0010_DirectNullMemberAccess_ReportsNullReferenceException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod()
    {
        return ((string)null).Length;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.NullReferenceException=definite_null_dereference:null_receiver"));
        }

        [Test]
        public async Task Ps0010_IfBranchNullReceiver_ReportsNullReferenceException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value is null)
        {
            return value.Length;
        }

        return 0;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.GetMessage(), Does.Contain("'TestMethod'"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.NullReferenceException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("definite_null_dereference"));
        }

        [Test]
        public async Task Ps0010_IfBranchNullReceiver_ReassignedBeforeUse_DoesNotReport()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int TestMethod(string value)
    {
        if (value == null)
        {
            value = string.Empty;
            return value.Length;
        }

        return 0;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_DirectThrow_IncludesStructuredEvidenceProperties()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public void TestMethod()
    {
        throw new InvalidOperationException();
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            var diagnostic = SingleDiagnostic(diagnostics.Where(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId).ToImmutableArray(), PurelySharpDiagnostics.ExceptionSummaryId);

            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionTypesProperty], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionCategoriesProperty], Is.EqualTo("direct_throw"));
            Assert.That(diagnostic.Properties[PurelySharpDiagnostics.ExceptionSourcesProperty], Is.EqualTo("System.InvalidOperationException=direct_throw:throw"));
        }

        [Test]
        public async Task Ps0010_DefaultReferenceMemberAccess_Caught_IsSuppressed()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using System;

public class TestClass
{
    public int TestMethod()
    {
        try
        {
            return default(string).Length;
        }
        catch (NullReferenceException)
        {
            return 0;
        }
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        [Test]
        public async Task Ps0010_NullConditionalAccess_DoesNotReportNullReferenceException()
        {
            var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int? TestMethod()
    {
        return ((string)null)?.Length;
    }
}",
                ImmutableDictionary<string, string>.Empty.Add("purelysharp_report_exceptions", "true"));

            Assert.That(diagnostics.Any(d => d.Id == PurelySharpDiagnostics.ExceptionSummaryId), Is.False);
        }

        private static Diagnostic SingleDiagnostic(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
        {
            return diagnostics.Single(d => d.Id == diagnosticId);
        }

        private static string CreateEffectSummaryJson(
            string assemblyPath,
            string symbol,
            string[] thrownExceptionTypes,
            params string[] transitiveThrownExceptionTypes)
        {
            var identity = GetAssemblyIdentity(assemblyPath);
            var methodIdentity = GetMethodIdentity(assemblyPath, symbol);
            var methodBodySha256Json = methodIdentity.MethodBodySha256 == null
                ? "null"
                : "\"" + methodIdentity.MethodBodySha256 + "\"";
            return $$"""
{
  "SchemaVersion": 1,
  "Assemblies": [
    {
      "AssemblyName": "{{identity.AssemblyName}}",
      "AssemblyPath": "runtime",
      "AssemblySha256": "{{identity.AssemblySha256}}",
      "ModuleVersionId": "{{identity.ModuleVersionId}}",
      "MethodCount": 1,
      "EmittedMethodCount": 1,
      "Methods": [
        {
          "Symbol": "{{symbol}}",
          "MetadataToken": "{{methodIdentity.MetadataToken}}",
          "RelativeVirtualAddress": 0,
          "MethodBodySha256": {{methodBodySha256Json}},
          "CacheKey": "diagnostic-evidence-test",
          "Effects": [],
          "RootCandidates": [],
          "TransitiveRootCandidates": [],
          "ThrownExceptionTypes": {{FormatJsonArray(thrownExceptionTypes)}},
          "TransitiveThrownExceptionTypes": {{FormatJsonArray(transitiveThrownExceptionTypes)}},
          "Calls": [],
          "Fields": []
        }
      ]
    }
  ]
}
""";
        }

        private static string CreatePuritySummaryJson(
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
        "CacheKey": "diagnostic-evidence-test",
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
          "CacheKey": "diagnostic-evidence-test",
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

            throw new AssertionException("Method symbol did not resolve in assembly: " + symbol);
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
                var signature = definition.DecodeSignature(new EffectSummaryTypeNameProvider(reader), genericContext: null);
                return "(" + string.Join(", ", signature.ParameterTypes) + ")";
            }
            catch (BadImageFormatException)
            {
                return "(?)";
            }
        }

        private static string GetMethodExactSymbolKey(MetadataReader reader, MethodDefinitionHandle handle)
        {
            var definition = reader.GetMethodDefinition(handle);
            var typeName = NormalizeExactTypeName(GetTypeName(reader, definition.GetDeclaringType()));
            var methodName = reader.GetString(definition.Name);
            var signature = DecodeExactMethodSignature(reader, definition);
            return typeName + "." + methodName + signature;
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

        private static string FormatJsonArray(params string[] values)
        {
            if (values.Length == 0)
            {
                return "[]";
            }

            return "[\"" + string.Join("\", \"", values) + "\"]";
        }

        private static string FormatJsonStringOrNull(string? value)
        {
            return value == null ? "null" : "\"" + value + "\"";
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

        private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
            string source,
            ImmutableDictionary<string, string>? globalOptions = null,
            bool allowUnsafe = false,
            ImmutableArray<AdditionalText>? additionalFiles = null)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var references = GetTrustedPlatformReferences()
                .Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location));

            var compilation = CSharpCompilation.Create(
                "DiagnosticEvidenceTests",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));

            var analyzerOptions = new AnalyzerOptions(
                additionalFiles ?? ImmutableArray<AdditionalText>.Empty,
                new TestAnalyzerConfigOptionsProvider(globalOptions ?? ImmutableDictionary<string, string>.Empty));

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

        private sealed class InMemoryAdditionalText : AdditionalText
        {
            private readonly string _text;

            public InMemoryAdditionalText(string path, string text)
            {
                Path = path;
                _text = text;
            }

            public override string Path { get; }

            public override SourceText GetText(CancellationToken cancellationToken = default)
            {
                return SourceText.From(_text);
            }
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
    }
}
