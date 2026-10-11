using System.Reflection;
using System.Runtime.Loader;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class LongFuzzRuntimeCoverageRegressionTests
{
    private static readonly int[] SingleItem = [0];

    [TestCase(3000036, ContractClaimProgramGenerator.DoesNotThrow, typeof(InvalidOperationException))]
    [TestCase(3000183, ContractClaimProgramGenerator.EnforcePure, typeof(DivideByZeroException))]
    public void OracleFindsCompiledClrWitnesses(int seed, string expectedMode, Type expectedException)
    {
        var (source, mode, _) = new ContractClaimProgramGenerator(new Random(seed)).Program();
        Assert.That(mode, Is.EqualTo(expectedMode));
        Assert.That(source, Does.Contain(seed == 3000036 ? "Thrower((y + 5))" : "case 1: case 2: State++;"));
        var compilation = TestCompilation.Create("LongFuzzCoverage" + seed, ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("LongFuzzCoverage" + seed, isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var arguments = seed == 3000036
                ? new object?[] { 0, -2, null, null }
                : new object?[] { 0, 0, SingleItem.ToArray(), null };
            TargetInvocationException? thrown = null;
            try
            {
                type.GetMethod("Target")!.Invoke(null, arguments);
            }
            catch (TargetInvocationException exception)
            {
                thrown = exception;
            }
            Assert.That(thrown?.InnerException, Is.TypeOf(expectedException));
            if (seed == 3000183)
            {
                Assert.That((int)type.GetField("State")!.GetValue(null)!, Is.EqualTo(1));
            }
        }
        finally
        {
            context.Unload();
        }
        var oracle = typeof(AnalyzerPipelineLongFuzzTests).GetNestedType("RuntimeOracle", BindingFlags.NonPublic)!;
        var run = oracle.GetMethod("Run", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!;
        var observation = run.Invoke(null, [source, mode])!;
        var violation = (string?)observation.GetType().GetProperty("Violation")!.GetValue(observation);
        Assert.That(violation, Is.Not.Null, $"seed {seed}: emitted CLR witness exists but runtime input grid missed it");
    }
}
