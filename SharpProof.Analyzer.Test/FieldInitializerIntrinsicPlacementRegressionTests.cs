using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class FieldInitializerIntrinsicPlacementRegressionTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    public async Task DirectIntrinsicInMemberInitializerReportsCompletePlacementDiagnostic(
        bool property, bool old)
    {
        var expression = old ? "Contract.Old(1)" : "Contract.Result<int>()";
        var source = CreateSource(property, expression);
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        AssertNoCompilationErrors(compilation);
        var image = AnalyzerTestHost.EmitImage(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var owner = model.GetEnclosingSymbol(invocation.SpanStart);
        var operation = model.GetOperation(invocation)!;
        var target = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        Assert.That(target, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            target!.ContainingType,
            compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")), Is.True);
        var operationRoot = operation;
        while (operationRoot.Parent != null)
        {
            operationRoot = operationRoot.Parent;
        }
        var expectedOwnerKind = property ? SymbolKind.Property : SymbolKind.Field;
        var expectedRootKind = property ? OperationKind.PropertyInitializer : OperationKind.FieldInitializer;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(owner?.Kind, Is.EqualTo(expectedOwnerKind));
            Assert.That(operationRoot.Kind, Is.EqualTo(expectedRootKind));
        }
        var expectedRuntimeFailure = old
            ? "Contract.Old(...) is valid only inside Contract.Ensures(...)."
            : "Contract.Result<T>() is valid only inside Contract.Ensures(...).";
        var runtime = new AssemblyLoadContext("FieldInitializerIntrinsicObservation", isCollectible: true);
        string runtimeFailure;
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
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var actual = DescribeDiagnostics(diagnostics);
        var intrinsic = old ? "Contract.Old" : "Contract.Result";
        var expected = $"SP0024|Error|{invocation.Span}|" +
            $"SharpProof contract '{intrinsic}' has invalid argument '<placement>': " +
            "expected use inside Contract.Ensures";
        var observation = $"Compiler emit succeeded; CLR construction threw exact InvalidOperationException: {runtimeFailure}\n" +
            $"intrinsic={operation.Kind};root={operationRoot.Kind};ownerKind={owner?.Kind};" +
            $"owner={owner};apiAssembly={target.ContainingAssembly.Identity};span={invocation.Span}\n" +
            $"Expected full diagnostic set: {expected}\nActual full diagnostic set: [{actual}]";
        await TestContext.Out.WriteLineAsync(observation);
        Assert.That(actual, Is.EqualTo(expected), observation);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InitializerLambdaPreservesItsValidEnsuresIntrinsicContext(bool property)
    {
        const string expression = """
            () => {
                Contract.Ensures(Contract.Result<int>() == 1);
                Contract.Ensures(Contract.Old(1) == 1);
                return 1;
            }
            """;
        var source = CreateSource(property, expression, "System.Func<int>");
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
        var runtime = new AssemblyLoadContext("FieldInitializerValidLambda", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            var instance = Activator.CreateInstance(subject);
            Assert.That(instance, Is.Not.Null);
            var value = property
                ? subject.GetProperty("Value")!.GetValue(instance)
                : subject.GetField("Value")!.GetValue(instance);
            Assert.That(value, Is.TypeOf<Func<int>>());
            Assert.That(((Func<int>)value!)(), Is.EqualTo(1));
        }
        finally
        {
            runtime.Unload();
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        Assert.That(DescribeDiagnostics(diagnostics), Is.Empty);
    }

    [Test]
    public async Task UnselectedMethodMisuseReportsItsCompletePlacementDiagnostic()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Read() => Contract.Result<int>();
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        AssertNoCompilationErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var expected = $"SP0024|Error|{invocation.Span}|" +
            "SharpProof contract 'Contract.Result' has invalid argument '<placement>': " +
            "expected use inside Contract.Ensures";
        Assert.That(DescribeDiagnostics(diagnostics), Is.EqualTo(expected));
    }

    private static string CreateSource(bool property, string expression, string type = "int")
    {
        var member = property
            ? $"public {type} Value {{ get; }} = {expression};"
            : $"public {type} Value = {expression};";
        return $$"""
            using SharpProof.Attributes;
            public sealed class Subject {
                {{member}}
            }
            """;
    }

    private static void AssertNoCompilationErrors(CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
    }

    private static string DescribeDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.Select(diagnostic =>
            $"{diagnostic.Id}|{diagnostic.Severity}|{diagnostic.Location.SourceSpan}|" +
            diagnostic.GetMessage(CultureInfo.InvariantCulture)));
    }
}
