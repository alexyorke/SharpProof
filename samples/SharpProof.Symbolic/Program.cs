using SharpProof.Symbolic;
const string source = """
                      public static class Sample
                      {
                          public static int Abs(int value)
                          {
                              if (value < 0)
                                  value = -value;

                              return value;
                          }
                      }
                      """;
using var session = SharpProofAnalysisSession.FromText(source, "Sample.cs");
var result = session.Analyze(new SharpProofAnalysisRequest(
    new SharpProofTarget(SharpProofTargetKind.Point, Line: 8),
    SharpProofAnalysisFacet.ProofFacts));
Console.WriteLine($"Status: {result.Status}");
foreach (var fact in result.ProofFacts)
    Console.WriteLine($"{fact.Condition}: {fact.Status} ({fact.Reason})");
