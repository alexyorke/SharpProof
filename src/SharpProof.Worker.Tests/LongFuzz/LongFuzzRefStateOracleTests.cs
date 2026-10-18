using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
[NonParallelizable]
public sealed class LongFuzzRefStateOracleTests
{
    [Test]
    public void SameValueWriteThroughGeneratedRefAliasIsObserved()
    {
        var observation = Observe("ref int stateRef = ref State; stateRef = 0; return 0;");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Is.Null);
            Assert.That(observation.Violation, Does.Contain("State writes=1"));
            Assert.That(observation.Inputs, Is.GreaterThan(0));
        }
    }

    [Test]
    public void RefAliasAssignmentWhoseRightHandSideThrowsIsNotCounted()
    {
        var observation = Observe("ref int stateRef = ref State; stateRef = ThrowValue(); return 0;");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Is.Null);
            Assert.That(observation.Violation, Is.Null);
            Assert.That(observation.Inputs, Is.GreaterThan(0));
        }
    }

    [Test]
    public void ReadOnlyRefAliasDoesNotCountAsAWrite()
    {
        var observation = Observe("ref readonly int stateRef = ref State; return stateRef;");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Is.Null);
            Assert.That(observation.Violation, Is.Null);
            Assert.That(observation.Inputs, Is.GreaterThan(0));
        }
    }

    [Test]
    public void OrdinaryStatePropertyWriteRemainsTracked()
    {
        var observation = Observe("State = 0; return 0;");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Is.Null);
            Assert.That(observation.Violation, Does.Contain("State writes=1"));
            Assert.That(observation.Inputs, Is.GreaterThan(0));
        }
    }

    [TestCase("ref int stateRef = ref State; stateRef++; return 0;")]
    [TestCase("ref int stateRef = ref State; stateRef = ref Other; return 0;")]
    public void UnsupportedRefAliasMutationFailsTheOracleExplicitly(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var observation = Observe(body);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Does.StartWith("unsupported EnforcePure runtime oracle:"));
            Assert.That(observation.Violation, Is.Null);
            Assert.That(observation.Inputs, Is.Zero);
        }
    }

    private static AnalyzerPipelineLongFuzzTests.Observation Observe(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = $$"""
            using SharpProof.Attributes;
            public static class C
            {
                public static int State;
                public static int Other;
                private static int ThrowValue() => throw new System.InvalidOperationException();
                [EnforcePure]
                public static int Target(int x, int y, int[]? a, string? s)
                {
                    {{body}}
                }
                public static bool Pre(int x, int y, int[]? a, string? s) => true;
                public static bool Post(int r, int x0, int y0, int s0, int st) => true;
            }
            """;
        return AnalyzerPipelineLongFuzzTests.RuntimeOracle.Run(source, ContractClaimProgramGenerator.EnforcePure);
    }
}
