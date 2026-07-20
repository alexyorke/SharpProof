using System.Globalization;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Schema;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class DiagnosticExplainPropertyTests
{
    [Test]
    public async Task PurityDiagnostic_IncludesExplainTargetProperties()
    {
        const string sourcePath = "src/ExplainPurity.cs";
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [EnforcePure]
    public void Impure()
    {
        System.Console.WriteLine(""hello"");
    }
}",
            sourcePath: sourcePath);

        var diagnostic = diagnostics.Single(diagnostic => diagnostic.Id == "SP0002");
        AssertExplainTarget(diagnostic, sourcePath);
        Assert.That(diagnostic.Properties["sharpproof.explain.contract"], Is.EqualTo("[EnforcePure]"));
        Assert.That(diagnostic.Properties["sharpproof.explain.proof_status"], Is.EqualTo("not_proven"));
        Assert.That(diagnostic.Properties["sharpproof.explain.unknown_reason"],
            Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task EnsuresDiagnostic_IncludesContractProofStatusAndNormalizedReason()
    {
        const string sourcePath = "src/ExplainEnsures.cs";
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures(""result > 0"")]
    public int Identity()
    {
        return 0;
    }
}",
            sourcePath: sourcePath);

        var diagnostic = diagnostics.Single(diagnostic => diagnostic.Id == "SP0018");
        AssertExplainTarget(diagnostic, sourcePath, "result > 0");
        Assert.That(diagnostic.Properties["sharpproof.explain.contract"], Is.EqualTo("result > 0"));
        Assert.That(diagnostic.Properties["sharpproof.explain.proof_status"],
            Is.EqualTo("proven_false"));
        Assert.That(diagnostic.Properties["sharpproof.explain.unknown_reason"],
            Is.EqualTo("ir_condition_syntactic_false"));
        Assert.That(diagnostic.Properties["sharpproof.ensures.condition"], Is.EqualTo("result > 0"));
        Assert.That(diagnostic.Properties["sharpproof.ensures.proof_status"],
            Is.EqualTo("ProvenFalse"));
        Assert.That(diagnostic.Properties["sharpproof.ensures.failure_reason"],
            Is.EqualTo("ir_condition_syntactic_false"));
        Assert.That(diagnostic.Properties.ContainsKey("sharpproof.ensures.unknown_reason"), Is.False);
    }

    [Test]
    public async Task RequiresDiagnostic_IncludesContractProofStatusAndCallee()
    {
        const string sourcePath = "src/ExplainRequires.cs";
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            @"
#pragma warning disable SP0004
using SharpProof.Attributes;

public static class TestClass
{
    [Requires(""value > 0"")]
    public static int Callee(int value) => value;

    public static int Caller() => Callee(0);
}",
            sourcePath: sourcePath);

        var diagnostic = diagnostics.Single(diagnostic => diagnostic.Id == "SP0027");
        AssertExplainTarget(diagnostic, sourcePath, "value > 0");
        Assert.That(diagnostic.Properties["sharpproof.requires.condition"], Is.EqualTo("value > 0"));
        Assert.That(diagnostic.Properties["sharpproof.requires.proof_status"],
            Is.EqualTo("ProvenFalse"));
        Assert.That(diagnostic.Properties["sharpproof.requires.failure_reason"],
            Is.EqualTo("ir_condition_syntactic_false"));
        Assert.That(diagnostic.Properties["sharpproof.requires.callee"],
            Does.Contain("TestClass.Callee(int)"));
        Assert.That(diagnostic.Properties.ContainsKey("sharpproof.requires.unknown_reason"), Is.False);
    }

    [TestCase(true, TestName = "RequiresUnsupported_PreservesFamilyUnknownEvidence")]
    [TestCase(false, TestName = "EnsuresUnsupported_PreservesFamilyUnknownEvidence")]
    public async Task UnsupportedContract_PreservesFamilyUnknownEvidence(bool requires)
    {
        var attribute = requires ? "Requires" : "Ensures";
        var sourcePath = "src/Explain" + attribute + "Unsupported.cs";
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "#pragma warning disable SP0004\n" +
            "using SharpProof.Attributes;\n" +
            "public static class TestClass\n" +
            "{\n" +
            "    [" + attribute + "(\"value >\")]\n" +
            "    public static int Value(int value) => value;\n" +
            "}\n",
            sourcePath: sourcePath);

        var expectedId = requires
            ? "SP0028"
            : "SP0019";
        var diagnostic = diagnostics.Single(candidate => candidate.Id == expectedId);
        var conditionProperty = requires
            ? "sharpproof.requires.condition"
            : "sharpproof.ensures.condition";
        var proofStatusProperty = requires
            ? "sharpproof.requires.proof_status"
            : "sharpproof.ensures.proof_status";
        var failureReasonProperty = requires
            ? "sharpproof.requires.failure_reason"
            : "sharpproof.ensures.failure_reason";
        var unknownReasonProperty = requires
            ? "sharpproof.requires.unknown_reason"
            : "sharpproof.ensures.unknown_reason";

        Assert.That(diagnostic.Properties[conditionProperty], Is.EqualTo("value >"));
        Assert.That(diagnostic.Properties[proofStatusProperty], Is.EqualTo("Unknown"));
        Assert.That(diagnostic.Properties[failureReasonProperty], Is.EqualTo("condition parse failure"));
        Assert.That(diagnostic.Properties[unknownReasonProperty], Is.EqualTo("condition parse failure"));
        AssertExplainTarget(diagnostic, sourcePath, "value >");
    }

    private static void AssertExplainTarget(
        Diagnostic diagnostic,
        string sourcePath,
        string? expectedImpliedCondition = null)
    {
        Assert.That(diagnostic.Properties[SharpProofEvidenceSchema.DiagnosticVersionProperty],
            Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion.ToString(CultureInfo.InvariantCulture)));
        Assert.That(diagnostic.Properties.ContainsKey("sharpproof.evidence.schema_compatibility"), Is.False);

        var lineSpan = diagnostic.Location.GetLineSpan();
        var line = lineSpan.StartLinePosition.Line + 1;
        var column = lineSpan.StartLinePosition.Character + 1;
        var expectedQuery = "SharpProof.SymbolicCli explain --file \"" +
                            sourcePath +
                            "\" --line " +
                            line.ToString(CultureInfo.InvariantCulture) +
                            " --column " +
                            column.ToString(CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(expectedImpliedCondition))
            expectedQuery += " --implies \"" + expectedImpliedCondition + "\"";

        Assert.That(diagnostic.Properties["sharpproof.explain.file"], Is.EqualTo(sourcePath));
        Assert.That(diagnostic.Properties["sharpproof.explain.line"],
            Is.EqualTo(line.ToString(CultureInfo.InvariantCulture)));
        Assert.That(diagnostic.Properties["sharpproof.explain.column"],
            Is.EqualTo(column.ToString(CultureInfo.InvariantCulture)));
        Assert.That(diagnostic.Properties["sharpproof.explain.query"], Is.EqualTo(expectedQuery));
    }
}
