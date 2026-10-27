using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzSourceRetentionCapControls
{
    private const string Finite = "public static bool Target(long value) => value >= 0;";
    private const string Partial = "public static bool Target(long value) => IsPositive(value + 1); " +
        "private static bool IsPositive(long value) => value > 0; " +
        "public static bool NormalCompletion(long value) => true;";

    [Test]
    public async Task SixtyFiveActualSourceFailuresRetainTheFirstSixtyFour()
    {
        var positive = new GeneratedCSharpCase(GeneratedCSharpExpression.Boolean(true), 0, 0, true);
        var sources = Enumerable.Range(0, 65).Select(index => new FuzzSourceCampaignCase(
            positive, Finite, [(long)index], Partial, [[(long)index]])).ToImmutableArray();
        var result = await FuzzRunner.ReplaySourceCasesAsync(sources, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Cases, Is.EqualTo(65));
        Assert.That(result.Agreements, Is.Zero);
        Assert.That(result.Abstentions, Is.Zero);
        Assert.That(result.FrontendAgreements, Is.EqualTo(65));
        Assert.That(result.SmtAgreements, Is.EqualTo(65));
        Assert.That(result.PartialSmtAgreements, Is.Zero);
        Assert.That(result.FrontendResults.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Status)), Is.All.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(result.FiniteResults.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Status)), Is.All.EqualTo(FuzzOracleStatus.Agreement));
        Assert.That(result.PartialResults.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Status)), Is.All.EqualTo(FuzzOracleStatus.Mismatch));
        Assert.That(result.PartialResults.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Detail)), Is.All.EqualTo(
            "The guarded interpreter disagrees with C# execution in scenario 0."));
        Assert.That(result.Failures, Has.Length.EqualTo(64));
        Assert.That(result.Failures.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Case)), Is.EqualTo(Enumerable.Range(0, 64)));
        Assert.That(result.Failures.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Oracle)), Is.All.EqualTo("partial-term-smt"));
        Assert.That(result.Failures.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Detail)), Is.All.Not.Empty);
    }
}
