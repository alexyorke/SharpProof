using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PostconditionObligationBuilderTests
{
    [Test]
    public void EntrySourceDomainsDistinguishFullInt64AndBoundedIntervals()
    {
        var factory = new IrFactory();
        var fullRangeValue = factory.CreateVariable(
            "full-range",
            factory.IntegerType);
        var boundedValue = factory.CreateVariable(
            "bounded",
            factory.IntegerType);
        var target = new CompilerCallablePreparation(
            factory,
            new WorkerCallableManifestEntry
            {
                CallableId = "M:Test.Subject.Entry",
                ClaimIds = []
            },
            [],
            [
                new CompilerCanonicalVariable(
                    CompilerVariableRole.Parameter,
                    0,
                    fullRangeValue,
                    null,
                    new CompilerIntegerInterval(
                        long.MinValue,
                        long.MaxValue),
                    "parameter:0"),
                new CompilerCanonicalVariable(
                    CompilerVariableRole.Parameter,
                    1,
                    boundedValue,
                    null,
                    new CompilerIntegerInterval(
                        int.MinValue,
                        int.MaxValue),
                    "parameter:1")
            ],
            WorkerClaimReason.None,
            CompilerPreparedBody.Trivial());

        var result = CallableEvidenceBuilder.BuildEntry(
            target,
            WorkerBudgets.DefaultMaximumExpressionDepth);

        Assert.That(result.IsSuccess, Is.True);
        var evidence = result.Evidence!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(evidence.Assumptions, Has.Length.EqualTo(1));
            Assert.That(
                evidence.Labels.Values,
                Is.EqualTo(["domain:parameter:1"]));
            Assert.That(
                IrTermAnalysis.CollectVariables(
                    evidence.Assumptions.Single().Predicate),
                Is.EqualTo([boundedValue]));
        }
    }

    [Test]
    public void FullInt64SourceDomainAddsNoAssumption()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable(
            "value",
            factory.IntegerType);
        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        var entryDomainAssumptions =
            ImmutableArray.CreateBuilder<Assumption>();
        var labels = new Dictionary<ProofJustification, string>();

        var succeeded = PostconditionObligationBuilder
            .TryAddSourceDomainAssumptions(
                factory,
                [new CompilerCanonicalVariable(
                    CompilerVariableRole.Result,
                    -1,
                    value,
                    null,
                    new CompilerIntegerInterval(
                        long.MinValue,
                        long.MaxValue),
                    "value")],
                [new SymbolicReturn(
                    factory.Boolean(false),
                    null,
                    ImmutableDictionary<IrVarId, IrTerm>.Empty)],
                ImmutableDictionary<IrVarId, SpecResultProjection>.Empty,
                assumptions,
                entryDomainAssumptions,
                labels);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(succeeded, Is.True);
            Assert.That(assumptions, Is.Empty);
            Assert.That(entryDomainAssumptions, Is.Empty);
            Assert.That(labels, Is.Empty);
        }
    }

    [Test]
    public void SourceDomainPredicateIsRetainedAlongsideIdenticalUserAssumption()
    {
        var factory = new IrFactory();
        var value = factory.CreateVariable("value", factory.IntegerType);
        var predicate = factory.Binary(
            IrBinaryOperator.GreaterThanOrEqual, factory.Variable(value),
            factory.Integer(0));
        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        assumptions.Add(CreateAssumption(
            factory,
            predicate,
            new UserAssumedJustification(new SourceLocationId(1))));
        var labels = new Dictionary<ProofJustification, string>();
        var entry = ImmutableArray.CreateBuilder<Assumption>();

        Assert.That(PostconditionObligationBuilder.TryAddSourceDomainAssumptions(
            factory,
            [new CompilerCanonicalVariable(
                CompilerVariableRole.Parameter, 0, value, null,
                new CompilerIntegerInterval(1, int.MaxValue), "parameter:0")],
            [], ImmutableDictionary<IrVarId, SpecResultProjection>.Empty,
            assumptions, entry, labels), Is.True);
        Assert.That(assumptions, Has.Count.EqualTo(2));
        Assert.That(assumptions.Any(static item =>
            item.Justification is UserAssumedJustification), Is.True);
        Assert.That(labels.Values, Is.EqualTo(["domain:parameter:0"]));
    }

    [Test]
    public void NormalCompletionAuthorityReplacesAliasedResultDomain()
    {
        var factory = new IrFactory();
        var value = factory.Variable(factory.CreateVariable(
            "value",
            factory.IntegerType));
        var predicate = factory.Binary(
            IrBinaryOperator.NotEqual,
            value,
            factory.Integer(0));
        ProofJustification resultDomainJustification =
            new LoweredJustification(factory.CreateOperation(
                "source-domain:result"));
        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        assumptions.Add(CreateAssumption(
            factory,
            predicate,
            resultDomainJustification));
        var labels = new Dictionary<ProofJustification, string>
        {
            [resultDomainJustification] = "domain:result"
        };

        var completion = PostconditionObligationBuilder
            .AddNormalCompletionAssumption(
                factory,
                [new SymbolicReturn(
                    predicate,
                    factory.Integer(7),
                    ImmutableDictionary<IrVarId, IrTerm>.Empty)],
                ImmutableDictionary<IrVarId, SpecResultProjection>.Empty,
                assumptions,
                labels);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(completion, Is.SameAs(predicate));
            Assert.That(assumptions, Has.Count.EqualTo(1));
            Assert.That(
                assumptions[0].Justification,
                Is.Not.SameAs(resultDomainJustification));
            Assert.That(
                labels.Values,
                Is.EqualTo(["body:normal-completion"]));
        }
    }

    [Test]
    public void ArrayEmptyLengthResultRangeUsesCardinalityProjection()
    {
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var arrayEmptyResult = factory.CreateVariable(
            "array-empty-result",
            sequenceType);
        var lengthProxy = factory.CreateVariable(
            "array-empty-length",
            factory.IntegerType);
        var result = factory.CreateVariable(
            "result",
            factory.IntegerType);
        var assumptions = ImmutableArray.CreateBuilder<Assumption>();
        var entryDomainAssumptions =
            ImmutableArray.CreateBuilder<Assumption>();
        var labels = new Dictionary<ProofJustification, string>();

        var succeeded = PostconditionObligationBuilder
            .TryAddSourceDomainAssumptions(
                factory,
                [new CompilerCanonicalVariable(
                    CompilerVariableRole.Result,
                    -1,
                    result,
                    null,
                    new CompilerIntegerInterval(
                        int.MinValue,
                        int.MaxValue),
                    "result")],
                [new SymbolicReturn(
                    factory.Boolean(true),
                    factory.Length(factory.Variable(arrayEmptyResult)),
                    ImmutableDictionary<IrVarId, IrTerm>.Empty)],
                ImmutableDictionary<IrVarId, SpecResultProjection>.Empty.Add(
                    arrayEmptyResult,
                    new SpecResultProjection(null, lengthProxy)),
                assumptions,
                entryDomainAssumptions,
                labels);

        Assert.That(succeeded, Is.True);
        Assert.That(assumptions, Has.Count.EqualTo(1));
        var predicate = assumptions.Single().Predicate;
        var predicateVariables = IrTermAnalysis.CollectVariables(predicate);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                PostconditionObligationBuilder.IsSupportedProofDomain(
                    factory,
                    predicate),
                Is.True);
            Assert.That(predicateVariables, Does.Contain(lengthProxy));
            Assert.That(predicateVariables, Does.Not.Contain(arrayEmptyResult));
            Assert.That(entryDomainAssumptions, Is.Empty);
            Assert.That(labels.Values, Is.EqualTo(["domain:result"]));
        }
    }

    private static Assumption CreateAssumption(
        IrFactory factory,
        IrTerm predicate,
        ProofJustification justification)
    {
        return (Assumption)typeof(Assumption)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single()
            .Invoke([factory, predicate, justification]);
    }
}
