using System.Collections.Immutable;
using NUnit.Framework;
namespace SharpProof.Test;
[TestFixture]
public sealed class NullableContractVerificationTests {
    public sealed record NullableCase(string Source, string DiagnosticId, bool Expected);
    private const string Nullable = "#nullable enable";
    private const string CodeAnalysis = "#nullable enable\nusing System.Diagnostics.CodeAnalysis;";
    private static IEnumerable<TestCaseData> Cases() {
        yield return Case("AsyncNullableResult_NullReturnDoesNotReportViolation",
            Class("public static async System.Threading.Tasks.Task<string?> GetName()\n{\n    await System.Threading.Tasks.Task.Yield();\n    return null;\n}", Nullable), "SP0041", false);
        yield return Case("NotNullReturn_ExceptionalOnlyExit_DoesNotReport",
            Class("[return: NotNull]\npublic static string? GetName() => throw new System.InvalidOperationException();", CodeAnalysis),
                "SP0041", false);
        yield return Case("NotNullIfNotNull_NullResult_ReportsViolation",
            Class("[return: NotNullIfNotNull(nameof(value))]\npublic static string? Normalize(string? value) => null;", CodeAnalysis),
                "SP0041", true);
        yield return Case("NotNullIfNotNull_ConditionalAccess_DoesNotReport",
            Class("[return: NotNullIfNotNull(nameof(value))]\npublic static string? Normalize(string? value) => value?.Trim();",
                CodeAnalysis), "SP0041", false);
        yield return Case("NotNullIfNotNull_SecondContractViolation_ReportsViolation",
            Class("[return: NotNullIfNotNull(nameof(first))]\n[return: NotNullIfNotNull(nameof(second))]\n" +
                  "public static string? Select(string? first, string? second)\n{\n" +
                  "    if (first is null) return null;\n    return first;\n}", CodeAnalysis),
                "SP0041", true);
        yield return Case("MemberNotNull_AssignedNonNull_DoesNotReport",
            Class("private string? _name;\n\n[MemberNotNull(nameof(_name))]\npublic void Initialize()\n{\n    _name = \"default\";\n}",
                CodeAnalysis, false), "SP0043", false);
        yield return Case("MemberNotNull_ExpressionBodiedConstructorNullAssignment_ReportsViolation",
            Class("private string? _value;\n\n[MemberNotNull(nameof(_value))]\npublic TestClass() => _value = null;", CodeAnalysis, false),
                "SP0043", true);
        yield return Case("MemberNotNull_UnstableProperty_RemainsInconclusive",
            Class("private int _reads;\nprivate string? Current => _reads++ == 0 ? \"value\" : null;\n\n[MemberNotNull(nameof(Current))]\npublic void Initialize() { }", CodeAnalysis, false), "SP0043", false);
        yield return Case("MemberNotNull_AutoPropertyNullAssignment_ReportsViolation",
            Class("private string? Current { get; set; }\n\n[MemberNotNull(nameof(Current))]\npublic void Initialize()\n{\n    Current = null;\n}", CodeAnalysis, false), "SP0043", true);
        yield return Case("NullForgivingOperator_InsideLambdaIsAudited",
            Class("public static System.Func<int> Create()\n{\n    string? value = null;\n    return () => value!.Length;\n}", Nullable),
                "SP0044", true);
        yield return Case("NullForgivingOperator_InUnreachableCodeIsIgnored",
            Class("public static string Get(string? value)\n{\n    if (false)\n    {\n        return value!;\n    }\n    return \"fallback\";\n}", Nullable), "SP0044", false);
        yield return Case("NullForgivingOperator_StaleMemberProofAfterExternalCall_DoesNotReport",
            Class("private string? _value;\n\npublic int Length()\n{\n    if (_value is null) return 0;\n    System.GC.KeepAlive(this);\n    return _value!.Length;\n}",
                Nullable, false), "SP0044", false);
        yield return Case("InferredGuardPostcondition_IsConsumedByCaller",
            Class("public static void Guard(string? value)\n{\n    if (value is null) throw new System.ArgumentNullException(nameof(value));\n}\n\npublic static int Length(string? value)\n{\n    Guard(value);\n    return value!.Length;\n}", Nullable), "SP0044", false);
        yield return Case("NullForgivingOperator_SourcePredicateGuard_DoesNotReport",
            """
            #nullable enable
            public sealed record Failure(string Message);
            public sealed record Outcome<T>(T? Value, Failure? Failure) where T : class {
                public bool IsSuccess => Failure == null;
            }
            public static class Consumer {
                private static Outcome<string> Query() => new(null, new("failed"));
                public static int ErrorLength(System.Threading.CancellationToken cancellationToken) {
                    var outcome = Query();
                    if (!outcome.IsSuccess) {
                        cancellationToken.ThrowIfCancellationRequested();
                        var failure = outcome.Failure!;
                        return failure.Message.Length;
                    }
                    return 0;
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MetadataRecordPredicateGuard_DoesNotReport",
            """
            #nullable enable
            public sealed record Outcome<T>(
                T? Value,
                SharpProof.Symbolic.SharpProofError? Error) where T : class {
                public bool IsSuccess => Error == null;
            }
            public static class Consumer {
                public static int ErrorLength(Outcome<string> outcome) {
                    if (!outcome.IsSuccess) return outcome.Error!.Message.Length;
                    return 0;
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_AliasedConditionalOutputGuard_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var isExact = factory.TryGet(out var expression);
                    return isExact ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CapturedConditionalOutputAlias_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var isExact = factory.TryGet(out var expression);
                    System.Action mutate = () => isExact = true;
                    mutate();
                    return isExact ? expression! : new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_MutatedConditionalOutputTarget_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var isExact = factory.TryGet(out var expression);
                    expression = null;
                    return isExact ? expression! : new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_DirectConditionalOutputIgnoresLaterCapture",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    object? expression = null;
                    System.Action mutateLater = () => expression = null;
                    if (factory.TryGet(out expression)) return expression!;
                    System.GC.KeepAlive(mutateLater);
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_AliasedConditionalOutputInvalidatedAcrossLoop",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                private static void Reset(ref object? value) => value = null;
                public static object Get(IFactory factory) {
                    var isExact = factory.TryGet(out var expression);
                    while (isExact) {
                        System.GC.KeepAlive(expression!);
                        Reset(ref expression);
                    }
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_AliasedConditionalOutputInvalidatedByForIncrementor",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var isExact = factory.TryGet(out var expression);
                    for (; isExact; expression = null) {
                        System.GC.KeepAlive(expression!);
                    }
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_UserEqualityPredicate_RemainsConservative",
            """
            #nullable enable
            public sealed class Failure {
                public string Message => "failed";
                public static bool operator ==(Failure? left, Failure? right) => false;
                public static bool operator !=(Failure? left, Failure? right) => true;
                public override bool Equals(object? value) => ReferenceEquals(this, value);
                public override int GetHashCode() => 0;
            }
            public sealed record Outcome(Failure? Failure) {
                public bool IsSuccess => Failure == null;
            }
            public static class Consumer {
                public static int ErrorLength(Outcome outcome) {
                    if (!outcome.IsSuccess) return outcome.Failure!.Message.Length;
                    return 0;
                }
            }
            """, "SP0044", true);
        yield return Case("NotNullRef_NullCompletion_ReportsViolation",
            Class("public static void Reset([NotNull] ref string? value) => value = null;", CodeAnalysis), "SP0042", true);
        yield return Case("MaybeNullWhen_OppositeBranchMustHonorNonNullAnnotation",
            Class("public static bool TryGet([MaybeNullWhen(false)] out string value)\n{\n    value = null;\n    return true;\n}",
                CodeAnalysis), "SP0042", true);
        yield return Case("MemberNotNullWhen_MatchingNullCompletion_ReportsViolation",
            Class("private string? _value;\n\n[MemberNotNullWhen(true, nameof(_value))]\npublic bool Initialize() => true;", CodeAnalysis,
                false), "SP0043", true);
        yield return Case("NullableDisabled_DoesNotInventReturnContract",
            Class("public static string Name() => null;", "#nullable disable"), "SP0041", false);
        yield return Case("GenericReferenceConstraint_NonNullReturnIsAccepted",
            Class("public static T Create<T>() where T : class, new() => new T();", Nullable), "SP0041", false);
    }
    [TestCaseSource(nameof(Cases))]
    public async Task NullableContractMatrix(NullableCase testCase) {
        var ids = (await AnalyzeAsync(testCase.Source)).Select(static diagnostic => diagnostic.Id);
        Assert.That(ids, testCase.Expected ? Does.Contain(testCase.DiagnosticId) : Does.Not.Contain(testCase.DiagnosticId));
    }
    [TestCase(true)]
    [TestCase(false)]
    public async Task NotNullWhen_MatchingBooleanWithNonNullOutValue_DoesNotReport(bool result) {
        var value = result.ToString().ToLowerInvariant();
        var source = Class("public static bool TryGet([NotNullWhen(" + value +
            ")] out string? value)\n{\n    value = \"value\";\n    return " + value + ";\n}", CodeAnalysis);
        Assert.That(
            (await AnalyzeAsync(source)).Select(static diagnostic => diagnostic.Id),
            Has.None.EqualTo("SP0042").And.None.EqualTo("SP0047"));
    }
    [Test]
    public async Task NotNullWhen_TryGetValueOrAssignedObject_DoesNotReportUnknownContract() {
        var source = Class("""
            public static bool TryGet(
                System.Collections.Generic.Dictionary<string, object> values,
                [NotNullWhen(true)] out object? value)
            {
                if (values.TryGetValue("key", out value))
                    return true;
                value = new object();
                values.Add("fallback", value);
                return true;
            }
            """, CodeAnalysis);
        var diagnostics = await AnalyzeAsync(source);
        Assert.That(
            diagnostics
                .Where(static diagnostic => diagnostic.Id is "SP0042" or "SP0047")
                .Select(static diagnostic =>
                    diagnostic.Id + "@" +
                    (diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1).ToString()),
            Is.Empty);
    }
    [Test]
    public async Task NotNullWhen_DelegatedTryHelper_DoesNotReportUnknownContract() {
        var diagnostics = await AnalyzeAsync(Class("""
            private static bool TryInner([NotNullWhen(true)] out object? value)
            {
                value = new object();
                return true;
            }

            public static bool TryOuter([NotNullWhen(true)] out object? value) =>
                TryInner(out value);
            """, CodeAnalysis));
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Has.None.EqualTo("SP0042").And.None.EqualTo("SP0047"));
    }
    [Test]
    public async Task MaybeNullWhen_DelegatedGenericTryGetValue_DoesNotReportUnknownContract() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;
            internal sealed class Cache<TKey, TValue> where TKey : notnull {
                private readonly Dictionary<TKey, TValue> _entries = new();

                internal bool TryGetValue(
                    TKey key,
                    [MaybeNullWhen(false)] out TValue value) =>
                    _entries.TryGetValue(key, out value);
            }
            """);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Has.None.EqualTo("SP0042").And.None.EqualTo("SP0047"));
    }
    [Test]
    public async Task MaybeNullWhen_BranchControlledGenericTryGetValue_DoesNotReportUnknownContract() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Collections.Generic;
            using System.Diagnostics.CodeAnalysis;
            internal sealed class Cache<TKey, TValue> where TKey : notnull {
                private readonly Dictionary<TKey, TValue> _entries = new();

                internal bool TryGetValue(
                    TKey key,
                    [MaybeNullWhen(false)] out TValue value)
                {
                    if (_entries.TryGetValue(key, out value))
                        return true;
                    value = default;
                    return false;
                }
            }
            """);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Has.None.EqualTo("SP0042").And.None.EqualTo("SP0047"));
    }
    [Test]
    public async Task NotNullWhen_OppositeConstantCompletion_DoesNotReportUnknownContract() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            namespace System.Diagnostics.CodeAnalysis {
                [System.AttributeUsage(System.AttributeTargets.Parameter)]
                internal sealed class NotNullWhenAttribute(bool returnValue) : System.Attribute {
                    public bool ReturnValue { get; } = returnValue;
                }
            }
            namespace Example {
                using System.Diagnostics.CodeAnalysis;
                internal static class Parser {
                    private static bool TryGet([NotNullWhen(true)] out Microsoft.Z3.ReExpr? value)
                    {
                        value = null;
                        return false;
                    }
                }
            }
            """);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Has.None.EqualTo("SP0042").And.None.EqualTo("SP0047"));
    }
    [Test]
    public async Task NotNullWhen_OverloadedEqualityOutput_DoesNotReportUnknownContract() {
        var diagnostics = await AnalyzeAsync(Class("""
            private sealed class Result {
                public static bool operator ==(Result? left, Result? right) => false;
                public static bool operator !=(Result? left, Result? right) => true;
                public override bool Equals(object? value) => ReferenceEquals(this, value);
                public override int GetHashCode() => 0;
            }

            private static bool TryGet([NotNullWhen(true)] out Result? value)
            {
                value = new Result();
                return true;
            }
            """, CodeAnalysis, false));
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Has.None.EqualTo("SP0042").And.None.EqualTo("SP0047"));
    }
    [Test]
    public async Task NotNullWhen_SwitchAssignmentWithObliviousNonNullFlow_DoesNotReportUnknownContract() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            using Microsoft.Z3;
            internal static class Parser {
                private static bool TryGet(
                    Context context,
                    ReExpr seed,
                    [NotNullWhen(true)] out ReExpr? value)
                {
                    value = seed;
                    var quantifier = '*';
                    value = quantifier switch {
                        '*' => context.MkStar(value),
                        _ => context.MkPlus(value)
                    };
                    return true;
                }
            }
            """);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Has.None.EqualTo("SP0042").And.None.EqualTo("SP0047"));
    }
    [ReadmeExample("sp0041-nullable-return-contract")]
    [Test]
    public Task NonNullableReturn_NullLiteral_ReportsViolation() =>
        AssertDiagnosticAsync(Class("public static string GetName() => null;", Nullable), "SP0041");
    [Test]
    public async Task NonNullableReturn_ObjectCreation_DoesNotReportUnknownContract() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            public sealed record Result(int Value);
            public static class Factory {
                public static Result Create() => new Result(1);
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Not.Contain("SP0047"));
    }
    [ReadmeExample("sp0042-nullable-parameter-contract")]
    [Test]
    public Task NotNullWhen_TrueWithNullOutValue_ReportsViolation() => AssertDiagnosticAsync(
        Class("public static bool TryGet([NotNullWhen(true)] out string? value)\n{\n    value = null;\n    return true;\n}", CodeAnalysis),
            "SP0042");
    [ReadmeExample("sp0043-nullable-member-contract")]
    [Test]
    public Task MemberNotNull_EmptyInitializer_ReportsViolation() => AssertDiagnosticAsync(
        Class("private string? _name;\n\n[MemberNotNull(nameof(_name))]\npublic void Initialize() { }", CodeAnalysis, false), "SP0043");
    [ReadmeExample("sp0044-unsafe-null-forgiving")]
    [Test]
    public Task NullForgivingOperator_ReportsUnsafeUse() => AssertDiagnosticAsync(
        Class("public static int Unsafe()\n{\n    string? value = null;\n    return value!.Length;\n}\n\npublic static int Unnecessary(string value) => value!.Length;", Nullable), "SP0044");
    [Test]
    public async Task NullForgivingOperator_GuardedByCorrelatedNullableCondition_DoesNotReport() {
        var diagnostics = await AnalyzeAsync(Class("""
            private readonly record struct Completion(string? ResultExpression);

            public static string Select(Completion completion, bool? expectedResult)
            {
                if (expectedResult.HasValue && completion.ResultExpression == null)
                    return "";
                return expectedResult.HasValue ? completion.ResultExpression! : "";
            }
            """, Nullable));
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Not.Contain("SP0044"));
    }
    [Test]
    public async Task MultipleReturns_ReportOnlyReachableViolatingCompletion() {
        var diagnostics = await AnalyzeAsync(Class(
            "public static string Select(bool valid)\n{\n    if (valid) return \"value\";\n    return null;\n}", Nullable));
        Assert.That(diagnostics.Count(static diagnostic => diagnostic.Id == "SP0041"), Is.EqualTo(1));
    }
    [Test]
    public async Task InterfaceNotNullReturnContractAppliesToImplementation() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IValueSource {
                [return: NotNull]
                string? GetValue();
            }
            public sealed class ValueSource : IValueSource {
                public string? GetValue() => null;
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0041"));
    }
    [Test]
    public async Task AsyncInterfaceNonNullResultContractAppliesToImplementation() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            public interface IValueSource {
                System.Threading.Tasks.Task<string> GetValueAsync();
            }
            public sealed class ValueSource : IValueSource {
                public async System.Threading.Tasks.Task<string?> GetValueAsync() {
                    await System.Threading.Tasks.Task.Yield();
                    return null;
                }
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0041"));
    }
    [Test]
    public async Task InterfaceNotNullIfNotNullContractAppliesToImplementation() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IValueSource {
                [return: NotNullIfNotNull(nameof(input))]
                string? Normalize(string? input);
            }
            public sealed class ValueSource : IValueSource {
                public string? Normalize(string? value) => null;
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0041"));
    }
    [Test]
    public async Task InterfaceNotNullParameterContractAppliesToImplementation() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IValueSource {
                void Reset([NotNull] ref string? input);
            }
            public sealed class ValueSource : IValueSource {
                public void Reset(ref string? value) => value = null;
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0042"));
    }
    [Test]
    public async Task InterfaceNotNullWhenParameterContractAppliesToImplementation() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IValueSource {
                bool TryGet([NotNullWhen(true)] out string? output);
            }
            public sealed class ValueSource : IValueSource {
                public bool TryGet(out string? value) {
                    value = null;
                    return true;
                }
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0042"));
    }
    [Test]
    public async Task InterfaceMaybeNullWhenParameterContractAppliesToImplementation() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IValueSource {
                bool TryGet([MaybeNullWhen(false)] out string output);
            }
            public sealed class ValueSource : IValueSource {
                public bool TryGet(out string value) {
                    value = null;
                    return true;
                }
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0042"));
    }
    [Test]
    public async Task BaseMemberNotNullContractAppliesToOverride() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public abstract class ValueSource {
                protected string? Value;
                [MemberNotNull(nameof(Value))]
                public abstract void Initialize();
            }
            public sealed class EmptyValueSource : ValueSource {
                public override void Initialize() { }
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0043"));
    }
    [Test]
    public async Task HiddenDerivedMemberDoesNotSatisfyBaseMemberNotNullContract() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public abstract class ValueSource {
                protected string? Value;
                [MemberNotNull(nameof(Value))]
                public abstract void Initialize();
            }
            public sealed class HiddenValueSource : ValueSource {
                private new string? Value;
                public override void Initialize() => Value = "derived";
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Contain("SP0043"));
    }
    [Test]
    public async Task BaseMemberAssignmentSatisfiesInheritedMemberNotNullContract() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public abstract class ValueSource {
                protected string? Value;
                [MemberNotNull(nameof(Value))]
                public abstract void Initialize();
            }
            public sealed class InitializedValueSource : ValueSource {
                public override void Initialize() => base.Value = "base";
            }
            """);
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id), Does.Not.Contain("SP0043"));
    }
    private static string Class(string members, string directives, bool isStatic = true) =>
        SemanticTestSource.Class(members, directives).Replace(
            "public class TestClass",
            isStatic ? "public static class TestClass" : "public sealed class TestClass");
    private static TestCaseData Case(string name, string source, string diagnosticId, bool expected) =>
        new TestCaseData(new NullableCase(source, diagnosticId, expected)).SetName(name);
    private static async Task AssertDiagnosticAsync(string source, string diagnosticId) => Assert.That(
        (await AnalyzeAsync(source)).Select(static diagnostic => diagnostic.Id), Does.Contain(diagnosticId));
    private static Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> AnalyzeAsync(string source) =>
        AnalyzerTestHost.GetDiagnosticsAsync(source);
}
