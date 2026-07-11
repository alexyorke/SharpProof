using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class AnalysisTruncationDiagnosticTests
{
    [Test]
    public async Task PurityDiagnostic_ReportsConfiguredStateMergeTruncation()
    {
        const string source = """
                              using System;
                              using SharpProof.Attributes;

                              public sealed class Sample
                              {
                                  [EnforcePure]
                                  public void Visit(bool selectFirst)
                                  {
                                      int first;
                                      int second;
                                      if (selectFirst)
                                      {
                                          first = 1;
                                          second = 2;
                                      }
                                      else
                                      {
                                          first = 3;
                                          second = 4;
                                      }

                                      Console.WriteLine(first + second);
                                  }
                              }
                              """;
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_analysis_max_merged_path_conditions", "1")
                .Add("sharpproof_analysis_max_guard_facts_per_target_per_state", "1"));
        var diagnostic = diagnostics.Single(item => item.Id == SharpProofDiagnostics.PurityNotVerifiedId);

        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.AnalysisTruncatedProperty],
            Is.EqualTo(bool.TrueString));
        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.AnalysisLimitCodesProperty],
            Does.Contain("analysis_limit.merged_path_conditions"));
        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.AnalysisLimitEventsProperty],
            Does.Contain("analysis_limit.merged_path_conditions|1|2|"));
    }
}
