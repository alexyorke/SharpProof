using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class NullableReturnChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("NullableLocalMultipleReturns", "static object M(bool flag,object value,int tag){object? Local(){if(flag)return value;return null;}_=Local();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("NullableLambdaMultipleReturns", "static object M(bool flag,object value,int tag){Func<object?> callback=()=>{if(flag)return value;return null;};_=callback();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("NullableAnonymousMultipleReturns", "static object M(bool flag,object value,int tag){Func<object?> callback=delegate{if(flag)return value;return null;};_=callback();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("NullableLocalIndependentReturns", "static (object,object) M(bool flag,object left,object right,int tag){object? Local(){if(flag)return left;return right;}_=Local();return(left,right);}", "(T1, T2) M<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag)"),
            ("NullableLambdaIndependentReturns", "static (object,object) M(bool flag,object left,object right,int tag){Func<object?> callback=()=>{if(flag)return left;return right;};_=callback();return(left,right);}", "(T1, T2) M<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag)"),
            ("NullableLocalTaskReturns", "static object M(bool flag,object value,int tag){async Task<object?> Local(){await Task.Yield();if(flag)return value;return null;}_=Local();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("NullableTaskLambdaReturns", "static Task<object> M(bool flag,Task<object> value,int tag){Func<Task<object?>> callback=async()=>{if(flag)return await value;return null;};_=callback();return value;}", "global::System.Threading.Tasks.Task<T1> M<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag)"),
            ("NullableValueTaskLambdaReturns", "static ValueTask<object> M(bool flag,ValueTask<object> value,int tag){Func<ValueTask<object?>> callback=async()=>{if(flag)return await value;return null;};_=callback();return value;}", "global::System.Threading.Tasks.ValueTask<T1> M<T1, T2>(bool flag, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag)"),
            ("NullableLocalIteratorYields", "static object M(object value,int tag){System.Collections.Generic.IEnumerable<object?> Get(){yield return value;yield return null;}foreach(var item in Get()){}return value;}", "T1 M<T1, T2>(T1 value, T2 tag)"),
            ("NullableLocalAsyncIteratorYields", "static object M(object value,int tag){async System.Collections.Generic.IAsyncEnumerable<object?> Get(){await Task.Yield();yield return value;yield return null;}_=Get();return value;}", "T1 M<T1, T2>(T1 value, T2 tag)"),
            ("NarrowedLocalReturn", "static object? M(object? value,int tag){object Local(){if(value is null)return new object();return value;}_=Local();return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("NarrowedLambdaReturn", "static object? M(object? value,int tag){Func<object> callback=()=>{if(value is null)return new object();return value;};_=callback();return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("NullableLocalTupleReturns", "static object M(bool flag,object value,int tag){(object?,int) Local(){if(flag)return(value,1);return(null,2);}_=Local();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("NullableLambdaTupleReturns", "static object M(bool flag,object value,int tag){Func<(object?,int)> callback=()=>{if(flag)return(value,1);return(null,2);};_=callback();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("NullableInterfaceReturn", "static IComparable M(bool flag,IComparable value,int tag){IComparable? Local(){if(flag)return value;return null;}_=Local();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : global::System.IComparable?"),
            ("NullableBaseReturn", "class Base{}static Base M(bool flag,Base value,int tag){Base? Local(){if(flag)return value;return null;}_=Local();return value;}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : global::Cases.Base?"),
            ("NullableValueResultRemainsFixed", "static int M(bool flag,int value,int tag){Func<int?> callback=()=>{if(flag)return value;return null;};_=callback();return value;}", "int M<T1>(bool flag, int value, T1 tag)"),
            ("NullableNumericResultRemainsFixed", "static int M(bool flag,int value,int tag){Func<long?> callback=()=>{if(flag)return value;return null;};_=callback();return value;}", "int M<T1>(bool flag, int value, T1 tag)"),
            ("NullableRefResultRemainsInvariant", "delegate ref object? Callback();class Box<T>{public T Value=default!;}static Box<object?> M(Box<object?> value,int tag){Callback callback=()=>ref value.Value;_=callback();return value;}", "global::Cases.Box<object?> M<T1>(global::Cases.Box<object?> value, T1 tag)"),
            ("NullableTreeRetainsShape", "static object M(object value,int tag){System.Linq.Expressions.Expression<Func<object?>> callback=()=>value;_=callback;return value;}", "object M<T1>(object value, T1 tag)")
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
