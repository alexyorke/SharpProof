using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
internal sealed class DeveloperCommandsCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task NamedExplainLoadsValidatedEvidenceWithDefaultOrExplicitCompanionPaths(bool explicitPaths)
    {
        using var fixture = new StoredEvidenceFixture();
        var arguments = new List<string> { "explain", "--claim", fixture.Response.Manifest.Claims[0].ClaimId, "--result", fixture.ResultPath };
        if (explicitPaths)
        {
            var request = Path.Combine(fixture.Root, "selected-request.json");
            var manifest = Path.Combine(fixture.Root, "selected-compiler-manifest.json");
            File.Move(fixture.RequestPath, request);
            File.Move(fixture.ManifestPath, manifest);
            arguments.AddRange(["--request", request, "--compiler-manifest", manifest]);
        }
        var run = await RunAsync([.. arguments]);
        Assert.That(run.ExitCode, Is.Zero, run.Error);
        Assert.That(run.Error, Is.Empty);
        Assert.That(run.Output, Does.Contain("1 Unknown").And.Contain("UnsupportedCallable")
            .And.Contain(fixture.Response.Manifest.Claims[0].ClaimId).And.Contain("Next:")
            .And.Contain("Unselected methods are not proved."));
    }

    [Test]
    public async Task PositionalExplainRetainsItsRecordedEvidenceJsonContract()
    {
        using var fixture = new StoredEvidenceFixture();
        var run = await RunAsync("explain", fixture.ResultPath, "--json");
        Assert.That(run.ExitCode, Is.Zero, run.Error);
        Assert.That(run.Error, Is.Empty);
        using var document = JsonDocument.Parse(run.Output);
        Assert.That(document.RootElement.GetProperty("scope").GetString(), Does.Contain("Stored evidence only"));
        Assert.That(document.RootElement.GetProperty("entries").GetArrayLength(), Is.EqualTo(2));
        Assert.That(document.RootElement.GetProperty("selectedClaims").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task NamedExplainRejectsAnAbsentClaimWithoutRenderingAnUnrelatedReport()
    {
        using var fixture = new StoredEvidenceFixture();
        var run = await RunAsync("explain", "--result", fixture.ResultPath, "--claim", "claim:absent");
        Assert.That(run.ExitCode, Is.EqualTo(2));
        Assert.That(run.Output, Is.Empty);
        Assert.That(run.Error, Does.Contain("requested claim is absent"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task IdenticalEvidenceHasNoRegressionsWithEitherExitPolicy(bool failOnRegression)
    {
        using var fixture = new StoredEvidenceFixture();
        var arguments = new List<string> { "compare", "--baseline", fixture.ResultPath, "--current", fixture.ResultPath };
        if (failOnRegression)
        { arguments.Add("--fail-on-regression"); }
        var run = await RunAsync([.. arguments]);
        Assert.That(run.ExitCode, Is.Zero, run.Error);
        Assert.That(run.Error, Is.Empty);
        Assert.That(run.Output, Does.Contain("0 changes; 0 regressions.").And.Not.Contain("NOTE").And.Not.Contain("REGRESSION"));
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task LostProofIsReportedAndFailsOnlyWhenRequested(bool failOnRegression, int exitCode)
    {
        using var baseline = new StoredEvidenceFixture();
        using var current = new StoredEvidenceFixture();
        SetProven(baseline);
        var run = await CompareAsync(baseline, current, failOnRegression);
        Assert.That(run.ExitCode, Is.EqualTo(exitCode), run.Error);
        Assert.That(run.Error, Is.Empty);
        Assert.That(run.Output, Does.Contain("REGRESSION Proven -> Unknown:")
            .And.Contain("New incomplete coverage:").And.Contain("Existing verifier policy still applies."));
    }

    [Test]
    public async Task NewlyEstablishedProofIsAnImprovementUnderTheRegressionExitPolicy()
    {
        using var baseline = new StoredEvidenceFixture();
        using var current = new StoredEvidenceFixture();
        SetProven(current);
        var run = await CompareAsync(baseline, current, failOnRegression: true);
        Assert.That(run.ExitCode, Is.Zero, run.Error);
        Assert.That(run.Error, Is.Empty);
        Assert.That(run.Output, Does.Contain("CHANGE Unknown -> Proven:").And.Contain("0 regressions.")
            .And.Not.Contain("REGRESSION"));
    }

    [TestCase("verification")]
    [TestCase("assumption")]
    public async Task DifferentPolicyCannotAppearAsAnUnchangedVerificationRun(string policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        using var baseline = new StoredEvidenceFixture();
        using var current = new StoredEvidenceFixture();
        current.Request = policy == "verification"
            ? current.Request with { VerifyPolicy = WorkerVerifyPolicy.RequireProven }
            : current.Request with { AssumptionPolicy = WorkerAssumptionPolicy.Error };
        current.Save();
        var run = await CompareAsync(baseline, current);
        Assert.That(run.ExitCode, Is.EqualTo(2));
        Assert.That(run.Output, Is.Empty);
        Assert.That(run.Error, Does.Contain("same verification and assumption policies"));
    }

    [TestCase("assembly")]
    [TestCase("framework")]
    [TestCase("missing-assembly")]
    [TestCase("missing-framework")]
    public async Task ComparisonRequiresTheSameRecordedAssemblyAndFramework(string difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        using var baseline = new StoredEvidenceFixture();
        using var current = new StoredEvidenceFixture();
        var target = difference.StartsWith("missing-", StringComparison.Ordinal) ? baseline : current;
        var snapshot = target.Artifact.Compilation;
        target.Artifact = target.Artifact with
        {
            Compilation = difference switch
            {
                "assembly" => snapshot with { AssemblyIdentity = "Other, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" },
                "framework" => snapshot with { TargetFramework = "net9.0" },
                "missing-assembly" => snapshot with { AssemblyIdentity = " " },
                _ => snapshot with { TargetFramework = " " }
            }
        };
        target.Save();
        var run = await CompareAsync(baseline, current);
        Assert.That(run.ExitCode, Is.EqualTo(2));
        Assert.That(run.Output, Is.Empty);
        Assert.That(run.Error, Does.Contain("same assembly identity and target framework"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DifferentSpecificationAuthorityCannotBeComparedAsTheSameRun(bool catalog)
    {
        using var baseline = new StoredEvidenceFixture();
        using var current = new StoredEvidenceFixture();
        current.Artifact = catalog
            ? current.Artifact with { SpecificationPackCatalogSha256 = new string('a', 64) }
            : current.Artifact with { SpecificationPackIds = ["dotnet.scalar"] };
        current.Save();
        var run = await CompareAsync(baseline, current);
        Assert.That(run.ExitCode, Is.EqualTo(2));
        Assert.That(run.Output, Is.Empty);
        Assert.That(run.Error, Does.Contain("same specification-pack selection and catalog"));
    }

    [TestCase("query")]
    [TestCase("method-resource")]
    [TestCase("method-time")]
    [TestCase("project-time")]
    [TestCase("parallelism")]
    [TestCase("expression-depth")]
    public async Task DifferentResourceBudgetsRemainComparableAndAreDisclosed(string budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        using var baseline = new StoredEvidenceFixture();
        using var current = new StoredEvidenceFixture();
        var budgets = current.Request.Budgets;
        switch (budget)
        {
            case "query":
                budgets.QueryRlimit++;
                break;
            case "method-resource":
                budgets.MethodRlimit++;
                break;
            case "method-time":
                budgets.MethodWallTimeMilliseconds++;
                break;
            case "project-time":
                budgets.ProjectWallTimeMilliseconds++;
                break;
            case "parallelism":
                budgets.MaxParallelism = 1;
                break;
            case "expression-depth":
                budgets.MaximumExpressionDepth++;
                current.Artifact = current.Artifact with { MaximumExpressionDepth = budgets.MaximumExpressionDepth };
                break;
            default:
                throw new AssertionException("Unknown fixture budget.");
        }
        current.Save();
        var run = await CompareAsync(baseline, current, failOnRegression: true);
        Assert.That(run.ExitCode, Is.Zero, run.Error);
        Assert.That(run.Error, Is.Empty);
        Assert.That(run.Output, Does.Contain("NOTE Verification budgets differ;")
            .And.Contain("0 changes; 0 regressions."));
    }

    [Test]
    public async Task DifferentWorkerVersionRemainsComparableAndIsDisclosed()
    {
        using var baseline = new StoredEvidenceFixture();
        using var current = new StoredEvidenceFixture();
        current.Response = current.Response with
        {
            Summary = current.Response.Summary with
            {
                Versions = current.Response.Summary.Versions with { WorkerVersion = "next-worker" }
            }
        };
        current.Save();
        var run = await CompareAsync(baseline, current, failOnRegression: true);
        Assert.That(run.ExitCode, Is.Zero, run.Error);
        Assert.That(run.Error, Is.Empty);
        Assert.That(run.Output, Does.Contain("NOTE Worker or API-spec versions differ;")
            .And.Contain("0 changes; 0 regressions."));
    }

    [Test]
    public async Task UnknownEvidenceCannotBeExportedAsARegressionTest()
    {
        using var fixture = new StoredEvidenceFixture();
        var consumer = Path.Combine(fixture.Root, "Consumer");
        Directory.CreateDirectory(consumer);
        var project = Path.Combine(consumer, "Consumer.csproj");
        await File.WriteAllTextAsync(project, "<Project />", TestContext.CurrentContext.CancellationToken);
        var destination = Path.Combine(fixture.Root, "Regression");
        var run = await RunAsync("export-test", "--project", project, "--result", fixture.ResultPath,
            "--claim", fixture.Response.Manifest.Claims[0].ClaimId, "--output", destination);
        Assert.That(run.ExitCode, Is.EqualTo(2));
        Assert.That(run.Output, Is.Empty);
        Assert.That(run.Error, Does.Contain("Only replayed direct scalar postcondition refutations can be exported"));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RecordedScalarRefutationExportsThroughValidatedTransportOnlyWhileSourceStillMatches(bool changeSource)
    {
        using var export = new NUnitExportCoverageTests.ExportFixture();
        using var recorded = new StoredEvidenceFixture(includeMetadata: false);
        var span = export.Claim.Location;
        var line = export.Compilation.SyntaxTrees.Single().GetLineSpan(new TextSpan(span.Start, span.Length)).StartLinePosition;
        var claim = export.Claim with { Location = span with { Line = line.Line + 1, Column = line.Character + 1 } };
        var manifest = new WorkerClaimManifest
        {
            Claims = [claim],
            Callables = [new()
            {
                CallableId = claim.CallableId,
                ClaimIds = [claim.ClaimId],
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [WorkerSelectionReason.DiscoveredPostcondition],
                Location = claim.Location
            }]
        };
        // The recorded result exercises transport and export binding. Source and reference hashes come
        // from the semantic fixture, and the exporter independently rebinds the original contract.
        recorded.Artifact = export.Artifact with
        {
            Manifest = manifest,
            Features = WorkerFeatureSet.Contracts,
            Callables = [new() { CallableId = claim.CallableId, FailureReason = WorkerClaimReason.None }]
        };
        recorded.Response = recorded.Response with
        {
            ClaimResults = [export.Result with { Reason = WorkerClaimReason.None }],
            CallableResults = [new()
            {
                CallableId = claim.CallableId,
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None
            }]
        };
        recorded.Save();
        var admitted = recorded.Load();
        Assert.That(admitted.Response.ClaimResults[0].Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(admitted.Artifact.Compilation.SyntaxTrees[0].Path, Is.EqualTo(export.SourcePath));
        if (changeSource)
        { await File.AppendAllTextAsync(export.SourcePath, "\n// Source changed after the recorded verification.\n", TestContext.CurrentContext.CancellationToken); }

        var run = await RunAsync("export-test", "--project", export.ProjectPath, "--result", recorded.ResultPath,
            "--claim", claim.ClaimId, "--output", export.OutputPath);
        if (changeSource)
        {
            Assert.That(run.ExitCode, Is.EqualTo(2));
            Assert.That(run.Output, Is.Empty);
            Assert.That(run.Error, Does.Contain("Source changed since verification"));
            Assert.That(Directory.Exists(export.OutputPath), Is.False);
        }
        else
        {
            Assert.That(run.ExitCode, Is.Zero, run.Error);
            Assert.That(run.Error, Is.Empty);
            Assert.That(run.Output, Does.Contain("NUnit regression project exported.").And.Contain("Consumer code was not executed."));
            var generated = await File.ReadAllTextAsync(Path.Combine(export.OutputPath, "Regression.cs"), TestContext.CurrentContext.CancellationToken);
            var project = XDocument.Load(Path.Combine(export.OutputPath, "SharpProof.Regression.csproj"));
            Assert.That(generated, Does.Contain("global::@Api.@Run(_argument0)").And.Contain("_sharpProofResult")
                .And.Contain("The original postcondition must hold.").And.Not.Contain("Contract.Result"));
            Assert.That(project.Descendants("ProjectReference").Single().Attribute("Include")!.Value, Is.EqualTo(export.ProjectPath));
            var compilation = export.Compilation.AddReferences(MetadataReference.CreateFromFile(typeof(Assert).Assembly.Location))
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(generated));
            Assert.That(compilation.GetDiagnostics().Where(static diagnostic =>
            {
                ArgumentNullException.ThrowIfNull(diagnostic);
                return diagnostic.Severity == DiagnosticSeverity.Error;
            }), Is.Empty);
        }
        Assert.That(Directory.GetDirectories(export.Root, ".sharpproof-export-*"), Is.Empty);
    }

    [TestCase("explain")]
    [TestCase("compare")]
    [TestCase("export-test")]
    public async Task EveryEvidenceCommandRejectsResponseProvenanceMismatchBeforeRenderingOrExport(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var fixture = new StoredEvidenceFixture();
        fixture.WriteResponse(fixture.Response with { RequestHash = new string('a', 64) });
        var destination = Path.Combine(fixture.Root, "Regression");
        var run = command switch
        {
            "explain" => await RunAsync("explain", "--result", fixture.ResultPath),
            "compare" => await RunAsync("compare", "--baseline", fixture.ResultPath, "--current", fixture.ResultPath),
            _ => await RunAsync("export-test", "--project", Path.Combine(fixture.Root, "Consumer.csproj"),
                "--result", fixture.ResultPath, "--claim", "claim:captured", "--output", destination)
        };
        Assert.That(run.ExitCode, Is.EqualTo(2));
        Assert.That(run.Output, Is.Empty);
        Assert.That(run.Error, Does.Contain("Evidence validation failed:").And.Contain("response.request_mismatch"));
        Assert.That(Directory.Exists(destination), Is.False);
    }

    [Test]
    public async Task NamedExplainRejectsCompilerArtifactTamperingEvenWhenJsonRemainsWellFormed()
    {
        using var fixture = new StoredEvidenceFixture();
        await File.AppendAllTextAsync(fixture.ManifestPath, "\n ", TestContext.CurrentContext.CancellationToken);
        var run = await RunAsync("explain", "--result", fixture.ResultPath);
        Assert.That(run.ExitCode, Is.EqualTo(2));
        Assert.That(run.Output, Is.Empty);
        Assert.That(run.Error, Does.Contain("compiler manifest does not match the request digest"));
    }

    [TestCase("{")]
    [TestCase("null")]
    [TestCase("{}")]
    public async Task NamedExplainReportsMalformedOrAbsentStoredResultsAsEvidenceFailures(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var fixture = new StoredEvidenceFixture();
        await File.WriteAllTextAsync(fixture.ResultPath, json, TestContext.CurrentContext.CancellationToken);
        var run = await RunAsync("explain", "--result", fixture.ResultPath);
        Assert.That(run.ExitCode, Is.EqualTo(2));
        Assert.That(run.Output, Is.Empty);
        Assert.That(run.Error, Does.StartWith("SharpProof: "));
    }

    private static void SetProven(StoredEvidenceFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        fixture.Response = fixture.Response with
        {
            ClaimResults = [fixture.Response.ClaimResults[0] with
            {
                Outcome = WorkerClaimOutcome.Proven,
                Reason = WorkerClaimReason.None,
                Diagnostic = null
            }],
            CallableResults = [fixture.Response.CallableResults[0] with
            {
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None
            }]
        };
        fixture.Save();
    }

    private static Task<CommandRun> CompareAsync(StoredEvidenceFixture baseline, StoredEvidenceFixture current, bool failOnRegression = false)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(current);
        var arguments = new List<string>
        {
            "compare", "--baseline", baseline.ResultPath, "--current", current.ResultPath,
            "--baseline-request", baseline.RequestPath, "--baseline-compiler-manifest", baseline.ManifestPath,
            "--request", current.RequestPath, "--compiler-manifest", current.ManifestPath
        };
        if (failOnRegression)
        { arguments.Add("--fail-on-regression"); }
        return RunAsync([.. arguments]);
    }

    private static async Task<CommandRun> RunAsync(params string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var exitCode = await ToolApplication.RunAsync(arguments.ToImmutableArray(), output, error,
            UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken);
        return new(exitCode, output.ToString(), error.ToString());
    }

    private static Task<string> UnexpectedDiscovery(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new AssertionException("SDK discovery must not start for evidence commands.");
    }

    private sealed class CommandRun
    {
        internal CommandRun(int exitCode, string output, string error)
        {
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(error);
            ExitCode = exitCode;
            Output = output;
            Error = error;
        }

        internal int ExitCode { get; }
        internal string Output { get; }
        internal string Error { get; }
    }
}
