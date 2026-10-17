using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class GoldenVcRegressionTests
{
    public static IEnumerable<string> Cases()
    {
        return GoldenTest.Cases("vc");
    }

    [TestCaseSource(nameof(Cases))]
    public async Task TypedVerificationPreservesRetiredRegression(string caseName)
    {
        var fixture = GoldenTest.Load("vc", caseName);
        var depthLimited = fixture.Source.StartsWith("// vc-depth-limit: 3", StringComparison.Ordinal);
        var entryDepthLimited = fixture.Source.StartsWith("// vc-depth-limit: 1", StringComparison.Ordinal);
        var maximumDepth = entryDepthLimited ? 1 : depthLimited ? 3 : WorkerBudgets.DefaultMaximumExpressionDepth;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(fixture.Source, maximumDepth);
        if (caseName == "diamond-join")
        {
            AssertDiamondJoinArtifact(artifact);
        }
        using var project = new ShadowTestProject(artifact);
        if (depthLimited || entryDepthLimited)
        {
            project.Request.Budgets.MaximumExpressionDepth = maximumDepth;
        }
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var snapshot = project.Bind();
        var response = await worker.VerifyAsync(project.Request, snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty, string.Join(",", response.Errors.Select(error => error.Code + ":" + error.Message)));
        Assert.That(response.ClaimResults, Is.Not.Empty);
        if (entryDepthLimited)
        {
            Assert.That(response.ClaimResults.Select(result => result.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(response.ClaimResults.Select(result => result.Reason), Is.All.EqualTo(WorkerClaimReason.UnsupportedExpression));
        }
        else if (depthLimited)
        {
            Assert.That(response.ClaimResults.Select(result => result.Outcome),
                Is.EqualTo(new[] { WorkerClaimOutcome.Proven, WorkerClaimOutcome.Unknown }));
            Assert.That(response.ClaimResults[1].Reason, Is.EqualTo(WorkerClaimReason.DeepPostcondition));
        }
        else
        {
            Assert.That(response.ClaimResults.All(result => result.Outcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted), Is.True,
                string.Join(",", response.ClaimResults.Select(result => result.Reason)));
        }
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var rows = response.Manifest.Claims.Zip(response.ClaimResults, (claim, result) =>
            claim.CallableId + " " + result.Outcome + "/" + result.Reason + "/" + result.Vacuity +
            " core=[" + string.Join(",", result.ProofCore) + "]" +
            " assumptions=[" + string.Join(",", result.Assumptions.Select(assumption => assumption.Kind + ":" + assumption.Used)) + "]");
        GoldenTest.Compare(fixture, string.Join("\n", rows) + "\n");
    }

    private static void AssertDiamondJoinArtifact(CompilerManifestArtifact artifact)
    {
        var callable = CompilerManifestArtifactJson.DecodeCallables(
            artifact, cancellationToken: TestContext.CurrentContext.CancellationToken).Single();
        var program = callable.Total!.Program;
        var instructions = program.Blocks.SelectMany(block => block.Instructions).ToArray();
        var diagnostic = string.Join(Environment.NewLine, program.Blocks.Select(block =>
            $"{block.Id}: {string.Join("; ", block.Instructions.Select(instruction =>
                instruction switch
                {
                    IrAssignInstruction assign => $"assign {assign.Target} = {DescribeTerm(assign.Value)}",
                    IrBranchInstruction branch => $"branch {DescribeTerm(branch.Condition)} -> {branch.WhenTrue}/{branch.WhenFalse}",
                    IrGotoInstruction go => $"goto {go.Target}",
                    IrReturnInstruction returned => $"return {(returned.Value is null ? "void" : DescribeTerm(returned.Value))}",
                    _ => instruction.GetType().Name
                }))}"));
        Assert.That(instructions.OfType<IrBranchInstruction>(), Is.Not.Empty,
            "The typed body must retain a control-flow branch." + Environment.NewLine + diagnostic);
        var assignedLiterals = instructions.OfType<IrAssignInstruction>()
            .Select(instruction => IntegerLiteral(instruction.Value))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        Assert.That(assignedLiterals, Does.Contain(3).And.Contain(7),
            "The typed body must assign both diamond values." + Environment.NewLine + diagnostic);
    }

    private static long? IntegerLiteral(IrTerm term)
    {
        return term switch
        {
            IrIntegerTerm integer => integer.Value,
            IrCastTerm cast => IntegerLiteral(cast.Operand),
            _ => null
        };
    }

    private static string DescribeTerm(IrTerm term)
    {
        return term switch
        {
            IrBooleanTerm boolean => $"bool({boolean.Value})",
            IrIntegerTerm integer => $"int{integer.Width}({integer.Value})",
            IrVariableTerm variable => $"var({variable.Variable})",
            IrBinaryTerm binary => $"({DescribeTerm(binary.Left)} {binary.Operator} {DescribeTerm(binary.Right)})",
            IrConditionalTerm conditional =>
                $"({DescribeTerm(conditional.Condition)} ? {DescribeTerm(conditional.WhenTrue)} : {DescribeTerm(conditional.WhenFalse)})",
            IrCastTerm cast => $"cast({DescribeTerm(cast.Operand)})",
            _ => term.Kind.ToString()
        };
    }
}
