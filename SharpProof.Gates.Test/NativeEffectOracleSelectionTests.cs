using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class NativeEffectOracleSelectionTests
{
    [TestCase(1, false)]
    [TestCase(200, true)]
    public async Task ExplicitLimitPreservesPinnedMethodSelection(int maximumMethods, bool exhaustive)
    {
        var root = RepositoryLayout.FindRoot();
        var document = OpenSourceCorpusCatalog.Load(root);
        var report = await NativeEffectOracleGate.RunAsync(root, maximumMethods: maximumMethods, cancellationToken: CancellationToken.None);
        Assert.That(report.UniverseMethodCount, Is.EqualTo(document.Methods.Length));
        Assert.That(report.CheckedMethodCount, Is.EqualTo(maximumMethods));
        Assert.That(report.Exhaustive, Is.EqualTo(exhaustive));
        Assert.That(report.Rows.Select(row => row.MethodId),
            Is.EqualTo(document.Methods.Take(maximumMethods).Select(method => method.Id)));
        Assert.That(report.RuntimeContradictions, Is.Zero);
        Assert.That(report.Passed, Is.True);
    }

    [TestCase(-1)]
    [TestCase(201)]
    public void LimitsOutsidePinnedUniverseAreRejected(int maximumMethods)
    {
        var error = Assert.ThrowsAsync<ArgumentOutOfRangeException>(new Func<Task>(() =>
            NativeEffectOracleGate.RunAsync(RepositoryLayout.FindRoot(), maximumMethods: maximumMethods, cancellationToken: CancellationToken.None)));
        Assert.That(error!.ParamName, Is.EqualTo(nameof(maximumMethods)));
    }
}
