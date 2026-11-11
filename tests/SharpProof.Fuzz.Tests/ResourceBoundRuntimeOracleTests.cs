using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Fuzz.Test;

// The campaign itself runs only in nightly. These keep it honest between runs:
// its generator stays deterministic, and a bound below the real cost still fails.
[TestFixture]
public sealed class ResourceBoundRuntimeOracleTests
{
    private static ResourceBoundCase Case(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new(0, ResourceBoundShape.NestedLoops, $"public static void Target0({ResourceBoundCase.Parameters}) {{ {body} }}", ElementWidth: 4);
    }

    [Test]
    public void GenerationDependsOnlyOnTheSeedAndOrdinal()
    {
        var first = Enumerable.Range(0, 40).Select(static ordinal => ResourceBoundProgramGenerator.Generate(12345, ordinal)).ToImmutableArray();
        var second = Enumerable.Range(0, 40).Select(static ordinal => ResourceBoundProgramGenerator.Generate(12345, ordinal)).ToImmutableArray();
        var other = Enumerable.Range(0, 40).Select(static ordinal => ResourceBoundProgramGenerator.Generate(54321, ordinal)).ToImmutableArray();
        Assert.That(second, Is.EqualTo(first));
        Assert.That(other.Select(static item => item.Member),
            Is.Not.EqualTo(first.Select(static item => item.Member)));
        Assert.That(first.Select(static item => item.Shape).Distinct().Count(),
            Is.EqualTo(ResourceBoundCase.ShapeCount));
    }

    [Test]
    public async Task ASmallRunProvesEveryControlShapeAndExecutesIt()
    {
        var summary = await ResourceBoundRuntimeOracle.RunAsync(40, 12345, 4, TestContext.CurrentContext.CancellationToken);
        Assert.That(summary.Failures, Is.Empty, () => summary.Failures[0].Detail);
        Assert.That(summary.Passed, Is.True);
        var coverage = summary.Coverage;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(coverage.ShapeMask, Is.EqualTo(ResourceBoundFuzzCoverage.AllShapes));
            Assert.That(coverage.ProvenShapeMask & ResourceBoundFuzzCoverage.ProvableShapes, Is.EqualTo(ResourceBoundFuzzCoverage.ProvableShapes));
            Assert.That(coverage.NativeBounds, Is.EqualTo(coverage.ProvenCases));
            Assert.That(coverage.Executions, Is.GreaterThan(coverage.ProvenCases));
            Assert.That(coverage.TightCases, Is.Positive);
            Assert.That(coverage.ExceptionalExecutions, Is.Positive);
        }
    }

    // Lowering a real proof stands in for an analysis bug, so these keep showing
    // that the comparison can fail, on both proof routes, without one in the tree.
    [TestCase("for (int i = 0; i < n; i++) { int k = m; while (k > 0) { k--; } }", "Entries", "LoopBodyEntries")]
    [TestCase("for (int i = 0; i < n; i++) { var a = new int[2]; }", "Arrays", "ArrayAllocations")]
    [TestCase("for (int i = 0; i < n; i++) { var a = new int[2]; }", "Elements", "AllocatedElements")]
    public void AClaimBelowTheRuntimeCountFailsOnBothRoutes(string body, string lowered, string metric)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(metric);
        var result = ResourceBoundRuntimeOracle.Check([Case(body)], 1, claim => lowered switch
        {
            "Entries" => claim with { Entries = ResourceExpression.Zero },
            "Arrays" => claim with { Arrays = ResourceExpression.Zero },
            _ => claim with { Elements = ResourceExpression.Zero }
        }, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Proven, Is.EqualTo(1));
        Assert.That(result.Violating, Is.EqualTo(1));
        Assert.That(result.Failures.Select(static failure => failure.Oracle),
            Is.EquivalentTo(ImmutableArray.Create("resource-bound-sp0060", "resource-bound-native")));
        Assert.That(result.Failures, Has.All.Property(nameof(FuzzFailure.Detail)).Contains(metric + " "));
    }

    // A true proof that counts no strings, so only the measured build can see it.
    [Test]
    public void AnAllocationNoCountMarksFailsOnMeasuredBytes()
    {
        var result = ResourceBoundRuntimeOracle.Check([Case("int k = new string('x', 64).Length; while (k > 0) { k--; }")], 1,
            static claim => claim, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.Proven, Is.EqualTo(1));
        Assert.That(result.Failures, Has.All.Property(nameof(FuzzFailure.Detail)).Contains("AllocatedBytes"));
        Assert.That(result.Failures, Has.Count.EqualTo(2));
    }

    [Test]
    public void OnlyTheNamedOracleSelectsTheResourceRun()
    {
        Assert.That(FuzzOptions.Parse(["--cases", "3"]).ResourceBoundsOnly, Is.False);
        Assert.That(FuzzOptions.Parse(["--oracle", FuzzOptions.ResourceBoundsOracle]).ResourceBoundsOnly, Is.True);
        Assert.That(new Action(() => FuzzOptions.Parse(["--oracle", "frontend"])), Throws.TypeOf<FuzzUsageException>());
    }
}
