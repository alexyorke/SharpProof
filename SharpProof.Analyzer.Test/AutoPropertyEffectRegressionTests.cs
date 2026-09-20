using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AutoPropertyEffectRegressionTests
{
    [Test]
    public async Task AutoPropertyReadsRemainProvableForEffectContracts()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public sealed class Dto
            {
                public int Age { get; set; }
                public static int Count { get; set; }
            }

            public static class Sample
            {
                [EnforcePure]
                public static int Read(Dto value) => value.Age;

                [DoesNotThrow]
                public static int NoThrow() => Dto.Count;

                [ZeroAllocations]
                public static int NoAllocation() => Dto.Count;
            }
            """,
            "effects",
            []);

        Assert.That(diagnostics, Is.Empty);
    }
}
