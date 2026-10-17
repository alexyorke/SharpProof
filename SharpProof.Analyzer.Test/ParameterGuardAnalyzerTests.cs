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
    public async Task UsedUnderscoreParametersAreInputsRatherThanDiscards()
    {
        const string source = """
            public static class Sample
            {
                public static int Read(string _) => _.Length;
                public static System.Func<string, int> Used() => _ => _.Length;
                public static System.Func<string, int> Ignored() => _ => 0;
                public static System.Func<string, string, int> Discarded() => (_, _) => 0;
            }
            """;
        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter '_' has no explicit guard", "Parameter '_' has no explicit guard")));
    }
    [Test]
    public async Task NullableAnnotationsDoNotEraseExplicitInputConstraints()
    {
        const string source = """
            namespace SharpProof.Attributes
            {
                public sealed class NotNullAttribute : System.Attribute { }
                public sealed class PositiveAttribute : System.Attribute { }
            }
            public static class Sample
            {
                public static string? MissingNullGuard([SharpProof.Attributes.NotNull] string? text) => text;
                public static int? MissingRangeGuard([SharpProof.Attributes.Positive] int? count) => count;
                public static int? Validated([SharpProof.Attributes.Positive] int? value)
                {
                    if (value is null or <= 0) throw new System.ArgumentOutOfRangeException(nameof(value));
                    return value;
                }
            }
            """;
        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EquivalentTo(ImmutableArray.Create("Parameter 'text' has no explicit guard", "Parameter 'count' has no explicit guard")));
    }
    [Test]
    public async Task IdentityMethodsWithDeclaredConstraintsStillNeedGuards()
    {
        const string source = """
            [System.AttributeUsage(System.AttributeTargets.Parameter)]
            public sealed class PositiveAttribute : System.Attribute { }
            public static class Sample
            {
                public static int Missing([Positive] int value) => value;
                public static int Validated([Positive] int value)
                {
                    if (value <= 0) throw new System.ArgumentOutOfRangeException(nameof(value));
                    return value;
                }
            }
            """;
        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }
    [Test]
    public async Task UnusedValuesAreTotalDomainButScopedIdsStillRequireValidation()
    {
        const string source = """
            public readonly struct Payload { public int Count { get; } }
            public readonly struct ScopedIrId { public int Scope { get; } }
            public sealed record Holder(ScopedIrId Id);
            public static class Sample
            {
                public static int Ignore(Payload value) => 0;
                public static int IgnoreId(ScopedIrId id) => 0;
                public static int Divide(int value) => value / 2;
            }
            """;
        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EquivalentTo(ImmutableArray.Create("Parameter 'Id' has no explicit guard", "Parameter 'id' has no explicit guard")));
    }
    [Test]
    public async Task RecognizesValidationThroughUnchangedLocalAliases()
    {
        const string source = """
            using System;
            public static class Sample
            {
                public static string Valid(string text)
                {
                    var alias = text;
                    if (alias is null) throw new ArgumentNullException(nameof(text));
                    return alias;
                }
                public static string Invalid(string text)
                {
                    var alias = text;
                    alias = "replacement";
                    if (alias is null) throw new ArgumentNullException(nameof(alias));
                    return text;
                }
            }
            """;
        var diagnostics = await Analyze(source);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'text' has no explicit guard")));
    }
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

        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.GetMessage(CultureInfo.InvariantCulture))),
            Is.EqualTo(ImmutableArray.Create(
                "Parameter 'text' has no explicit guard")));
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

        Assert.That(diagnostics.Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.GetMessage(CultureInfo.InvariantCulture))),
            Is.EqualTo(ImmutableArray.Create("Parameter 'text' has no explicit guard")));
    }

    [Test]
    public async Task SkipsAbstractDeclarationsAndGeneratedFiles()
    {
        const string source = """
            public abstract class Sample
            {
                public abstract int Abstract(int value);
                public int Concrete(int value) => new int[value].Length;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Id)),
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

        Assert.That(await Analyze(source), Is.Empty);
    }

    [Test]
    public async Task NullableReferencesAdmitNullUnlessNullabilityIsSuppressed()
    {
        const string source = """
            public static class Sample
            {
                public static string Optional(string? value) => value ?? string.Empty;
                public static string Forward(string? value) => value!;
                public static int Forced(string? value) => value!.Length;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) :
                diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }
    [Test]
    public async Task NullableValuesRequireChecksWhenExtracted()
    {
        const string source = """
            public static class Sample
            {
                public static int Optional(int? value) => value.GetValueOrDefault();
                public static int Forced(int? value) => value.Value;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) :
                diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }
    [Test]
    public async Task BooleanNullPredicatesValidateTheirParameter()
    {
        const string source = """
            public static class Sample
            {
                public static bool IsMissing(string value) => value is null;
                public static bool IsPresent(string value) => value != null;
                public static bool IsLong(string value) => value.Length > 3;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) :
                diagnostic.GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }
    [Test]
    public async Task NumericInputsUsedAsIndexesSizesOrDivisorsStillNeedGuards()
    {
        const string source = """
            public static class Sample
            {
                public static int Identity(int value) => value;
                public static int Index(int[] values, int index) => values[index];
                public static int Allocate(int length) => new int[length].Length;
                public static int Divide(int value, int divisor) => value / divisor;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.GetMessage(CultureInfo.InvariantCulture))),
            Is.EquivalentTo(ImmutableArray.Create(
                "Parameter 'values' has no explicit guard",
                "Parameter 'index' has no explicit guard",
                "Parameter 'length' has no explicit guard",
                "Parameter 'value' has no explicit guard",
                "Parameter 'divisor' has no explicit guard")));
    }

    [Test]
    public async Task ExplicitTotalDomainReasonAppliesOnlyToMarkedParameter()
    {
        const string source = """

            public static class Sample
            {
                public static int Read(
                    /* SPINT001: all values valid - Any integer is accepted. */ int any,
                    int limited) => any + new int[limited].Length;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.GetMessage(CultureInfo.InvariantCulture))),
            Is.EqualTo(ImmutableArray.Create("Parameter 'limited' has no explicit guard")));
    }

    [Test]
    public async Task EnumPassThroughAcceptsAllValuesButIncompleteSwitchNeedsValidation()
    {
        const string source = """
            public enum State { A, B }
            public static class Sample
            {
                public static State Identity(State value) => value;
                public static int Dispatch(State value) => value switch
                {
                    State.A => 1,
                    State.B => 2
                };
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }
    [Test]
    public async Task IntrinsicStructsAcceptTheirEntireValueDomain()
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            public static class Sample
            {
                public static object ReturnPair((string? Name, int Count) pair) => pair;
                public static object ReturnEntry(KeyValuePair<string, int> entry) => entry;
                public static Guid ReturnGuid(Guid id) => id;
                public static System.Numerics.BigInteger ReturnInteger(System.Numerics.BigInteger value) => value;
                public static nint ReturnNative(nint value) => value;
            }
            """;

        Assert.That(await Analyze(source), Is.Empty);
    }
    [Test]
    public async Task BlockBodiedPredicatesValidateMentionedParameters()
    {
        const string source = """
            public enum State { A, B }
            public static class Sample
            {
                public static bool IsKnown(State value) { return value is State.A or State.B; }
                public static bool IsMissing(string value) { return value is null; }
                public static bool IsLong(string value) { return value.Length > 3; }
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.GetMessage(CultureInfo.InvariantCulture))),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }
    [Test]
    public async Task NUnitAssertionsValidateExpectedValuesOnlyInTestAssemblies()
    {
        const string source = """
            using System;
            using NUnit.Framework;
            public static class Sample
            {
                public static void Verify(DateTime expected, int index)
                {
                    Assert.That(DateTime.MinValue, Is.EqualTo(expected));
                    _ = new int[index];
                }
            }
            """;

        Assert.That((await Analyze(source, testAssembly: true)).Select(static diagnostic =>
                diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.GetMessage(CultureInfo.InvariantCulture))),
            Is.EqualTo(ImmutableArray.Create("Parameter 'index' has no explicit guard")));
        Assert.That((await Analyze(source)).Length, Is.EqualTo(2));
    }
    [Test]
    public async Task StateChecksValidateDefaultArraysAndNullableExtraction()
    {
        const string source = """
            using System.Collections.Immutable;
            public static class Sample
            {
                public static int Normalize(ImmutableArray<int> values)
                {
                    if (values.IsDefault) return 0;
                    return values.Length;
                }
                public static int Extract(int? value)
                {
                    if (value.HasValue) return value.Value;
                    return 0;
                }
                public static int Unsafe(ImmutableArray<int> values) => values.Length;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'values' has no explicit guard")));
    }
    [Test]
    public async Task ValueIdentityAndAutoPropertyProjectionsAcceptAllValues()
    {
        const string source = """
            public readonly struct State
            {
                public int Code { get; }
                public int Danger => throw new System.InvalidOperationException();
            }
            public static class Sample
            {
                public static State Identity(State value) => value;
                public static int Project(State value) => value.Code;
                public static int Unsafe(State value) => value.Danger;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }
    [Test]
    public async Task MetadataAutoPropertyProjectionsAcceptAllValuesButComputedGettersDoNot()
    {
        var model = AnalyzerTestHost.CreateCompilation("""
            public readonly record struct State(int Code)
            {
                public int Danger => throw new System.InvalidOperationException();
            }
            """, []).WithAssemblyName("ProjectionModel");
        using var image = new MemoryStream();
        Assert.That(model.Emit(image).Success, Is.True);
        var compilation = AnalyzerTestHost.CreateCompilation("""
            public static class Sample
            {
                public static int Project(State value) => value.Code;
                public static int Unsafe(State value) => value.Danger;
            }
            """, [], [MetadataReference.CreateFromImage(image.ToArray())]);
        var diagnostics = await compilation.WithAnalyzers([new ParameterGuardAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        Assert.That(diagnostics.Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }
    [Test]
    public async Task InstanceValidationAndTrailingLambdaReasonsAreRecognized()
    {
        const string source = """
            public readonly struct Item { public void Validate() { } }
            public static class Sample
            {
                public static Item Read(Item value) { value.Validate(); return value; }
                public static System.Func<string, int> Accepted() =>
                    value /* SPINT001: all values valid - the callback ignores its input */ => 1;
                public static System.Func<string, int> Unsafe() => other => other.Length;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'other' has no explicit guard")));
    }
    [Test]
    public async Task NumericChecksDistinguishUncheckedAndTotalDispatchFromUnsafeOperations()
    {
        const string source = """
            public static class Sample
            {
                public static int Identity(int value) { checked { return value; } }
                public static int Hash(int value) => unchecked(value * 397);
                public static int Dispatch(int value) => value switch { 0 => 0, _ => 1 };
                public static int Checked(int value) => checked(value + 1);
                public static System.Func<int, int> Divide() => value => 1 / value;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard", "Parameter 'value' has no explicit guard")));
    }
    [Test]
    public async Task CapturedNullableInputDoesNotMakeAnUnrelatedResultSuppressionUnsafe()
    {
        const string source = """
            public static class Sample
            {
                public static string Observe(string? value) => Run(() => value!)!.ToString()!;
                private static object? Run(System.Func<string> callback)
                {
                    System.ArgumentNullException.ThrowIfNull(callback);
                    return new object();
                }
            }
            """;

        Assert.That(await Analyze(source), Is.Empty);
    }
    [Test]
    public async Task EmptyMarkersAndExplicitFailureResultsNeedNoArtificialRejection()
    {
        const string source = """
            public readonly struct Tag { }
            public readonly struct Token { public int Kind { get; } }
            public static class Sample
            {
                public static int Marker(Tag value) => value.GetHashCode();
                public static string? Resolve(Token value) => value.Kind switch { 1 => "x", _ => null };
                public static string? Conditional(Token value) => value.Kind == 1 ? "x" : null;
                public static int Unsafe(Token value) => 10 / value.Kind;
            }
            """;

        Assert.That((await Analyze(source)).Select(static diagnostic =>
                (diagnostic ?? throw new System.ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }
    private static async Task<Diagnostic[]> Analyze(string source, string path = "input.cs", bool testAssembly = false)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = AnalyzerTestHost.CreateCompilation(source, [],
            testAssembly ? [MetadataReference.CreateFromFile(typeof(Assert).Assembly.Location)] : null,
            filePath: path);
        if (testAssembly)
        {
            compilation = compilation.WithAssemblyName("AnalyzerFixture.Test");
        }
        var analyzers = compilation.WithAnalyzers([new ParameterGuardAnalyzer()]);
        return (await analyzers.GetAnalyzerDiagnosticsAsync()).ToArray();
    }

}
