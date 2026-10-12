using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreSourceCalleeWorkCoverageTests
{
    [TestCase(8, false)]
    [TestCase(2200, true)]
    public async Task OrdinaryCalleeConversionsRetainDefaultWorkAdmission(int elementCount, bool incomplete)
    {
        var elements = string.Join(",", Enumerable.Repeat("0", elementCount));
        var source = "using SharpProof.Attributes; public static class Fixture { " +
            "private static int Helper() { long[] values = new long[] { " + elements +
            " }; return values.Length; } [EnforcePure] public static int Target() => Helper(); }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0002"]);
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var factory = new RecordingSessionFactory();
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(compilation, "effects", analyzer: new SharpProofAnalyzer(factory));
        Assert.That(factory.Session, Is.Not.Null);
        var actual = (CSharpCompilation)factory.Session!.Compilation;
        var root = await actual.SyntaxTrees.Single().GetRootAsync().ConfigureAwait(false);
        var helper = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Helper");
        var model = actual.GetSemanticModel(helper.SyntaxTree);
        var helperSymbol = (IMethodSymbol)model.GetDeclaredSymbol(helper)!;
        Assert.That(SymbolEqualityComparer.Default.Equals(helperSymbol.ContainingAssembly, actual.Assembly), Is.True);
        Assert.That(helperSymbol.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(helper.DescendantNodesAndSelf().Count(), Is.LessThan(TotalBodyLowering.MaximumWork));
        var helperOperation = model.GetOperation(helper);
        Assert.That(helperOperation, Is.Not.Null);
        var operationCount = helperOperation!.DescendantsAndSelf().Count();
        Assert.That(operationCount, incomplete ? Is.GreaterThan(TotalBodyLowering.MaximumWork) : Is.LessThan(TotalBodyLowering.MaximumWork));
        var target = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Target");
        var targetSymbol = (IMethodSymbol)model.GetDeclaredSymbol(target)!;
        var (body, gap) = AdvisoryLowering.Lower(actual, targetSymbol, target, CancellationToken.None);
        Assert.That(gap, Is.Null);
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.Lowering.IsExact, Is.True);
        Assert.That(body.Program.Blocks.Sum(block => block.Instructions.Length), Is.LessThanOrEqualTo(TotalBodyLowering.MaximumWork));
        var calls = body.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrCallInstruction>().ToArray();
        if (incomplete)
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0002");
            Assert.That(calls, Has.Length.EqualTo(1));
            Assert.That(IrOpaqueCallSite.Effects(body.Program.Factory, calls.Single().Operation), Is.EqualTo(IrOpaqueCallEffects.All));
        }
        else
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(calls, Is.Empty);
        }
        Assert.That(factory.GetNamedOutcome("Target"), Is.EqualTo(AnalyzerSemanticOutcome.Unknown));
    }
}
