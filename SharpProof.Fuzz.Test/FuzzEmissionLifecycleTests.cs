using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Fuzz;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzEmissionLifecycleTests
{
    private static readonly string[] ParameterBindingMethodNames = ["Supported", "Extra"];

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

    [Test]
    public void OmittedArraySizeSyntaxHasNoExpressionOperation()
    {
        const string source = "public static class OmittedSizeControl { " +
            "public static int[] Target() => new int[] { 1 }; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "omitted-size-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("OmittedSizeControl", [tree],
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
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.SyntaxTree, Is.SameAs(tree));
        var method = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single();
        var symbol = model.GetDeclaredSymbol(method);
        Assert.That(symbol, Is.Not.Null);
        Assert.That(symbol!.ReturnType.TypeKind, Is.EqualTo(TypeKind.Array));
        Assert.That(symbol.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(tree));
        var creation = (ArrayCreationExpressionSyntax)method.ExpressionBody!.Expression;
        Assert.That(model.GetOperation(creation, CancellationToken.None), Is.Not.Null);
        var omitted = creation.Type.RankSpecifiers.Single().Sizes.Single();
        Assert.That(omitted, Is.TypeOf<OmittedArraySizeExpressionSyntax>());
        Assert.That(omitted.SyntaxTree, Is.SameAs(tree));
        Assert.That(model.GetOperation(omitted, CancellationToken.None), Is.Null);
        Assert.That(FrontendDifferentialOracle.GetExpressionOperation(
            model, omitted, CancellationToken.None), Is.Null);
    }

    [Test]
    public void OwnedParameterBindingAcceptsItsBooleanSlotAndRejectsASeventhSlot()
    {
        const string source = "public static class ParameterBindingControl { " +
            "public static bool Supported(long left, long right, bool condition, " +
            "string text, long[] values, object reference) => condition; " +
            "public static bool Extra(long left, long right, bool condition, " +
            "string text, long[] values, object reference, bool extra) => extra; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "parameter-binding-control.cs",
            cancellationToken: CancellationToken.None);
        var compilation = CSharpCompilation.Create("ParameterBindingControl", [tree],
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
        var model = compilation.GetSemanticModel(tree);
        Assert.That(model.SyntaxTree, Is.SameAs(tree));
        var methods = tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().ToArray();
        Assert.That(methods.Select(method => method.Identifier.ValueText),
            Is.EquivalentTo(ParameterBindingMethodNames));
        var generated = new GeneratedCSharpCase(
            GeneratedCSharpExpression.Condition(), 0L, 0L, true);
        foreach (var method in methods)
        {
            var supported = method.Identifier.ValueText == "Supported";
            var symbol = model.GetDeclaredSymbol(method);
            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol!.Parameters.Length, Is.EqualTo(supported ? 6 : 7));
            Assert.That(symbol.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Boolean));
            Assert.That(symbol.DeclaringSyntaxReferences.Single().SyntaxTree, Is.SameAs(tree));
            var operation = model.GetOperation(method.ExpressionBody!.Expression,
                CancellationToken.None);
            Assert.That(operation, Is.Not.Null);
            Assert.That(operation!.Syntax.SyntaxTree, Is.SameAs(tree));
            var factory = new IrFactory();
            var lowering = new RoslynOperationLowerer(factory).Lower(operation);
            Assert.That(lowering.IsExact, Is.True);
            var binding = lowering.Variables.Single();
            Assert.That(binding.Symbol, Is.AssignableTo<IParameterSymbol>());
            var parameter = (IParameterSymbol)binding.Symbol;
            Assert.That(parameter.Ordinal, Is.EqualTo(supported ? 2 : 6));
            Assert.That(SymbolEqualityComparer.Default.Equals(
                parameter.ContainingSymbol, symbol), Is.True);
            Assert.That(factory.GetVariableInfo(binding.Variable).Type,
                Is.EqualTo(factory.BooleanType));
            if (supported)
            {
                var environment = FrontendDifferentialOracle.CreateEnvironment(
                    factory, symbol, lowering, generated);
                Assert.That(environment.Variables.Single().Key, Is.EqualTo(binding.Variable));
                Assert.That(environment.Variables.Single().Value.Boolean, Is.True);
                Assert.That(environment.SequenceOrigins, Is.Empty);
            }
            else
            {
                Action bind = () => FrontendDifferentialOracle.CreateEnvironment(
                    factory, symbol, lowering, generated);
                var exception = Assert.Throws<InvalidOperationException>(bind);
                Assert.That(exception!.Message,
                    Is.EqualTo("The generated method has an unexpected parameter."));
            }
        }
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
