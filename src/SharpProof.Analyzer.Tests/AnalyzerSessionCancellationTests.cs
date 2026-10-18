using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerSessionCancellationTests
{
    private static readonly Compilation SharedCompilation =
        AnalyzerTestHost.CreateCompilation(
            """
            public static class Fixture
            {
                public static int Read() => 1;
            }
            """,
            ["SP0047"]);

    [TestCase(false)]
    [TestCase(true)]
    public void CancellationStopsLazyContractInitialization(
        bool resolveContractSource)
    {
        var compilation = SharedCompilation;
        var method = compilation.GetTypeByMetadataName("Fixture")!
            .GetMembers("Read")
            .OfType<IMethodSymbol>()
            .Single();
        using var cancellation = new CancellationTokenSource();
        var session = new AnalyzerSession(
            compilation,
            AnalyzerConfiguration.AdvisoryAll,
            cancellation.Token);

        cancellation.Cancel();

        Assert.That(
            (Action)(() =>
            {
                if (resolveContractSource)
                {
                    _ = session.IsContractCompanion(method);
                }
                else
                {
                    _ = session.GetContractClauses(method);
                }
            }),
            Throws.InstanceOf<OperationCanceledException>());
    }
}
