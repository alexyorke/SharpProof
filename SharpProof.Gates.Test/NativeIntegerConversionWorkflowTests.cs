using System.Globalization;
using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class NativeIntegerConversionWorkflowTests
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
    public void ActualCompilerAndClrTypesKeepBoxedBoundaryValuesAndRejectOverflow(
        string keyword, string minimumText, string maximumText)
    {
        var compilation = AnalyzerGateHost.CreateCompilation($$"""
            public static class C {
                public static void Target({{keyword}} value) { }
                public static {{keyword}} Lower() => {{keyword}}.MinValue;
                public static {{keyword}} Upper() => {{keyword}}.MaxValue;
            }
            """, "OrdinaryIntegerConversion", includeExternalEffectsFixture: false);
        var compilerType = ((IMethodSymbol)compilation.GetTypeByMetadataName("C")!.GetMembers("Target").Single())
            .Parameters.Single().Type;
        var minimum = BigInteger.Parse(minimumText, CultureInfo.InvariantCulture);
        var maximum = BigInteger.Parse(maximumText, CultureInfo.InvariantCulture);
        using var image = NativeSourceEmission.EmitImage(compilation, "Ordinary source did not compile: ", TestContext.CurrentContext.CancellationToken);
        var context = new AssemblyLoadContext("ordinary-integer-conversion-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var owner = context.LoadFromStream(image).GetType("C", throwOnError: true)!;
            var runtimeType = owner.GetMethod("Target")!.GetParameters().Single().ParameterType;
            foreach (var (numeric, member) in new[] { (minimum, "Lower"), (maximum, "Upper") })
            {
                var expected = owner.GetMethod(member)!.Invoke(null, null);
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(NativeIntegerConversion.TryRuntimeValue(runtimeType, numeric, out var runtimeValue), Is.True);
                    Assert.That(NativeIntegerConversion.TryCompilerValue(compilerType, numeric, out var compilerValue), Is.True);
                    Assert.That(runtimeValue, Is.TypeOf(runtimeType));
                    Assert.That(compilerValue, Is.TypeOf(runtimeType));
                    Assert.That(runtimeValue, Is.EqualTo(expected));
                    Assert.That(compilerValue, Is.EqualTo(expected));
                }
            }
            foreach (var numeric in new[] { minimum - 1, maximum + 1 })
            {
                using (Assert.EnterMultipleScope())
                {
                    Assert.That(NativeIntegerConversion.TryRuntimeValue(runtimeType, numeric, out var runtimeValue), Is.False);
                    Assert.That(NativeIntegerConversion.TryCompilerValue(compilerType, numeric, out var compilerValue), Is.False);
                    Assert.That(runtimeValue, Is.Null);
                    Assert.That(compilerValue, Is.Null);
                }
            }
        }
        finally
        {
            context.Unload();
        }
    }

    [Test]
    public void ActualStringParameterKeepsBothUnsupportedIntegerConversionsNull()
    {
        var compilation = AnalyzerGateHost.CreateCompilation(
            "public static class C { public static void Target(string value) { } }",
            "OrdinaryNonIntegerConversion", includeExternalEffectsFixture: false);
        var compilerType = ((IMethodSymbol)compilation.GetTypeByMetadataName("C")!.GetMembers("Target").Single())
            .Parameters.Single().Type;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(NativeIntegerConversion.TryRuntimeValue(typeof(string), BigInteger.Zero, out var runtimeValue), Is.False);
            Assert.That(NativeIntegerConversion.TryCompilerValue(compilerType, BigInteger.Zero, out var compilerValue), Is.False);
            Assert.That(runtimeValue, Is.Null);
            Assert.That(compilerValue, Is.Null);
        }
    }
}
