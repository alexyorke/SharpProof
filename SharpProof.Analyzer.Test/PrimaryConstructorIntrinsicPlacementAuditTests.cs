using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class PrimaryConstructorIntrinsicPlacementAuditTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["SP0024"];

    private const string Source = """
        using SharpProof.Attributes;
        public class Parent {
            public Parent(int value) { }
        }
        public sealed class Subject() : Parent(Contract.Result<int>()) { }
        """;

    [Test]
    public async Task ResultInPrimaryConstructorBaseArgumentReportsPlacementDiagnostic()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(Source, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var image = AnalyzerTestHost.EmitImage(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var invocation = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var owner = model.GetEnclosingSymbol(invocation.SpanStart);
        var operation = model.GetOperation(invocation)!;
        var target = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        Assert.That(target, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            target!.ContainingType,
            compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")), Is.True);

        var runtime = new AssemblyLoadContext(
            "PrimaryConstructorIntrinsicPlacementAudit", isCollectible: true);
        string runtimeFailure;
        try
        {
            using var stream = new MemoryStream(image);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            var exception = Assert.Throws<TargetInvocationException>(
                (Action)(() => Activator.CreateInstance(subject)))!;
            Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
            runtimeFailure = exception.InnerException!.Message;
            Assert.That(runtimeFailure, Is.EqualTo(
                "Contract.Result<T>() is valid only inside Contract.Ensures(...)."));
        }
        finally
        {
            runtime.Unload();
        }

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var observed = string.Join("\n", diagnostics.Select(diagnostic =>
            $"{diagnostic.Id}@{diagnostic.Location.SourceSpan}:" +
            diagnostic.GetMessage(CultureInfo.InvariantCulture)));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(ExpectedDiagnosticIds),
            $"Same-compilation emit succeeded; CLR construction threw InvalidOperationException: {runtimeFailure}\n" +
            $"intrinsic={operation.Kind};ownerKind={owner?.Kind};owner={owner};" +
            $"apiAssembly={target.ContainingAssembly.Identity};span={invocation.Span}\n" +
            $"Actual diagnostics: {observed}");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics[0].Location.SourceSpan, Is.EqualTo(invocation.Span));
            Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("Contract.Result").And.Contain("<placement>")
                    .And.Contain("expected use inside Contract.Ensures"));
        }
    }

    [Test]
    public async Task OrdinaryPrimaryConstructorBaseArgumentPreservesFullDiagnosticSet()
    {
        const string source = """
            using SharpProof.Attributes;
            public class Parent {
                public Parent(int value) { }
            }
            public sealed class Subject() : Parent(1) { }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        AnalyzerTestHost.AssertIds(diagnostics);
    }

    [Test]
    public async Task UnselectedMethodMisuseActivatesDefaultPlacementDiagnostic()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Read() => Contract.Result<int>();
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        AnalyzerTestHost.AssertIds(diagnostics, "SP0024");
        Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
            Does.Contain("Contract.Result").And.Contain("<placement>")
                .And.Contain("expected use inside Contract.Ensures"));
    }
}
