using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave8ExceptionInitializationContainingTypeTests
{
    [TestCase("unused-containing-initializer", WorkerClaimOutcome.Proven)]
    public async Task NoFaultInitializationAndExistingCalleeBoundariesStayDistinct(string scenario, WorkerClaimOutcome expected)
    {
        var artifact = CreateAndRun(scenario);
        var preparation = Prepare(artifact);
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        await TestContext.Out.WriteLineAsync($"{scenario}: entryComplete={preparation.Total?.EffectsCompleteAtEntry}; native={result.Outcome?.GetType().Name}/{result.Reason}");
        Assert.That(await Publish(artifact), Is.EqualTo(expected));
        if (expected == WorkerClaimOutcome.Proven)
        { Assert.That(result.Outcome, Is.TypeOf<ProvenOutcome>(), result.Reason.ToString()); }
        else
        { Assert.That(result.Outcome, Is.Null); }
    }

    private static string Source(string scenario)
    {
        Assert.That(scenario, Is.EqualTo("unused-containing-initializer"));
        return "using SharpProof.Attributes; public class Outer { static Outer() { throw new System.InvalidOperationException(\"unused outer\"); } public static class Subject { [DoesNotThrow] public static int Target() { return 7; } } }";
    }
    private static CompilerManifestArtifact CreateAndRun(string scenario)
    {
        var compilation = TestCompilation.Create("Wave8InitializationContainingType", ("Subject.cs", Source(scenario)));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Debug));
        Assert.That(compilation.Options.NullableContextOptions, Is.EqualTo(NullableContextOptions.Enable));
        Assert.That(((CSharpParseOptions)compilation.SyntaxTrees.Single().Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("Wave8InitializationContainingType", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var type = assembly.GetType("Outer+Subject")!;
            object? Invoke()
            {
                if (scenario is "constructor-fault" or "base-constructor-fault")
                { return type.GetConstructor(Type.EmptyTypes)!.Invoke(null); }
                var receiver = scenario == "instance-safe-initializer" ? Activator.CreateInstance(type) : null;
                return type.GetMethod("Target")!.Invoke(receiver, null);
            }
            if (scenario is "module-owner-fault" or "event-initializer-fault")
            {
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => Invoke()));
                Exception? fault = error!.InnerException;
                Assert.That(fault, Is.TypeOf<TypeInitializationException>());
                while (fault is TypeInitializationException initialization)
                { fault = initialization.InnerException; }
                Assert.That(fault, Is.TypeOf<InvalidOperationException>());
            }
            else
            {
                Assert.That(Invoke(), Is.EqualTo(7));
                if (scenario is "module-constant-write" or "instance-safe-initializer")
                { Assert.That(type.GetField("State")!.GetValue(null), Is.EqualTo(1)); }
            }
        }
        finally { context.Unload(); }
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
    }

    private static CompilerCallablePreparation Prepare(CompilerManifestArtifact artifact)
    {
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        return preparations.Single(item => item.EffectClaims.Length != 0);
    }

    private static async Task<WorkerClaimOutcome> Publish(CompilerManifestArtifact artifact)
    {
        using var project = new ShadowTestProject(artifact);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        await TestContext.Out.WriteLineAsync($"published={claim.Outcome}/{claim.Reason}/{claim.EffectCertainty}");
        return claim.Outcome;
    }
}
