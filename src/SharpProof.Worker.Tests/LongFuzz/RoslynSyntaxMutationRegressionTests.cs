using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test.LongFuzz;

// Nightly syntax-mutation cases and their ordinary regression. The 16 fixed cases mutate one expression node
// of a typed seed program, then use compilation and the CLR as admission gates.
[TestFixture]
public sealed class RoslynSyntaxMutationRegressionTests
{
    private static readonly ImmutableArray<string> Expressions =
    [
        "x >= 0 ? x : -x",
        "x < 0 ? -x : x",
        "Math.Abs(x)",
        "x switch { < 0 => -x, _ => x }"
    ];

    private static IEnumerable<int> CaseIds => Enumerable.Range(0, 16);

    [TestCaseSource(nameof(CaseIds))]
    public async Task MutatedProgramCompilesRunsAndSelectsExactClaim(int caseId)
    {
        var result = await ProbeCase(caseId);
        await TestContext.Progress.WriteLineAsync($"syntax-pilot {caseId}: variant={result.Variant}, " +
            $"x={result.Anchor}, false={result.FalseClaim}, outcome={result.Outcome}, reason={result.Reason}");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(result.FalseClaim
                ? WorkerClaimOutcome.Refuted : WorkerClaimOutcome.Proven), result.Source);
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None), result.Source);
        }
    }

    internal static async Task<(string Source, int Variant, int Anchor, bool FalseClaim,
        WorkerClaimOutcome Outcome, WorkerClaimReason Reason)> ProbeCase(int caseId)
    {
        if ((uint)caseId >= 16u)
        { throw new ArgumentOutOfRangeException(nameof(caseId)); }
        var variant = caseId % Expressions.Length;
        var anchor = (caseId / Expressions.Length & 1) == 0 ? -3 : 2;
        var falseClaim = (caseId / (Expressions.Length * 2) & 1) != 0;
        var expected = Math.Abs(anchor);
        var source = SourceForCase(caseId);

        var runtime = Compile(source, contractsEnabled: false);
        AssertNoErrors(runtime, source);
        Assert.That(RunClr(runtime, anchor), Is.EqualTo(expected), source);

        var claims = Compile(source, contractsEnabled: true);
        AssertNoErrors(claims, source);
        var discovery = new ClaimManifestBuilder(claims).Build();
        var artifact = CompilerManifestArtifactProducer.Create(claims, "/project", "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(artifact.SpecificationPackIds, Is.Empty);
        var targets = artifact.Manifest.Claims.Where(claim =>
            claim is null ? throw new System.ArgumentNullException(nameof(claim)) : (claim.CallableId.Contains("Target", StringComparison.Ordinal) &&
            claim.Kind == WorkerClaimKind.Postcondition)).ToImmutableArray();
        Assert.That(targets, Has.Length.EqualTo(1), source);

        using var project = new ShadowTestProject(artifact);
        artifact = project.Artifact;
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty, source);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True, source);
        var results = response.ClaimResults.Where(result => result is null ? throw new System.ArgumentNullException(nameof(result)) : (result.ClaimId == targets[0].ClaimId)).ToImmutableArray();
        Assert.That(results, Has.Length.EqualTo(1), source);
        var result = results[0];
        return (source, variant, anchor, falseClaim, result.Outcome, result.Reason);
    }

    internal static string SourceForCase(int caseId)
    {
        if ((uint)caseId >= 16u)
        { throw new ArgumentOutOfRangeException(nameof(caseId)); }
        var variant = caseId % Expressions.Length;
        var anchor = (caseId / Expressions.Length & 1) == 0 ? -3 : 2;
        var falseClaim = (caseId / (Expressions.Length * 2) & 1) != 0;
        var claimed = Math.Abs(anchor) + (falseClaim ? 1 : 0);
        return MutateReturn(anchor, claimed, Expressions[variant]);
    }

    private static string MutateReturn(int anchor, int claimed, string expression)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        var seed = "using System; using SharpProof.Attributes; public static class Subject { " +
            "public static int Target(int x) { Contract.Requires(x == " + anchor + "); " +
            "Contract.Ensures(Contract.Result<int>() == " + claimed + "); " +
            "return x >= 0 ? x : -x; } }";
        var root = CSharpSyntaxTree.ParseText(seed, new CSharpParseOptions(LanguageVersion.CSharp12))
            .GetCompilationUnitRoot();
        var oldExpression = root.DescendantNodes().OfType<ReturnStatementSyntax>()
            .Single().Expression!;
        var replacement = SyntaxFactory.ParseExpression(expression);
        return root.ReplaceNode(oldExpression, replacement.WithTriviaFrom(oldExpression)).ToFullString();
    }

    private static CSharpCompilation Compile(string source, bool contractsEnabled)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        ImmutableArray<string> symbols = contractsEnabled ? [Contract.ConditionalSymbol] : [];
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: symbols),
            "/project/Subject.cs");
        return CSharpCompilation.Create("SyntaxPilot_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static void AssertNoErrors(CSharpCompilation compilation, string source)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        System.ArgumentNullException.ThrowIfNull(source);
        var errors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).ToImmutableArray();
        Assert.That(errors, Is.Empty,
            string.Join(" | ", errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.ToString()))) + "\n" + source);
    }

    private static int RunClr(CSharpCompilation compilation, int anchor)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emission = compilation.Emit(image);
        Assert.That(emission.Success, Is.True, string.Join(" | ", emission.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("roslyn-syntax-pilot", isCollectible: true);
        try
        {
            var target = context.LoadFromStream(image).GetType("Subject")!
                .GetMethod("Target", BindingFlags.Public | BindingFlags.Static)!;
            return (int)target.Invoke(null, [anchor])!;
        }
        finally
        {
            context.Unload();
        }
    }
}
