using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Test;

public partial class DiagnosticEvidenceTests
{
    private static readonly MetadataReference EnforcePureAttributeReference =
        MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location);

    private static readonly ImmutableArray<MetadataReference> GeneratedPurityProbeReferences =
        AnalyzerTestHost.GetTrustedPlatformReferences().Add(EnforcePureAttributeReference);

    private static readonly Type GeneratedPurityCatalogType =
        typeof(SharpProofAnalyzer).Assembly.GetType("SharpProof.Analyzer.GeneratedPurityCatalog", true)!;

    private static readonly MethodInfo GeneratedPurityCatalogFromOptionsMethod =
        GeneratedPurityCatalogType.GetMethod("FromOptions", BindingFlags.Public | BindingFlags.Static)!;

    private static readonly MethodInfo GeneratedPurityCatalogTryGetPurityMethod =
        GeneratedPurityCatalogType.GetMethod("TryGetPurity", BindingFlags.Public | BindingFlags.Instance)!;

    private static readonly MethodInfo GeneratedPurityCatalogTryGetFieldPurityMethod =
        GeneratedPurityCatalogType.GetMethod("TryGetFieldPurity", BindingFlags.Public | BindingFlags.Instance)!;

    private static readonly Lazy<object> DefaultGeneratedPurityCatalog = new(
        () => CreateGeneratedPurityCatalog(CreateGeneratedPurityAnalyzerOptions()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly PropertyInfo? GeneratedPurityClassificationProperty =
        FindGeneratedPurityClassificationProperty();

    private static IEnumerable<TestCaseData> GetThreadingSemanticRuleCases()
    {
        yield return new TestCaseData(
                @"
using System.Threading;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod(object gate)
    {
        Monitor.Enter(gate);
    }
}",
                "synchronization",
                "MethodInvocationPurityRule",
                "System.Threading.Monitor.Enter")
            .SetName("Sp0002_MonitorEnter_UsesThreadingSemanticRuleSource");

        yield return new TestCaseData(
                @"
using System.Threading;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void TestMethod()
    {
        Thread.Sleep(1);
    }
}",
                "catalog_hit",
                "MethodInvocationPurityRule",
                "System.Threading.Thread.Sleep")
            .SetName("Sp0002_ThreadSleep_UsesThreadingSemanticRuleSource");

        yield return new TestCaseData(
                @"
using System.Threading;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(Thread thread)
    {
        return thread.ManagedThreadId;
    }
}",
                "catalog_hit",
                "PropertyReferencePurityRule",
                "System.Threading.Thread.ManagedThreadId")
            .SetName("Sp0002_ThreadManagedThreadId_UsesThreadingSemanticRuleSource");

        yield return new TestCaseData(
                @"
using System;
using System.Threading;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public CancellationTokenRegistration TestMethod(CancellationToken token)
    {
        return token.Register(() => { });
    }
}",
                "catalog_hit",
                "MethodInvocationPurityRule",
                "System.Threading.CancellationToken.Register")
            .SetName("Sp0002_CancellationTokenRegister_UsesThreadingSemanticRuleSource");

        yield return new TestCaseData(
                @"
using System.Threading;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(AsyncLocal<int> state)
    {
        return state.Value;
    }
}",
                "catalog_hit",
                "PropertyReferencePurityRule",
                "System.Threading.AsyncLocal")
            .SetName("Sp0002_AsyncLocalValue_UsesThreadingSemanticRuleSource");

        yield return new TestCaseData(
                @"
using System.Threading;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Semaphore TestMethod()
    {
        return new Semaphore(0, 1);
    }
}",
                "synchronization",
                "ObjectCreationPurityRule",
                "System.Threading.Semaphore.Semaphore")
            .SetName("Sp0002_SemaphoreConstructor_UsesThreadingSemanticRuleSource");

        yield return new TestCaseData(
                @"
using System.Threading;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(ThreadLocal<int> state)
    {
        return state.Value;
    }
}",
                "catalog_hit",
                "PropertyReferencePurityRule",
                "System.Threading.ThreadLocal")
            .SetName("Sp0002_ThreadLocalValue_UsesThreadingSemanticRuleSource");

        yield return new TestCaseData(
                @"
using System.Threading.Channels;
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public Channel<int> TestMethod()
    {
        return Channel.CreateUnbounded<int>();
    }
}",
                "catalog_hit",
                "MethodInvocationPurityRule",
                "System.Threading.Channels.Channel.CreateUnbounded")
            .SetName("Sp0002_ChannelCreateUnbounded_UsesThreadingSemanticRuleSource");
    }

    private static ImmutableDictionary<string, string> ReportExceptionsOptions()
    {
        return ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true");
    }

    private static ImmutableDictionary<string, string> CheckedExceptionsOptions()
    {
        return ImmutableDictionary<string, string>.Empty.Add("sharpproof_checked_exceptions", "true");
    }

    private static ImmutableDictionary<string, string> RuntimeHazardSitesOptions()
    {
        return ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", "sites");
    }

    private static ImmutableDictionary<string, string> RuntimeHazardAllOptions()
    {
        return ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", "all");
    }

    private static ImmutableDictionary<string, string> ReportAndCheckedExceptionsOptions()
    {
        return ReportExceptionsOptions().Add("sharpproof_checked_exceptions", "true");
    }

    private static void AssertExceptionEdgesPropertyContains(Diagnostic diagnostic, params string[] expectedFragments)
    {
        Assert.That(
            diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ExceptionEdgesProperty, out var serializedEdges) &&
            !string.IsNullOrWhiteSpace(serializedEdges),
            Is.True,
            "Expected sharpproof.exceptions.edges on diagnostic.");

        foreach (var expectedFragment in expectedFragments)
            Assert.That(serializedEdges, Does.Contain(expectedFragment));
    }

    private static void AssertExceptionEdgesPropertyContainsIfPresent(Diagnostic diagnostic,
        params string[] expectedFragments)
    {
        if (!diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ExceptionEdgesProperty, out var serializedEdges) ||
            string.IsNullOrWhiteSpace(serializedEdges))
            return;

        foreach (var expectedFragment in expectedFragments)
            Assert.That(serializedEdges, Does.Contain(expectedFragment));
    }

    private static void AssertSp0002Evidence(
        Diagnostic diagnostic,
        string? category = null,
        string? rule = null,
        string? catalogSource = null,
        string? symbolContains = null,
        string? operationKind = null)
    {
        if (category != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo(category));

        if (rule != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty], Is.EqualTo(rule));

        if (operationKind != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityOperationKindProperty],
                Is.EqualTo(operationKind));

        if (catalogSource != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
                Is.EqualTo(catalogSource));

        if (symbolContains != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty],
                Does.Contain(symbolContains));
    }

    private static string CreateEffectSummaryJson(
        string assemblyPath,
        string symbol,
        string[] thrownExceptionTypes,
        params string[] transitiveThrownExceptionTypes)
    {
        return GeneratedPurityTestSupport.CreateEffectSummaryJson(
            assemblyPath,
            symbol,
            thrownExceptionTypes,
            transitiveThrownExceptionTypes);
    }

    private static string CreatePuritySummaryJson(
        string assemblyPath,
        string actualMethodLookupSymbol,
        string classification,
        string categoriesJson,
        string? symbolOverride = null)
    {
        return GeneratedPurityTestSupport.CreatePuritySummaryJson(
            assemblyPath,
            actualMethodLookupSymbol,
            classification,
            categoriesJson,
            symbolOverride);
    }

    private static AnalyzerOptions CreateAnalyzerOptions(
        ImmutableDictionary<string, string>? globalOptions = null,
        ImmutableArray<AdditionalText>? additionalFiles = null)
    {
        return AnalyzerTestHost.CreateAnalyzerOptions(globalOptions, additionalFiles);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        ImmutableDictionary<string, string>? globalOptions = null,
        bool allowUnsafe = false,
        ImmutableArray<AdditionalText>? additionalFiles = null,
        ImmutableArray<MetadataReference>? additionalMetadataReferences = null)
    {
        return await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            globalOptions,
            allowUnsafe,
            additionalFiles,
            null,
            true,
            additionalMetadataReferences: additionalMetadataReferences,
            compilationName: "DiagnosticEvidenceTests");
    }

    private static AnalyzerOptions CreateGeneratedPurityAnalyzerOptions()
    {
        return CreateAnalyzerOptions();
    }

    private static CSharpCompilation CreateGeneratedPurityProbeCompilation(SyntaxTree syntaxTree)
    {
        return CSharpCompilation.Create(
            "GeneratedPurityProbe",
            new[] { syntaxTree },
            GeneratedPurityProbeReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static GeneratedPurityProbeContext CreateGeneratedPurityProbeContext(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CreateGeneratedPurityProbeCompilation(syntaxTree);
        return new GeneratedPurityProbeContext(
            syntaxTree,
            compilation,
            compilation.GetSemanticModel(syntaxTree),
            syntaxTree.GetRoot());
    }

    private static object CreateGeneratedPurityCatalog(AnalyzerOptions analyzerOptions)
    {
        return GeneratedPurityCatalogFromOptionsMethod.Invoke(
            null,
            new object[] { analyzerOptions, CancellationToken.None })!;
    }

    private static object GetGeneratedPurityCatalog(AnalyzerOptions? analyzerOptions = null)
    {
        return analyzerOptions is null
            ? DefaultGeneratedPurityCatalog.Value
            : CreateGeneratedPurityCatalog(analyzerOptions);
    }

    private static GeneratedPurityResolution ResolveGeneratedPurity(
        IMethodSymbol methodSymbol,
        Compilation compilation,
        AnalyzerOptions? analyzerOptions = null)
    {
        var args = new object?[] { methodSymbol.OriginalDefinition, compilation, null };
        var matched = (bool)GeneratedPurityCatalogTryGetPurityMethod.Invoke(
            GetGeneratedPurityCatalog(analyzerOptions),
            args)!;
        return new GeneratedPurityResolution(matched, ExtractGeneratedPurityClassification(args[2]));
    }

    private static GeneratedPurityResolution ResolveGeneratedPurity(
        IFieldSymbol fieldSymbol,
        Compilation compilation,
        AnalyzerOptions? analyzerOptions = null)
    {
        var args = new object?[] { fieldSymbol.OriginalDefinition, compilation, null };
        var matched = (bool)GeneratedPurityCatalogTryGetFieldPurityMethod.Invoke(
            GetGeneratedPurityCatalog(analyzerOptions),
            args)!;
        return new GeneratedPurityResolution(matched, ExtractGeneratedPurityClassification(args[2]));
    }

    private static GeneratedPurityResolution ResolveGeneratedPurityByExpressionText(
        GeneratedPurityProbeContext probe,
        string expressionText,
        AnalyzerOptions? analyzerOptions = null)
    {
        var objectCreations = probe.Root
            .DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(node => node.ToString() == expressionText)
            .ToArray();
        if (objectCreations.Length > 0)
        {
            var symbol = objectCreations
                .Select(node => (IMethodSymbol)probe.SemanticModel.GetSymbolInfo(node).Symbol!)
                .Select(static symbol => symbol.OriginalDefinition)
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .Single();
            return ResolveGeneratedPurity(symbol, probe.Compilation, analyzerOptions);
        }

        var invocations = probe.Root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(node => node.ToString() == expressionText)
            .ToArray();
        if (invocations.Length > 0)
        {
            var symbol = invocations
                .Select(node => (IMethodSymbol)probe.SemanticModel.GetSymbolInfo(node).Symbol!)
                .Select(static symbol => symbol.OriginalDefinition)
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .Single();
            return ResolveGeneratedPurity(symbol, probe.Compilation, analyzerOptions);
        }

        var memberAccesses = probe.Root
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(node => node.ToString() == expressionText)
            .ToArray();
        if (memberAccesses.Length > 0)
        {
            var symbols = memberAccesses
                .Select(node => probe.SemanticModel.GetSymbolInfo(node).Symbol)
                .Where(static symbol => symbol != null)
                .Distinct(SymbolEqualityComparer.Default)
                .ToArray();
            return symbols.Single() switch
            {
                IPropertySymbol { GetMethod: not null } property => ResolveGeneratedPurity(property.GetMethod,
                    probe.Compilation, analyzerOptions),
                IFieldSymbol field => ResolveGeneratedPurity(field, probe.Compilation, analyzerOptions),
                IMethodSymbol method => ResolveGeneratedPurity(method, probe.Compilation, analyzerOptions),
                _ => throw new InvalidOperationException("Unsupported generated purity symbol for expression: " +
                                                         expressionText)
            };
        }

        throw new InvalidOperationException("Could not resolve generated purity expression: " + expressionText);
    }

    private static GeneratedPurityResolution[] ResolveGeneratedPurityByExpressionTexts(
        GeneratedPurityProbeContext probe,
        AnalyzerOptions? analyzerOptions = null,
        params string[] expressionTexts)
    {
        return expressionTexts
            .Select(expressionText => ResolveGeneratedPurityByExpressionText(probe, expressionText, analyzerOptions))
            .ToArray();
    }

    private static PropertyInfo? FindGeneratedPurityClassificationProperty()
    {
        var purityEntryParameter = GeneratedPurityCatalogTryGetPurityMethod.GetParameters()[2].ParameterType;
        return purityEntryParameter.IsByRef
            ? purityEntryParameter.GetElementType()?.GetProperty("Classification")
            : purityEntryParameter.GetProperty("Classification");
    }

    private static string? ExtractGeneratedPurityClassification(object? purityEntry)
    {
        return purityEntry is null
            ? null
            : (string?)GeneratedPurityClassificationProperty?.GetValue(purityEntry);
    }

    private static ImmutableArray<AdditionalText> CreateSyntheticGeneratedPurityAdditionalFiles(
        string assemblyPath,
        params (string FileName, string ActualMethodLookupSymbol, string DisplaySymbol, string Classification, string
            CategoriesJson)[] entries)
    {
        return GeneratedPurityTestSupport.CreateSyntheticGeneratedPurityAdditionalFiles(
            entries.Select(entry => (
                assemblyPath,
                entry.FileName,
                entry.ActualMethodLookupSymbol,
                entry.DisplaySymbol,
                entry.Classification,
                entry.CategoriesJson)).ToArray());
    }

    private static string FormatJsonArray(params string[] values)
    {
        if (values.Length == 0) return "[]";

        return "[\"" + string.Join("\", \"", values) + "\"]";
    }

    private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
    {
        return AnalyzerTestHost.GetTrustedPlatformReferences();
    }

    private static MetadataOnlyAssemblyFixture CreateMetadataOnlyAssemblyFixture(
        string assemblyName,
        string source)
    {
        var tempDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            assemblyName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var assemblyPath = Path.Combine(tempDirectory, assemblyName + ".dll");
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emitResult = compilation.Emit(assemblyPath);
        Assert.That(
            emitResult.Success,
            Is.True,
            string.Join(Environment.NewLine, emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return new MetadataOnlyAssemblyFixture(tempDirectory, assemblyPath);
    }

    private readonly record struct GeneratedPurityResolution(bool Matched, string? Classification);

    private readonly record struct GeneratedPurityProbeContext(
        SyntaxTree SyntaxTree,
        Compilation Compilation,
        SemanticModel SemanticModel,
        SyntaxNode Root);

    private sealed class MetadataOnlyAssemblyFixture : IDisposable
    {
        public MetadataOnlyAssemblyFixture(string directoryPath, string assemblyPath)
        {
            DirectoryPath = directoryPath;
            AssemblyPath = assemblyPath;
            Reference = MetadataReference.CreateFromFile(assemblyPath);
        }

        public string DirectoryPath { get; }
        public string AssemblyPath { get; }
        public MetadataReference Reference { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }
}
