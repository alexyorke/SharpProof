using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.ToolingTest;

[TestFixture]
public sealed class SharpProofAnalysisSessionTests
{
    private const string Source = """
                                  public static class Target
                                  {
                                      public static int Abs(int value)
                                      {
                                          if (value < 0)
                                              return -value;

                                          return value;
                                      }
                                  }
                                  """;

    [Test]
    public void Session_ExecutesEveryDiscriminatedQueryKindWithTypedPayloads()
    {
        using var session = SharpProofAnalysisSession.FromText(
            Source,
            "SessionTarget.cs",
            new SharpProofAnalysisOptions(enableSmt: true));
        var point = SymbolicQueryTarget.Point(8, 9);

        var results = new[]
        {
            session.Analyze(SharpProofQuery.SourceLocation(point)),
            session.Analyze(SharpProofQuery.Method(point)),
            session.Analyze(SharpProofQuery.Invariant(point)),
            session.Analyze(SharpProofQuery.Reachability(point)),
            session.Analyze(SharpProofQuery.Condition(point, "value >= 0")),
            session.Analyze(SharpProofQuery.RuntimeHazards(point)),
            session.Analyze(SharpProofQuery.Capabilities(point)),
            session.Analyze(SharpProofQuery.Complexity(point))
        };

        Assert.Multiple(() =>
        {
            Assert.That(results.Take(4).Select(static result => result.Payload),
                Has.All.TypeOf<SourceQueryPayload>());
            Assert.That(results[4].Payload, Is.TypeOf<ConditionQueryPayload>());
            Assert.That(results[5].Payload, Is.TypeOf<RuntimeHazardQueryPayload>());
            Assert.That(results[6].Payload, Is.TypeOf<CapabilityQueryPayload>());
            Assert.That(results[7].Payload, Is.TypeOf<ComplexityQueryPayload>());
            Assert.That(results, Has.All.Matches<SharpProofQueryResult>(static result => result.IsSuccess));
            Assert.That(results, Has.All.Matches<SharpProofQueryResult>(static result => result.Error == null));
            Assert.That(results, Has.All.Matches<SharpProofQueryResult>(static result =>
                result.Location.FilePath == "SessionTarget.cs"));
        });
    }

    [Test]
    public async Task Session_CachesEquivalentQueriesAcrossConcurrentCallers()
    {
        using var session = SharpProofAnalysisSession.FromText(Source, "CachedSession.cs");
        var query = SharpProofQuery.Invariant(SymbolicQueryTarget.Point(8, 9));

        var results = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(_ => Task.Run(() => session.Analyze(query))));

        Assert.That(results, Has.All.SameAs(results[0]));
    }

    [Test]
    public void Session_ReturnsTypedFailureAndRejectsQueriesAfterDisposal()
    {
        var session = SharpProofAnalysisSession.FromText(Source, "FailedSession.cs");
        var failure = session.Analyze(SharpProofQuery.Invariant(SymbolicQueryTarget.Point(99, 1)));

        Assert.Multiple(() =>
        {
            Assert.That(failure.Status, Is.EqualTo(SharpProofQueryStatus.Failed));
            Assert.That(failure.Error!.Code, Is.EqualTo(SymbolicErrorCodes.InvalidTarget));
            Assert.That(failure.Payload, Is.Null);
        });

        session.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            session.Analyze(SharpProofQuery.Invariant(SymbolicQueryTarget.Point(8, 9))));
    }

    [Test]
    public void Session_ReturnsCancellationWithoutPoisoningTheQueryCache()
    {
        using var session = SharpProofAnalysisSession.FromText(Source, "CanceledSession.cs");
        var query = SharpProofQuery.Invariant(SymbolicQueryTarget.Point(8, 9));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var canceled = session.Analyze(query, cancellation.Token);
        var retried = session.Analyze(query);

        Assert.Multiple(() =>
        {
            Assert.That(canceled.Status, Is.EqualTo(SharpProofQueryStatus.Canceled));
            Assert.That(canceled.Error!.Code, Is.EqualTo(SymbolicErrorCodes.Canceled));
            Assert.That(retried.IsSuccess, Is.True);
            Assert.That(retried.Payload, Is.TypeOf<SourceQueryPayload>());
        });
    }
}
