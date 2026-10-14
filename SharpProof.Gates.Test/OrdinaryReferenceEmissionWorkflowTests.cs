using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Attributes;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryReferenceEmissionWorkflowTests
{
    private static readonly string[] ExpectedAttributeNames = ["SharpProofTrustedAttribute", "EffectContractAttribute"];

    [TestCase(false)]
    [TestCase(true)]
    public void RealMissingCompilationReferencesPreserveTheLibraryEmissionFailure(bool frameworkOnly)
    {
        var original = AnalyzerGateHost.CreateCompilation("public class Control {}",
            "OrdinaryReferenceSource", includeExternalEffectsFixture: false);
        ImmutableArray<MetadataReference> references = frameworkOnly
            ? [.. original.References.Where(reference => reference.Display != typeof(Contract).Assembly.Location)]
            : [];

        var exception = Assert.Throws<InvalidOperationException>((Action)(() => AnalyzerGateHost.CreateReferences(references)));

        Assert.That(exception!.Message, Does.StartWith("Could not create the corpus effect-contract reference: "));
        Assert.That(exception.Message, Does.Contain(frameworkOnly ? "CS0246" : "CS0518"));
        Assert.That(exception.Message, Does.Contain(frameworkOnly ? "SharpProof" : "System.Object"));
    }

    [Test]
    public void RealFrameworkAndContractsEmitTheLibraryAndBindItsTrustedSynchronization()
    {
        var original = AnalyzerGateHost.CreateCompilation("public class Control {}",
            "OrdinaryCompleteReferences", includeExternalEffectsFixture: false);
        var references = AnalyzerGateHost.CreateReferences([.. original.References]);
        var compilation = AnalyzerGateHost.CreateCompilation("""
            public static class Caller {
                public static void Run() => ExternalCorpusEffects.Synchronize();
            }
            """, "OrdinaryReferenceCaller", includeExternalEffectsFixture: false)
            .RemoveAllReferences().AddReferences(references);

        Assert.That(references, Has.Length.EqualTo(original.References.Count() + 1));
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var method = compilation.GetTypeByMetadataName("ExternalCorpusEffects")!
            .GetMembers("Synchronize").OfType<IMethodSymbol>().Single();
        Assert.That(method.DeclaringSyntaxReferences, Is.Empty);
        Assert.That(method.GetAttributes().Select(static attribute => attribute.AttributeClass!.Name),
            Is.EquivalentTo(ExpectedAttributeNames));
        var contract = method.GetAttributes().Single(static attribute => attribute.AttributeClass!.Name == "EffectContractAttribute");
        Assert.That(contract.ConstructorArguments.Single().Value, Is.EqualTo((long)SharpProofEffect.Synchronizes));
        Assert.That(contract.NamedArguments.Single(static argument => argument.Key == "Complete").Value.Value, Is.EqualTo(true));
    }
}
