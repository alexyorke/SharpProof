using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Testing;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class EffectAnnotatedCallableIdentityRegressionTests
{
    [Test]
    public void EffectAnnotatedNestedCallablesHaveDistinctManifestIds()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public static class Fixture {
                public static void Configure() {
                    [EnforcePure]
                    static int First() => 1;

                    [EnforcePure]
                    static int Second() => 2;

                    var first = [EnforcePure] () => 3;
                    var second = [EnforcePure] () => 4;

                    if (DateTime.Now.Ticks == 0) {
                        [DoesNotThrow]
                        static int Same(int value) => value;
                    } else {
                        [DoesNotThrow]
                        static int Same(int value) => value + 1;
                    }
                }
            }
            """,
            []);

        var result = new ClaimManifestBuilder(
            compilation,
            WorkerFeatureSet.All).Build();
        var callables = result.Manifest.Callables;

        Assert.That(callables, Has.Length.EqualTo(6));
        Assert.That(
            callables.Select(static callable => callable.CallableId).Distinct(
                StringComparer.Ordinal).Count(),
            Is.EqualTo(callables.Length));
    }
}
