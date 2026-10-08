using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class InitializerIntrinsicConstructorMatrixAuditTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["SP0024"];

    [TestCase(false, false, false)]
    [TestCase(false, false, true)]
    [TestCase(false, true, false)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public async Task ExplicitInitializerIntrinsicsReportPlacement(bool thisInitializer, bool old, bool conciseBody)
    {
        var initializer = thisInitializer ? "this" : "base";
        var intrinsic = old ? "Contract.Old(1)" : "Contract.Result<int>()";
        var body = conciseBody ? "=> _ = 0;" : "{ }";
        var source = $$"""
            using SharpProof.Attributes;
            public class Parent {
                public Parent(int value) { }
            }
            public sealed class Subject : Parent {
                public Subject(int value) : base(value) { }
                public Subject() : {{initializer}}({{intrinsic}}) {{body}}
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var image = AnalyzerTestHost.EmitImage(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var constructor = root.DescendantNodes().OfType<ConstructorDeclarationSyntax>()
            .Single(declaration => declaration.ParameterList.Parameters.Count == 0);
        var model = compilation.GetSemanticModel(tree);
        var bodySyntax = (SyntaxNode?)constructor.Body ?? constructor.ExpressionBody!.Expression;
        var bodyOperation = model.GetOperation(bodySyntax)!;
        var ancestors = new List<string>();
        for (var operation = bodyOperation; operation != null; operation = operation.Parent)
        { ancestors.Add(operation.Kind.ToString()); }
        var invocation = constructor.Initializer!.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var runtime = new AssemblyLoadContext("InitializerIntrinsicConstructorMatrixAudit", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            var exception = Assert.Throws<TargetInvocationException>((Action)(() => Activator.CreateInstance(subject)))!;
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(exception.InnerException!.Message, Is.EqualTo(old
                ? "Contract.Old(...) is valid only inside Contract.Ensures(...)."
                : "Contract.Result<T>() is valid only inside Contract.Ensures(...)."));
        }
        finally { runtime.Unload(); }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var observed = string.Join("\n", diagnostics.Select(diagnostic =>
            $"{diagnostic.Id}@{diagnostic.Location.SourceSpan}:{diagnostic.GetMessage(CultureInfo.InvariantCulture)}"));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id), Is.EqualTo(ExpectedDiagnosticIds),
            $"Valid same-compilation CLR throws for {initializer}/{intrinsic}/{body}; body ancestors={string.Join("/", ancestors)}\nActual diagnostics:{observed}");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics[0].Location.SourceSpan, Is.EqualTo(invocation.Span));
            Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain(old ? "Contract.Old" : "Contract.Result").And.Contain("<placement>")
                    .And.Contain("expected use inside Contract.Ensures"));
        }
    }

    [Test]
    public async Task ConciseConstructorBodyMisuseStillReports()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync("""
            using SharpProof.Attributes;
            public class Parent { public Parent(int value) { } }
            public sealed class Subject : Parent {
                public Subject() : base(1) => _ = Contract.Result<int>();
            }
            """, "contracts", []);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("Contract.Result").And.Contain("<placement>"));
    }

    [Test]
    public async Task NestedConstructorBodyMisusesHaveDistinctOwners()
    {
        const string source = """
            using SharpProof.Attributes;
            public class Parent { public Parent(int value) { } }
            public sealed class Subject : Parent {
                public Subject() : base(1) {
                    _ = Contract.Result<int>();
                    void Nested() { _ = Contract.Old(1); }
                    Nested();
                }
            }
            """;
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(source, "contracts", []);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024", "SP0024");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.Substring(diagnostics[0].Location.SourceSpan.Start, diagnostics[0].Location.SourceSpan.Length),
                Is.EqualTo("Contract.Result<int>()"));
            Assert.That(source.Substring(diagnostics[1].Location.SourceSpan.Start, diagnostics[1].Location.SourceSpan.Length),
                Is.EqualTo("Contract.Old(1)"));
        }
    }
}
