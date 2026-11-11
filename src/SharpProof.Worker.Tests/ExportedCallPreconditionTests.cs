using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A project of the same build exports the Requires of its public and internal
// callables. A caller checks them at its calls only when the export is stamped
// with the exact image it compiled against. A call that does not attach is
// counted unchecked, and no callee clause is assumed after the call.
[TestFixture]
public sealed class ExportedCallPreconditionTests
{
    private const string LibrarySource = """
        using SharpProof.Attributes;
        public static class Remote
        {
            public static int Take(int n)
            {
                Contract.Requires(n > 0);
                Contract.Requires(n < 100);
                return n;
            }
            public static int Name(string text)
            {
                Contract.Requires(text != null && text.Length > 0);
                return text.Length;
            }
            public static int Pick([Positive] int weight, int index)
            {
                Contract.Requires(index < 10);
                return weight * index;
            }
            public static int Plain(int n) => n;
        }
        public sealed class Counter
        {
            private int _limit = 10;
            public Counter(int start)
            {
                Contract.Requires(start >= 0);
                Value = start;
            }
            public int Value { get; }
            public int Below(int n)
            {
                Contract.Requires(n < _limit);
                return n;
            }
        }
        public class Shape
        {
            public virtual int Area(int side)
            {
                Contract.Requires(side > 0);
                return side * side;
            }
        }
        """;

    private sealed record Library(PortableExecutableReference Reference, string Export, string ImagePath);

    private static readonly string[] s_exportedCallables =
    [
        "M:Remote.Take(System.Int32)", "M:Remote.Name(System.String)", "M:Remote.Pick(System.Int32,System.Int32)",
        "M:Counter.#ctor(System.Int32)", "M:Counter.Below(System.Int32)", "M:Shape.Area(System.Int32)"
    ];

    private static readonly int[] s_takeClauseLines = [6, 7];

    private static CSharpCompilation Compile(string name, string path, string source, params MetadataReference[] references)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        System.ArgumentNullException.ThrowIfNull(path);
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(references);
        var compilation = CSharpCompilation.Create(name, [CSharpSyntaxTree.ParseText(source, path: path)],
            TestMetadataReferences.WithSharpProof.AddRange(references), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }

