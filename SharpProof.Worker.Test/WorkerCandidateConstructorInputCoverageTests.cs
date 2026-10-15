using NUnit.Framework;
using SharpProof.CompilerArtifact;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerCandidateConstructorInputCoverageTests
{
    private const string Source = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int value) {
                Contract.Requires(value >= 0);
                Contract.Ensures(Contract.Result<int>() >= value);
                return value;
            }
        }
        """;

    [TestCase("")]
    [TestCase(" ")]
    public void MissingCallableIdentityIsRejected(string identity)
    {
        var original = EnrollSourceControl();
        var exception = Assert.Throws<ArgumentException>(new Action(() =>
            new PassiveCallableCandidate(identity, original.Program, original.Parameters, original.Result,
                original.Requires, original.Ensures)));

        Assert.That(exception!.ParamName, Is.EqualTo("callableId"));
    }

    [TestCase("parameters")]
    [TestCase("requires")]
    [TestCase("ensures")]
    public void UninitializedConstructorArraysAreRejected(string input)
    {
        var original = EnrollSourceControl();
        var exception = Assert.Throws<ArgumentException>(new Action(() =>
            new PassiveCallableCandidate(original.CallableId, original.Program,
                input == "parameters" ? default : original.Parameters, original.Result,
                input == "requires" ? default : original.Requires,
                input == "ensures" ? default : original.Ensures)));

        Assert.That(exception!.Message, Does.Contain("Candidate arrays must be initialized"));
    }

    [Test]
    public void ResultDependentClauseCannotBeUsedAsAnEntryPrecondition()
    {
        var original = EnrollSourceControl();
        var exception = Assert.Throws<ArgumentException>(new Action(() =>
            new PassiveCallableCandidate(original.CallableId, original.Program, original.Parameters, original.Result,
                original.Ensures, original.Ensures)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(exception!.ParamName, Is.EqualTo("requires"));
            Assert.That(exception.Message, Does.Contain("Requires must refer only to entry inputs"));
        }
    }

    private static PassiveCallableCandidate EnrollSourceControl()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(Source);
        CompilerManifestArtifactJson.DeserializePrepared(
            CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparations.Single());
        using (Assert.EnterMultipleScope())
        {
            Assert.That(candidate, Is.Not.Null);
            Assert.That(candidate!.Parameters, Has.Length.EqualTo(1));
            Assert.That(candidate.Requires, Has.Length.EqualTo(1));
            Assert.That(candidate.Ensures, Has.Length.EqualTo(1));
        }
        return candidate!;
    }
}
