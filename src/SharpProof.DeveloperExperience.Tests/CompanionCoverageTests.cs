using System.Collections.Immutable;
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

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
public sealed class CompanionCoverageTests
{
    [TestCase("public struct Api { }")]
    [TestCase("public class Outer { public interface Api { void Read(); } }")]
    [TestCase("public unsafe interface Api { int* Read(); }")]
    [TestCase("public unsafe interface Api { void Read(delegate*<int> value); }")]
    [TestCase("public interface Api { Missing Read(); }")]
    [TestCase("public interface Api { void Read(Missing value); }")]
    [TestCase("public class Api { public void Read(__arglist) { } }")]
    [TestCase("public interface Api<T> where T : allows ref struct { void Read(); }")]
    [TestCase("public interface Api { T Read<T>() where T : allows ref struct; }")]
    [TestCase("internal class Hidden { } public class Api<T> where T : Hidden { }")]
    [TestCase("internal class Hidden { } public class Api { private T Read<T>() where T : Hidden => throw new System.NotImplementedException(); }")]
    [TestCase("public interface Api { void Read([System.Runtime.InteropServices.Optional] object value); }")]
    [TestCase("public interface Api { void Read([System.Diagnostics.CodeAnalysis.UnscopedRef] out int value); }")]
    [TestCase("file class Hidden { } internal interface Api { Hidden[] Read(); }")]
    [TestCase("public class Api { private class Hidden { } private System.Collections.Generic.List<Hidden> Read() => new(); }")]
    public async Task UnsupportedSignaturesDeclineBeforeOfferingAnEdit(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var target = await TargetAsync(document);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CompanionScaffolding.IsSupported(target), Is.False);
            Assert.That(await RefactoringsAsync(document), Is.Empty);
        }
    }

    [Test]
    public async Task RefactoringDeclinesOverloadsThatCollideAfterReceiverInsertion()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace,
            "public class Api { public void Read(dynamic value) { } public static void Read(Api receiver, object value) { } }");
        var compilation = (await document.Project.GetCompilationAsync())!;
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(await RefactoringsAsync(document), Is.Empty);
    }

    [Test]
    public async Task UnboundGenericTargetsDeclineAndHelperBoundariesRejectMissingInputs()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public interface Api<T> { T Read(); }");
        var target = await TargetAsync(document);
        var compilation = (await document.Project.GetCompilationAsync())!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CompanionScaffolding.IsSupported(target.ConstructUnboundGenericType()), Is.False);
            AssertNullGuard(() => CompanionScaffolding.IsSupported(null!), "target");
            AssertNullGuard(() => CompanionScaffolding.Method(null!), "target");
            AssertNullGuard(() => CompanionScaffolding.Companion(null!), "target");
            AssertNullGuard(() => CompanionScaffolding.MissingByName(null!, target), "companion");
            AssertNullGuard(() => CompanionScaffolding.MissingByName(target, null!), "target");
            AssertNullGuard(() => CompanionScaffolding.NamespaceName(null!), "symbol");
            AssertNullGuard(() => CompanionScaffolding.ContractTarget(null!, compilation), "companion");
            AssertNullGuard(() => CompanionScaffolding.ContractTarget(target, null!), "compilation");
            AssertNullGuard(() => CompanionScaffolding.FindCompanions(null!, compilation, default), "target");
            AssertNullGuard(() => CompanionScaffolding.FindCompanions(target, null!, default), "compilation");
            AssertAsyncNullGuard(() => CompanionScaffolding.HasNullableContextAsync(null!, document.Project, false, default), "target");
            AssertAsyncNullGuard(() => CompanionScaffolding.HasNullableContextAsync(target, null!, false, default), "project");
        }
    }

    [Test]
    public async Task NullableContextChecksRejectSymbolsFromAnotherWorkspaceSnapshot()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "#nullable enable\npublic interface Api { string? Read(); }");
        var target = await TargetAsync(document);
        var unrelated = workspace.AddProject("Unrelated", LanguageNames.CSharp);
        Assert.That(await CompanionScaffolding.HasNullableContextAsync(target, unrelated, true, default), Is.False);
    }

    [TestCase("using System;", null)]
    [TestCase("public interface Api { void Read(); } public class ApiContracts { }", "Api")]
    public async Task RefactoringDeclinesLocationsWithoutATargetAndExistingNames(string source, string? typeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        Assert.That(await RefactoringsAsync(document, typeName), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOrForgedContractForAttributesDoNotAuthorizeScaffolding(bool forged)
    {
        var source = "public interface Api { void Read(); }" + (forged ?
            "namespace SharpProof.Attributes { public sealed class ContractForAttribute : System.Attribute { public ContractForAttribute(System.Type target) { } } }" : string.Empty);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source, includeAttributes: false);
        var target = await TargetAsync(document);
        var compilation = (await document.Project.GetCompilationAsync())!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CompanionScaffolding.ContractTarget(target, compilation), Is.Null);
            Assert.That(CompanionScaffolding.FindCompanions(target, compilation, default), Is.Empty);
            Assert.That(await RefactoringsAsync(document), Is.Empty);
        }
    }

    [Test]
    public async Task RefactoringAndRepairDeclineWhenGeneratedSourceCannotCompile()
    {
        const string source = "public class System { } public interface Api { int Read(); }";
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        Assert.That(await CompilerErrorsAsync(document), Is.Empty);
        Assert.That(CompanionScaffolding.IsSupported(await TargetAsync(document)), Is.True);
        Assert.That(await RefactoringsAsync(document), Is.Empty);
        document = AddCompanion(document, "[SharpProof.Attributes.ContractFor(typeof(Api))] public static class ApiContracts { }");
        Assert.That(await MissingMemberFixesAsync(document), Is.Empty);
    }

    [Test]
    public async Task RefactoringPreservesInternalNestedTypesReadOnlyInputsAndMultipleConstraints()
    {
        const string source = """
            #nullable enable
            namespace @event.@class;
            internal class Container { internal class Item { } }
            internal interface Api<T, U> where T : struct where U : class?
            {
                Container.Item Read(ref readonly int value);
                void Save(scoped System.Span<int> values);
                V Echo<V>([System.ComponentModel.Description("value")] V value) where V : unmanaged;
            }
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var action = (await RefactoringsAsync(document)).Single();
        Assert.That(action.Title, Is.EqualTo("Generate SharpProof contract companion"));
        Assert.That(action.EquivalenceKey, Is.EqualTo(nameof(CompanionRefactoringProvider)));
        var solution = await ApplyAsync(action);
        var generated = solution.GetProject(document.Project.Id)!.Documents.Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Id != document.Id);
        var text = (await generated.GetTextAsync()).ToString();
        var root = await generated.GetSyntaxRootAsync();
        var read = root!.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
            (method ?? throw new ArgumentNullException(nameof(method))).Identifier.ValueText == "Read");
        var model = await generated.GetSemanticModelAsync();
        var readSymbol = model!.GetDeclaredSymbol(read)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Does.Contain("namespace @event.@class").And.Contain("where U : class?"));
            Assert.That(readSymbol.Parameters[1].RefKind, Is.EqualTo(RefKind.RefReadOnlyParameter));
            Assert.That(readSymbol.Parameters[1].Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(await CompilerErrorsAsync(generated), Is.Empty, text);
            Assert.That(await CompanionDiagnosticsAsync(generated), Is.Empty, text);
        }
    }

    [TestCase("[SharpProof.Attributes.ContractFor(typeof(Api))] public static class First { } [SharpProof.Attributes.ContractFor(typeof(Api))] public static class Second { }")]
    [TestCase("[SharpProof.Attributes.ContractFor(typeof(Api)), SharpProof.Attributes.ContractFor(typeof(Api))] public static class ApiContracts { }")]
    [TestCase("public static class ApiContracts { }")]
    [TestCase("public class Outer { [SharpProof.Attributes.ContractFor(typeof(Api))] public static class ApiContracts { } }")]
    [TestCase("[SharpProof.Attributes.ContractFor(typeof(Api))] file static class ApiContracts { }")]
    [TestCase("[SharpProof.Attributes.ContractFor(typeof(Api))] public static class ApiContracts<T> { }")]
    public async Task MissingMemberRepairDeclinesAmbiguousOrUnsupportedCompanions(string companion)
    {
        ArgumentException.ThrowIfNullOrEmpty(companion);
        using var workspace = new AdhocWorkspace();
        var document = AddCompanion(CreateDocument(workspace, "public interface Api { void Read(); }"), companion);
        Assert.That(await MissingMemberFixesAsync(document), Is.Empty);
    }

    [TestCase("[SharpProof.Attributes.ContractFor(typeof(Api<int>))] public static class ApiContracts { }")]
    [TestCase("[SharpProof.Attributes.ContractFor(typeof(Api<>))] public static class ApiContracts<U> { }")]
    public async Task MissingMemberRepairDeclinesClosedOrRenamedGenericMappings(string companion)
    {
        ArgumentException.ThrowIfNullOrEmpty(companion);
        using var workspace = new AdhocWorkspace();
        var document = AddCompanion(CreateDocument(workspace, "public interface Api<T> { T Read(); }"), companion);
        Assert.That(await MissingMemberFixesAsync(document), Is.Empty);
    }

    [Test]
    public async Task MissingMemberRepairDeclinesNarrowedNullableGenericConstraints()
    {
        using var workspace = new AdhocWorkspace();
        var document = AddCompanion(CreateDocument(workspace,
            "#nullable enable\npublic interface Api<T> where T : class? { void Read(); }"),
            "#nullable enable\n[SharpProof.Attributes.ContractFor(typeof(Api<>))] public static class ApiContracts<T> where T : class { }");
        Assert.That(await CompanionDiagnosticsAsync(document), Has.Some.Property(nameof(Diagnostic.Id)).EqualTo("SPCF0003"));
        Assert.That(await MissingMemberFixesAsync(document), Is.Empty);
    }

    [Test]
    public async Task MissingMemberRepairDeclinesAMethodNamedAfterACustomCompanion()
    {
        using var workspace = new AdhocWorkspace();
        var document = AddCompanion(CreateDocument(workspace, "public interface Api { void CustomContracts(); }"),
            "[SharpProof.Attributes.ContractFor(typeof(Api))] public static class CustomContracts { }");
        Assert.That(CompanionScaffolding.IsSupported(await TargetAsync(document)), Is.True);
        Assert.That(await MissingMemberFixesAsync(document), Is.Empty);
    }

    [TestCase("public interface Api { void ApiContracts(); }")]
    [TestCase("public interface Api { void Read(); void Read(int value); }")]
    public async Task MissingMemberRepairDeclinesNameCollisionsAndPartiallyPopulatedOverloads(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        using var workspace = new AdhocWorkspace();
        var document = AddCompanion(CreateDocument(workspace, source),
            "[SharpProof.Attributes.ContractFor(typeof(Api))] public static class ApiContracts { public static void Read(Api receiver) { throw new System.NotImplementedException(); } }");
        Assert.That(await MissingMemberFixesAsync(document), Is.Empty);
    }

    [Test]
    public async Task MissingMemberRepairAddsAllAbsentOverloadsAndAvoidsReceiverNameCollisions()
    {
        const string source = "public interface Api { void Read(int receiver, int _receiver); void Read(string value); }";
        using var workspace = new AdhocWorkspace();
        var document = AddCompanion(CreateDocument(workspace, source),
            "[SharpProof.Attributes.ContractFor(typeof(Api))] public static class ApiContracts { }");
        var actions = await MissingMemberFixesAsync(document);
        Assert.That(actions, Has.Length.EqualTo(1));
        Assert.That(actions[0].EquivalenceKey, Is.EqualTo(nameof(MissingCompanionMemberCodeFixProvider)));
        var solution = await ApplyAsync(actions[0]);
        var companion = solution.GetProject(document.Project.Id)!.Documents.Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Name == "Companion.cs");
        var root = await companion.GetSyntaxRootAsync();
        var methods = root!.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(methods, Has.Length.EqualTo(2));
            Assert.That(methods[0].ParameterList.Parameters[0].Identifier.ValueText, Is.EqualTo("__receiver"));
            Assert.That(await CompilerErrorsAsync(companion), Is.Empty);
            Assert.That(await CompanionDiagnosticsAsync(companion), Is.Empty);
            Assert.That(new MissingCompanionMemberCodeFixProvider().FixableDiagnosticIds, Is.EqualTo(ImmutableArray.Create("SPCF0004")));
        }
    }

    [Test]
    public async Task RepairAcceptsADiagnosticAtTheCompanionAndIgnoresLocationsOutsideAType()
    {
        using var workspace = new AdhocWorkspace();
        var document = AddCompanion(CreateDocument(workspace, "public interface Api { void Read(); }"),
            "using System; [SharpProof.Attributes.ContractFor(typeof(Api))] public static class ApiContracts { }");
        var companion = document.Project.Documents.Single(item => (item ?? throw new ArgumentNullException(nameof(item))).Name == "Companion.cs");
        var root = await companion.GetSyntaxRootAsync();
        var declaration = root!.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var actions = await MissingMemberFixesAsync(companion, declaration.Identifier.Span);
        Assert.That(actions, Has.Length.EqualTo(1));
        var solution = await ApplyAsync(actions[0]);
        Assert.That(await CompanionDiagnosticsAsync(solution.GetDocument(companion.Id)!), Is.Empty);
        Assert.That(await MissingMemberFixesAsync(companion, root.DescendantNodes().OfType<UsingDirectiveSyntax>().Single().Span), Is.Empty);
    }

    [Test]
    public async Task MissingMemberRepairDeclinesASourceGeneratedCompanion()
    {
        using var workspace = new AdhocWorkspace();
        var original = CreateDocument(workspace, "public interface Api { void Read(); }");
        var document = original.Project.AddAnalyzerReference(new CompanionGeneratorReference()).GetDocument(original.Id)!;
        var diagnostics = await CompanionDiagnosticsAsync(document);
        Assert.That(diagnostics, Has.Some.Property(nameof(Diagnostic.Id)).EqualTo("SPCF0004"));
        Assert.That(await MissingMemberFixesAsync(document), Is.Empty);
    }

    [TestCase("public class { void Read(); }")]
    [TestCase("public interface { void Read(); }")]
    public async Task MissingMemberRepairDeclinesMalformedTypeDeclarations(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var root = await document.GetSyntaxRootAsync();
        var declaration = root!.DescendantNodes().OfType<TypeDeclarationSyntax>().Single();
        Assert.That(await MissingMemberFixesAsync(document, declaration.Span), Is.Empty);
    }

    [Test]
    public void EditorInputValidationReportsTheMissingArgumentPrecisely()
    {
        var invalidName = Assert.Throws<ArgumentException>(new Action(() => global::SharpProof.Internal.CodeFixInputValidation.Require(new object(), " ")));
        var missingValue = Assert.Throws<ArgumentNullException>(new Action(() => global::SharpProof.Internal.CodeFixInputValidation.Require(null, "value")));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(invalidName!.ParamName, Is.EqualTo("parameterName"));
            Assert.That(missingValue!.ParamName, Is.EqualTo("value"));
        }
    }

    private sealed class CompanionGeneratorReference : AnalyzerReference
    {
        public override string? FullPath => null;
        public override object Id => this;

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages()
        {
            return [];
        }

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language)
        {
            ArgumentNullException.ThrowIfNull(language);
            return [];
        }

        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages()
        {
            return [new CompanionGenerator().AsSourceGenerator()];
        }

        public override ImmutableArray<ISourceGenerator> GetGenerators(string language)
        {
            ArgumentNullException.ThrowIfNull(language);
            return language == LanguageNames.CSharp ? [new CompanionGenerator().AsSourceGenerator()] : [];
        }
    }

    private sealed class CompanionGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            global::SharpProof.Internal.InputValidation.Require(context, nameof(context));
            context.RegisterPostInitializationOutput(static outputContext =>
            {
                global::SharpProof.Internal.InputValidation.Require(outputContext, nameof(outputContext));
                outputContext.AddSource("GeneratedContracts.g.cs", SourceText.From(
                    "[SharpProof.Attributes.ContractFor(typeof(Api))] public static class GeneratedContracts { }", System.Text.Encoding.UTF8));
            });
        }
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string source, bool includeAttributes = true)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(source);
        var attributesPath = typeof(Contract).Assembly.Location;
        var references = CompanionScaffoldingTests.Compilation(string.Empty).References.Where(reference =>
            (reference ?? throw new ArgumentNullException(nameof(reference))).Display != attributesPath || includeAttributes);
        var project = workspace.AddProject("CompanionCoverage", LanguageNames.CSharp)
            .WithMetadataReferences(references)
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview))
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return project.AddDocument("Target.cs", SourceText.From(source));
    }

    private static void AssertNullGuard(Action action, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrEmpty(parameterName);
        Assert.That(Assert.Throws<ArgumentNullException>(action)!.ParamName, Is.EqualTo(parameterName));
    }

    private static void AssertAsyncNullGuard(Func<Task> action, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentException.ThrowIfNullOrEmpty(parameterName);
        Assert.That(Assert.ThrowsAsync<ArgumentNullException>(action)!.ParamName, Is.EqualTo(parameterName));
    }

    private static Document AddCompanion(Document document, string source)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(source);
        return document.Project.AddDocument("Companion.cs", SourceText.From(source)).Project.GetDocument(document.Id)!;
    }

    private static async Task<INamedTypeSymbol> TargetAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = await document.GetSyntaxRootAsync();
        var declaration = root!.DescendantNodes().OfType<TypeDeclarationSyntax>().Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Identifier.ValueText == "Api");
        var model = await document.GetSemanticModelAsync();
        return (INamedTypeSymbol)model!.GetDeclaredSymbol(declaration)!;
    }

    private static async Task<ImmutableArray<CodeAction>> RefactoringsAsync(Document document, string? typeName = "Api")
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = await document.GetSyntaxRootAsync();
        var declaration = root!.DescendantNodes().OfType<TypeDeclarationSyntax>().FirstOrDefault(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Identifier.ValueText == typeName);
        var span = declaration?.Identifier.Span ?? new TextSpan(0, root.FullSpan.Length);
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        await new CompanionRefactoringProvider().ComputeRefactoringsAsync(new CodeRefactoringContext(document, span,
            action => actions.Add(action ?? throw new ArgumentNullException(nameof(action))), TestContext.CurrentContext.CancellationToken));
        return actions.ToImmutable();
    }

    private static async Task<ImmutableArray<CodeAction>> MissingMemberFixesAsync(Document document, TextSpan? span = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = await document.GetSyntaxRootAsync();
        var location = root!.FindNode(span ?? root.DescendantNodes().OfType<MethodDeclarationSyntax>().First().Identifier.Span).GetLocation();
        var diagnostic = Diagnostic.Create(new DiagnosticDescriptor("SPCF0004", "Missing member", "Missing member", "SharpProof.ContractFor.Usage", DiagnosticSeverity.Error, true), location);
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        await new MissingCompanionMemberCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action ?? throw new ArgumentNullException(nameof(action))), TestContext.CurrentContext.CancellationToken));
        return actions.ToImmutable();
    }

    private static async Task<Solution> ApplyAsync(CodeAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var operations = await action.GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        return operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
    }

    private static async Task<ImmutableArray<Diagnostic>> CompilerErrorsAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var compilation = await document.Project.GetCompilationAsync();
        return compilation!.GetDiagnostics().Where(static diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Severity == DiagnosticSeverity.Error).ToImmutableArray();
    }

    private static async Task<ImmutableArray<Diagnostic>> CompanionDiagnosticsAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var compilation = await document.Project.GetCompilationAsync();
        var diagnostics = await compilation!.WithAnalyzers([new SharpProofAnalyzer()]).GetAnalyzerDiagnosticsAsync();
        return diagnostics.Where(static diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Id.StartsWith("SPCF", StringComparison.Ordinal)).ToImmutableArray();
    }
}
