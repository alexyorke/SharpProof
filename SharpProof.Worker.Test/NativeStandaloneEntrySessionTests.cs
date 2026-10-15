using System.Runtime.Loader;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Smt;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeStandaloneEntrySessionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CompilerOwnedInstanceEntryRetainsReceiverAndActualRequires(bool contradictory)
    {
        var source = Source(contradictory);
        using var image = new MemoryStream();
        var compilation = TestCompilation.Create("StandaloneInstanceEntry", source)
            .RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp12)));
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        var context = new AssemblyLoadContext("standalone-instance-entry", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("InstanceEntry")!;
            var instance = Activator.CreateInstance(type)!;
            // Normal CLR elision executes the body; contradictory Requires is
            // still an empty declared entry domain, not an unconditional fact.
            Assert.That(type.GetMethod("Target")!.Invoke(instance, [1]), Is.EqualTo(1));
        }
        finally { context.Unload(); }
        var preparation = Prepare(source);
        var entry = preparation.TotalEntry!;
        Assert.That(entry.HasReceiver, Is.True);
        Assert.That(entry.Parameters, Has.Length.EqualTo(2));
        Assert.That(entry.Clauses, Has.Length.EqualTo(1));
        var result = await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets(), CancellationToken.None);
        Assert.That(result.Kind, Is.EqualTo(contradictory ? CallableEntryFeasibilityKind.Contradictory : CallableEntryFeasibilityKind.Feasible));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
        if (contradictory)
        {
            Assert.That(result.ProofCore, Is.Not.Empty);
            Assert.That(result.ProofCore.All(label => label.StartsWith("requires:", StringComparison.Ordinal)), Is.True);
            Assert.That(result.UsedAssumptionIds, Is.EqualTo(entry.Clauses.Select(clause => clause.AssumptionId!)));
        }
        else
        {
            Assert.That(result.ProofCore, Is.Empty);
            Assert.That(result.UsedAssumptionIds, Is.Empty);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeBackendAndItsMethodBudgetMustBeSuppliedTogether(bool backendPresent)
    {
        var preparation = Prepare(Source(false));
        var budgets = new WorkerBudgets();
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(preparation.TotalEntry!.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        var resource = new MethodResourceBudget(() => session.ConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
        var exception = Assert.ThrowsAsync<ArgumentException>(new Func<Task>(async () =>
            await TotalCallableVerifier.VerifyEntryAsync(preparation, budgets, CancellationToken.None,
                backendPresent ? session : null, backendPresent ? null : resource)));
        Assert.That(exception!.ParamName, Is.EqualTo("backend"));
        Assert.That(session.ConsumedResourceCount, Is.Zero);
    }

    [Test]
    public async Task ActualNativeSessionWithItsMatchingDefaultBudgetChecksInstanceEntry()
    {
        var preparation = Prepare(Source(false));
        var budgets = new WorkerBudgets();
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(preparation.TotalEntry!.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
        var resource = new MethodResourceBudget(() => session.ConsumedResourceCount, budgets.QueryRlimit, budgets.MethodRlimit);
        var result = await TotalCallableVerifier.VerifyEntryAsync(preparation, budgets, CancellationToken.None, session, resource);
        Assert.That(result.Kind, Is.EqualTo(CallableEntryFeasibilityKind.Feasible));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(result.ProofCore, Is.Empty);
        Assert.That(result.UsedAssumptionIds, Is.Empty);
        Assert.That(session.ConsumedResourceCount, Is.GreaterThan(0));
        Assert.That(resource.IsExceeded, Is.False);
    }

    private static string Source(bool contradictory)
    {
        return "using SharpProof.Attributes; public sealed class InstanceEntry { public int Target(int value) { " +
            "Contract.Requires(" + (contradictory ? "value > 0 && value < 0" : "value > 0") + "); " +
            "Contract.Ensures(Contract.Result<int>() == value); return value; } }";
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        return preparation;
    }
}
