using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Dataflow;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class SpecResultDomainProjectionTests
{
    [Test]
    public void NonNullEmptySequenceProjectsToBooleanAndIntegerFacts()
    {
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(
            factory.IntegerType);
        var result = factory.CreateVariable("result", sequenceType);
        var template = CreateTemplate(
            IrTypeKind.Sequence,
            SpecNullness.NonNull,
            SpecCardinality.Empty);

        var succeeded = SpecResultDomainProjection.TryCreate(
            factory,
            template,
            result,
            out var projection,
            out var evidence);
        var contract = factory.Binary(
            IrBinaryOperator.AndAlso,
            factory.Binary(
                IrBinaryOperator.NotEqual,
                factory.Variable(result),
                factory.Null(sequenceType)),
            factory.Binary(
                IrBinaryOperator.Equal,
                factory.Length(factory.Variable(result)),
                factory.Integer(0)));
        var rewritten = SpecResultDomainProjection.Rewrite(
            factory,
            contract,
            ImmutableDictionary<IrVarId, SpecResultProjection>.Empty.Add(
                result,
                projection));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(succeeded, Is.True);
            Assert.That(projection, Is.Not.EqualTo(default(SpecResultProjection)));
            Assert.That(projection.NonNullVariable, Is.Not.Null);
            Assert.That(projection.LengthVariable, Is.Not.Null);
            Assert.That(evidence, Has.Length.EqualTo(2));
            Assert.That(rewritten, Is.TypeOf<IrBinaryTerm>());
        }
        var conjunction = (IrBinaryTerm)rewritten;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                conjunction.Left,
                Is.TypeOf<IrVariableTerm>());
            Assert.That(
                ((IrBinaryTerm)conjunction.Right).Left,
                Is.TypeOf<IrVariableTerm>());
        }
    }

    [Test]
    public void UnknownNullnessCreatesNoProxyOrEvidence()
    {
        var factory = new IrFactory();
        var result = factory.CreateVariable(
            "result",
            factory.ObjectType);
        var template = CreateTemplate(
            IrTypeKind.Reference,
            SpecNullness.Unknown,
            SpecCardinality.NotApplicable);

        var succeeded = SpecResultDomainProjection.TryCreate(
            factory,
            template,
            result,
            out var projection,
            out var evidence);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(succeeded, Is.True);
            Assert.That(projection, Is.EqualTo(default(SpecResultProjection)));
            Assert.That(evidence, Is.Empty);
        }
    }

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
    public void RewriteVisitsSequenceAccessAndOpaqueChildren()
    {
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var result = factory.CreateVariable("result", sequenceType);
        var index = factory.Integer(0);
        var template = CreateTemplate(
            IrTypeKind.Sequence,
            SpecNullness.NonNull,
            SpecCardinality.Empty);
        Assert.That(
            SpecResultDomainProjection.TryCreate(
                factory, template, result, out var projection, out _),
            Is.True);
        var proxy = factory.Variable(projection.NonNullVariable!.Value);
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "Use",
            factory.BooleanType,
            isStatic: true,
            factory.IntegerType);
        var opaque = factory.PureOpaque(
            member,
            null,
            factory.Length(factory.Variable(result)));
        var root = factory.Binary(
            IrBinaryOperator.AndAlso,
            factory.Binary(
                IrBinaryOperator.NotEqual,
                factory.SequenceAccess(factory.Variable(result), index),
                factory.Integer(0)),
            opaque);

        var rewritten = SpecResultDomainProjection.Rewrite(
            factory,
            root,
            ImmutableDictionary<IrVarId, SpecResultProjection>.Empty.Add(result, projection));

        Assert.That(rewritten, Is.TypeOf<IrBinaryTerm>());
        var rewrittenBinary = (IrBinaryTerm)rewritten;
        var rewrittenOpaque = (IrOpaqueTerm)rewrittenBinary.Right;
        Assert.That(rewrittenOpaque.Arguments, Has.Length.EqualTo(1));
        Assert.That(
            rewrittenOpaque.Arguments[0],
            Is.EqualTo(factory.Variable(projection.LengthVariable!.Value)));
        Assert.That(proxy, Is.Not.Null);
    }

    [Test]
    public void CardinalityWithoutNonNullOrSequenceTypeFailsClosed()
    {
        var factory = new IrFactory();
        var sequence = factory.CreateVariable(
            "sequence",
            factory.GetOrCreateSequenceType(factory.IntegerType));
        var reference = factory.CreateVariable(
            "reference",
            factory.ObjectType);
        var nullableCardinality = CreateTemplate(
            IrTypeKind.Sequence,
            SpecNullness.MaybeNull,
            SpecCardinality.Empty);
        var referenceCardinality = CreateTemplate(
            IrTypeKind.Reference,
            SpecNullness.NonNull,
            SpecCardinality.Empty);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                SpecResultDomainProjection.TryCreate(
                    factory,
                    nullableCardinality,
                    sequence,
                    out _,
                    out _),
                Is.False);
            Assert.That(
                SpecResultDomainProjection.TryCreate(
                    factory,
                    referenceCardinality,
                    reference,
                    out _,
                    out _),
                Is.False);
        }
    }

    [Test]
    public void IntervalProjectionPreservesFiniteBoundsAndTop()
    {
        var factory = new IrFactory();
        var value = factory.Variable(
            factory.CreateVariable("value", factory.IntegerType));

        var finite = SpecResultDomainProjection.TryCreateIntervalPredicate(
            factory,
            value,
            IntervalDomain.Instance.Range(1, 5),
            out var finitePredicate);
        var top = SpecResultDomainProjection.TryCreateIntervalPredicate(
            factory,
            value,
            IntervalDomain.Instance.Top,
            out var topPredicate);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(finite, Is.True);
            Assert.That(finitePredicate, Is.TypeOf<IrBinaryTerm>());
            Assert.That(
                ((IrBinaryTerm)finitePredicate!).Operator,
                Is.EqualTo(IrBinaryOperator.AndAlso));
            Assert.That(top, Is.True);
            Assert.That(topPredicate, Is.Null);
            Assert.That(
                SpecResultDomainProjection.TryCreateIntervalPredicate(
                    factory,
                    value,
                    IntervalDomain.Instance.Bottom,
                    out _),
                Is.False);
        }
    }

    private static ApiSpecTemplate CreateTemplate(
        IrTypeKind resultType,
        SpecNullness nullness,
        SpecCardinality cardinality,
        IEnumerable<SpecTermDeclaration>? postconditions = null)
    {
        var evidence = new SpecEvidence(
            SpecEvidenceKind.Documented,
            "worker-domain-projection-test");
        return ApiSpecTable.Create([
            new ApiSpecDeclaration(
                new ApiSpecTarget(
                    "test.result",
                    "M:Test.Result",
                    "Test",
                    SpecTargetMemberKind.Method,
                    "Result",
                    true,
                    0,
                    null,
                    [],
                    resultType,
                    [new ApiSpecAssemblyIdentity("Test", string.Empty)]),
                new ApiSpecFacets(
                    new SpecEffectFacet(SpecEffect.None, evidence),
                    new SpecAllocationFacet(
                        SpecAllocationBehavior.Unknown,
                        evidence),
                    new SpecThrowFacet(
                        SpecThrowBehavior.DoesNotThrow,
                        [],
                        evidence),
                    new SpecNullnessFacet(nullness, evidence),
                    new SpecCardinalityFacet(
                        cardinality,
                        null,
                        evidence)),
                [.. (postconditions ?? []).Select(
                    condition => new SpecPostconditionDeclaration(
                        condition,
                        evidence))])
        ]).Templates.Single();
    }
}
