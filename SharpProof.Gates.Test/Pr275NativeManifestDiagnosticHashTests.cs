using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Host;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class Pr275NativeManifestDiagnosticHashTests
{
    [Test]
    public async Task ActualCanceledWorkerDiagnosticRetainsItsOwnedCompilerManifestHash()
    {
        const string source = "using SharpProof.Attributes; public static class DiagnosticHashSubject { [EnforcePure] public static int Identity(int value) => value; }";
        var compilation = AnalyzerGateHost.CreateCompilation(source, "Pr275NativeManifestDiagnosticHash", includeExternalEffectsFixture: false);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        using var emitted = new MemoryStream();
        Assert.That(compilation.Emit(emitted).Success, Is.True);
        using var temporary = new TempDirectory("SharpProof-pr275-native-manifest-hash-");
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All).Build();
        Assert.That(discovery.Targets, Has.Count.EqualTo(1));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, temporary.FullName, "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        var hash = ComputeSha256(bytes);
        var path = Path.Combine(temporary.FullName, "compiler-manifest.json");
        await File.WriteAllBytesAsync(path, bytes);
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference { Path = path, Sha256 = hash },
            Cache = new WorkerCacheOptions { Enabled = false, Directory = Path.Combine(temporary.FullName, "cache") },
            Budgets = new WorkerBudgets()
        };
        Assert.That(WorkerProtocolJson.Validate(request).IsValid, Is.True);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var worker = SharpProofWorker.Create(request.Budgets);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var response = await worker.VerifyAsync(request, cancellation.Token);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Canceled));
        Assert.That(response.Errors.Select(static error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code)), Does.Contain("worker.canceled"));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var failure = Assert.Throws<InvalidOperationException>((Action)(() =>
            NativeCorpusVerifier.EnsureComplete(response, request.Budgets, hash)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failure!.Data["SharpProof.Corpus.CompilerManifestSha256"], Is.EqualTo(hash));
            Assert.That(failure.Data["SharpProof.Corpus.WorkerFailure"],
                Is.EqualTo(NativeCorpusVerifier.CaptureWorkerDiagnostics(response, request.Budgets)));
            Assert.That(request.Budgets.MethodWallTimeMilliseconds, Is.EqualTo(10000));
            Assert.That(request.Budgets.ProjectWallTimeMilliseconds, Is.EqualTo(300000));
            Assert.That(ComputeSha256(await File.ReadAllBytesAsync(path)), Is.EqualTo(hash));
        }
    }

    private static string ComputeSha256(byte[] bytes)
    {
        System.ArgumentNullException.ThrowIfNull(bytes, nameof(bytes));
        return string.Concat(SHA256.HashData(bytes).Select(static value => value.ToString("x2", CultureInfo.InvariantCulture)));
    }
}
