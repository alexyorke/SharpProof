using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275NativeAssumptionMapTests
{
    [Test]
    public async Task ActualContradictorySourceCoreMapsOnlyPresentOwnedAssumptionIds()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) { Contract.Requires(false); return value; }
            }
            """;
        var compilation = TestCompilation.Create("Pr275MappedAssumptionIds", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact);
        var preparation = project.Snapshot.Callables.Single();
        var entry = preparation.TotalEntry;
        Assert.That(entry, Is.Not.Null);
        Assert.That(entry!.Clauses, Has.Length.EqualTo(1));
        var clause = entry.Clauses.Single();
        Assert.That(clause.Kind, Is.EqualTo(CompilerContractKind.Requires));
        Assert.That(clause.AssumptionId, Is.Not.Null.And.Not.Empty);
        Assert.That(preparation.Entry.Assumptions.Select(assumption => assumption is null ? throw new System.ArgumentNullException(nameof(assumption)) : (assumption.Id)), Does.Contain(clause.AssumptionId));
        var factory = entry.Factory;
        var justification = new LoweredJustification(clause.Operation);
        var assumption = new Assumption(factory,
            factory.Binary(IrBinaryOperator.AndAlso, clause.Safe, clause.Value), justification);
        var query = new VerificationQuery(factory, ImmutableArray.Create(assumption),
            Goal.CreateInternalConsistency(factory), [.. entry.Parameters.Select(parameter => parameter.Entry)]);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions(project.Request.Budgets.QueryRlimit));
        var outcome = await new ProofKernel(session).VerifyAsync(query, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(outcome, Is.TypeOf<ProvenOutcome>());
        Assert.That(session.ConsumedResourceCount, Is.GreaterThan(0));
        var core = ((ProvenOutcome)outcome).Core;
        Assert.That(core.IsDefault, Is.False);
        Assert.That(core, Has.Length.EqualTo(1));
        Assert.That(core.Single(), Is.EqualTo(justification));
        var ownedIds = new Dictionary<ProofJustification, string> { [justification] = clause.AssumptionId! };
        Assert.That(CallableProofCore.AssumptionIds(core, ownedIds), Is.EqualTo(new[] { clause.AssumptionId }));
        // An absent raw map entry contributes no ID; it grants no claim or proof publication.
        Assert.That(CallableProofCore.AssumptionIds(core, new Dictionary<ProofJustification, string>()), Is.Empty);
    }
}
