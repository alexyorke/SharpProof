using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// `new Derived(args)` for an exception constructor that only forwards its
// parameters to a core base constructor allocates a Derived and runs that
// base constructor; any other source exception stays an unknown call.
[TestFixture]
[NonParallelizable]
internal sealed class ForwardingExceptionConstructorTests
{
    private const string Forwarding =
        "public sealed class Failure : System.Exception { public Failure(string message) : base(message) { } " +
        "public Failure(string message, System.Exception inner) : base(message, inner) { } } ";

    [TestCase("[EnforcePure]", "if (x < 0) { throw new Failure(\"negative\"); } return x;", WorkerClaimOutcome.Proven)]
    // A caught Failure is a Failure at run time.
    [TestCase("[DoesNotThrow]", "try { if (x < 0) { throw new Failure(\"negative\"); } return x; } catch (Failure) { return 0; }", WorkerClaimOutcome.Proven)]
    // An unrelated handler does not catch it; the replayed witness escapes.
    [TestCase("[DoesNotThrow]", "try { if (x < 0) { throw new Failure(\"negative\"); } return x; } catch (System.ArgumentException) { return 0; }", WorkerClaimOutcome.Refuted)]
    public async Task AForwardingConstructorRunsItsBaseConstructor(string attribute, string body, WorkerClaimOutcome expected)
    {
        var claim = await Verify(Forwarding, attribute, body);
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    // The base constructor's own effects carry over: ArgumentNullException(paramName)
    // looks its message up in runtime resources, which enters a lock.
    [Test]
    public async Task TheBaseConstructorsResourceLookupCarriesOver()
    {
        var claim = await Verify("public sealed class Failure : System.ArgumentNullException { public Failure(string paramName) : base(paramName) { } } ",
            "[EnforcePure]", "if (x < 0) { throw new Failure(\"x\"); } return x;");
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // A member that could run during construction, a field, a reordered or
    // changed argument and a body each keep the construction unknown.
    [TestCase("public sealed class Failure : System.Exception { public Failure(string message) : base(message) { } public override string Message => \"m\"; } ")]
    [TestCase("public sealed class Failure : System.Exception { private int _code; public Failure(string message) : base(message) { _code = 1; } } ")]
    [TestCase("public sealed class Failure : System.Exception { public Failure(string message) : base(message + \"!\") { } } ")]
    [TestCase("public sealed class Failure : System.Exception { public Failure(string message) : base(message) { System.Console.WriteLine(); } } ")]
    public async Task OtherSourceExceptionsStayUnknown(string type)
    {
        var claim = await Verify(type, "[EnforcePure]", "if (x < 0) { throw new Failure(\"negative\"); } return x;");
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> Verify(string types, string attribute, string body)
    {
        var source = "using SharpProof.Attributes; " + types + "public static class T { " + attribute +
            " public static int Target(int x) { " + body + " } }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: ImmutableArray.Create(Contract.ConditionalSymbol)),
            "/project/Subject.cs");
        var compilation = CSharpCompilation.Create("Forwarding_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact);
        artifact = project.Artifact;
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        return response.ClaimResults.Single(result => artifact.Manifest.Claims.Any(item =>
            item is null ? throw new System.ArgumentNullException(nameof(item)) :
                (item.ClaimId == result.ClaimId && item.CallableId.Contains("T.Target", StringComparison.Ordinal) &&
                    item.EffectContractKind != WorkerEffectContractKind.Unspecified)));
    }
}
