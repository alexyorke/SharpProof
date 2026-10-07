using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave8ExceptionInitializationBoundaryTests
{
    [TestCase("module-fault", false)]
    [TestCase("module-fault", true)]
    [TestCase("constructor-fault", false)]
    [TestCase("constructor-fault", true)]
    [TestCase("field-initializer-fault", false)]
    [TestCase("field-initializer-fault", true)]
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

    [TestCase("module-empty", WorkerClaimOutcome.Proven)]
    [TestCase("module-constant-write", WorkerClaimOutcome.Proven)]
    [TestCase("mutable-constant", WorkerClaimOutcome.Proven)]
    [TestCase("callee-mutable-constant", WorkerClaimOutcome.Proven)]
    [TestCase("readonly-object", WorkerClaimOutcome.Proven)]
    [TestCase("callee-empty-explicit", WorkerClaimOutcome.Unknown)]
    [TestCase("base-constructor-fault", WorkerClaimOutcome.Unknown)]
    [TestCase("static-derived-base-fault", WorkerClaimOutcome.Proven)]
    [TestCase("instance-safe-initializer", WorkerClaimOutcome.Proven)]
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
            "module-fault" => "public static class Boot { [System.Runtime.CompilerServices.ModuleInitializer] public static void Init() { throw new System.InvalidOperationException(\"module\"); } } public static class Subject { " + target + " }",
            "constructor-fault" => "public class Subject { static Subject() { throw new System.InvalidOperationException(\"constructor entry\"); } [DoesNotThrow] public Subject() { } }",
            "field-initializer-fault" => "public static class Subject { public static readonly int State = Fail(); static int Fail() { throw new System.InvalidOperationException(\"field initializer\"); } [DoesNotThrow] public static int Target() { return State; } }",
            "module-empty" => "public static class Boot { [System.Runtime.CompilerServices.ModuleInitializer] public static void Init() { } } public static class Subject { " + target + " }",
            "module-constant-write" => "public static class Boot { [System.Runtime.CompilerServices.ModuleInitializer] public static void Init() { Subject.State = 1; } } public static class Subject { public static int State; " + target + " }",
            "mutable-constant" => "public static class Subject { public static int State = 1; " + target + " }",
            "callee-mutable-constant" => "public static class Other { public static int State = 1; public static int Helper() { return 7; } } public static class Subject { [DoesNotThrow] public static int Target() { return Other.Helper(); } }",
            "readonly-object" => "public static class Subject { public static readonly object State = new object(); " + target + " }",
            "callee-empty-explicit" => "public static class Other { static Other() { } public static int Helper() { return 7; } } public static class Subject { [DoesNotThrow] public static int Target() { return Other.Helper(); } }",
            "base-constructor-fault" => "public class Base { static Base() { throw new System.InvalidOperationException(\"base\"); } public Base() { } } public class Subject : Base { [DoesNotThrow] public Subject() { } }",
            "static-derived-base-fault" => "public class Base { static Base() { throw new System.InvalidOperationException(\"unused base\"); } } public class Subject : Base { " + target + " }",
            "instance-safe-initializer" => "public class Subject { public static int State; static Subject() { State = 1; } [DoesNotThrow] public int Target() { return 7; } }",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        return "using SharpProof.Attributes; " + declaration;
    }

    private static CompilerManifestArtifact CreateAndRun(string scenario)
    {
        var compilation = TestCompilation.Create("Wave8InitializationBoundary", ("Subject.cs", Source(scenario)));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Debug));
        Assert.That(compilation.Options.NullableContextOptions, Is.EqualTo(NullableContextOptions.Enable));
        Assert.That(((CSharpParseOptions)compilation.SyntaxTrees.Single().Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("Wave8InitializationBoundary", isCollectible: true);
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
            if (scenario is "module-fault" or "constructor-fault" or "field-initializer-fault" or "base-constructor-fault")
            {
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => Invoke()));
                Assert.That(error!.InnerException, Is.TypeOf<TypeInitializationException>());
                Assert.That(error.InnerException!.InnerException, Is.TypeOf<InvalidOperationException>());
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
