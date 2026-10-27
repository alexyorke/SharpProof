using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class DeveloperMetadataCoverageTests
{
    private const string IdentitySource = """
        using SharpProof.Attributes;
        public static class Subject {
            public static int Target(int originalValue) {
                Contract.Ensures(Contract.Result<int>() == originalValue);
                return originalValue;
            }
        }
        """;

    [Test]
    public void CompilerArtifactRetainsSourceAndReferenceSnapshotsForExport()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(IdentitySource);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            TestContext.CurrentContext.CancellationToken);
        var roundTrip = CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations,
            TestContext.CurrentContext.CancellationToken);
        var root = JsonNode.Parse(json)!;
        Assert.That(root["schemaVersion"]!.GetValue<int>(), Is.EqualTo(CompilerManifestArtifactVersions.Current));
        Assert.That(root["protocolVersion"]!.GetValue<string>(), Is.EqualTo(WorkerProtocolVersions.Current));
        Assert.That(root["compilation"]!["syntaxTrees"]!.AsArray(), Has.Count.EqualTo(1));
        Assert.That(root["compilation"]!["references"]!.AsArray(), Is.Not.Empty);
        Assert.That(Path.GetFileName(roundTrip.Compilation.SyntaxTrees.Single().Path), Is.EqualTo("Subject.cs"));
        Assert.That(roundTrip.Compilation.SyntaxTrees.Single().TextLength, Is.EqualTo(IdentitySource.Length));
        Assert.That(roundTrip.Compilation.SyntaxTrees.Single().LineMap, Is.Not.Empty);
        Assert.That(JsonSerializer.Serialize(roundTrip.Compilation.SyntaxTrees, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(artifact.Compilation.SyntaxTrees, WorkerProtocolJson.SharedOptions)));
        Assert.That(JsonSerializer.Serialize(roundTrip.Compilation.References, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(JsonSerializer.Serialize(artifact.Compilation.References, WorkerProtocolJson.SharedOptions)));
        Assert.That(roundTrip.Compilation.References.SelectMany(reference => reference is null ? throw new ArgumentNullException(nameof(reference)) : reference.Modules), Is.Not.Empty);
        Assert.That(preparations.Single().Compilation, Is.SameAs(roundTrip.Compilation));
        Assert.That(roundTrip.Manifest.Hash, Is.EqualTo(artifact.Manifest.Hash));
    }

    [Test]
    public async Task CompilerAbstentionReachesUnknownWorkerClaimWithMappedSourceAndLabels()
    {
        const string source = """
            using SharpProof.Attributes;
            public static class Subject {
            #line 70 "MappedView.cs"
                public static int Target(int originalValue, int unusedValue) {
                    Contract.Requires(originalValue >= 0);
                    Contract.Ensures(Contract.Result<int>() == Contract.Result<int>());
                    Contract.Ensures(Contract.Result<int>() == originalValue);
                    var unsupported = 1.5 * originalValue;
                    throw null!;
                }
            }
            """;
        using var project = new ShadowTestProject(source);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.IsBodyAbstraction, Is.True);
        Assert.That(preparation.Diagnostic, Is.Not.Null);
        Assert.That(preparation.Diagnostic!.Stage, Is.EqualTo("CompilerLowering"));
        Assert.That(preparation.Diagnostic.Code, Is.Not.Empty);
        Assert.That(preparation.Diagnostic.Location.Path, Does.EndWith("MappedView.cs"));
        Assert.That(preparation.Diagnostic.Location.Line, Is.GreaterThanOrEqualTo(70));
        Assert.That(WorkerProtocolJson.IsDiagnosticContextValid(preparation.Diagnostic), Is.True);
        Assert.That(preparation.Presentation!.Name, Is.EqualTo("Subject.Target"));
        Assert.That(preparation.Presentation.ParameterNames, Is.EqualTo(ImmutableArray.Create("originalValue", "unusedValue")));

        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        var unknown = response.ClaimResults.Single(result => result is null ? throw new ArgumentNullException(nameof(result)) : result.Outcome == WorkerClaimOutcome.Unknown);
        var proven = response.ClaimResults.Single(result => result is null ? throw new ArgumentNullException(nameof(result)) : result.Outcome == WorkerClaimOutcome.Proven);
        Assert.That(unknown.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        Assert.That(unknown.Diagnostic, Is.EqualTo(preparation.Diagnostic));
        Assert.That(proven.Diagnostic, Is.Null);
        Assert.That(unknown.Presentation, Is.EqualTo(preparation.Presentation));
        Assert.That(proven.Presentation, Is.EqualTo(preparation.Presentation));
        Assert.That(unknown.Model, Is.Empty);
        Assert.That(response.Manifest.Hash, Is.EqualTo(project.Artifact.Manifest.Hash));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var json = WorkerProtocolJson.SerializeResponse(response);
        Assert.That(WorkerProtocolJson.SerializeResponse(WorkerProtocolJson.DeserializeResponse(json)!), Is.EqualTo(json));
    }

    [TestCase(true, WorkerClaimOutcome.Proven)]
    [TestCase(false, WorkerClaimOutcome.Refuted)]
    public async Task DefiniteSolverResultsDiscardCompilerDiagnosticWithoutChangingProofAuthority(
        bool postcondition, WorkerClaimOutcome expected)
    {
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int originalValue) {
                    Contract.Ensures({{(postcondition ? "true" : "false")}});
                    return originalValue;
                }
            }
            """;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var original = artifact.Callables.Single();
        var decorated = original with
        {
            Diagnostic = new WorkerDiagnosticContext
            {
                Code = "DescriptiveContext",
                Location = artifact.Manifest.Claims.Single().Location
            }
        };
        artifact = artifact with { Callables = [decorated] };
        Assert.That(decorated.Total, Is.SameAs(original.Total));
        Assert.That(decorated.TotalEntry, Is.SameAs(original.TotalEntry));
        using var project = new ShadowTestProject(artifact);
        Assert.That(project.Snapshot.Callables.Single().Diagnostic, Is.Not.Null);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot,
            TestContext.CurrentContext.CancellationToken);
        var result = response.ClaimResults.Single();
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(result.Outcome, Is.EqualTo(expected));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(result.Diagnostic, Is.Null);
        Assert.That(result.Presentation!.ParameterNames, Is.EqualTo(ImmutableArray.Create("originalValue")));
        Assert.That(response.Manifest.Hash, Is.EqualTo(artifact.Manifest.Hash));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public void OptionalCompilerMetadataCanBeAbsentAtTheWorkerBoundary()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(IdentitySource);
        artifact = artifact with
        {
            Callables = [artifact.Callables.Single() with { Presentation = null, Diagnostic = null }]
        };
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            TestContext.CurrentContext.CancellationToken);
        var callable = JsonNode.Parse(json)!["callables"]![0]!.AsObject();
        Assert.That(callable.ContainsKey("presentation"), Is.False);
        Assert.That(callable.ContainsKey("diagnostic"), Is.False);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations,
            TestContext.CurrentContext.CancellationToken);
        var preparation = preparations.Single();
        Assert.That(preparation.Presentation, Is.Null);
        Assert.That(preparation.Diagnostic, Is.Null);
        var unknown = CallableClaimResultAssembler.Unknown(preparation, 0, WorkerClaimReason.SolverIncomplete);
        Assert.That(unknown.Presentation, Is.Null);
        Assert.That(unknown.Diagnostic, Is.Null);
        Assert.That(unknown.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("name")]
    [TestCase("default-parameters")]
    [TestCase("null-parameter")]
    [TestCase("diagnostic-stage")]
    [TestCase("diagnostic-code")]
    [TestCase("diagnostic-location")]
    [TestCase("diagnostic-path")]
    public void CompilerArtifactBoundaryRejectsMalformedDescriptiveMetadata(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(IdentitySource);
        var original = artifact.Callables.Single();
        var diagnostic = new WorkerDiagnosticContext
        {
            Code = "UnsupportedBody",
            Location = artifact.Manifest.Claims.Single().Location
        };
        var malformed = mutation switch
        {
            "name" => original with { Presentation = original.Presentation! with { Name = " " } },
            "default-parameters" => original with { Presentation = original.Presentation! with { ParameterNames = default } },
            "null-parameter" => original with { Presentation = original.Presentation! with { ParameterNames = [null!] } },
            "diagnostic-stage" => original with { Diagnostic = diagnostic with { Stage = "Solver" } },
            "diagnostic-code" => original with { Diagnostic = diagnostic with { Code = "Unsupported Body" } },
            "diagnostic-location" => original with { Diagnostic = diagnostic with { Location = new WorkerSourceLocation() } },
            "diagnostic-path" => original with { Diagnostic = diagnostic with { Location = diagnostic.Location with { Path = null! } } },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        artifact = artifact with { Callables = [malformed] };
        Assert.Throws<InvalidDataException>((Action)(() => CompilerLoweredArtifact.Decode(
            artifact.Callables, artifact.Manifest, artifact.Compilation,
            TestContext.CurrentContext.CancellationToken)));
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact,
            TestContext.CurrentContext.CancellationToken);
        Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.DeserializePrepared(json,
            out _, TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void DiagnosticSourceProjectionPreservesMappedAndGeneratedPathCases()
    {
        Assert.That(WorkerProtocolJson.IsNoneLocation(CompilerSourceLocationProjection.Create(Location.None)), Is.True);
        Assert.That(CompilerSourceLocationProjection.MappedPath(null, default), Is.EqualTo("<compiler-generated>"));
        Assert.That(CompilerSourceLocationProjection.MappedPath(CSharpSyntaxTree.ParseText(string.Empty), default),
            Is.EqualTo("<compiler-generated>"));
        var relative = CSharpSyntaxTree.ParseText(string.Empty, path: "Subject.cs");
        Assert.That(CompilerSourceLocationProjection.MappedPath(relative, default), Is.EqualTo("Subject.cs"));
        var absolute = CSharpSyntaxTree.ParseText(string.Empty, path: "/physical/Subject.cs");
        var start = new LinePosition(4, 2);
        var end = new LinePosition(4, 3);
        Assert.That(CompilerSourceLocationProjection.MappedPath(absolute,
            new FileLinePositionSpan("MappedView.cs", start, end)), Is.EqualTo(Path.GetFullPath("/physical/MappedView.cs")));
        Assert.That(CompilerSourceLocationProjection.MappedPath(relative,
            new FileLinePositionSpan("/mapped/View.cs", start, end)), Is.EqualTo(Path.GetFullPath("/mapped/View.cs")));
        Assert.That(CompilerSourceLocationProjection.MappedPath(relative,
            new FileLinePositionSpan(@"C:\mapped\View.cs", start, end)), Is.EqualTo("C:/mapped/View.cs"));
    }
}
