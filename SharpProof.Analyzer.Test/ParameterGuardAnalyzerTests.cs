using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.DefensiveCodingAnalyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ParameterGuardAnalyzerTests
{
    [Test]
    public async Task ReportsEveryUnguardedInputAcrossCallableForms()
    {
        const string source = """
            public sealed class Sample(int primary)
            {
                public Sample(int first, out int output) : this(first) { output = 0; }
                public int Method(int value, string text)
                {
                    int Local(int item) => item;
                    System.Func<int, int> closure = (int captured) => captured;
                    System.Func<int, int> simple = input => input;
                    return Local(value) + closure(primary) + simple(value) + text.Length;
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EquivalentTo(ImmutableArray.Create(
                "Parameter 'primary' has no explicit guard",
                "Parameter 'first' has no explicit guard",
                "Parameter 'value' has no explicit guard",
                "Parameter 'text' has no explicit guard",
                "Parameter 'item' has no explicit guard",
                "Parameter 'captured' has no explicit guard",
                "Parameter 'input' has no explicit guard")));
    }

    [Test]
    public async Task AcceptsExplicitRejectingChecksAndGuardHelpers()
    {
        const string source = """
            using System;
            public static class Sample
            {
                public static int Checked(int count, string text, object item)
                {
                    RequirePositive(count);
                    ArgumentNullException.ThrowIfNull(text);
                    return (item ?? throw new ArgumentNullException(nameof(item))).GetHashCode();
                }
                private static void RequirePositive(int value)
                {
                    if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value));
                }
            }
            """;

        Assert.That(await Analyze(source), Is.Empty);
    }

    [Test]
    public async Task OrdinaryUseAndNestedGuardDoNotValidateOuterParameter()
    {
        const string source = """
            using System;
            public static class Sample
            {
                public static int Read(string text)
                {
                    Func<int, int> nested = value =>
                    {
                        if (text is null) throw new ArgumentNullException(nameof(text));
                        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
                        return value;
                    };
                    return nested(text.Length);
                }
            }
            """;

        var diagnostics = await Analyze(source);

        Assert.That(diagnostics.Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'text' has no explicit guard")));
    }

    [Test]
    public async Task SkipsAbstractDeclarationsAndGeneratedFiles()
    {
        const string source = """
            public abstract class Sample
            {
                public abstract int Abstract(int value);
                public int Concrete(int value) => value;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic => diagnostic.Id),
            Is.EqualTo(ImmutableArray.Create(ParameterGuardAnalyzer.DiagnosticId)));
        Assert.That(await Analyze(source, "Sample.g.cs"), Is.Empty);
    }

    [Test]
    public async Task SkipsInputsWithNoInvalidValue()
    {
        const string source = """
            using System.Threading;
            public static class Sample
            {
                public static int Read(bool enabled, CancellationToken token, int count) => count;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'count' has no explicit guard")));
    }

    [Test]
    public async Task ExplicitTotalDomainExemptionAppliesOnlyToAnnotatedParameter()
    {
        const string source = """
            namespace SharpProof.Internal
            {
                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                internal sealed class AllValuesValidAttribute : System.Attribute
                {
                    public AllValuesValidAttribute(string reason)
                    {
                        if (reason is null) throw new System.ArgumentNullException(nameof(reason));
                    }
                }
            }
            public static class Sample
            {
                public static int Read(
                    [SharpProof.Internal.AllValuesValid("Any integer is accepted.")] int any,
                    int limited) => any + limited;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'limited' has no explicit guard")));
    }

    private static async Task<Diagnostic[]> Analyze(string source, string path = "input.cs")
    {
        var compilation = AnalyzerTestHost.CreateCompilation(source, [], filePath: path);
        var analyzers = compilation.WithAnalyzers([new ParameterGuardAnalyzer()]);
        return (await analyzers.GetAnalyzerDiagnosticsAsync()).ToArray();
    }

}
