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

    [TestCase(3001234, ContractClaimProgramGenerator.ZeroAllocations, "Contract.Requires((s != null && s.Length == 7));")]
    [TestCase(3001600, ContractClaimProgramGenerator.DoesNotThrow, "checked((int)(byte)(a == null ? 0 : a.Length) + 1)")]
    [TestCase(3001665, ContractClaimProgramGenerator.DoesNotThrow, "checked((int)(byte)(a == null ? 0 : a.Length) + 0)")]
    [TestCase(3001792, ContractClaimProgramGenerator.EnforcePure, "Contract.Requires((s != null && s.Length == 6));")]
    public void OracleFindsAdditionalCompiledClrWitnesses(int seed, string expectedMode, string sourceMarker)
    {
        var (source, mode, _) = new ContractClaimProgramGenerator(new Random(seed)).Program();
        Assert.That(mode, Is.EqualTo(expectedMode));
        Assert.That(source, Does.Contain(sourceMarker));
        var compilation = TestCompilation.Create("LongFuzzBoundary" + seed, ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("LongFuzzBoundary" + seed, isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var x = seed == 3001234 ? 100 : 0;
            int[]? array = seed is 3001600 or 3001665 ? new int[256] : null;
            string? value = seed switch
            {
                3001234 => "abcdefg",
                3001792 => "abcdef",
                _ => null
            };
            Assert.That((bool)type.GetMethod("Pre")!.Invoke(null, [x, 0, array, value])!, Is.True);
            var target = type.GetMethod("Target")!;
            if (seed == 3001792)
            {
                target.Invoke(null, [x, 0, array, value]);
                Assert.That((int)type.GetField("State")!.GetValue(null)!, Is.Not.Zero);
            }
            else
            {
                TargetInvocationException? thrown = null;
                try
                {
                    target.Invoke(null, [x, 0, array, value]);
                }
                catch (TargetInvocationException exception)
                {
                    thrown = exception;
                }
                var expected = seed == 3001234 ? typeof(InvalidOperationException) : typeof(OverflowException);
                Assert.That(thrown?.InnerException, Is.TypeOf(expected));
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
        Assert.That(violation, Is.Not.Null, $"seed {seed}: compiled CLR witness exists but runtime input grid missed it");
    }
}
