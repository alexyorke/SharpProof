using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ExpressionAccessorHelperWorkflowAuditTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task SelectedGetterMutationRequiresDiagnostic(bool indexer, bool concise)
    {
        var memberName = indexer ? "this[int index]" : "Value";
        var body = concise ? "=> Mutate();" : "{ get { return Mutate(); } }";
        var read = indexer ? "subject[0]" : "subject.Value";
        var source = $$"""
            using SharpProof.Attributes;
            public sealed class Subject {
                public static int State;
                private static int Mutate() { State = 1; return 1; }
                [EnforcePure]
                public int {{memberName}} {{body}}
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
        var expectedAttribute = compilation.GetTypeByMetadataName("SharpProof.Attributes.EnforcePureAttribute");
        Assert.That(expectedAttribute, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            property.GetAttributes().Single().AttributeClass, expectedAttribute), Is.True);
        RuntimeAssemblyTestHost.WithRuntimeAssembly("ExpressionAccessorHelperWorkflowAudit",
            AnalyzerTestHost.EmitImage(compilation), assembly =>
        {
            var type = assembly.GetType("Subject", throwOnError: true)!;
            var state = type.GetField("State", BindingFlags.Public | BindingFlags.Static)!;
            Assert.That(state.GetValue(null), Is.EqualTo(0));
            Assert.That(type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null),
                Is.EqualTo(1));
            Assert.That(state.GetValue(null), Is.EqualTo(1), "Compiled getter mutates global state.");
        });
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null,
            profile: "advisory", features: "effects");
        AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
    }
}
