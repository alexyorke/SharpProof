using System;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class BclSpecificationRegressionTests
{
    [Test]
    public async Task NonThrowingNullableAndPureFrameworkMembersArePure()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            """
            using System;
            using SharpProof.Attributes;

            public static class Fixture {
                [EnforcePure]
                public static bool HasValue(int? value) => value.HasValue;

                [EnforcePure]
                public static int DefaultValue(int? value) =>
                    value.GetValueOrDefault(5);

                [EnforcePure]
                public static int Coalesce(int? value) => value ?? 0;

                [EnforcePure]
                public static bool CommonMath(int left, int right) =>
                    Math.Max(left, right) >= Math.Min(left, right);

                [EnforcePure]
                public static bool CommonString(string value) =>
                    string.IsNullOrEmpty(value) || value[0] == 'x';
            }
            """,
            "effects",
            ["SP0002"]);

        Assert.That(diagnostics, Is.Empty);
    }

    [TestCase("value.Value")]
    [TestCase("value.HasValue ? value.Value : 0")]
    public async Task NullableValueRemainsConservativeUntilItsGuardIsModeled(string expression)
    {
        ArgumentException.ThrowIfNullOrEmpty(expression);
        // Nullable.Value can consult resource-message culture callbacks on its
        // empty path. The advisory effect model has no relationship between
        // HasValue and Value, so even a guarded unwrap retains that boundary.
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            $$"""
            using SharpProof.Attributes;
            public static class Fixture {
                [EnforcePure]
                public static int Unwrap(int? value) => {{expression}};
            }
            """,
            "effects",
            ["SP0002"]);

        AnalyzerTestHost.AssertIds(diagnostics, "SP0002", 1);
        AnalyzerTestHost.AssertMessageContains(diagnostics.Single(), "Unwrap");
    }
}
