using NUnit.Framework;
using SharpProof.Symbolic;
namespace SharpProof.Test;
[TestFixture]
public sealed class SymbolicQueryWitnessTests {
    private const string WitnessSource = """
        public static class WitnessSample {
            public static int Read(int value, string text, int[] values, int index) {
                if (value < 2 || value > 9) return -1;
                if (text == null || text.Length < 3) return -2;
                if (!text.StartsWith("pre") || !text.EndsWith("end")) return -3;
                if (index < 0 || index >= values.Length) return -4;
                return values[index] + value;
            }
        }
        """;
    [Test]
    public void QueryScopesAndImplication_ExposePublicProofResults() {
        const string marker = "return values[index] + value;";
        var position = WitnessSource.IndexOf(marker, StringComparison.Ordinal);
        var line = WitnessSource.Take(position).Count(static character => character == '\n') + 1;
        var column = position - WitnessSource.LastIndexOf('\n', Math.Max(0, position - 1));
        using var session = SharpProofAnalysisSession.FromText(WitnessSource, "WitnessSample.cs");
        var targets = new SharpProofTarget[] {
            new(SharpProofTargetKind.Position, Position: position),
            new(SharpProofTargetKind.Line, Line: line),
            new(SharpProofTargetKind.Span, SpanStart: position, SpanEnd: position + marker.Length),
            new(SharpProofTargetKind.AllLines)
        };
        foreach (var target in targets) {
            var result = session.Analyze(new SharpProofAnalysisRequest(target, SharpProofAnalysisFacet.ProofFacts));
            Assert.That(result.ProofFacts, Is.Not.Empty);
        }
        var proof = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Point, Line: line, Column: column),
            SharpProofAnalysisFacet.ProofFacts,
            "value > 5"));
        Assert.That(proof.ProofFacts.Single().Status, Is.EqualTo("Unknown"));
        Assert.That(proof.ProofFacts.Single().Counterexample, Does.Contain("value="));
        var sourcePath = Path.Combine(Path.GetTempPath(), "SharpProof.ProofQuery." + Guid.NewGuid() + ".cs");
        try {
            File.WriteAllText(sourcePath, WitnessSource);
            using var fileSession = SharpProofAnalysisSession.FromFile(sourcePath);
            var fileProof = fileSession.Analyze(new SharpProofAnalysisRequest(
                new SharpProofTarget(SharpProofTargetKind.Point, Line: line, Column: column),
                SharpProofAnalysisFacet.ProofFacts,
                "value > 5"));
            Assert.That(fileProof.ProofFacts.Single().Status, Is.EqualTo(proof.ProofFacts.Single().Status));
            Assert.That(fileProof.ProofFacts.Single().Counterexample, Is.Not.Null);
        }
        finally {
            File.Delete(sourcePath);
        }
    }
    [Test]
    public void RuntimeHazardQuery_ExposesPublicCounterexample() {
        const string source = "public static class C { public static int Divide(int numerator,int divisor) => numerator/divisor; }";
        using var session = SharpProofAnalysisSession.FromText(source, "HazardWitness.cs");
        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.AllLines),
            SharpProofAnalysisFacet.RuntimeHazards));
        var hazard = result.Hazards.Single(item => item.Kind == "DivideByZero");
        Assert.Multiple(() => {
            Assert.That(hazard.Status, Is.EqualTo("Unknown"));
            Assert.That(hazard.Counterexample, Does.Contain("divisor=0"));
        });
    }
}
