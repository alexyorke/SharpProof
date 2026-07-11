using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
using SharpProof.Attributes;

namespace SharpProof.Test;

[TestFixture]
public sealed class PurityPolicyAuditTests
{
    [Test]
    public void ConfigurationRegistry_MarksEveryPurityPolicyKnob()
    {
        var expected = new Dictionary<string, PurityPolicyImpact>(StringComparer.Ordinal)
        {
            ["sharpproof_attribute_stub_namespaces"] =
                PurityPolicyImpact.TrustsPure |
                PurityPolicyImpact.ForcesImpure |
                PurityPolicyImpact.ChangesAttributeIdentity,
            ["sharpproof_enable_effect_summary_json"] =
                PurityPolicyImpact.TrustsPure |
                PurityPolicyImpact.ForcesImpure |
                PurityPolicyImpact.EnablesGeneratedOverrides,
            ["sharpproof_known_impure_methods"] = PurityPolicyImpact.ForcesImpure,
            ["sharpproof_known_impure_namespaces"] = PurityPolicyImpact.ForcesImpure,
            ["sharpproof_known_impure_types"] = PurityPolicyImpact.ForcesImpure,
            ["sharpproof_known_pure_methods"] = PurityPolicyImpact.TrustsPure,
            ["sharpproof_purity_profile"] = PurityPolicyImpact.ChangesStrictness
        };

        var actual = AnalyzerConfigurationOptionRegistry.PurityPolicyOptions
            .ToDictionary(static option => option.Key, static option => option.PurityPolicyImpact,
                StringComparer.Ordinal);

        Assert.That(actual, Is.EqualTo(expected));
        Assert.That(
            AnalyzerConfigurationOptionRegistry.PurityPolicyOptions,
            Has.All.Property(nameof(AnalyzerConfigurationOption.Scope))
                .EqualTo(AnalyzerConfigurationScope.GlobalOnly));
    }

    [Test]
    public void BoundaryRegistry_IsStableUniqueAndBackedByPublicAttributeTargets()
    {
        var boundaries = PurityPolicyAuditRegistry.BoundarySources;
        Assert.That(
            boundaries.Select(static boundary => boundary.Id),
            Is.EqualTo(new[]
            {
                "member_impure_attribute",
                "member_pure_external_attribute",
                "recognized_external_pure_attribute",
                "assembly_impure_attribute",
                "assembly_pure_external_attribute",
                "additional_generated_summary",
                "built_in_generated_summary",
                "built_in_purity_catalog"
            }));
        Assert.That(
            boundaries.Select(static boundary => boundary.Id).Distinct(StringComparer.Ordinal).Count(),
            Is.EqualTo(boundaries.Length));
        Assert.That(
            boundaries.Select(static boundary => boundary.DecisionStage),
            Is.Ordered.Ascending);
        Assert.That(boundaries, Has.All.Property(nameof(PurityBoundaryPolicy.DecisionRule)).Not.Empty);

        AssertAttributeBoundary(typeof(ImpureAttribute), "member_impure_attribute", "assembly_impure_attribute");
        AssertAttributeBoundary(
            typeof(PureExternalAttribute),
            "member_pure_external_attribute",
            "assembly_pure_external_attribute");

        void AssertAttributeBoundary(Type attributeType, string memberPolicyId, string assemblyPolicyId)
        {
            var usage = attributeType.GetCustomAttributes(typeof(AttributeUsageAttribute), false)
                .OfType<AttributeUsageAttribute>()
                .Single();
            Assert.That(usage.ValidOn.HasFlag(AttributeTargets.Method), Is.True, attributeType.FullName);
            Assert.That(usage.ValidOn.HasFlag(AttributeTargets.Constructor), Is.True, attributeType.FullName);
            Assert.That(usage.ValidOn.HasFlag(AttributeTargets.Property), Is.True, attributeType.FullName);
            Assert.That(usage.ValidOn.HasFlag(AttributeTargets.Assembly), Is.True, attributeType.FullName);
            Assert.That(boundaries.Single(boundary => boundary.Id == memberPolicyId).Source,
                Is.EqualTo(attributeType.FullName));
            Assert.That(boundaries.Single(boundary => boundary.Id == assemblyPolicyId).Source,
                Is.EqualTo(attributeType.FullName));
        }
    }

    [Test]
    public async Task ExactConfiguredImpureMethod_WinsOverConfiguredPureMethod()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public static class Boundary
            {
                public static int Value() => 1;
            }

            public sealed class Consumer
            {
                [EnforcePure]
                public int Read() => Boundary.Value();
            }
            """,
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_known_pure_methods", "Boundary.Value()")
                .Add("sharpproof_known_impure_methods", "Boundary.Value()")
                .Add("sharpproof_suggest_missing_enforce_pure", "false"));

        Assert.That(diagnostics.Any(static diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.True);
    }

    [Test]
    public async Task ExactConfiguredPureMethod_ExemptsConfiguredImpureNamespace()
    {
        var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            namespace Boundary
            {
                public static class Api
                {
                    public static int Value() => 1;
                }
            }

            public sealed class Consumer
            {
                [EnforcePure]
                public int Read() => Boundary.Api.Value();
            }
            """,
            ImmutableDictionary<string, string>.Empty
                .Add("sharpproof_known_impure_namespaces", "Boundary")
                .Add("sharpproof_known_pure_methods", "Boundary.Api.Value()")
                .Add("sharpproof_suggest_missing_enforce_pure", "false"));

        Assert.That(diagnostics.Any(static diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId),
            Is.False);
    }

    [Test]
    public void PurityPolicyDocumentation_CoversEveryAuditedSource()
    {
        var policyDocument = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "purity-policy.md"));

        foreach (var option in AnalyzerConfigurationOptionRegistry.PurityPolicyOptions)
            Assert.That(policyDocument, Does.Contain("`" + option.Key + "`"), option.Key);

        foreach (var boundary in PurityPolicyAuditRegistry.BoundarySources)
            Assert.That(policyDocument, Does.Contain("`" + boundary.Id + "`"), boundary.Id);

        Assert.That(policyDocument, Does.Contain("sharpproof.impurity.catalog_source"));
        Assert.That(policyDocument, Does.Contain("[Pure]` and `[EnforcePure]"));
        Assert.That(policyDocument, Does.Contain("currently use the same classification behavior"));
        Assert.That(policyDocument, Does.Contain("SP0025"));
        Assert.That(policyDocument, Does.Contain("SP0032"));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "PLAN.md")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
