extern alias EditorSuggestionsAssembly;

using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
using SharpProof.CodeFixes;
using CompanionSuggestionAnalyzer = EditorSuggestionsAssembly::SharpProof.EditorSuggestions.CompanionSuggestionAnalyzer;
using SuggestionInputValidation = EditorSuggestionsAssembly::SharpProof.Internal.CodeFixInputValidation;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
public sealed class CompanionSuggestionTests
{
    [TestCase("public interface Api { int Read(int value); }")]
    [TestCase("public static class Api { public static int Read(int value) => value; }")]
    [TestCase("#nullable enable\npublic interface Api<T> where T : class? { T? Read(ref int receiver); U? Echo<U>(in U value) where U : class?; }")]
    [TestCase("public interface Api<T> where T : System.IDisposable { T Read(); }")]
    public async Task SuggestionGeneratesASeparateCompanionAndClearsTheDiagnostic(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var diagnostics = await AnalyzeAsync(document);
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        var diagnostic = diagnostics[0];
        Assert.That(diagnostic.Id, Is.EqualTo("SPED0001"));
        Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Info));
        Assert.That(diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo("Generate a SharpProof contract companion for 'Api'"));
        var fixes = await FixesAsync(document, diagnostic);
        Assert.That(fixes, Has.Length.EqualTo(1));
        Assert.That(fixes[0].Title, Is.EqualTo("Generate SharpProof contract companion"));
        var solution = await ApplyAsync(fixes[0]);
        var generated = solution.GetProject(document.Project.Id)!.Documents.Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Id != document.Id);
        var text = (await generated.GetTextAsync()).ToString();
        var compilation = (await generated.Project.GetCompilationAsync())!;
        var contractDiagnostics = await compilation.WithAnalyzers([new SharpProofAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(generated.Name, Is.EqualTo("ApiContracts.cs"));
            Assert.That((await solution.GetDocument(document.Id)!.GetTextAsync()).ToString(), Is.EqualTo(source));
            Assert.That(text, Does.Contain("NotImplementedException").And.Not.Contain("Contract.Requires").And.Not.Contain("Contract.Ensures").And.Not.Contain("Contract.Assume"));
            Assert.That(compilation.GetDiagnostics().Where(static item =>
                (item ?? throw new ArgumentNullException(nameof(item))).Severity == DiagnosticSeverity.Error), Is.Empty, text);
            Assert.That(contractDiagnostics.Where(static item =>
                (item ?? throw new ArgumentNullException(nameof(item))).Id.StartsWith("SPCF", StringComparison.Ordinal)), Is.Empty, text);
            Assert.That(await AnalyzeAsync(generated), Is.Empty);
            Assert.That(new GenerateCompanionCodeFixProvider().GetFixAllProvider(), Is.Null);
            Assert.That(new GenerateCompanionCodeFixProvider().FixableDiagnosticIds, Is.EqualTo(ImmutableArray.Create("SPED0001")));
        }
    }

    [TestCase("public interface Api { }")]
    [TestCase("public interface { void Read(); }")]
    [TestCase("public struct Api { public void Read() { } }")]
    [TestCase("file interface Api { void Read(); }")]
    [TestCase("public class Outer { public interface Api { void Read(); } }")]
    [TestCase("public interface Api<T> { T Echo<T>(T value); }")]
    [TestCase("public unsafe interface Api { int* Read(); }")]
    [TestCase("public interface Api<T> where T : allows ref struct { void Read(); }")]
    [TestCase("public interface Api { void Read([System.Diagnostics.CodeAnalysis.UnscopedRef] out int value); }")]
    [TestCase("#nullable enable\npublic interface Api { string? Read();\n#nullable disable\nstring Other(); }")]
    [TestCase("public interface Api { void Read(); } public static class ApiContracts { }")]
    [TestCase("public interface Api { void Read(); } [SharpProof.Attributes.ContractFor(typeof(Api))] public static class Existing { }")]
    [TestCase("public interface Api { void Read(); } [SharpProof.Attributes.ContractFor(typeof(Api)), SharpProof.Attributes.ContractFor(typeof(Api))] public static class Existing { }")]
    [TestCase("public class Api { public void Read(int value) { } public static void Read(Api receiver, int value) { } }")]
    [TestCase("public class Api { public void Read<T>(T value) { } public static void Read<U>(Api receiver, U value) { } }")]
    [TestCase("public class Api { public void Read<T>(System.Collections.Generic.List<T> value) { } public static void Read<U>(Api receiver, System.Collections.Generic.List<U> value) { } }")]
    [TestCase("public class Api { public void Read<T>(T[] value) { } public static void Read<U>(Api receiver, U[] value) { } }")]
    [TestCase("public class Api { public void Read(dynamic value) { } public static void Read(Api receiver, object value) { } }")]
    [TestCase("public class Api<T> { public void Read(T value) { } public static void Read(Api<T> receiver, T value) { } }")]
    [TestCase("public class Outer<T> { public class Nested { } } public class Api { public void Read<T>(Outer<T>.Nested value) { } public static void Read<U>(Api receiver, Outer<U>.Nested value) { } }")]
    [TestCase("public class Outer<T> { public class Nested { } } public class Api { private class Hidden { } private Outer<Hidden>.Nested Read() => throw new System.NotImplementedException(); }")]
    public async Task SuggestionsDeclineUnsupportedOrAlreadyMappedSource(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        Assert.That(await AnalyzeAsync(document), Is.Empty);
    }

    [TestCase("public class Api { public void Read(int value) { } public static void Read(Api receiver, string value) { } }")]
    [TestCase("public class Api { public void Read(int value) { } public static void Read(ref Api receiver, int value) { } }")]
    [TestCase("public class Api { public void Read<T>(T value) { } public static void Read<U>(Api receiver, int value) { } }")]
    [TestCase("public class Api { public void Read<T, U>(System.Collections.Generic.List<T> value) { } public static void Read<V, W>(Api receiver, System.Collections.Generic.List<W> value) { } }")]
    [TestCase("public class Api { public void Read<T>(T[] value) { } public static void Read<U>(Api receiver, U[,] value) { } }")]
    [TestCase("public interface Api { void Read(); void Read(int value); }")]
    public async Task DistinctProjectedOverloadsRetainTheGenerationAction(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var diagnostic = (await AnalyzeAsync(document)).Single();
        var fixes = await FixesAsync(document, diagnostic);
        Assert.That(fixes, Has.Length.EqualTo(1));
        var solution = await ApplyAsync(fixes[0]);
        var compilation = (await solution.GetProject(document.Project.Id)!.GetCompilationAsync())!;
        Assert.That(compilation.GetDiagnostics().Where(static item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Severity == DiagnosticSeverity.Error), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task SuggestionsRequireTheActualAttributeAssembly(bool forged)
    {
        var source = "public interface Api { void Read(); }" + (forged ?
            "namespace SharpProof.Attributes { public class ContractForAttribute : System.Attribute { public ContractForAttribute(System.Type target) { } } }" : string.Empty);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source, includeAttributes: false);
        Assert.That(await AnalyzeAsync(document), Is.Empty);
    }

    [Test]
    public async Task SuggestionsSkipGeneratedSourceAndHonorDiagnosticSuppression()
    {
        using var workspace = new AdhocWorkspace();
        var generated = CreateDocument(workspace, "// <auto-generated/>\npublic interface Api { void Read(); }");
        Assert.That(await AnalyzeAsync(generated), Is.Empty);
        var document = CreateDocument(workspace, "public interface Api { void Read(); }");
        var options = (CSharpCompilationOptions)document.Project.CompilationOptions!;
        document = document.Project.WithCompilationOptions(options.WithSpecificDiagnosticOptions(
            options.SpecificDiagnosticOptions.SetItem("SPED0001", ReportDiagnostic.Suppress))).GetDocument(document.Id)!;
        Assert.That(await AnalyzeAsync(document), Is.Empty);
    }

    [Test]
    public async Task CorePackageAnalyzerLoadingWithoutTheOptionalPackageMarkerStaysSilent()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public interface Api { void Read(); }", optionalPackage: false);
        Assert.That(await AnalyzeAsync(document), Is.Empty);
    }

    [Test]
    public async Task SameFilePartialDeclarationsProduceOneSuggestion()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public partial interface Api { void Read(); } public partial interface Api { void Write(); }");
        var diagnostics = await AnalyzeAsync(document);
        var root = await document.GetSyntaxRootAsync();
        var first = root!.DescendantNodes().OfType<InterfaceDeclarationSyntax>().First();
        Assert.That(diagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics[0].Location.SourceSpan, Is.EqualTo(first.Identifier.Span));
    }

    [Test]
    public async Task CrossFilePartialDeclarationsRetainTheExplicitRefactoring()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public partial interface Api { void Read(); }");
        document = document.Project.AddDocument("Other.cs", SourceText.From("public partial interface Api { void Write(); }")).Project.GetDocument(document.Id)!;
        Assert.That(await AnalyzeAsync(document), Is.Empty);
        var root = await document.GetSyntaxRootAsync();
        var declaration = root!.DescendantNodes().OfType<TypeDeclarationSyntax>().Single();
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        await new CompanionRefactoringProvider().ComputeRefactoringsAsync(new CodeRefactoringContext(document, declaration.Identifier.Span,
            action => actions.Add(action ?? throw new ArgumentNullException(nameof(action))), TestContext.CurrentContext.CancellationToken));
        Assert.That(actions, Has.Count.EqualTo(1));
    }

    [TestCase(null)]
    [TestCase("AnotherFix")]
    public async Task GenerationFixRejectsUnmarkedDiagnostics(string? marker)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public interface Api { void Read(); }");
        var actual = (await AnalyzeAsync(document)).Single();
        var properties = marker == null ? null : ImmutableDictionary<string, string?>.Empty.Add(CompanionSuggestionContract.FixKindProperty, marker);
        var diagnostic = Diagnostic.Create(actual.Descriptor, actual.Location, properties: properties);
        Assert.That(await FixesAsync(document, diagnostic), Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task GenerationFixRejectsUnrelatedDiagnosticIdsAndLocations(bool wrongId)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public interface Api { void Read(); }");
        var actual = (await AnalyzeAsync(document)).Single();
        var root = await document.GetSyntaxRootAsync();
        var descriptor = wrongId ? new DiagnosticDescriptor("OTHER0001", "Other", "Other", "Other", DiagnosticSeverity.Info, true) : actual.Descriptor;
        var location = wrongId ? actual.Location : root!.DescendantNodes().OfType<MethodDeclarationSyntax>().Single().Identifier.GetLocation();
        var diagnostic = Diagnostic.Create(descriptor, location, properties: actual.Properties);
        Assert.That(await FixesAsync(document, diagnostic), Is.Empty);
    }

    [Test]
    public async Task GenerationFixRechecksAnExistingCompanionBeforeOfferingAnAction()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public interface Api { void Read(); }");
        var diagnostic = (await AnalyzeAsync(document)).Single();
        document = document.Project.AddDocument("Existing.cs", SourceText.From(
            "[SharpProof.Attributes.ContractFor(typeof(Api))] public static class Existing { }")).Project.GetDocument(document.Id)!;
        Assert.That(await FixesAsync(document, diagnostic), Is.Empty);
    }

    [Test]
    public void GenerationProviderExportsTheCSharpEditorContract()
    {
        var export = typeof(GenerateCompanionCodeFixProvider).GetCustomAttribute<ExportCodeFixProviderAttribute>();
        var rule = new CompanionSuggestionAnalyzer().SupportedDiagnostics.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(export, Is.Not.Null);
            Assert.That(export!.Name, Is.EqualTo(nameof(GenerateCompanionCodeFixProvider)));
            Assert.That(export.Languages, Is.EquivalentTo(ImmutableArray.Create(LanguageNames.CSharp)));
            Assert.That(typeof(GenerateCompanionCodeFixProvider).GetCustomAttribute<System.Composition.SharedAttribute>(), Is.Not.Null);
            Assert.That(rule.IsEnabledByDefault, Is.True);
            Assert.That(rule.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Info));
            Assert.That(new SharpProofAnalyzer().SupportedDiagnostics.Select(static descriptor =>
                (descriptor ?? throw new ArgumentNullException(nameof(descriptor))).Id), Does.Not.Contain("SPED0001"));
        }
    }

    [Test]
    public void AnalyzerInputGuardsIdentifyTheMissingArgument()
    {
        var missingContext = Assert.Throws<ArgumentNullException>(new Action(() => new CompanionSuggestionAnalyzer().Initialize(null!)));
        var invalidName = Assert.Throws<ArgumentException>(new Action(() => SuggestionInputValidation.Require(new object(), " ")));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(missingContext!.ParamName, Is.EqualTo("context"));
            Assert.That(invalidName!.ParamName, Is.EqualTo("parameterName"));
        }
    }

    [Test]
    public async Task CompilerAnalyzerDiscoversAndRunsWithoutWorkspaceAssemblies()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public interface Api { void Read(); }");
        var loader = new CompilerOnlyLoader();
        try
        {
            var failures = new List<string>();
            var reference = new AnalyzerFileReference(typeof(CompanionSuggestionAnalyzer).Assembly.Location, loader);
            reference.AnalyzerLoadFailed += (_, args) => failures.Add((args ?? throw new ArgumentNullException(nameof(args))).Message);
            var analyzers = reference.GetAnalyzers(LanguageNames.CSharp);
            Assert.That(failures, Is.Empty);
            Assert.That(analyzers, Has.Length.EqualTo(1));
            var compilation = (await document.Project.GetCompilationAsync())!;
            var diagnostics = await compilation.WithAnalyzers(analyzers, document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
            Assert.That(diagnostics.Select(static item => (item ?? throw new ArgumentNullException(nameof(item))).Id), Is.EqualTo(ImmutableArray.Create("SPED0001")));
            Assert.That(loader.ForbiddenDependencyRequests, Is.Empty);
            Assert.That(loader.Assemblies.Select(static assembly => (assembly ?? throw new ArgumentNullException(nameof(assembly))).GetName().Name),
                Does.Not.Contain("Microsoft.CodeAnalysis.Workspaces").And.Not.Contain("Microsoft.CodeAnalysis.CSharp.Workspaces"));
        }
        finally
        {
            loader.Unload();
        }
    }

    private sealed class CompilerOnlyLoader : AssemblyLoadContext, IAnalyzerAssemblyLoader
    {
        internal System.Collections.Concurrent.ConcurrentQueue<string> ForbiddenDependencyRequests { get; } = new();

        internal CompilerOnlyLoader() : base(isCollectible: true)
        {
        }

        public void AddDependencyLocation(string fullPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(fullPath);
        }

        public Assembly LoadFromPath(string fullPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(fullPath);
            return LoadFromAssemblyPath(fullPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            ArgumentNullException.ThrowIfNull(assemblyName);
            var name = assemblyName.Name ?? string.Empty;
            if (name.Contains("Workspaces", StringComparison.Ordinal) || name.Contains("Features", StringComparison.Ordinal) ||
                name.StartsWith("System.Composition", StringComparison.Ordinal))
            {
                ForbiddenDependencyRequests.Enqueue(name);
                throw new FileNotFoundException("Editor dependencies are unavailable in a compiler analyzer host.", name);
            }
            return Default.Assemblies.FirstOrDefault(assembly =>
                AssemblyName.ReferenceMatchesDefinition((assembly ?? throw new ArgumentNullException(nameof(assembly))).GetName(), assemblyName));
        }
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string source, bool includeAttributes = true, bool optionalPackage = true)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(source);
        var attributesPath = typeof(Contract).Assembly.Location;
        var references = CompanionScaffoldingTests.Compilation(string.Empty).References.Where(reference =>
            (reference ?? throw new ArgumentNullException(nameof(reference))).Display != attributesPath || includeAttributes);
        var project = workspace.AddProject("CompanionSuggestionFixture", LanguageNames.CSharp)
            .WithMetadataReferences(references)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        if (optionalPackage)
        {
            project = project.AddAnalyzerConfigDocument("SharpProof.CodeFixes.globalconfig", SourceText.From(
                "is_global = true\nbuild_property.SharpProofEditorSuggestions = true\n"), filePath: "/CompanionSuggestionFixture/SharpProof.CodeFixes.globalconfig").Project;
        }
        return project.AddDocument("Target.cs", SourceText.From(source));
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var compilation = (await document.Project.GetCompilationAsync())!;
        return await compilation.WithAnalyzers([new CompanionSuggestionAnalyzer()], document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<ImmutableArray<CodeAction>> FixesAsync(Document document, Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostic);
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        await new GenerateCompanionCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action ?? throw new ArgumentNullException(nameof(action))), TestContext.CurrentContext.CancellationToken));
        return actions.ToImmutable();
    }

    private static async Task<Solution> ApplyAsync(CodeAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var operations = await action.GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        return operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
    }
}
