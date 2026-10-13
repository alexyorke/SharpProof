using NUnit.Framework;
using SharpProof.BuildTasks;

namespace SharpProof.Package.Test;

[TestFixture]
public sealed class VerificationPublicationNullArgumentTests
{
    [Test]
    public void PrepareRejectsNullArgumentEntriesBeforeReadingAnyPath()
    {
        string[] arguments = ["verify", null!, "--request", "request.json"];
        Action prepare = () => { VerificationPublication.Prepare(arguments, Path.GetTempPath()); };
        Assert.That(prepare, Throws.ArgumentException.With.Message.Contains("must not be null"));
    }
}
