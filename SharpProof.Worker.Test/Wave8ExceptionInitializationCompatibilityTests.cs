using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave8ExceptionInitializationCompatibilityTests
{
    [Test]
    public void Schema30CannotSupplyTheOldExceptionAdmissionMeaning()
    {
        var artifact = Create();
        artifact.SchemaVersion = 30;
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ConstraintRowPresenceIsTheNativeAndPublishedAdmissionBoundary(bool omit)
    {
        var artifact = Create();
        var total = artifact.Callables.Single().Total!;
        Assert.That(total.ExceptionConstraints, Has.Length.EqualTo(1));
        if (omit)
        { total.ExceptionConstraints = []; }
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var native = await NativeExceptionEffectVerifier.VerifyAsync(preparations.Single(), new WorkerBudgets());
        if (omit)
        { Assert.That(native.Outcome, Is.Null); }
        else
        { Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>()); }
        using var project = new ShadowTestProject(artifact);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var result = response.ClaimResults.Single();
        Assert.That(result.Outcome, Is.EqualTo(omit ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven));
        Assert.That(result.EffectCertainty, Is.EqualTo(omit ? WorkerEffectEvidenceCertainty.Unavailable :
            WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
    }

    private static CompilerManifestArtifact Create()
    {
        var compilation = TestCompilation.Create("Wave8InitializationCompatibility", ("Subject.cs",
            "using SharpProof.Attributes; public static class Subject { [DoesNotThrow] public static int Target() { return 7; } }"));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Debug));
        Assert.That(compilation.Options.NullableContextOptions, Is.EqualTo(NullableContextOptions.Enable));
        Assert.That(((CSharpParseOptions)compilation.SyntaxTrees.Single().Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
    }
}
