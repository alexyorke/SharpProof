using System.Runtime.InteropServices;
using System.Text.Json;
using SharpProof.Symbolic;
var expectation = args.SingleOrDefault() ?? "Required";
if (expectation is not ("Required" or "Graceful")) {
    Console.Error.WriteLine("Expected Required or Graceful as the native SMT expectation.");
    return 64;
}
const string source = """
                      public static class NativeSmtProbe
                      {
                          public static int Read(int left, int middle, int right)
                          {
                              if (left >= middle || middle >= right)
                              {
                                  return 0;
                              }

                              return left;
                          }
                      }
                      """;
using var session = SharpProofAnalysisSession.FromText(source, "NativeSmtProbe.cs");
var result = session.Analyze(new SharpProofAnalysisRequest(
    new SharpProofTarget(SharpProofTargetKind.Point, Line: 10, Column: 9),
    SharpProofAnalysisFacet.ProofFacts,
    "left < right"));
var proofsHold = result.Status == SharpProofQueryStatus.Succeeded;
var unknownProofCount = result.UnknownReasons.Length;
var nativeAvailable = proofsHold;
Console.WriteLine(JsonSerializer.Serialize(new {
    runtimeIdentifier = RuntimeInformation.RuntimeIdentifier,
    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    expectation,
    nativeAvailable,
    healthState = result.Status.ToString(),
    lastFailureCode = result.Error?.Code ?? string.Empty,
    executedQueryCount = result.ProofFacts.Length,
    proofCount = result.ProofFacts.Length,
    unknownProofCount,
    proofsHold
}));
if (result.ProofFacts.Length == 0) {
    Console.Error.WriteLine("The package probe did not execute an SMT-backed query.");
    return 2;
}
if (expectation == "Required") {
    if (!nativeAvailable || !proofsHold) {
        Console.Error.WriteLine("A bundled native Z3 asset was required, but SMT proofs were unavailable.");
        return 3;
    }
    return 0;
}
if (nativeAvailable) return proofsHold ? 0 : 4;
var stableFallback = result.Status == SharpProofQueryStatus.Unknown && unknownProofCount > 0;
if (!stableFallback) {
    Console.Error.WriteLine("SMT was unavailable without the documented permanent conservative fallback.");
    return 5;
}
return 0;
