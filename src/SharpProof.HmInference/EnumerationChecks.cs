using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace SharpProof.HmInference;

internal static class EnumerationChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Status, string? Signature)[] tests =
        [
            ("YieldIdentity", "static System.Collections.Generic.IEnumerable<int> M(int x) { yield return x; }", "Suggestion", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(T1 x)"),
            ("YieldEnumerator", "static System.Collections.Generic.IEnumerator<int> M(int x) { yield return x; }", "Suggestion", "global::System.Collections.Generic.IEnumerator<T1> M<T1>(T1 x)"),
            ("YieldMultiple", "static System.Collections.Generic.IEnumerable<int> M(int x,int y) { yield return x; yield return y; }", "Suggestion", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(T1 x, T1 y)"),
            ("YieldBreak", "static System.Collections.Generic.IEnumerable<int> M(int ignored) { yield break; }", "Suggestion", "global::System.Collections.Generic.IEnumerable<int> M<T1>(T1 ignored)"),
            ("YieldBranch", "static System.Collections.Generic.IEnumerable<int> M(int x,bool stop) { if(stop) yield break; yield return x; }", "Suggestion", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(T1 x, bool stop)"),
            ("YieldNullableValue", "static System.Collections.Generic.IEnumerable<int?> M(int? x) { yield return x; }", "Suggestion", "global::System.Collections.Generic.IEnumerable<T1?> M<T1>(T1? x) where T1 : struct"),
            ("YieldNullableReference", "static System.Collections.Generic.IEnumerable<string?> M(string? x) { yield return x; }", "Suggestion", "global::System.Collections.Generic.IEnumerable<T1?> M<T1>(T1? x) where T1 : class"),
            ("YieldFinally", "static System.Collections.Generic.IEnumerable<int> M(int x) { try { yield return x; } finally {} }", "Suggestion", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(T1 x)"),
            ("YieldArray", "static System.Collections.Generic.IEnumerable<int> M(int[] xs) { foreach(var x in xs) yield return x; }", "Suggestion", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(T1[] xs)"),
            ("NonGenericYieldBoxing", "static System.Collections.IEnumerable M(int x,bool ignored) { yield return x; }", "Suggestion", "global::System.Collections.IEnumerable M<T1, T2>(T1 x, T2 ignored)"),
            ("AsyncYield", "static async System.Collections.Generic.IAsyncEnumerable<int> M(int x) { await Task.Yield(); yield return x; }", "Suggestion", "global::System.Collections.Generic.IAsyncEnumerable<T1> M<T1>(T1 x)"),
            ("AsyncYieldEnumerator", "static async System.Collections.Generic.IAsyncEnumerator<int> M(int x) { await Task.Yield(); yield return x; }", "Suggestion", "global::System.Collections.Generic.IAsyncEnumerator<T1> M<T1>(T1 x)"),
            ("AsyncYieldBreak", "static async System.Collections.Generic.IAsyncEnumerable<int> M(int ignored) { await Task.Yield(); yield break; }", "Suggestion", "global::System.Collections.Generic.IAsyncEnumerable<int> M<T1>(T1 ignored)"),
            ("AsyncYieldAwaited", "static async System.Collections.Generic.IAsyncEnumerable<int> M(Task<int> task) { yield return await task; }", "Suggestion", "global::System.Collections.Generic.IAsyncEnumerable<T1> M<T1>(global::System.Threading.Tasks.Task<T1> task)"),
            ("AsyncYieldNullable", "static async System.Collections.Generic.IAsyncEnumerable<int?> M(int? x) { await Task.Yield(); yield return x; }", "Suggestion", "global::System.Collections.Generic.IAsyncEnumerable<T1?> M<T1>(T1? x) where T1 : struct"),
            ("EnumerableForeach", "static int M(System.Collections.Generic.IEnumerable<int> xs,int seed) { foreach(var x in xs) seed=x; return seed; }", "Suggestion", "T1 M<T1>(global::System.Collections.Generic.IEnumerable<T1> xs, T1 seed)"),
            ("ListForeach", "static int M(System.Collections.Generic.List<int> xs,int seed) { foreach(var x in xs) seed=x; return seed; }", "Suggestion", "T1 M<T1>(global::System.Collections.Generic.List<T1> xs, T1 seed)"),
            ("TupleEnumerable", "static int M(System.Collections.Generic.IEnumerable<(int,bool)> xs,int seed) { foreach(var (value,flag) in xs) seed=value; return seed; }", "Suggestion", "T1 M<T1, T2>(global::System.Collections.Generic.IEnumerable<(T1, T2)> xs, T1 seed)"),
            ("TypedEnumerableConversion", "static long M(System.Collections.Generic.IEnumerable<int> xs,long seed,bool ignored) { foreach(long x in xs) seed=x; return seed; }", "Suggestion", "long M<T1>(global::System.Collections.Generic.IEnumerable<int> xs, long seed, T1 ignored)"),
            ("TypedTupleForeachConversion", "static long M(System.Collections.Generic.IEnumerable<(int,bool)> xs,long seed,bool ignored) { foreach((long value,bool flag) in xs) seed=value; return seed; }", "Suggestion", "long M<T1>(global::System.Collections.Generic.IEnumerable<(int, bool)> xs, long seed, T1 ignored)"),
            ("TypedTupleLocalConversion", "static long M((int,bool) pair,bool ignored) { (long value,bool flag)=pair; return value; }", "Suggestion", "long M<T1>((int, bool) pair, T1 ignored)"),
            ("PatternForeach", "static int M(Sequence<int> xs,int seed) { foreach(var x in xs) seed=x; return seed; } } class Sequence<T> { public System.Collections.Generic.IEnumerator<T> GetEnumerator()=>throw new System.Exception(); } class Tail {", "Suggestion", "T1 M<T1>(global::Sequence<T1> xs, T1 seed)"),
            ("NestedEnumerator", "static int M(Sequence<int> xs,int seed) { foreach(var x in xs) seed=x; return seed; } } class Sequence<T> { public Enumerator GetEnumerator()=>default; public struct Enumerator { public bool MoveNext()=>false; public T Current=>default!; } } class Tail {", "Suggestion", "T1 M<T1>(global::Sequence<T1> xs, T1 seed)"),
            ("InheritedForeach", "static int[] M(Derived<int> xs,int[] seed) { foreach(var x in xs) seed=x; return seed; } } class Sequence<T> { public System.Collections.Generic.IEnumerator<T> GetEnumerator()=>throw new System.Exception(); } class Derived<T>:Sequence<T[]> {} class Tail {", "Suggestion", "T1[] M<T1>(global::Derived<T1> xs, T1[] seed)"),
            ("StringForeach", "static char M(string xs,char seed,bool ignored) { foreach(var x in xs) seed=x; return seed; }", "Suggestion", "char M<T1>(string xs, char seed, T1 ignored)"),
            ("AsyncEnumerableForeach", "static async Task<int> M(System.Collections.Generic.IAsyncEnumerable<int> xs,int seed) { await foreach(var x in xs) seed=x; return seed; }", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Collections.Generic.IAsyncEnumerable<T1> xs, T1 seed)"),
            ("AsyncYieldForeach", "static async System.Collections.Generic.IAsyncEnumerable<int> M(System.Collections.Generic.IAsyncEnumerable<int> xs) { await foreach(var x in xs) yield return x; }", "Suggestion", "global::System.Collections.Generic.IAsyncEnumerable<T1> M<T1>(global::System.Collections.Generic.IAsyncEnumerable<T1> xs)"),
            ("ConfiguredAsyncForeach", "static async Task<int> M(System.Collections.Generic.IAsyncEnumerable<int> xs,int seed) { await foreach(var x in xs.ConfigureAwait(false)) seed=x; return seed; }", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Collections.Generic.IAsyncEnumerable<T1> xs, T1 seed)"),
            ("CancelledAsyncForeach", "static async Task<int> M(System.Collections.Generic.IAsyncEnumerable<int> xs,int seed,System.Threading.CancellationToken token) { await foreach(var x in xs.WithCancellation(token).ConfigureAwait(false)) seed=x; return seed; }", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::System.Collections.Generic.IAsyncEnumerable<T1> xs, T1 seed, global::System.Threading.CancellationToken token)"),
            ("AsyncPatternForeach", "static async Task<int> M(AsyncSequence<int> xs,int seed) { await foreach(var x in xs) seed=x; return seed; } } class AsyncSequence<T> { public System.Collections.Generic.IAsyncEnumerator<T> GetAsyncEnumerator(System.Threading.CancellationToken token=default)=>throw new System.Exception(); } class Tail {", "Suggestion", "global::System.Threading.Tasks.Task<T1> M<T1>(global::AsyncSequence<T1> xs, T1 seed)"),
            ("ImplicitAwaiterOverload", "static async Task<int> M(AsyncPattern<int> xs,int seed,bool ignored) { await foreach(var x in xs) {seed=x;break;} return seed; } } class Move<T> {} static class Awaiters { public static System.Runtime.CompilerServices.TaskAwaiter<bool> GetAwaiter(this Move<int> x)=>Task.FromResult(false).GetAwaiter(); public static System.Runtime.CompilerServices.TaskAwaiter<bool> GetAwaiter<T>(this Move<T> x)=>Task.FromResult(true).GetAwaiter(); } class AsyncPattern<T> { public Enumerator GetAsyncEnumerator()=>new(); public sealed class Enumerator { public Move<T> MoveNextAsync()=>new(); public T Current=>default!; } } class Tail {", "Suggestion", "global::System.Threading.Tasks.Task<int> M<T1>(global::AsyncPattern<int> xs, int seed, T1 ignored)"),
            ("NullableGetValueOrDefault", "static int M(int? x,int fallback)=>x.GetValueOrDefault(fallback);", "Suggestion", "T1 M<T1>(T1? x, T1 fallback) where T1 : struct"),
            ("GenericInstanceVoid", "static void M(System.Collections.Generic.List<int> xs,int x)=>xs.Add(x);", "Suggestion", "void M<T1>(global::System.Collections.Generic.List<T1> xs, T1 x)"),
            ("GenericInstanceBool", "static bool M(System.Collections.Generic.List<int> xs,int x)=>xs.Contains(x);", "Suggestion", "bool M<T1>(global::System.Collections.Generic.List<T1> xs, T1 x)"),
            ("SyncYieldWithAsyncLambda", "static System.Collections.Generic.IEnumerable<System.Func<Task<int>>> M(int x) { yield return async()=>{ await Task.Yield(); return x; }; }", "Suggestion", "global::System.Collections.Generic.IEnumerable<global::System.Func<global::System.Threading.Tasks.Task<T1>>> M<T1>(T1 x)"),
            ("NestedIteratorNotRoot", "static int M(int x) { System.Collections.Generic.IEnumerable<int> Local() { yield return 1; } return x; }", "Suggestion", "T1 M<T1>(T1 x)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nusing System.Threading.Tasks; class Cases { " + test.Source + " }", cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(d => { ArgumentNullException.ThrowIfNull(d); return d.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException($"Invalid fixture {test.Name}: {errors[0]}"); }
            var result = Program.Results(compilation, cancellationToken).Single(r => { ArgumentNullException.ThrowIfNull(r); return r.Method == "M"; });
            if (result.Status != test.Status || result.Signature != test.Signature)
            {
                failures.Add($"{test.Name}: expected {test.Status}: {test.Signature}; got {result.Status}: {result.Signature ?? result.Message}");
                continue;
            }
            var diagnostics = await compilation.WithAnalyzers([new HmInferenceAnalyzer()], new CompilationWithAnalyzersOptions(new AnalyzerOptions([]), null, true, false)).GetAnalyzerDiagnosticsAsync(cancellationToken).ConfigureAwait(false);
            if (diagnostics.Any(d => { ArgumentNullException.ThrowIfNull(d); return d.Id == "AD0001"; }))
            { throw new InvalidOperationException("Analyzer failure: " + test.Name); }
            Program.Log.Info("PASS " + test.Name);
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return tests.Length;
    }
}
