using System.Globalization;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Host;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[SetUpFixture]
public sealed class OrdinaryNativeDiagnosticStateSetup
{
    [OneTimeSetUp]
    public static void InstallNativeLibrary()
    {
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
    }
}

[TestFixture]
public sealed class OrdinaryNativeDiagnosticStateWorkflowTests
{
    private delegate void RefMutation(ref int value);

    [TestCase(0)]
    [TestCase(1, TestName = "ActualNativeDiagnosticStateMixed")]
    [TestCase(10)]
    public async Task ActualNativeStateKeepsProofsUnsupportedRefBodiesAndBoundedDiagnostics(int mutationCount)
    {
        var source = new StringBuilder("using SharpProof.Attributes; public static class DiagnosticState {");
        source.Append("[EnforcePure] public static int Scalar(int value) => value;");
        for (var index = 0; index < mutationCount; index++)
        {
            source.Append("[EnforcePure] public static void Mutating")
                .Append(index.ToString("D2", CultureInfo.InvariantCulture))
                .Append("(ref int value) { value++; }");
        }
        source.Append('}');
        var compilation = AnalyzerGateHost.CreateCompilation(source.ToString(),
            "OrdinaryNativeDiagnosticState", includeExternalEffectsFixture: false);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("OrdinaryNativeDiagnosticState", isCollectible: true);
        try
        {
            var type = context.LoadFromStream(image).GetType("DiagnosticState", throwOnError: true)!;
            var scalar = type.GetMethod("Scalar")!.CreateDelegate<Func<int, int>>();
            Assert.That(scalar(-7), Is.EqualTo(-7));
            for (var index = 0; index < mutationCount; index++)
            {
                var mutate = type.GetMethod("Mutating" + index.ToString("D2", CultureInfo.InvariantCulture))!
                    .CreateDelegate<RefMutation>();
                var value = 5;
                mutate(ref value);
                Assert.That(value, Is.EqualTo(6));
            }
        }
        finally
        {
            context.Unload();
        }

        using var temporary = new TempDirectory("SharpProof-native-state-");
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All).Build();
        Assert.That(discovery.Targets, Has.Count.EqualTo(mutationCount + 1));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, temporary.FullName, "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: CancellationToken.None));
        var path = Path.Combine(temporary.FullName, "compiler-manifest.json");
        await File.WriteAllBytesAsync(path, bytes);
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference { Path = path, Sha256 = string.Concat(SHA256.HashData(bytes).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture))) },
            Cache = new WorkerCacheOptions { Enabled = false, Directory = Path.Combine(temporary.FullName, "cache") },
            Budgets = new WorkerBudgets()
        };
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, CancellationToken.None);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.CallableResults, Has.Length.EqualTo(mutationCount + 1));
        Assert.That(response.ClaimResults, Has.Length.EqualTo(mutationCount + 1));
        Assert.That(response.ClaimResults.Count(static claim => claim.Outcome == WorkerClaimOutcome.Proven), Is.EqualTo(1));
        Assert.That(response.ClaimResults.Count(static claim => claim.Outcome == WorkerClaimOutcome.Unknown), Is.EqualTo(mutationCount));
        Assert.That(response.ClaimResults.Count(static claim => claim.Outcome == WorkerClaimOutcome.Refuted), Is.Zero);

        using var diagnostics = JsonDocument.Parse(NativeCorpusVerifier.CaptureWorkerDiagnostics(response, request.Budgets));
        var root = diagnostics.RootElement;
        Assert.That(root.GetProperty("RunStatus").GetString(), Is.EqualTo("Complete"));
        Assert.That(root.GetProperty("FailureReason").GetString(), Is.EqualTo("None"));
        Assert.That(root.GetProperty("CallableCount").GetInt32(), Is.EqualTo(mutationCount + 1));
        Assert.That(root.GetProperty("IncompleteCallableCount").GetInt32(), Is.EqualTo(mutationCount));
        Assert.That(root.GetProperty("ClaimCount").GetInt32(), Is.EqualTo(mutationCount + 1));
        Assert.That(root.GetProperty("ClaimsWithReasonCount").GetInt32(), Is.EqualTo(mutationCount));
        var incomplete = root.GetProperty("IncompleteCallables").EnumerateArray().ToArray();
        var claims = root.GetProperty("ClaimsWithReasons").EnumerateArray().ToArray();
        Assert.That(incomplete, Has.Length.EqualTo(Math.Min(8, mutationCount)));
        Assert.That(claims, Has.Length.EqualTo(Math.Min(8, mutationCount)));
        Assert.That(incomplete.Select(static row => row.GetProperty("CallableId").GetString()!), Is.Ordered.Using<string>(StringComparer.Ordinal));
        Assert.That(claims.Select(static row => row.GetProperty("ClaimId").GetString()!), Is.Ordered.Using<string>(StringComparer.Ordinal));
        Assert.That(incomplete.Select(static row => row.GetProperty("Coverage").GetString()), Is.All.EqualTo("Incomplete"));
        Assert.That(claims.Select(static row => row.GetProperty("Outcome").GetString()), Is.All.EqualTo("Unknown"));
        Assert.That(root.GetProperty("Budgets").GetProperty("MethodWallTimeMilliseconds").GetInt32(), Is.EqualTo(10000));
    }
}
