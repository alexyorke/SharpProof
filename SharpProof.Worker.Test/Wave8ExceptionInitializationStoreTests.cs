using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Wave8ExceptionInitializationStoreTests
{
    [TestCase("cctor-boxing", WorkerClaimOutcome.Proven)]
    [TestCase("module-boxing", WorkerClaimOutcome.Proven)]
    [TestCase("cctor-reference", WorkerClaimOutcome.Proven)]
    [TestCase("module-reference", WorkerClaimOutcome.Proven)]
    [TestCase("cctor-numeric", WorkerClaimOutcome.Proven)]
    [TestCase("cctor-user-conversion", WorkerClaimOutcome.Unknown)]
    [TestCase("module-empty-return", WorkerClaimOutcome.Proven)]
    public async Task BodyStoresRespectTheirActualConversionAndRuntimeInitialization(string scenario, WorkerClaimOutcome expected)
    {
        var compilation = TestCompilation.Create("Wave8InitializationStores", ("Subject.cs", Source(scenario)));
        Assert.That(compilation.Options.OptimizationLevel, Is.EqualTo(OptimizationLevel.Debug));
        Assert.That(compilation.Options.NullableContextOptions, Is.EqualTo(NullableContextOptions.Enable));
        Assert.That(((CSharpParseOptions)compilation.SyntaxTrees.Single().Options).LanguageVersion, Is.EqualTo(LanguageVersion.CSharp12));
        CheckBoundStore(compilation, scenario);
        RunCompiledInvocation(compilation, scenario);
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var native = await NativeExceptionEffectVerifier.VerifyAsync(preparations.Single(item => item.EffectClaims.Length != 0), new WorkerBudgets());
        await TestContext.Out.WriteLineAsync($"{scenario}: native={native.Outcome?.GetType().Name}/{native.Reason}");
        if (expected == WorkerClaimOutcome.Proven)
        { Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>()); }
        else
        { Assert.That(native.Outcome, Is.Null); }
        using var project = new ShadowTestProject(artifact);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var result = response.ClaimResults.Single();
        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.EffectCertainty, Is.EqualTo(expected == WorkerClaimOutcome.Proven ?
            WorkerEffectEvidenceCertainty.CompleteMayEffectSummary : WorkerEffectEvidenceCertainty.Unavailable));
    }

    private static string Source(string scenario)
    {
        const string target = "[DoesNotThrow] public static int Target() { return 7; }";
        var declaration = scenario switch
        {
            "cctor-boxing" => "public static class Subject { public static object? State; static Subject() { State = 7; } " + target + " }",
            "module-boxing" => "public static class Boot { [System.Runtime.CompilerServices.ModuleInitializer] public static void Init() { Subject.State = 7; } } public static class Subject { public static object? State; " + target + " }",
            "cctor-reference" => "public static class Subject { public static object? State; static Subject() { State = \"safe\"; } " + target + " }",
            "module-reference" => "public static class Boot { [System.Runtime.CompilerServices.ModuleInitializer] public static void Init() { Subject.State = \"safe\"; } } public static class Subject { public static object? State; " + target + " }",
            "cctor-numeric" => "public static class Subject { public static long State; static Subject() { State = 7; } " + target + " }",
            "cctor-user-conversion" => "public sealed class Wrapped { public static implicit operator Wrapped(int value) { throw new System.InvalidOperationException(\"body conversion\"); } } public static class Subject { public static Wrapped? State; static Subject() { State = 7; } " + target + " }",
            "module-empty-return" => "public static class Boot { [System.Runtime.CompilerServices.ModuleInitializer] public static void Init() { ; return; } } public static class Subject { " + target + " }",
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        return "using SharpProof.Attributes; " + declaration;
    }

    private static void CheckBoundStore(CSharpCompilation compilation, string scenario)
    {
        if (scenario == "module-empty-return")
        { return; }
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var store = compilation.GetSemanticModel(tree).GetOperation(syntax) as ISimpleAssignmentOperation;
        var conversion = store?.Value as IConversionOperation;
        Assert.That(conversion, Is.Not.Null, "The fixture must exercise a bound conversion in a body store.");
        if (scenario == "cctor-user-conversion")
        { Assert.That(conversion!.OperatorMethod, Is.Not.Null); }
        else
        { Assert.That(conversion!.OperatorMethod, Is.Null); }
        if (scenario.EndsWith("reference", StringComparison.Ordinal))
        { Assert.That(conversion.Conversion.IsReference, Is.True); }
        if (scenario.EndsWith("boxing", StringComparison.Ordinal))
        {
            Assert.That(conversion.Operand.Type?.SpecialType, Is.EqualTo(SpecialType.System_Int32));
            Assert.That(conversion.Type?.SpecialType, Is.EqualTo(SpecialType.System_Object));
        }
        if (scenario == "cctor-numeric")
        { Assert.That(conversion.ConstantValue.Value, Is.EqualTo(7L)); }
    }

    private static void RunCompiledInvocation(CSharpCompilation compilation, string scenario)
    {
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join("\n", emission.Diagnostics));
        image.Position = 0;
        var context = new System.Runtime.Loader.AssemblyLoadContext("Wave8InitializationStores", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var type = assembly.GetType("Subject")!;
            object? Invoke()
            { return type.GetMethod("Target")!.Invoke(null, null); }
            if (scenario == "cctor-user-conversion")
            {
                var error = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => Invoke()));
                Exception? fault = error!.InnerException;
                Assert.That(fault, Is.TypeOf<TypeInitializationException>());
                while (fault is TypeInitializationException initialization)
                { fault = initialization.InnerException; }
                Assert.That(fault, Is.TypeOf<InvalidOperationException>());
                Assert.That(fault!.Message, Is.EqualTo("body conversion"));
            }
            else
            {
                Assert.That(Invoke(), Is.EqualTo(7));
                if (scenario != "module-empty-return")
                {
                    var value = type.GetField("State")!.GetValue(null);
                    if (scenario.EndsWith("reference", StringComparison.Ordinal))
                    { Assert.That(value, Is.EqualTo("safe")); }
                    else if (scenario == "cctor-numeric")
                    { Assert.That(value, Is.TypeOf<long>().And.EqualTo(7L)); }
                    else
                    { Assert.That(value, Is.TypeOf<int>().And.EqualTo(7)); }
                }
            }
        }
        finally { context.Unload(); }
    }
}
