using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class InferredContractSuggestionTests
{
    private static readonly ImmutableHashSet<string> SuggestionIds =
        ImmutableHashSet.Create(
            SharpProofDiagnostics.SuggestZeroAllocationsId,
            SharpProofDiagnostics.SuggestAllowedCapabilitiesId,
            SharpProofDiagnostics.SuggestExpectedComplexityId,
            SharpProofDiagnostics.SuggestExceptionContractId,
            SharpProofDiagnostics.SuggestEnsuresId,
            SharpProofDiagnostics.SuggestRequiresId,
            SharpProofDiagnostics.SuggestNullableContractId);

    [Test]
    public async Task Suggestions_AreSilentByDefault()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public static class C { public static int Identity(int value) => value; }",
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false"));

        Assert.That(diagnostics.Where(IsSuggestion), Is.Empty);
    }

    [Test]
    public async Task Suggestions_EmitStableHighConfidenceCandidatesForEveryFamily()
    {
        var zeroAllocation = SingleSuggestion(
            await GetSuggestionsAsync(
                "public static class C { public static int Identity(int value) => value; }",
                "zero-allocations"),
            SharpProofDiagnostics.SuggestZeroAllocationsId);
        AssertSuggestion(
            zeroAllocation,
            "zero-allocations",
            "global::SharpProof.Attributes.ZeroAllocations",
            "high");

        var capabilities = SingleSuggestion(
            await GetSuggestionsAsync(
                "using System; public static class C { public static void Write() => Console.WriteLine(1); }",
                "capabilities"),
            SharpProofDiagnostics.SuggestAllowedCapabilitiesId);
        AssertSuggestion(
            capabilities,
            "capabilities",
            "global::SharpProof.Attributes.AllowedCapabilities(" +
            "global::SharpProof.Attributes.SharpProofCapability.IO | " +
            "global::SharpProof.Attributes.SharpProofCapability.Console)",
            "high");

        var complexity = SingleSuggestion(
            await GetSuggestionsAsync(
                """
                public static class C
                {
                    public static int Work(int n)
                    {
                        var sum = 0;
                        for (var i = 0; i < n; i++)
                        for (var j = 0; j < n; j++)
                            sum += i + j;
                        return sum;
                    }
                }
                """,
                "complexity"),
            SharpProofDiagnostics.SuggestExpectedComplexityId);
        AssertSuggestion(
            complexity,
            "complexity",
            "global::SharpProof.Attributes.ExpectedComplexity(" +
            "global::SharpProof.Attributes.ComplexityKind.Quadratic)",
            "high");

        var exceptionContract = SingleSuggestion(
            await GetSuggestionsAsync(
                "public static class C { public static int Identity(int value) => value; }",
                "exceptions"),
            SharpProofDiagnostics.SuggestExceptionContractId);
        AssertSuggestion(
            exceptionContract,
            "exceptions",
            "global::SharpProof.Attributes.DoesNotThrow",
            "high");

        var ensures = SingleSuggestion(
            await GetSuggestionsAsync(
                "public static class C { public static int Identity(int value) => value; }",
                "ensures"),
            SharpProofDiagnostics.SuggestEnsuresId);
        AssertSuggestion(
            ensures,
            "ensures",
            "global::SharpProof.Attributes.Ensures(\"result == value\")",
            "high");

        var requires = SingleSuggestion(
            await GetSuggestionsAsync(
                """
                using System;
                public static class C
                {
                    public static int Positive(int value)
                    {
                        if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                        return value;
                    }
                }
                """,
                "requires"),
            SharpProofDiagnostics.SuggestRequiresId);
        AssertSuggestion(
            requires,
            "requires",
            "global::SharpProof.Attributes.Requires(\"value > 0\")",
            "high");
    }

    [Test]
    public async Task ExceptionSuggestion_UsesMediumConfidenceOnlyWhenEnabled()
    {
        const string source = """
                              using System;
                              public static class C
                              {
                                  public static void Fail()
                                  {
                                      throw new InvalidOperationException();
                                  }
                              }
                              """;

        var highOnly = await GetSuggestionsAsync(source, "exceptions");
        Assert.That(highOnly, Is.Empty);

        var medium = SingleSuggestion(
            await GetSuggestionsAsync(source, "exceptions", "medium"),
            SharpProofDiagnostics.SuggestExceptionContractId);
        Assert.That(medium.DefaultSeverity, Is.EqualTo(DiagnosticSeverity.Info));
        AssertSuggestion(
            medium,
            "exceptions",
            "global::SharpProof.Attributes.AllowedExceptions(typeof(global::System.InvalidOperationException))",
            "medium");
    }

    [Test]
    public async Task Suggestions_HonorVisibilityScopeAndExistingContracts()
    {
        var scoped = await GetSuggestionsAsync(
            """
            public static class C
            {
                public static int Public(int value) => value;
                private static int Private(int value) => value;
            }
            """,
            "zero-allocations",
            scope: "public");

        Assert.That(scoped, Has.Length.EqualTo(1));
        Assert.That(scoped[0].GetMessage(), Does.Contain("'Public'"));

        var alreadyContracted = await GetSuggestionsAsync(
            """
            using SharpProof.Attributes;
            public static class C
            {
                [ZeroAllocations]
                public static int Identity(int value) => value;
            }
            """,
            "zero-allocations");
        Assert.That(alreadyContracted, Is.Empty);
    }

    [Test]
    public async Task Suggestions_StayConservativeForUnknownSymbolicResults()
    {
        var capabilities = await GetSuggestionsAsync(
            "public static class C { public static void Invoke(dynamic value) => value.Run(); }",
            "capabilities");
        Assert.That(capabilities, Is.Empty);

        var complexity = await GetSuggestionsAsync(
            """
            public static class C
            {
                public static int Step(int value) => value + 1;

                public static int Work(int n)
                {
                    var i = 0;
                    while (i < n) i = Step(i);
                    return i;
                }
            }
            """,
            "complexity");
        Assert.That(complexity.Any(diagnostic => diagnostic.GetMessage().Contains("'Work'", StringComparison.Ordinal)),
            Is.False);
    }

    [Test]
    public async Task Suggestions_InferNullableReturnAndGuardParameterContracts()
    {
        var returnSuggestion = SingleSuggestion(
            await GetSuggestionsAsync(
                "#nullable enable\npublic static class C { public static string? Name() => \"name\"; }",
                "nullability"),
            SharpProofDiagnostics.SuggestNullableContractId);
        AssertSuggestion(
            returnSuggestion,
            "nullable-return",
            "global::System.Diagnostics.CodeAnalysis.NotNull",
            "high");

        var guardSuggestion = SingleSuggestion(
            await GetSuggestionsAsync(
                """
                #nullable enable
                using System;
                public static class C
                {
                    public static void Guard(string? value)
                    {
                        if (value is null) throw new ArgumentNullException(nameof(value));
                    }
                }
                """,
                "nullability"),
            SharpProofDiagnostics.SuggestNullableContractId);
        AssertSuggestion(
            guardSuggestion,
            "nullable-parameter:value",
            "global::System.Diagnostics.CodeAnalysis.NotNull",
            "high");
    }

    [Test]
    public async Task Suggestions_NormalizeReversedNullGuards()
    {
        const string source = """
                              #nullable enable
                              using System;
                              public static class C
                              {
                                  public static void Guard(string? value)
                                  {
                                      if (null == value) throw new ArgumentNullException(nameof(value));
                                  }
                              }
                              """;

        var nullableSuggestion = SingleSuggestion(
            await GetSuggestionsAsync(source, "nullability"),
            SharpProofDiagnostics.SuggestNullableContractId);
        AssertSuggestion(
            nullableSuggestion,
            "nullable-parameter:value",
            "global::System.Diagnostics.CodeAnalysis.NotNull",
            "high");

        var requiresSuggestion = SingleSuggestion(
            await GetSuggestionsAsync(source, "requires"),
            SharpProofDiagnostics.SuggestRequiresId);
        AssertSuggestion(
            requiresSuggestion,
            "requires",
            "global::SharpProof.Attributes.Requires(\"value != null\")",
            "high");
    }

    private static async Task<Diagnostic[]> GetSuggestionsAsync(
        string source,
        string kinds,
        string minimumConfidence = "high",
        string scope = "all")
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            globalOptions: ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_suggest_missing_enforce_pure", "false")
                .Add("sharpproof_suggest_inferred_contracts", "true")
                .Add("sharpproof_suggest_inferred_contracts_kinds", kinds)
                .Add("sharpproof_suggest_inferred_contracts_minimum_confidence", minimumConfidence)
                .Add("sharpproof_suggest_inferred_contracts_scope", scope),
            sourcePath: "src/Suggestions.cs",
            concurrentAnalysis: true,
            compilationName: "InferredContractSuggestions_" + kinds);
        return diagnostics.Where(IsSuggestion).ToArray();
    }

    private static Diagnostic SingleSuggestion(IEnumerable<Diagnostic> diagnostics, string diagnosticId)
    {
        var result = diagnostics.ToArray();
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0].Id, Is.EqualTo(diagnosticId));
        return result[0];
    }

    private static void AssertSuggestion(
        Diagnostic diagnostic,
        string kind,
        string attribute,
        string confidence)
    {
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.Severity, Is.EqualTo(DiagnosticSeverity.Info));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.SuggestedContractKindProperty],
                Is.EqualTo(kind));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.SuggestedContractAttributeProperty],
                Is.EqualTo(attribute));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.SuggestedContractConfidenceProperty],
                Is.EqualTo(confidence));
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.SuggestedContractEvidenceProperty],
                Is.Not.Empty);
            Assert.That(diagnostic.Properties, Does.ContainKey(SharpProofDiagnostics.BaselineEvidenceKeyProperty));
            Assert.That(diagnostic.Properties, Does.ContainKey(SharpProofDiagnostics.ExplainQueryProperty));
        });
    }

    private static bool IsSuggestion(Diagnostic diagnostic)
    {
        return SuggestionIds.Contains(diagnostic.Id);
    }
}
