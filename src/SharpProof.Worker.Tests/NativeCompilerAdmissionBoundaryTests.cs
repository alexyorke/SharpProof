using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeCompilerAdmissionBoundaryTests
{
    [TestCase("allocation")]
    [TestCase("purity")]
    [TestCase("effects")]
    public async Task AnOrdinaryPostconditionDoesNotAuthorizeAnEffectCheck(string contract)
    {
        System.ArgumentNullException.ThrowIfNull(contract);
        var preparation = Prepare("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int x) {
                    Contract.Ensures(Contract.Result<int>() == x);
                    return x;
                }
            }
            """);
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.EffectClaims, Is.Empty);
        var budgets = new WorkerBudgets();
        var result = contract switch
        {
            "allocation" => await NativeEffectSiteVerifier.VerifyAsync(preparation, budgets, cancellationToken: TestContext.CurrentContext.CancellationToken),
            "purity" => await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, budgets, cancellationToken: TestContext.CurrentContext.CancellationToken),
            _ => await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, budgets, cancellationToken: TestContext.CurrentContext.CancellationToken)
        };
        AssertUnknown(result, WorkerClaimReason.UnsupportedContract);
    }

    [Test]
    public async Task ACompilerRejectedAssumptionCannotAuthorizeCapabilities()
    {
        // The same unsupported assumption producer is exercised by
        // CompilerTotalCallableArtifactTests.SourceAdmissionDistinguishesUnsupportedBodiesAndTypedAttributes.
        var preparation = Prepare("""
            using SharpProof.Attributes;
            public static class Subject {
                [AllowedCapabilities(SharpProofCapability.None)]
                public static int Target(int x) {
                    Contract.Assume(System.Math.Abs(x) > 0);
                    Contract.Ensures(Contract.Result<int>() == x);
                    return x;
                }
            }
            """);
        Assert.That(preparation.Total, Is.Null);
        Assert.That(preparation.EffectClaims.Single().ContractKind,
            Is.EqualTo(WorkerEffectContractKind.AllowedCapabilities));
        AssertUnknown(await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, new WorkerBudgets(), cancellationToken: TestContext.CurrentContext.CancellationToken),
            WorkerClaimReason.UnsupportedContract);
    }

    [TestCase(64)]
    [TestCase(65)]
    public async Task ActualRequiresRespectsTheDefaultDepthBoundary(int depth)
    {
        // A left-associated conjunction of a Boolean parameter has one IR
        // level per operand. These are the two adjacent default-limit inputs.
        var predicate = string.Join(" & ", Enumerable.Repeat("x", depth));
        var preparation = Prepare($$"""
            using SharpProof.Attributes;
            public static class Subject {
                [ZeroAllocations, EnforcePure, DoesNotThrow]
                public static bool Target(bool x) {
                    Contract.Requires({{predicate}});
                    return x;
                }
            }
            """);
        Assert.That(preparation.Total, Is.Not.Null);
        var total = preparation.Total!;
        Assert.That(total.IsBodyAbstraction, Is.False);
        Assert.That(total.EffectsCompleteAtEntry, Is.True);
        Assert.That(preparation.EffectClaims, Has.Length.EqualTo(3));
        Assert.That(preparation.EffectClaims.All(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (total.ValidEffectClaimIds.Contains(claim.ClaimId))), Is.True);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(candidate!.Requires, Has.Length.EqualTo(1));
        Assert.That(IrTermAnalysis.GetDepth(candidate.Requires.Single().Value), Is.EqualTo(depth));
        Assert.That(IrTermAnalysis.GetDepth(candidate.Requires.Single().Safe), Is.LessThanOrEqualTo(depth));
        var budgets = new WorkerBudgets();
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
        var checks = ImmutableArray.Create(
            await NativeEffectSiteVerifier.VerifyAsync(preparation, budgets, cancellationToken: TestContext.CurrentContext.CancellationToken),
            await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, budgets, cancellationToken: TestContext.CurrentContext.CancellationToken),
            await NativeExceptionEffectVerifier.VerifyAsync(preparation, budgets, cancellationToken: TestContext.CurrentContext.CancellationToken));
        foreach (var check in checks)
        {
            if (depth == 65)
            { AssertUnknown(check, WorkerClaimReason.UnsupportedExpression); }
            else
            {
                Assert.That(check.Outcome, Is.TypeOf<ProvenOutcome>(), check.Reason.ToString());
                Assert.That(check.Reason, Is.EqualTo(WorkerClaimReason.None));
                Assert.That(check.QueryCompleted, Is.False);
            }
        }
    }

    private static CompilerCallablePreparation Prepare(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("NativeAdmissionBoundary", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        return preparations.Single();
    }

    private static void AssertUnknown(PassiveCallableCheckResult result, WorkerClaimReason reason)
    {
        System.ArgumentNullException.ThrowIfNull(result);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.Null);
            Assert.That(result.Reason, Is.EqualTo(reason));
            Assert.That(result.EntryModel, Is.Empty);
            Assert.That(result.Core, Is.Empty);
            Assert.That(result.BodyAssumptions, Is.Empty);
            Assert.That(result.QueryCompleted, Is.False);
        }
    }
}
