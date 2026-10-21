using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class DefaultValueChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("DefaultInferredLocal", "static int M(bool flag,int value,int tag){var result=flag?value:default;return result;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultUnusedCallback", "static int M(bool flag,int value,int tag){var read=()=>flag?value:default;_=read();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultCatch", "static int M(bool flag,int value,int tag){try{if(flag)throw new InvalidOperationException();return value;}catch(InvalidOperationException){return default;}}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultAsyncCoalesce", "static async Task<int> M(Task<int?> value,int tag)=>(await value)??default;", "global::System.Threading.Tasks.Task<T1> M<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 tag) where T1 : struct"),
            ("DefaultBoxingNonNull", "static object M(bool flag,int value,int tag)=>flag?value:default;", "object M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : struct"),
            ("DefaultBoxingNullable", "static object? M(bool flag,int value,int tag)=>flag?value:default;", "object? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultInterfaceNonNull", "static IComparable M(bool flag,int value,int tag)=>flag?value:default;", "global::System.IComparable M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : struct, global::System.IComparable"),
            ("DefaultFixedObjectCallback", "static int M(bool flag,int value,int tag){Func<object> read=()=>flag?value:default;_=read();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : struct"),
            ("DefaultFixedNullableObjectCallback", "static int M(bool flag,int value,int tag){Func<object?> read=()=>flag?value:default;_=read();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultImportedMaybeNonNull", "static T? Maybe<T>(T value)=>default;static object M(int value,int tag)=>Maybe(value);", "object M<T1, T2>(T1 value, T2 tag) where T1 : struct"),
            ("DefaultImportedMaybeNullable", "static T? Maybe<T>(T value)=>default;static object? M(int value,int tag)=>Maybe(value);", "object? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultUserConversion", "class Wrap<T>{public T Value;public Wrap(T value){Value=value;}public static implicit operator Wrap<T>(T value)=>new Wrap<T>(value);}static Wrap<int> M(bool flag,int value,int tag)=>flag?value:default;", "global::Cases.Wrap<T1?> M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultLoop", "static int M(IEnumerable<int> values,int tag){foreach(var value in values)return value;return default;}", "T1? M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)"),
            ("DefaultIterator", "static IEnumerable<int> M(int value,int tag){yield return value;yield return default;}", "global::System.Collections.Generic.IEnumerable<T1?> M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAsyncIterator", "static async IAsyncEnumerable<int> M(int value,int tag){await Task.Yield();yield return value;yield return default;}", "global::System.Collections.Generic.IAsyncEnumerable<T1?> M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultConditional", "static int M(bool flag,int value,int tag)=>flag?value:default;", "T1? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultStatements", "static int M(bool flag,int value,int tag){if(flag)return value;return default;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultFirstStatement", "static int M(bool flag,int value,int tag){if(flag)return default;return value;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultSwitch", "static int M(int key,int value,int tag)=>key switch{0=>value,_=>default};", "T1? M<T1, T2>(int key, T1 value, T2 tag)"),
            ("DefaultTupleComponent", "static (int value,int number) M(bool flag,int value,int number,int tag)=>(flag?value:default,number);", "(T1? value, T2 number) M<T1, T2, T3>(bool flag, T1 value, T2 number, T3 tag)"),
            ("DefaultWholeTuple", "static (int left,int right) M(bool flag,(int left,int right) value,int tag)=>flag?value:default;", "(T1? left, T2? right) M<T1, T2, T3>(bool flag, (T1 left, T2 right) value, T3 tag)"),
            ("DefaultGenericStruct", "struct Cell<T>{public T Value;}static Cell<int> M(bool flag,Cell<int> value,int tag)=>flag?value:default;", "global::Cases.Cell<T1> M<T1, T2>(bool flag, global::Cases.Cell<T1> value, T2 tag)"),
            ("DefaultAsyncTask", "static async Task<int> M(bool flag,Task<int> value,int tag){if(flag)return await value;return default;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag)"),
            ("DefaultAsyncValueTask", "static async ValueTask<int> M(bool flag,ValueTask<int> value,int tag){if(flag)return await value;return default;}", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag)"),
            ("DefaultCoalesce", "static int M(int? value,int tag)=>value??default;", "T1 M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("DefaultCoalesceAssignment", "static int? M(int? value,int tag)=>value??=default;", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("DefaultOnlyControl", "static int M(int tag)=>default;", "int M<T1>(T1 tag)"),
            ("DefaultTypedControl", "static int M(bool flag,int value,int tag)=>flag?value:default(int);", "int M<T1>(bool flag, int value, T1 tag)"),
            ("DefaultArithmeticControl", "static int M(bool flag,int value,int tag)=>flag?value+1:default;", "int M<T1>(bool flag, int value, T1 tag)"),
            ("DefaultSuppressed", "static int M(bool flag,int value,int tag)=>flag?value:default!;", "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultCallback", "static Func<int> M(bool flag,int value,int tag)=>()=>flag?value:default;", "global::System.Func<T1?> M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultFixedLocalControl", "static int M(bool flag,int value,int tag){int Local()=>flag?value:default;return Local();}", "int M<T1>(bool flag, int value, T1 tag)"),
            ("DefaultFixedDelegateControl", "static int M(bool flag,int value,int tag){Func<int> read=()=>flag?value:default;return read();}", "int M<T1>(bool flag, int value, T1 tag)"),
            ("DefaultRecursive", "static int M(int depth,int value,int tag){if(depth==0)return default;if(depth==1)return value;return M(depth-1,value,tag);}", "T1? M<T1, T2>(int depth, T1 value, T2 tag)")
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
        var versionChecks = 0;
        foreach (var version in new[] { LanguageVersion.CSharp7_3, LanguageVersion.CSharp8, LanguageVersion.CSharp9, LanguageVersion.CSharp13 })
        {
            foreach (var nullable in new[] { false, true })
            {
                if (nullable && version == LanguageVersion.CSharp7_3)
                { continue; }
                var tree = CSharpSyntaxTree.ParseText((nullable ? "#nullable enable\n" : "") + "class Cases{static int M(bool flag,int value,int tag)=>flag?value:default;}", new CSharpParseOptions(version), cancellationToken: cancellationToken);
                var result = Program.Results(Program.Compile([tree]), cancellationToken).Single();
                var signature = !nullable ? "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"
                    : version < LanguageVersion.CSharp9 ? "T1 M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : struct" : "T1? M<T1, T2>(bool flag, T1 value, T2 tag)";
                if (result.Status != "Suggestion" || result.Signature != signature)
                { failures.Add("Default version " + version + " nullable=" + nullable + ": expected " + signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
                else
                { Program.Log.Info("PASS DefaultValue" + version + "Nullable" + nullable); }
                versionChecks++;
            }
        }
        foreach (var (name, body) in new[]
        {
            ("DefaultLayoutDirect", "static async LayoutTask<int> M(bool flag,int value){await new LayoutSignal();if(flag)return value;return default;}"),
            ("DefaultLayoutImportedAnnotation", "static LayoutTask<int> M(bool flag,int value){static async LayoutTask<T?> Local<T>(bool chosen,T item){await new LayoutSignal();if(chosen)return item;return default;}return Local(flag,value);}")
        })
        {
            var tree = CSharpSyntaxTree.ParseText(BuilderLayoutChecks.Support + "\nclass Cases{" + body + "}", cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            compilation = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
                { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            const string signature = "global::LayoutTask<T1> M<T1>(bool flag, T1 value) where T1 : unmanaged";
            if (result.Status != "Suggestion" || result.Signature != signature)
            { failures.Add(name + ": expected " + signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS " + name); }
            versionChecks++;
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        return tests.Length + versionChecks;
    }
}
