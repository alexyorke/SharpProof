using System.Collections.Immutable;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
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
        yield return Case("NullForgivingOperator_TwoArmedAssignmentJoinRetainsNullablePath_Reports",
            """
            #nullable enable
            public static class Consumer {
                public static int Length(bool flag) {
                    string? value;
                    if (flag)
                        value = "present";
                    else
                        value = null;
                    return value!.Length;
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SwitchAssignmentJoinRetainsNullablePath_Reports",
            """
            #nullable enable
            public static class Consumer {
                public static int Length(int selection) {
                    string? value;
                    switch (selection) {
                        case 0:
                            value = "present";
                            break;
                        default:
                            value = null;
                            break;
                    }
                    return value!.Length;
                }
            }
            """, "SP0044", true);
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
        yield return Case("NullForgivingOperator_WherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeZeroLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeLastZeroLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.TakeLast(0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeLastNegativeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.TakeLast(-1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeLastUpdatedZeroAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    var count = 1;
                    count = 0;
                    foreach (var item in items.TakeLast(count))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeZeroAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    var count = 0;
                    foreach (var item in items.Take(count))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NamedStaticTakeLastZeroLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.TakeLast(count: 0, source: items))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeLastPositiveLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new[] { new Item(null) }.TakeLast(1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_TakeLastUpdatedPositiveAliasLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var count = 0;
                    count = 1;
                    foreach (var item in new[] { new Item(null) }.TakeLast(count))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomTakeLastZeroLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> TakeLast(IEnumerable<Item> source, int count) =>
                    new[] { new Item(null) };
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in TakeLast(items, 0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_TakeZeroRangeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(0..0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeReversedRangeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(2..1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeReversedFromEndRangeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(^1..^2))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeAliasedReversedRangeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    var range = new Range(Index.FromStart(3), Index.FromStart(2));
                    foreach (var item in items.Take(range))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeForwardFromEndRangeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(^2..^1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_TakeMixedDirectionRangeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(0..^0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_TakeRuntimeRangeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(
                    IEnumerable<Item> items,
                    int start,
                    int end) {
                    foreach (var item in items.Take(start..end))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_TakeAliasedEqualRangeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    var range = new Range(Index.FromEnd(1), Index.FromEnd(1));
                    foreach (var item in items.Take(range))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeOmittedStartZeroEndLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(..0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeEndStartOmittedEndLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(^0..))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NamedStaticTakeEqualRangeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Take(range: 2..2, source: items))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeNonemptyRangeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(0..1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomTakeEqualRangeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> Take(
                    IEnumerable<Item> items,
                    Range range) =>
                    new[] { new Item(null) };
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Take(items, 0..0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_EnumerableEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ConcatEmptySourcesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<Item>()
                        .Concat(Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ConcatAliasedEmptySourcesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var empty = Enumerable.Empty<Item>();
                    foreach (var item in empty.Concat(empty))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_UpdatedConcatEmptyAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    IEnumerable<Item> combined = Enumerable.Empty<Item>();
                    combined = combined.Concat(combined);
                    foreach (var item in combined)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_UnionEmptyPipelinesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<int>()
                        .Select(static _ => new Item(null))
                        .Union(Enumerable.Empty<Item>().OrderBy(static item => item.Value)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_UnionByEmptySourcesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<Item>().UnionBy(
                        Enumerable.Empty<Item>(),
                        static item => item.Value))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IntersectEmptySecondLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Intersect(Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IntersectByEmptyKeysLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(int Key, object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.IntersectBy(
                        Enumerable.Empty<int>(),
                        static item => item.Key))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_JoinEmptyInnerLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(int Key, object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Join(
                        Enumerable.Empty<int>(),
                        static item => item.Key,
                        static key => key,
                        static (item, _) => item))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ZipEmptySecondLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Zip(
                        Enumerable.Empty<int>(),
                        static (item, _) => item))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ExceptEmptySecondLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new[] { new Item(null) }
                        .Except(Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_GroupJoinEmptyInnerLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(int Key, object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new[] { new Item(0, null) }.GroupJoin(
                        Enumerable.Empty<int>(),
                        static item => item.Key,
                        static key => key,
                        static (item, _) => item))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_QueryableConcatEmptySourcesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var first = Enumerable.Empty<Item>().AsQueryable();
                    var second = Enumerable.Empty<Item>().AsQueryable();
                    foreach (var item in first.Concat(second))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ConcatNonemptySecondLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<Item>()
                        .Concat(new[] { new Item(null) }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ConcatEmptySecondPreservesWherePredicate_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Concat(Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ConcatEmptyFirstPreservesWherePredicate_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Empty<Item>().Concat(
                        items.Where(static item => item.Value is not null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_StaticNamedConcatEmptyFirstPreservesWherePredicate_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Concat(
                        second: items.Where(static item => item.Value is not null),
                        first: Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_QueryableConcatEmptyFirstPreservesWherePredicate_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    var empty = Enumerable.Empty<Item>().AsQueryable();
                    var filtered = items.AsQueryable()
                        .Where(static item => item.Value is not null);
                    foreach (var item in empty.Concat(filtered))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ConcatNonemptyFirstDoesNotAdoptSecondWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in new[] { new Item(null) }.Concat(
                        items.Where(static item => item.Value is not null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_UnionEmptyFirstDoesNotAdoptSecondWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public sealed class MutatingComparer : IEqualityComparer<Item> {
                public bool Equals(Item? left, Item? right) => ReferenceEquals(left, right);
                public int GetHashCode(Item item) {
                    item.Value = null;
                    return 0;
                }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Empty<Item>().Union(
                        items.Where(static item => item.Value is not null),
                        new MutatingComparer()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_MutatingUnionComparerDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public sealed class MutatingComparer : IEqualityComparer<Item> {
                public bool Equals(Item? left, Item? right) => ReferenceEquals(left, right);
                public int GetHashCode(Item item) {
                    item.Value = null;
                    return 0;
                }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Union(Enumerable.Empty<Item>(), new MutatingComparer()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomConcatEmptySourcesLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> Concat(
                    IEnumerable<Item> first,
                    IEnumerable<Item> second) =>
                    new[] { new Item(null) };
                public static object FirstValue() {
                    foreach (var item in Concat(
                        Enumerable.Empty<Item>(),
                        Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArraySegmentEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ArraySegment<Item>.Empty)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArraySegmentZeroCountLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new[] { new Item(null) };
                    foreach (var item in new ArraySegment<Item>(
                        items,
                        offset: 0,
                        count: 0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArraySegmentOverEmptyArrayLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ArraySegment<Item>(
                        Array.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_DefaultConstructedArraySegmentLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ArraySegment<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_DefaultReadOnlySpanLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    ReadOnlySpan<Item> items = default;
                    foreach (var item in items)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_DefaultMemorySpanLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in default(Memory<Item>).Span)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SourceDefinedDefaultSegmentLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            public sealed record Item(object? Value);
            public readonly struct Segment {
                public Enumerator GetEnumerator() => new();
                public struct Enumerator {
                    private bool moved;
                    public Item Current => new(null);
                    public bool MoveNext() {
                        if (moved) return false;
                        moved = true;
                        return true;
                    }
                }
            }
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in default(Segment))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArraySegmentZeroCountAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var count = 1;
                    count = 0;
                    foreach (var item in new ArraySegment<Item>(
                        new[] { new Item(null) },
                        0,
                        count))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArraySegmentPositiveCountLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ArraySegment<Item>(
                        new[] { new Item(null) },
                        0,
                        1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SpanZeroLengthLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new[] { new Item(null) };
                    foreach (var item in new Span<Item>(items, 0, 0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReadOnlySpanOverEmptyArrayLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ReadOnlySpan<Item>(Array.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MemoryZeroLengthSpanLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new[] { new Item(null) };
                    foreach (var item in new Memory<Item>(items, 0, 0).Span)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReadOnlyMemoryPositiveLengthSpanLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ReadOnlyMemory<Item>(
                        new[] { new Item(null) },
                        0,
                        1).Span)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SourceDefinedArraySegmentConstructorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections;
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            namespace Mine {
                public sealed class ArraySegment : IEnumerable<Item> {
                    public ArraySegment(Item[] items, int offset, int count) {
                    }
                    public IEnumerator<Item> GetEnumerator() {
                        yield return new Item(null);
                    }
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
            }
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Mine.ArraySegment(
                        System.Array.Empty<Item>(),
                        0,
                        0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ReadOnlyMemoryEmptySpanLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ReadOnlyMemory<Item>.Empty.Span)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MemoryEmptySpanLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Memory<Item>.Empty.Span)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayAsSpanZeroLengthLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new[] { new Item(null) };
                    foreach (var item in items.AsSpan(start: 0, length: 0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayAsMemoryZeroLengthAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new[] { new Item(null) };
                    var length = 1;
                    length = 0;
                    foreach (var item in items.AsMemory(0, length).Span)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MemorySliceZeroLengthLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new Memory<Item>(new[] { new Item(null) });
                    foreach (var item in items.Slice(0, 0).Span)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayAsSpanPositiveLengthLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new[] { new Item(null) }.AsSpan(0, 1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_EmptyStringToCharArrayLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in string.Empty.ToCharArray())
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_StringToCharArrayZeroLengthLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in "x".ToCharArray(0, 0))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NonemptyStringToCharArrayLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in "x".ToCharArray())
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SourceDefinedToCharArrayLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            public sealed class Text {
                public char[] ToCharArray() => new[] { 'x' };
            }
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in new Text().ToCharArray())
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_EmptyStringAsMemorySpanLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in string.Empty.AsMemory().Span)
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyMemorySliceSpanLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ReadOnlyMemory<Item>.Empty.Slice(0).Span)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptySpanToArrayLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ReadOnlySpan<Item>.Empty.ToArray())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NonemptyMemorySpanLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var memory = new Memory<Item>(new[] { new Item(null) });
                    foreach (var item in memory.Span)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomAsSpanOverEmptyArrayLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> AsSpan(Item[] source) =>
                    new[] { new Item(null) };
                public static object FirstValue() {
                    foreach (var item in AsSpan(Array.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SourceDefinedMemoryExtensionsAsSpanLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class MemoryExtensions {
                public static IEnumerable<Item> AsSpan(Item[] source) =>
                    new[] { new Item(null) };
            }
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in MemoryExtensions.AsSpan(Array.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArraySegmentEmptyAliasProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var values = ArraySegment<int>.Empty;
                    foreach (var item in values.Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SpanEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Span<Item>.Empty)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReadOnlySpanEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ReadOnlySpan<Item>.Empty)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NonemptySpanLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Span<Item>(new[] { new Item(null) }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SourceDefinedArraySegmentEmptyLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections;
            using System.Collections.Generic;
            namespace Contoso {
                public sealed class ArraySegment<T> : IEnumerable<T> {
                    public static ArraySegment<T> Empty { get; } =
                        new([(T)(object)new Demo.Item(null)]);
                    private readonly IEnumerable<T> values;
                    private ArraySegment(IEnumerable<T> values) => this.values = values;
                    public IEnumerator<T> GetEnumerator() => values.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
            }
            namespace Demo {
                public sealed record Item(object? Value);
                public static class Consumer {
                    public static object FirstValue() {
                        foreach (var item in Contoso.ArraySegment<Item>.Empty)
                            return item.Value!;
                        return new object();
                    }
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_StringEmptyProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in string.Empty.Select(
                        static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyStringLiteralProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in "".Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ConstEmptyStringProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    const string text = "";
                    foreach (var item in text.Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NonemptyStringProjectionLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    const string text = "x";
                    foreach (var item in text.Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_RuntimeStringProjectionLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(string text) {
                    foreach (var item in text.Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomEmptyTextProjectionLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public static class Text {
                public static readonly string Empty = "x";
            }
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Text.Empty.Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ImmutableArrayEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableArray<Item>.Empty)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ImmutableArrayCreateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableArray.Create<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ImmutableArrayCreateRangeFromEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableArray.CreateRange(Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ToImmutableListFromEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<Item>().ToImmutableList())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ImmutableDictionaryCreateRangeFromEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var pair in ImmutableDictionary.CreateRange(
                        Enumerable.Empty<KeyValuePair<int, Item>>()))
                        return pair.Value.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ToImmutableDictionaryFromEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var pair in Enumerable.Empty<Item>().ToImmutableDictionary(
                        static item => item,
                        static item => item))
                        return pair.Value.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MutatingImmutableConversionDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var pair in items
                        .Where(static item => item.Value is not null)
                        .ToImmutableDictionary(
                            static item => {
                                item.Value = null;
                                return item;
                            },
                            static item => item))
                        return pair.Value.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_NonemptyImmutableCreateRangeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Immutable;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableArray.CreateRange(
                        new[] { new Item(null) }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomCreateRangeOverEmptySourceLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.Immutable;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static ImmutableArray<Item> CreateRange(IEnumerable<Item> source) =>
                    ImmutableArray.Create(new Item(null));
                public static object FirstValue() {
                    foreach (var item in CreateRange(Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ImmutableListCreateThroughProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableList.Create<int>()
                        .Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ImmutableQueueCreateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableQueue.Create<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ImmutableDictionaryCreateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var pair in ImmutableDictionary.Create<int, Item>())
                        return pair.Value.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NonemptyImmutableCreateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Immutable;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableArray.Create(new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SourceDefinedImmutableCreateLookalikeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections;
            using System.Collections.Generic;
            namespace Contoso {
                public sealed class ImmutableArray<T> : IEnumerable<T> {
                    private readonly IEnumerable<T> values;
                    public ImmutableArray(IEnumerable<T> values) => this.values = values;
                    public IEnumerator<T> GetEnumerator() => values.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
                public static class ImmutableArray {
                    public static ImmutableArray<T> Create<T>() =>
                        new([(T)(object)new Demo.Item(null)]);
                }
            }
            namespace Demo {
                public sealed record Item(object? Value);
                public static class Consumer {
                    public static object FirstValue() {
                        foreach (var item in Contoso.ImmutableArray.Create<Item>())
                            return item.Value!;
                        return new object();
                    }
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ImmutableListEmptyThroughProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableList<int>.Empty
                        .Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ImmutableQueueEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableQueue<Item>.Empty)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ImmutableDictionaryEmptyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var pair in ImmutableDictionary<int, Item>.Empty)
                        return pair.Value.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SourceDefinedImmutableLookalikeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections;
            using System.Collections.Generic;
            namespace Contoso {
                public sealed class ImmutableQueue<T> : IEnumerable<T> {
                    public static ImmutableQueue<T> Empty { get; } =
                        new([(T)(object)new Demo.Item(null)]);
                    private readonly IEnumerable<T> values;
                    private ImmutableQueue(IEnumerable<T> values) => this.values = values;
                    public IEnumerator<T> GetEnumerator() => values.GetEnumerator();
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
            }
            namespace Demo {
                public sealed record Item(object? Value);
                public static class Consumer {
                    public static object FirstValue() {
                        foreach (var item in Contoso.ImmutableQueue<Item>.Empty)
                            return item.Value!;
                        return new object();
                    }
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_RepeatZeroLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Repeat(new Item(null), 0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_RepeatZeroAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var count = 1;
                    count = 0;
                    foreach (var item in Enumerable.Repeat(new Item(null), count))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_RangeZeroAliasProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var count = 1;
                    count = 0;
                    foreach (var item in Enumerable.Range(0, count)
                        .Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_RepeatUpdatedPositiveAliasLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var count = 0;
                    count = 1;
                    foreach (var item in Enumerable.Repeat(new Item(null), count))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_NegativeRepeatCountLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Repeat(new Item(null), -1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NegativeRangeCountProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Range(count: -1, start: 0)
                        .Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NegativeTakeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(-1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CustomNegativeRangeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> Range(int start, int count) =>
                    new[] { new Item(null) };
                public static object FirstValue() {
                    foreach (var item in Range(0, -1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_NamedRepeatZeroLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Repeat(
                        count: 0,
                        element: new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_RangeZeroThroughProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Range(count: 0, start: 10)
                        .Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_RepeatOneLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Repeat(new Item(null), 1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_NonconstantRepeatCountLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(int count) {
                    foreach (var item in Enumerable.Repeat(new Item(null), count))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomRepeatZeroLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> Repeat(Item element, int count) =>
                    new[] { element };
                public static object FirstValue() {
                    foreach (var item in Repeat(new Item(null), 0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayEmptyThroughWhereLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Array.Empty<Item>().Where(static _ => true))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptySourceThroughOrderByLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<Item>()
                        .OrderBy(static item => item.Value))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyQueryableThroughOrderByLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<Item>()
                        .AsQueryable()
                        .OrderBy(item => item.Value))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptySourceThroughGroupByProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<Item>()
                        .GroupBy(static item => item.Value)
                        .Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptySourceThroughZipLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<int>()
                        .Zip(
                            new[] { 1 },
                            static (_, _) => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptySourceThroughMaterializerLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<Item>().ToList())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MutatingOrderByDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .OrderBy(static item => {
                            item.Value = null;
                            return 0;
                        }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomOrderByOverEmptySourceLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> OrderBy(
                    IEnumerable<Item> source,
                    Func<Item, object?> selector) =>
                    new[] { new Item(null) };
                public static object FirstValue() {
                    foreach (var item in OrderBy(
                        Enumerable.Empty<Item>(),
                        static item => item.Value))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_EmptySourceThroughTypeChangingOfTypeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<object>().OfType<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptySourceThroughTypeChangingCastLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<object>().Cast<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyQueryableThroughTypeChangingOfTypeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<object>()
                        .AsQueryable()
                        .OfType<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CustomOfTypeOverEmptySourceLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> OfType(IEnumerable<object> source) =>
                    new[] { new Item(null) };
                public static object FirstValue() {
                    foreach (var item in OfType(Enumerable.Empty<object>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_EmptySourceThroughProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<int>().Select(static _ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyOuterSourceThroughSelectManyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<int>()
                        .SelectMany(static _ => new[] { new Item(null) }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyOuterSourceThroughNamedSelectManyResultLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.SelectMany(
                        resultSelector: static (_, _) => new Item(null),
                        collectionSelector: static _ => new[] { 1 },
                        source: Enumerable.Empty<int>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyQueryableThroughSelectManyLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<int>()
                        .AsQueryable()
                        .SelectMany(_ => new[] { new Item(null) }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SelectManyDoesNotLeakOuterPredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .SelectMany(static _ => new[] { new Item(null) }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomSelectManyOverEmptySourceLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> SelectMany(
                    IEnumerable<int> source,
                    Func<int, IEnumerable<Item>> selector) =>
                    new[] { new Item(null) };
                public static object FirstValue() {
                    foreach (var item in SelectMany(
                        Enumerable.Empty<int>(),
                        static _ => new[] { new Item(new object()) }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_EmptySourceThroughNamedStaticProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Select(
                        selector: static _ => new Item(null),
                        source: Enumerable.Empty<int>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyQueryableThroughProjectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in Enumerable.Empty<int>()
                        .AsQueryable()
                        .Select(_ => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CustomSelectOverEmptySourceLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> Select(
                    IEnumerable<int> source,
                    Func<int, Item> selector) =>
                    new[] { new Item(null) };
                public static object FirstValue() {
                    foreach (var item in Select(
                        Enumerable.Empty<int>(),
                        static _ => new Item(new object())))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ZeroLengthArrayLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Item[0])
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ZeroLengthArrayAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var length = 1;
                    length = 0;
                    foreach (var item in new Item[length])
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NegativeArrayLengthAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var length = 0;
                    length = -1;
                    foreach (var item in new Item[length])
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ZeroLengthStackallocAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    var length = 1;
                    length = 0;
                    Span<int> items = stackalloc int[length];
                    foreach (var _ in items)
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_PositiveLengthStackallocLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    Span<int> items = stackalloc int[1];
                    foreach (var _ in items)
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_GcAllocatedZeroLengthArrayLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    var length = 1;
                    length = 0;
                    foreach (var _ in GC.AllocateUninitializedArray<int>(length))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_GcAllocatedInitializedZeroLengthArrayLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in GC.AllocateArray<int>(0, pinned: true))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_GcAllocatedPositiveLengthArrayLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in GC.AllocateUninitializedArray<int>(1))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceZeroLengthLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in Array.CreateInstance(typeof(int), 0))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceZeroDimensionAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    var width = 1;
                    width = 0;
                    foreach (var _ in Array.CreateInstance(typeof(int), 2, width))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceZeroDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    var height = 1;
                    height = 0;
                    var lengths = new[] { 2, height };
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceZeroInitializedDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in Array.CreateInstance(typeof(int), new int[2]))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceZeroInitializedDimensionVectorAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = new int[rank];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceEmptyDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    int[] lengths = [];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceEmptyFactoryDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in Array.CreateInstance(typeof(int), Array.Empty<int>()))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceEmptySingletonDimensionSpreadLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    int[] lengths = [.. ArraySegment<int>.Empty];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceEmptyOperatorDimensionSpreadLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    int[] lengths = [.. Enumerable.Range(1, 3).Take(0)];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceNonEmptyOperatorDimensionSpreadLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    int[] lengths = [.. Enumerable.Repeat(1, 1)];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceNonPositiveRepeatDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Repeat(0, rank).ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceReorderedNonPositiveRepeatDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Repeat(0, rank).Reverse().ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredNonPositiveRepeatDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Repeat(0, rank)
                        .Where(static _ => true)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredAllNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 0, -1 }
                        .Where(static value => value == 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredMixedDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 0, 2 }
                        .Where(static value => value > 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceSubsequenceOfNonPositiveDimensionsLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Repeat(0, rank)
                        .Skip(1)
                        .Take(2)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceConcatenatedNonPositiveDimensionsLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int firstRank, int secondRank) {
                    var lengths = Enumerable.Repeat(0, firstRank)
                        .Concat(Enumerable.Repeat(-1, secondRank))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceConcatenatedGuaranteedNonPositiveDimensionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var first = new[] { 0 };
                    var second = new[] { 2 };
                    var lengths = first.Concat(second).ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceConcatenatedPossiblyEmptyNonPositiveAndPositiveDimensionsLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Repeat(0, rank)
                        .Concat(new[] { 2 })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceConcatenatedPositiveDimensionsLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 2 }
                        .Concat(new[] { 3 })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceUnionedNonPositiveDimensionsLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int firstRank, int secondRank) {
                    var lengths = Enumerable.Repeat(0, firstRank)
                        .Union(Enumerable.Repeat(-1, secondRank))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceUnionByNonPositiveDimensionsLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 0, -1 }
                        .UnionBy(new[] { -2 }, static _ => 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceUnionedPossiblyEmptyNonPositiveAndPositiveDimensionsLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Repeat(0, rank)
                        .Union(new[] { 2 })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceUnionedPositiveDimensionsLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 2 }
                        .Union(new[] { 3 })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceAppendedNonPositiveDimensionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = Enumerable.Repeat(2, 1)
                        .Append(0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstancePrependedNonPositiveDimensionAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var dimension = 1;
                    dimension = 0;
                    var lengths = new[] { 2 }
                        .Prepend(dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstancePositiveDimensionAppendedToEmptySourceLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = Array.Empty<int>()
                        .Append(2)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstancePositiveDimensionAppendedToPossiblyEmptyNonPositiveSourceLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Repeat(0, rank)
                        .Append(2)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstancePositiveDimensionAppendedToGuaranteedNonPositiveSourceLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 0 }
                        .Append(2)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(static _ => 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedBlockConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(static (_, _) => { return -1; })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMultiReturnLambdaNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Select(static value => {
                            if (value > 0)
                                return 0;
                            return -1;
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMultiReturnAnonymousNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Select(delegate(int value) {
                            if (value > 0)
                                return 0;
                            return -1;
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedBlockHelperNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Zero() => 0;
                private static int Negative() => -1;
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Select(static value => {
                            if (value > 0)
                                return Zero();
                            return Negative();
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNestedLambdaPositiveReturnDoesNotAffectOuterSummary_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Select(static value => {
                            Func<int> nested = () => 1;
                            GC.KeepAlive(nested);
                            if (value > 0)
                                return 0;
                            return -1;
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMultiReturnLambdaPositiveArmLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static value => {
                            if (value > 0)
                                return 1;
                            return 0;
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedAnonymousConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(delegate(int _) { return 0; })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSourceMethodConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Zero(int _) => 0;
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(Zero)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSourceHelperConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Constant() => 0;
                private static int Zero(int _) => Constant();
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(Zero)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedDeepSourceHelperConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Third() => -1;
                private static int Second() => Third();
                private static int First(int _) => Second();
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(First)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedLambdaSourceHelperConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Zero() => 0;
                public static object FirstValue(int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(static _ => Zero())
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedConditionalNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Select(static value => value > 0 ? 0 : -1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedConditionalHelperNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Zero() => 0;
                private static int Negative() => -1;
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Select(static value => value > 0 ? Zero() : Negative())
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedConditionalPositiveArmLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static value => value > 0 ? 1 : 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSwitchNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 0, 1 }
                        .Select(static value => value switch {
                            > 0 => 0,
                            0 => throw new InvalidOperationException(),
                            _ => -1
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSwitchPositiveArmLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static value => value switch {
                            > 0 => 1,
                            _ => 0
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMathMinNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Select(static value => Math.Min(value, 0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMathMaxNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Math.Max(-1, 0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMathMaxPossiblyPositiveDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static value => Math.Max(value, 0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMathClampNonPositiveMaximumLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Maximum() => 0;
                public static object FirstValue() {
                    var lengths = new[] { -20, 20 }
                        .Select(static value => Math.Clamp(value, min: -10, max: Maximum()))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMathClampPositiveMaximumLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 20 }
                        .Select(static value => Math.Clamp(value, min: -10, max: 10))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedMathAbsDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => -Math.Abs(value))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedNonNegativeSourceMethodLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Magnitude(int value) {
                    var magnitude = Math.Max(value, 0);
                    return magnitude;
                }
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => -Magnitude(value))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedNonNegativeMathClampLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -20, 20 }
                        .Select(static value => -Math.Clamp(value, min: 0, max: 10))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCheckedDoubleNegatedNonPositiveSourceMethodLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive(int value) => Math.Min(value, 0);
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => -checked(-NonPositive(value)))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedUncheckedNegatedNonPositiveSourceMethodLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive(int value) => Math.Min(value, 0);
                public static object FirstValue() {
                    var lengths = new[] { -1 }
                        .Select(static value => -NonPositive(value))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNarrowedNonPositiveSourceMethodLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive() => -200;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => (int)(sbyte)NonPositive())
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMathAbsDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1 }
                        .Select(static value => Math.Abs(value))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedSourceDefinedAbsLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            namespace Contoso {
                public static class Numbers {
                    public static int Abs(int value) => -1;
                }
            }
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static value => -Contoso.Numbers.Abs(value))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNonPositiveMathSignLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => Math.Sign(Math.Min(value, 0)))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCheckedNonPositiveSumLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => checked(
                            Math.Min(value, 0) + Math.Min(value, 0)))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedUncheckedOverflowingNonPositiveSumLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Minimum() => int.MinValue;
                private static int MinusOne() => -1;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => unchecked(Minimum() + MinusOne()))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedUncheckedNonPositiveAdditiveIdentityLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => unchecked(Math.Min(value, 0) + 0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCheckedNonPositiveDifferenceLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive() => -6;
                private static int NonNegative() => 2;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => checked(NonPositive() - NonNegative()))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCheckedOppositeSignProductLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive() => -6;
                private static int NonNegative() => 2;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => checked(NonPositive() * NonNegative()))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedUncheckedOverflowingOppositeSignProductLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive() => -1073741824;
                private static int NonNegative() => 3;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => unchecked(NonPositive() * NonNegative()))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNonPositiveQuotientLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive() => -6;
                private static int NonNegative() => 2;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => NonPositive() / NonNegative())
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNonPositiveRemainderLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive() => -5;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static value => NonPositive() % value)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNonPositiveRightShiftLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => Math.Min(value, 0) >> 1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedSignBitClearingMaskLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => -(value & int.MaxValue))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNonPositiveBitwiseOrLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => Math.Min(value, 0) | -2)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSignBitSettingExclusiveOrLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => int.MinValue ^ Math.Abs(value))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedAmbiguousNonPositiveExclusiveOrLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int First() => -2;
                private static int Second() => -3;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => First() ^ Second())
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedUnsignedRightShiftLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Negative() => -1;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => -(Negative() >>> 1))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPositiveUnsignedRightShiftLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Negative() => -1;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Negative() >>> 1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSignChangingLeftShiftLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive() => -1610612736;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => NonPositive() << 1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedBitwiseNegatedNonNegativeLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => ~Math.Abs(value))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedBitwiseNegatedNonPositiveLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive() => -2;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => ~NonPositive())
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedOverloadedRightShiftLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public readonly struct Bits {
                public static int operator >>(Bits value, int count) => 1;
            }
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => new Bits() >> 1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedByteConversionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => -(int)(byte)value)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedCheckedUnsignedConversionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static uint Unsigned() => uint.MaxValue;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => -checked((int)Unsigned()))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedUncheckedUnsignedConversionLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static uint Unsigned() => uint.MaxValue;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => -unchecked((int)Unsigned()))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCheckedUnsignedNarrowingOfNonPositiveLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive(int value) => Math.Min(value, 0);
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => (int)checked((byte)NonPositive(value)))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCheckedSignedNarrowingOfNonPositiveLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NonPositive(int value) => Math.Min(value, 0);
                public static object FirstValue() {
                    var lengths = new[] { -2, 2 }
                        .Select(static value => (int)checked((sbyte)NonPositive(value)))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNegatedUserDefinedConversionLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public readonly struct Number {
                public static explicit operator int(Number value) => -1;
            }
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => -(int)new Number())
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedStaticPropertyNonPositiveLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension => 0;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedBlockPropertyNonPositiveLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension {
                    get {
                        if (DateTime.UtcNow.Ticks > 0)
                            return 0;
                        return -1;
                    }
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSealedInstancePropertyNonPositiveLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed class Consumer {
                private int Dimension => 0;
                public object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(_ => Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedConstantIndexerNonPositiveLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed class Dimensions {
                public int this[int index] => 0;
            }
            public static class Consumer {
                public static object FirstValue(Dimensions dimensions) {
                    var lengths = new[] { 1 }
                        .Select(index => dimensions[index])
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedVirtualPropertyLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public class DimensionSource {
                public virtual int Dimension => 0;
            }
            public sealed class PositiveDimensionSource : DimensionSource {
                public override int Dimension => 1;
            }
            public static class Consumer {
                public static object FirstValue(DimensionSource source) {
                    var lengths = new[] { 1 }
                        .Select(_ => source.Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedStateDependentPropertyLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed class Consumer {
                private int _dimension;
                private int Dimension => _dimension;
                public object FirstValue() {
                    var projected = new[] { 1 }.Select(_ => Dimension);
                    _dimension = 1;
                    var lengths = projected.ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedRecursivePropertyLoopBodyRemainsConservative_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension => Dimension;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPositivePropertyLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension => 1;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedTupleLiteralElementNonPositiveLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => (0, 1).Item1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNamedTupleLiteralElementNonPositiveLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => (width: 0, height: 1).width)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedTupleLiteralPositiveElementLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => (0, 1).Item2)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedArrayLiteralElementNonPositiveLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => new[] { 0, 1 }[0])
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedArrayLiteralAllNonPositiveUnknownElementLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 0, 1 }
                        .Select(static value => new[] { -1, 0 }[value])
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedArrayLiteralMixedUnknownElementLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static value => new[] { 0, 1 }[value])
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedArrayLiteralOutOfRangeElementLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => new[] { 1 }[2])
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedFreshZeroInitializedArrayElementLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 0, 1 }
                        .Select(static value => new int[value][0])
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMutatedArrayElementLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int[] MakePositive(int[] values) {
                    values[0] = 1;
                    return values;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => MakePositive(new[] { 0 })[0])
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedBoundArgumentIdentityHelperLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Identity(0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNestedBoundArgumentHelpersLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                private static int Forward(int value) => Identity(value);
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Forward(0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedReorderedNamedBoundArgumentHelperLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Choose(int positive, int nonPositive) => nonPositive;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Choose(nonPositive: 0, positive: 1))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPositiveArgumentIdentityHelperLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Identity(1))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedReassignedPositiveArgumentHelperLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Reassign(int value) {
                    value = 1;
                    return value;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Reassign(0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedReassignedNonPositiveArgumentHelperLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Normalize(int value) {
                    value = 0;
                    return value;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Normalize(1))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedConditionallyMutatedBoundArgumentHelperLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int MaybeReassign(int value) {
                    if (DateTime.UtcNow.Ticks > 0)
                        value = 1;
                    return value;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => MaybeReassign(0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedRefMutatedBoundArgumentHelperLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Mutate(ref int value) {
                    value = 1;
                    return value;
                }
                private static int Dimension() {
                    var value = 0;
                    return Mutate(ref value);
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Dimension())
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedBoundArgumentMutatedThroughRefLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static void MakePositive(ref int value) => value = 1;
                private static int Dimension(int value) {
                    MakePositive(ref value);
                    return value;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Dimension(0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCapturedMutatedBoundArgumentHelperLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension(int value) {
                    void MakePositive() => value = 1;
                    MakePositive();
                    return value;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Dimension(0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCapturedMutatedReassignedArgumentHelperLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension(int value) {
                    value = 0;
                    void MakePositive() => value = 1;
                    MakePositive();
                    return value;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Dimension(0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCapturedOuterBoundArgumentHelperLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Forward(int value) {
                    int Inner() => value;
                    return Inner();
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static _ => Forward(0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedBoundArgumentIndexerLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed class Dimensions {
                public int this[int value] => value;
            }
            public static class Consumer {
                public static object FirstValue(Dimensions dimensions) {
                    var lengths = new[] { 1 }
                        .Select(_ => dimensions[0])
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPositiveArgumentIndexerLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed class Dimensions {
                public int this[int value] => value;
            }
            public static class Consumer {
                public static object FirstValue(Dimensions dimensions) {
                    var lengths = new[] { 1 }
                        .Select(_ => dimensions[1])
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedAllNonPositiveSourceIdentityMethodGroupLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var lengths = Enumerable.Repeat(0, 1)
                        .Select(Identity)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedAllNonPositiveSourceIdentityLambdaLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = Enumerable.Repeat(0, 1)
                        .Select(static value => value)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedIndexedSelectorNonPositiveIndexLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { "first", "second" }
                        .Select(static (_, index) => -index)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedIndexedMethodGroupNonPositiveIndexLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int NegativeIndex(int _, int index) => -index;
                public static object FirstValue() {
                    var lengths = new[] { 1, 2 }
                        .Select(NegativeIndex)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPositiveSourceIdentityMethodGroupLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var lengths = Enumerable.Repeat(1, 1)
                        .Select(Identity)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMixedSourceIdentityMethodGroupLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var lengths = new[] { 0, 1 }
                        .Select(Identity)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMutatingIdentitySelectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Reassign(int value) {
                    value = 1;
                    return value;
                }
                public static object FirstValue() {
                    var lengths = Enumerable.Repeat(0, 1)
                        .Select(Reassign)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedEmptySourcePositiveSelectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = Array.Empty<int>()
                        .Select(static _ => 1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedAllNonPositiveSourceVirtualSelectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public class Selector {
                public virtual int Identity(int value) => value;
            }
            public sealed class PositiveSelector : Selector {
                public override int Identity(int value) => 1;
            }
            public static class Consumer {
                public static object FirstValue(Selector selector) {
                    var lengths = Enumerable.Repeat(0, 1)
                        .Select(selector.Identity)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMutatedAllNonPositiveSourceIdentitySelectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var source = new[] { 0 };
                    var projected = source.Select(Identity);
                    source[0] = 1;
                    var lengths = projected.ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSourceMutatedAfterMaterializationLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var source = new[] { 0 };
                    var lengths = source.Select(Identity).ToArray();
                    source[0] = 1;
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedEagerListSnapshotIgnoresLaterSourceMutationLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var source = new[] { 0 };
                    var snapshot = source.ToList();
                    source[0] = 1;
                    var lengths = snapshot.Select(Identity).ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMutatedListSnapshotBeforeMaterializationLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var snapshot = new[] { 0 }.ToList();
                    var projected = snapshot.Select(Identity);
                    snapshot[0] = 1;
                    var lengths = projected.ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedChainedDeferredSourceMutationLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var source = new[] { 0 };
                    var projected = source
                        .Where(static _ => true)
                        .Select(Identity);
                    source[0] = 1;
                    var lengths = projected.ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedDeferredSourceMutationThroughAliasLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    var source = new[] { 0 };
                    var alias = source;
                    var projected = source.Select(Identity);
                    alias[0] = 1;
                    var lengths = projected.ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedAllNonPositiveSourceLocalDelegateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                public static object FirstValue() {
                    Func<int, int> selector = Identity;
                    var lengths = Enumerable.Repeat(0, 1)
                        .Select(selector)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedAllNonPositiveSourceReassignedPositiveDelegateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Identity(int value) => value;
                private static int Positive(int value) => 1;
                public static object FirstValue() {
                    Func<int, int> selector = Identity;
                    selector = Positive;
                    var lengths = Enumerable.Repeat(0, 1)
                        .Select(selector)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredNonPositivePredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(static value => value <= 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredStrictUpperBoundPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(static value => value < 1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredReversedNonPositivePredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(static value => 0 >= value)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredNegatedPositivePredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(static value => !(value > 0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredConjunctiveNonPositivePredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(bool keep) {
                    var lengths = new[] { -1, 1 }
                        .Where(value => value <= 0 && keep)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredDisjunctiveNonPositivePredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(static value => value <= 0 || value == -1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredDisjunctivePositivePredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(static value => value <= 0 || value == 1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredIndexedNonPositivePredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(static (value, index) => value <= 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceTakeWhileNonPositivePredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .TakeWhile(static value => value <= 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceSkipWhileNonPositiveLookalikeLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 0, 1 }
                        .SkipWhile(static value => value <= 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredSourceMethodPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool IsNonPositive(int value) => value <= 0;
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(IsNonPositive)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredGuardedSourceMethodPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool IsNonPositive(int value) {
                    if (value > 0)
                        return false;
                    return true;
                }
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(IsNonPositive)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredGuardedCapturedLambdaPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(bool include) {
                    var lengths = new[] { -1, 1 }
                        .Where(value => {
                            if (value > 0)
                                return false;
                            return include;
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredLambdaLocalBindingPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(value => {
                            var positive = value > 0;
                            if (positive)
                                return false;
                            return true;
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredLambdaReassignedLocalPredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Where(value => {
                            var candidate = value;
                            candidate = 0;
                            return candidate <= 0;
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredGuardedCapturedLocalDelegatePredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(bool include) {
                    Func<int, bool> predicate = value => {
                        if (value > 0)
                            return false;
                        return include;
                    };
                    var lengths = new[] { -1, 1 }
                        .Where(predicate)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredCapturedPositivePredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(bool include) {
                    var lengths = new[] { -1, 1 }
                        .Where(value => include || value <= 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredReassignedCapturedGuardedPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var include = false;
                    var query = new[] { -1, 1 }
                        .Where(value => {
                            if (value > 0)
                                return false;
                            return include;
                        });
                    include = true;
                    var lengths = query.ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredReassignedCapturedPositivePredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var include = false;
                    var query = new[] { -1, 1 }
                        .Where(value => include || value <= 0);
                    include = true;
                    var lengths = query.ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredCapturedStableRecordPropertyPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed record Filter(bool Include);
            public static class Consumer {
                public static object FirstValue(Filter filter) {
                    var lengths = new[] { -1, 1 }
                        .Where(value => {
                            if (value > 0)
                                return false;
                            return filter.Include;
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredCapturedUnstablePropertyPredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed class Toggle {
                private int reads;
                public bool Flag => reads++ == 0;
            }
            public static class Consumer {
                public static object FirstValue(Toggle toggle) {
                    var lengths = new[] { 1 }
                        .Where(value =>
                            toggle.Flag && !toggle.Flag ||
                            value <= 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredCapturedUnstableIndexerPredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed class Toggle {
                private int reads;
                public bool this[int index] => reads++ == 0;
            }
            public static class Consumer {
                public static object FirstValue(Toggle toggle) {
                    var lengths = new[] { 1 }
                        .Where(value =>
                            toggle[0] && !toggle[0] ||
                            value <= 0)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredPositiveReturnGuardPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool IsNonPositive(int value) {
                    if (value <= 0)
                        return true;
                    return false;
                }
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(IsNonPositive)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredLocalBooleanGuardPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool IsNonPositive(int value) {
                    var positive = value > 0;
                    if (positive)
                        return false;
                    return true;
                }
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(IsNonPositive)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredHelperGuardPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool IsPositive(int value) => value > 0;
                private static bool IsNonPositive(int value) {
                    if (IsPositive(value))
                        return false;
                    return true;
                }
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(IsNonPositive)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredSwitchPredicateLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool IsNonPositive(int value) =>
                    value switch {
                        > 0 => false,
                        _ => true
                    };
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(IsNonPositive)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredSwitchPositivePredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool IsPositive(int value) =>
                    value switch {
                        > 0 => true,
                        _ => false
                    };
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(IsPositive)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredGuardedPositivePredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool IsPositive(int value) {
                    if (value > 0)
                        return true;
                    return false;
                }
                public static object FirstValue() {
                    var lengths = new[] { -1, 1 }
                        .Where(IsPositive)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredReassignedGuardPredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool AcceptAll(int value) {
                    var positive = value > 0;
                    positive = false;
                    if (positive)
                        return false;
                    return true;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Where(AcceptAll)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredMutatingPredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static bool ReassignThenAccept(int value) {
                    value = 0;
                    return true;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Where(ReassignThenAccept)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceFilteredVirtualPredicateLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public class Filter {
                public virtual bool IsNonPositive(int value) => value <= 0;
            }
            public sealed class AcceptAllFilter : Filter {
                public override bool IsNonPositive(int value) => true;
            }
            public static class Consumer {
                public static object FirstValue(Filter filter) {
                    var lengths = new[] { 1 }
                        .Where(filter.IsNonPositive)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSourceDefinedMinLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            namespace Contoso {
                public static class Numbers {
                    public static int Min(int first, int second) => 1;
                }
            }
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(static value => Contoso.Numbers.Min(value, 0))
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPositiveSourceHelperLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Positive() => 1;
                private static int Dimension(int _) => Positive();
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedVirtualSourceHelperLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public class DimensionSource {
                public virtual int Dimension(int _) => 0;
            }
            public sealed class PositiveDimensionSource : DimensionSource {
                public override int Dimension(int _) => 1;
            }
            public static class Consumer {
                public static object FirstValue(DimensionSource source) {
                    int SelectDimension(int value) => source.Dimension(value);
                    var lengths = new[] { 1 }
                        .Select(SelectDimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedRecursiveSourceHelperLoopBodyRemainsConservative_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension(int _) => Dimension(0);
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPrivateInstanceMethodConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed class Consumer {
                private int Zero(int _) => 0;
                public object FirstValue(int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(Zero)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedNonVirtualInstanceMethodConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public class DimensionSource {
                public int Dimension(int _) => 0;
            }
            public static class Consumer {
                public static object FirstValue(DimensionSource source, int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(source.Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCapturingLocalFunctionConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    var captured = new object();
                    int Zero(int _) {
                        GC.KeepAlive(captured);
                        return 0;
                    }
                    var lengths = Enumerable.Range(0, rank)
                        .Select(Zero)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedSealedOverrideConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public abstract class DimensionSource {
                public abstract int Dimension(int value);
            }
            public sealed class ZeroDimensionSource : DimensionSource {
                public override int Dimension(int _) => 0;
                public object FirstValue(int rank) {
                    var lengths = Enumerable.Range(0, rank)
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedStateDependentInstanceMethodLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public sealed class Consumer {
                private int _dimension = 1;
                private int Dimension(int _) => _dimension;
                public object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedLocalFunctionConstantNonPositiveDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    static int Zero(int _) => 0;
                    var lengths = Enumerable.Range(0, rank)
                        .Select(Zero)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMultiReturnNonPositiveMethodLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension(int value) {
                    if (value > 0)
                        return 0;
                    return -1;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1, 2 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedLocalResultNonPositiveMethodLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension(int _) {
                    var result = 0;
                    return result;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedReassignedLocalResultNonPositiveMethodLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension(int _) {
                    var result = 1;
                    result = 0;
                    return result;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedHelperInitializedLocalAliasMethodLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Zero() => 0;
                private static int Dimension(int _) {
                    var first = Zero();
                    var result = first;
                    return result;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPositiveLocalResultMethodLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension(int _) {
                    var result = 1;
                    return result;
                }
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedCapturedMutableLocalResultLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var dimension = 0;
                    int SelectDimension(int _) => dimension;
                    var projected = new[] { 1 }.Select(SelectDimension);
                    dimension = 1;
                    var lengths = projected.ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPositiveSourceMethodLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension(int _) => 1;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedParameterDependentSourceMethodLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                private static int Dimension(int value) => value;
                public static object FirstValue() {
                    var lengths = new[] { 1 }
                        .Select(Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedVirtualMethodLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public class DimensionSource {
                public virtual int Dimension(int _) => 0;
            }
            public sealed class PositiveDimensionSource : DimensionSource {
                public override int Dimension(int _) => 1;
            }
            public static class Consumer {
                public static object FirstValue(DimensionSource source) {
                    var lengths = new[] { 1 }
                        .Select(source.Dimension)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedInputDependentDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = Enumerable.Range(1, 1)
                        .Select(static value => value)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedMutableCaptureDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var dimension = 0;
                    var projected = Enumerable.Range(0, 1).Select(_ => dimension);
                    dimension = 1;
                    var lengths = projected.ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceProjectedPositiveDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = Enumerable.Repeat(0, 1)
                        .Select(static _ => 1)
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstancePositiveRepeatDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = Enumerable.Repeat(1, 1).ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceMutatedNonPositiveRepeatDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = Enumerable.Repeat(0, 1).ToArray();
                    lengths[0] = 1;
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceSourceDefinedRepeatDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            namespace Contoso {
                public static class Sequence {
                    public static int[] Repeat(int element, int count) => new[] { 1 };
                }
            }
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in Array.CreateInstance(typeof(int), Contoso.Sequence.Repeat(0, 1)))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceZeroInitializedDimensionSpreadLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    int[] lengths = [2, .. new int[1]];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceOnlyUnknownZeroInitializedSpreadLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    int[] lengths = [.. new int[rank]];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceMixedUnknownZeroInitializedSpreadLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue(int rank) {
                    int[] lengths = [2, .. new int[rank]];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceMutatedZeroInitializedVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new int[2];
                    lengths[0] = 2;
                    lengths[1] = 3;
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceZeroCollectionDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    var width = 1;
                    width = 0;
                    int[] lengths = [2, width];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths, [0, 0]))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceZeroSpreadDimensionVectorLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    var width = 0;
                    int[] lengths = [2, .. new[] { 3, width }];
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ArrayCreateInstancePositiveDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 2, 3 };
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstanceMutatedDimensionVectorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    var lengths = new[] { 2, 0 };
                    lengths[1] = 3;
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ArrayCreateInstancePositiveLengthLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System;
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in Array.CreateInstance(typeof(int), 1))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SourceDefinedAllocateArrayLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            namespace Contoso {
                public static class GC {
                    public static int[] AllocateArray(int length) => new[] { 1 };
                }
            }
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var _ in Contoso.GC.AllocateArray(0))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ZeroDimensionArrayLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Item[2, 0])
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ZeroDimensionArrayAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var width = 1;
                    width = 0;
                    foreach (var item in new Item[2, width])
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyCollectionExpressionAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    IEnumerable<Item> items = [];
                    foreach (var item in items)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_FreshListLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new List<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ListFromEmptySourceLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new List<Item>(Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_FreshHashSetWithCapacityLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new HashSet<Item>(
                        4,
                        EqualityComparer<Item>.Default))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_FreshConcurrentQueueLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Concurrent;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ConcurrentQueue<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_FreshDictionaryValuesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Dictionary<int, Item>().Values)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_FreshDictionaryKeysLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Dictionary<Item, int>().Keys)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_FreshConcurrentDictionaryValuesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Concurrent;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ConcurrentDictionary<int, Item>().Values)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyDictionaryInterfaceValuesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    IDictionary<int, Item> items = new Dictionary<int, Item>();
                    foreach (var item in items.Values)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ImmutableDictionaryEmptyValuesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in ImmutableDictionary<int, Item>.Empty.Values)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NonemptyDictionaryValuesLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Dictionary<int, Item> {
                        [0] = new Item(null)
                    }.Values)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CapturedDictionaryValuesPopulatedLaterLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new Dictionary<int, Item>();
                    var values = items.Values;
                    items.Add(0, new Item(null));
                    foreach (var item in values)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ReboundDictionaryDoesNotMutateCapturedValues_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new Dictionary<int, Item>();
                    var values = items.Values;
                    items = new Dictionary<int, Item> {
                        [0] = new Item(null)
                    };
                    foreach (var item in values)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SourceDefinedValuesPropertyLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public sealed class Items {
                public IEnumerable<Item> Values => new[] { new Item(null) };
            }
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Items().Values)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_FreshObservableCollectionLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ObservableCollection<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReadOnlyCollectionOverEmptyListLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ReadOnlyCollection<Item>(
                        new List<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CollectionOverEmptyListLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Collection<Item>(new List<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReadOnlyDictionaryOverEmptyDictionaryValuesLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ReadOnlyDictionary<int, Item>(
                        new Dictionary<int, Item>()).Values)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReadOnlyObservableCollectionOverEmptySourceLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ReadOnlyObservableCollection<Item>(
                        new ObservableCollection<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReadOnlyCollectionOverNonemptyListLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new ReadOnlyCollection<Item>(
                        new List<Item> { new(null) }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ReadOnlyCollectionBackingPopulatedLaterLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var backing = new List<Item>();
                    var items = new ReadOnlyCollection<Item>(backing);
                    backing.Add(new Item(null));
                    foreach (var item in items)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ReboundBackingDoesNotMutateReadOnlyCollection_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var backing = new List<Item>();
                    var items = new ReadOnlyCollection<Item>(backing);
                    backing = new List<Item> { new(null) };
                    foreach (var item in items)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ObservableCollectionSnapshotsEmptySource_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var source = new List<Item>();
                    var items = new ObservableCollection<Item>(source);
                    source.Add(new Item(null));
                    foreach (var item in items)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_PopulatedCollectionWrapperInitializerLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Collection<Item>(new List<Item>()) {
                        new(null)
                    })
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SourceDefinedCollectionWrapperLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections;
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            namespace Mine {
                public sealed class ReadOnlyCollection : IEnumerable<Item> {
                    public ReadOnlyCollection(IEnumerable<Item> source) {
                    }
                    public IEnumerator<Item> GetEnumerator() {
                        yield return new Item(null);
                    }
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
            }
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Mine.ReadOnlyCollection(
                        System.Linq.Enumerable.Empty<Item>()))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_FreshArrayListLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (Item item in new ArrayList(4))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_FreshListAliasLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new List<Item>();
                    foreach (var item in items)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ListPopulatedAfterConstructionLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new List<Item>();
                    items.Add(new Item(null));
                    foreach (var item in items)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_AliasedListPopulatedAfterConstructionLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new List<Item>();
                    var alias = items;
                    items.Add(new Item(null));
                    foreach (var item in alias)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ReboundListDoesNotMutateCapturedEmptyAlias_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var items = new List<Item>();
                    var alias = items;
                    items = new List<Item>();
                    items.Add(new Item(null));
                    foreach (var item in alias)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_EmptyListInitializerLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new List<Item> { })
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_PopulatedListConstructorLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new List<Item>(
                        new[] { new Item(null) }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_WrappedListMutatedAfterConstructionLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Collections.ObjectModel;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    var inner = new List<Item>();
                    var outer = new Collection<Item>(inner);
                    inner.Add(new Item(null));
                    foreach (var item in outer)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_NonemptyListInitializerLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new List<Item> { new(null) })
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SourceDefinedListLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections;
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            namespace Mine {
                public sealed class List : IEnumerable<Item> {
                    public IEnumerator<Item> GetEnumerator() {
                        yield return new Item(null);
                    }
                    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                }
            }
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new Mine.List())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_NonemptyArrayLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue() {
                    foreach (var item in new[] { new Item(null) })
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomEmptyMethodLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> Empty() {
                    yield return new Item(null);
                }
                public static object FirstValue() {
                    foreach (var item in Empty())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_NamedStaticTakeZeroLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Take(count: 0, source: items))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_QueryableTakeZeroLoopBodyIsUnreachable_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items) {
                    foreach (var item in items.Take(0))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeOneLoopBodyRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_DefaultIfEmptyAfterTakeZeroRemainsReachable_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Take(0).DefaultIfEmpty())
                        return item!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ImmutableArrayWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(ImmutableArray<Item> items) {
                    foreach (var item in items.Where(static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_QueryableWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items) {
                    foreach (var item in items.Where(static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_LocalWhereSequenceRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    var filtered = items.Where(static item => item.Value is not null);
                    foreach (var item in filtered)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ChainedLocalWhereSequenceAliasRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    var filtered = items.Where(static item => item.Value is not null);
                    var alias = filtered;
                    foreach (var item in alias)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReassignedLocalWhereSequenceDoesNotRefineLoopElement_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    var filtered = items.Where(static item => item.Value is not null);
                    filtered = items;
                    foreach (var item in filtered)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_LaterSourcePredicateDoesNotRefineCapturedSequence_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    var source = items;
                    var filtered = source.Where(static item => true);
                    source = items.Where(static item => item.Value is not null);
                    foreach (var item in filtered)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_UpdatedWhereSequenceRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    items = items.Where(static item => item.Value is not null);
                    foreach (var item in items)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MultiplyUpdatedWhereSequenceRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value, object? Other);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    items = items.Where(static item => item.Value is not null);
                    items = items.Where(static item => item.Other is not null);
                    foreach (var item in items)
                        return (item.Value!, item.Other!);
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_UpdatedNonFilteringSequenceDoesNotRefineLoopElement_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    items = items.Select(static item => item);
                    foreach (var item in items)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_IndexedQueryableWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items) {
                    foreach (var item in items.Where(
                        static (item, index) => index > 0 && item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CapturedQueryableWherePredicate_Reports",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items, object? expected) {
                    foreach (var item in items.Where(item => item.Value == expected))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ChainedWherePredicatesRefineLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value, bool Enabled);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Where(static item => item.Enabled))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughSkipAndTakeRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Skip(1)
                        .Take(2))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TakeWhilePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.TakeWhile(
                        static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IndexedTakeWhilePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.TakeWhile(
                        static (item, index) => index < 10 && item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughPureSkipWhileRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value, bool Enabled);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .SkipWhile(static item => item.Enabled))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughIndexedSkipWhileRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value, bool Enabled);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .SkipWhile(static (item, index) => index < 1 && item.Enabled))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SkipWhilePredicateDoesNotRefineEveryElement_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.SkipWhile(
                        static item => item.Value is null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_MutatingSkipWhileInvalidatesWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .SkipWhile(static item => {
                            item.Value = null;
                            return false;
                        }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_OuterWhereReassertsAfterMutatingSkipWhile_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .SkipWhile(static item => {
                            item.Value = null;
                            return false;
                        })
                        .Where(static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MutatingTakeWhileInvalidatesWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .TakeWhile(static item => {
                            item.Value = null;
                            return true;
                        }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_MutatingBlockWhereInvalidatesInnerPredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Where(static item => {
                            item.Value = null;
                            return true;
                        }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_OuterWhereReassertsAfterMutatingTakeWhile_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .TakeWhile(static item => {
                            item.Value = null;
                            return true;
                        })
                        .Where(static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CustomTakeWhileDoesNotRefineLoopElement_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> TakeWhile(
                    IEnumerable<Item> items,
                    Func<Item, bool> predicate) => new[] { new Item(null) };
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in TakeWhile(
                        items,
                        static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_QueryableWhereThroughLazySubsetRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Skip(1)
                        .Take(2)
                        .AsEnumerable())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MutatingSelectAfterWhereDoesNotRefineLoopElement_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(static item => {
                            item.Value = null;
                            return item;
                        }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_WhereThroughIdentitySelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(static item => item))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughIdentityCastSelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(static item => (Item)item))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughIdentityAsSelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(static item => item as Item))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughSameElementCastRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Cast<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IdentityCastedWhereSequenceRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in (IEnumerable<Item>)items.Where(
                        static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IdentityAsWhereSequenceRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(
                                 static item => item.Value is not null) as IEnumerable<Item>)
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IdentityCastedQueryableWhereRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items) {
                    foreach (var item in (IQueryable<Item>)items.Where(
                        static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_UserConvertedWhereSequenceDoesNotRefineLoopElement_Reports",
            """
            #nullable enable
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public sealed class ConvertedItems : IEnumerable<Item> {
                public static explicit operator ConvertedItems(IEnumerable<Item> items) => new();
                public IEnumerator<Item> GetEnumerator() =>
                    new[] { new Item(null) }.AsEnumerable().GetEnumerator();
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in (ConvertedItems)items.Where(
                        static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_WhereThroughSameElementOfTypeRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .OfType<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_StaticWhereThroughSameElementOfTypeRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.OfType<Item>(
                        Enumerable.Where(
                            items,
                            static item => item.Value is not null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_QueryableWhereThroughSameElementOfTypeRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .OfType<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CustomOfTypeDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> OfType(IEnumerable<Item> items) =>
                    new[] { new Item(null) };
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in OfType(
                        items.Where(static item => item.Value is not null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_StaticWhereThroughSameElementCastRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Cast<Item>(
                        Enumerable.Where(
                            items,
                            static item => item.Value is not null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_QueryableWhereThroughSameElementCastRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Cast<Item>())
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CustomCastDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> Cast(IEnumerable<Item> items) =>
                    new[] { new Item(null) };
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Cast(
                        items.Where(static item => item.Value is not null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_WhereThroughIndexedIdentitySelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(static (item, index) => item))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughBlockIdentitySelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(static item => {
                            return item;
                        }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughIdentityMethodSelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static Item Identity(Item item) => item;
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(Identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughIdentityCastMethodSelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static Item Identity(Item item) => (Item)item;
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(Identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughGenericIdentityMethodSelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static T Identity<T>(T value) => value;
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(Identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughIndexedIdentityMethodSelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static Item Identity(Item item, int index) => item;
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(Identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughExactInstanceIdentitySelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public sealed class IdentitySelector {
                public Item Identity(Item item) => item;
            }
            public static class Consumer {
                public static object FirstValue(
                    IEnumerable<Item> items,
                    IdentitySelector selector) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(selector.Identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_WhereThroughExactOverrideIdentitySelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public class IdentitySelector {
                public virtual Item Identity(Item item) => new(null);
            }
            public sealed class ExactIdentitySelector : IdentitySelector {
                public override Item Identity(Item item) => item;
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(new ExactIdentitySelector().Identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_InterfaceIdentitySelectDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public interface IIdentitySelector {
                Item Identity(Item item);
            }
            public static class Consumer {
                public static object FirstValue(
                    IEnumerable<Item> items,
                    IIdentitySelector selector) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(selector.Identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_VirtualIdentitySelectDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public class IdentitySelector {
                public virtual Item Identity(Item item) => item;
            }
            public static class Consumer {
                public static object FirstValue(
                    IEnumerable<Item> items,
                    IdentitySelector selector) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(selector.Identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_MutatingInstanceIdentitySelectDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public sealed class IdentitySelector {
                public Item Reset(Item item) {
                    item.Value = null;
                    return item;
                }
            }
            public static class Consumer {
                public static object FirstValue(
                    IEnumerable<Item> items,
                    IdentitySelector selector) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(selector.Reset))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_WhereThroughLocalIdentityDelegateSelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    Func<Item, Item> identity = static item => item;
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MutatingIdentityMethodSelectDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                private static Item Reset(Item item) {
                    item.Value = null;
                    return item;
                }
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(Reset))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ReassignedIdentityDelegateSelectDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    Func<Item, Item> identity = static item => item;
                    identity = static item => new Item(null);
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(identity))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_QueryableWhereThroughIdentitySelectRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(static item => item))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NonIdentitySelectDoesNotPreserveWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Select(static item => new Item(null)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomTakeAfterWhereDoesNotRefineLoopElement_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> Take(IEnumerable<Item> items, int count) =>
                    new[] { new Item(null) };
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Take(
                        items.Where(static item => item.Value is not null),
                        1))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_MixedChainedWherePredicatesRefineLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Immutable;
            using System.Linq;
            public sealed record Item(object? Value, bool Enabled);
            public static class Consumer {
                public static object FirstValue(ImmutableArray<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Where(static item => item.Enabled))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_StaticWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Where(
                        items,
                        static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReorderedNamedWhereArgumentsRefineLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Where(
                        predicate: static item => item.Value is not null,
                        source: items))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReorderedNamedSubsetArgumentsRefineLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Take(
                        count: 2,
                        source: Enumerable.Skip(
                            count: 1,
                            source: Enumerable.Where(
                                predicate: static item => item.Value is not null,
                                source: items))))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReorderedNamedIdentitySelectArgumentsRefineLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Enumerable.Select(
                        selector: static item => item,
                        source: Enumerable.Where(
                            predicate: static item => item.Value is not null,
                            source: items)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReorderedNamedQueryableWhereArgumentsRefineLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IQueryable<Item> items) {
                    foreach (var item in Queryable.Where(
                        predicate: static item => item.Value is not null,
                        source: items))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IndexedWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(
                        static (item, index) => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IndexedWhereConjunctionRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(
                        static (item, index) => index > 0 && item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IndexedWhereDisjunctionDoesNotRefineLoopElement_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(
                        static (item, index) => index == 0 || item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_IndexedMethodWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static bool HasValue(Item item, int index) => item.Value is not null;
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(HasValue))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MethodGroupWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static bool HasValue(Item item) => item.Value is not null;
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(HasValue))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_InstanceMethodWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public sealed class Validator {
                public bool HasValue(Item item) => item.Value is not null;
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items, Validator validator) {
                    foreach (var item in items.Where(validator.HasValue))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_InterfaceMethodWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public interface IValidator {
                bool HasValue(Item item);
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items, IValidator validator) {
                    foreach (var item in items.Where(validator.HasValue))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_VirtualMethodWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public class Validator {
                public virtual bool HasValue(Item item) => item.Value is not null;
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items, Validator validator) {
                    foreach (var item in items.Where(validator.HasValue))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_MutatingInstanceMethodWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public sealed class Validator {
                public bool Reset(Item item) {
                    item.Value = null;
                    return true;
                }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items, Validator validator) {
                    foreach (var item in items.Where(validator.Reset))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_LocalDelegateWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    Func<Item, bool> hasValue = static item => item.Value is not null;
                    foreach (var item in items.Where(hasValue))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_LocalMethodDelegateWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static bool HasValue(Item item) => item.Value is not null;
                public static object FirstValue(IEnumerable<Item> items) {
                    Func<Item, bool> predicate = HasValue;
                    foreach (var item in items.Where(predicate))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ReassignedLocalDelegateWherePredicate_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    Func<Item, bool> predicate = static item => item.Value is not null;
                    predicate = static item => true;
                    foreach (var item in items.Where(predicate))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CapturedLocalDelegateWherePredicate_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items, object? expected) {
                    Func<Item, bool> predicate = item => item.Value == expected;
                    foreach (var item in items.Where(predicate))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_BlockMethodGroupWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static bool HasValue(Item item) {
                    if (item.Value is null) return false;
                    return true;
                }
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(HasValue))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MutatingMethodGroupWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                private static bool Reset(Item item) {
                    item.Value = null;
                    return true;
                }
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(Reset))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_UnstableMethodGroupWherePredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                private int reads;
                public object? Value => reads++ == 0 ? new object() : null;
            }
            public static class Consumer {
                private static bool HasValue(Item item) => item.Value is not null;
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(HasValue))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_MutatingOuterWhereInvalidatesInnerPredicate_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                private static bool Reset(Item item) {
                    item.Value = null;
                    return true;
                }
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items
                        .Where(static item => item.Value is not null)
                        .Where(static item => Reset(item)))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_BlockWherePredicateRefinesLoopElement_DoesNotReport",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed record Item(object? Value);
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(static item => {
                        return item.Value is not null;
                    }))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MutatedWhereElement_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(static item => item.Value is not null)) {
                        item.Value = null;
                        return item.Value!;
                    }
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ExposedWhereElement_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                public object? Value { get; set; }
            }
            public static class Consumer {
                private static void Reset(Item item) => item.Value = null;
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(static item => item.Value is not null)) {
                        Reset(item);
                        return item.Value!;
                    }
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_UnstableWherePredicateMember_Reports",
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            public sealed class Item {
                private int reads;
                public object? Value => reads++ == 0 ? new object() : null;
            }
            public static class Consumer {
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in items.Where(static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_CustomWhereDoesNotRefineLoopElement_Reports",
            """
            #nullable enable
            using System;
            using System.Collections.Generic;
            public sealed record Item(object? Value);
            public static class Consumer {
                private static IEnumerable<Item> Where(
                    IEnumerable<Item> items,
                    Func<Item, bool> predicate) => items;
                public static object FirstValue(IEnumerable<Item> items) {
                    foreach (var item in Where(items, static item => item.Value is not null))
                        return item.Value!;
                    return new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_NullFailureSentinelImpliesValue_DoesNotReport",
            """
            #nullable enable
            public sealed record Outcome<T>(T? Value, System.Exception? Error) where T : class;
            public static class Consumer {
                public static T Get<T>(Outcome<T> outcome) where T : class {
                    var failure = outcome.Error is not null
                        ? "query failed"
                        : outcome.Value is null ? "query returned no value" : null;
                    if (failure is not null) throw new System.InvalidOperationException(failure);
                    return outcome.Value!;
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_InvertedNullSentinelDoesNotImplyValue_Reports",
            """
            #nullable enable
            public static class Consumer {
                public static object Get(object? value) {
                    var failure = value is null ? null : "unexpected value";
                    if (failure is not null) throw new System.InvalidOperationException(failure);
                    return value!;
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_MutatedNullSentinelInput_Reports",
            """
            #nullable enable
            public static class Consumer {
                public static object Get(object? value) {
                    var failure = value is null ? "missing value" : null;
                    value = null;
                    if (failure is not null) throw new System.InvalidOperationException(failure);
                    return value!;
                }
            }
            """, "SP0044", true);
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
        yield return Case("NullForgivingOperator_ChainedConditionalOutputAlias_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var isExact = factory.TryGet(out var expression);
                    var shouldUseExpression = isExact;
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_AssignedConditionalOutputAlias_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    bool shouldUseExpression;
                    shouldUseExpression = factory.TryGet(out var expression);
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_AssignedConditionalOutputPolarity_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(false)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    bool shouldUseExpression;
                    shouldUseExpression = !factory.TryGet(out var expression);
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SwitchArmConditionalOutput_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var shouldUseExpression = factory.TryGet(out var expression);
                    return shouldUseExpression switch {
                        true => expression!,
                        false => new object()
                    };
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SwitchControlFlowConditionalOutputs_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryTrue([NotNullWhen(true)] out object? value);
                bool TryFalse([NotNullWhen(false)] out object? value);
            }
            public static class Consumer {
                public static object FalseExpressionArm(IFactory factory) {
                    var failed = factory.TryFalse(out var expression);
                    return failed switch {
                        false => expression!,
                        true => new object()
                    };
                }
                public static object TrueStatementCase(IFactory factory) {
                    var succeeded = factory.TryTrue(out var expression);
                    switch (succeeded) {
                        case true:
                            return expression!;
                        default:
                            return new object();
                    }
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SwitchGuardMutatedOutput_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                private static bool Reset(ref object? value) {
                    value = null;
                    return true;
                }
                public static object Get(IFactory factory) {
                    var succeeded = factory.TryGet(out var expression);
                    return succeeded switch {
                        true when Reset(ref expression) => expression!,
                        _ => new object()
                    };
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_SwitchStatementGuardMutatedOutput_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                private static bool Reset(ref object? value) {
                    value = null;
                    return true;
                }
                public static object Get(IFactory factory) {
                    var succeeded = factory.TryGet(out var expression);
                    switch (succeeded) {
                        case true when Reset(ref expression):
                            return expression!;
                        default:
                            return new object();
                    }
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_PureSwitchGuardPreservesOutput_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory, bool enabled) {
                    var succeeded = factory.TryGet(out var expression);
                    return succeeded switch {
                        true when enabled => expression!,
                        _ => new object()
                    };
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SwitchGuardAssignsNonNullOutput_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var succeeded = factory.TryGet(out var expression);
                    return succeeded switch {
                        true when (expression = new object()) is not null => expression!,
                        _ => new object()
                    };
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SwitchStatementGuardAssignsNonNullOutput_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var succeeded = factory.TryGet(out var expression);
                    switch (succeeded) {
                        case true when (expression = new object()) is not null:
                            return expression!;
                        default:
                            return new object();
                    }
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_SwitchGuardAssignsNullOutput_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var succeeded = factory.TryGet(out var expression);
                    return succeeded switch {
                        true when (expression = null) is null => expression!,
                        _ => new object()
                    };
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_AmbiguousSwitchSection_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var succeeded = factory.TryGet(out var expression);
                    switch (succeeded) {
                        case true:
                        case false:
                            return expression!;
                    }
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_ReassignedConditionalOutputAlias_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    bool shouldUseExpression;
                    shouldUseExpression = factory.TryGet(out var expression);
                    shouldUseExpression = true;
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_BooleanComparedConditionalAlias_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var shouldUseExpression = factory.TryGet(out var expression) == true;
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IdentityCastConditionalAlias_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var shouldUseExpression = (bool)factory.TryGet(out var expression);
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_IdentityCastPolarity_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(false)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var shouldUseExpression = (bool)!factory.TryGet(out var expression);
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_UserBooleanConversion_RemainsConservative",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public readonly struct Flag {
                private readonly bool value;
                public Flag(bool value) => this.value = value;
                public static implicit operator Flag(bool value) => new(value);
                public static explicit operator bool(Flag value) => true;
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var shouldUseExpression = (bool)(Flag)factory.TryGet(out var expression);
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_BooleanPatternConditionalAlias_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var shouldUseExpression = factory.TryGet(out var expression) is true;
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_BooleanPatternPolarity_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object NotFalse(IFactory factory) {
                    var shouldUseExpression = factory.TryGet(out var expression) is not (false);
                    return shouldUseExpression ? expression! : new object();
                }
                public static object FalsePattern(IFactory factory) {
                    var shouldSkipExpression = factory.TryGet(out var expression) is false;
                    return !shouldSkipExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ConjoinedConditionalOutputAlias_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory, bool enabled) {
                    var shouldUseExpression = enabled && factory.TryGet(out var expression);
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_XorFalseConditionalOutputAlias_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var shouldUseExpression = factory.TryGet(out var expression) ^ false;
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_ConstantBooleanOperatorIdentities_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryTrue([NotNullWhen(true)] out object? value);
                bool TryFalse([NotNullWhen(false)] out object? value);
            }
            public static class Consumer {
                public static object AndTrue(IFactory factory) {
                    var failed = factory.TryFalse(out var expression) && true;
                    return !failed ? expression! : new object();
                }
                public static object OrFalse(IFactory factory) {
                    var shouldUseExpression = false || factory.TryTrue(out var expression);
                    return shouldUseExpression ? expression! : new object();
                }
                public static object XorTrue(IFactory factory) {
                    var shouldSkipExpression = true ^ factory.TryTrue(out var expression);
                    return !shouldSkipExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_AbsorbingBooleanConstantDoesNotInventContract",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var alwaysFalse = factory.TryGet(out var expression) && false;
                    return !alwaysFalse ? expression! : new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_TernaryConditionalOutputAlias_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory, bool enabled) {
                    object? expression = null;
                    var shouldUseExpression = enabled ? factory.TryGet(out expression) : false;
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_TernaryImplicationPolarity_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryTrue([NotNullWhen(true)] out object? value);
                bool TryFalse([NotNullWhen(false)] out object? value);
            }
            public static class Consumer {
                public static object FalseContract(IFactory factory, bool enabled) {
                    object? expression = null;
                    var failed = enabled ? factory.TryFalse(out expression) : true;
                    return !failed ? expression! : new object();
                }
                public static object ReversedArm(IFactory factory, bool enabled) {
                    object? expression = null;
                    var shouldUseExpression = enabled ? false : factory.TryTrue(out expression);
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NonImplyingTernaryBranch_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory, bool enabled) {
                    object? expression = null;
                    var maybeUseExpression = enabled ? factory.TryGet(out expression) : true;
                    return maybeUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_LogicalImplicationPolarity_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryTrue([NotNullWhen(true)] out object? value);
                bool TryFalse([NotNullWhen(false)] out object? value);
            }
            public static class Consumer {
                public static object DisjoinedFalse(IFactory factory, bool disabled) {
                    var failed = disabled || factory.TryFalse(out var expression);
                    return !failed ? expression! : new object();
                }
                public static object BitwiseConjoinedTrue(IFactory factory, bool enabled) {
                    var shouldUseExpression = enabled & factory.TryTrue(out var expression);
                    return shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_MultipleImpliedConditionalOutputs_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryFirst([NotNullWhen(true)] out object? value);
                bool TrySecond([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var shouldUseExpressions =
                        factory.TryFirst(out var first) && factory.TrySecond(out var second);
                    return shouldUseExpressions ? (first!, second!) : (new object(), new object());
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_NonImplyingLogicalBranch_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory, bool enabled) {
                    var shouldUseExpression = enabled && factory.TryGet(out var expression);
                    return !shouldUseExpression ? expression! : new object();
                }
            }
            """, "SP0044", true);
        yield return Case("NullForgivingOperator_BooleanComparisonPolarity_DoesNotReport",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object NotFalse(IFactory factory) {
                    var shouldUseExpression = factory.TryGet(out var expression) != false;
                    return shouldUseExpression ? expression! : new object();
                }
                public static object FalseOnLeft(IFactory factory) {
                    var shouldSkipExpression = false == factory.TryGet(out var expression);
                    return !shouldSkipExpression ? expression! : new object();
                }
            }
            """, "SP0044", false);
        yield return Case("NullForgivingOperator_CopiedAliasSurvivesLaterSourceCapture",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var isExact = factory.TryGet(out var expression);
                    var shouldUseExpression = isExact;
                    System.Action mutateSource = () => isExact = false;
                    mutateSource();
                    return shouldUseExpression ? expression! : new object();
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
        yield return Case("NullForgivingOperator_LocalFunctionMutatedConditionalAlias_Reports",
            """
            #nullable enable
            using System.Diagnostics.CodeAnalysis;
            public interface IFactory {
                bool TryGet([NotNullWhen(true)] out object? value);
            }
            public static class Consumer {
                public static object Get(IFactory factory) {
                    var isExact = factory.TryGet(out var expression);
                    Mutate();
                    return isExact ? expression! : new object();
                    void Mutate() => isExact = true;
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
    [TestCase("^a|b")]
    [TestCase("a|b$")]
    [TestCase("^a|b$")]
    [TestCase(@"\Aa|b")]
    [TestCase(@"a|b\z")]
    public void RegexNormalizer_TopLevelAlternationWithEdgeAnchor_FailsSafely(
        string pattern) =>
        Assert.That(
            Z3RegexCompiler.TryNormalize(
                pattern,
                RegexOptions.None,
                out _),
            Is.False);
    [TestCase("^(a|b)")]
    [TestCase(@"^a\|b")]
    [TestCase("^[a|b]")]
    public void RegexNormalizer_NestedOrLiteralAlternationWithAnchor_Normalizes(
        string pattern) =>
        Assert.That(
            Z3RegexCompiler.TryNormalize(
                pattern,
                RegexOptions.None,
                out _),
            Is.True);
    [TestCase("[a-z-[aeiou]]", 13)]
    [TestCase(@"[a\-[b]]", 7)]
    [TestCase(@"[\]]", 4)]
    [TestCase("[]]", 3)]
    [TestCase("[^]]", 4)]
    public void RegexNormalizer_CharacterClassEndHonorsEscapedHyphen(
        string pattern,
        int expectedEnd) {
        Assert.That(
            Z3RegexCompiler.TryFindCharacterClassEnd(
                pattern,
                0,
                out var end),
            Is.True);
        Assert.That(end, Is.EqualTo(expectedEnd));
    }
    [Test]
    public void PredicateImplication_UsesAmbientSmtService() {
        var source = """
            #nullable enable
            using System;
            using System.Linq;
            public static class Consumer {
                public static object FirstValue(bool include) {
                    var lengths = new[] { -1, 1 }
                        .Where(value => {
                            if (value > 0)
                                return false;
                            return include;
                        })
                        .ToArray();
                    foreach (var _ in Array.CreateInstance(typeof(int), lengths))
                        return ((object?)null)!;
                    return new object();
                }
            }
            """;
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            source,
            new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview));
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        var compilation =
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "PredicateImplicationAmbientSmt",
                [tree],
                trustedPlatformAssemblies
                    .Split(Path.PathSeparator)
                    .Select(static path =>
                        Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path)),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var suppression = tree.GetRoot()
            .DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.PostfixUnaryExpressionSyntax>()
            .Single();
        using var smt = new SharpProof.Symbolic.Smt.SmtAnalysisService(
            SharpProof.Symbolic.Smt.SmtAnalysisOptions.Default);
        _ = new SharpProof.Symbolic.SymbolicInvariantService().AnalyzeAt(
            suppression,
            model,
            smt);
        Assert.That(smt.ExecutedQueryCount, Is.GreaterThan(0));
    }
    [TestCase(true)]
    [TestCase(false)]
    public void SmtVariableNames_DistinguishSymbolsAtSameOffsetInDifferentTrees(
        bool includeFilePaths) {
        var firstTree =
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                "class A { static int Threshold; }",
                path: includeFilePaths ? "A.cs" : string.Empty);
        var secondTree =
            Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
                "class B { static int Threshold; }",
                path: includeFilePaths ? "B.cs" : string.Empty);
        var compilation =
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "DistinctSourceTrees",
                [firstTree, secondTree]);
        var first = compilation.GetTypeByMetadataName("A")!
            .GetMembers("Threshold")
            .Single();
        var second = compilation.GetTypeByMetadataName("B")!
            .GetMembers("Threshold")
            .Single();
        Assert.That(
            SharpProof.Symbolic.SymbolicFactFactory.GetSmtVariableName(first),
            Is.Not.EqualTo(
                SharpProof.Symbolic.SymbolicFactFactory.GetSmtVariableName(second)));
    }
    [TestCase(true, true, true)]
    [TestCase(true, false, false)]
    public void SymbolicStateMerge_PreservesOnlyUniversalContradiction(
        bool firstContradictory,
        bool secondContradictory,
        bool expectedContradictory) {
        var merged =
            SharpProof.Symbolic.Ir.SymbolicStateMerger.MergePathStatesAcrossAll(
                [
                    new SharpProof.Symbolic.Ir.SymbolicState(
                        isContradictory: firstContradictory),
                    new SharpProof.Symbolic.Ir.SymbolicState(
                        isContradictory: secondContradictory)
                ],
                static (left, right) =>
                    SharpProof.Symbolic.Ir.SymbolicState.CreateProofFactKey(left) ==
                    SharpProof.Symbolic.Ir.SymbolicState.CreateProofFactKey(right),
                0);
        Assert.That(
            merged.IsContradictory,
            Is.EqualTo(expectedContradictory));
    }
    [Test]
    public void SmtAnalysisService_DisposedSessionRaceReturnsUnknown() {
        using var smt = new SharpProof.Symbolic.Smt.SmtAnalysisService(
            SharpProof.Symbolic.Smt.SmtAnalysisOptions.Default,
            static () => throw new ObjectDisposedException("test-session"));
        SharpProof.ProofCore.Analysis.AnalysisProofResult? result = null;
        Assert.That(
            () => result = smt.ClassifyPathFeasibility(
                [new SharpProof.ProofCore.Smt.SmtBooleanConstant(true)]),
            Throws.Nothing);
        Assert.That(result!.Reason, Is.EqualTo("smt_disposed"));
    }
    [Test]
    public void AnalysisSession_InvalidBudgetIsRejectedBeforeReadingFile() {
        var options = new SharpProof.Symbolic.SharpProofAnalysisOptions(
            new SharpProof.Symbolic.SharpProofAnalysisBudget(
                MaxMergedIfElseFacts: 0));
        Assert.That(
            () => SharpProof.Symbolic.SharpProofAnalysisSession.FromFile(
                Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString("N"),
                    "missing.cs"),
                options),
            Throws.TypeOf<ArgumentOutOfRangeException>());
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
    [Test]
    public void PotentialBugRegression_ConstantOnLeftDoesNotInventPositiveBound() {
        const string source = """
            public static class C {
                public static int M(int y) {
                    if (-1 < y) {
                        var x = y;
                        return x;
                    }
                    return 0;
                }
            }
            """;
        Assert.That(
            AnalyzeProofAtMarker(source, "return x", "x > 0").ProofFacts.Single().Status,
            Is.Not.EqualTo("ProvenTrue"));
    }
    [Test]
    public void PotentialBugRegression_InexactStateMetadataSurvivesRemovalAndMerge() {
        var provenance = new SharpProof.Symbolic.Ir.SymbolicLoweringProvenance(
            "test",
            default,
            "inexact");
        var inexact = new SharpProof.Symbolic.Ir.SymbolicState(
            facts: [
                new SharpProof.Symbolic.Ir.SymbolicFact(
                    new SharpProof.Symbolic.Ir.SymbolicTruthAtom(
                        new SharpProof.Symbolic.Ir.SymbolicVariableTerm(
                            "removed",
                            SharpProof.ProofCore.Smt.SmtValueKind.Bool)),
                    true,
                    SharpProof.Symbolic.Ir.SymbolicFactConfidence.Exact,
                    "test",
                    default,
                    null,
                    null)
            ],
            isExact: false,
            unknownReason: SharpProof.Symbolic.SymbolicUnknownReason.UnsupportedIrEncoding,
            provenance: [provenance]);
        var removed =
            SharpProof.Symbolic.Ir.SymbolicIrReferenceScanner.RemoveVariableReferences(
                inexact,
                "removed");
        var merged =
            SharpProof.Symbolic.Ir.SymbolicStateMerger.MergePathStatesAcrossAll(
                [new SharpProof.Symbolic.Ir.SymbolicState(), inexact],
                static (left, right) =>
                    SharpProof.Symbolic.Ir.SymbolicState.CreateProofFactKey(left) ==
                    SharpProof.Symbolic.Ir.SymbolicState.CreateProofFactKey(right),
                0);
        Assert.Multiple(() => {
            Assert.That(removed.IsExact, Is.False);
            Assert.That(
                removed.UnknownReason,
                Is.EqualTo(
                    SharpProof.Symbolic.SymbolicUnknownReason.UnsupportedIrEncoding));
            Assert.That(removed.Provenance, Does.Contain(provenance));
            Assert.That(merged.IsExact, Is.False);
            Assert.That(
                merged.UnknownReason,
                Is.EqualTo(
                    SharpProof.Symbolic.SymbolicUnknownReason.UnsupportedIrEncoding));
            Assert.That(merged.Provenance, Does.Contain(provenance));
        });
    }
    [Test]
    public void PotentialBugRegression_StringRemoveOneArgumentUsesStartIndexAsLength() {
        const string source = """
            public static class C {
                public static int M() {
                    var text = "hello".Remove(2);
                    return text.Length;
                }
            }
            """;
        Assert.That(
            AnalyzeProofAtMarker(
                source,
                "return text.Length",
                "text.Length == 2").ProofFacts.Single().Status,
            Is.EqualTo("ProvenTrue"));
    }
    [Test]
    public void PotentialBugRegression_MixedDefaultSwitchSectionIncludesOwnCase() {
        const string source = """
            public static class C {
                public static int M(int value) {
                    switch (value) {
                        case 1:
                        default:
                            return value;
                        case 2:
                            return 0;
                    }
                }
            }
            """;
        Assert.That(
            AnalyzeProofAtMarker(
                source,
                "return value",
                "value == 1").ProofFacts.Single().Status,
            Is.Not.EqualTo("ProvenFalse"));
    }
    [TestCase(@"b(?<=ab)c", "abc")]
    [TestCase(@"a(?=bc)b", "abc")]
    [TestCase(@"\A(a(?=bc)b)c\z", "abc")]
    public void PotentialBugRegression_UnsafeLocalLookaroundReturnsUnknown(
        string pattern,
        string value) {
        using var solver = new SharpProof.ProofCore.Smt.SmtSolver();
        var text = new SharpProof.ProofCore.Smt.SmtVariable(
            "lookaround-text",
            SharpProof.ProofCore.Smt.SmtValueKind.String);
        var result = solver.CheckSatisfiability(
            [
                new SharpProof.ProofCore.Smt.SmtRegexMatchFormula(
                    text,
                    pattern),
                new SharpProof.ProofCore.Smt.SmtStringStartsWithFormula(
                    text,
                    new SharpProof.ProofCore.Smt.SmtStringConstant(
                        value.Substring(0, 1))),
                new SharpProof.ProofCore.Smt.SmtBinaryFormula(
                    SharpProof.ProofCore.Smt.SmtBinaryOperator.Equal,
                    new SharpProof.ProofCore.Smt.SmtStringLengthTerm(text),
                    new SharpProof.ProofCore.Smt.SmtIntegerConstant(value.Length))
            ],
            TimeSpan.FromMilliseconds(500));
        Assert.That(
            result.Feasibility,
            Is.EqualTo(SharpProof.ProofCore.Smt.Feasibility.Unknown));
    }
    [Test]
    public void PotentialBugRegression_UnicodeCategoryHonorsIgnoreCase() {
        using var solver = new SharpProof.ProofCore.Smt.SmtSolver();
        var text = new SharpProof.ProofCore.Smt.SmtVariable(
            "category-text",
            SharpProof.ProofCore.Smt.SmtValueKind.String);
        var result = solver.CheckSatisfiability(
            [
                new SharpProof.ProofCore.Smt.SmtRegexMatchFormula(
                    text,
                    @"\A\p{Lu}\z",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
                new SharpProof.ProofCore.Smt.SmtBinaryFormula(
                    SharpProof.ProofCore.Smt.SmtBinaryOperator.Equal,
                    text,
                    new SharpProof.ProofCore.Smt.SmtStringConstant("a"))
            ],
            TimeSpan.FromMilliseconds(500));
        Assert.That(
            result.Feasibility,
            Is.EqualTo(SharpProof.ProofCore.Smt.Feasibility.Satisfiable));
    }
    [Test]
    public void PotentialBugRegression_ListSliceDesignationPreservesMinimumLength() {
        const string source = """
            public static class C {
                public static int M(int[] values) {
                    if (values is [1, 2, .. var rest])
                        return values[1];
                    return 0;
                }
            }
            """;
        Assert.That(
            AnalyzeProofAtMarker(
                source,
                "return values[1]",
                "values.Length >= 2").ProofFacts.Single().Status,
            Is.EqualTo("ProvenTrue"));
    }
    [Test]
    public void PotentialBugRegression_UlongAboveLongMaxIsReachable() {
        const string source = """
            public static class C {
                public static int M(ulong value) {
                    if (value > 9223372036854775807UL)
                        return 1;
                    return 0;
                }
            }
            """;
        Assert.That(
            AnalyzeProofAtMarker(
                source,
                "return 1",
                "value == 0").ProofFacts.Single().Status,
            Is.Not.EqualTo("Unreachable"));
    }
    [TestCase(
        """
        using System;
        public static class C {
            public static int M(int[] values) {
                values[0] = 1;
                Span<int> alias = values;
                alias[0] = 2;
                return values[0];
            }
        }
        """)]
    [TestCase(
        """
        public static class C {
            public static int M() {
                var value = 1;
                ref var alias = ref value;
                alias = 2;
                return value;
            }
        }
        """)]
    public void PotentialBugRegression_AliasWritesDoNotLeaveStaleValueFacts(
        string source) =>
        Assert.That(
            AnalyzeProofAtMarker(
                source,
                "return",
                source.Contains("values", StringComparison.Ordinal)
                    ? "values[0] == 1"
                    : "value == 1").ProofFacts.Single().Status,
            Is.Not.EqualTo("ProvenTrue"));
    [TestCase(
        """
        using System.Collections.Generic;
        public static class C {
            public static int M(string key) {
                var values = new Dictionary<string, int>();
                return values[key];
            }
        }
        """,
        "System.Collections.Generic.KeyNotFoundException")]
    [TestCase(
        """
        using System.Collections.Generic;
        public static class C {
            public static int M(int capacity) {
                var values = new List<int>(capacity);
                return values.Count;
            }
        }
        """,
        "System.ArgumentOutOfRangeException")]
    [TestCase(
        """
        public static class C {
            public static string M(int value, string format) =>
                value.ToString(format);
        }
        """,
        "System.FormatException")]
    [TestCase(
        """
        public static class C {
            public static string M(string oldValue, string newValue) =>
                "abc".Replace(oldValue, newValue);
        }
        """,
        "System.ArgumentException")]
    public void PotentialBugRegression_ThrowingFrameworkMembersAreNotProvenSafe(
        string source,
        string expectedException) {
        var effects = AnalyzeEffectsAtMarker(source, "M(");
        Assert.Multiple(() => {
            Assert.That(
                effects.DoesNotThrow,
                Is.Not.EqualTo(SharpProof.Symbolic.SharpProofVerdict.Proven));
            Assert.That(
                effects.ThrownExceptions,
                Does.Contain(expectedException));
        });
    }
    [Test]
    public async Task PotentialBugRegression_AsyncEnsuresUsesBodyReturnNullability() {
        var diagnostics = await AnalyzeAsync("""
            #nullable enable
            using System.Threading.Tasks;
            using SharpProof.Attributes;
            public static class C {
                [Ensures("result != null")]
                public static async Task<string?> M() {
                    await Task.Yield();
                    return null;
                }
            }
            """);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Contain("SP0018"));
    }
    [Test]
    public void PotentialBugRegression_ArrayLengthDoesNotPoisonComplexity() {
        const string source = """
            public static class C {
                public static int M(int[] values) {
                    var total = 0;
                    for (var index = 0; index < values.Length; index++)
                        total++;
                    return total;
                }
            }
            """;
        Assert.That(
            AnalyzeComplexityAtMarker(source, "M("),
            Does.Not.Contain("Unknown"));
    }
    [Test]
    public void PotentialBugRegression_ProductLoopBoundRetainsMultiplication() {
        const string source = """
            public static class C {
                public static int M(int left, int right) {
                    var total = 0;
                    for (var index = 0; index < left * right; index++)
                        total++;
                    return total;
                }
            }
            """;
        Assert.That(
            AnalyzeComplexityAtMarker(source, "M("),
            Does.Contain("*"));
    }
    [Test]
    public void PotentialBugRegression_ReorderedRegexArgumentsUseParameterOrdinals() {
        const string source = """
            using System.Text.RegularExpressions;
            public static class C {
                public static int M() {
                    if (Regex.IsMatch(pattern: "a+", input: "aaa"))
                        return 1;
                    return 0;
                }
            }
            """;
        Assert.That(
            AnalyzeProofAtMarker(
                source,
                "return 1",
                "true").ProofFacts.Single().Status,
            Is.Not.EqualTo("Unreachable"));
    }
    [Test]
    public void PotentialBugRegression_ReorderedSubstringArgumentsUseParameterOrdinals() {
        const string source = """
            public static class C {
                public static int M(string text) {
                    if (text.Substring(length: 0, startIndex: 3) == "abc")
                        return 1;
                    return 0;
                }
            }
            """;
        Assert.That(
            AnalyzeProofAtMarker(
                source,
                "return 1",
                "text == \"abcdef\"").ProofFacts.Single().Status,
            Is.Not.EqualTo("ProvenTrue"));
    }
    [Test]
    public void PotentialBugRegression_GuardedDiscardSwitchCanThrow() {
        const string source = """
            public static class C {
                public static int M(int value, bool enabled) =>
                    value switch {
                        _ when enabled => 1
                    };
            }
            """;
        Assert.That(
            AnalyzeEffectsAtMarker(source, "M(").DoesNotThrow,
            Is.Not.EqualTo(SharpProof.Symbolic.SharpProofVerdict.Proven));
    }
    [TestCase("Exchange")]
    [TestCase("Add")]
    [TestCase("CompareExchange")]
    public void PotentialBugRegression_InterlockedWritesArgumentState(
        string method) {
        var call = method == "CompareExchange"
            ? "System.Threading.Interlocked.CompareExchange(ref value, 1, 0)"
            : "System.Threading.Interlocked." + method + "(ref value, 1)";
        var source = """
            public static class C {
                public static int M(ref int value) {
                    CALL;
                    return value;
                }
            }
            """.Replace("CALL", call, StringComparison.Ordinal);
        Assert.That(
            AnalyzeEffectsAtMarker(source, "M(").Effects.HasFlag(
                SharpProof.Attributes.SharpProofEffect.WritesArgumentState),
            Is.True);
    }
    [Test]
    public void PotentialBugRegression_VolatileWriteReportsArgumentMutation() {
        const string source = """
            public static class C {
                public static void M(ref int value) {
                    System.Threading.Volatile.Write(ref value, 1);
                }
            }
            """;
        Assert.That(
            AnalyzeEffectsAtMarker(source, "M(").Effects.HasFlag(
                SharpProof.Attributes.SharpProofEffect.WritesArgumentState),
            Is.True);
    }
    [Test]
    public void PotentialBugRegression_BitConverterSpanUsesArgumentException() {
        const string source = """
            using System;
            public static class C {
                public static int M(ReadOnlySpan<byte> value) =>
                    BitConverter.ToInt32(value);
            }
            """;
        var effects = AnalyzeEffectsAtMarker(source, "M(");
        Assert.Multiple(() => {
            Assert.That(
                effects.ThrownExceptions,
                Does.Contain("System.ArgumentException"));
            Assert.That(
                effects.ThrownExceptions,
                Does.Not.Contain("System.ArgumentOutOfRangeException"));
        });
    }
    [Test]
    public async Task PotentialBugRegression_AllowedBaseExceptionAcceptsHazardSubtype() {
        var diagnostics = await AnalyzeAsync("""
            using SharpProof.Attributes;
            public static class C {
                [AllowedExceptions(typeof(System.ArgumentException))]
                public static string M() => "a".Substring(2);
            }
            """);
        Assert.That(
            diagnostics.Select(static diagnostic => diagnostic.Id),
            Does.Not.Contain("SP0030"));
    }
    [Test]
    public void PotentialBugRegression_UnrelatedCaughtThrowDoesNotHideThrowNull() {
        const string source = """
            public static class C {
                public static void M() {
                    try {
                        throw new System.InvalidOperationException();
                    }
                    catch (System.InvalidOperationException) {
                    }
                    throw null;
                }
            }
            """;
        Assert.That(
            AnalyzeEffectsAtMarker(source, "M(").DoesNotThrow,
            Is.EqualTo(SharpProof.Symbolic.SharpProofVerdict.Disproven));
    }
    [Test]
    public void PotentialBugRegression_ReturnedLambdasMergeEffects() {
        const string source = """
            using System;
            public static class C {
                private static int state;
                private static Func<int> Create(bool mutate) {
                    if (mutate)
                        return () => ++state;
                    return () => 0;
                }
                public static int M(bool mutate) => Create(mutate)();
            }
            """;
        Assert.That(
            AnalyzeEffectsAtMarker(source, "M(").Effects.HasFlag(
                SharpProof.Attributes.SharpProofEffect.WritesStaticState),
            Is.True);
    }
    [TestCase(
        """
        using System.Collections.Generic;
        public sealed class Box {
            public Box(List<int> values) { }
        }
        public static class C {
            public static void M(List<int> values) {
                Box box = new(values);
            }
        }
        """)]
    [TestCase(
        """
        using System.Collections.Generic;
        public static class C {
            public static void M(List<int> values) {
                values?.Clear();
            }
        }
        """)]
    public void PotentialBugRegression_MutationInventoryTracksImplicitExposures(
        string source) {
        var (root, model) = CreateSemanticModel(source);
        var method = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "M");
        var parameter =
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(
                model,
                method.ParameterList.Parameters[0])!;
        var inventory =
            SharpProof.Symbolic.SymbolicMutationInventory.Create(
                method,
                model,
                CancellationToken.None);
        Assert.That(inventory.ExposesSymbol(parameter, mutableOnly: true), Is.True);
    }
    [Test]
    public void PotentialBugRegression_NegativeConfiguredFlagsAreRejected() {
        using var document = System.Text.Json.JsonDocument.Parse(
            """{"effects":-1}""");
        var method = typeof(SharpProof.Analyzer.Configuration.ConfiguredEffectContractResolver)
            .GetMethod(
                "TryReadFlags",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(typeof(SharpProof.Attributes.SharpProofEffect));
        var arguments = new object?[] {
            document.RootElement,
            "effects",
            SharpProof.Attributes.SharpProofEffect.None,
            null
        };
        Assert.That(
            () => method.Invoke(null, arguments),
            Throws.Nothing);
        Assert.That(arguments[3], Is.EqualTo((SharpProof.Attributes.SharpProofEffect)(-1L)));
        Assert.That((bool)method.Invoke(null, arguments)!, Is.False);
    }
    [Test]
    public void PotentialBugRegression_NonVariableReferenceNamesAreStructural() {
        var method = typeof(SharpProof.Symbolic.Ir.SymbolicIrFormulaEncoder)
            .GetMethod(
                "ReferenceName",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static)!;
        var first = new SharpProof.ProofCore.Smt.SmtConditionalFormula(
            new SharpProof.ProofCore.Smt.SmtBooleanConstant(true),
            new SharpProof.ProofCore.Smt.SmtVariable(
                "first",
                SharpProof.ProofCore.Smt.SmtValueKind.Reference),
            new SharpProof.ProofCore.Smt.SmtNullConstant(),
            SharpProof.ProofCore.Smt.SmtValueKind.Reference);
        var second = new SharpProof.ProofCore.Smt.SmtConditionalFormula(
            new SharpProof.ProofCore.Smt.SmtBooleanConstant(true),
            new SharpProof.ProofCore.Smt.SmtVariable(
                "second",
                SharpProof.ProofCore.Smt.SmtValueKind.Reference),
            new SharpProof.ProofCore.Smt.SmtNullConstant(),
            SharpProof.ProofCore.Smt.SmtValueKind.Reference);
        Assert.That(
            method.Invoke(null, [first]),
            Is.Not.EqualTo(method.Invoke(null, [second])));
    }
    [Test]
    public void PotentialBugRegression_OuterLambdaShadowingPreventsReplacement() {
        const string source = """
            public static class C {
                public static void M(int p) { }
            }
            """;
        var (root, model) = CreateSemanticModel(source);
        var declaration = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single();
        var method =
            Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetDeclaredSymbol(
                model,
                declaration)!;
        Assert.That(
            SharpProof.Analyzer.RequiresContractHelpers.TryRewriteForArguments(
                "xs.Any(p => ys.Any(q => p > q))",
                method,
                method,
                new Dictionary<
                    string,
                    Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax> {
                    ["p"] =
                        Microsoft.CodeAnalysis.CSharp.SyntaxFactory
                            .ParseExpression("42")
                },
                out var rewritten),
            Is.True);
        Assert.That(rewritten, Does.Contain("p > q"));
        Assert.That(rewritten, Does.Not.Contain("42 > q"));
    }
    [Test]
    public void PotentialBugRegression_LinuxDlopenUsesGlobalFlag() {
        Assert.That(
            SharpProof.Symbolic.Smt.SmtNativeLibraryBootstrap.GetDlopenFlags(
                System.Runtime.InteropServices.OSPlatform.Linux),
            Is.EqualTo(0x102));
        Assert.That(
            SharpProof.Symbolic.Smt.SmtNativeLibraryBootstrap.GetDlopenFlags(
                System.Runtime.InteropServices.OSPlatform.OSX),
            Is.EqualTo(0xA));
    }
    [Test]
    public void PotentialBugRegression_ReorderedComplexityArgumentsUseParameterOrdinals() {
        const string source = """
            public static class C {
                private static int Sum(int[] a, int[] b) {
                    var total = 0;
                    foreach (var value in a)
                        total += value;
                    return total;
                }
                public static int M(int[] large, int[] small) =>
                    Sum(b: small, a: large);
            }
            """;
        var (root, model) = CreateSemanticModel(source);
        var invocation = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
            .Single(candidate => candidate.ToString().StartsWith(
                "Sum(",
                StringComparison.Ordinal));
        var operation =
            (Microsoft.CodeAnalysis.Operations.IInvocationOperation)
            model.GetOperation(invocation)!;
        Assert.That(
            SharpProof.Symbolic.SymbolicComplexityAnalysisSession
                .FindArgumentByOrdinal(operation.Arguments, 0)!
                .Value.Syntax.ToString(),
            Is.EqualTo("large"));
    }
    private static (
        Microsoft.CodeAnalysis.SyntaxNode Root,
        Microsoft.CodeAnalysis.SemanticModel Model)
        CreateSemanticModel(string source) {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            source,
            new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(
                Microsoft.CodeAnalysis.CSharp.LanguageVersion.Preview));
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ??
            throw new InvalidOperationException(
                "Trusted platform assemblies are unavailable.");
        var compilation =
            Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
                "PotentialBugRegression",
                [tree],
                trustedPlatformAssemblies
                    .Split(Path.PathSeparator)
                    .Select(static path =>
                        Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(path)),
                new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(
                    Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary));
        return (tree.GetRoot(), compilation.GetSemanticModel(tree));
    }
    private static SharpProof.Symbolic.MethodEffects AnalyzeEffectsAtMarker(
        string source,
        string marker) {
        using var session =
            SharpProof.Symbolic.SharpProofAnalysisSession.FromText(source);
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        var result = session.Analyze(
            new SharpProof.Symbolic.SharpProofAnalysisRequest(
                new SharpProof.Symbolic.SharpProofTarget(
                    SharpProof.Symbolic.SharpProofTargetKind.Position,
                    Position: position),
                SharpProof.Symbolic.SharpProofAnalysisFacet.Effects));
        return result.MethodEffects ??
               throw new InvalidOperationException("Method effects were unavailable.");
    }
    private static string AnalyzeComplexityAtMarker(
        string source,
        string marker) {
        using var session =
            SharpProof.Symbolic.SharpProofAnalysisSession.FromText(source);
        var position = source.IndexOf(marker, StringComparison.Ordinal);
        var result = session.Analyze(
            new SharpProof.Symbolic.SharpProofAnalysisRequest(
                new SharpProof.Symbolic.SharpProofTarget(
                    SharpProof.Symbolic.SharpProofTargetKind.Position,
                    Position: position),
                SharpProof.Symbolic.SharpProofAnalysisFacet.Complexity));
        return result.Complexity ??
               throw new InvalidOperationException("Complexity was unavailable.");
    }
    private static SharpProof.Symbolic.SharpProofAnalysisResult
        AnalyzeProofAtMarker(
            string source,
            string marker,
            string condition) =>
        AnalyzeProofAtPosition(
            source,
            source.IndexOf(marker, StringComparison.Ordinal),
            condition);
    private static SharpProof.Symbolic.SharpProofAnalysisResult
        AnalyzeProofAtPosition(
            string source,
            int position,
            string condition) {
        if (position < 0)
            throw new InvalidOperationException("Marker was not found.");
        var lineStart = source.LastIndexOf('\n', position);
        var line = source.Take(position).Count(static value => value == '\n') + 1;
        var column = position - lineStart;
        using var session =
            SharpProof.Symbolic.SharpProofAnalysisSession.FromText(source);
        return session.Analyze(new SharpProof.Symbolic.SharpProofAnalysisRequest(
            new SharpProof.Symbolic.SharpProofTarget(
                SharpProof.Symbolic.SharpProofTargetKind.Point,
                Line: line,
                Column: column),
            SharpProof.Symbolic.SharpProofAnalysisFacet.ProofFacts,
            condition));
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
