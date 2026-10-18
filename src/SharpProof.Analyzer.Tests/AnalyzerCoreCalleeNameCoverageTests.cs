using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreCalleeNameCoverageTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void FormatterUsesOnlyARealResolvableCompilerCallee(bool referenced, bool useCallsite)
    {
        var origin = AnalyzerTestHost.CreateCompilation(
            "public static class RemoteApi { public static int Value() => 1; }", [])
            .WithAssemblyName("AnalyzerCoreCalleeNameOrigin");
        var callee = origin.GetTypeByMetadataName("RemoteApi")!.GetMembers("Value").OfType<IMethodSymbol>().Single();
        var identity = CompilerIdentityBridge.CreateSymbolDisplay(callee);
        var reference = origin.ToMetadataReference();
        var caller = AnalyzerTestHost.CreateCompilation(
            referenced
                ? "public static class Fixture { public static int Target() => RemoteApi.Value(); }"
                : "public static class Fixture { public static int Target() => 1; }",
            [], additionalReferences: referenced ? [reference] : [])
            .WithAssemblyName("AnalyzerCoreCalleeNameCaller");
        Assert.That(origin.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        Assert.That(caller.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var location = Location.None;
        if (referenced)
        {
            Assert.That(caller.GetAssemblyOrModuleSymbol(reference), Is.Not.Null);
            var imported = caller.GetTypeByMetadataName("RemoteApi")!.GetMembers("Value").OfType<IMethodSymbol>().Single();
            Assert.That(CompilerIdentityBridge.CreateSymbolDisplay(imported), Is.EqualTo(identity));
            if (useCallsite)
            {
                location = caller.SyntaxTrees.Single().GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Single().GetLocation();
            }
        }
        else
        {
            Assert.That(caller.GetTypeByMetadataName("RemoteApi"), Is.Null);
        }
        Assert.That(AdvisoryRequiresDiagnostics.CalleeName(caller, location, identity, TestContext.CurrentContext.CancellationToken),
            Is.EqualTo(referenced ? callee.Name : identity));
    }
}
