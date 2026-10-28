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
            """, [], [MetadataReference.CreateFromImage(ImmutableArray.Create<byte>(image.GetBuffer().AsSpan(0, checked((int)image.Length))))]);
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
    [Test]
    public async Task OperatorsConversionsAndAnonymousMethodsRequireTheirOwnGuards()
    {
        const string source = """
            public sealed class Box
            {
                public static Box operator +(Box left, Box right)
                {
                    if (left is null) throw new System.ArgumentNullException(nameof(left));
                    if (right is null) throw new System.ArgumentNullException(nameof(right));
                    return left;
                }
                public static explicit operator string(Box value) => value.ToString();
                public static System.Func<string, int> Callback() => delegate(string text) { return text.Length; };
            }
            """;
        Assert.That(Messages(await Analyze(source)), Is.EquivalentTo(ImmutableArray.Create("Parameter 'value' has no explicit guard", "Parameter 'text' has no explicit guard")));
    }

    [Test]
    public async Task SwitchRejectionsAndNullableStateChecksAreValidation()
    {
        const string source = """
            public static class Sample
            {
                public static int Switch(string text)
                {
                    switch (text) { case null: throw new System.ArgumentNullException(nameof(text)); default: return text.Length; }
                }
                public static int Branch(int? value)
                {
                    int result = 0;
                    if (value.HasValue) result = value.Value;
                    return result;
                }
                public static int Conditional(int? value)
                {
                    int result = value.HasValue ? value.Value : 0;
                    return result;
                }
            }
            """;
        Assert.That(await Analyze(source), Is.Empty);
    }

    [Test]
    public async Task ParenthesesPreserveNullableDereferenceAndValueProjectionRules()
    {
        const string source = """
            #nullable enable
            public readonly struct Payload { public readonly int Count; }
            public static class Sample
            {
                public static Payload Identity(Payload value) => ((value));
                public static int Field(Payload value) => value.Count;
                public static int Missing(string? text) => (text!).Length;
            }
            """;
        Assert.That(Messages(await Analyze(source)), Is.EqualTo(ImmutableArray.Create("Parameter 'text' has no explicit guard")));
    }

    [Test]
    public async Task SafeIntegralAndUnboundedDivisionsDifferFromDecimalOverflow()
    {
        const string source = """
            public static class Sample
            {
                public static long Signed(long value) => value / 2L;
                public static uint Unsigned(uint value) => value / 2U;
                public static ulong Wide(ulong value) => value / 2UL;
                public static System.Numerics.BigInteger Unbounded(System.Numerics.BigInteger value) => value / 2;
                public static decimal Overflow(decimal value) => value / 0.5M;
            }
            """;
        Assert.That(Messages(await Analyze(source)), Is.EqualTo(ImmutableArray.Create("Parameter 'value' has no explicit guard")));
    }

    [Test]
    public async Task OnlyRecognizedGenericValidationCallsGuardInputs()
    {
        const string source = """
            #nullable enable
            namespace Foreign { public sealed class NotNullAttribute : System.Attribute { } }
            public static class Sample
            {
                public static string? Transport([Foreign.NotNull] string? text) => text;
                public static int Validated(string text) { Check<string>(text); return text.Length; }
                private static void Check<T>(T value) { if (value is null) throw new System.ArgumentNullException(nameof(value)); }
                public static int Missing(string text) { (new System.Action<string>(System.Console.WriteLine))(text); return text.Length; }
                public static int Generic<T>(T value) where T : struct => value.GetHashCode();
            }
            """;
        Assert.That(Messages(await Analyze(source)), Is.EquivalentTo(ImmutableArray.Create("Parameter 'text' has no explicit guard", "Parameter 'value' has no explicit guard")));
    }

    [Test]
    public async Task CopiedNumericInputsStillRequireGuardsForHazardousUses()
    {
        const string source = """
            public static class Sample
            {
                public static int Allocate(int length)
                {
                    var alias = length;
                    return new int[alias].Length;
                }
                public static int Divide(int divisor)
                {
                    var first = divisor;
                    var second = first;
                    return 1 / second;
                }
                public static int PassThrough(int value)
                {
                    var alias = value;
                    return alias;
                }
            }
            """;
        Assert.That(Messages(await Analyze(source, validateCompilation: true)), Is.EquivalentTo(ImmutableArray.Create(
            "Parameter 'length' has no explicit guard",
            "Parameter 'divisor' has no explicit guard")));
    }

    [Test]
    public async Task CopiedNullableInputsStillRequireGuardsBeforeSuppressedDereferences()
    {
        const string source = """
            #nullable enable
            public static class Sample
            {
                public static int Missing(string? text)
                {
                    var first = text;
                    var alias = first;
                    return alias!.Length;
                }
                public static int Guarded(string? text)
                {
                    var alias = text;
                    System.ArgumentNullException.ThrowIfNull(alias);
                    return alias.Length;
                }
                public static int Independent(string? text)
                {
                    var alias = text;
                    alias = "replacement";
                    return alias!.Length;
                }
            }
            """;
        Assert.That(Messages(await Analyze(source, validateCompilation: true)),
            Is.EqualTo(ImmutableArray.Create("Parameter 'text' has no explicit guard")));
    }

    [Test]
    public async Task OpaqueCallResultsAreNotParameterValueAliases()
    {
        const string source = """
            #nullable enable
            public static class Sample
            {
                public static int Numeric(int value)
                {
                    var result = Compute(value);
                    return result.Value;
                }
                public static int Nullable(string? text)
                {
                    var result = Create(() => text);
                    return result!.Length;
                }
                private static int? Compute(int value) => value;
                private static string? Create(System.Func<string?> callback)
                {
                    System.ArgumentNullException.ThrowIfNull(callback);
                    return "";
                }
            }
            """;
        Assert.That(await Analyze(source, validateCompilation: true), Is.Empty);
    }


    private static ImmutableArray<string> Messages(IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        return diagnostics.Select(static diagnostic =>
            (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).GetMessage(CultureInfo.InvariantCulture)).ToImmutableArray();
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(string source, string path = "input.cs", bool testAssembly = false, bool validateCompilation = false)
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
        if (validateCompilation)
        {
            Assert.That(compilation.GetDiagnostics().Where(static diagnostic => (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Severity == DiagnosticSeverity.Error), Is.Empty);
        }
        var analyzers = compilation.WithAnalyzers([new ParameterGuardAnalyzer()]);
        var diagnostics = await analyzers.GetAnalyzerDiagnosticsAsync();
        Assert.That(diagnostics.Any(static diagnostic => (diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))).Id == "AD0001"), Is.False,
            string.Join(Environment.NewLine, diagnostics));
        return diagnostics;
    }

}
