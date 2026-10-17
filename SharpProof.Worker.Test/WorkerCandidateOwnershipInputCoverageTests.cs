using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerCandidateOwnershipInputCoverageTests
{
    [Test]
    public void ClausesRequireTheirOriginalOwnedParameterBindings()
    {
        var original = EnrollSourceControl();
        var exception = Assert.Throws<ArgumentException>(new Action(() =>
            new PassiveCallableCandidate(original.CallableId, original.Program, [], original.Result,
                original.Requires, original.Ensures)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("requires"));
            Assert.That(exception.Message, Does.Contain("Clauses may refer only to owned canonical bindings"));
        }
    }

    [TestCase(2, "Disjoint inputs must be canonical owned pairs")]
    [TestCase(1, "Disjoint inputs require distinct nominal reference types")]
    public void DisjointInputPairsRequireOwnedNominalReferenceParameters(int right, string message)
    {
        System.ArgumentNullException.ThrowIfNull(message);
        var original = EnrollSourceControl();
        var exception = Assert.Throws<ArgumentException>(new Action(() =>
            new PassiveCallableCandidate(original.CallableId, original.Program, original.Parameters, original.Result,
                original.Requires, original.Ensures,
                disjointInputs: [new CompilerDisjointInputPair(0, right)])));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("disjointInputs"));
            Assert.That(exception.Message, Does.Contain(message));
        }
    }

    private static PassiveCallableCandidate EnrollSourceControl()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value, int unused) {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() >= value);
                    return value;
                }
            }
            """);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparations.Single());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate!.Parameters, Has.Length.EqualTo(2));
            Assert.That(candidate.Requires, Has.Length.EqualTo(1));
            Assert.That(candidate.Ensures, Has.Length.EqualTo(1));
        }
        return candidate!;
    }
}
