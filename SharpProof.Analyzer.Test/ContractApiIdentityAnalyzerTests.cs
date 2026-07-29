using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ContractApiIdentityAnalyzerTests
{
    [Test]
    public async Task SourceShadowedRuntimeClauseIsVisiblyIncomplete()
    {
        const string source =
            """
            namespace SharpProof.Attributes {
                public static class Contract {
                    public static void Requires(bool condition) {
                        System.Console.WriteLine(condition);
                    }
                    public static void Ensures(bool condition) {
                        System.Console.WriteLine(condition);
                    }
                    public static void Assume(bool condition) {
                        System.Console.WriteLine(condition);
                    }
                }
            }
            public static class Subject {
                public static int Read(int value) {
                    SharpProof.Attributes.Contract.Ensures(value > 0);
                    return value;
                }
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRejected(diagnostics, "Read");
    }

    [Test]
    public async Task SourceShadowedClosedAttributeIsVisiblyIncomplete()
    {
        const string source =
            """
            namespace SharpProof.Attributes {
                [System.AttributeUsage(
                    System.AttributeTargets.Parameter |
                    System.AttributeTargets.ReturnValue)]
                public sealed class NotNullAttribute :
                    System.Attribute {
                }
            }
            public static class Subject {
                [return: SharpProof.Attributes.NotNull]
                public static string Read() => "value";
            }
            """;

        var diagnostics = await Analyze(source);

        AssertRejected(diagnostics, "Read");
    }

    [Test]
    public async Task SourceShadowedEffectAttributeIsVisiblyIncomplete()
    {
        const string source =
            """
            namespace SharpProof.Attributes {
                [System.AttributeUsage(
                    System.AttributeTargets.Method)]
                public sealed class EnforcePureAttribute :
                    System.Attribute {
                }
            }
            public static class Subject {
                [SharpProof.Attributes.EnforcePure]
                public static async System.Threading.Tasks.Task Read<T>() {
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            """;

        var diagnostics = await Analyze(
            source,
            features: "effects");

        AssertRejected(diagnostics, "Read");
    }

    [Test]
    public async Task UnrelatedLookalikeAttributeRemainsUnselected()
    {
        const string source =
            """
            namespace Lookalike {
                public sealed class NotNullAttribute :
                    System.Attribute {
                }
            }
            public static class Subject {
                [return: Lookalike.NotNull]
                public static string Read() => "value";
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task MatchingPackageAttributeRemainsAdmitted()
    {
        const string source =
            """
            using SharpProof.Attributes;
            public static class Subject {
                [return: NotNull]
                public static string Read() => "value";
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(diagnostics, Is.Empty);
    }

    private static Task<ImmutableArray<Diagnostic>> Analyze(
        string source,
        string features = "contracts")
    {
        return AnalyzerTestHost.AnalyzeAsync(
            source,
            mode: null,
            enabledIds: ["SP0047"],
            profile: "advisory",
            features: features);
    }

    private static void AssertRejected(
        ImmutableArray<Diagnostic> diagnostics,
        string method)
    {
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(["SP0047"]));
        var message = diagnostics.Single().GetMessage(
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.That(
            message,
            Does.Contain(method)
                .And.Contain("ContractApiIdentityRejected"));
    }
}
