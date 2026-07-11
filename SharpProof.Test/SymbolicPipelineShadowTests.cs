using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SearchLib.Smt;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicPipelineShadowTests
{
    [Test]
    public void ShadowMode_RecordsDisagreementWithoutChangingLegacyResult()
    {
        var (semanticModel, condition) = CreateCondition(@"
public sealed class Target
{
    private int _value;
    private bool Ready => _value > 0;

    public void M()
    {
        if (this.Ready) { }
    }
}");

        var legacyFormula = Translate(condition, semanticModel, SymbolicPipelineMode.Legacy);
        var newFormula = Translate(condition, semanticModel, SymbolicPipelineMode.New);

        using (SymbolicPipelineTestControl.UseMode(SymbolicPipelineMode.Shadow))
        {
            Assert.That(
                SymbolicReachabilityService.TryTranslateConditionFormula(
                    condition,
                    semanticModel,
                    CancellationToken.None,
                    out var shadowFormula),
                Is.True);
            Assert.That(shadowFormula, Is.EqualTo(legacyFormula));
            Assert.That(SymbolicPipelineTestControl.Disagreements.Length, Is.EqualTo(1));
            Assert.That(SymbolicPipelineTestControl.Disagreements[0].Stage, Is.EqualTo("condition-lowering"));
            Assert.That(SymbolicPipelineTestControl.Disagreements[0].LegacyFormula, Is.EqualTo(legacyFormula));
            Assert.That(SymbolicPipelineTestControl.Disagreements[0].NewFormula, Is.EqualTo(newFormula));
        }

        Assert.That(newFormula, Is.Not.EqualTo(legacyFormula));
        Assert.That(SymbolicPipelineTestControl.Mode, Is.EqualTo(SymbolicPipelineMode.Legacy));
        Assert.That(SymbolicPipelineTestControl.Disagreements, Is.Empty);
    }

    [Test]
    public void NestedTestModes_RestoreThePreviousScope()
    {
        using (SymbolicPipelineTestControl.UseMode(SymbolicPipelineMode.New))
        {
            Assert.That(SymbolicPipelineTestControl.Mode, Is.EqualTo(SymbolicPipelineMode.New));
            using (SymbolicPipelineTestControl.UseMode(SymbolicPipelineMode.Shadow))
                Assert.That(SymbolicPipelineTestControl.Mode, Is.EqualTo(SymbolicPipelineMode.Shadow));
            Assert.That(SymbolicPipelineTestControl.Mode, Is.EqualTo(SymbolicPipelineMode.New));
        }

        Assert.That(SymbolicPipelineTestControl.Mode, Is.EqualTo(SymbolicPipelineMode.Legacy));
    }

    private static SmtFormula Translate(
        ExpressionSyntax condition,
        SemanticModel semanticModel,
        SymbolicPipelineMode mode)
    {
        using (SymbolicPipelineTestControl.UseMode(mode))
        {
            Assert.That(
                SymbolicReachabilityService.TryTranslateConditionFormula(
                    condition,
                    semanticModel,
                    CancellationToken.None,
                    out var formula),
                Is.True);
            return formula!;
        }
    }

    private static (SemanticModel SemanticModel, ExpressionSyntax Condition) CreateCondition(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "ShadowPipelineProbe",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var semanticModel = compilation.GetSemanticModel(tree);
        var condition = tree.GetRoot().DescendantNodes().OfType<IfStatementSyntax>().Single().Condition;
        return (semanticModel, condition);
    }
}
