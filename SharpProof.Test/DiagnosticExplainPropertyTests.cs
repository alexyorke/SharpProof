using System.Linq;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
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
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainUnknownReasonProperty], Is.Not.Null.And.Not.Empty);
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
            AssertExplainTarget(diagnostic, sourcePath, expectedImpliedCondition: "result > 0");
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainContractProperty], Is.EqualTo("result > 0"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainProofStatusProperty], Is.EqualTo("proven_false"));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainUnknownReasonProperty], Is.EqualTo("branch_unreachable"));
        }

        private static void AssertExplainTarget(
            Microsoft.CodeAnalysis.Diagnostic diagnostic,
            string sourcePath,
            string? expectedImpliedCondition = null)
        {
            var lineSpan = diagnostic.Location.GetLineSpan();
            var line = lineSpan.StartLinePosition.Line + 1;
            var column = lineSpan.StartLinePosition.Character + 1;
            var expectedQuery = "SharpProof.SymbolicCli explain --file \"" +
                sourcePath +
                "\" --line " +
                line.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " --column " +
                column.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(expectedImpliedCondition))
            {
                expectedQuery += " --implies \"" + expectedImpliedCondition + "\"";
            }

            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainFileProperty], Is.EqualTo(sourcePath));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainLineProperty], Is.EqualTo(line.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainColumnProperty], Is.EqualTo(column.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ExplainQueryProperty], Is.EqualTo(expectedQuery));
        }
    }
}
