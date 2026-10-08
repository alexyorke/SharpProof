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
public sealed class EventInitializerIntrinsicOwnershipAuditTests
{
    private const string SourcePath = "event-initializer-ownership.cs";
    private const string MisuseSource = """
        #undef SHARPPROOF_CONTRACTS
        using System;
        using SharpProof.Attributes;
        public sealed class ResultSubject {
            public event Func<int> Changed = Contract.Result<Func<int>>();
            public int Raise() => Changed();
        }
        public sealed class OldSubject {
            public event Func<int> Changed = Contract.Old<Func<int>>(null);
            public int Raise() => Changed();
        }
        """;
    private const string LambdaControlSource = """
        #undef SHARPPROOF_CONTRACTS
        using System;
        using SharpProof.Attributes;
        public sealed class Subject {
            public event Func<int> Changed = () => {
                Contract.Ensures(Contract.Result<int>() == 7 && Contract.Old(7) == 7);
                return 7;
            };
            public int Raise() => Changed();
        }
        """;
    private const string ExpectedMisuseDiagnostics =
        "SP0024|Error|event-initializer-ownership.cs|[144..172)|SharpProof contract 'Contract.Result' has invalid argument '<placement>': expected use inside Contract.Ensures" + "\n" +
        "SP0024|Error|event-initializer-ownership.cs|[283..312)|SharpProof contract 'Contract.Old' has invalid argument '<placement>': expected use inside Contract.Ensures";

