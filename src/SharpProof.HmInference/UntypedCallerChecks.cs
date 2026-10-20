using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class UntypedCallerChecks
{
    internal static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Body, string Signature)[] tests =
        [
            ("AsyncNullable", "static async System.Threading.Tasks.Task<object?> M(object? value,int tag){await System.Threading.Tasks.Task.Yield();return value;}static System.Threading.Tasks.Task<object?> Caller()=>M(null,0);", "global::System.Threading.Tasks.Task<object?> M<T1>(object? value, T1 tag)"),
            ("AsyncLambda", "static System.Func<int,System.Threading.Tasks.Task<string?>> M(System.Func<int,System.Threading.Tasks.Task<string?>> map,int tag)=>map;static System.Func<int,System.Threading.Tasks.Task<string?>> Caller()=>M(async value=>{await System.Threading.Tasks.Task.Yield();return (string?)null;},0);", "global::System.Func<int, global::System.Threading.Tasks.Task<T1?>> M<T1, T2>(global::System.Func<int, global::System.Threading.Tasks.Task<T1?>> map, T2 tag) where T1 : class"),
            ("ValueTaskNullable", "static async System.Threading.Tasks.ValueTask<int?> M(int? value,int tag){await System.Threading.Tasks.Task.Yield();return value;}static System.Threading.Tasks.ValueTask<int?> Caller()=>M(null,0);", "global::System.Threading.Tasks.ValueTask<int?> M<T1>(int? value, T1 tag)"),
            ("TypedDefault", "static int M(int value,int tag)=>value;static int Caller()=>M(default(int),0);", "T1 M<T1, T2>(T1 value, T2 tag)"),
            ("TypedLambda", "static System.Func<int,string> M(System.Func<int,string> map,int tag)=>map;static System.Func<int,string> Caller()=>M((int value)=>value.ToString(),0);", "global::System.Func<T1, T2> M<T1, T2, T3>(global::System.Func<T1, T2> map, T3 tag)"),
            ("NestedBlockLambda", "static System.Func<System.Func<int,int>> M(System.Func<System.Func<int,int>> map,int tag)=>map;static System.Func<System.Func<int,int>> Caller()=>M(()=>{return value=>value;},0);", "global::System.Func<global::System.Func<int, int>> M<T1>(global::System.Func<global::System.Func<int, int>> map, T1 tag)"),
            ("Null", "static object? M(object? value,int tag)=>value;static object? Caller()=>M(null,0);", "object? M<T1>(object? value, T1 tag)"),
            ("Default", "static int M(int value,int tag)=>value;static int Caller()=>M(default,0);", "int M<T1>(int value, T1 tag)"),
            ("EmptyCollection", "static int M(System.Collections.Generic.List<int> value,int tag)=>value.Count;static int Caller()=>M([],0);", "int M<T1>(global::System.Collections.Generic.List<int> value, T1 tag)"),
            ("TargetTypedNew", "static int M(System.Collections.Generic.List<int> value,int tag)=>value.Count;static int Caller()=>M(new(),0);", "int M<T1>(global::System.Collections.Generic.List<int> value, T1 tag)"),
            ("LambdaOutput", "static System.Func<int,string> M(System.Func<int,string> map,int tag)=>map;static System.Func<int,string> Caller()=>M(value=>\"ok\",0);", "global::System.Func<int, T1> M<T1, T2>(global::System.Func<int, T1> map, T2 tag)"),
            ("NestedLambda", "static System.Func<System.Func<int,int>> M(System.Func<System.Func<int,int>> map,int tag)=>map;static System.Func<System.Func<int,int>> Caller()=>M(()=>value=>value,0);", "global::System.Func<global::System.Func<int, int>> M<T1>(global::System.Func<global::System.Func<int, int>> map, T1 tag)"),
            ("AnonymousRegistration", "static void M(System.Action<System.Func<int,int>> sink,int tag){sink(value=>value);}public static System.Func<int,int>? Captured;static void Caller()=>M(delegate{Captured=null;},0);", "void M<T1>(global::System.Action<global::System.Func<int, int>> sink, T1 tag)"),
            ("ExpressionTree", "static System.Linq.Expressions.Expression<System.Func<int,string>> M(System.Linq.Expressions.Expression<System.Func<int,string>> map,int tag)=>map;static System.Linq.Expressions.Expression<System.Func<int,string>> Caller()=>M(value=>\"ok\",0);", "global::System.Linq.Expressions.Expression<global::System.Func<int, T1>> M<T1, T2>(global::System.Linq.Expressions.Expression<global::System.Func<int, T1>> map, T2 tag)")
        ];
        var failures = new List<string>();
        foreach (var test in tests)
        {
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nstatic class Cases{" + test.Body + "}", cancellationToken: cancellationToken)]);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
            if (!emitted.Success)
            { throw new InvalidOperationException("Untyped caller fixture " + test.Name + ": " + string.Join("; ", emitted.Diagnostics)); }
            var result = Program.Results(compilation, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { failures.Add(test.Name + ": " + result.Status + ": " + (result.Signature ?? result.Message)); }
            else
            { Program.Log.Info("PASS UntypedCaller" + test.Name); }
        }
        if (failures.Count != 0)
        { throw new InvalidOperationException(string.Join("\n", failures)); }
        var crossFile = Program.Compile([
            CSharpSyntaxTree.ParseText("#nullable enable\npublic static class ContextualExtensions{public static object? M(this int receiver,object? value,int tag)=>value;}", cancellationToken: cancellationToken),
            CSharpSyntaxTree.ParseText("#nullable enable\npublic static class ContextualConsumer{public static object? Run()=>1.M(tag:0,value:null);}", cancellationToken: cancellationToken)]);
        var crossResult = Program.Results(crossFile, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
        if (crossResult.Status != "Suggestion" || crossResult.Signature != "object? M<T1, T2>(this T1 receiver, object? value, T2 tag)")
        { throw new InvalidOperationException("Cross-file named extension inference: " + (crossResult.Signature ?? crossResult.Message)); }
        Program.Log.Info("PASS UntypedCallerCrossFileNamedExtension");
        var fixedOnly = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nstatic class Cases{static object? M(object? value)=>value;static object? Run()=>M(null);}", cancellationToken: cancellationToken)]);
        if (Program.Results(fixedOnly, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; }).Status == "Suggestion")
        { throw new InvalidOperationException("A fully pinned contextual signature cannot become generic."); }
        Program.Log.Info("PASS UntypedCallerNoGenericComponentRemains");
        return tests.Length + 2;
    }
}
