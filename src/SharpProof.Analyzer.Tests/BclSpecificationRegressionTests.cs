using System;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class BclSpecificationRegressionTests
{
    [Test]
    public async Task CommonNullableAndPureFrameworkMembersArePure()
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
                public static int CoalesceAndUnwrap(int? value) =>
                    value.HasValue ? value.Value : 0;

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
}