    [Test]
    public async Task MisplacedEventResultAndOldReportCompletePlacementDiagnostics()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(MisuseSource, [], filePath: SourcePath);
        AssertNoCompilationErrors(compilation);
        var image = AnalyzerTestHost.EmitImage(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = await tree.GetRootAsync();
        var declarations = syntax.DescendantNodes().OfType<EventFieldDeclarationSyntax>()
            .SelectMany(static declaration => declaration.Declaration.Variables).ToArray();
        Assert.That(declarations, Has.Length.EqualTo(2));
        var model = compilation.GetSemanticModel(tree);
        foreach (var declaration in declarations)
        {
            await AssertEventInitializerOwnerAsync(compilation, declaration);
            var invocation = declaration.Initializer!.Value as InvocationExpressionSyntax;
            Assert.That(invocation, Is.Not.Null);
            var target = model.GetSymbolInfo(invocation!).Symbol as IMethodSymbol;
            var old = declaration.Ancestors().OfType<ClassDeclarationSyntax>().First().Identifier.ValueText == "OldSubject";
            var rawExpressionOwner = model.GetEnclosingSymbol(invocation!.SpanStart);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(target, Is.Not.Null);
                Assert.That(target!.Name, Is.EqualTo(old ? "Old" : "Result"));
                Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingType,
                    compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")), Is.True);
                Assert.That(rawExpressionOwner, Is.AssignableTo<IFieldSymbol>());
                Assert.That(((IFieldSymbol)rawExpressionOwner!).AssociatedSymbol, Is.AssignableTo<IEventSymbol>());
                Assert.That(invocation.Span.Start, Is.EqualTo(old ? 283 : 144));
                Assert.That(invocation.Span.End, Is.EqualTo(old ? 312 : 172));
            }
            await TestContext.Out.WriteLineAsync(
                "Real API=" + target!.ContainingAssembly.Identity + "; invocation=" + invocation.Span +
                "; expressionOwner=" + rawExpressionOwner);
        }
        var runtime = new AssemblyLoadContext("EventInitializerMisuse", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image);
            var assembly = runtime.LoadFromStream(stream);
            foreach (var typeName in new[] { "ResultSubject", "OldSubject" })
            {
                var subject = assembly.GetType(typeName)!;
                var thrown = Assert.Throws<TargetInvocationException>(
                    (Action)(() => Activator.CreateInstance(subject)))!;
                var expectedMessage = typeName == "OldSubject"
                    ? "Contract.Old(...) is valid only inside Contract.Ensures(...)."
                    : "Contract.Result<T>() is valid only inside Contract.Ensures(...).";
                Assert.That(thrown.InnerException, Is.TypeOf<InvalidOperationException>());
                Assert.That(thrown.InnerException!.Message, Is.EqualTo(expectedMessage));
                await TestContext.Out.WriteLineAsync(
                    "Same-compilation CLR " + typeName + " constructor threw InvalidOperationException=" +
                    thrown.InnerException.Message);
            }
        }
        finally
        {
            runtime.Unload();
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var actual = DescribeDiagnostics(diagnostics);
        await TestContext.Out.WriteLineAsync(
            "Expected complete diagnostics: [" + ExpectedMisuseDiagnostics + "]\nActual complete diagnostics: [" + actual + "]");
        Assert.That(actual, Is.EqualTo(ExpectedMisuseDiagnostics));
    }

    [Test]
    public async Task EventInitializerLambdaKeepsItsValidEnsuresResultAndOldContext()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(LambdaControlSource, [], filePath: SourcePath);
        AssertNoCompilationErrors(compilation);
        var image = AnalyzerTestHost.EmitImage(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = await tree.GetRootAsync();
        var declaration = syntax.DescendantNodes().OfType<EventFieldDeclarationSyntax>().Single()
            .Declaration.Variables.Single();
        await AssertEventInitializerOwnerAsync(compilation, declaration);
        var model = compilation.GetSemanticModel(tree);
        var intrinsics = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(invocation => model.GetSymbolInfo(invocation).Symbol is IMethodSymbol { Name: "Result" or "Old" })
            .ToArray();
        Assert.That(intrinsics, Has.Length.EqualTo(2));
        foreach (var invocation in intrinsics)
        {
            var target = (IMethodSymbol)model.GetSymbolInfo(invocation).Symbol!;
            var owner = model.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingType,
                    compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")), Is.True);
                Assert.That(owner, Is.Not.Null);
                Assert.That(owner!.MethodKind, Is.EqualTo(MethodKind.AnonymousFunction));
                Assert.That(owner.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            }
            await TestContext.Out.WriteLineAsync(
                "Control intrinsic=" + target.Name + "; span=" + invocation.Span + "; lambdaOwner=" + owner);
        }
        var runtime = new AssemblyLoadContext("EventInitializerLambdaControl", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image);
            var subject = runtime.LoadFromStream(stream).GetType("Subject")!;
            var instance = Activator.CreateInstance(subject);
            Assert.That(instance, Is.Not.Null);
            Assert.That(subject.GetMethod("Raise")!.Invoke(instance, null), Is.EqualTo(7));
        }
        finally
        {
            runtime.Unload();
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        var actual = DescribeDiagnostics(diagnostics);
        await TestContext.Out.WriteLineAsync("Same-compilation event lambda CLR returned 7; complete diagnostics=[" + actual + "]");
        Assert.That(actual, Is.Empty);
    }

    private static async Task AssertEventInitializerOwnerAsync(
        CSharpCompilation compilation, VariableDeclaratorSyntax declaration)
    {
        var tree = declaration.SyntaxTree;
        var model = compilation.GetSemanticModel(tree);
        var declaredEvent = model.GetDeclaredSymbol(declaration) as IEventSymbol;
        var rawOwner = model.GetEnclosingSymbol(declaration.Initializer!.EqualsToken.SpanStart);
        var backingField = rawOwner as IFieldSymbol;
        var associatedEvent = backingField?.AssociatedSymbol as IEventSymbol;
        var root = model.GetOperation(declaration.Initializer.Value)!;
        while (root.Parent != null)
        {
            root = root.Parent;
        }
        var fieldRoot = root as IFieldInitializerOperation;
        await TestContext.Out.WriteLineAsync(
            "Event ownership: tree=" + tree.FilePath + "; initializer=" + declaration.Initializer.Span +
            "; rawKind=" + rawOwner?.Kind + "; rawOwner=" + rawOwner +
            "; backingImplicit=" + backingField?.IsImplicitlyDeclared + "; associatedEvent=" + associatedEvent +
            "; declaredEvent=" + declaredEvent + "; rootKind=" + root.Kind +
            "; initializedFields=" + (fieldRoot == null ? "<none>" : string.Join(",", fieldRoot.InitializedFields)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(declaredEvent, Is.Not.Null);
            Assert.That(declaredEvent!.IsImplicitlyDeclared, Is.False);
            Assert.That(rawOwner, Is.AssignableTo<IFieldSymbol>());
            Assert.That(backingField!.IsImplicitlyDeclared, Is.True);
            Assert.That(backingField.AssociatedSymbol, Is.AssignableTo<IEventSymbol>());
            Assert.That(SymbolEqualityComparer.Default.Equals(associatedEvent, declaredEvent), Is.True);
            Assert.That(root.Kind, Is.EqualTo(OperationKind.FieldInitializer));
            Assert.That(fieldRoot, Is.Not.Null);
            Assert.That(AnalyzerGeneratedCodePolicy.IsGenerated(tree, compilation, default), Is.False);
            Assert.That(AnalyzerGeneratedCodePolicy.IsGenerated(declaredEvent, tree, compilation, default), Is.False);
        }
        Assert.That(fieldRoot!.InitializedFields, Has.Length.EqualTo(1));
        Assert.That(SymbolEqualityComparer.Default.Equals(fieldRoot.InitializedFields.Single(), backingField), Is.True);
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
