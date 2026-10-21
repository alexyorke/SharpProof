using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class ConvertedCoalescingChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("CoalesceUserConversion", "static Wrap<int> M(Wrap<int>? value,int fallback)=>value??fallback;struct Wrap<T>{public T Value;public Wrap(T value){Value=value;}public static implicit operator Wrap<T>(T value)=>new(value);}", "global::Cases.Wrap<T1> M<T1>(global::Cases.Wrap<T1>? value, T1 fallback)"),
            ("CoalesceAssignUserConversion", "static Wrap<int> M(Wrap<int>? value,int fallback)=>value??=fallback;struct Wrap<T>{public T Value;public Wrap(T value){Value=value;}public static implicit operator Wrap<T>(T value)=>new(value);}", "global::Cases.Wrap<T1> M<T1>(global::Cases.Wrap<T1>? value, T1 fallback)"),
            ("CoalesceAsyncUserConversion", "static async Task<Wrap<int>> M(Task<Wrap<int>?> value,int fallback)=>(await value)??fallback;struct Wrap<T>{public T Value;public Wrap(T value){Value=value;}public static implicit operator Wrap<T>(T value)=>new(value);}", "global::System.Threading.Tasks.Task<global::Cases.Wrap<T1>> M<T1>(global::System.Threading.Tasks.Task<global::Cases.Wrap<T1>?> value, T1 fallback)"),
            ("CoalesceClassUserConversion", "static Wrap<int> M(Wrap<int>? value,int fallback)=>value??fallback;class Wrap<T>{public T Value;public Wrap(T value){Value=value;}public static implicit operator Wrap<T>(T value)=>new(value);}", "global::Cases.Wrap<T1> M<T1>(global::Cases.Wrap<T1>? value, T1 fallback)"),
            ("CoalesceReferenceFallback", "static object M(object? value,string fallback)=>value??fallback;", "T1 M<T1, T2>(T1? value, T2 fallback) where T1 : class where T2 : T1"),
            ("CoalesceNullableReferenceFallback", "static object? M(object? value,string? fallback)=>value??fallback;", "T1? M<T1, T2>(T1? value, T2? fallback) where T1 : class where T2 : class, T1"),
            ("CoalesceReferenceSelection", "static object M(string? value,object fallback)=>value??fallback;", "T2 M<T1, T2>(T1? value, T2 fallback) where T1 : class, T2"),
            ("CoalesceBoxedValueSelection", "static object M(int? value,object fallback)=>value??fallback;", "T2 M<T1, T2>(T1? value, T2 fallback) where T1 : struct, T2"),
            ("CoalesceBoxedInterfaceSelection", "static IComparable M(int? value,IComparable fallback)=>value??fallback;", "T2 M<T1, T2>(T1? value, T2 fallback) where T1 : struct, T2"),
            ("CoalesceBoxedFallback", "static object M(object? value,int fallback)=>value??fallback;", "T1 M<T1, T2>(T1? value, T2 fallback) where T1 : class where T2 : T1"),
            ("CoalesceNumericFallbackControl", "static long M(long? value,int fallback,int tag)=>value??fallback;", "long M<T1>(long? value, int fallback, T1 tag)"),
            ("CoalesceNumericSelectionControl", "static long M(int? value,long fallback,int tag)=>value??fallback;", "long M<T1>(int? value, long fallback, T1 tag)"),
            ("CoalesceArrayProjection", "static IEnumerable<int> M(int[]? value,IEnumerable<int> fallback)=>value??fallback;", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(T1[]? value, global::System.Collections.Generic.IEnumerable<T1> fallback)"),
            ("CoalesceListProjection", "static IEnumerable<int> M(List<int>? value,IEnumerable<int> fallback)=>value??fallback;", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(global::System.Collections.Generic.List<T1>? value, global::System.Collections.Generic.IEnumerable<T1> fallback)"),
            ("CoalesceCovariantFallback", "static IEnumerable<object> M(IEnumerable<object>? value,string[] fallback)=>value??fallback;", "global::System.Collections.Generic.IEnumerable<T1> M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1>? value, T2[] fallback) where T1 : class where T2 : class, T1"),
            ("CoalesceAssignReferenceFallback", "static object M(object? value,string fallback)=>value??=fallback;", "T1 M<T1, T2>(T1? value, T2 fallback) where T1 : class where T2 : T1"),
            ("CoalesceAssignBoxedFallback", "static object M(object? value,int fallback)=>value??=fallback;", "T1 M<T1, T2>(T1? value, T2 fallback) where T1 : class where T2 : T1"),
            ("CoalesceAssignProjection", "static IEnumerable<int> M(IEnumerable<int>? value,List<int> fallback)=>value??=fallback;", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(global::System.Collections.Generic.IEnumerable<T1>? value, global::System.Collections.Generic.List<T1> fallback)"),
            ("CoalesceAssignNumericControl", "static int M(int? value,short fallback,int tag)=>value??=fallback;", "int M<T1>(int? value, short fallback, T1 tag)"),
            ("CoalesceAsyncReferenceFallback", "static async Task<object> M(Task<object?> value,string fallback)=>(await value)??fallback;", "global::System.Threading.Tasks.Task<T1> M<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 fallback) where T1 : class where T2 : T1"),
            ("CoalesceAsyncBoxedSelection", "static async Task<object> M(Task<int?> value,object fallback)=>(await value)??fallback;", "global::System.Threading.Tasks.Task<T2> M<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 fallback) where T1 : struct, T2"),
            ("CoalesceAsyncAssignBoxedFallback", "static async Task<object> M(object? value,Task<int> fallback)=>value??=await fallback;", "global::System.Threading.Tasks.Task<T1> M<T1, T2>(T1? value, global::System.Threading.Tasks.Task<T2> fallback) where T1 : class where T2 : T1"),
            ("CoalesceValueTaskBoxedSelection", "static async ValueTask<object> M(ValueTask<int?> value,object fallback)=>(await value)??fallback;", "global::System.Threading.Tasks.ValueTask<T2> M<T1, T2>(global::System.Threading.Tasks.ValueTask<T1?> value, T2 fallback) where T1 : struct, T2"),
            ("CoalesceFactoryBoxedFallback", "static object M(object? value,Func<int> fallback)=>value??fallback();", "T1 M<T1, T2>(T1? value, global::System.Func<T2> fallback) where T1 : class where T2 : T1")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var tree = CSharpSyntaxTree.ParseText("#nullable enable\nusing System;using System.Collections.Generic;using System.Threading.Tasks;class Cases{" + test.Source + "}", cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]).WithOptions(Program.Compile([]).Options.WithAllowUnsafe(true));
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            if (result.Signature != test.Signature || result.Status != "Suggestion")
            { failures.Add(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); continue; }
            Program.Log.Info("PASS " + test.Name);
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return tests.Length;
    }
}
