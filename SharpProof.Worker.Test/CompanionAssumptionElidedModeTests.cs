using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompanionAssumptionElidedModeTests
{
    [TestCase(false, true)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(true, false)]
    public async Task CompilerElidedContractModePreservesCompanionFilters(bool companion, bool valid)
    {
        var clauses = "Contract.Assume(value > 0); Contract.Ensures(Contract.Result<int>() > 0);";
        var source = "using SharpProof.Attributes; public static class Subject { public static int Target(int value) { " +
            (companion ? "" : clauses) + " return " + (valid ? "value" : "-1") + "; } } " +
            (companion ? "[ContractFor(typeof(Subject))] public static class Specification { public static int Target(int value) { " +
                clauses + " return 0; } }" : "");
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp12);
        Assert.That(parseOptions.PreprocessorSymbolNames, Does.Not.Contain(Contract.ConditionalSymbol));
        var compilation = CSharpCompilation.Create("CompanionElidedMode",
            [CSharpSyntaxTree.ParseText(source, parseOptions, "Subject.cs")], TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug,
                nullableContextOptions: NullableContextOptions.Enable));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(valid ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), claim.Reason.ToString());
        Assert.That(claim.Assumptions.Single().Kind, Is.EqualTo(WorkerAssumptionKind.UserAssume));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }
}
