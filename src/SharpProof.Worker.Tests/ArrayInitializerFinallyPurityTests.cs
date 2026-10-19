using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Compositional LongFuzz seed 1077: a nonconstant array initializer precedes
// a finally write on both the normal return and the index-throwing path.
[TestFixture]
[NonParallelizable]
public sealed class ArrayInitializerFinallyPurityTests
{
    private const string DynamicArray = "new[] { y, -2, 3, 5 }[y & 7]";

    [Test]
    public async Task Seed1077FinallyWriteRefutesPurityAfterDynamicArrayInitializer()
    {
        var source = Source(DynamicArray, "Mark(y);");
        var normal = RunClr(source, 0);
        var throwing = RunClr(source, 7);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(normal, Is.EqualTo(new Observation(0, null, 0, 1)));
            Assert.That(throwing, Is.EqualTo(new Observation(null,
                typeof(IndexOutOfRangeException).FullName, 7, 1)));
        }

        var claim = await Verify(source);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted),
            "seed 1077 must account for the finally write; actual reason: " + claim.Reason);
    }

    [TestCase("new[] { 0, -2, 3, 5 }[y & 7]", "Mark(y);", WorkerClaimOutcome.Refuted)]
    [TestCase("new[] { 0, -2, 3, 5 }[y & 7]", "_ = y;", WorkerClaimOutcome.Proven)]
    public async Task ConstantInitializerControlsKeepPurityVerdict(string expression, string cleanup,
        WorkerClaimOutcome expected)
    {
        System.ArgumentNullException.ThrowIfNull(cleanup);
        System.ArgumentNullException.ThrowIfNull(expression);
        var source = Source(expression, cleanup);
        var observation = RunClr(source, 0);
        Assert.That(observation.Writes, Is.EqualTo(cleanup == "Mark(y);" ? 1 : 0));
        var claim = await Verify(source);
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    [Test]
    public async Task DynamicInitializerWithLocalOnlyCleanupIsPure()
    {
        var source = Source(DynamicArray, "_ = y;");
        Assert.That(RunClr(source, 0), Is.EqualTo(new Observation(0, null, 0, 0)));
        var claim = await Verify(source);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task DynamicInitializerFreezesLocalBeforeLaterAssignment()
    {
        var source = Source("a[0]", "_ = y;", "",
            "Contract.Ensures(Contract.Result<int>() == Contract.Old(x)); " +
            "int y = x; int[] a = new[] { y, 2 }; y = 42;");
        Assert.That(RunClr(source, 7), Is.EqualTo(new Observation(7, null, 0, 0)));
        var claim = await Verify(source);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task DynamicInitializerDoesNotProveFalseResultAfterLocalAssignment()
    {
        var source = Source("a[0]", "_ = y;", "",
            "Contract.Ensures(Contract.Result<int>() == unchecked(Contract.Old(x) + 1)); " +
            "int y = x; int[] a = new[] { y, 2 }; y = 42;");
        Assert.That(RunClr(source, 7), Is.EqualTo(new Observation(7, null, 0, 0)));
        var claim = await Verify(source);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    [Test]
    public async Task SideEffectingInitializerRemainsUnsupported()
    {
        var source = Source("new[] { y++, -2, 3, 5 }[y & 7]", "_ = y;");
        Assert.That(RunClr(source, 0), Is.EqualTo(new Observation(-2, null, 0, 0)));
        var claim = await Verify(source);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    [Test]
    public async Task ThrowingInitializerRemainsUnsupported()
    {
        var source = Source("new[] { 10 / y, -2, 3, 5 }[y & 7]", "_ = y;", "[DoesNotThrow]");
        Assert.That(RunClr(source, 0).Exception, Is.EqualTo(typeof(DivideByZeroException).FullName));
        var claim = await Verify(source);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
    }

    private sealed record Observation(int? Value, string? Exception, int State, int Writes);

    private static string Source(string expression, string cleanup, string claim = "[EnforcePure]",
        string prefix = "int y = x;")
    {
        System.ArgumentNullException.ThrowIfNull(claim);
        System.ArgumentNullException.ThrowIfNull(cleanup);
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(prefix);
        return "using System; using SharpProof.Attributes; public static class C { " +
            "public static int State; public static int Writes; " +
            "private static void Mark(int value) { State = value; Writes++; } " +
            "private static int Helper(int value) => checked(value + 1); " +
            "private sealed class D : IDisposable { private readonly int _value; " +
            "public D(int value) { _value = value; } public void Dispose() => Mark(_value); } " +
            claim + " public static int Target(int x) {  " + prefix + " try { return " +
            expression + "; } finally { " + cleanup + " } } }";
    }

    private static CSharpCompilation Compile(string source, bool contractsEnabled)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        ImmutableArray<string> symbols = contractsEnabled ? [Contract.ConditionalSymbol] : [];
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: symbols),
            "/project/Subject.cs");
        return CSharpCompilation.Create("ArrayFinallyProbe_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static Observation RunClr(string source, int input)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source, contractsEnabled: false);
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var image = new MemoryStream();
        var emit = compilation.Emit(image);
        Assert.That(emit.Success, Is.True, string.Join(" | ", emit.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("array-finally-oracle", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("C")!;
            var target = type.GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            int? value = null;
            string? exceptionName = null;
            try
            {
                value = (int)target.Invoke(null, [input])!;
            }
            catch (TargetInvocationException exception)
            {
                exceptionName = exception.InnerException!.GetType().FullName;
            }
            return new Observation(value, exceptionName,
                (int)type.GetField("State")!.GetValue(null)!,
                (int)type.GetField("Writes")!.GetValue(null)!);
        }
        finally
        {
            context.Unload();
        }
    }

    private static async Task<WorkerClaimResult> Verify(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile(source, contractsEnabled: true);
        Assert.That(compilation.GetDiagnostics().Where(d => d is null ? throw new System.ArgumentNullException(nameof(d)) : (d.Severity == DiagnosticSeverity.Error)), Is.Empty);
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
        return response.ClaimResults.Single(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (artifact.Manifest.Claims.Any(item =>
            item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.ClaimId == result.ClaimId && item.CallableId.Contains("Target", StringComparison.Ordinal)))));
    }
}
