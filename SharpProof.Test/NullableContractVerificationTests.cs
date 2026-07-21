using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class NullableContractVerificationTests
{
    [Test]
    public async Task AsyncNullableResult_NullReturnDoesNotReportViolation()
    {
        const string source = """
                              #nullable enable
                              using System.Threading.Tasks;
                              public static class Sample
                              {
                                  public static async Task<string?> GetName()
                                  {
                                      await Task.Yield();
                                      return null;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SP0041"));
    }

    [ReadmeExample("sp0041-nullable-return-contract")]
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
            Does.Contain("SP0041"));
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
            Does.Not.Contain("SP0041"));
    }

    [ReadmeExample("sp0042-nullable-parameter-contract")]
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
            Does.Contain("SP0042"));
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
            Does.Not.Contain("SP0042"));
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
            Does.Contain("SP0041"));
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
            Does.Not.Contain("SP0041"));
    }

    [ReadmeExample("sp0043-nullable-member-contract")]
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
            Does.Contain("SP0043"));
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
            Does.Not.Contain("SP0043"));
    }

    [Test]
    public async Task MemberNotNull_ExpressionBodiedConstructorNullAssignment_ReportsViolation()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public sealed class Sample
                              {
                                  private string? _value;

                                  [MemberNotNull(nameof(_value))]
                                  public Sample() => _value = null;
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0043"));
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
            Does.Not.Contain("SP0043"));
    }

    [Test]
    public async Task MemberNotNull_AutoPropertyNullAssignment_ReportsViolation()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public sealed class Sample
                              {
                                  private string? Current { get; set; }

                                  [MemberNotNull(nameof(Current))]
                                  public void Initialize()
                                  {
                                      Current = null;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0043"));
    }

    [ReadmeExample("sp0044-unsafe-null-forgiving")]
    [Test]
    public async Task NullForgivingOperator_ReportsUnsafeUse()
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

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0044"));
    }

    [Test]
    public async Task NullForgivingOperator_InsideLambdaIsAudited()
    {
        const string source = """
                              #nullable enable
                              using System;
                              public static class Sample
                              {
                                  public static Func<int> Create()
                                  {
                                      string? value = null;
                                      return () => value!.Length;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0044"));
    }

    [Test]
    public async Task NullForgivingOperator_InUnreachableCodeIsIgnored()
    {
        const string source = """
                              #nullable enable
                              public static class Sample
                              {
                                  public static string Get(string? value)
                                  {
                                      if (false)
                                      {
                                          return value!;
                                      }

                                      return "fallback";
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SP0044"));
    }

    [Test]
    public async Task InferredGuardPostcondition_IsConsumedByCaller()
    {
        const string source = """
                              #nullable enable
                              using System;
                              public static class Sample
                              {
                                  public static void Guard(string? value)
                                  {
                                      if (value is null) throw new ArgumentNullException(nameof(value));
                                  }

                                  public static int Length(string? value)
                                  {
                                      Guard(value);
                                      return value!.Length;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SP0044"));
    }

    [Test]
    public async Task NotNullRef_NullCompletion_ReportsViolation()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public static class Sample
                              {
                                  public static void Reset([NotNull] ref string? value) => value = null;
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0042"));
    }

    [Test]
    public async Task MaybeNullWhen_OppositeBranchMustHonorNonNullAnnotation()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public static class Sample
                              {
                                  public static bool TryGet([MaybeNullWhen(false)] out string value)
                                  {
                                      value = null;
                                      return true;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0042"));
    }

    [Test]
    public async Task MemberNotNullWhen_MatchingNullCompletion_ReportsViolation()
    {
        const string source = """
                              #nullable enable
                              using System.Diagnostics.CodeAnalysis;
                              public sealed class Sample
                              {
                                  private string? _value;

                                  [MemberNotNullWhen(true, nameof(_value))]
                                  public bool Initialize() => true;
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0043"));
    }

    [Test]
    public async Task MultipleReturns_ReportOnlyReachableViolatingCompletion()
    {
        const string source = """
                              #nullable enable
                              public static class Sample
                              {
                                  public static string Select(bool valid)
                                  {
                                      if (valid) return "value";
                                      return null;
                                  }
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Count(static diagnostic =>
            diagnostic.Id == "SP0041"), Is.EqualTo(1));
    }

    [Test]
    public async Task NullableDisabled_DoesNotInventReturnContract()
    {
        const string source = """
                              #nullable disable
                              public static class Sample
                              {
                                  public static string Name() => null;
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SP0041"));
    }

    [Test]
    public async Task GenericReferenceConstraint_NonNullReturnIsAccepted()
    {
        const string source = """
                              #nullable enable
                              public static class Sample
                              {
                                  public static T Create<T>() where T : class, new() => new T();
                              }
                              """;

        var diagnostics = await AnalyzeAsync(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SP0041"));
    }

    private static Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> AnalyzeAsync(
        string source,
        ImmutableDictionary<string, string>? options = null)
    {
        return AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            globalOptions: options);
    }
}
