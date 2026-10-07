using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave8ExceptionInitializationAuditTests
{
    [TestCase("DoesNotThrow", false)]
    [TestCase("DoesNotThrow", true)]
    [TestCase("AllowedExceptions(typeof(System.InvalidOperationException))", false)]
    [TestCase("AllowedExceptions(typeof(System.InvalidOperationException))", true)]
    public async Task ThrowingTargetInitializerCannotProduceCompleteExceptionProof(string attribute, bool published)
    {
        var source = "using SharpProof.Attributes; public static class Subject { " +
            "static Subject() { throw new System.InvalidOperationException(\"user initializer\"); } " +
            "[" + attribute + "] public static int Target() { return 7; } }";
        var artifact = CreateAndCheckRuntime(source, "root-fault");
        var preparation = Prepare(artifact);
        Assert.That(preparation.Total!.EffectsCompleteAtEntry, Is.False);
        if (published)
        {
            var outcome = await Publish(artifact);
            Assert.That(outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        }
        else
        {
            var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
            await TestContext.Out.WriteLineAsync($"native root initializer: {result.Outcome?.GetType().Name}/{result.Reason}");
            Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        }
    }

    [TestCase("no-initializer", WorkerClaimOutcome.Proven)]
    [TestCase("readonly-constant", WorkerClaimOutcome.Proven)]
    [TestCase("empty-explicit-initializer", WorkerClaimOutcome.Proven)]
    [TestCase("callee-fault", WorkerClaimOutcome.Unknown)]
    [TestCase("body-fault", WorkerClaimOutcome.Refuted)]
    public async Task EntryInitializationAndActualBodyFaultControlsRemainDistinct(string scenario, WorkerClaimOutcome expected)
    {
        var (members, body, other) = scenario switch
        {
            "no-initializer" => ("", "return 7;", ""),
            "readonly-constant" => ("public static readonly int State = 1;", "return 7;", ""),
            "empty-explicit-initializer" => ("static Subject() { }", "return 7;", ""),
            "callee-fault" => ("", "return Other.Helper();", "public static class Other { static Other() { throw new System.InvalidOperationException(\"callee initializer\"); } public static int Helper() { return 7; } }"),
            "body-fault" => ("", "throw new System.InvalidOperationException(\"body\");", ""),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var source = "using SharpProof.Attributes; public static class Subject { " + members +
            " [DoesNotThrow] public static int Target() { " + body + " } } " + other;
        var artifact = CreateAndCheckRuntime(source, scenario);
        var preparation = Prepare(artifact);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        await TestContext.Out.WriteLineAsync($"native {scenario}: entryComplete={preparation.Total!.EffectsCompleteAtEntry}; {result.Outcome?.GetType().Name}/{result.Reason}");
        var published = await Publish(artifact);
        Assert.That(published, Is.EqualTo(expected));
        if (expected == WorkerClaimOutcome.Proven)
        { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
        else if (expected == WorkerClaimOutcome.Refuted)
        { Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>(), result.Reason.ToString()); }
        else
        { Assert.That(result.Outcome, Is.Null); }
    }

    private static CompilerManifestArtifact CreateAndCheckRuntime(string source, string scenario)
    {
        var compilation = TestCompilation.Create("Wave8ExceptionInitialization", ("Subject.cs", source));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Debug));
        Assert.That(compilation.Options.NullableContextOptions, Is.EqualTo(NullableContextOptions.Enable));
        Assert.That(((CSharpParseOptions)compilation.SyntaxTrees.Single().Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("Wave8ExceptionInitialization", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var target = assembly.GetType("Subject")!.GetMethod("Target")!;
            if (scenario is "root-fault" or "callee-fault" or "body-fault")
            {
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => target.Invoke(null, null)));
                if (scenario == "body-fault")
                { Assert.That(error!.InnerException, Is.TypeOf<InvalidOperationException>()); }
                else
                {
                    var owner = assembly.GetType(scenario == "root-fault" ? "Subject" : "Other")!;
                    Assert.That(owner.Attributes.HasFlag(System.Reflection.TypeAttributes.BeforeFieldInit), Is.False);
                    Assert.That(error!.InnerException, Is.TypeOf<TypeInitializationException>());
                    Assert.That(error.InnerException!.InnerException, Is.TypeOf<InvalidOperationException>());
                }
            }
            else
            { Assert.That(target.Invoke(null, null), Is.EqualTo(7)); }
        }
        finally { context.Unload(); }
        var discovery = new ClaimManifestBuilder(compilation).Build();
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
    }

    private static CompilerCallablePreparation Prepare(CompilerManifestArtifact artifact)
    {
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(item => item.Entry.CallableId.Contains("Subject.Target", StringComparison.Ordinal));
    }

    private static async Task<WorkerClaimOutcome> Publish(CompilerManifestArtifact artifact)
    {
        using var project = new ShadowTestProject(artifact);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        await TestContext.Out.WriteLineAsync($"published initializer: {claim.Outcome}/{claim.Reason}/{claim.EffectCertainty}");
        return claim.Outcome;
    }
}
