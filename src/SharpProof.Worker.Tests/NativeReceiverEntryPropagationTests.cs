using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeReceiverEntryPropagationTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CompilerOwnedReceiverDomainAgreesForTotalAndStandaloneEntry(bool requiresNullReceiver)
    {
        var source = "using SharpProof.Attributes; public sealed class ReceiverDomain { public int Target(int value) { " +
            "Contract.Requires(" + (requiresNullReceiver ? "this == null" : "this != null && value > 0") + "); " +
            "Contract.Ensures(Contract.Result<int>() == value); return value; } }";
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.TotalEntry, Is.Not.Null);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var totalEntry = await solver.VerifyEntryAsync(cancellationToken: TestContext.CurrentContext.CancellationToken);
        var independentEntry = await TotalCallableVerifier.VerifyEntryAsync(preparation, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(preparation.Total!.HasReceiver, Is.True);
            Assert.That(preparation.TotalEntry!.HasReceiver, Is.EqualTo(preparation.Total.HasReceiver));
            // Decoding creates separate factory owners; compare canonical role
            // names and types through each owner rather than foreign IDs.
            Assert.That(preparation.TotalEntry.Parameters.SelectMany(parameter => ((parameter.Entry.IsDefault || parameter.Current.IsDefault || parameter.Old.IsDefault) ? throw new System.ArgumentException("Value must be initialized.", nameof(parameter)) : (ImmutableArray.Create(parameter.Entry, parameter.Current, parameter.Old))))
                .Select(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : ((preparation.TotalEntry.Factory.GetString(preparation.TotalEntry.Factory.GetVariableInfo(variable).Name),
                    preparation.TotalEntry.Factory.GetString(preparation.TotalEntry.Factory.GetTypeInfo(preparation.TotalEntry.Factory.GetVariableInfo(variable).Type).Name),
                    preparation.TotalEntry.Factory.GetTypeInfo(preparation.TotalEntry.Factory.GetVariableInfo(variable).Type).Kind,
                    preparation.TotalEntry.Factory.GetTypeInfo(preparation.TotalEntry.Factory.GetVariableInfo(variable).Type).Width,
                    preparation.TotalEntry.Factory.GetTypeInfo(preparation.TotalEntry.Factory.GetVariableInfo(variable).Type).Signed,
                    preparation.TotalEntry.Factory.GetTypeInfo(preparation.TotalEntry.Factory.GetVariableInfo(variable).Type).ElementType.HasValue))),
                Is.EqualTo(preparation.Total.Parameters.SelectMany(parameter => ((parameter.Entry.IsDefault || parameter.Current.IsDefault || parameter.Old.IsDefault) ? throw new System.ArgumentException("Value must be initialized.", nameof(parameter)) : (ImmutableArray.Create(parameter.Entry, parameter.Current, parameter.Old))))
                    .Select(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : ((preparation.Total.Program.Factory.GetString(preparation.Total.Program.Factory.GetVariableInfo(variable).Name),
                        preparation.Total.Program.Factory.GetString(preparation.Total.Program.Factory.GetTypeInfo(preparation.Total.Program.Factory.GetVariableInfo(variable).Type).Name),
                        preparation.Total.Program.Factory.GetTypeInfo(preparation.Total.Program.Factory.GetVariableInfo(variable).Type).Kind,
                        preparation.Total.Program.Factory.GetTypeInfo(preparation.Total.Program.Factory.GetVariableInfo(variable).Type).Width,
                        preparation.Total.Program.Factory.GetTypeInfo(preparation.Total.Program.Factory.GetVariableInfo(variable).Type).Signed,
                        preparation.Total.Program.Factory.GetTypeInfo(preparation.Total.Program.Factory.GetVariableInfo(variable).Type).ElementType.HasValue)))));
            Assert.That(preparation.TotalEntry.Parameters, Has.Length.EqualTo(2));
            Assert.That(totalEntry.Outcome, requiresNullReceiver ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>());
            Assert.That(totalEntry.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(independentEntry.Kind, Is.EqualTo(requiresNullReceiver
                ? CallableEntryFeasibilityKind.Contradictory : CallableEntryFeasibilityKind.Feasible));
            Assert.That(independentEntry.Reason, Is.EqualTo(WorkerClaimReason.None));
        }
    }
}
