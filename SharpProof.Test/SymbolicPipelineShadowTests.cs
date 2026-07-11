using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SearchLib.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

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
    public void M(int[] values)
    {
        if (values is [1]) { }
    }
}");

        var legacy = Translate(condition, semanticModel, SymbolicPipelineMode.Legacy);
        var current = Translate(condition, semanticModel, SymbolicPipelineMode.New);
        Assert.That(legacy.Succeeded, Is.True);
        Assert.That(current.Succeeded, Is.False);

        using (SymbolicPipelineTestControl.UseMode(SymbolicPipelineMode.Shadow))
        {
            Assert.That(
                SymbolicReachabilityService.TryTranslateConditionFormula(
                    condition,
                    semanticModel,
                    CancellationToken.None,
                    out var shadowFormula),
                Is.True);
            Assert.That(shadowFormula, Is.EqualTo(legacy.Formula));
            Assert.That(SymbolicPipelineTestControl.Disagreements.Length, Is.EqualTo(1));
            Assert.That(SymbolicPipelineTestControl.Disagreements[0].Stage, Is.EqualTo("condition-lowering"));
            Assert.That(SymbolicPipelineTestControl.Disagreements[0].LegacyFormula, Is.EqualTo(legacy.Formula));
            Assert.That(SymbolicPipelineTestControl.Disagreements[0].NewSucceeded, Is.False);
            Assert.That(SymbolicPipelineTestControl.Disagreements[0].NewFormula, Is.Null);
        }

        Assert.That(SymbolicPipelineTestControl.Mode, Is.EqualTo(SymbolicPipelineMode.Legacy));
        Assert.That(SymbolicPipelineTestControl.Disagreements, Is.Empty);
    }

    [Test]
    public void ExactIrCondition_WinsOverSourcePropertyCompatibilityFormula()
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
        var context = new SymbolicLoweringContext(semanticModel, CancellationToken.None);
        var lowering = SymbolicSemanticPipeline.LowerCondition(condition, context);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(
            SymbolicIrFormulaEncoder.TryEncode(lowering.Value!, out var exactFormula),
            Is.True);

        var selected = Translate(condition, semanticModel, SymbolicPipelineMode.Legacy);

        Assert.That(selected.Succeeded, Is.True);
        Assert.That(selected.Formula, Is.EqualTo(exactFormula));
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

    private static (bool Succeeded, SmtFormula? Formula) Translate(
        ExpressionSyntax condition,
        SemanticModel semanticModel,
        SymbolicPipelineMode mode)
    {
        using (SymbolicPipelineTestControl.UseMode(mode))
        {
            var succeeded = SymbolicReachabilityService.TryTranslateConditionFormula(
                condition,
                semanticModel,
                CancellationToken.None,
                out var formula);
            return (succeeded, formula);
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
