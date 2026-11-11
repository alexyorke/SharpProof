using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A call into another assembly checks the clauses the calling compilation can
// see: closed parameter attributes in the referenced image, and the Requires of
// a companion the calling project declares. A clause that is not checked stays
// in the unchecked count; none is assumed after the call.
[TestFixture]
public sealed class MetadataCallPreconditionTests
{
    private const string LibrarySource = """
        using SharpProof.Attributes;
        public static class Remote
        {
            public static int Count([Positive] int n) => n;
            public static int Length([NonNull] string text) => text.Length;
            public static int Pick([InRange(0, 9)] int index, [Positive] int weight) => index * weight;
            public static int Plain(int n) => n;
            public static int Describe([NonNull] object value) => 0;
        }
        public sealed class Box
        {
            public Box([Positive] int size) { Size = size; }
            public int Size { get; }
            public int Scale([InRange(1, 4)] int factor) => Size * factor;
        }
        public class Shape
        {
            public virtual int Area([Positive] int side) => side * side;
        }
        """;

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

    private static PortableExecutableReference Library(TempDirectory directory, string source = LibrarySource, bool referenceAssembly = false)
    {
        System.ArgumentNullException.ThrowIfNull(directory);
        System.ArgumentNullException.ThrowIfNull(source);
        var path = Path.Combine(directory.FullName, referenceAssembly ? "RemoteLibrary.ref.dll" : "RemoteLibrary.dll");
        var options = referenceAssembly ? new EmitOptions(metadataOnly: true) : new EmitOptions();
        using (var stream = File.Create(path))
        {
            // A reference assembly keeps signatures and attributes; its bodies only throw.
            var library = referenceAssembly
                ? source.Replace("using SharpProof.Attributes;", "using SharpProof.Attributes;\n[assembly: System.Runtime.CompilerServices.ReferenceAssembly]", StringComparison.Ordinal)
                : source;
            Assert.That(Compile("RemoteLibrary", "Library.cs", library).Emit(stream, options: options).Success, Is.True);
        }
        return MetadataReference.CreateFromFile(path);
    }

    private static CompilerManifestArtifact Artifact(string source, MetadataReference library)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        System.ArgumentNullException.ThrowIfNull(library);
        var compilation = Compile("Consumer", "Consumer.cs", source, library);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All, discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
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

