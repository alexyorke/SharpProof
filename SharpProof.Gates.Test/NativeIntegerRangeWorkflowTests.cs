using System.Globalization;
using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class NativeIntegerRangeWorkflowTests
{
    [TestCase("sbyte", "-128", "127")]
    [TestCase("byte", "0", "255")]
    [TestCase("short", "-32768", "32767")]
    [TestCase("ushort", "0", "65535")]
    [TestCase("char", "0", "65535")]
    [TestCase("int", "-2147483648", "2147483647")]
    [TestCase("uint", "0", "4294967295")]
    [TestCase("long", "-9223372036854775808", "9223372036854775807")]
    [TestCase("ulong", "0", "18446744073709551615")]
    public void ActualCompilerAndRuntimeParameterTypesKeepBothIntegerBoundaries(
        string keyword, string minimumText, string maximumText)
    {
        System.ArgumentNullException.ThrowIfNull(keyword);
        System.ArgumentNullException.ThrowIfNull(maximumText);
        System.ArgumentNullException.ThrowIfNull(minimumText);
        var compilation = AnalyzerGateHost.CreateCompilation(
            $"public static class C {{ public static void Target({keyword} value) {{ }} }}",
            "OrdinaryIntegerRange", includeExternalEffectsFixture: false);
        var method = (IMethodSymbol)compilation.GetTypeByMetadataName("C")!.GetMembers("Target").Single();
        var minimum = BigInteger.Parse(minimumText, CultureInfo.InvariantCulture);
        var maximum = BigInteger.Parse(maximumText, CultureInfo.InvariantCulture);
        using var image = NativeSourceEmission.EmitImage(compilation, "Ordinary source did not compile: ", TestContext.CurrentContext.CancellationToken);
        var context = new AssemblyLoadContext("ordinary-integer-range-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var runtimeType = assembly.GetType("C", throwOnError: true)!.GetMethod("Target")!.GetParameters().Single().ParameterType;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(NativeIntegerRange.FitsRuntimeType(runtimeType, minimum), Is.True);
                Assert.That(NativeIntegerRange.FitsRuntimeType(runtimeType, maximum), Is.True);
                Assert.That(NativeIntegerRange.FitsRuntimeType(runtimeType, minimum - 1), Is.False);
                Assert.That(NativeIntegerRange.FitsRuntimeType(runtimeType, maximum + 1), Is.False);
                Assert.That(NativeIntegerRange.FitsCompilerType(method.Parameters.Single().Type.SpecialType, minimum), Is.True);
                Assert.That(NativeIntegerRange.FitsCompilerType(method.Parameters.Single().Type.SpecialType, maximum), Is.True);
                Assert.That(NativeIntegerRange.FitsCompilerType(method.Parameters.Single().Type.SpecialType, minimum - 1), Is.False);
                Assert.That(NativeIntegerRange.FitsCompilerType(method.Parameters.Single().Type.SpecialType, maximum + 1), Is.False);
            }
        }
        finally
        {
            context.Unload();
        }
    }

    [Test]
    public void OrdinaryStringParameterKeepsTheExistingNonIntegralFallbackPolicies()
    {
        var compilation = AnalyzerGateHost.CreateCompilation(
            "public static class C { public static void Target(string value) { } }",
            "OrdinaryNonIntegerRange", includeExternalEffectsFixture: false);
        var method = (IMethodSymbol)compilation.GetTypeByMetadataName("C")!.GetMembers("Target").Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(NativeIntegerRange.FitsRuntimeType(typeof(string), BigInteger.Zero), Is.False);
            Assert.That(NativeIntegerRange.FitsCompilerType(method.Parameters.Single().Type.SpecialType, BigInteger.Zero), Is.True);
        }
    }
}
