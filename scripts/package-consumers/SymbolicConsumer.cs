using System.Runtime.InteropServices;
using System.Text.Json;
using SharpProof.Symbolic;

var expectation = args.SingleOrDefault() ?? "Required";
if (expectation is not ("Required" or "Graceful"))
{
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

using var session = SharpProofAnalysisSession.FromText(
    source,
    "NativeSmtProbe.cs",
    new SharpProofAnalysisOptions(
        enableSmt: true,
        impliedConditions: new[] { "left < middle", "left < right" }));
var response = session.Analyze(SharpProofQuery.Invariant(SharpProofTarget.Point(line: 10, column: 9)));
var result = (SourceQueryPayload)response.Payload!;
var health = result.Smt;
var proofsHold = result.ConditionProofCount == 2 && result.AllConditionsHold;
var unknownProofCount = result.UnknownProofCount;
var nativeAvailable = health.State == "Ready";

Console.WriteLine(JsonSerializer.Serialize(new
{
    runtimeIdentifier = RuntimeInformation.RuntimeIdentifier,
    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    expectation,
    nativeAvailable,
    healthState = health.State,
    health.LastFailureCode,
    health.ExecutedQueryCount,
    proofCount = result.ConditionProofCount,
    unknownProofCount,
    proofsHold
}));

if (health.ExecutedQueryCount == 0)
{
    Console.Error.WriteLine("The package probe did not execute an SMT-backed query.");
    return 2;
}

if (expectation == "Required")
{
    if (!nativeAvailable || !proofsHold)
    {
        Console.Error.WriteLine("A bundled native Z3 asset was required, but SMT proofs were unavailable.");
        return 3;
    }

    return 0;
}

if (nativeAvailable) return proofsHold ? 0 : 4;

var stableFallback = health.State == "PermanentlyUnavailable" &&
                     health.LastFailureCode is
                         "smt_native_library_missing" or
                         "smt_native_library_incompatible" or
                         "smt_platform_unsupported" or
                         "smt_initialization_failure" &&
                     unknownProofCount > 0;
if (!stableFallback)
{
    Console.Error.WriteLine("SMT was unavailable without the documented permanent conservative fallback.");
    return 5;
}

return 0;
