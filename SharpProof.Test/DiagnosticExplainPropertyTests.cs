using System.Globalization;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
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

        var diagnostic = diagnostics.Single(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId);
        AssertExplainTarget(diagnostic, sourcePath);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainContractProperty], Is.EqualTo("[EnforcePure]"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainProofStatusProperty], Is.EqualTo("not_proven"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainUnknownReasonProperty],
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

        var diagnostic = diagnostics.Single(diagnostic => diagnostic.Id == SharpProofDiagnostics.EnsuresNotProvenId);
        AssertExplainTarget(diagnostic, sourcePath, "result > 0");
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainContractProperty], Is.EqualTo("result > 0"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainProofStatusProperty],
            Is.EqualTo("proven_false"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainUnknownReasonProperty],
            Is.EqualTo("ir_condition_syntactic_false"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.EnsuresConditionProperty], Is.EqualTo("result > 0"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.EnsuresProofStatusProperty],
            Is.EqualTo("ProvenFalse"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.EnsuresFailureReasonProperty],
            Is.EqualTo("ir_condition_syntactic_false"));
        Assert.That(diagnostic.Properties.ContainsKey(SharpProofDiagnostics.EnsuresUnknownReasonProperty), Is.False);
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

        var diagnostic = diagnostics.Single(diagnostic => diagnostic.Id == SharpProofDiagnostics.RequiresNotProvenId);
        AssertExplainTarget(diagnostic, sourcePath, "value > 0");
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RequiresConditionProperty], Is.EqualTo("value > 0"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RequiresProofStatusProperty],
            Is.EqualTo("ProvenFalse"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RequiresFailureReasonProperty],
            Is.EqualTo("ir_condition_syntactic_false"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.RequiresCalleeProperty],
            Does.Contain("TestClass.Callee(int)"));
        Assert.That(diagnostic.Properties.ContainsKey(SharpProofDiagnostics.RequiresUnknownReasonProperty), Is.False);
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
            ? SharpProofDiagnostics.RequiresUnsupportedId
            : SharpProofDiagnostics.EnsuresUnsupportedId;
        var diagnostic = diagnostics.Single(candidate => candidate.Id == expectedId);
        var conditionProperty = requires
            ? SharpProofDiagnostics.RequiresConditionProperty
            : SharpProofDiagnostics.EnsuresConditionProperty;
        var proofStatusProperty = requires
            ? SharpProofDiagnostics.RequiresProofStatusProperty
            : SharpProofDiagnostics.EnsuresProofStatusProperty;
        var failureReasonProperty = requires
            ? SharpProofDiagnostics.RequiresFailureReasonProperty
            : SharpProofDiagnostics.EnsuresFailureReasonProperty;
        var unknownReasonProperty = requires
            ? SharpProofDiagnostics.RequiresUnknownReasonProperty
            : SharpProofDiagnostics.EnsuresUnknownReasonProperty;

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
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.EvidenceSchemaVersionProperty],
            Is.EqualTo(SharpProofEvidenceSchema.CurrentVersion.ToString(CultureInfo.InvariantCulture)));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.EvidenceSchemaCompatibilityProperty],
            Is.EqualTo(SharpProofEvidenceSchema.CompatibilityPolicy));

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

        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainFileProperty], Is.EqualTo(sourcePath));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainLineProperty],
            Is.EqualTo(line.ToString(CultureInfo.InvariantCulture)));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainColumnProperty],
            Is.EqualTo(column.ToString(CultureInfo.InvariantCulture)));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainQueryProperty], Is.EqualTo(expectedQuery));
    }
}
