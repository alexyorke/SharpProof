using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class CommonBugDataflowAnalyzerTests : CommonBugAnalyzerTestBase
{
    [Test]
    public async Task DefaultReturningLinqResultDereferencedImmediately_Reports()
    {
        const string source = """
                              #nullable disable
                              using System.Collections.Generic;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static int Length(IEnumerable<string> values) =>
                                      values.FirstOrDefault().Length;
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.MaybeNullResultDereferenceId);
    }

    [Test]
    public async Task DefaultReturningLinqResultUsesNullConditional_DoesNotReport()
    {
        const string source = """
                              #nullable enable
                              using System.Collections.Generic;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static int Length(IEnumerable<string> values) =>
                                      values.FirstOrDefault()?.Length ?? 0;
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.MaybeNullResultDereferenceId);
    }

    [Test]
    public async Task IQueryableMaterializedBeforeWhere_Reports()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static IEnumerable<int> Filter(IQueryable<int> values) =>
                                      values.ToList().Where(value => value > 0);
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.PrematureQueryMaterializationId);
    }

    [Test]
    public async Task IQueryableComposedBeforeMaterialization_DoesNotReport()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static List<int> Filter(IQueryable<int> values) =>
                                      values.Where(value => value > 0).ToList();
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.PrematureQueryMaterializationId);
    }

    [Test]
    public async Task DeferredSelectorMutatesCapturedState_Reports()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static IEnumerable<int> Project(IEnumerable<int> values)
                                  {
                                      var count = 0;
                                      return values.Select(value =>
                                      {
                                          count++;
                                          return value;
                                      });
                                  }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.DeferredQuerySideEffectId);
    }

    [Test]
    public async Task PureDeferredSelector_DoesNotReportSideEffect()
    {
        const string source = """
                              using System.Collections.Generic;
                              using System.Linq;
                              public static class Sample
                              {
                                  public static IEnumerable<int> Project(IEnumerable<int> values) =>
                                      values.Select(value => value + 1);
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.DeferredQuerySideEffectId);
    }

    [Test]
    public async Task QueryablePredicateCallsSourceHelper_ReportsTranslationRisk()
    {
        const string source = """
                              using System.Linq;
                              public static class Sample
                              {
                                  private static bool IsPositive(int value) => value > 0;
                                  public static IQueryable<int> Filter(IQueryable<int> values) =>
                                      values.Where(value => IsPositive(value));
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.QueryTranslationRiskId);
    }

    [Test]
    public async Task SystemTextJsonSerializesCyclicSourceType_Reports()
    {
        const string source = """
                              #nullable enable
                              using System.Text.Json;
                              public sealed class Person
                              {
                                  public Person? Friend { get; set; }
                              }
                              public static class Sample
                              {
                                  public static string Write(Person value) => JsonSerializer.Serialize(value);
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.SerializationCycleRiskId);
    }

    [Test]
    public async Task SystemTextJsonCycleWithExplicitOptions_DoesNotReportStructuralRisk()
    {
        const string source = """
                              #nullable enable
                              using System.Text.Json;
                              public sealed class Person
                              {
                                  public Person? Friend { get; set; }
                              }
                              public static class Sample
                              {
                                  public static string Write(Person value, JsonSerializerOptions options) =>
                                      JsonSerializer.Serialize(value, options);
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.SerializationCycleRiskId);
    }

    [Test]
    public async Task SystemTextJsonIgnoresNewtonsoftAttribute_ReportsMismatch()
    {
        const string source = """
                              using System;
                              using System.Text.Json;
                              namespace Newtonsoft.Json
                              {
                                  [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
                                  public sealed class JsonIgnoreAttribute : Attribute { }
                              }
                              public sealed class Person
                              {
                                  [Newtonsoft.Json.JsonIgnore]
                                  public Person? Friend { get; set; }
                              }
                              public static class Sample
                              {
                                  public static string Write(Person value) => JsonSerializer.Serialize(value);
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.SerializerAttributeMismatchId);
    }

    [Test]
    public async Task RequiredOnNonNullableValueType_Reports()
    {
        const string source = """
                              using System.ComponentModel.DataAnnotations;
                              public sealed class Request
                              {
                                  [Required]
                                  public int Count { get; set; }
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.IneffectiveRequiredAttributeId);
    }

    [Test]
    public async Task RequiredOnNullableValueType_DoesNotReport()
    {
        const string source = """
                              using System.ComponentModel.DataAnnotations;
                              public sealed class Request
                              {
                                  [Required]
                                  public int? Count { get; set; }
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.IneffectiveRequiredAttributeId);
    }

    [Test]
    public async Task UncheckedArrayLengthMultiplication_Reports()
    {
        const string source = """
                              public static class Sample
                              {
                                  public static byte[] Allocate(int count, int width) => new byte[count * width];
                              }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.UncheckedAllocationArithmeticId);
    }

    [Test]
    public async Task CheckedArrayLengthMultiplication_DoesNotReport()
    {
        const string source = """
                              public static class Sample
                              {
                                  public static byte[] Allocate(int count, int width) => new byte[checked(count * width)];
                              }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.UncheckedAllocationArithmeticId);
    }

    [Test]
    public async Task SuppressMessageWithoutJustification_ReportsDebt()
    {
        const string source = """
                              using System.Diagnostics.CodeAnalysis;
                              [SuppressMessage("Design", "CA1000")]
                              public sealed class Sample { }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.SuppressionWithoutJustificationId);
    }

    [Test]
    public async Task SuppressMessageWithJustification_DoesNotReportDebt()
    {
        const string source = """
                              using System.Diagnostics.CodeAnalysis;
                              [SuppressMessage("Design", "CA1000", Justification = "Reviewed compatibility surface")]
                              public sealed class Sample { }
                              """;

        AssertMissing(await AnalyzeAsync(source), SharpProofDiagnostics.SuppressionWithoutJustificationId);
    }

    [Test]
    public async Task ExplicitNullableDisable_ReportsAdoptionDebt()
    {
        const string source = """
                              #nullable disable
                              public sealed class Sample { }
                              """;

        AssertHas(await AnalyzeAsync(source), SharpProofDiagnostics.NullableAnalysisDisabledId);
    }

}
