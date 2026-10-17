using System.Globalization;
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

[TestFixture]
public sealed class OrdinaryNativeCancellationWorkflowTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ActualWorkerCancellationKeepsTheCorpusFailureDiagnostic(bool cancel)
    {
        const string source = "using SharpProof.Attributes; public static class OrdinaryCancellation { [EnforcePure] public static int Identity(int value) => value; }";
        var compilation = AnalyzerGateHost.CreateCompilation(source, "OrdinaryNativeCancellation", includeExternalEffectsFixture: false);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var temporary = new TempDirectory("SharpProof-ordinary-native-cancellation-");
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All).Build();
        Assert.That(discovery.Targets, Has.Count.EqualTo(1));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, temporary.FullName, "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken));
        var path = Path.Combine(temporary.FullName, "compiler-manifest.json");
        await File.WriteAllBytesAsync(path, bytes);
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference { Path = path, Sha256 = ComputeSha256(bytes) },
            Cache = new WorkerCacheOptions { Enabled = false, Directory = Path.Combine(temporary.FullName, "cache") },
            Budgets = new WorkerBudgets()
        };
        Assert.That(WorkerProtocolJson.Validate(request).IsValid, Is.True);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var worker = SharpProofWorker.Create(request.Budgets);
        using var cancellation = new CancellationTokenSource();
        if (cancel)
        {
            await cancellation.CancelAsync();
        }
        var response = await worker.VerifyAsync(request, cancellation.Token);
        if (!cancel)
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.DoesNotThrow((Action)(() => NativeCorpusVerifier.EnsureComplete(response, request.Budgets)));
            var verdicts = await NativeCorpusVerifier.VerifyAsync(compilation, TestContext.CurrentContext.CancellationToken);
            Assert.That(verdicts.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            return;
        }

        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Canceled));
        Assert.That(response.Errors.Select(static error => error.Code), Does.Contain("worker.canceled"));
        var failure = Assert.Throws<InvalidOperationException>((Action)(() => NativeCorpusVerifier.EnsureComplete(response, request.Budgets)));
        Assert.That(failure!.Message, Is.EqualTo("The worker did not complete the corpus artifact (" + response.RunStatus + ", " +
            response.FailureReason + "): " + string.Join("; ", response.Errors.Select(static error => error.Code + ": " + error.Message))));
        var captured = (string)failure.Data["SharpProof.Corpus.WorkerFailure"]!;
        Assert.That(captured, Is.EqualTo(NativeCorpusVerifier.CaptureWorkerDiagnostics(response, request.Budgets)));
        using var diagnostics = JsonDocument.Parse(captured);
        Assert.That(diagnostics.RootElement.GetProperty("RunStatus").GetString(), Is.EqualTo(response.RunStatus.ToString()));
        Assert.That(diagnostics.RootElement.GetProperty("Budgets").GetProperty("MethodWallTimeMilliseconds").GetInt32(), Is.EqualTo(10000));
        await TestContext.Out.WriteLineAsync("actual-native-cancellation source-sha256=" + ComputeSha256(Encoding.UTF8.GetBytes(source)));
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return string.Concat(SHA256.HashData(bytes).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
