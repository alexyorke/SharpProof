using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class NullableContractVerificationTests
{
    [Test]
    public async Task NonNullableReturn_NullLiteral_ReportsViolation()
    {
        const string source = """
                              #nullable enable
                              public static class Sample
                              {
                                  public static string GetName() => null;
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain(SharpProofDiagnostics.NullableReturnContractViolationId));
    }

    [Test]
    public async Task NotNullReturn_ExceptionalOnlyExit_DoesNotReport()
    {
        const string source = """
                              #nullable enable
                              using System;
                              using System.Diagnostics.CodeAnalysis;
                              public static class Sample
                              {
                                  [return: NotNull]
                                  public static string? GetName() => throw new InvalidOperationException();
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain(SharpProofDiagnostics.NullableReturnContractViolationId));
    }

    [Test]
    public async Task NotNullWhen_TrueWithNullOutValue_ReportsViolation()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public static class Sample
                              {
                                  public static bool TryGet([NotNullWhen(true)] out string? value)
                                  {
                                      value = null;
                                      return true;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain(SharpProofDiagnostics.NullableParameterPostconditionViolationId));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task NotNullWhen_MatchingBooleanWithNonNullOutValue_DoesNotReport(bool result)
    {
        var source = $$"""
                       #nullable enable
                       using System.Diagnostics.CodeAnalysis;
                       public static class Sample
                       {
                           public static bool TryGet([NotNullWhen({{result.ToString().ToLowerInvariant()}})] out string? value)
                           {
                               value = "value";
                               return {{result.ToString().ToLowerInvariant()}};
                           }
                       }
                       """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain(SharpProofDiagnostics.NullableParameterPostconditionViolationId));
    }

    [Test]
    public async Task NotNullIfNotNull_NullResult_ReportsViolation()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public static class Sample
                              {
                                  [return: NotNullIfNotNull(nameof(value))]
                                  public static string? Normalize(string? value) => null;
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain(SharpProofDiagnostics.NullableReturnContractViolationId));
    }

    [Test]
    public async Task NotNullIfNotNull_ConditionalAccess_DoesNotReport()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public static class Sample
                              {
                                  [return: NotNullIfNotNull(nameof(value))]
                                  public static string? Normalize(string? value) => value?.Trim();
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain(SharpProofDiagnostics.NullableReturnContractViolationId));
    }

    [Test]
    public async Task MemberNotNull_EmptyInitializer_ReportsViolation()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public sealed class Sample
                              {
                                  private string? _name;

                                  [MemberNotNull(nameof(_name))]
                                  public void Initialize()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain(SharpProofDiagnostics.NullableMemberContractViolationId));
    }

    [Test]
    public async Task MemberNotNull_AssignedNonNull_DoesNotReport()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public sealed class Sample
                              {
                                  private string? _name;

                                  [MemberNotNull(nameof(_name))]
                                  public void Initialize()
                                  {
                                      _name = "default";
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain(SharpProofDiagnostics.NullableMemberContractViolationId));
    }

    [Test]
    public async Task MemberNotNull_UnstableProperty_RemainsInconclusive()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public sealed class Sample
                              {
                                  private int _reads;
                                  private string? Current => _reads++ == 0 ? "value" : null;

                                  [MemberNotNull(nameof(Current))]
                                  public void Initialize()
                                  {
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain(SharpProofDiagnostics.NullableMemberContractViolationId));
    }

    [Test]
    public async Task NullForgivingOperator_TracksUnsafeAndUnnecessaryUses()
    {
        const string source = """
                              #nullable enable
                              public static class Sample
                              {
                                  public static int Unsafe()
                                  {
                                      string? value = null;
                                      return value!.Length;
                                  }

                                  public static int Unnecessary(string value) => value!.Length;
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);
        var ids = diagnostics.Select(static diagnostic => diagnostic.Id).ToArray();

        Assert.That(ids, Does.Contain(SharpProofDiagnostics.UnsafeNullForgivingOperatorId));
        Assert.That(ids, Does.Contain(SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId));
    }

    [Test]
    public async Task NullFacts_UnknownCallInvalidatesMemberButNotCapturedLocal()
    {
        const string source = """
                              #nullable enable
                              public sealed class Sample
                              {
                                  private string? _value;
                                  private void Unknown() { _value = null; }

                                  public int Member()
                                  {
                                      if (_value is null) return 0;
                                      Unknown();
                                      return _value!.Length;
                                  }

                                  public int Local()
                                  {
                                      var value = _value;
                                      if (value is null) return 0;
                                      Unknown();
                                      return value!.Length;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);
        var suppressions = diagnostics
            .Where(static diagnostic => diagnostic.Id is
                SharpProofDiagnostics.UnsafeNullForgivingOperatorId or
                SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId)
            .OrderBy(static diagnostic => diagnostic.Location.SourceSpan.Start)
            .ToArray();

        Assert.That(
            suppressions.Count(static diagnostic =>
                diagnostic.Id == SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId),
            Is.EqualTo(1));
        Assert.That(
            suppressions.Single(static diagnostic =>
                diagnostic.Id == SharpProofDiagnostics.UnnecessaryNullForgivingOperatorId)
                .Location.GetLineSpan().StartLinePosition.Line,
            Is.GreaterThan(14));
    }

    private static Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> AnalyzeAsync(string source)
    {
        return AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            analyzerFeatures: AnalyzerFeatures.Nullability);
    }
}
