using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class IteratorRootAdmissionControlTests
{
    private const int EmptyStatements = 4096;
    private const int WarmupIterations = 10000;
    private const int MeasuredIterations = 128;
    private static int s_integer;
    private static IEnumerable<int>? s_sequence;

    [TestCase(false)]
    [TestCase(true)]
    public async Task OrdinaryRootKeepsExactAllocationEvidenceAfterCfgErasesEmptyStatements(bool referenceReturn)
    {
        var source = "using System.Collections.Generic; using SharpProof.Attributes; public static class Subject { " +
            "[ZeroAllocations] public static " + (referenceReturn ? "IEnumerable<int>?" : "int") + " Target() { " +
            new string(';', EmptyStatements) + (referenceReturn ? "return null;" : "return 7;") + " } }";
        var repository = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT") ?? TestContext.CurrentContext.WorkDirectory;
        var evidence = Path.Combine(repository, "artifacts", "correctness", "iterator-root-admission-controls",
            (referenceReturn ? "reference" : "integer") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        var sourcePath = Path.Combine(evidence, "Subject.cs");
        await File.WriteAllTextAsync(sourcePath, source, new UTF8Encoding(false));
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview, preprocessorSymbols: []),
            sourcePath, Encoding.UTF8);
        var compilation = CSharpCompilation.Create("IteratorRootControl_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True);
        var bytes = image.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(evidence, "Subject.dll"), bytes);
        var context = new AssemblyLoadContext("IteratorRootControl_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            using var runtimeImage = new MemoryStream(bytes);
            var method = context.LoadFromStream(runtimeImage).GetType("Subject")!.GetMethod("Target")!;
            Delegate target = referenceReturn ? method.CreateDelegate<Func<IEnumerable<int>?>>() : method.CreateDelegate<Func<int>>();
            for (var repeat = 0; repeat < WarmupIterations; repeat++)
            { Execute(target); }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var repeat = 0; repeat < MeasuredIterations; repeat++)
            { Execute(target); }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(target);
            Assert.That(allocated, Is.Zero);
            if (referenceReturn)
            { Assert.That(s_sequence, Is.Null); }
            else
            { Assert.That(s_integer, Is.EqualTo(7)); }
            using var peStream = new MemoryStream(bytes);
            using var pe = new PEReader(peStream);
            var metadata = pe.GetMetadataReader();
            var mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
            var definition = metadata.GetMethodDefinition(MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0x00ffffff));
            var il = pe.GetMethodBody(definition.RelativeVirtualAddress).GetILBytes()!;
            Assert.That(method.Module.ModuleVersionId, Is.EqualTo(mvid));
            Assert.That(target.Method.Module.ModuleVersionId, Is.EqualTo(mvid));
            Assert.That(target.Method.MetadataToken, Is.EqualTo(method.MetadataToken));
            Assert.That(target.Target, Is.Null);
            Assert.That(method.GetMethodBody()!.GetILAsByteArray(), Is.EqualTo(il));
            Assert.That(method.GetCustomAttributes(typeof(IteratorStateMachineAttribute), inherit: false), Is.Empty);
            var symbol = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
            var attribute = symbol.GetAttributes().Single();
            var attributeSyntax = await attribute.ApplicationSyntaxReference!.GetSyntaxAsync();
            var callableId = DocumentationCommentId.CreateDeclarationId(symbol)!;
            var canonicalAttribute = SymbolEqualityComparer.Default.Equals(attribute.AttributeClass,
                compilation.GetTypeByMetadataName(typeof(ZeroAllocationsAttribute).FullName!));
            var artifact = CompilerManifestArtifactProducer.Create(compilation, evidence, "net9.0", WorkerFeatureSet.All,
                new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
            using var project = new ShadowTestProject(artifact, cacheEnabled: false);
            var preparation = project.Snapshot.Callables.Single();
            var claim = artifact.Manifest.Claims.Single();
            var effectClaim = preparation.EffectClaims.Single();
            var native = await NativeEffectSiteVerifier.VerifyAsync(preparation, project.Request.Budgets);
            await File.WriteAllTextAsync(Path.Combine(evidence, "compiler-artifact.json"), CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
            await File.WriteAllTextAsync(Path.Combine(evidence, "observations.json"), JsonSerializer.Serialize(new
            {
                ReferenceReturn = referenceReturn,
                SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source))),
                PeSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                Compiler = typeof(CSharpCompilation).Assembly.FullName,
                CompilerMvid = typeof(CSharpCompilation).Module.ModuleVersionId,
                Symbols = ((CSharpParseOptions)tree.Options).PreprocessorSymbolNames.ToArray(),
                CallableId = callableId,
                CanonicalAttribute = canonicalAttribute,
                AttributePath = attributeSyntax.SyntaxTree.FilePath,
                AttributeStart = attributeSyntax.SpanStart,
                AttributeLength = attributeSyntax.Span.Length,
                ManifestClaim = claim,
                EntryCallableId = preparation.Entry.CallableId,
                EntryClaimIds = preparation.Entry.ClaimIds,
                PreparedEffectClaimId = effectClaim.ClaimId,
                PreparedEffectContractKind = effectClaim.ContractKind.ToString(),
                ValidEffectClaimIds = preparation.Total?.ValidEffectClaimIds.ToArray(),
                EntryAssumptions = preparation.Entry.Assumptions,
                TotalClauseKinds = preparation.Total?.Clauses.Select(clause => clause.Kind.ToString()).ToArray(),
                CallPreconditionCount = preparation.Total?.CallPreconditions.Length,
                NativeBodyAssumptions = native.BodyAssumptions.Select(operation => operation.ToString()).ToArray(),
                EmptyStatements,
                WarmupIterations,
                MeasuredIterations,
                AllocatedBytes = allocated,
                Result = referenceReturn ? null : (int?)s_integer,
                PeMvid = mvid,
                MethodToken = method.MetadataToken,
                DelegateMethodToken = target.Method.MetadataToken,
                Il = Convert.ToHexStringLower(il),
                CacheEnabled = project.Request.Cache.Enabled,
                project.Request.Budgets,
                HasTotal = preparation.Total != null,
                BodyAbstraction = preparation.Total?.IsBodyAbstraction,
                EffectsCompleteAtEntry = preparation.Total?.EffectsCompleteAtEntry,
                NativeOutcome = native.Outcome?.GetType().Name ?? "Unknown",
                NativeReason = native.Reason.ToString(),
                native.HasFeasibleEntryWitness,
                native.QueryCompleted,
                NativeAllocationWitness = native.AllocationWitness?.ToString()
            }));
            Assert.That(canonicalAttribute, Is.True);
            Assert.That(artifact.Manifest.Callables.Single().CallableId, Is.EqualTo(callableId));
            Assert.That(preparation.Entry.CallableId, Is.EqualTo(callableId));
            Assert.That(claim.CallableId, Is.EqualTo(callableId));
            Assert.That(claim.Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(claim.Evidence, Is.EqualTo(WorkerClaimEvidence.Attribute));
            Assert.That(claim.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.ZeroAllocations));
            Assert.That(claim.Location.Path, Is.EqualTo(attributeSyntax.SyntaxTree.FilePath));
            Assert.That(claim.Location.Start, Is.EqualTo(attributeSyntax.SpanStart));
            Assert.That(claim.Location.Length, Is.EqualTo(attributeSyntax.Span.Length));
            Assert.That(effectClaim.ContractKind, Is.EqualTo(WorkerEffectContractKind.ZeroAllocations));
            Assert.That(effectClaim.ClaimId, Is.EqualTo(claim.ClaimId));
            Assert.That(preparation.Entry.ClaimIds, Is.EqualTo(new[] { claim.ClaimId }));
            Assert.That(preparation.Entry.Assumptions, Is.Empty);
            Assert.That(preparation.Total, Is.Not.Null);
            Assert.That(preparation.Total!.ValidEffectClaimIds, Is.EqualTo(new[] { claim.ClaimId }));
            Assert.That(preparation.Total.Clauses, Is.Empty);
            Assert.That(preparation.Total.CallPreconditions, Is.Empty);
            Assert.That(native.BodyAssumptions, Is.Empty);
            Assert.That(preparation.Total.IsBodyAbstraction, Is.False);
            Assert.That(preparation.Total.EffectsCompleteAtEntry, Is.True);
            Assert.That(native.HasFeasibleEntryWitness, Is.True);
            Assert.That(native.AllocationWitness, Is.Null);
            Assert.That(native.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>());
        }
        finally
        {
            s_sequence = null;
            context.Unload();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Execute(Delegate target)
    {
        if (target is Func<int> integer)
        { s_integer = integer(); }
        else
        { s_sequence = ((Func<IEnumerable<int>?>)target)(); }
    }
}
