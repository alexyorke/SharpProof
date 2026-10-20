using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryLongBranchAllocationWorkflowTests
{
    private const int DeclaredLocals = 299;

    [TestCase(0, 0)]
    [TestCase(7, 2093)]
    public void ActualDebugEmittedEarlyReturnAcrossScalarLocalsRemainsFreeOfAllocationOpcodes(
        int seed, int expectedResult)
    {
        var declarations = string.Concat(Enumerable.Range(0, DeclaredLocals).Select(index => $"int value{index}=seed;\n"));
        var sum = string.Join("+", Enumerable.Range(0, DeclaredLocals).Select(index => $"value{index}"));
        var source = $"public static class ScalarBody {{ public static int Read(int seed) {{ if (seed == 0) return 0; {declarations} return {sum}; }} }}";
        var compilation = AnalyzerGateHost.CreateCompilation(source,
            "OrdinaryLongBranchAllocation" + seed, includeExternalEffectsFixture: false);
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Debug));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("OrdinaryLongBranchAllocation" + seed, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var method = assembly.GetType("ScalarBody", throwOnError: true)!.GetMethod("Read")!;
            var body = method.GetMethodBody()!;
            var bytes = body.GetILAsByteArray()!.ToImmutableArray();
            var arguments = new object[] { seed };

            using (Assert.EnterMultipleScope())
            {
                Assert.That(method.IsStatic, Is.True);
                Assert.That(body.InitLocals, Is.True);
                Assert.That(body.LocalVariables, Has.Count.EqualTo(301));
                Assert.That(body.LocalVariables.Count(static local => local is null ? throw new System.ArgumentNullException(nameof(local)) : (local.LocalType == typeof(int))), Is.EqualTo(300));
                Assert.That(body.LocalVariables.Count(static local => local is null ? throw new System.ArgumentNullException(nameof(local)) : (local.LocalType == typeof(bool))), Is.EqualTo(1));
                Assert.That(body.LocalVariables.Any(static local => local is null ? throw new System.ArgumentNullException(nameof(local)) : (local.LocalIndex >= 256)), Is.True);
                Assert.That(bytes.Length, Is.GreaterThan(sbyte.MaxValue).And.LessThan(65536));
                Assert.That(method.Invoke(null, arguments), Is.EqualTo(expectedResult));
                Assert.That(AllocationIlOracle.HasPotentialAllocation(method), Is.False);
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
