using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.CodeFixes.Test;

[TestFixture]
public sealed class ResultTypeCodeFixTests
{
    [TestCase("Contract.Result<long>() >= 0", "Contract.Result<int>() >= 0")]
    [TestCase("Contract.Result<short>() >= 0", "Contract.Result<int>() >= 0")]
    public async Task FixUsesCallableReturnTypeAndRemovesUsageDiagnostic(string expression, string expected)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(expected);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, expression);
        var diagnostics = await AnalyzeAsync(document);
        Assert.That(diagnostics.Single().Id, Is.EqualTo("SP0024"));
        var fixes = await GetFixesAsync(document, diagnostics[0]);
        Assert.That(fixes, Has.Count.EqualTo(1));
        var operations = await fixes[0].GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        var text = (await changed.GetTextAsync()).ToString();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(text, Is.EqualTo(Source(expected)));
            Assert.That(await AnalyzeAsync(changed), Is.Empty);
            var compilation = await changed.Project.GetCompilationAsync();
            Assert.That(compilation!.GetDiagnostics().Where(static item => item is null ? throw new ArgumentNullException(nameof(item)) :
                item.Severity == DiagnosticSeverity.Error), Is.Empty);
        }
    }

    [TestCase("Contract.Old(Contract.Result<long>()) >= 0")]
    [TestCase("Contract.Result<int>() >= 0")]
    public async Task DoesNotOfferFixForOtherIntrinsicErrors(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, expression);
        var root = await document.GetSyntaxRootAsync();
        var invocation = root!.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .First(static node => node is null ? throw new ArgumentNullException(nameof(node)) :
                node.Expression.ToString().StartsWith("Contract.Result", StringComparison.Ordinal));
        // Even a stale/misassigned diagnostic must not offer an unrelated repair.
        var descriptor = new DiagnosticDescriptor("SP0024", "Usage", "Usage", "Usage", DiagnosticSeverity.Error, true);
        var fixes = await GetFixesAsync(document, Diagnostic.Create(descriptor, invocation.GetLocation(),
            properties: ImmutableDictionary<string, string?>.Empty.Add("SharpProof.FixKind", "ResultReturnType")));
        Assert.That(fixes, Is.Empty);
    }

    [TestCase(null)]
    [TestCase("AnotherFix")]
    public async Task IgnoresDiagnosticsWithoutTheExpectedAnalyzerMarker(string? marker)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "Contract.Result<long>() >= 0");
        var actual = (await AnalyzeAsync(document)).Single();
        var stale = Diagnostic.Create(actual.Descriptor, actual.Location,
            properties: marker == null ? null : ImmutableDictionary<string, string?>.Empty.Add("SharpProof.FixKind", marker));
        Assert.That(await GetFixesAsync(document, stale), Is.Empty);
    }

    [TestCase("using static SharpProof.Attributes.Contract;", "Result<long>() >= 0", "Result<int>() >= 0")]
    [TestCase("using C = SharpProof.Attributes.Contract;", "C.Result<long>() >= 0", "C.Result<int>() >= 0")]
    public async Task FixesCompilerBoundAliasesAndStaticImports(string import, string expression, string expected)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(expected);
        using var workspace = new AdhocWorkspace();
        var source = import + " " + Source(expression);
        var document = CreateDocument(workspace, expression).WithText(SourceText.From(source));
        var actual = (await AnalyzeAsync(document)).Single();
        var fixes = await GetFixesAsync(document, actual);
        Assert.That(fixes, Has.Count.EqualTo(1));
        var operations = await fixes[0].GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await AnalyzeAsync(changed), Is.Empty);
            Assert.That((await changed.GetTextAsync()).ToString(), Does.Contain(expected));
        }
    }

    [TestCase("public static void Run() { Contract.Ensures(Contract.Result<long>() >= 0); }")]
    [TestCase("public static async System.Threading.Tasks.Task<int> Run() { Contract.Ensures(Contract.Result<long>() >= 0); await System.Threading.Tasks.Task.Yield(); return 1; }")]
    [TestCase("public static int Run() { return (int)Contract.Result<long>(); }")]
    [TestCase("public static int Run() { Contract.Requires(Contract.Result<long>() >= 0); return 1; }")]
    [TestCase("public static int Run() { Contract.Ensures(Contract.Result<long>(1) >= 0); return 1; }")]
    [TestCase("public static int Run() { Contract.Ensures(Fake.Result<long>() >= 0); return 1; } private static class Fake { public static T Result<T>() => default; }")]
    public async Task RejectsStaleMarkerOnUnsupportedContextOrUnboundSymbol(string member)
    {
        ArgumentNullException.ThrowIfNull(member);
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "true").WithText(SourceText.From(
            "using SharpProof.Attributes; public static class Fixture { " + member + " }"));
        var root = await document.GetSyntaxRootAsync();
        var invocation = root!.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .First(static node => node is null ? throw new ArgumentNullException(nameof(node)) :
                node.Expression.ToString().Contains("Result<long>", StringComparison.Ordinal));
        var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("SP0024", "Usage", "Usage", "Usage", DiagnosticSeverity.Error, true),
            invocation.GetLocation(),
            properties: ImmutableDictionary<string, string?>.Empty.Add("SharpProof.FixKind", "ResultReturnType"));
        Assert.That(await GetFixesAsync(document, diagnostic), Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RejectsMarkerWhoseLocationDoesNotCoverAnIntrinsicInvocation(bool isPartial)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "Contract.Result<long>() >= 0");
        var actual = (await AnalyzeAsync(document)).Single();
        var tree = await document.GetSyntaxTreeAsync();
        var span = isPartial ? new TextSpan(actual.Location.SourceSpan.Start, 1) : new TextSpan(0, 5);
        var diagnostic = Diagnostic.Create(actual.Descriptor, Location.Create(tree!, span), properties: actual.Properties);
        Assert.That(await GetFixesAsync(document, diagnostic), Is.Empty);
    }

    [Test]
    public async Task UsesLocalFunctionReturnTypeInsteadOfOuterMethodReturnType()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "true").WithText(SourceText.From(
            "using SharpProof.Attributes; public static class Fixture { public static int Run() { " +
            "short Local() { Contract.Ensures(Contract.Result<long>() >= 0); return 1; } return Local(); } }"));
        var fixes = await GetFixesAsync(document, (await AnalyzeAsync(document)).Single());
        Assert.That(fixes, Has.Count.EqualTo(1));
        var operations = await fixes[0].GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await AnalyzeAsync(changed), Is.Empty);
            Assert.That((await changed.GetTextAsync()).ToString(), Does.Contain("Contract.Result<short>()"));
        }
    }

    [Test]
    public void UsesRoslynBatchFixer()
    {
        Assert.That(new ResultTypeCodeFixProvider().GetFixAllProvider(), Is.SameAs(WellKnownFixAllProviders.BatchFixer));
    }

    [Test]
    public async Task FixAllCorrectsEachCallableIndependently()
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, "Contract.Result<long>() >= 0");
        var source = Source("Contract.Result<long>() >= 0").Replace(
            "return 1; } }",
            "return 1; } public static short Other() { Contract.Ensures(Contract.Result<int>() >= 0); return 2; } }",
            StringComparison.Ordinal);
        document = document.WithText(SourceText.From(source));
        var diagnostics = await AnalyzeAsync(document);
        Assert.That(diagnostics, Has.Length.EqualTo(2));
        var provider = new ResultTypeCodeFixProvider();
        var action = await provider.GetFixAllProvider().GetFixAsync(new FixAllContext(
            document, provider, FixAllScope.Document, "SharpProof.ResultReturnType", provider.FixableDiagnosticIds,
            new DocumentDiagnostics(document.Id, diagnostics), TestContext.CurrentContext.CancellationToken));
        Assert.That(action, Is.Not.Null);
        var operations = await action!.GetOperationsAsync(TestContext.CurrentContext.CancellationToken);
        var changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution.GetDocument(document.Id)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(await AnalyzeAsync(changed), Is.Empty);
            var text = (await changed.GetTextAsync()).ToString();
            Assert.That(text, Does.Contain("Contract.Result<int>() >= 0"));
            Assert.That(text, Does.Contain("Contract.Result<short>() >= 0"));
        }
    }

    private sealed class DocumentDiagnostics : FixAllContext.DiagnosticProvider
    {
        private readonly DocumentId _documentId;
        private readonly ImmutableArray<Diagnostic> _diagnostics;

        internal DocumentDiagnostics(DocumentId documentId, ImmutableArray<Diagnostic> diagnostics)
        {
            ArgumentNullException.ThrowIfNull(documentId);
            if (diagnostics.IsDefault)
            {
                throw new ArgumentException("Diagnostics must be initialized.", nameof(diagnostics));
            }
            _documentId = documentId;
            _diagnostics = diagnostics;
        }

        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(document);
            return Task.FromResult<IEnumerable<Diagnostic>>(document.Id == _documentId ? _diagnostics : []);
        }

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(project);
            return Task.FromResult<IEnumerable<Diagnostic>>([]);
        }

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(project);
            return Task.FromResult<IEnumerable<Diagnostic>>(_diagnostics);
        }
    }

    private static Document CreateDocument(AdhocWorkspace workspace, string expression)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(expression);
        var project = workspace.AddProject("Fixture", LanguageNames.CSharp)
            .WithMetadataReferences(TestMetadataReferences.WithSharpProof)
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithSpecificDiagnosticOptions(new Dictionary<string, ReportDiagnostic> { ["SP0024"] = ReportDiagnostic.Error }));
        return project.AddDocument("Fixture.cs", SourceText.From(Source(expression)));
    }

    private static string Source(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        return "using SharpProof.Attributes; public static class Fixture { public static int Run() { Contract.Ensures(" +
            expression + "); return 1; } }";
    }

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var compilation = await document.Project.GetCompilationAsync();
        return await compilation!.WithAnalyzers([new SharpProofAnalyzer()]).GetAnalyzerDiagnosticsAsync();
    }

    private static async Task<ImmutableArray<CodeAction>> GetFixesAsync(Document document, Diagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(diagnostic);
        var fixes = ImmutableList<CodeAction>.Empty;
        await new ResultTypeCodeFixProvider().RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostic, (action, _) => fixes = fixes.Add(action ?? throw new ArgumentNullException(nameof(action))),
            TestContext.CurrentContext.CancellationToken));
        return fixes.ToImmutableArray();
    }
}
