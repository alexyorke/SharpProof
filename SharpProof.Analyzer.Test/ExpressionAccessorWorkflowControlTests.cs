using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ExpressionAccessorWorkflowControlTests
{
    [Test]
    public async Task GetterBodyFormsPreserveEffectDiagnostics(
        [Values(false, true)] bool indexer,
        [Values("concise", "arrow", "block")] string shape,
        [Values(false, true)] bool mutates)
    {
        var name = indexer ? "this[int index]" : "Value";
        var member = shape switch
        {
            "concise" => $"[EnforcePure] public int {name} => Evaluate();",
            "arrow" => $"public int {name} {{ [EnforcePure] get => Evaluate(); }}",
            "block" => $"[EnforcePure] public int {name} {{ get {{ return Evaluate(); }} }}",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var helperBody = mutates ? "State = 1; return 1;" : "return 7;";
        var read = indexer ? "subject[0]" : "subject.Value";
        var source = $$"""
            using SharpProof.Attributes;
            public sealed class Subject {
                public static int State;
                private static int Evaluate() { {{helperBody}} }
                {{member}}
                public static int Run() {
                    var subject = new Subject();
                    return {{read}};
                }
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0002", "SP0047"],
            filePath: "Subject.cs");
        Assert.That(compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var declaration = root.DescendantNodes().OfType<BasePropertyDeclarationSyntax>().Single();
        var property = (IPropertySymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        Assert.That(property.GetMethod, Is.Not.Null);
        var attributes = shape == "arrow" ? property.GetMethod!.GetAttributes() : property.GetAttributes();
        var expectedAttribute = compilation.GetTypeByMetadataName("SharpProof.Attributes.EnforcePureAttribute");
        Assert.That(expectedAttribute, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            attributes.Single().AttributeClass, expectedAttribute), Is.True);
        RuntimeAssemblyTestHost.WithRuntimeAssembly("ExpressionAccessorWorkflowControl",
            AnalyzerTestHost.EmitImage(compilation), assembly =>
        {
            var type = assembly.GetType("Subject", throwOnError: true)!;
            var state = type.GetField("State", BindingFlags.Public | BindingFlags.Static)!;
            Assert.That(state.GetValue(null), Is.EqualTo(0));
            Assert.That(type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null),
                Is.EqualTo(mutates ? 1 : 7));
            Assert.That(state.GetValue(null), Is.EqualTo(mutates ? 1 : 0));
        });
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null,
            profile: "advisory", features: "effects");
        AnalyzerTestHost.AssertIds(diagnostics, mutates ? ["SP0002"] : []);
        if (mutates)
        {
            var expectedSpan = declaration is PropertyDeclarationSyntax propertySyntax
                ? propertySyntax.Identifier.Span
                : ((IndexerDeclarationSyntax)declaration).ThisKeyword.Span;
            Assert.That(diagnostics.Single().Location.SourceTree, Is.SameAs(tree));
            Assert.That(diagnostics.Single().Location.SourceSpan, Is.EqualTo(expectedSpan));
        }
    }
}
