using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class LongFuzzRefStateOracleTests
{
    [Test]
    public void RetainedSwitchSectionSeedHasNoRuntimeHarnessError()
    {
        var (source, mode, _) = new ContractClaimProgramGenerator(new Random(1442402408)).Program();
        Assert.That(source, Does.Contain("ref int r0 = ref State;"));
        Assert.That(source, Does.Contain("=> y != y;"));
        var observation = AnalyzerPipelineLongFuzzTests.RuntimeOracle.Run(source, mode);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Is.Null);
            Assert.That(observation.Violation, Is.Null);
            Assert.That(observation.Inputs, Is.Zero);
        }
    }

    [Test]
    public void SwitchSectionRefAliasIsAdmittedBeforeUnsatisfiablePrecondition()
    {
        // Retained RuntimeHarness seed 1442402408: admission precedes Pre.
        var observation = Observe("""
            x = checked(y * 2 + 1);
            switch (2 * y)
            {
                case 0: x = 1; break;
                case 1:
                case 2:
                    ref int r0 = ref State;
                    r0 = y is > 0 and < 5 ? y : 0;
                    break;
                default: break;
            }
            Other += 1;
            return x;
            """, "y != y");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Is.Null);
            Assert.That(observation.Violation, Is.Null);
            Assert.That(observation.Inputs, Is.Zero);
        }
    }

    [TestCase("case 0: case 1:")]
    [TestCase("default:")]
    public void SameValueWriteThroughSwitchSectionRefAliasIsObserved(string labels)
    {
        ArgumentNullException.ThrowIfNull(labels);
        var observation = Observe($"switch (x) {{ {labels} ref int stateRef = ref State; stateRef = 0; break; }} return 0;");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Is.Null);
            Assert.That(observation.Violation, Does.Contain("State writes=1"));
            Assert.That(observation.Inputs, Is.GreaterThan(0));
        }
    }

    [Test]
    public void ThrowingSwitchSectionRefAliasStoreIsNotCounted()
    {
        var observation = Observe("switch (x) { default: ref int stateRef = ref State; stateRef = ThrowValue(); break; } return 0;");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Is.Null);
            Assert.That(observation.Violation, Is.Null);
            Assert.That(observation.Inputs, Is.GreaterThan(0));
        }
    }

    [TestCase("stateRef++;")]
    [TestCase("stateRef += 1;")]
    [TestCase("stateRef = ref Other;")]
    [TestCase("Other++; stateRef = 0;")]
    [TestCase("stateRef = 0; Other = stateRef;")]
    [TestCase("System.Threading.Interlocked.Increment(ref stateRef);")]
    [TestCase("break; case 1: Other = 0;")]
    public void UnsupportedSwitchSectionRefAliasUseFailsExplicitly(string statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        var observation = Observe($"switch (x) {{ case 0: ref int stateRef = ref State; {statements} break; }} return 0;");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Does.Contain("adjacent simple assignment shape"));
            Assert.That(observation.Violation, Is.Null);
            Assert.That(observation.Inputs, Is.Zero);
        }
    }

    [Test]
    public void ReadOnlySwitchSectionRefAliasDoesNotCountAsAWrite()
    {
        var observation = Observe("switch (x) { default: ref readonly int stateRef = ref State; return stateRef; }");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Is.Null);
            Assert.That(observation.Violation, Is.Null);
            Assert.That(observation.Inputs, Is.GreaterThan(0));
        }
    }

    [Test]
    public void ReadOnlySwitchSectionRefAliasEscapeFailsExplicitly()
    {
        var observation = Observe("switch (x) { default: ref readonly int stateRef = ref State; return Read(in stateRef); }");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(observation.Error, Does.Contain("escapes through a by-reference use"));
            Assert.That(observation.Violation, Is.Null);
            Assert.That(observation.Inputs, Is.Zero);
        }
    }

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

    private static AnalyzerPipelineLongFuzzTests.Observation Observe(string body, string precondition = "true")
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(precondition);
        var source = $$"""
            using SharpProof.Attributes;
            public static class C
            {
                public static int State;
                public static int Other;
                private static int ThrowValue() => throw new System.InvalidOperationException();
                private static int Read(in int value) => value;
                [EnforcePure]
                public static int Target(int x, int y, int[]? a, string? s)
                {
                    {{body}}
                }
                public static bool Pre(int x, int y, int[]? a, string? s) => {{precondition}};
                public static bool Post(int r, int x0, int y0, int s0, int st) => true;
            }
            """;
        return AnalyzerPipelineLongFuzzTests.RuntimeOracle.Run(source, ContractClaimProgramGenerator.EnforcePure);
    }
}
