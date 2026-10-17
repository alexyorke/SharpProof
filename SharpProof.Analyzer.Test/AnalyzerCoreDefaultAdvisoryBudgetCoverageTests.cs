using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreDefaultAdvisoryBudgetCoverageTests
{
    [TestCase(8, false)]
    [TestCase(300, true)]
    public async Task OrdinaryBooleanPropagationRetainsDefaultAdvisoryBudgetBehavior(int flagCount, bool incomplete)
    {
        var declarations = string.Join(" ", Enumerable.Range(1, flagCount).Select(index => "bool b" + index + " = false;"));
        var propagation = string.Join(" ", Enumerable.Range(1, flagCount).Reverse().Select(index =>
            "b" + index + " = b" + (index - 1) + ";"));
        var source = "using SharpProof.Attributes; public static class Fixture { " +
            "private static int Need(int value) { Contract.Requires(value > 0); return value; } " +
            "public static int Target(bool again) { bool b0 = true; " + declarations +
            " while (again) { " + propagation + " } return Need(0); } }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"]);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", analyzer: new SharpProofAnalyzer(factory));
        Assert.That(factory.Session, Is.Not.Null);
        var actual = (CSharpCompilation)factory.Session!.Compilation;
        var root = await actual.SyntaxTrees.Single().GetRootAsync().ConfigureAwait(false);
        var declaration = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Target");
        var caller = (IMethodSymbol)actual.GetSemanticModel(declaration.SyntaxTree).GetDeclaredSymbol(declaration)!;
        var (body, gap) = AdvisoryLowering.Lower(actual, caller, declaration, TestContext.CurrentContext.CancellationToken);
        Assert.That(gap, Is.Null);
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Lowering.IsExact, Is.True);
        Assert.That(body.Program.Blocks.Sum(block => block.Instructions.Length), Is.LessThanOrEqualTo(TotalBodyLowering.MaximumWork));
        var callPreconditionsProperty = body.Lowering.GetType().GetProperty(
            "CallPreconditions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.That(callPreconditionsProperty, Is.Not.Null);
        var callPreconditions = callPreconditionsProperty!.GetValue(body.Lowering);
        Assert.That(callPreconditions, Is.Not.Null);
        Assert.That(callPreconditions, Is.Not.Empty);
        if (incomplete)
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(factory.GetNamedOutcome("Target"), Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
        }
        else
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0027");
            Assert.That(factory.GetNamedOutcome("Target"), Is.EqualTo(AnalyzerSemanticOutcome.Refuted));
        }
    }
}
