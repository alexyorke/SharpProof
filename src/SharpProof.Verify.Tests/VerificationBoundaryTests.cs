using System.Collections.Immutable;
using SharpProof.Specs;

namespace SharpProof.Verify.Test;

[TestFixture]
public sealed class VerificationBoundaryTests
{
    [Test]
    public void EvidenceAndQueryRejectMalformedPublicInputs()
    {
        var factory = new IrFactory();
        var goal = FalseGoal(factory);
        var spec = ApiSpecTable.Default.Templates[0].Id;

        Assert.That(new SourceLocationId(42).ToString(), Is.EqualTo("location42"));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            _ = new SourceLocationId(-1);
        }));
        Assert.That(new SpecJustification(spec).Spec, Is.EqualTo(spec));
        Assert.Throws<ArgumentException>((Action)(() =>
        {
            _ = new SpecJustification(default);
        }));
        Assert.Throws<ArgumentOutOfRangeException>((Action)(() =>
        {
            _ = BackendCheckResult.Unknown(BackendFailureReason.None);
        }));
        Assert.Throws<ArgumentException>((Action)(() =>
        {
            _ = new VerificationQuery(factory, [null!], goal);
        }));

        Assert.That(new VerificationQuery(factory, [], goal).Assumptions, Is.Empty);
    }

    [Test]
    public async Task BackendResultShapesCannotCreateProofsOrRefutations()
    {
        var factory = new IrFactory();
        var query = new VerificationQuery(factory, [], FalseGoal(factory));
        var emptyModel = new BackendModel([]);
        var malformed = new BackendCheckResult?[]
        {
            null,
            new((BackendCheckStatus)99, [], null, BackendFailureReason.None, default),
            new(BackendCheckStatus.Unsatisfiable, [], emptyModel, BackendFailureReason.None, default),
            new(BackendCheckStatus.Satisfiable, [], null, BackendFailureReason.None, default)
        };

        foreach (var result in malformed)
        {
            var outcome = await new ProofKernel(new ResultBackend(result)).VerifyAsync(query, TestContext.CurrentContext.CancellationToken);
            Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
            Assert.That(((UnknownOutcome)outcome).Reason,
                Is.EqualTo(AbstentionReason.MalformedBackendResult));
        }

        Assert.That(await new ProofKernel(new ResultBackend(BackendCheckResult.Unsatisfiable([])))
            .VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<ProvenOutcome>());
        Assert.That(await new ProofKernel(new ResultBackend(BackendCheckResult.Satisfiable(emptyModel)))
            .VerifyAsync(query, TestContext.CurrentContext.CancellationToken), Is.TypeOf<RefutedOutcome>());
    }

    [Test]
    public async Task NullOrUnsupportedModelValuesCannotRefute()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var integer = factory.CreateVariable("integer", factory.IntegerType);
        var nestedType = factory.GetOrCreateSequenceType(
            factory.GetOrCreateSequenceType(factory.IntegerType));
        var nested = factory.CreateVariable("nested", nestedType);
        var nullValue = new BackendModel(
            ImmutableDictionary<IrVarId, IrValue>.Empty.Add(integer, null!), default);
        var nestedValue = new BackendModel([
            KeyValuePair.Create(nested, factory.CreateSequenceValue(nestedType, []))
        ]);
        var nullOutcome = await new ProofKernel(new ResultBackend(BackendCheckResult.Satisfiable(nullValue)))
            .VerifyAsync(new VerificationQuery(factory, [], FalseGoal(factory), [integer]), TestContext.CurrentContext.CancellationToken);
        var nestedOutcome = await new ProofKernel(new ResultBackend(BackendCheckResult.Satisfiable(nestedValue)))
            .VerifyAsync(new VerificationQuery(factory, [], FalseGoal(factory), [nested]), TestContext.CurrentContext.CancellationToken);

        Assert.That(nullOutcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)nullOutcome).Reason,
            Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));
        Assert.That(nestedOutcome, Is.TypeOf<UnknownOutcome>());
        Assert.That(((UnknownOutcome)nestedOutcome).Reason,
            Is.EqualTo(AbstentionReason.CounterexampleReplayFailed));

        var sequenceType = factory.GetOrCreateSequenceType(factory.IntegerType);
        var sequence = factory.CreateVariable("sequence", sequenceType);
        var valid = new BackendModel([
            KeyValuePair.Create(sequence, factory.CreateSequenceValue(sequenceType, []))
        ]);
        Assert.That(await new ProofKernel(new ResultBackend(BackendCheckResult.Satisfiable(valid)))
            .VerifyAsync(new VerificationQuery(factory, [], FalseGoal(factory), [sequence]), TestContext.CurrentContext.CancellationToken),
            Is.TypeOf<RefutedOutcome>());
    }

    private static Goal FalseGoal(IrFactory factory)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        return new Goal(factory, factory.Boolean(false),
            ProofDiagnosticKind.Postcondition, new SourceLocationId(0));
    }

    private sealed class ResultBackend(BackendCheckResult? result /* SPINT001: all values valid - the test backend deliberately returns null to exercise caller validation */) : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query,
            CancellationToken cancellationToken)
        {
            System.ArgumentNullException.ThrowIfNull(query);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result!);
        }
    }
}
