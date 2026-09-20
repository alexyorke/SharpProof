namespace SharpProof.Smt.Test;

[TestFixture]
public sealed class ResourceAccountingRegressionTests
{
    [Test]
    public async Task NativeResourceAccountingChargesOnlyTheCurrentQuery()
    {
        const uint queryLimit = 1_000_000;
        var factory = new IrFactory();
        var operation = factory.CreateOperation("tracked");
        var expensive = CreateTrackedQuery(factory, operation, 256);
        var inexpensive = new VerificationQuery(
            factory,
            [],
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
        using var backend = new IrSmtBackend(
            new IrSmtBackendOptions(queryLimit));

        var first = await backend.CheckAsync(expensive, CancellationToken.None);
        var firstCost = backend.ConsumedResourceCount;
        var second = await backend.CheckAsync(inexpensive, CancellationToken.None);
        var secondCost = backend.ConsumedResourceCount - firstCost;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
            Assert.That(second.Status, Is.EqualTo(BackendCheckStatus.Unsatisfiable));
            Assert.That(firstCost, Is.GreaterThan(0));
            Assert.That(secondCost, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                secondCost,
                Is.LessThan(firstCost),
                "A trivial query must not be charged the prior query's native work.");
        }
    }

    [Test]
    public async Task RepeatedQueriesDoNotHitResourceLimitFromPriorQueries()
    {
        const uint queryLimit = 50_000;
        var factory = new IrFactory();
        var operation = factory.CreateOperation("tracked");
        var query = CreateTrackedQuery(factory, operation, 256);
        using var backend = new IrSmtBackend(
            new IrSmtBackendOptions(queryLimit));

        for (var index = 0; index < 40; index++)
        {
            var result = await backend.CheckAsync(query, CancellationToken.None);

            Assert.That(
                result.Status,
                Is.EqualTo(BackendCheckStatus.Unsatisfiable),
                $"query {index} should remain within its own resource limit");
        }
    }

    private static VerificationQuery CreateTrackedQuery(
        IrFactory factory,
        ScopedIrId<IrOperationTag> operation,
        int assumptionCount)
    {
        var assumptions = Enumerable.Range(0, assumptionCount)
            .Select(index => new Assumption(
                factory,
                factory.Variable(factory.CreateVariable(
                    "tracked-" + index,
                    factory.BooleanType)),
                new LoweredJustification(operation)))
            .ToArray();

        return new VerificationQuery(
            factory,
            assumptions,
            new Goal(
                factory,
                factory.Boolean(true),
                ProofDiagnosticKind.InternalConsistency,
                new SourceLocationId(0)));
    }
}
