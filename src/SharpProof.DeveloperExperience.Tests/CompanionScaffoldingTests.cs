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
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
public sealed class CompanionScaffoldingTests
{
    private static readonly ImmutableArray<string> DebugSymbols = ["DEBUG"];

    [TestCase("public static class Api { public static int Read(int value) => value; }")]
    [TestCase("internal interface Api { int Read(int value); }")]
    public void StaticAndInternalTargetsProduceCompilableCompanions(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        var compilation = Compilation(source);
        var target = compilation.GetTypeByMetadataName("Api")!;
        Assert.That(CompanionScaffolding.IsSupported(target), Is.True);
        var generated = CompanionScaffolding.Companion(target).NormalizeWhitespace().ToFullString();
        var result = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated));
        foreach (var diagnostic in result.GetDiagnostics())
        {
            Assert.That(diagnostic.Severity, Is.Not.EqualTo(DiagnosticSeverity.Error), diagnostic.ToString());
        }
    }
    [Test]
    public async Task GeneratedGenericCompanionCompilesAndPassesActualValidation()
    {
        const string source = """
            #nullable enable
            namespace Example;
            public interface IService<T> where T : class, new()
            {
                T? Find(int key);
                void Reset(ref int receiver);
                U Echo<U>(in U value) where U : struct;
                static int Normalize(int value) => value;
            }
            """;
        var compilation = Compilation(source);
        var target = compilation.GetTypeByMetadataName("Example.IService`1")!;
        Assert.That(CompanionScaffolding.IsSupported(target), Is.True);
        var companion = CompanionScaffolding.Companion(target);
        var generated = "#nullable enable\nnamespace Example { " + companion.NormalizeWhitespace().ToFullString() + " }";
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
        {
            global::SharpProof.Internal.InputValidation.Require(diagnostic, nameof(diagnostic));
            return diagnostic.Severity == DiagnosticSeverity.Error;
        }), Is.Empty, generated);
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new SharpProofAnalyzer())).GetAnalyzerDiagnosticsAsync();
        Assert.That(diagnostics.Where(static diagnostic =>
        {
            global::SharpProof.Internal.InputValidation.Require(diagnostic, nameof(diagnostic));
            return diagnostic.Id.StartsWith("SPCF", StringComparison.Ordinal);
        }), Is.Empty, generated);
        Assert.That(generated, Does.Not.Contain("Contract.Requires").And.Not.Contain("Contract.Assume").And.Not.Contain("Contract.Ensures"));
    }

    [Test]
    public async Task RefactoringPreservesNullableRefAndOptionalSignaturesInANewDocument()
    {
        const string source = """
            #nullable enable
            namespace Example;
            public interface Api<T> where T : class?
            {
                ref readonly string? Read();
                ref T? Find(out T? value);
                void Reset(scoped ref int receiver, params string?[] values);
                void Optional(int value = 3, string? text = null);
                receiver Echo<receiver>(receiver value) where receiver : struct;
                U? OptionalGeneric<U>() where U : class?;
            }
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var actions = await RefactoringsAsync(document);
        Assert.That(actions, Has.Length.EqualTo(1));
        var operations = await actions[0].GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var generated = solution.GetProject(document.Project.Id)!.Documents.Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Id != document.Id);
        var text = (await generated.GetTextAsync()).ToString();
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await solution.GetDocument(document.Id)!.GetTextAsync()).ToString(), Is.EqualTo(source));
            Assert.That(text, Does.Contain("#nullable enable").And.Contain("NotImplementedException"));
            Assert.That(text, Does.Not.Contain("Contract.Requires").And.Not.Contain("Contract.Assume").And.Not.Contain("Contract.Ensures"));
            Assert.That(await CompilerErrorsAsync(generated), Is.Empty, text);
            Assert.That(await CompanionDiagnosticsAsync(generated), Is.Empty, text);
        }
    }

    [TestCase("file class Api { public int Read() => 0; }")]
    [TestCase("public class Api { private class Hidden { } private Hidden Read() => new(); }")]
    [TestCase("public interface Api<T> { T Echo<T>(T value); }")]
    [TestCase("public interface Api { void ApiContracts(); }")]
    [TestCase("public interface Api<ApiContracts> { void Read(); }")]
    public async Task RefactoringDeclinesShapesThatCannotBeCopiedSafely(string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        Assert.That(await RefactoringsAsync(document), Is.Empty);
    }

    [Test]
    public async Task RefactoringDoesNotIntroduceAnAmbiguousExtensionMethod()
    {
        const string source = "public static class Api { public static int Read(this string value) => value.Length; } public static class Caller { public static int Run() => \"x\".Read(); }";
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var action = (await RefactoringsAsync(document)).Single();
        var operations = await action.GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var generated = solution.GetProject(document.Project.Id)!.Documents.Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Id != document.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await CompilerErrorsAsync(generated), Is.Empty);
            Assert.That(await CompanionDiagnosticsAsync(generated), Is.Empty);
            Assert.That((await generated.GetTextAsync()).ToString(), Does.Not.Contain("this string"));
        }
    }

    [Test]
    public async Task RefactoringDeclinesMixedNullableSignatureContexts()
    {
        const string source = "#nullable enable\npublic interface Api { string? Read();\n#nullable disable\nstring Other(); }";
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        Assert.That(await RefactoringsAsync(document), Is.Empty);
    }

    [TestCase("#nullable enable\npublic interface Api { string? Read(); }", "#nullable enable")]
    [TestCase("#nullable disable\npublic interface Api { string Read(); }", "#nullable disable")]
    public async Task RefactoringPreservesSourceNullableContext(string source, string expectedDirective)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(expectedDirective);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var action = (await RefactoringsAsync(document)).Single();
        var operations = await action.GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var generated = solution.GetProject(document.Project.Id)!.Documents.Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Id != document.Id);
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await generated.GetTextAsync()).ToString(), Does.Contain(expectedDirective));
            Assert.That(await CompilerErrorsAsync(generated), Is.Empty);
            Assert.That(await CompanionDiagnosticsAsync(generated), Is.Empty);
        }
    }

    [TestCase("[SharpProof.Attributes.ContractFor(typeof(Api))] public static class ExistingContracts { }")]
    [TestCase("[SharpProof.Attributes.ContractFor(typeof(Api)), SharpProof.Attributes.ContractFor(typeof(Api))] public static class ExistingContracts { }")]
    public async Task RefactoringDeclinesAnExistingCompanionWithADifferentName(string companion)
    {
        ArgumentException.ThrowIfNullOrEmpty(companion);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public interface Api { void Read(); }", companion);
        Assert.That(await RefactoringsAsync(document), Is.Empty);
    }

    [Test]
    public async Task MissingMemberDiagnosticIsAvailableToDocumentSemanticAnalysis()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace,
            "public interface Api { void Read(); void Write(); }",
            "[SharpProof.Attributes.ContractFor(typeof(Api))] public static class ApiContracts { public static void Write(Api receiver) { throw new System.NotImplementedException(); } }");
        var compilation = (await document.Project.GetCompilationAsync())!;
        var tree = (await document.GetSyntaxTreeAsync())!;
        var analyzer = compilation.WithAnalyzers([new SharpProofAnalyzer()]);
        var diagnostics = await analyzer.GetAnalyzerSemanticDiagnosticsAsync(
            compilation.GetSemanticModel(tree), null, TestContext.CurrentContext.CancellationToken);
        var missing = diagnostics.Single(item => (item ?? throw new ArgumentNullException(nameof(item))).Id == "SPCF0004");
        Assert.That(missing.Location.SourceTree, Is.SameAs(tree));
        Assert.That(await MissingMemberFixesAsync(document, missing), Has.Length.EqualTo(1));
        var all = await analyzer.GetAnalyzerDiagnosticsAsync();
        Assert.That(all.Count(item => (item ?? throw new ArgumentNullException(nameof(item))).Id == "SPCF0004"), Is.EqualTo(1));
    }

    [Test]
    public async Task MissingMemberFixUsesActualTargetDiagnosticAndUpdatesTheCompanionDocument()
    {
        const string source = "#nullable enable\npublic interface Api<T> where T : class { T? Read(ref int receiver); void Write(T value); }";
        const string companion = "#nullable enable\n[SharpProof.Attributes.ContractFor(typeof(Api<>))] public static class ExistingContracts<T> where T : class { public static void Write(Api<T> receiver, T value) { throw new System.NotImplementedException(); } }";
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source, companion);
        var diagnostics = await CompanionDiagnosticsAsync(document);
        Assert.That(diagnostics.Select(static diagnostic => (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Id), Is.EqualTo(ImmutableArray.Create("SPCF0004")));
        var tree = await document.GetSyntaxTreeAsync();
        Assert.That(diagnostics[0].Location.SourceTree, Is.SameAs(tree));
        var fixes = await MissingMemberFixesAsync(document, diagnostics[0]);
        Assert.That(fixes, Has.Length.EqualTo(1));
        var operations = await fixes[0].GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = solution.GetProject(document.Project.Id)!.Documents.Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Name == "Companion.cs");
        using (Assert.EnterMultipleScope())
        {
            Assert.That((await solution.GetDocument(document.Id)!.GetTextAsync()).ToString(), Is.EqualTo(source));
            Assert.That((await changed.GetTextAsync()).ToString(), Does.Contain("Read").And.Contain("NotImplementedException"));
            Assert.That(await CompilerErrorsAsync(changed), Is.Empty);
            Assert.That(await CompanionDiagnosticsAsync(changed), Is.Empty);
            Assert.That(new MissingCompanionMemberCodeFixProvider().GetFixAllProvider(), Is.Null);
        }
    }

    [TestCase("public class Api { public void Read() { } }", "[SharpProof.Attributes.ContractFor(typeof(Api))] public class ApiContracts { }")]
    [TestCase("public interface Api<T> where T : class { void Read(); }", "[SharpProof.Attributes.ContractFor(typeof(Api<>))] public static class ApiContracts<T> { }")]
    [TestCase("#nullable enable\npublic interface Api { string? Read(); }", "#nullable disable\n[SharpProof.Attributes.ContractFor(typeof(Api))] public static class ApiContracts { }")]
    public async Task MissingMemberFixDeclinesInvalidOrIncompatibleCompanions(string source, string companion)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentException.ThrowIfNullOrEmpty(companion);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source, companion);
        var root = await document.GetSyntaxRootAsync();
        var method = root!.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var diagnostic = Diagnostic.Create(new DiagnosticDescriptor("SPCF0004", "Missing member", "Missing member", "SharpProof.ContractFor.Usage", DiagnosticSeverity.Error, true), method.GetLocation());
        Assert.That(await MissingMemberFixesAsync(document, diagnostic), Is.Empty);
    }

    [Test]
    public async Task GenerationDeclinesATypeThatWouldBreakAnExistingDocument()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "namespace N { public interface Api { void Read(); } }");
        document = document.Project.AddDocument("Existing.cs", SourceText.From(
            "using Other; namespace Other { public class ApiContracts { } } " +
            "namespace N { public class Existing { public ApiContracts Value = new ApiContracts(); } }"))
            .Project.GetDocument(document.Id)!;
        Assert.That(await CompilerErrorsAsync(document), Is.Empty);
        Assert.That(await RefactoringsAsync(document), Is.Empty);
    }

    [Test]
    public async Task GenerationAllowsUnrelatedExistingCompilerErrors()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public interface Api { void Read(); }");
        document = document.Project.AddDocument("Existing.cs", SourceText.From(
            "public static class Existing { public static int Error() => missing; }"))
            .Project.GetDocument(document.Id)!;
        var fixes = await RefactoringsAsync(document);
        Assert.That(fixes, Has.Length.EqualTo(1));
        var operations = await fixes[0].GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        Assert.That((await CompilerErrorsAsync(changed)).Single().Id, Is.EqualTo("CS0103"));
    }

    [Test]
    public async Task MissingMemberRepairDeclinesAnEditThatBreaksAnotherPartialDeclaration()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace,
            "public interface Api { void Read(); int Existing(); }",
            "[SharpProof.Attributes.ContractFor(typeof(Api))] public static partial class ApiContracts { }");
        document = document.Project.AddDocument("OtherPart.cs", SourceText.From(
            "using static Helpers; public static class Helpers { public static int Read() => 1; } " +
            "public static partial class ApiContracts { public static int Existing(Api receiver) => Read(); }"))
            .Project.GetDocument(document.Id)!;
        Assert.That(await CompilerErrorsAsync(document), Is.Empty);
        var diagnostic = (await CompanionDiagnosticsAsync(document)).Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Id == "SPCF0004");
        Assert.That(await MissingMemberFixesAsync(document, diagnostic), Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task MissingMemberRepairKeepsNewMethodsOutsideATrailingConditionalRegion(bool debug)
    {
        const string companion = """
            [SharpProof.Attributes.ContractFor(typeof(Api))]
            public static class ApiContracts {
            #if DEBUG
                public const int DebugOnly = 1;
            #endif
            }
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "public interface Api { void Read(); }", companion);
        document = document.Project.WithParseOptions(new CSharpParseOptions(LanguageVersion.Preview,
            preprocessorSymbols: debug ? DebugSymbols : ImmutableArray<string>.Empty)).GetDocument(document.Id)!;
        var diagnostic = (await CompanionDiagnosticsAsync(document)).Single(item =>
            (item ?? throw new ArgumentNullException(nameof(item))).Id == "SPCF0004");
        var fixes = await MissingMemberFixesAsync(document, diagnostic);
        Assert.That(fixes, Has.Length.EqualTo(1));
        var operations = await fixes[0].GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var solution = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        foreach (var symbols in new[] { DebugSymbols, ImmutableArray<string>.Empty })
        {
            var changed = solution.GetProject(document.Project.Id)!.WithParseOptions(
                new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: symbols)).GetDocument(document.Id)!;
            Assert.That(await CompilerErrorsAsync(changed), Is.Empty);
            Assert.That(await CompanionDiagnosticsAsync(changed), Is.Empty);
            Assert.That((await changed.Project.GetCompilationAsync())!.GetTypeByMetadataName("ApiContracts")!.GetMembers("Read"),
                Has.Length.EqualTo(1));
        }
    }
    [Test]
    public void ExportLiteralsPreserveSignedAndUnsignedBoundaries()
    {
        var compilation = Compilation("public static class Values { }");
        foreach (var (type, text) in new[]
        {
            (SpecialType.System_Int64, long.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            (SpecialType.System_UInt64, ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            (SpecialType.System_Char, "65535"),
            (SpecialType.System_Int32, int.MinValue.ToString(System.Globalization.CultureInfo.InvariantCulture))
        }

        )
        {
            var literal = NUnitExporter.ModelLiteral(new WorkerModelValue { Kind = "Integer", Value = text }, compilation.GetSpecialType(type));
            var parsed = CSharpSyntaxTree.ParseText("public static class Literal { public static object Value() => " + literal + "; }");
            Assert.That(compilation.AddSyntaxTrees(parsed).GetDiagnostics().Where(static diagnostic =>
            {
                global::SharpProof.Internal.InputValidation.Require(diagnostic, nameof(diagnostic));
                return diagnostic.Severity == DiagnosticSeverity.Error;
            }), Is.Empty, literal);
        }

        Assert.Throws<InvalidDataException>(new Action(() =>
        {
            _ = NUnitExporter.ModelLiteral(new WorkerModelValue { Kind = "Integer", Value = "256" }, compilation.GetSpecialType(SpecialType.System_Byte));
        }));
    }

    internal static CSharpCompilation Compilation(string source)
    {
        global::SharpProof.Internal.InputValidation.Require(source, nameof(source));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).Append(typeof(Contract).Assembly.Location).Distinct(StringComparer.Ordinal).Select(static path =>
        {
            global::SharpProof.Internal.InputValidation.Require(path, nameof(path));
            return MetadataReference.CreateFromFile(path);
        });
        return CSharpCompilation.Create("DeveloperExperienceFixture", [CSharpSyntaxTree.ParseText(source)], references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string source, string? companion = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(source);
        var project = workspace.AddProject("ScaffoldingFixture", LanguageNames.CSharp)
            .WithMetadataReferences(Compilation(string.Empty).References)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var document = project.AddDocument("Target.cs", SourceText.From(source));
        return companion == null ? document : document.Project.AddDocument("Companion.cs", SourceText.From(companion)).Project.GetDocument(document.Id)!;
    }

    private static async Task<ImmutableArray<CodeAction>> RefactoringsAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var root = await document.GetSyntaxRootAsync();
        var declaration = root!.DescendantNodes().OfType<TypeDeclarationSyntax>().First();
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        await new CompanionRefactoringProvider().ComputeRefactoringsAsync(new CodeRefactoringContext(document, declaration.Identifier.Span,
            action => actions.Add(action ?? throw new ArgumentNullException(nameof(action))), TestContext.CurrentContext.CancellationToken));
        return actions.ToImmutable();
    }

    private static async Task<ImmutableArray<CodeAction>> MissingMemberFixesAsync(Document document, Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostic);
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        await new MissingCompanionMemberCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic,
            (action, _) => actions.Add(action ?? throw new ArgumentNullException(nameof(action))), TestContext.CurrentContext.CancellationToken));
        return actions.ToImmutable();
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
        return diagnostics.Where(static diagnostic => (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Id.StartsWith("SPCF", StringComparison.Ordinal)).ToImmutableArray();
    }
}
