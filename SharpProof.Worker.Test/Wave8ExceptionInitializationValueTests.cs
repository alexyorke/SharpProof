using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave8ExceptionInitializationValueTests
{
    [TestCase("user-conversion-fault", false)]
    [TestCase("user-conversion-fault", true)]
    [TestCase("field-dependency-fault", false)]
    [TestCase("field-dependency-fault", true)]
    [TestCase("module-setter-fault", false)]
    [TestCase("module-setter-fault", true)]
    public async Task AuthoredInitializationFaultCannotBecomeCompleteExceptionProof(string scenario, bool published)
    {
        var artifact = CreateAndRun(scenario);
        var preparation = Prepare(artifact);
        if (published)
        { Assert.That(await Publish(artifact), Is.EqualTo(WorkerClaimOutcome.Unknown)); }
        else
        {
            var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
            await TestContext.Out.WriteLineAsync($"{scenario}: entryComplete={preparation.Total?.EffectsCompleteAtEntry}; native={result.Outcome?.GetType().Name}/{result.Reason}");
            Assert.That(result.Outcome, Is.Not.TypeOf<ProvenOutcome>(), result.Reason.ToString());
        }
    }

    [TestCase("own-field-write", WorkerClaimOutcome.Proven)]
    [TestCase("safe-field-dependency", WorkerClaimOutcome.Proven)]
    [TestCase("auto-property-constant", WorkerClaimOutcome.Proven)]
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
        const string target = "[DoesNotThrow] public static int Target() { return 7; }";
        var declaration = scenario switch
        {
            "user-conversion-fault" => "public sealed class Wrapped { public static implicit operator Wrapped(int value) { throw new System.InvalidOperationException(\"conversion\"); } } public static class Subject { public static readonly Wrapped State = 1; static Subject() { } " + target + " }",
            "field-dependency-fault" => "public static class Other { public static int State; static Other() { throw new System.InvalidOperationException(\"dependency\"); } } public static class Subject { static Subject() { Other.State = 1; } " + target + " }",
            "module-setter-fault" => "public static class Boot { [System.Runtime.CompilerServices.ModuleInitializer] public static void Init() { Subject.State = 1; } } public static class Subject { public static int State { set { throw new System.InvalidOperationException(\"setter\"); } } " + target + " }",
            "own-field-write" => "public static class Subject { public static int State; static Subject() { State = 1; } " + target + " }",
            "safe-field-dependency" => "public static class Other { public static int State; static Other() { State = 2; } } public static class Subject { static Subject() { Other.State = 1; } " + target + " }",
            "auto-property-constant" => "public static class Subject { public static int State { get; set; } = 1; static Subject() { } " + target + " }",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        return "using SharpProof.Attributes; " + declaration;
    }
    private static CompilerManifestArtifact CreateAndRun(string scenario)
    {
        var compilation = TestCompilation.Create("Wave8InitializationValues", ("Subject.cs", Source(scenario)));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Debug));
        Assert.That(compilation.Options.NullableContextOptions, Is.EqualTo(NullableContextOptions.Enable));
        Assert.That(((CSharpParseOptions)compilation.SyntaxTrees.Single().Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("Wave8InitializationValues", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var type = assembly.GetType("Subject")!;
            object? Invoke()
            {
                if (scenario is "constructor-fault" or "base-constructor-fault")
                { return type.GetConstructor(Type.EmptyTypes)!.Invoke(null); }
                var receiver = scenario == "instance-safe-initializer" ? Activator.CreateInstance(type) : null;
                return type.GetMethod("Target")!.Invoke(receiver, null);
            }
            if (scenario is "user-conversion-fault" or "field-dependency-fault" or "module-setter-fault")
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
