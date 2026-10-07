using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class GeneratedLocalRuntimeWorkflowAuditTests
{
    [TestCase("advisory", false)]
    [TestCase("advisory", true)]
    [TestCase("strict", false)]
    [TestCase("strict", true)]
    public async Task SelectedLocalRuntimeMutationRequiresDiagnostic(string profile, bool generated)
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int State;
                public static void Run() {
                    [EnforcePure]
                    static void Mutate() { State++; }
                    Mutate();
                }
            }
            """;
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0002"],
            filePath: generated ? "Subject.g.cs" : "Subject.cs");
        var compilerErrors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.That(compilerErrors, Is.Empty);
        var tree = compilation.SyntaxTrees.Single();
        var root = await tree.GetRootAsync();
        var declaration = root.DescendantNodes().OfType<LocalFunctionStatementSyntax>().Single();
        var operation = (ILocalFunctionOperation)compilation.GetSemanticModel(tree).GetOperation(declaration)!;
        var expectedAttribute = compilation.GetTypeByMetadataName("SharpProof.Attributes.EnforcePureAttribute");
        Assert.That(expectedAttribute, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(
            operation.Symbol.GetAttributes().Single().AttributeClass, expectedAttribute), Is.True);
        var image = AnalyzerTestHost.EmitImage(compilation);
        RuntimeAssemblyTestHost.WithRuntimeAssembly("GeneratedLocalRuntimeWorkflowAudit", image, assembly =>
        {
            var type = assembly.GetType("Subject", throwOnError: true)!;
            var state = type.GetField("State", BindingFlags.Public | BindingFlags.Static)!;
            Assert.That(state.GetValue(null), Is.EqualTo(0));
            type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, null);
            Assert.That(state.GetValue(null), Is.EqualTo(1), "Compiled local function mutates global state.");
        });
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, mode: null,
            profile: profile, features: "effects");
        AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
    }
}
