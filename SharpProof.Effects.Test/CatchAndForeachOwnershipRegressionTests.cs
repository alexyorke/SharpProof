using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class CatchAndForeachOwnershipRegressionTests
{
    [Test]
    public void CatchVariableFieldWriteRemainsObservable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using System;

            public sealed class MyException : Exception
            {
                public bool Handled;
            }

            public static class Sample
            {
                public static void Catch(MyException error)
                {
                    try
                    {
                        throw error;
                    }
                    catch (MyException ex)
                    {
                        ex.Handled = true;
                    }
                }
            }
            """);

        Assert.That(
            ClassifyNamedLocal(compilation, "Catch", "ex").IsUnknown,
            Is.True,
            "a caught exception receives its value through synthesized runtime state");

        var result = EffectTestHost.AnalyzeSample(compilation, "Catch");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Writes.IsUnknown, Is.True);
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.False,
                "a field write through a caught exception must remain observable");
        }
    }

    [Test]
    public void ForeachVariableFieldWriteRemainsObservable()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            public sealed class Box
            {
                public int Count;
            }

            public static class Sample
            {
                public static void Iterate(Box[] values)
                {
                    foreach (var item in values)
                    {
                        item.Count = 0;
                    }
                }
            }
            """);

        Assert.That(
            ClassifyNamedLocal(compilation, "Iterate", "item").IsUnknown,
            Is.True,
            "a foreach item receives its value through synthesized runtime state");

        var result = EffectTestHost.AnalyzeSample(compilation, "Iterate");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Summary.Writes.IsUnknown, Is.True);
            Assert.That(
                EffectContractMappings.IsObservablePure(result.Summary),
                Is.False,
                "a field write through a foreach item must remain observable");
        }
    }

    private static EffectRegionSet ClassifyNamedLocal(
        Compilation compilation,
        string methodName,
        string localName)
    {
        var method = EffectTestHost.SampleMethod(compilation, methodName);
        var root = EffectTestHost.RootOperation(compilation, method);
        var classifier = new ConversionOwnershipClassifier(
            method,
            compilation,
            new CoalesceAssignmentFlowCaptures(),
            new ConditionalTruthOperatorFlowCaptures(),
            new CreationFlowCaptures());
        var relevantOperations = root.Descendants()
            .Where(operation =>
                !ConversionOwnershipClassifier.IsInsideNestedCallable(
                    operation,
                    root))
            .ToImmutableArray();

        classifier.BuildLocalRegions(
            static _ => true,
            relevantOperations);

        var reference = root.Descendants()
            .OfType<ILocalReferenceOperation>()
            .Single(operation => operation.Local.Name == localName);
        return classifier.ClassifyRegion(reference);
    }
}
