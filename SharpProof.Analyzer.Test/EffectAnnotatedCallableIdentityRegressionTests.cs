using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Testing;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class EffectAnnotatedCallableIdentityRegressionTests
{
    [Test]
    public void RepeatedAllowedExceptionsAttributesProduceOneCombinedClaim()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture
            {
                [AllowedExceptions(typeof(InvalidOperationException))]
                [AllowedExceptions(typeof(FormatException))]
                public static void ThrowFormat() => throw new FormatException();
            }
            """,
            []);

        var result = new ClaimManifestBuilder(
            compilation,
            WorkerFeatureSet.All).Build();
        var target = result.Targets.Single(pair => pair.Key.Name == "ThrowFormat").Value;
        var claim = target.EffectClaims.Single();
        var expectedExceptions = new[] {
            "System.FormatException",
            "System.InvalidOperationException"
        };
        var actualExceptionNames = claim.Evidence.Constraint.AllowedExceptionTypes
            .Select(value => value[(value.LastIndexOf("::", StringComparison.Ordinal) + 2)..])
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.EffectClaims, Has.Length.EqualTo(1));
            Assert.That(
                claim.Entry.EffectContractKind,
                Is.EqualTo(WorkerEffectContractKind.AllowedExceptions));
            Assert.That(
                actualExceptionNames,
                Is.EqualTo(expectedExceptions));
            Assert.That(
                result.Manifest.Claims.Count(static entry =>
                    entry.EffectContractKind == WorkerEffectContractKind.AllowedExceptions),
                Is.EqualTo(1));
        }
    }

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
