using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

// Finite regression controls run in the ordinary Worker suite. The generated
// campaign remains explicit in AnalyzerPipelineLongFuzzTests.
[TestFixture]
[NonParallelizable]
public sealed class AnalyzerPipelineRegressionTests
{
    [Test]
    public async Task TautologicalRequiresCannotHideEscapingThrow()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class C
            {
                public static int State;
                public static int Other;
                [DoesNotThrow]
                public static int Target(int x, int y, int[]? a, string? s)
                {
                    Contract.Requires((true || !(x != y)));
                    if (true) throw new System.IO.IOException();
                    return x;
                }
                public static bool Pre(int x, int y, int[]? a, string? s) => true;
                public static bool Post(int r, int x0, int y0, int s0, int st) => true;
            }
            """;
        var result = await AnalyzerPipelineLongFuzzTests.RunCase(source, ContractClaimProgramGenerator.DoesNotThrow,
            _ => { });
        var control = await AnalyzerPipelineLongFuzzTests.RunCase(
            source.Replace("Contract.Requires((true || !(x != y)));", "", StringComparison.Ordinal),
            ContractClaimProgramGenerator.DoesNotThrow, _ => { });
        var simple = await AnalyzerPipelineLongFuzzTests.RunCase(
            source.Replace("Contract.Requires((true || !(x != y)));", "Contract.Requires(true);",
                StringComparison.Ordinal), ContractClaimProgramGenerator.DoesNotThrow, _ => { });

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted.ToString()), result.Runtime);
            Assert.That(control.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted.ToString()), control.Runtime);
            Assert.That(simple.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted.ToString()), simple.Runtime);
            Assert.That(result.Runtime, Does.Contain("threw IOException"));
            Assert.That(control.Runtime, Does.Contain("threw IOException"));
            Assert.That(simple.Runtime, Does.Contain("threw IOException"));
        }
    }

    [Test]
    public async Task ElidedConditionalCallCannotHideEscapingThrow()
    {
        const string source = """
            using System.Diagnostics;
            using SharpProof.Attributes;
            public static class C
            {
                public static int State;
                public static int Other;
                [Conditional("TRACE")]
                private static void Log(bool value) { }
                [DoesNotThrow]
                public static int Target(int x, int y, int[]? a, string? s)
                {
                    Log(true || x == 0);
                    throw new System.IO.IOException();
                }
                public static bool Pre(int x, int y, int[]? a, string? s) => true;
                public static bool Post(int r, int x0, int y0, int s0, int st) => true;
            }
            """;
        var result = await AnalyzerPipelineLongFuzzTests.RunCase(source, ContractClaimProgramGenerator.DoesNotThrow,
            _ => { });
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted.ToString()), result.Runtime);
        Assert.That(result.Runtime, Does.Contain("threw IOException"));
    }
}
