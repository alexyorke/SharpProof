using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class NativeCorpusEmptyWorkflowTests
{
    [Test]
    public async Task OrdinarySourceWithoutCallableClaimsHasNoNativeVerdicts()
    {
        var compilation = AnalyzerGateHost.CreateCompilation("public sealed class C { }");

        var verdicts = await NativeCorpusVerifier.VerifyAsync(compilation, TestContext.CurrentContext.CancellationToken);

        Assert.That(verdicts, Is.Empty);
    }
}
