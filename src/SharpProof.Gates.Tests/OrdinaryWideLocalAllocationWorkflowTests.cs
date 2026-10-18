using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryWideLocalAllocationWorkflowTests
{
    [TestCase(127, 128, false)]
    [TestCase(299, 300, true)]
    public void ActualDebugEmittedScalarLocalsRemainFreeOfAllocationOpcodes(
        int declaredLocals, int expectedPeLocals, bool expectedWideLocal)
    {
        // Source producer preserved from the measured CompilerScalarIlDecoderTests.
        var declarations = string.Concat(Enumerable.Range(0, declaredLocals).Select(index => $"int value{index}=seed;\n"));
        var sum = string.Join("+", Enumerable.Range(0, declaredLocals).Select(index => $"value{index}"));
        var source = $"public static class ScalarBody {{ public static int Read(int seed) {{ {declarations} return {sum}; }} }}";
        var compilation = AnalyzerGateHost.CreateCompilation(source,
            "OrdinaryWideLocalAllocation" + declaredLocals, includeExternalEffectsFixture: false);
        compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Debug));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("OrdinaryWideLocalAllocation" + declaredLocals, isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var method = assembly.GetType("ScalarBody", throwOnError: true)!.GetMethod("Read")!;
            var body = method.GetMethodBody()!;
            var runtimeSeed = declaredLocals % 11 + 1;
            var arguments = new object[] { runtimeSeed };

            using (Assert.EnterMultipleScope())
            {
                Assert.That(method.IsStatic, Is.True);
                Assert.That(body.InitLocals, Is.True);
                Assert.That(body.LocalVariables, Has.Count.EqualTo(expectedPeLocals));
                Assert.That(body.LocalVariables.Select(static local => local is null ? throw new System.ArgumentNullException(nameof(local)) : (local.LocalType)), Is.All.EqualTo(typeof(int)));
                Assert.That(body.LocalVariables.Any(static local => local is null ? throw new System.ArgumentNullException(nameof(local)) : (local.LocalIndex >= 256)), Is.EqualTo(expectedWideLocal));
                Assert.That(body.GetILAsByteArray(), Is.Not.Null.And.Not.Empty);
                Assert.That(body.GetILAsByteArray()!.Length, Is.LessThan(65536));
                Assert.That(method.Invoke(null, arguments), Is.EqualTo(runtimeSeed * declaredLocals));
                Assert.That(AllocationIlOracle.HasPotentialAllocation(method), Is.False);
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