    [TestCase("public static int Caller() { return Remote.Count(0); }", "Remote.Count(0)", "Positive", 0)]
    [TestCase("public static int Caller() { return Remote.Length(null); }", "Remote.Length(null)", "NonNull", 0)]
    [TestCase("public static int Caller() { return Remote.Pick(10, 1); }", "Remote.Pick(10, 1)", "InRange(0, 9)", 0)]
    [TestCase("public static int Caller() { return new Box(-1).Size; }", "new Box(-1)", "Positive", 0)]
    [TestCase("public static int Caller(Box box) { return box.Scale(5); }", "box.Scale(5)", "InRange(1, 4)", 0)]
    [TestCase("public static int Caller() { return Remote.Describe(null); }", "Remote.Describe(null)", "NonNull", 0)]
    public async Task ViolatedAttributeIsRefutedAtTheCall(string caller, string call, string attribute, int parameter)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        System.ArgumentNullException.ThrowIfNull(call);
        System.ArgumentNullException.ThrowIfNull(attribute);
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var source = Consumer(caller);
        var artifact = Artifact(source, Library(directory));
        var response = await Verify(artifact);
        var refuted = CallClaims(response, "Consumer.Caller").Where(static row => row.Result.Outcome == WorkerClaimOutcome.Refuted).ToImmutableArray();
        Assert.That(refuted, Has.Length.EqualTo(1), () => string.Join("; ", CallClaims(response, "Consumer.Caller").Select(static row => row.Result.Outcome + "/" + row.Result.Reason)));
        var (claim, _) = refuted[0];
        using (Assert.EnterMultipleScope())
        {
            Assert.That(source.Substring(claim.Location.Start, claim.Location.Length), Is.EqualTo(call));
            Assert.That(claim.CallPrecondition!.ClauseLocation, Is.Null);
            Assert.That(claim.CallPrecondition.CallerDeclared, Is.False);
            Assert.That(claim.CallPrecondition.MetadataClause!.Attribute, Is.EqualTo(attribute));
            Assert.That(claim.CallPrecondition.MetadataClause.Parameter, Is.EqualTo(parameter));
            Assert.That(claim.CallPrecondition.MetadataClause.Assembly, Does.StartWith("RemoteLibrary, "));
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
        }
    }

    [TestCase("public static int Caller() { return Remote.Count(3); }")]
    [TestCase("public static int Caller(int n) { Contract.Requires(n > 0); return Remote.Count(n); }")]
    [TestCase("public static int Caller(string s) { return s == null ? 0 : Remote.Length(s); }")]
    [TestCase("public static int Caller(int i) { return i >= 0 && i <= 9 ? Remote.Pick(i, 2) : 0; }")]
    [TestCase("public static int Caller() { return new Box(2).Scale(4); }")]
    [TestCase("public static int Caller() { return Remote.Describe(\"text\") + Remote.Describe(new Box(1)) + Remote.Describe(new int[0]); }")]
    public async Task SatisfiedAttributeIsProven(string caller)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var artifact = Artifact(Consumer(caller), Library(directory));
        var claims = CallClaims(await Verify(artifact), "Consumer.Caller");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claims, Is.Not.Empty);
            Assert.That(claims.Select(static row => row.Result.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
        }
    }

    [Test]
    public async Task EachAttributeIsItsOwnClaim()
    {
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var claims = CallClaims(await Verify(Artifact(Consumer("public static int Caller() { return Remote.Pick(3, 0); }"),
            Library(directory))), "Consumer.Caller");
        Assert.That(claims.Select(static row => (row.Claim.CallPrecondition!.MetadataClause!.Attribute, row.Result.Outcome)), Is.EquivalentTo(new[]
        {
            ("InRange(0, 9)", WorkerClaimOutcome.Proven),
            ("Positive", WorkerClaimOutcome.Refuted)
        }));
    }

    [Test]
    public async Task ReferenceAssemblyAttributesAreChecked()
    {
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var artifact = Artifact(Consumer("public static int Caller() { return Remote.Count(0); }"), Library(directory, referenceAssembly: true));
        var claims = CallClaims(await Verify(artifact), "Consumer.Caller");
        Assert.That(claims.Select(static row => row.Result.Outcome), Is.EqualTo(new[] { WorkerClaimOutcome.Refuted }));
    }

    [Test]
    public void DispatchedCallIsCountedNotChecked()
    {
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var artifact = Artifact(Consumer("public static int Caller(Shape shape) { return shape.Area(0); }"), Library(directory));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.EqualTo(1));
            Assert.That(artifact.Manifest.Claims.Select(static claim => claim.Kind), Has.None.EqualTo(WorkerClaimKind.CallPrecondition));
        }
    }

    [TestCase("public static System.Func<int> Caller() { return () => Remote.Count(0); }", TestName = "Lambda")]
    [TestCase("public static int Caller(int[] values) { return Remote.Count(values.Length > 0 ? values[0] : 1) + Remote.Count(2); }", TestName = "OneOfTwo")]
    public void UncheckedCallIsCounted(string caller)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var artifact = Artifact(Consumer(caller), Library(directory));
        var checkedSites = artifact.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.CallPrecondition)
            .Select(static claim => claim.Location).Distinct().Count();
        var sites = caller.Split("Remote.Count(").Length - 1;
        Assert.That(artifact.Manifest.UncheckedCallSites + checkedSites, Is.EqualTo(sites));
    }

    [Test]
    public async Task CalleeAttributeIsNotAssumedAfterTheCall()
    {
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var response = await Verify(Artifact(Consumer("""
            public static int Caller(int n) {
                Contract.Ensures(Contract.Result<int>() > 0);
                Remote.Count(n);
                return n;
            }
            """), Library(directory)));
        var postcondition = response.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.Postcondition);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.ClaimResults.Single(result => result.ClaimId == postcondition.ClaimId).Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(CallClaims(response, "Consumer.Caller").Single().Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        }
    }

    [Test]
    public async Task InlinedHelperAnswersForItsOwnCall()
    {
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var response = await Verify(Artifact(Consumer("""
            public static int Helper(int n) { return Remote.Count(n); }
            public static int Caller() { return Helper(0); }
            """), Library(directory)));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(CallClaims(response, "Consumer.Helper").Select(static row => row.Result.Outcome), Is.EqualTo(new[] { WorkerClaimOutcome.Refuted }));
            Assert.That(CallClaims(response, "Consumer.Caller"), Is.Empty);
        }
    }

    [Test]
    public async Task CallerDeclaredCompanionIsChecked()
    {
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var source = """
            using SharpProof.Attributes;
            [ContractFor(typeof(Remote))]
            public static class RemoteContracts {
                public static int Plain(int n) { Contract.Requires(n > 0); return 0; }
            }
            public static class Consumer {
                public static int Bad() { return Remote.Plain(0); }
                public static int Good() { return Remote.Plain(1); }
            }
            """;
        var artifact = Artifact(source, Library(directory));
        var response = await Verify(artifact);
        var (bad, badResult) = CallClaims(response, "Consumer.Bad").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(badResult.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(bad.CallPrecondition!.CallerDeclared, Is.True);
            Assert.That(bad.CallPrecondition.MetadataClause, Is.Null);
            Assert.That(source.Substring(bad.CallPrecondition.ClauseLocation!.Start, bad.CallPrecondition.ClauseLocation.Length), Does.Contain("n > 0"));
            Assert.That(CallClaims(response, "Consumer.Good").Single().Result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
            Assert.That(SharpProof.Worker.Launcher.LauncherPresentation.CallPreconditionDetail(bad), Does.Contain("declared by the calling project"));
        }
    }

    [Test]
    public async Task PartialCompanionKeepsAttributeChecksOnOtherMembers()
    {
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var artifact = Artifact("""
            using SharpProof.Attributes;
            [ContractFor(typeof(Remote))]
            public static class RemoteContracts {
                public static int Plain(int n) { Contract.Requires(n > 0); return 0; }
            }
            public static class Consumer { public static int Caller() { return Remote.Count(0); } }
            """, Library(directory));
        var (claim, result) = CallClaims(await Verify(artifact), "Consumer.Caller").Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(claim.CallPrecondition!.MetadataClause!.Attribute, Is.EqualTo("Positive"));
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
        }
    }

    [Test]
    public void CompanionAndAttributesAreCheckedTogether()
    {
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var artifact = Artifact("""
            using SharpProof.Attributes;
            [ContractFor(typeof(Remote))]
            public static class RemoteContracts {
                public static int Count(int n) { Contract.Requires(n < 100); return 0; }
            }
            public static class Consumer { public static int Caller(int n) { return Remote.Count(n); } }
            """, Library(directory));
        var targets = artifact.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.CallPrecondition)
            .Select(static claim => claim.CallPrecondition!).ToImmutableArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(targets.Select(static target => (target.ClauseOrdinal, target.CallerDeclared, target.MetadataClause?.Attribute)),
                Is.EqualTo(new (int, bool, string?)[] { (0, true, null), (1, false, "Positive") }));
            Assert.That(artifact.Manifest.UncheckedCallSites, Is.Zero);
        }
    }

    // Every field of a metadata target is checked against the captured
    // evidence, so a resealed manifest cannot relabel what was proved.
    [TestCase("assembly")]
    [TestCase("attribute")]
    [TestCase("member")]
    [TestCase("parameter")]
    public void ArtifactRejectsAForgedMetadataTarget(string field)
    {
        System.ArgumentNullException.ThrowIfNull(field);
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var artifact = Artifact(Consumer("public static int Caller() { return Remote.Pick(10, 1); }"), Library(directory));
        var claim = artifact.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.CallPrecondition &&
            claim.CallPrecondition!.MetadataClause!.Attribute.StartsWith("InRange", StringComparison.Ordinal));
        var metadata = claim.CallPrecondition!.MetadataClause!;
        var forged = claim with
        {
            CallPrecondition = claim.CallPrecondition with
            {
                MetadataClause = field switch
                {
                    "assembly" => metadata with { Assembly = "Other, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" },
                    "attribute" => metadata with { Attribute = "Positive" },
                    "member" => metadata with { Member = metadata.Member.Replace("Pick", "Count", StringComparison.Ordinal) },
                    _ => metadata with { Parameter = 1 }
                }
            }
        };
        Assert.That(forged.CallPrecondition!.MetadataClause, Is.Not.EqualTo(metadata));
        var manifest = artifact.Manifest with { Claims = artifact.Manifest.Claims.Replace(claim, forged) };
        WorkerProtocolJson.SealManifest(manifest);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact with { Manifest = manifest }, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<System.Text.Json.JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(json, out _, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [TestCase("source-and-metadata")]
    [TestCase("neither")]
    [TestCase("caller-declared-metadata")]
    public void ManifestRejectsAMalformedMetadataTarget(string mutation)
    {
        System.ArgumentNullException.ThrowIfNull(mutation);
        using var directory = new TempDirectory("sharpproof-metadata-call-");
        var artifact = Artifact(Consumer("public static int Caller() { return Remote.Count(0); }"), Library(directory));
        var claim = artifact.Manifest.Claims.Single(static claim => claim.Kind == WorkerClaimKind.CallPrecondition);
        var target = claim.CallPrecondition!;
        var changed = claim with
        {
            CallPrecondition = mutation switch
            {
                "source-and-metadata" => target with { ClauseLocation = claim.Location },
                "neither" => target with { MetadataClause = null },
                _ => target with { CallerDeclared = true }
            }
        };
        var manifest = artifact.Manifest with { Claims = artifact.Manifest.Claims.Replace(claim, changed) };
        manifest.Hash = string.Empty;
        Assert.That(WorkerProtocolJson.ValidateManifest(manifest).Errors.Select(static error => error.Code), Has.Member("manifest.claim_call_precondition"));
    }
}
