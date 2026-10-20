using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [Test]
    public async Task CompilerArtifactCarriesTaskChildrenWithoutInventingRootVerdicts()
    {
        var artifact = CreateArtifact("using SharpProof.Attributes; using System.Threading.Tasks; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,100)] public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {for(int i=0;i<count;i++){}} }");
        Assert.That(artifact.Callables.Single().ForkJoinProjection, Is.Not.Null.And.Not.Empty);
        var target = Read(artifact);
        Assert.That(target.Total, Is.Null);
        Assert.That(target.TotalEntry, Is.Not.Null);
        Assert.That(target.ForkJoinProjection, Is.Not.Null);
        var source = NativeForkJoinSourceCostVerifier.Check(target.ForkJoinProjection!, target.TotalEntry!, TestContext.CurrentContext.CancellationToken);
        Assert.That(source.HasSourceCosts, Is.True, source.UnknownReason);
        Assert.That(source.Upper!.LoopEntries.ToDisplayString(), Is.EqualTo("(max(0, n) + 3)"));
        Assert.That(target.Compilation.ResourceMetadataReferences, Has.Some.Matches<CompilerReferenceSnapshot>(reference => reference is null ? throw new ArgumentNullException(nameof(reference)) :
            reference.Identity == target.ForkJoinProjection!.FrameworkAssemblyIdentity));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
    }

    [TestCase("identity")]
    [TestCase("entry")]
    [TestCase("ordinary-body")]
    public void CompilerArtifactRejectsUnownedTaskProjectionData(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var artifact = CreateArtifact("using SharpProof.Attributes; using System.Threading.Tasks; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,100)] public static Task Target(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {for(int i=0;i<count;i++){}} }");
        var callable = artifact.Callables.Single();
        if (mutation == "entry")
        { callable.TotalEntry = null; }
        else if (mutation == "ordinary-body")
        {
            var ordinary = CreateArtifact("using SharpProof.Attributes; public static class C { [ResourceUpperBound(ResourceMetric.LoopBodyEntries,100)] public static void Target(int n) {for(int i=0;i<n;i++){}} }");
            ordinary.Callables.Single().ForkJoinProjection = callable.ForkJoinProjection;
            artifact = ordinary;
        }
        else
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(callable.ForkJoinProjection!)!.AsObject();
            json["root"] = "other";
            callable.ForkJoinProjection = System.Text.Encoding.UTF8.GetBytes(json.ToJsonString());
        }
        Action read = () => { _ = Read(artifact); };
        Rejects(read);
    }
}