    // Emits the library and its reference assembly, and stamps its export the
    // way the MSBuild step does. `stamp` may rewrite the stamped text.
    private static Library Build(TempDirectory directory, string source = LibrarySource, string? exportSource = null,
        Func<string, string>? stamp = null)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = Compile("RemoteLibrary", Path.Combine(directory.FullName, "Library.cs"), source);
        var implementation = Path.Combine(directory.FullName, "RemoteLibrary.dll");
        var reference = Path.Combine(directory.FullName, "RemoteLibrary.ref.dll");
        using (var stream = File.Create(implementation))
        { Assert.That(compilation.Emit(stream).Success, Is.True); }
        using (var stream = File.Create(reference))
        { Assert.That(compilation.Emit(stream, options: new EmitOptions(metadataOnly: true)).Success, Is.True); }
        var exporting = exportSource == null ? compilation : Compile("RemoteLibrary", Path.Combine(directory.FullName, "Library.cs"), exportSource);
        var export = CompilerContractsExportProducer.Create(exporting, "net10.0", TestContext.CurrentContext.CancellationToken);
        var stamped = new CompilerStampedContractsExport
        {
            Schema = CompilerContractsExportVersions.StampedSchema,
            Version = CompilerContractsExportVersions.Current,
            Images = [Image("implementation", implementation), Image("reference", reference)],
            Export = export
        };
        var json = JsonSerializer.Serialize(stamped, WorkerProtocolJson.SharedOptions);
        return new(MetadataReference.CreateFromFile(reference), stamp?.Invoke(json) ?? json, reference);
    }

    private static CompilerContractsExportImage Image(string role, string path)
    {
        var bytes = File.ReadAllBytes(path);
        using var pe = new PEReader(new MemoryStream(bytes));
        var reader = pe.GetMetadataReader();
        return new()
        {
            Role = role,
            Mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString("D"),
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes))
        };
    }

    private static CompilerManifestArtifact Artifact(string source, Library library)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(library);
        var compilation = Compile("Consumer", "Consumer.cs", source, library.Reference);
        ImmutableArray<AdditionalText> files = [new MemoryAdditionalText("/library/obj/SharpProof/contracts-export.json", library.Export)];
        var exports = CompilerContractExports.Load(compilation, files, "/project", TestContext.CurrentContext.CancellationToken);
        var discovery = new ClaimManifestBuilder(compilation) { ContractExports = exports }.Build();
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net10.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken, files, contractExports: exports);
    }

    private static async Task<WorkerVerifyResponse> Verify(CompilerManifestArtifact artifact)
    {
        System.ArgumentNullException.ThrowIfNull(artifact);
        using var project = new ShadowTestProject(artifact);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete), () => string.Join("; ", response.Errors.Select(static error => error.Code + ": " + error.Message)));
        return response;
    }

    private static ImmutableArray<(WorkerClaimManifestEntry Claim, WorkerClaimResult Result)> CallClaims(WorkerVerifyResponse response, string caller)
    {
        System.ArgumentNullException.ThrowIfNull(response);
        System.ArgumentNullException.ThrowIfNull(caller);
        return [.. response.Manifest.Claims
            .Where(claim => claim.Kind == WorkerClaimKind.CallPrecondition && claim.CallableId.Contains(caller, StringComparison.Ordinal))
            .Select(claim => (claim, response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId)))];
    }

    private static string Consumer(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        return "using SharpProof.Attributes;\npublic static class Consumer {\n    " + members + "\n}\n";
    }

    private static ImmutableArray<WorkerCallPreconditionTarget> Targets(CompilerManifestArtifact artifact)
    {
        return [.. artifact.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.CallPrecondition)
            .Select(static claim => claim.CallPrecondition!)];
    }

    [Test]
    public void ExportCarriesOnlyParameterClauses()
    {
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var export = CompilerContractsExportJson.TryDeserializeStamped(Build(directory).Export)!.Export;
        var callables = export.Callables.ToDictionary(static callable => callable.DocumentationId);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(export.AssemblyName, Is.EqualTo("RemoteLibrary"));
            Assert.That(callables.Keys, Is.EquivalentTo(s_exportedCallables));
            Assert.That(callables["M:Remote.Take(System.Int32)"].Complete, Is.True);
            Assert.That(callables["M:Remote.Take(System.Int32)"].ClauseLocations.Select(static location => location.Line), Is.EqualTo(s_takeClauseLines));
            // The closed attribute travels in metadata; only the Requires is exported.
            Assert.That(callables["M:Remote.Pick(System.Int32,System.Int32)"].ClauseLocations, Has.Length.EqualTo(1));
            Assert.That(callables["M:Counter.Below(System.Int32)"].Complete, Is.False);
            Assert.That(callables["M:Counter.Below(System.Int32)"].Requires, Is.Null);
        }
    }

    [Test]
    public async Task ViolatingCallIsRefutedAtTheCallSite()
    {
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var source = Consumer("public static int Caller() { return Remote.Take(0); }");
        var artifact = Artifact(source, Build(directory));
        var claims = CallClaims(await Verify(artifact), "Consumer.Caller");
        var refuted = claims.Where(static row => row.Result.Outcome == WorkerClaimOutcome.Refuted).ToImmutableArray();
        Assert.That(refuted, Has.Length.EqualTo(1), () => string.Join("; ", claims.Select(static row => row.Result.Outcome + "/" + row.Result.Reason)));
        var target = refuted[0].Claim.CallPrecondition!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.Substring(refuted[0].Claim.Location.Start, refuted[0].Claim.Location.Length), Is.EqualTo("Remote.Take(0)"));
            Assert.That(target.ExportedBy, Is.EqualTo("RemoteLibrary"));
            Assert.That(target.MetadataClause, Is.Null);
            Assert.That(target.CallerDeclared, Is.False);
            Assert.That(target.ClauseLocation!.Path, Does.EndWith("Library.cs"));
            Assert.That(target.ClauseLocation.Line, Is.EqualTo(6));
            Assert.That(claims.Select(static row => row.Result.Outcome), Is.EquivalentTo(new[] { WorkerClaimOutcome.Refuted, WorkerClaimOutcome.Proven }));
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
            Assert.That(SharpProof.Worker.Launcher.LauncherPresentation.CallPreconditionDetail(refuted[0].Claim),
                Does.Contain("Library.cs").And.Contain("exported by RemoteLibrary"));
        }
    }

    [TestCase("public static int Caller() { return Remote.Take(5); }")]
    [TestCase("public static int Caller(int n) { Contract.Requires(n > 0 && n < 50); return Remote.Take(n); }")]
    [TestCase("public static int Caller(string s) { return s != null && s.Length > 0 ? Remote.Name(s) : 0; }")]
    [TestCase("public static int Caller() { return new Counter(3).Value; }")]
    public async Task SatisfiedCallIsProven(string caller)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var artifact = Artifact(Consumer(caller), Build(directory));
        var claims = CallClaims(await Verify(artifact), "Consumer.Caller");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claims, Is.Not.Empty);
            Assert.That(claims.Select(static row => row.Result.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(claims.Select(static row => row.Claim.CallPrecondition!.ExportedBy), Is.All.EqualTo("RemoteLibrary"));
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
        }
    }

    [Test]
    public async Task ExportedClausesFollowClosedAttributes()
    {
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var artifact = Artifact(Consumer("public static int Caller() { return Remote.Pick(0, 12); }"), Build(directory));
        var claims = CallClaims(await Verify(artifact), "Consumer.Caller");
        Assert.That(claims.Select(static row => (row.Claim.CallPrecondition!.ClauseOrdinal, row.Claim.CallPrecondition.MetadataClause?.Attribute,
            row.Claim.CallPrecondition.ExportedBy, row.Result.Outcome)), Is.EqualTo(new (int, string?, string?, WorkerClaimOutcome)[]
            {
                (0, "Positive", null, WorkerClaimOutcome.Refuted),
                (1, null, "RemoteLibrary", WorkerClaimOutcome.Refuted)
            }));
    }

    [Test]
    public async Task CalleeRequiresIsNotAssumedAfterTheCall()
    {
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var response = await Verify(Artifact(Consumer("""
            public static int Caller(int n) {
                Contract.Ensures(Contract.Result<int>() > 0);
                Remote.Take(n);
                return n;
            }
            """), Build(directory)));
        var postcondition = response.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.Postcondition);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.ClaimResults.Single(result => result.ClaimId == postcondition.ClaimId).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(CallClaims(response, "Consumer.Caller").Select(static row => row.Result.Outcome),
                Is.EquivalentTo(new[] { WorkerClaimOutcome.Refuted, WorkerClaimOutcome.Refuted }));
        }
    }

    [Test]
    public async Task InlinedHelperAnswersForItsOwnCall()
    {
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var response = await Verify(Artifact(Consumer("""
            public static int Helper(int n) { return Remote.Take(n); }
            public static int Caller() { return Helper(0); }
            """), Build(directory)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CallClaims(response, "Consumer.Helper").Select(static row => row.Result.Outcome),
                Is.EquivalentTo(new[] { WorkerClaimOutcome.Refuted, WorkerClaimOutcome.Refuted }));
            Assert.That(CallClaims(response, "Consumer.Caller"), Is.Empty);
        }
    }

    [Test]
    public void DispatchedCallIsExcludedAndCounted()
    {
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var artifact = Artifact(Consumer("public static int Caller(Shape shape) { return shape.Area(0); }"), Build(directory));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Targets(artifact), Is.Empty);
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.EqualTo(1));
        }
    }

    [Test]
    public void ClauseReadingStateIsUncheckedAndCounted()
    {
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var artifact = Artifact(Consumer("public static int Caller(Counter counter) { return counter.Below(100) + Remote.Take(1); }"), Build(directory));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Targets(artifact).Select(static target => target.Callee), Has.All.Contains("Take"));
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.EqualTo(1));
        }
    }

    // The library changed after its export was written: the export's stamp
    // names images the caller did not compile against.
    [Test]
    public void StaleExportIsUncheckedAndCounted()
    {
        using var directory = new TempDirectory("sharpproof-exported-call-");
        using var previous = new TempDirectory("sharpproof-exported-call-");
        var stale = Build(previous).Export;
        var current = Build(directory, LibrarySource.Replace("public static int Plain(int n) => n;", "public static int Plain(int n) => n + 1;",
            StringComparison.Ordinal));
        var artifact = Artifact(Consumer("public static int Caller() { return Remote.Take(0); }"), current with { Export = stale });
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Targets(artifact), Is.Empty);
            Assert.That(artifact.Compilation.ContractExports.IsDefaultOrEmpty, Is.True);
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.EqualTo(1));
        }
    }

    // A readable export whose stamp does not match is never checked, and its
    // callees' calls are counted. One that cannot be read, or names another
    // assembly, says nothing about this reference.
    [TestCase("sha256", 1)]
    [TestCase("mvid", 1)]
    [TestCase("assembly", 0)]
    [TestCase("schema", 0)]
    public void ForgedStampIsRejected(string field, int uncheckedSites)
    {
        System.ArgumentNullException.ThrowIfNull(field);
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var library = Build(directory, stamp: json =>
        {
            var stamped = JsonSerializer.Deserialize<CompilerStampedContractsExport>(json, WorkerProtocolJson.SharedOptions)!;
            var images = stamped.Images.Select(image => field switch
            {
                "sha256" => image with { Sha256 = new string('0', 64) },
                "mvid" => image with { Mvid = Guid.NewGuid().ToString("D") },
                _ => image
            }).ToImmutableArray();
            stamped = field switch
            {
                "assembly" => stamped with { Images = images, Export = stamped.Export with { AssemblyIdentity = "Other, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null" } },
                "schema" => stamped with { Images = images, Schema = "SharpProof.ContractsExport.Other" },
                _ => stamped with { Images = images }
            };
            return JsonSerializer.Serialize(stamped, WorkerProtocolJson.SharedOptions);
        });
        var artifact = Artifact(Consumer("public static int Caller() { return Remote.Take(0); }"), library);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(Targets(artifact), Is.Empty);
            Assert.That(artifact.Compilation.ContractExports.IsDefaultOrEmpty, Is.True);
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.EqualTo(uncheckedSites));
        }
    }

    // A manifest whose exported row cites an export the compilation did not
    // record, or relabels where the clause is, does not decode.
    [TestCase("export-hash")]
    [TestCase("exporter")]
    [TestCase("location")]
    [TestCase("snapshot")]
    public void ArtifactRejectsAForgedExportedRow(string mutation)
    {
        System.ArgumentNullException.ThrowIfNull(mutation);
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var artifact = Artifact(Consumer("public static int Caller() { return Remote.Take(0); }"), Build(directory));
        Assert.That(Targets(artifact), Is.Not.Empty);
        CompilerManifestArtifact forged;
        if (mutation is "export-hash" or "snapshot")
        {
            forged = artifact with
            {
                Compilation = artifact.Compilation with
                {
                    ContractExports = [.. artifact.Compilation.ContractExports.Select(export => mutation == "snapshot"
                        ? export with { Images = [.. export.Images.Select(static image => image with { Sha256 = new string('1', 64) })] }
                        : export with { Sha256 = new string('2', 64) })]
                }
            };
        }
        else
        {
            var claim = artifact.Manifest.Claims.First(static claim => claim.Kind == WorkerClaimKind.CallPrecondition);
            var target = claim.CallPrecondition!;
            var changed = claim with
            {
                CallPrecondition = mutation == "exporter" ? target with { ExportedBy = "Other" }
                    : target with { ClauseLocation = target.ClauseLocation! with { Line = target.ClauseLocation.Line + 1 } }
            };
            var manifest = artifact.Manifest with { Claims = artifact.Manifest.Claims.Replace(claim, changed) };
            WorkerProtocolJson.SealManifest(manifest);
            forged = artifact with { Manifest = manifest };
        }
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(forged, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void ManifestRejectsACallerDeclaredExportedTarget()
    {
        using var directory = new TempDirectory("sharpproof-exported-call-");
        var artifact = Artifact(Consumer("public static int Caller() { return Remote.Take(0); }"), Build(directory));
        var claim = artifact.Manifest.Claims.First(static claim => claim.Kind == WorkerClaimKind.CallPrecondition);
        var changed = claim with { CallPrecondition = claim.CallPrecondition! with { CallerDeclared = true } };
        var manifest = artifact.Manifest with { Claims = artifact.Manifest.Claims.Replace(claim, changed) };
        manifest.Hash = string.Empty;
        Assert.That(WorkerProtocolJson.ValidateManifest(manifest).Errors.Select(static error => error.Code), Has.Member("manifest.claim_call_precondition"));
    }

    private sealed class MemoryAdditionalText(string path, string content) : AdditionalText, ICompilerAdditionalTextSnapshot
    {
        private readonly SourceText _text = SourceText.From(content ?? throw new System.ArgumentNullException(nameof(content)), Encoding.UTF8);

        public override string Path { get; } = path ?? throw new System.ArgumentNullException(nameof(path));

        SourceText ICompilerAdditionalTextSnapshot.CapturedText => _text;

        public override SourceText GetText(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _text;
        }
    }
}
