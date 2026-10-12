using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzEmissionLifecycleTests
{
    [Test]
    public void RealCompilationEmissionReturnsItsSuccessfulReadableImage()
    {
        var compilation = CreateValidCompilation();
        var emission = GeneratedCompilationEmitter.Emit(
            compilation, CancellationToken.None);
        using var image = emission.Image;

        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        Assert.That(image.CanRead, Is.True);
        Assert.That(image.Length, Is.GreaterThan(0));
    }

    [Test]
    public async Task RealCompilationEmissionPropagatesTheActualPrecanceledTokenException()
    {
        var compilation = CreateValidCompilation();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Action emit = () =>
        {
            using var image = GeneratedCompilationEmitter.Emit(
                compilation, cancellation.Token).Image;
        };

        Assert.Throws<OperationCanceledException>(emit);
    }

    [Test]
    public void MetadataEmissionReportsAnActualCompilerReturnTypeError()
    {
        const string source = "public static class MetadataEmissionControl { " +
            "public static int Target() => \"bad\"; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "metadata-emission-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("MetadataEmissionInvalidReturn", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id), Does.Contain("CS0029"));
        Action emit = () =>
        {
            using var image = MetadataProgramDifferentialOracle.Emit(
                compilation, CancellationToken.None);
        };

        var exception = Assert.Throws<InvalidOperationException>(emit);

        Assert.That(exception!.Message, Does.Contain("CS0029"));
        Assert.That(exception.Message, Does.Contain("metadata-emission-control.cs"));
    }

    [Test]
    public void CompiledMethodShapesExposeOnlyTheirOwnedExpressionOperations()
    {
        const string source = "public static class ExpressionOperationControl { " +
            "public static int Block(int value) { return value + 1; } " +
            "public static int Expression(int value) => value + 1; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "expression-operation-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("ExpressionOperationControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var emission = GeneratedCompilationEmitter.Emit(
            compilation, CancellationToken.None);
        using var image = emission.Image;
        Assert.That(emission.Emit.Success, Is.True,
            string.Join(Environment.NewLine, emission.Emit.Diagnostics));
        Assert.That(image.CanRead, Is.True);
        Assert.That(image.Length, Is.GreaterThan(0));
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.SyntaxTree, Is.SameAs(tree));
        var methods = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .ToDictionary(method => method.Identifier.ValueText);
        Assert.That(methods.Count, Is.EqualTo(2));
        foreach (var method in methods.Values)
        {
            var symbol = model.GetDeclaredSymbol(method);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol!.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            var declaration = symbol.DeclaringSyntaxReferences.Single();
            Assert.That(declaration.SyntaxTree, Is.SameAs(tree));
            Assert.That(declaration.Span, Is.EqualTo(method.Span));
        }
        var block = methods["Block"];
        Assert.That(block.ExpressionBody, Is.Null);
        Assert.That(FrontendDifferentialOracle.GetExpressionOperation(
            model, block.ExpressionBody?.Expression, CancellationToken.None), Is.Null);
        var expression = methods["Expression"].ExpressionBody!.Expression;
        var direct = model.GetOperation(expression, CancellationToken.None);
        Assert.That(direct, Is.Not.Null);
        Assert.That(direct!.Syntax.SyntaxTree, Is.SameAs(tree));
        var accessed = FrontendDifferentialOracle.GetExpressionOperation(
            model, expression, CancellationToken.None);
        Assert.That(accessed, Is.Not.Null);
        Assert.That(accessed!.Syntax, Is.SameAs(expression));
        Assert.That(accessed.Kind, Is.EqualTo(direct.Kind));
    }

    private static CSharpCompilation CreateValidCompilation()
    {
        const string source = "public static class EmittedControl { " +
            "public static bool Target(bool value) => !value; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("FuzzEmissionControl", [tree],
            TestMetadataReferences.SortedDistinctPlatform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                checkOverflow: true, nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        return compilation;
    }
}
