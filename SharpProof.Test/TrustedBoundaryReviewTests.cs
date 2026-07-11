using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test;

[TestFixture]
public sealed class TrustedBoundaryReviewTests
{
    private const string ReviewMode = "sharpproof_trusted_boundary_review_mode";
    private const string KnownPureMethods = "sharpproof_known_pure_methods";
    private static readonly string BoundaryValueKey = ConfiguredMemberKeyTestFactory.Method(
        "Boundary",
        "Value",
        "named:System.Int32",
        parameters: new[] { ("none", "named:System.Int32") });
    private static readonly string StringLengthGetterKey = ConfiguredMemberKeyTestFactory.Getter(
        "System.String",
        "Length",
        "named:System.Int32");

    [Test]
    public async Task DefaultMode_DoesNotReportTrustedBoundaries()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(SourceWithConfiguredBoundaryCall);

        Assert.That(ReviewDiagnostics(diagnostics), Is.Empty);
    }

    [Test]
    public async Task UsedMode_ReportsExactConfiguredValueOncePerSymbol()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            SourceWithConfiguredBoundaryCall,
            Options("used", BoundaryValueKey),
            concurrentAnalysis: true);

        var diagnostic = ReviewDiagnostics(diagnostics).Single();
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundarySymbolProperty],
            Is.EqualTo("Boundary.Value(int)"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundarySourceProperty],
            Is.EqualTo("config_known_pure_method"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryValueProperty],
            Is.EqualTo(BoundaryValueKey));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryDispositionProperty],
            Is.EqualTo("applied"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryOverriddenByProperty], Is.Empty);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryClassificationProperty],
            Is.EqualTo("pure"));
    }

    [Test]
    public async Task AllMode_ReportsDirectContractAndOverriddenConfiguredShortcut()
    {
        const string source = """
                              using SharpProof.Attributes;

                              public static class Boundary
                              {
                                  [PureExternal]
                                  public static int Value(int value) => value;
                              }

                              public sealed class Consumer
                              {
                                  [EnforcePure]
                                  public int Read() => Boundary.Value(1);
                              }
                              """;

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            Options("all", BoundaryValueKey));

        var reviewDiagnostics = ReviewDiagnostics(diagnostics);
        Assert.That(reviewDiagnostics, Has.Length.EqualTo(2));

        var direct = BySource(reviewDiagnostics, "member_pure_external_attribute");
        Assert.That(direct.Properties[SharpProofDiagnostics.TrustedBoundaryValueProperty],
            Is.EqualTo("SharpProof.Attributes.PureExternalAttribute"));
        Assert.That(direct.Properties[SharpProofDiagnostics.TrustedBoundaryDispositionProperty],
            Is.EqualTo("applied"));

        var configured = BySource(reviewDiagnostics, "config_known_pure_method");
        Assert.That(configured.Properties[SharpProofDiagnostics.TrustedBoundaryDispositionProperty],
            Is.EqualTo("overridden"));
        Assert.That(configured.Properties[SharpProofDiagnostics.TrustedBoundaryOverriddenByProperty],
            Is.EqualTo("member_pure_external_attribute"));
        Assert.That(configured.Properties[SharpProofDiagnostics.TrustedBoundaryOverrideValueProperty],
            Is.EqualTo("SharpProof.Attributes.PureExternalAttribute"));
    }

    [Test]
    public async Task UsedMode_OmitsShortcutOverriddenByDirectContract()
    {
        const string source = """
                              using SharpProof.Attributes;

                              public static class Boundary
                              {
                                  [PureExternal]
                                  public static int Value(int value) => value;
                              }

                              public sealed class Consumer
                              {
                                  [EnforcePure]
                                  public int Read() => Boundary.Value(1);
                              }
                              """;

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            Options("used", BoundaryValueKey));

        var diagnostic = ReviewDiagnostics(diagnostics).Single();
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundarySourceProperty],
            Is.EqualTo("member_pure_external_attribute"));
    }

    [Test]
    public async Task AssemblyImpure_OverridesRecognizedExternalPureAttribute()
    {
        const string source = """
                              using System;
                              using SharpProof.Attributes;

                              [assembly: Impure]

                              namespace JetBrains.Annotations
                              {
                                  [AttributeUsage(AttributeTargets.Method)]
                                  public sealed class PureAttribute : Attribute { }
                              }

                              public static class Boundary
                              {
                                  [JetBrains.Annotations.Pure]
                                  public static int Value() => 1;
                              }

                              public sealed class Consumer
                              {
                                  [EnforcePure]
                                  public int Read() => Boundary.Value();
                              }
                              """;

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            ImmutableDictionary<string, string>.Empty.Add(ReviewMode, "all"));

        var diagnostic = ReviewDiagnostics(diagnostics).Single();
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundarySourceProperty],
            Is.EqualTo("recognized_external_pure_attribute"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryValueProperty],
            Is.EqualTo("JetBrains.Annotations.PureAttribute"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryDispositionProperty],
            Is.EqualTo("overridden"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryOverriddenByProperty],
            Is.EqualTo("assembly_impure_attribute"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryOverrideValueProperty],
            Is.EqualTo("SharpProof.Attributes.ImpureAttribute"));
    }

    [Test]
    public async Task AssemblyPureExternal_ReportsExactAppliedAttribute()
    {
        const string source = """
                              using SharpProof.Attributes;

                              [assembly: PureExternal]

                              public static class Boundary
                              {
                                  public static int Value() => 1;
                              }

                              public sealed class Consumer
                              {
                                  [EnforcePure]
                                  public int Read() => Boundary.Value();
                              }
                              """;

        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            ImmutableDictionary<string, string>.Empty.Add(ReviewMode, "used"));

        var diagnostic = ReviewDiagnostics(diagnostics).Single();
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundarySourceProperty],
            Is.EqualTo("assembly_pure_external_attribute"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryValueProperty],
            Is.EqualTo("SharpProof.Attributes.PureExternalAttribute"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryDispositionProperty],
            Is.EqualTo("applied"));
    }

    [Test]
    public async Task AdditionalGeneratedPure_ReportsExactAppliedSummaryPath()
    {
        var summary = GeneratedPurityTestSupport.CreatePuritySummaryJson(
            typeof(string).Assembly.Location,
            "System.String.get_Length()",
            "pure",
            "[]");
        const string summaryPath = "trusted.SharpProof.EffectSummary.json";
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public sealed class Consumer
            {
                [EnforcePure]
                public int Read(string value) => value.Length;
            }
            """,
            ImmutableDictionary<string, string>.Empty.Add(ReviewMode, "used"),
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(summaryPath, summary)));

        var diagnostic = ReviewDiagnostics(diagnostics).Single();
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundarySourceProperty],
            Is.EqualTo("additional_generated_summary"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryValueProperty],
            Is.EqualTo(summaryPath));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.TrustedBoundaryDispositionProperty],
            Is.EqualTo("applied"));
    }

    [Test]
    public async Task StrongerAdditionalSummary_ReportsOverriddenConfiguredShortcut()
    {
        var summary = GeneratedPurityTestSupport.CreatePuritySummaryJson(
            typeof(string).Assembly.Location,
            "System.String.get_Length()",
            "impure",
            "[\"mutable_state_read\"]");
        const string summaryPath = "override.SharpProof.EffectSummary.json";
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public sealed class Consumer
            {
                [EnforcePure]
                public int Read(string value) => value.Length;
            }
            """,
            Options("all", StringLengthGetterKey),
            additionalFiles: ImmutableArray.Create<AdditionalText>(
                new AnalyzerTestHost.InMemoryAdditionalText(summaryPath, summary)));

        var configured = BySource(ReviewDiagnostics(diagnostics), "config_known_pure_method");
        Assert.That(configured.Properties[SharpProofDiagnostics.TrustedBoundaryDispositionProperty],
            Is.EqualTo("overridden"));
        Assert.That(configured.Properties[SharpProofDiagnostics.TrustedBoundaryOverriddenByProperty],
            Is.EqualTo("additional_generated_summary"));
        Assert.That(configured.Properties[SharpProofDiagnostics.TrustedBoundaryOverrideValueProperty],
            Is.EqualTo(summaryPath));
    }

    [Test]
    public async Task InvalidMode_ReportsConfigurationDiagnostic()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            "public sealed class Consumer { }",
            ImmutableDictionary<string, string>.Empty.Add(ReviewMode, "sometimes"));

        var diagnostic = diagnostics.Single(item =>
            item.Id == SharpProofDiagnostics.InvalidAnalyzerConfigurationId &&
            item.Properties[SharpProofDiagnostics.ConfigurationKeyProperty] == ReviewMode);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ConfigurationValueProperty],
            Is.EqualTo("sometimes"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Does.Contain("off, used, all"));
    }

    private static readonly string SourceWithConfiguredBoundaryCall = """
        using SharpProof.Attributes;

        public static class Boundary
        {
            public static int Value(int value) => value;
        }

        public sealed class Consumer
        {
            [EnforcePure]
            public int Read() => Boundary.Value(1) + Boundary.Value(2);
        }
        """;

    private static ImmutableDictionary<string, string> Options(string mode, string configuredPureMethod)
    {
        return ImmutableDictionary<string, string>.Empty
            .Add(ReviewMode, mode)
            .Add(KnownPureMethods, configuredPureMethod)
            .Add("sharpproof_suggest_missing_enforce_pure", "false");
    }

    private static Diagnostic[] ReviewDiagnostics(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics
            .Where(static diagnostic => diagnostic.Id == SharpProofDiagnostics.TrustedBoundaryReviewId)
            .ToArray();
    }

    private static Diagnostic BySource(IEnumerable<Diagnostic> diagnostics, string source)
    {
        return diagnostics.Single(diagnostic =>
            diagnostic.Properties[SharpProofDiagnostics.TrustedBoundarySourceProperty] == source);
    }
}
