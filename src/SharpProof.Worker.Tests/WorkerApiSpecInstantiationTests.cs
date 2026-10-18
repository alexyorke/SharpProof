using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerApiSpecInstantiationTests
{
    [Test]
    public void ApiSpecNullComparisonPreservesTheWorkerResultType()
    {
        var resultDeclaration = new SpecVariableDeclaration(
            SpecVariableRole.Result,
            -1,
            IrTypeKind.Reference);
        var template = CreateTemplate(
            IrTypeKind.Reference,
            SpecNullness.Unknown,
            SpecCardinality.NotApplicable,
            [new SpecBinaryDeclaration(
                IrBinaryOperator.NotEqual,
                resultDeclaration,
                new SpecNullDeclaration(IrTypeKind.Reference),
                IrTypeKind.Boolean)]);
        var factory = new IrFactory();
        var resultType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "WorkerResult<string>");
        var result = factory.Variable(
            factory.CreateVariable("result", resultType));

        var instantiated = ApiSpecInstantiator.InstantiatePostconditions(
            template,
            factory,
            new Dictionary<SpecVarId, IrTerm>
            {
                [template.Result!.Value] = result
            });

        Assert.That(instantiated.Status, Is.EqualTo(SpecInstantiationStatus.Succeeded));
        var comparison = instantiated.Postconditions.Single() as IrBinaryTerm;
        Assert.That(comparison, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(comparison!.Left.Type, Is.EqualTo(resultType));
            Assert.That(comparison.Right.Type, Is.EqualTo(resultType));
        }
    }


    [Test]
    public void ReferenceResultRejectsSequenceCardinality()
    {
        var exception = Assert.Throws<ArgumentException>(new Action(() =>
        {
            _ = CreateTemplate(
                IrTypeKind.Reference,
                SpecNullness.NonNull,
                SpecCardinality.Empty);
        }));

        Assert.That(exception!.Message, Does.Contain("cardinality facet"));
    }

    private static ApiSpecTemplate CreateTemplate(
        IrTypeKind resultType,
        SpecNullness nullness,
        SpecCardinality cardinality,
        IEnumerable<SpecTermDeclaration>? postconditions = null)
    {
        return WorkerApiSpecTestFixtures.CreateTemplate(
            "test.result",
            "M:Test.Result",
            "Test",
            "worker-domain-projection-test",
            resultType,
            nullness,
            cardinality,
            postconditions);
    }
}
