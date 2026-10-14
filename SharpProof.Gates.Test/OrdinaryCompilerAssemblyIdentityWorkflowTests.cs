using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryCompilerAssemblyIdentityWorkflowTests
{
    [Test]
    public void IndependentlyEmittedNominalTypesKeepTheirOwnAssemblyIdentity()
    {
        const string source = "namespace OrdinaryIdentity; public struct Value { public int Number; }";
        var first = AnalyzerGateHost.CreateCompilation(source, "OrdinaryFirstLibrary", includeExternalEffectsFixture: false);
        var second = AnalyzerGateHost.CreateCompilation(source, "OrdinarySecondLibrary", includeExternalEffectsFixture: false);
        var firstSymbol = first.GetTypeByMetadataName("OrdinaryIdentity.Value")!;
        var secondSymbol = second.GetTypeByMetadataName("OrdinaryIdentity.Value")!;
        using var firstImage = NativeSourceEmission.EmitImage(first, "First library failed: ", CancellationToken.None);
        using var secondImage = NativeSourceEmission.EmitImage(second, "Second library failed: ", CancellationToken.None);
        var context = new AssemblyLoadContext("ordinary-independent-libraries-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            var firstType = context.LoadFromStream(firstImage).GetType("OrdinaryIdentity.Value", throwOnError: true)!;
            var secondType = context.LoadFromStream(secondImage).GetType("OrdinaryIdentity.Value", throwOnError: true)!;
            using (Assert.EnterMultipleScope())
            {
                Assert.That(firstType.FullName, Is.EqualTo(secondType.FullName));
                Assert.That(firstType.Assembly.GetName().Name, Is.EqualTo(first.AssemblyName));
                Assert.That(secondType.Assembly.GetName().Name, Is.EqualTo(second.AssemblyName));
                Assert.That(firstType, Is.Not.EqualTo(secondType));
                Assert.That(CompilerClrTypeMatcher.Matches(firstSymbol, firstType, typeof(int)), Is.True);
                Assert.That(CompilerClrTypeMatcher.Matches(secondSymbol, secondType, typeof(int)), Is.True);
                Assert.That(CompilerClrTypeMatcher.Matches(firstSymbol, secondType, typeof(int)), Is.False);
                Assert.That(CompilerClrTypeMatcher.Matches(secondSymbol, firstType, typeof(int)), Is.False);
            }
        }
        finally
        {
            context.Unload();
        }
    }
}
