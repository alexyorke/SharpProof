using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class PrimaryConstructorIntrinsicPlacementBoundaryAuditTests
{
    private static readonly string[] ExpectedDiagnosticIds = ["SP0024"];

    [Test]
    public async Task OldInParenthesizedClassPrimaryBaseArgumentReportsPlacementDiagnostic()
    {
        const string source = """
            using SharpProof.Attributes;
            public class Parent { public Parent(int value) { } }
            public sealed class Subject() : Parent(checked((Contract.Old(1)))) { }
            """;
        await AssertMisuseAsync(source, "contracts", true, "Old",
            "Contract.Old(...) is valid only inside Contract.Ensures(...).");
    }

    [Test]
    public async Task ResultInRecordPrimaryBaseArgumentReportsPlacementDiagnostic()
    {
        const string source = """
            using SharpProof.Attributes;
            public record Parent { public Parent(int value) { } }
            public sealed record Subject() : Parent(Contract.Result<int>());
            """;
        await AssertMisuseAsync(source, "contracts", true, "Result",
            "Contract.Result<T>() is valid only inside Contract.Ensures(...).");
    }

    [Test]
    public async Task OrdinaryRecordPrimaryBaseArgumentPreservesFullDiagnosticSet()
    {
        const string source = """
            using SharpProof.Attributes;
            public record Parent { public Parent(int value) { } }
            public sealed record Subject() : Parent(1);
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        AssertNoCompilationErrors(compilation);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        AnalyzerTestHost.AssertIds(diagnostics);
    }

    [Test]
    public async Task PrimaryArgumentLambdaKeepsItsOwnValidEnsuresIntrinsicContext()
    {
        const string source = """
            using SharpProof.Attributes;
            public class Parent { public Parent(int value) { } }
            public sealed class Subject() : Parent(
                ((System.Func<int>)(() => {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    Contract.Ensures(Contract.Old(1) == 1);
                    return 1;
                }))()) { }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        AssertNoCompilationErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var intrinsicCalls = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method &&
                method.Name is "Result" or "Old").ToArray();
        Assert.That(intrinsicCalls, Has.Length.EqualTo(2));
        foreach (var invocation in intrinsicCalls)
        {
            var owner = model.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
            Assert.That(owner, Is.Not.Null);
            Assert.That(owner!.MethodKind, Is.EqualTo(MethodKind.AnonymousFunction));
            Assert.That(owner.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        }

        var image = AnalyzerTestHost.EmitImage(compilation);
        var runtime = new AssemblyLoadContext("PrimaryIntrinsicValidLambda", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            Assert.That(Activator.CreateInstance(subject), Is.Not.Null);
        }
        finally
        {
            runtime.Unload();
        }

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        AnalyzerTestHost.AssertIds(diagnostics);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ValidSuppressionKeepsPlacementDiagnosticsForOrdinaryAndPrimaryMisuse(
        bool primaryConstructor)
    {
        var source = primaryConstructor
            ? """
              using SharpProof.Attributes;
              public class Parent { public Parent(int value) { } }
              [method: SharpProofSuppress("reviewed elsewhere")]
              public sealed class Subject() : Parent(Contract.Result<int>()) { }
              """
            : """
              using SharpProof.Attributes;
              public static class Subject {
                  [SharpProofSuppress("reviewed elsewhere")]
                  public static int Read() => Contract.Result<int>();
              }
              """;
        await AssertMisuseAsync(source, "contracts", primaryConstructor, "Result",
            "Contract.Result<T>() is valid only inside Contract.Ensures(...).");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EffectsOnlyKeepsPlacementDiagnosticsForOrdinaryAndPrimaryMisuse(
        bool primaryConstructor)
    {
        var source = primaryConstructor
            ? """
              using SharpProof.Attributes;
              public class Parent { public Parent(int value) { } }
              public sealed class Subject() : Parent(Contract.Result<int>()) { }
              """
            : """
              using SharpProof.Attributes;
              public static class Subject {
                  public static int Read() => Contract.Result<int>();
              }
              """;
        await AssertMisuseAsync(source, "effects", primaryConstructor, "Result",
            "Contract.Result<T>() is valid only inside Contract.Ensures(...).");
    }

    [Test]
    public async Task OrdinaryPrimaryBaseArgumentInEffectsOnlyPreservesFullDiagnosticSet()
    {
        const string source = """
            using SharpProof.Attributes;
            public class Parent { public Parent(int value) { } }
            public sealed class Subject() : Parent(1) { }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        AssertNoCompilationErrors(compilation);
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "effects");
        AnalyzerTestHost.AssertIds(diagnostics);
    }

    private static async Task AssertMisuseAsync(
        string source, string mode, bool executeConstructor,
        string intrinsicName, string expectedRuntimeFailure)
    {
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        AssertNoCompilationErrors(compilation);
        var image = AnalyzerTestHost.EmitImage(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var model = compilation.GetSemanticModel(tree);
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Single(candidate => model.GetSymbolInfo(candidate).Symbol is IMethodSymbol method &&
                method.Name == intrinsicName);
        var target = (IMethodSymbol)model.GetSymbolInfo(invocation).Symbol!;
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingType,
            compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")), Is.True);
        var owner = model.GetEnclosingSymbol(invocation.SpanStart);
        var primaryBase = root.DescendantNodes()
            .OfType<PrimaryConstructorBaseTypeSyntax>().SingleOrDefault();
        var argumentSyntax = primaryBase?.ArgumentList.Arguments.Single();
        var argumentSyntaxKind = argumentSyntax == null
            ? null : model.GetOperation(argumentSyntax)?.Kind;
        var argumentExpression = argumentSyntax?.Expression;
        var originalExpressionKind = argumentExpression == null
            ? null : model.GetOperation(argumentExpression)?.Kind;
        while (argumentExpression is ParenthesizedExpressionSyntax or CheckedExpressionSyntax)
        {
            argumentExpression = argumentExpression is ParenthesizedExpressionSyntax parenthesized
                ? parenthesized.Expression
                : ((CheckedExpressionSyntax)argumentExpression).Expression;
        }
        var unwrappedExpressionKind = argumentExpression == null
            ? null : model.GetOperation(argumentExpression)?.Kind;

        var runtimeFailure = "No CLR construction for ordinary method policy control";
        if (executeConstructor)
        {
            var runtime = new AssemblyLoadContext("PrimaryIntrinsicBoundaryMisuse", isCollectible: true);
            try
            {
                using var stream = new MemoryStream(image);
                var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
                var exception = Assert.Throws<TargetInvocationException>(
                    (Action)(() => Activator.CreateInstance(subject)))!;
                Assert.That(exception.InnerException, Is.TypeOf<InvalidOperationException>());
                runtimeFailure = exception.InnerException!.Message;
                Assert.That(runtimeFailure, Is.EqualTo(expectedRuntimeFailure));
            }
            finally
            {
                runtime.Unload();
            }
        }

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode);
        var observed = string.Join("\n", diagnostics.Select(diagnostic =>
            $"{diagnostic.Id}@{diagnostic.Location.SourceSpan}:" +
            diagnostic.GetMessage(CultureInfo.InvariantCulture)));
        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id),
            Is.EqualTo(ExpectedDiagnosticIds),
            $"Emit succeeded; CLR={runtimeFailure};mode={mode};owner={owner};" +
            $"span={invocation.Span};argumentSyntaxOperation={argumentSyntaxKind};" +
            $"argumentExpressionOperation={originalExpressionKind};" +
            $"unwrappedOperation={unwrappedExpressionKind}\nActual diagnostics: {observed}");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics[0].Location.SourceSpan, Is.EqualTo(invocation.Span));
            Assert.That(diagnostics[0].GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("Contract." + intrinsicName).And.Contain("<placement>")
                    .And.Contain("expected use inside Contract.Ensures"));
        }
    }

    private static void AssertNoCompilationErrors(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
    }
}
