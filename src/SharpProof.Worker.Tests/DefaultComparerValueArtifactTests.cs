using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

internal sealed class DefaultComparerValueArtifactTests
{
    [TestCase("valid")]
    [TestCase("ordinary")]
    [TestCase("unknown-kind")]
    [TestCase("instance")]
    [TestCase("parameter")]
    [TestCase("impure")]
    public void PortableAdmissionRequiresTheOwnedCacheValueShape(string scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject {
                [ZeroAllocations]
                public static int Root() {
                    Contract.Ensures(Contract.Result<int>() == 1);
                    var value = System.Collections.Generic.Comparer<int>.Default;
                    return value != null ? 1 : 0;
                }
            }
            """);
        var graph = artifact.Callables.Single().Total!.Graph;
        var index = Array.FindIndex(graph.Members, candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) :
            candidate.Name.StartsWith(IrOpaqueCallSite.DefaultComparerValuePrefix, StringComparison.Ordinal));
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        var member = graph.Members[index];
        switch (scenario)
        {
            case "ordinary":
                member.Name = "ordinary";
                break;
            case "unknown-kind":
                member.Name = IrOpaqueCallSite.DefaultComparerValuePrefix + "System_Boolean";
                break;
            case "instance":
                member.IsStatic = false;
                break;
            case "parameter":
                member.ParameterTypes = [member.ReturnType];
                break;
            case "impure":
                graph.Terms.Single(term => term is null ? throw new ArgumentNullException(nameof(term)) : term.Kind == IrTermKind.Opaque && term.A == index).C = 1;
                break;
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        if (scenario == "valid")
        {
            CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(preparations.Single().Total, Is.Not.Null);
        }
        else
        {
            Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        }
    }
}
