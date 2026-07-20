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
var response = session.Analyze(new SharpProofQuery(
    SharpProofQueryKind.Invariant,
    new SharpProofTarget(SharpProofTargetKind.Point, Line: 8)));
var result = (SourceQueryPayload)response.Payload!;

Console.WriteLine($"Program points: {result.ProgramPointCount}");
Console.WriteLine($"Invariant: {result.Invariant}");
