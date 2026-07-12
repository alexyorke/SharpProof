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

    private static Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> AnalyzeAsync(string source)
    {
        return AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            analyzerFeatures: AnalyzerFeatures.Nullability);
    }
}
