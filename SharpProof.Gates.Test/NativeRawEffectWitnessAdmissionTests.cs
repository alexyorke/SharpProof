using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class NativeRawEffectWitnessAdmissionTests
{
    [TestCase("exception")]
    [TestCase("allocation")]
    [TestCase("purity")]
    [TestCase("capabilities")]
    [TestCase("summary")]
    public async Task RealEffectWitnessPassesAndRealEntryCounterexampleIsRejected(string mode)
    {
        var allocations = mode == "allocation";
        var purity = mode == "purity";
        var capabilities = mode == "capabilities";
        var summary = mode == "summary";
        var body = mode switch
        {
            "exception" => "return 10 / x;",
            "allocation" => "new object(); return x;",
            "purity" or "capabilities" => "object gate = new object(); lock (gate) { x++; } return x;",
            "summary" => "Counter = 1; return x;",
            _ => throw new AssertionException("Unknown declared test mode.")
        };
        var source = "public static class C { public static int Counter; public static int Target(int x) { " + body + " } }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var declaration = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var span = declaration.GetLocation().GetLineSpan();
        var file = new OpenSourceCorpusFile("fixture", "raw-effect-witness.cs",
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(source), source);
        var method = new OpenSourceCorpusMethod("sample", file.SourceId, file.Path,
            span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1,
            OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(declaration)),
            "Target", "effects", CorpusVerdict.Unknown, CorpusSupport.Supported);
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(new(2, [], [file], [method]),
            CancellationToken.None, allocations, purity, capabilities, summary);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.Effects).Build();
        var target = discovery.Targets.Values.Single();
        Assert.That(target.Method.ContainingAssembly, Is.SameAs(compilation.Assembly));
        var artifact = CompilerManifestArtifactProducer.Create(compilation, RepositoryLayout.FindRoot(), "net9.0",
            WorkerFeatureSet.Effects, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.Entry.CallableId, Is.EqualTo(target.Entry.CallableId));
        var claim = preparation.EffectClaims.Single();
        var budgets = new WorkerBudgets();
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var evidence = summary
            ? await NativeEffectSiteVerifier.VerifyEffectContractAsync(preparation, budgets, CancellationToken.None)
            : capabilities
            ? await NativeEffectSiteVerifier.VerifyCapabilitiesAsync(preparation, budgets, CancellationToken.None)
            : purity
            ? await NativeEffectSiteVerifier.VerifyPurityAsync(preparation, budgets, CancellationToken.None)
            : allocations
            ? await NativeEffectSiteVerifier.VerifyAsync(preparation, budgets, CancellationToken.None)
            : (await NativeExceptionEffectVerifier.VerifyClaimsAsync(preparation, budgets, CancellationToken.None))[claim.ClaimId];
        Assert.That(evidence.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(evidence.Reason, Is.EqualTo(WorkerClaimReason.None));
        if (summary)
        { Assert.That(evidence.WriteWitness, Is.Not.Null); }
        else if (capabilities || purity)
        { Assert.That(evidence.LockWitness, Is.Not.Null); }
        else if (allocations)
        { Assert.That(evidence.AllocationWitness, Is.Not.Null); }
        else
        { Assert.That(evidence.ExceptionWitness, Is.Not.Null); }
        Action admitEffect = () => NativeEffectOracleGate.RequireReplayValidatedWitness(
            evidence, allocations, purity, capabilities, summary);
        Assert.DoesNotThrow(admitEffect);

        // This is a genuine entry-feasibility VC counterexample, not effect
        // evidence. The raw admission boundary must reject its missing family.
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!, budgets.QueryRlimit, budgets.MethodRlimit);
        var entry = await solver.VerifyEntryAsync(CancellationToken.None);
        Assert.That(entry.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(entry.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(entry.ExceptionWitness, Is.Null);
        Assert.That(entry.AllocationWitness, Is.Null);
        Assert.That(entry.WriteWitness, Is.Null);
        Assert.That(entry.LockWitness, Is.Null);
        Action rejectEntry = () => NativeEffectOracleGate.RequireReplayValidatedWitness(
            entry, allocations, purity, capabilities, summary);
        var rejected = Assert.Throws<InvalidDataException>(rejectEntry);
        Assert.That(rejected!.Message, Is.EqualTo("A native effect refutation has no replay-validated witness."));
    }
}
