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
        var callable = artifact.Callables.Single();
        var total = callable.Total!;
        var graph = total.Graph;
        var index = graph.Members.IndexOf(graph.Members.FirstOrDefault(candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) :
            candidate.Name.StartsWith(IrOpaqueCallSite.DefaultComparerValuePrefix, StringComparison.Ordinal))!);
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        var members = graph.Members.ToBuilder();
        var terms = graph.Terms.ToBuilder();
        var member = members[index];
        switch (scenario)
        {
            case "ordinary":
                members[index] = member with { Name = "ordinary" };
                break;
            case "unknown-kind":
                members[index] = member with { Name = IrOpaqueCallSite.DefaultComparerValuePrefix + "System_Boolean" };
                break;
            case "instance":
                members[index] = member with { IsStatic = false };
                break;
            case "parameter":
                members[index] = member with { ParameterTypes = [member.ReturnType] };
                break;
            case "impure":
                var opaque = graph.Terms.Single(term => term is null ? throw new ArgumentNullException(nameof(term)) : term.Kind == IrTermKind.Opaque && term.A == index);
                terms[graph.Terms.IndexOf(opaque)] = opaque with { C = 1 };
                break;
        }
        graph = graph with { Members = members.ToImmutable(), Terms = terms.ToImmutable() };
        artifact = artifact with { Callables = artifact.Callables.ReplaceAt(0, callable with { Total = total with { Graph = graph } }) };
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
