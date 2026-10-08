using System.Globalization;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
[NonParallelizable]
public sealed class PropertyInitializerIntrinsicOwnershipAuditTests
{
    private const string SourcePath = "property-initializer-ownership.cs";
    private const string ResultSource = """
        using SharpProof.Attributes;
        public sealed class Subject {
            public int Value { get; } = Contract.Result<int>();
        }
        """;
    private const string OldSource = """
        using SharpProof.Attributes;
        public sealed class Subject {
            public int Value { get; } = Contract.Old(1);
        }
        """;
    private const string LiteralSource = """
        using SharpProof.Attributes;
        public sealed class Subject {
            public int Value { get; } = 1;
        }
        """;
    private const string OrdinaryPropertySource = """
        using SharpProof.Attributes;
        public sealed class Subject {
            public int Value => 1;
        }
        """;
    private const string ResultDiagnostic =
        "SP0024|Error|property-initializer-ownership.cs|[91..113)|" +
        "SharpProof contract 'Contract.Result' has invalid argument '<placement>': " +
        "expected use inside Contract.Ensures";
    private const string OldDiagnostic =
        "SP0024|Error|property-initializer-ownership.cs|[91..106)|" +
        "SharpProof contract 'Contract.Old' has invalid argument '<placement>': " +
        "expected use inside Contract.Ensures";

