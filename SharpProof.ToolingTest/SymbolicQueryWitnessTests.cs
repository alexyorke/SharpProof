using NUnit.Framework;
using Microsoft.CodeAnalysis;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicQueryWitnessTests {
    [Test]
    public void QueryScopesAndImplication_ExposeReachabilityWitnesses() {
        const string source = """
                              public static class WitnessSample
                              {
                                  public static int Read(int value, string text, int[] values, int index)
                                  {
                                      if (value < 2 || value > 9) return -1;
                                      if (text == null || text.Length < 3) return -2;
                                      if (!text.StartsWith("pre") || !text.EndsWith("end")) return -3;
                                      if (index < 0 || index >= values.Length) return -4;
                                      return values[index] + value;
                                  }
                              }
                              """;
        var targetText = "return values[index] + value;";
        var position = source.IndexOf(targetText, StringComparison.Ordinal);
        var line = FindLine(source, targetText);
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var (tree, compilation) = Compile(source, "WitnessSample.cs");
        var input = SymbolicSourceInput.FromSyntaxTree(tree, compilation);
        var options = new SymbolicQueryOptions(smtAnalysis: smtAnalysis);
        var service = new SymbolicQueryExecutor();

        var point = service.Query(new SymbolicQueryContext(input, SharpProofTargetFactory.AtPosition(position), options));
        var lineResult = service.Query(new SymbolicQueryContext(input, SharpProofTargetFactory.LineNumber(line), options));
        var span = service.Query(new SymbolicQueryContext(
            input,
            SharpProofTargetFactory.Span(position, position + targetText.Length),
            options));
        var allLines = service.Query(new SymbolicQueryContext(input, SharpProofTargetFactory.AllLines(), options));

        var programPoint = point.ProgramPoints.Single();
        Assert.Multiple(() => {
            Assert.That(programPoint.ReachabilityWitness.IsAvailable, Is.True);
            Assert.That(programPoint.ReachabilityWitness.Assignments, Has.Some.Property("SourceName").EqualTo("value"));
        });

        foreach (var result in new[] { point, lineResult, span, allLines }) {
            Assert.That(result.ProgramPoints, Is.Not.Empty);
            Assert.That(result.ProgramPoints.Select(static item => item.ReachabilityWitness), Is.Not.Empty);
        }
        var proof = service.Prove(new SymbolicQueryContext(
            input,
            SharpProofTargetFactory.Point(line, FindColumn(source, position)),
            options),
            "value > 5");
        Assert.That(proof.TruthValue, Is.EqualTo(SymbolicTruthValue.Unknown));
        Assert.That(proof.CounterexampleWitness.IsAvailable, Is.True);
        Assert.That(int.Parse(proof.CounterexampleWitness.Assignments.Single(assignment => assignment.SourceName == "value").Value),
            Is.LessThanOrEqualTo(5));

        var sourcePath = Path.Combine(Path.GetTempPath(), "SharpProof.ProofQuery." + Guid.NewGuid() + ".cs");
        try {
            File.WriteAllText(sourcePath, source);
            var (fileTree, fileCompilation) = Compile(File.ReadAllText(sourcePath), sourcePath);
            var fileProof = service.Prove(new SymbolicQueryContext(
                SymbolicSourceInput.FromSyntaxTree(fileTree, fileCompilation),
                SharpProofTargetFactory.Point(line, FindColumn(source, position)),
                options), "value > 5");
            Assert.That(fileProof.TruthValue, Is.EqualTo(proof.TruthValue));
            Assert.That(fileProof.CounterexampleWitness.IsAvailable, Is.True);
        }
        finally {
            File.Delete(sourcePath);
        }
    }
    [Test]
    public void RuntimeHazardQuery_ExposesInputsThatSatisfyTheTrigger() {
        const string source = """
                              public static class HazardWitness
                              {
                                  public static int Divide(int numerator, int divisor)
                                  {
                                      return numerator / divisor;
                                  }
                              }
                              """;
        using var smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        var (tree, compilation) = Compile(source, "HazardWitness.cs");
        var result = new SymbolicQueryExecutor().QueryRuntimeHazards(new SymbolicQueryContext(
            SymbolicSourceInput.FromSyntaxTree(tree, compilation),
            SharpProofTargetFactory.AllLines(),
            new SymbolicQueryOptions(smtAnalysis: smtAnalysis)),
            new SymbolicRuntimeHazardQueryOptions(
                true,
                new[] { SymbolicRuntimeHazardKind.DivideByZero }));

        var hazard = result.Hazards.Single(hazard => hazard.Kind == SymbolicRuntimeHazardKind.DivideByZero);
        var divisorAssignment = hazard.TriggerWitness.Assignments
            .Single(assignment => assignment.SourceName == "divisor");
        Assert.Multiple(() => {
            Assert.That(hazard.Status, Is.EqualTo(SymbolicRuntimeHazardStatus.Unknown));
            Assert.That(hazard.UnknownReasonInfo.Source, Is.EqualTo(SymbolicUnknownReasonSource.RuntimeHazard));
            Assert.That(hazard.UnknownReasonInfo.Code, Is.EqualTo("runtime_hazard.unknown"));
            Assert.That(hazard.TriggerWitness.IsAvailable, Is.True);
            Assert.That(divisorAssignment.Value, Is.EqualTo("0"));
            Assert.That(result.Hazards.Select(static item => item.TriggerWitness), Does.Contain(hazard.TriggerWitness));
        });
    }
    private static (SyntaxTree Tree, Compilation Compilation) Compile(string source, string filePath) =>
        SymbolicSourceCompilation.Create(
            source,
            filePath,
            SymbolicSourceCompilationKind.Query,
            AnalyzerTestHost.GetTrustedPlatformReferences(),
            default);

    private static int FindLine(string source, string text) {
        var position = source.IndexOf(text, StringComparison.Ordinal);
        return source.Substring(0, position).Count(static character => character == '\n') + 1;
    }
    private static int FindColumn(string source, int position) {
        var lineStart = source.LastIndexOf('\n', Math.Max(0, position - 1));
        return position - lineStart;
    }
}
