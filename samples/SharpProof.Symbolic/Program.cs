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

var request = new SymbolicQueryRequest(
    SymbolicSourceInput.FromText(source, "Sample.cs"),
    SymbolicQueryTarget.Point(line: 8));
var result = new SymbolicQueryService().Query(request);

Console.WriteLine($"Program points: {result.ProgramPointCount}");
Console.WriteLine($"Invariant: {result.InvariantInfo.MergedInvariantText}");
