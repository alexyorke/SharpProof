using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class GenericSourceCallBudgetTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ScalarTypeSpecializationRespectsTheSharedConstructionLimit(bool containingType)
    {
        var helper = containingType ? "static class Reader<T> { public static T Reset() => default(T); }" : "static T Reset<T>() => default(T);";
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target() => " +
            (containingType ? "Reader<int>.Reset()" : "Reset<int>()") + "; " + helper + " }");
        var compilation = CSharpCompilation.Create("GenericBudget", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method =>
            method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var invocation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        var successful = false;
        for (var available = 0; available <= 128; available++)
        {
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
            var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
            Assert.That(session.Spend(RoslynTotalProgramLowerer.MaximumRegionSteps - available), Is.True);
            var prepared = session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out var graph);
            Assert.That(session.ConstructionLimitExceeded, Is.EqualTo(!prepared));
            if (prepared)
            {
                Assert.That(frame, Is.Not.Null);
                Assert.That(graph, Is.Not.Null);
                successful = true;
            }
            else
            {
                Assert.That(successful, Is.False, "More construction budget must not invalidate an admissible helper.");
                Assert.That(frame, Is.Null);
                Assert.That(graph, Is.Null);
            }
        }
        Assert.That(successful, Is.True, "This small closed scalar helper must fit an ordinary construction allowance.");
    }
}
