using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class InterceptionCompilationBoundaryCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualInvocationRequiresCompilationToExcludePossibleInterception(bool featureEnabled)
    {
        var options = new CSharpParseOptions(LanguageVersion.CSharp13).WithFeatures(featureEnabled
            ? [new KeyValuePair<string, string>("InterceptorsNamespaces", "Interception")]
            : []);
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target() => Call(); private static int Call() => 17; }", options);
        var compilation = CSharpCompilation.Create("InterceptionBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var invocation = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = (IInvocationOperation)compilation.GetSemanticModel(tree).GetOperation(invocation)!;
        Assert.That(CSharpOperationSemantics.IsIntercepted(operation, compilation), Is.False);
        Assert.That(CSharpOperationSemantics.IsIntercepted(operation, null), Is.EqualTo(featureEnabled));
        using var stream = new MemoryStream();
        Assert.That(compilation.Emit(stream).Success, Is.True);
        var targetSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(declaration => declaration.Identifier.ValueText == "Target");
        var target = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(targetSyntax)!;
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), target);
        var clause = new RoslynTotalExpressionLowerer(context).LowerClause(operation);
        Assert.That(clause.Classification.IsExact, Is.False);
        Assert.That(clause.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedInvocationShape));
        var unrelated = CSharpCompilation.Create("UnrelatedInterceptionBoundary", [CSharpSyntaxTree.ParseText("public class Other { }")],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(CSharpOperationSemantics.IsIntercepted(operation, unrelated), Is.EqualTo(featureEnabled));
    }
}
