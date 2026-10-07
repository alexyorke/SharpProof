using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ExpressionAccessorOverflowWorkflowControlTests
{
    [Test]
    public async Task GetterWrappersRetainCheckedOverflow(
        [Values("concise", "arrow", "block")] string shape,
        [Values(false, true)] bool throws)
    {
        var expression = throws ? "checked(Input + 1)" : "unchecked(Input + 1)";
        var member = shape switch
        {
            "concise" => $"[DoesNotThrow] public int Value => {expression};",
            "arrow" => $"public int Value {{ [DoesNotThrow] get => {expression}; }}",
            "block" => $"[DoesNotThrow] public int Value {{ get {{ return {expression}; }} }}",
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        var source = $$"""
            using SharpProof.Attributes;
            public sealed class Subject {
                public static int Input;
                {{member}}
                public static int Run(int value) {
                    Input = value;
                    return new Subject().Value;
                }
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0046", "SP0047"],
            filePath: "OverflowSubject.cs");
        Assert.That(compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var declaration = root.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var property = (IPropertySymbol)model.GetDeclaredSymbol(declaration)!;
        Assert.That(property.GetMethod, Is.Not.Null);
        var attributes = shape == "arrow" ? property.GetMethod!.GetAttributes() : property.GetAttributes();
        var expectedAttribute = compilation.GetTypeByMetadataName("SharpProof.Attributes.DoesNotThrowAttribute");
        Assert.That(expectedAttribute, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            attributes.Single().AttributeClass, expectedAttribute), Is.True);
        var binarySyntax = root.DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var binary = (IBinaryOperation)model.GetOperation(binarySyntax)!;
        Assert.That(binary.IsChecked, Is.EqualTo(throws));
        RuntimeAssemblyTestHost.WithRuntimeAssembly("ExpressionAccessorOverflowWorkflowControl",
            AnalyzerTestHost.EmitImage(compilation), assembly =>
        {
            var type = assembly.GetType("Subject", throwOnError: true)!;
            var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!
                .CreateDelegate<Func<int, int>>();
            Assert.That(run(0), Is.EqualTo(1));
            if (throws)
            {
                Assert.Throws<OverflowException>((Action)(() => run(int.MaxValue)));
            }
            else
            {
                Assert.That(run(int.MaxValue), Is.EqualTo(int.MinValue));
            }
        });
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null,
            profile: "advisory", features: "effects");
        AnalyzerTestHost.AssertIds(diagnostics, throws ? ["SP0046"] : []);
        if (throws)
        {
            AnalyzerTestHost.AssertMessageContains(diagnostics.Single(), "System.OverflowException");
            Assert.That(diagnostics.Single().Location.SourceTree, Is.SameAs(tree));
        }
    }
}
