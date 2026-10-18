using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OperationSupportStageBoundaryCoverageTests
{
    [Test]
    public void UndefinedSupportStageRejectsActualCompiledLiteralKind()
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target() => 17; }");
        var compilation = CSharpCompilation.Create("OperationStageBoundary", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var literal = tree.GetRoot().DescendantNodes().OfType<LiteralExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(literal)!;
        Assert.That(operation.Kind, Is.EqualTo(OperationKind.Literal));
        Assert.That(CSharpOperationSemantics.IsSupported(OperationSupportStage.ContractExpressionLowering, operation.Kind), Is.True);
        Assert.That(CSharpOperationSemantics.IsSupported(OperationSupportStage.EffectDiscovery, operation.Kind), Is.True);
        var invalid = (OperationSupportStage)int.MaxValue;
        var exception = Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
            _ = CSharpOperationSemantics.IsSupported(invalid, operation.Kind)));
        Assert.That(exception!.ParamName, Is.EqualTo("stage"));
        Assert.That(exception.ActualValue, Is.EqualTo(invalid));
    }
}