    [TestCase(false)]
    [TestCase(true)]
    public async Task MisplacedIntrinsicReportsPlacementAfterBackingPropertyOwnershipIsEstablished(bool old)
    {
        var source = old ? OldSource : ResultSource;
        var compilation = AnalyzerTestHost.CreateCompilation(source, [], filePath: SourcePath);
        AssertNoCompilationErrors(compilation);
        var image = AnalyzerTestHost.EmitImage(compilation);
        await AssertInitializerOwnerAsync(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = await tree.GetRootAsync();
        var invocation = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var target = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        Assert.That(target, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(target!.Name, Is.EqualTo(old ? "Old" : "Result"));
            Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingType,
                compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")), Is.True);
            Assert.That(invocation.Span.Start, Is.EqualTo(91));
            Assert.That(invocation.Span.End, Is.EqualTo(old ? 106 : 113));
        }
        var expectedRuntimeMessage = old
            ? "Contract.Old(...) is valid only inside Contract.Ensures(...)."
            : "Contract.Result<T>() is valid only inside Contract.Ensures(...).";
        var runtime = new AssemblyLoadContext("PropertyInitializerMisuse-" + (old ? "Old" : "Result"), isCollectible: true);
        string runtimeMessage;
        try
        {
            using var stream = new MemoryStream(image);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            var thrown = Assert.Throws<TargetInvocationException>(
                (Action)(() => Activator.CreateInstance(subject)))!;
            Assert.That(thrown.InnerException, Is.TypeOf<InvalidOperationException>());
            runtimeMessage = thrown.InnerException!.Message;
            Assert.That(runtimeMessage, Is.EqualTo(expectedRuntimeMessage));
        }
        finally
        {
            runtime.Unload();
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var actual = DescribeDiagnostics(diagnostics);
        var expected = old ? OldDiagnostic : ResultDiagnostic;
        await TestContext.Out.WriteLineAsync(
            "Same compilation emitted; real API=" + target!.ContainingAssembly.Identity +
            "; CLR constructor threw exact InvalidOperationException=" + runtimeMessage +
            "\nExpected complete diagnostics: " + expected + "\nActual complete diagnostics: [" + actual + "]");
        Assert.That(actual, Is.EqualTo(expected));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LiteralInitializerAndOrdinaryPropertyControlsRemainValid(bool ordinaryProperty)
    {
        var source = ordinaryProperty ? OrdinaryPropertySource : LiteralSource;
        var compilation = AnalyzerTestHost.CreateCompilation(source, [], filePath: SourcePath);
        AssertNoCompilationErrors(compilation);
        var image = AnalyzerTestHost.EmitImage(compilation);
        if (!ordinaryProperty)
        {
            await AssertInitializerOwnerAsync(compilation);
        }
        else
        {
            var tree = compilation.SyntaxTrees.Single();
            var syntax = await tree.GetRootAsync();
            var declaration = syntax.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
            var property = compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(property.IsImplicitlyDeclared, Is.False);
                Assert.That(AnalyzerGeneratedCodePolicy.IsGenerated(property, tree, compilation, default), Is.False);
            }
            await TestContext.Out.WriteLineAsync("Ordinary property control declared owner=" + property);
        }
        var runtime = new AssemblyLoadContext("PropertyInitializerControl-" + ordinaryProperty, isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            var instance = Activator.CreateInstance(subject);
            Assert.That(instance, Is.Not.Null);
            Assert.That(subject.GetProperty("Value")!.GetValue(instance), Is.EqualTo(1));
        }
        finally
        {
            runtime.Unload();
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        await TestContext.Out.WriteLineAsync("Control CLR property value=1; complete diagnostics=[" + DescribeDiagnostics(diagnostics) + "]");
        Assert.That(DescribeDiagnostics(diagnostics), Is.Empty);
    }

    private static async Task AssertInitializerOwnerAsync(CSharpCompilation compilation)
    {
        var tree = compilation.SyntaxTrees.Single();
        var syntax = await tree.GetRootAsync();
        var declaration = syntax.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var expression = declaration.Initializer!.Value;
        var model = compilation.GetSemanticModel(tree);
        var declaredProperty = model.GetDeclaredSymbol(declaration)!;
        var rawOwner = model.GetEnclosingSymbol(expression.SpanStart);
        var backingField = rawOwner as IFieldSymbol;
        var associatedProperty = backingField?.AssociatedSymbol as IPropertySymbol;
        var operationRoot = model.GetOperation(expression)!;
        while (operationRoot.Parent != null)
        {
            operationRoot = operationRoot.Parent;
        }
        var propertyRoot = operationRoot as IPropertyInitializerOperation;
        await TestContext.Out.WriteLineAsync(
            "Compiler ownership observation: rawKind=" + rawOwner?.Kind + "; rawOwner=" + rawOwner +
            "; backingImplicit=" + backingField?.IsImplicitlyDeclared + "; associatedProperty=" + associatedProperty +
            "; declaredProperty=" + declaredProperty + "; rootKind=" + operationRoot.Kind +
            "; initializedProperties=" + (propertyRoot == null ? "<none>" : string.Join(",", propertyRoot.InitializedProperties)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rawOwner, Is.AssignableTo<IFieldSymbol>());
            Assert.That(backingField?.IsImplicitlyDeclared, Is.True);
            Assert.That(backingField?.AssociatedSymbol, Is.AssignableTo<IPropertySymbol>());
            Assert.That(SymbolEqualityComparer.Default.Equals(associatedProperty, declaredProperty), Is.True);
            Assert.That(operationRoot.Kind, Is.EqualTo(OperationKind.PropertyInitializer));
            Assert.That(propertyRoot, Is.Not.Null);
            Assert.That(declaredProperty.IsImplicitlyDeclared, Is.False);
            Assert.That(AnalyzerGeneratedCodePolicy.IsGenerated(declaredProperty, tree, compilation, default), Is.False);
        }
        Assert.That(propertyRoot!.InitializedProperties, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(propertyRoot.InitializedProperties.Single(), declaredProperty), Is.True);
    }

    private static void AssertNoCompilationErrors(CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
    }

    private static string DescribeDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics.Select(static diagnostic =>
            $"{diagnostic.Id}|{diagnostic.Severity}|{diagnostic.Location.SourceTree?.FilePath}|" +
            $"{diagnostic.Location.SourceSpan}|{diagnostic.GetMessage(CultureInfo.InvariantCulture)}"));
    }
}
