using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class CompilerCaptureRequestWorkflowTests
{
    private static readonly ImmutableArray<string> SelectedPackIds = ["dotnet.scalar"];
    private const string RequestKey =
        "build_property._SharpProofCompilerCaptureRequestPath";

    [Test]
    public async Task CancellationWhileReadingAdditionalInputPublishesNoCompletionReceipt()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            await File.WriteAllTextAsync(request, "00112233445566778899aabbccddeeff");
            using var cancellation = new CancellationTokenSource();
            Assert.CatchAsync<OperationCanceledException>((Func<Task>)(() => AnalyzeAsync(path, request,
                additionalFiles: [new CancellingAdditionalInput(cancellation)],
                cancellationToken: cancellation.Token)));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(cancellation.IsCancellationRequested, Is.True);
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(request + ".completed"), Is.False);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task AggregateInputReaderFailureRemainsAnAnalyzerFailureWithoutPublication()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            await File.WriteAllTextAsync(request, "00112233445566778899aabbccddeeff");
            var diagnostics = await AnalyzeAsync(path, request,
                additionalFiles: [new FailingAdditionalInput()]);
            using (Assert.EnterMultipleScope())
            {
                AnalyzerTestHost.AssertIds(diagnostics, "AD0001");
                AnalyzerTestHost.AssertMessageContains(diagnostics.Single(), "input reader failed");
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(request + ".completed"), Is.False);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestCase("not-a-guid")]
    [TestCase("00112233445566778899AABBCCDDEEFF")]
    [TestCase("00112233-4455-6677-8899-aabbccddeeff")]
    public async Task InvalidInvocationReportsFailureWithoutPublishing(string invocation)
    {
        System.ArgumentNullException.ThrowIfNull(invocation);
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            await File.WriteAllTextAsync(request, invocation);
            var diagnostics = await AnalyzeAsync(path, request);

            using (Assert.EnterMultipleScope())
            {
                AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
                AnalyzerTestHost.AssertMessageContains(diagnostics.Single(),
                    "compiler capture invocation is invalid");
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(request + ".completed"), Is.False);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task CanonicalInvocationPublishesManifestAndMatchingCompletionReceipt()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            const string invocation = "00112233445566778899aabbccddeeff";
            await File.WriteAllTextAsync(request, " \n" + invocation + "\n ");

            var diagnostics = await AnalyzeAsync(path, request);
            Assert.That(diagnostics, Is.Empty);
            var artifact = CompilerManifestArtifactJson.Deserialize(
                await File.ReadAllTextAsync(path), cancellationToken: TestContext.CurrentContext.CancellationToken);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.Compilation.ProjectDirectory,
                    Is.EqualTo(Path.GetDirectoryName(path)));
                Assert.That(await File.ReadAllTextAsync(request + ".completed"),
                    Is.EqualTo(invocation));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task SelectedSpecificationPackIsPublishedInTheManifest()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            await File.WriteAllTextAsync(request, "00112233445566778899aabbccddeeff");
            var diagnostics = await AnalyzeAsync(path, request, " dotnet.scalar ");
            Assert.That(diagnostics, Is.Empty);
            var artifact = CompilerManifestArtifactJson.Deserialize(
                await File.ReadAllTextAsync(path), cancellationToken: TestContext.CurrentContext.CancellationToken);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.SpecificationPackIds, Is.EqualTo(SelectedPackIds));
                Assert.That(await File.ReadAllTextAsync(request + ".completed"),
                    Is.EqualTo("00112233445566778899aabbccddeeff"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task CapturedOuterParameterClaimIdentityUsesItsRoleAndNotItsName()
    {
        var directory = CreateDirectory();
        try
        {
            var identitiesBuilder = ImmutableArray.CreateBuilder<string>();
            foreach (var (name, captured) in ImmutableArray.Create(("left", "left"), ("renamed", "renamed"), ("left", "right")))
            {
                var path = Path.Combine(directory, "manifest-" + identitiesBuilder.Count + ".json");
                var request = path + ".request";
                await File.WriteAllTextAsync(request, "00112233445566778899aabbccddeeff");
                var diagnostics = await AnalyzeAsync(path, request, source:
                    $$"""
                    using SharpProof.Attributes;
                    internal static class Subject {
                        internal static int Outer(int {{name}}, int right) {
                            int Local(int input) {
                                Contract.Ensures({{captured}} >= 0);
                                return input;
                            }
                            return Local(0);
                        }
                    }
                    """);
                Assert.That(diagnostics, Is.Empty);
                var artifact = CompilerManifestArtifactJson.Deserialize(
                    await File.ReadAllTextAsync(path), cancellationToken: TestContext.CurrentContext.CancellationToken);
                Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
                identitiesBuilder.Add(artifact.Manifest.Claims.Single().ClaimId);
            }
            var identities = identitiesBuilder.ToImmutable();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(identities[1], Is.EqualTo(identities[0]));
                Assert.That(identities[2], Is.Not.EqualTo(identities[0]));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task GenericLocalContractsPublishDistinctSemanticClaimIdentities()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            await File.WriteAllTextAsync(request, "00112233445566778899aabbccddeeff");
            var diagnostics = await AnalyzeAsync(path, request, source:
                """
                using SharpProof.Attributes;
                internal static class Subject {
                    internal static int Outer<T>(int value) {
                        int ParentType<U>(int input) {
                            Contract.Ensures(typeof(T) != null);
                            return input;
                        }
                        int LocalType<U>(int input) {
                            Contract.Ensures(typeof(U) != null);
                            return input;
                        }
                        return ParentType<int>(value) + LocalType<int>(value);
                    }
                }
                """);
            Assert.That(diagnostics, Is.Empty);
            var artifact = CompilerManifestArtifactJson.Deserialize(
                await File.ReadAllTextAsync(path), cancellationToken: TestContext.CurrentContext.CancellationToken);
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(2));
                Assert.That(artifact.Manifest.Claims.Select(claim => claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.ClaimId)).Distinct().Count(),
                    Is.EqualTo(2));
                Assert.That(await File.ReadAllTextAsync(request + ".completed"),
                    Is.EqualTo("00112233445566778899aabbccddeeff"));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task DuplicateSpecificationPacksReportFailureWithoutCompletionReceipt()
    {
        var directory = CreateDirectory();
        try
        {
            var path = Path.Combine(directory, "manifest.json");
            var request = Path.Combine(directory, "request");
            await File.WriteAllTextAsync(request, "00112233445566778899aabbccddeeff");
            var diagnostics = await AnalyzeAsync(path, request, "dotnet.scalar; dotnet.scalar");
            using (Assert.EnterMultipleScope())
            {
                AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
                AnalyzerTestHost.AssertMessageContains(diagnostics.Single(),
                    "must not contain duplicate identifiers");
                Assert.That(File.Exists(path), Is.False);
                Assert.That(File.Exists(request + ".completed"), Is.False);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string path, string request, string? packs = null,
        string source = "internal static class Subject { }",
        ImmutableArray<AdditionalText> /* SPINT001: all values valid - default denotes absent optional inputs and is handled downstream */ additionalFiles = default)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(request);
        System.ArgumentNullException.ThrowIfNull(path);
        return AnalyzeAsync(path, request, TestContext.CurrentContext.CancellationToken, packs, source, additionalFiles);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string path, string request, CancellationToken cancellationToken,
        string? packs = null,
        string source = "internal static class Subject { }",
        ImmutableArray<AdditionalText> /* SPINT001: all values valid - default denotes absent optional inputs and is normalized by the analyzer host */ additionalFiles = default)
    {

        System.ArgumentNullException.ThrowIfNull(path);
        System.ArgumentNullException.ThrowIfNull(request);
        System.ArgumentNullException.ThrowIfNull(source);
        var options = ImmutableDictionary.CreateBuilder<string, string>();
        options["build_property.SharpProofProfile"] = "advisory";
        options["build_property.SharpProofFeatures"] = "all";
        options["build_property.SharpProofVerifyPolicy"] = "advisory";
        options["build_property.SharpProofAssumptionPolicy"] = "allow";
        options["build_property._SharpProofCompilerManifestPath"] = path;
        options["build_property._SharpProofCompilationTargetFramework"] = "net8.0";
        options["build_property._SharpProofProjectDirectory"] = Path.GetDirectoryName(path)!;
        options["build_property.SharpProofVerifyMaximumExpressionDepth"] = "64";
        options[RequestKey] = request;
        if (packs != null)
        {
            options["build_property.SharpProofSpecificationPacks"] = packs;
        }
        return AnalyzerTestHost.AnalyzeAsync(
            AnalyzerTestHost.CreateCompilation(source, []),
            options.ToImmutable(),
            additionalFiles: additionalFiles,
            analyzer: new FinalCompilationCollectorAnalyzer(),
            cancellationToken: cancellationToken);
    }

    private sealed class CancellingAdditionalInput(CancellationTokenSource cancellation) : AdditionalText,
        ICompilerAdditionalTextSnapshot
    {
        private readonly CancellationTokenSource _guarded_cancellation = cancellation ?? throw new System.ArgumentNullException(nameof(cancellation));
        public override string Path => "input.data";

        SourceText ICompilerAdditionalTextSnapshot.CapturedText => GetText(_guarded_cancellation.Token);

        public override SourceText GetText(CancellationToken cancellationToken)
        {
            _guarded_cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return SourceText.From(string.Empty);
        }
    }

    private sealed class FailingAdditionalInput : AdditionalText, ICompilerAdditionalTextSnapshot
    {
        public override string Path => "input.data";

        SourceText ICompilerAdditionalTextSnapshot.CapturedText => GetText(cancellationToken: TestContext.CurrentContext.CancellationToken);

        public override SourceText GetText(CancellationToken cancellationToken)
        {
            throw new AggregateException(new IOException("input reader failed"));
        }
    }

    private static string CreateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "SharpProofCaptureWorkflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
