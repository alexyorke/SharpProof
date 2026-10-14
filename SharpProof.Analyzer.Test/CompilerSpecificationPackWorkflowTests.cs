using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerSpecificationPackWorkflowTests
{
    [TestCase(int.MinValue, int.MaxValue)]
    [TestCase(int.MaxValue, int.MinValue)]
    [TestCase(0, 0)]
    public void CapturedBclMaximumUsesTheSelectedPackAndCachesTheResolvedMethod(int left, int right)
    {
        var compilation = AnalyzerTestHost.CreateCompilation("internal class Subject { }", []);
        var method = compilation.GetTypeByMetadataName("System.Math")!.GetMembers("Max")
            .OfType<IMethodSymbol>().Single(candidate => candidate.Parameters.Length == 2 &&
                candidate.Parameters.All(parameter => parameter.Type.SpecialType == SpecialType.System_Int32));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var disabled = new CompilerSpecificationPackProvider(factory, (IEnumerable<string>?)null);
        Assert.That(disabled.ResolveTotal(method), Is.Null);
        Assert.That(disabled.ResolveTotal(method), Is.Null);
        var provider = new CompilerSpecificationPackProvider(factory, ["dotnet.scalar"]);
        var model = provider.ResolveTotal(method);
        Assert.That(model, Is.Not.Null);
        Assert.That(provider.ResolveTotal(method), Is.Not.Null);
        var integer = factory.GetOrCreateIntegerType(32, true);
        var rule = model!.Apply([factory.Integer(integer, left), factory.Integer(integer, right)]);
        var result = new IrInterpreter(factory).Evaluate(rule.Value);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rule.Classification.IsExact, Is.True);
            Assert.That(rule.Throws, Is.Empty);
            Assert.That(result.Status, Is.EqualTo(IrEvaluationStatus.Value));
            Assert.That(result.Value!.Integer, Is.EqualTo(Math.Max(left, right)));
            Assert.That(result.Value.Type, Is.EqualTo(integer));
            Assert.That(model.Apply([]).Classification.IsExact, Is.False);
            Assert.That(model.Apply([factory.Boolean(true), factory.Integer(integer, right)])
                .Classification.IsExact, Is.False);
            Assert.That(new CompilerSpecificationPackProvider(new IrFactory(), ["dotnet.scalar"])
                .ResolveTotal(method), Is.Null);
        }
    }

    [TestCase("dotnet.scalar", "dotnet.scalar", "identifiers must be unique")]
    [TestCase("dotnet.scalar", "missing.pack", "Unknown SharpProof specification pack")]
    public void InvalidConsumerPackSelectionsAreRejected(string first, string second, string message)
    {
        Assert.That((Action)(() => CompilerSpecificationPackProvider.ResolveConfiguration([first, second])),
            Throws.TypeOf<InvalidOperationException>().With.Message.Contains(message));
    }
}
