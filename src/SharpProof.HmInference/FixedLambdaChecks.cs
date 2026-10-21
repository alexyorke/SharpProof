using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class FixedLambdaChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("FixedLambdaLocal", "static object M(object value,int tag){Func<object> callback=()=>value;_=callback();return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaField", "static Func<object> callback=null!;static object M(object value,int tag){callback=()=>value;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaArgument", "static void Take(Func<object> callback){}static object M(object value,int tag){Take(()=>value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaConstructor", "class Box{public Box(Func<object> callback){}}static object M(object value,int tag){_=new Box(()=>value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedAnonymousMethod", "static object M(object value,int tag){Func<object> callback=delegate{return value;};_=callback();return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaBlockReturns", "static object M(bool flag,object value,int tag){Func<object> callback=()=>{if(flag)return value;return new object();};_=callback();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaConditional", "static (object,object) M(bool flag,object left,object right,int tag){Func<object> callback=()=>flag?left:right;_=callback();return(left,right);}", "(T1, T2) M<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull"),
            ("FixedLambdaSwitch", "static object M(int key,object value,int tag){Func<object> callback=()=>key switch{0=>value,_=>new object()};_=callback();return value;}", "T1 M<T1, T2>(int key, T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaNullable", "static object? M(object? value,int tag){Func<object?> callback=()=>value;_=callback();return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("FixedLambdaTask", "static object M(object value,int tag){Func<Task<object>> callback=async()=>{await Task.Yield();return value;};_=callback();return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaValueTask", "static object M(object value,int tag){Func<ValueTask<object>> callback=async()=>{await Task.Yield();return value;};_=callback();return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaAwaitedNullable", "static Task<object?> M(Task<object?> value,int tag){Func<Task<object?>> callback=async()=>await value;_=callback();return value;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 tag) where T1 : class"),
            ("FixedLambdaNested", "static object M(object value,int tag){Func<Func<object>> callback=()=>()=>value;_=callback()();return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLocalReturnsLambda", "static object M(object value,int tag){Func<object> Local()=>()=>value;_=Local()();return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedCustomDelegate", "delegate object Callback();static object M(object value,int tag){Callback callback=()=>value;_=callback();return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("ExplicitLambdaReturn", "static object M(object value,int tag){var callback=object()=>value;_=callback();return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaCast", "static object M(object value,int tag){var callback=(Func<object>)(()=>value);_=callback();return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLambdaStateRestoration", "static int M(object value,int other,int tag){Func<object> fixedCallback=()=>value;var freeCallback=()=>other;_=fixedCallback();return freeCallback();}", "T2 M<T1, T2, T3>(T1 value, T2 other, T3 tag) where T1 : notnull"),
            ("FixedExpressionTreeRetainsShape", "static object M(object value,int tag){System.Linq.Expressions.Expression<Func<object>> callback=()=>value;_=callback;return value;}", "object M<T1>(object value, T1 tag)"),
            ("FixedNaturalDelegateObjectRetainsType", "static object M(object value,int tag){object callback=()=>value;_=callback;return value;}", "object M<T1>(object value, T1 tag)"),
            ("FixedRefLambdaRetainsAlias", "delegate ref object Callback();class Box<T>{public T Value=default!;}static Box<object> M(Box<object> value,int tag){Callback callback=()=>ref value.Value;_=callback();return value;}", "global::Cases.Box<object> M<T1>(global::Cases.Box<object> value, T1 tag)")
        ];
        foreach (var test in tests)
        {
            var tree = CSharpSyntaxTree.ParseText("#nullable enable\nusing System;using System.Threading.Tasks;class Cases{" + test.Source + "}", cancellationToken: cancellationToken);
            var compilation = Program.Compile([tree]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            if (result.Signature != test.Signature || result.Status != "Suggestion")
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length;
    }
}
