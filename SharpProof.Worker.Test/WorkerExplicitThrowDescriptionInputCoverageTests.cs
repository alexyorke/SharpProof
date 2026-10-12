using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

public sealed partial class WorkerTests
{
    [Test]
    public async Task ActualReturnSiteHasNoExplicitThrowDescription()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static int Target(bool condition) {
                    if (condition) { throw new System.InvalidOperationException(); }
                    return 0;
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.IsSuccess, Is.True, preparation.FailureReason.ToString());
        Assert.That(preparation.Total, Is.Not.Null);
        var program = preparation.Total!.Program;
        var instructions = program.Blocks.SelectMany(block => block.Instructions).ToArray();
        var thrown = instructions.OfType<IrThrowInstruction>()
            .Single(instruction => instruction.ExceptionKind == IrExceptionKind.Explicit);
        var returned = instructions.OfType<IrReturnInstruction>().Single();
        var types = ExplicitThrowSites.Types(program.Factory, thrown.Operation);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(types[0], Is.EqualTo("System.InvalidOperationException"));
            Assert.That(types, Does.Contain("System.Exception"));
            Assert.That(ExplicitThrowSites.IsExact(program.Factory, thrown.Operation), Is.True);
            Assert.That(ExplicitThrowSites.Types(program.Factory, returned.Operation), Is.Empty);
            Assert.That(ExplicitThrowSites.IsExact(program.Factory, returned.Operation), Is.False);
        }

        using var project = TestProject.Create(source);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        var result = AssertClaimVerdict(response, WorkerClaimOutcome.Refuted, WorkerClaimReason.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.EffectWitness?.Kind, Is.EqualTo("explicit-throw"));
            Assert.That(result.EffectWitness?.Detail, Is.EqualTo("System.InvalidOperationException"));
            Assert.That(result.EffectWitness?.ExactExceptionTypeHierarchy, Is.EqualTo(types));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }
}
