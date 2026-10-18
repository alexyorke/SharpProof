using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task ActualReferenceDowncastKindHasItsFrameworkExceptionLabel()
    {
        const string source = """
            using SharpProof.Attributes;
            public class Base { }
            public sealed class Derived : Base { }
            public static class Subject {
                [AllowedExceptions(typeof(System.InvalidCastException))]
                public static Derived Target(Base value) => (Derived)value;
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        Assert.That(preparation.Total, Is.Not.Null);
        var total = preparation.Total!;
        var instructions = total.Program.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToArray();
        var faults = instructions.OfType<IrThrowInstruction>()
            .Where(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.ExceptionKind == IrExceptionKind.InvalidCast)).ToArray();
        Assert.That(faults, Has.Length.EqualTo(1));
        var kind = faults[0].ExceptionKind;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(total.IsBodyAbstraction, Is.False);
            Assert.That(instructions.OfType<IrHavocInstruction>().Any(instruction =>
                instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Origin == IrHavocOrigin.Approximation)), Is.True);
            Assert.That(total.ExceptionConstraints.Single().AllowedKinds, Does.Contain(kind));
            Assert.That(ExplicitThrowSites.TypeName(kind), Is.EqualTo("System.InvalidCastException"));
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = AssertClaimVerdict(response, WorkerClaimOutcome.Proven, WorkerClaimReason.None,
            WorkerVacuityKind.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            Assert.That(result.ProofCore, Is.Not.Empty);
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
