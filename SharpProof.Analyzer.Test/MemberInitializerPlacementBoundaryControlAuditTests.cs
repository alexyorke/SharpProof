using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
[NonParallelizable]
public sealed class MemberInitializerPlacementBoundaryControlAuditTests
{
    private const string GeneratedPropertySource = "using System.CodeDom.Compiler;\nusing SharpProof.Attributes;\npublic sealed class Subject {\n    [GeneratedCode(\"audit\", \"1\")]\n    public int Value { get; } = Contract.Result<int>();\n}";
    private const string SuppressedEffectsSource = "using SharpProof.Attributes;\npublic sealed class Subject {\n    [SharpProofSuppress(\"reviewed constructor\")]\n    public Subject() { }\n    public int Field = Contract.Result<int>();\n    [SharpProofSuppress(\"reviewed property\")]\n    public int Value { get; } = Contract.Old(1);\n}";
    private const string PartialASource = "using SharpProof.Attributes;\npublic sealed partial class Subject {\n    public int First =          Contract.Result<int>(), Second = Contract.Old(1);\n}";
    private const string PartialBSource = "using SharpProof.Attributes;\npublic sealed partial class Subject {\n    public int Third { get; } = Contract.Result<int>();\n}";

    [Test]
    public async Task GeneratedDeclaredPropertyInitializerRemainsQuiet()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            GeneratedPropertySource, [], filePath: "initializer-boundary.generated-property.cs");
        AssertCompilesAndEmits(compilation);
        var owners = await AssertBoundInitializerOwnersAsync(compilation, 1);
        var property = owners.Single() as IPropertySymbol;
        Assert.That(property, Is.Not.Null);
        var tree = compilation.SyntaxTrees.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(AnalyzerGeneratedCodePolicy.IsGenerated(
                tree, compilation, default), Is.False);
            Assert.That(AnalyzerGeneratedCodePolicy.IsGenerated(
                property!, tree, compilation, default), Is.True);
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        await TestContext.Out.WriteLineAsync(
            "Generated declared-property policy=True; complete diagnostics=[" +
            DescribeDiagnostics(diagnostics) + "]");
        Assert.That(DescribeDiagnostics(diagnostics), Is.Empty);
    }

    [Test]
    public async Task ValidPropertyAndConstructorSuppressionKeepsInitializerPlacementInEffectsOnly()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            SuppressedEffectsSource, [], filePath: "initializer-boundary.suppressed-effects.cs");
        AssertCompilesAndEmits(compilation);
        var owners = await AssertBoundInitializerOwnersAsync(compilation, 2);
        var subject = compilation.GetTypeByMetadataName("Subject")!;
        var suppress = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.SharpProofSuppressAttribute")!;
        var constructor = subject.InstanceConstructors.Single(
            static method => !method.IsImplicitlyDeclared);
        var property = owners.OfType<IPropertySymbol>().Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(constructor.GetAttributes().Any(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, suppress)), Is.True);
            Assert.That(property.GetAttributes().Any(attribute =>
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, suppress)), Is.True);
        }
        var tree = compilation.SyntaxTrees.Single();
        foreach (var owner in owners)
        {
            Assert.That(AnalyzerGeneratedCodePolicy.IsGenerated(
                owner, tree, compilation, default), Is.False);
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "effects");
        const string expected = "SP0024|Error|initializer-boundary.suppressed-effects.cs|[156..178)|SharpProof contract 'Contract.Result' has invalid argument '<placement>': expected use inside Contract.Ensures\nSP0024|Error|initializer-boundary.suppressed-effects.cs|[258..273)|SharpProof contract 'Contract.Old' has invalid argument '<placement>': expected use inside Contract.Ensures";
        var actual = DescribeDiagnostics(diagnostics);
        await TestContext.Out.WriteLineAsync(
            "Valid constructor/property suppression; effects-only; expected=[" +
            expected + "]; actual=[" + actual + "]");
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public async Task PartialAndMultipleDeclaratorPlacementKeepsDistinctTreeSpans()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            PartialASource, [], filePath: "initializer-boundary.partial-a.cs");
        compilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            PartialBSource,
            (CSharpParseOptions)compilation.SyntaxTrees.Single().Options,
            path: "initializer-boundary.partial-b.cs"));
        AssertCompilesAndEmits(compilation);
        var owners = await AssertBoundInitializerOwnersAsync(compilation, 3);
        Assert.That(owners.OfType<IFieldSymbol>().Count(), Is.EqualTo(2));
        Assert.That(owners.OfType<IPropertySymbol>().Count(), Is.EqualTo(1));
        foreach (var tree in compilation.SyntaxTrees)
        {
            var syntax = await tree.GetRootAsync();
            var result = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Single(invocation => invocation.Expression.ToString() == "Contract.Result<int>");
            Assert.That(result.Span.ToString(), Is.EqualTo("[99..121)"));
        }
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts");
        const string expected = "SP0024|Error|initializer-boundary.partial-a.cs|[99..121)|SharpProof contract 'Contract.Result' has invalid argument '<placement>': expected use inside Contract.Ensures\nSP0024|Error|initializer-boundary.partial-a.cs|[132..147)|SharpProof contract 'Contract.Old' has invalid argument '<placement>': expected use inside Contract.Ensures\nSP0024|Error|initializer-boundary.partial-b.cs|[99..121)|SharpProof contract 'Contract.Result' has invalid argument '<placement>': expected use inside Contract.Ensures";
        var actual = DescribeDiagnostics(diagnostics);
        await TestContext.Out.WriteLineAsync(
            "Two partial trees; first has two declarators; equal Result spans [99..121); " +
            "expected=[" + expected + "]; actual=[" + actual + "]");
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static async Task<IReadOnlyList<ISymbol>> AssertBoundInitializerOwnersAsync(
        CSharpCompilation compilation, int expectedCalls)
    {
        var owners = new List<ISymbol>();
        var api = compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract");
        Assert.That(api, Is.Not.Null);
        foreach (var tree in compilation.SyntaxTrees)
        {
            var syntax = await tree.GetRootAsync();
            var model = compilation.GetSemanticModel(tree);
            var invocations = syntax.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var invocation in invocations)
            {
                var target = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                Assert.That(target, Is.Not.Null);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(target!.Name, Is.AnyOf("Result", "Old"));
                    Assert.That(SymbolEqualityComparer.Default.Equals(
                        target.ContainingType, api), Is.True);
                }
                var initializer = invocation.Ancestors().OfType<EqualsValueClauseSyntax>().First();
                var declaredOwner = initializer.Parent switch
                {
                    PropertyDeclarationSyntax property => model.GetDeclaredSymbol(property),
                    VariableDeclaratorSyntax variable => model.GetDeclaredSymbol(variable),
                    _ => null
                };
                var rawOwner = model.GetEnclosingSymbol(invocation.SpanStart);
                var owner = rawOwner is IFieldSymbol { AssociatedSymbol: IPropertySymbol propertyOwner }
                    ? propertyOwner
                    : rawOwner;
                Assert.That(declaredOwner, Is.Not.Null);
                Assert.That(SymbolEqualityComparer.Default.Equals(owner, declaredOwner), Is.True);
                var root = model.GetOperation(invocation)!;
                while (root.Parent != null)
                {
                    root = root.Parent;
                }
                IEnumerable<ISymbol> initializedOwners = root switch
                {
                    IFieldInitializerOperation field => field.InitializedFields.Cast<ISymbol>(),
                    IPropertyInitializerOperation property => property.InitializedProperties.Cast<ISymbol>(),
                    _ => []
                };
                Assert.That(initializedOwners.Any(symbol =>
                    SymbolEqualityComparer.Default.Equals(symbol, owner)), Is.True);
                if (declaredOwner is IPropertySymbol)
                {
                    Assert.That(rawOwner, Is.AssignableTo<IFieldSymbol>());
                    var backing = (IFieldSymbol)rawOwner!;
                    Assert.That(backing.IsImplicitlyDeclared, Is.True);
                    Assert.That(backing.AssociatedSymbol, Is.AssignableTo<IPropertySymbol>());
                    Assert.That(root, Is.AssignableTo<IPropertyInitializerOperation>());
                }
                else
                {
                    Assert.That(rawOwner, Is.AssignableTo<IFieldSymbol>());
                    Assert.That(root, Is.AssignableTo<IFieldInitializerOperation>());
                }
                await TestContext.Out.WriteLineAsync(
                    tree.FilePath + invocation.Span + ": real API=" + target +
                    "; raw owner=" + rawOwner + "; declared owner=" + declaredOwner +
                    "; initializer root=" + root.Kind);
                owners.Add(owner!);
            }
        }
        Assert.That(owners, Has.Count.EqualTo(expectedCalls));
        return owners;
    }

    private static void AssertCompilesAndEmits(CSharpCompilation compilation)
    {
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(AnalyzerTestHost.EmitImage(compilation), Is.Not.Empty);
    }

    private static string DescribeDiagnostics(IEnumerable<Diagnostic> diagnostics)
    {
        return string.Join("\n", diagnostics
            .OrderBy(static diagnostic => diagnostic.Location.SourceTree?.FilePath, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(static diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .Select(static diagnostic =>
                $"{diagnostic.Id}|{diagnostic.Severity}|{diagnostic.Location.SourceTree?.FilePath}|" +
                $"{diagnostic.Location.SourceSpan}|{diagnostic.GetMessage(CultureInfo.InvariantCulture)}"));
    }
}
