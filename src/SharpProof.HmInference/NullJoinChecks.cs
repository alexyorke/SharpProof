using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class NullJoinChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("StoreNullReferenceAlias", "static object? M(object value,int tag){var result=value;ref var alias=ref result;alias=null;return result;}", "T1? M<T1, T2>(T1 value, T2 tag) where T1 : class"),
            ("StoreNullValueAlias", "static int? M(int? value,int tag){var result=value;ref var alias=ref result;alias=null;return result;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("StoreNullReference", "static object? M(bool flag,object value,int tag){var result=value;if(flag)result=null;return result;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : class"),
            ("StoreDefaultReference", "static object? M(bool flag,object value,int tag){var result=value;if(flag)result=default;return result;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : class"),
            ("StoreNullCopy", "static object M(object value,int tag){var result=value;var copy=result;result=null;return copy;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : class"),
            ("StoreNullParameter", "static object? M(object? value,int tag){value=null;return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("StoreNullValue", "static int? M(bool flag,int? value,int tag){var result=value;if(flag)result=null;return result;}", "T1? M<T1, T2>(bool flag, T1? value, T2 tag) where T1 : struct"),
            ("StoreDefaultValue", "static int? M(bool flag,int? value,int tag){var result=value;if(flag)result=default;return result;}", "T1? M<T1, T2>(bool flag, T1? value, T2 tag) where T1 : struct"),
            ("StoreNullReferenceCapture", "static object? M(object value,int tag){var result=value;void Clear(){result=null;}Clear();return result;}", "T1? M<T1, T2>(T1 value, T2 tag) where T1 : class"),
            ("StoreNullValueCapture", "static int? M(int? value,int tag){var result=value;void Clear(){result=null;}Clear();return result;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("StoreNullReferenceCallback", "static Func<object?> M(object value,int tag){var result=value;var read=()=>result;result=null;return read;}", "global::System.Func<T1?> M<T1, T2>(T1 value, T2 tag) where T1 : class"),
            ("StoreNullAsyncReference", "static async Task<object?> M(bool flag,Task<object> value,int tag){var result=await value;if(flag)result=null;return result;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : class"),
            ("StoreNullAsyncValue", "static async ValueTask<int?> M(bool flag,ValueTask<int?> value,int tag){var result=await value;if(flag)result=null;return result;}", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.ValueTask<T1?> value, T2 tag) where T1 : struct"),
            ("StoreNullArray", "static object? M(object?[] values,int tag){values[0]=null;return values[0];}", "T1? M<T1, T2>(T1?[] values, T2 tag) where T1 : class"),
            ("StoreNullList", "static int? M(List<int?> values,int tag){values[0]=null;return values[0];}", "T1? M<T1, T2>(global::System.Collections.Generic.List<T1?> values, T2 tag) where T1 : struct"),
            ("StoreNullRefReference", "static object? M(ref object? value,int tag){value=null;return value;}", "T1? M<T1, T2>(ref T1? value, T2 tag) where T1 : class"),
            ("StoreNullRefValue", "static int? M(ref int? value,int tag){value=null;return value;}", "T1? M<T1, T2>(ref T1? value, T2 tag) where T1 : struct"),
            ("StoreNullPointer", "static unsafe int* M(int* value,int tag){var result=value;result=null;return result;}", "T1* M<T1, T2>(T1* value, T2 tag) where T1 : unmanaged"),
            ("StoreTypedNullControl", "static object? M(object value,int tag){var result=value;result=(object?)null;return result;}", "object? M<T1>(object value, T1 tag)"),
            ("StoreFixedValueControl", "static int? M(int? value,int tag){int? result=value;result=null;return result;}", "int? M<T1>(int? value, T1 tag)"),
            ("StoreLiftedCallerControl", "static int? Use()=>M(true,7,0);static int? M(bool flag,int? value,int tag){var result=value;if(flag)result=null;return result;}", "int? M<T1>(bool flag, int? value, T1 tag)"),
            ("StoreNullArrayRoot", "static int[]? M(int[] value,int tag){var result=value;result=null;return result;}", "T1[]? M<T1, T2>(T1[] value, T2 tag)"),
            ("StoreNullListRoot", "static List<int>? M(List<int> value,int tag){var result=value;result=null;return result;}", "global::System.Collections.Generic.List<T1>? M<T1, T2>(global::System.Collections.Generic.List<T1> value, T2 tag)"),
            ("StoreNullTupleRoot", "static (object left,int right)? M((object left,int right)? value,int tag){var result=value;result=default;return result;}", "(T1 left, T2 right)? M<T1, T2, T3>((T1 left, T2 right)? value, T3 tag)"),
            ("StoreNullFunctionPointer", "static unsafe delegate*<int,int> M(delegate*<int,int> value,int tag){var result=value;result=null;return result;}", "delegate*<T1, T2> M<T1, T2, T3>(delegate*<T1, T2> value, T3 tag)"),
            ("CoalesceNullReference", "static object? M(object? value,int tag)=>value??null;", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("CoalesceDefaultReference", "static object? M(object? value,int tag)=>value??default;", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("CoalesceNullValue", "static int? M(int? value,int tag)=>value??null;", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("CoalesceAssignNullReference", "static object? M(object? value,int tag)=>value??=null;", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("CoalesceAssignDefaultReference", "static object? M(object? value,int tag)=>value??=default;", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("CoalesceAssignNullValue", "static int? M(int? value,int tag)=>value??=null;", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("CoalesceAssignScalarDefault", "static int? M(int? value,int tag)=>value??=default;", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("CoalesceScalarDefault", "static int M(int? value,int tag)=>value??default;", "T1 M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("CoalesceTypedDefaultRetainsEndpoint", "static object? M(object? value,int tag)=>value??default(object);", "object? M<T1>(object? value, T1 tag)"),
            ("CoalesceTypedNullRetainsEndpoint", "static object? M(object? value,int tag)=>value??(object?)null;", "object? M<T1>(object? value, T1 tag)"),
            ("CoalesceNullArray", "static object[]? M(object[]? value,int tag)=>value??null;", "T1[]? M<T1, T2>(T1[]? value, T2 tag)"),
            ("CoalesceDefaultList", "static List<int>? M(List<int>? value,int tag)=>value??default;", "global::System.Collections.Generic.List<T1>? M<T1, T2>(global::System.Collections.Generic.List<T1>? value, T2 tag)"),
            ("CoalesceNullDelegate", "static Func<object>? M(Func<object>? value,int tag)=>value??null;", "global::System.Func<T1>? M<T1, T2>(global::System.Func<T1>? value, T2 tag)"),
            ("CoalesceNullNullableTuple", "static (object text,int number)? M((object text,int number)? value,int tag)=>value??null;", "(T1 text, T2 number)? M<T1, T2, T3>((T1 text, T2 number)? value, T3 tag)"),
            ("CoalesceNullTupleComponent", "static (object? text,int number) M(object? value,int number,int tag)=>(value??null,number);", "(T1? text, T2 number) M<T1, T2, T3>(T1? value, T2 number, T3 tag) where T1 : class"),
            ("CoalesceNullAwaitReference", "static async Task<object?> M(Task<object?> value,int tag)=>(await value)??null;", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 tag) where T1 : class"),
            ("CoalesceNullAwaitValue", "static async ValueTask<int?> M(ValueTask<int?> value,int tag)=>(await value)??null;", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(global::System.Threading.Tasks.ValueTask<T1?> value, T2 tag) where T1 : struct"),
            ("CoalesceNullChain", "static object? M(object? value,object? fallback,int tag)=>value??fallback??null;", "T1? M<T1, T2>(T1? value, T1? fallback, T2 tag) where T1 : class"),
            ("CoalesceNullNonNullableReference", "static object? M(object value,int tag)=>value??null;", "T1? M<T1, T2>(T1 value, T2 tag) where T1 : class"),
            ("CoalesceSuppressedNullReference", "static object? M(object? value,int tag)=>value??(null!);", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("CoalesceAssignNullArray", "static object[]? M(object[]? value,int tag)=>value??=null;", "T1[]? M<T1, T2>(T1[]? value, T2 tag)"),
            ("CoalesceAssignDefaultList", "static List<int>? M(List<int>? value,int tag)=>value??=default;", "global::System.Collections.Generic.List<T1>? M<T1, T2>(global::System.Collections.Generic.List<T1>? value, T2 tag)"),
            ("CoalesceAssignNullTuple", "static (object text,int number)? M((object text,int number)? value,int tag)=>value??=null;", "(T1 text, T2 number)? M<T1, T2, T3>((T1 text, T2 number)? value, T3 tag)"),
            ("CoalesceAsyncAssignReference", "static async Task<object?> M(object? value,int tag){await Task.Yield();return value??=null;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("CoalesceAsyncAssignValue", "static async ValueTask<int?> M(int? value,int tag){await Task.Yield();return value??=null;}", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("CoalesceNullTaskValue", "static async Task<int?> M(Task<int?> value,int tag)=>(await value)??null;", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 tag) where T1 : struct"),
            ("CoalesceNullValueTaskReference", "static async ValueTask<object?> M(ValueTask<object?> value,int tag)=>(await value)??null;", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(global::System.Threading.Tasks.ValueTask<T1?> value, T2 tag) where T1 : class"),
            ("CoalesceAssignNullReferenceFlow", "static object? M(object? value,int tag){value??=null;return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("CoalesceAssignNullValueFlow", "static int? M(int? value,int tag){value??=null;return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : struct"),
            ("CoalesceNullFixedLocalReturn", "static object? M(object? value,int tag){object? Local()=>value??null;return Local();}", "object? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("CoalesceNullFixedLambdaReturn", "static object? M(object? value,int tag){Func<object?> callback=()=>value??null;return callback();}", "object? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("RecursiveNullReferenceReturns", "static object? M(int depth,object value,int tag){if(depth==0)return null;if(depth==1)return value;return M(depth-1,value,tag);}", "T1? M<T1, T2>(int depth, T1 value, T2 tag) where T1 : class"),
            ("RecursiveNullValueReturns", "static int? M(int depth,int value,int tag){if(depth==0)return null;if(depth==1)return value;return M(depth-1,value,tag);}", "T1? M<T1, T2>(int depth, T1 value, T2 tag) where T1 : struct"),
            ("RecursiveNullArrayReturns", "static object[]? M(int depth,object[] value,int tag){if(depth==0)return null;if(depth==1)return value;return M(depth-1,value,tag);}", "T1[]? M<T1, T2>(int depth, T1[] value, T2 tag)"),
            ("RecursiveAsyncNullReferenceReturns", "static async Task<object?> M(int depth,Task<object> value,int tag){if(depth==0)return null;if(depth==1)return await value;return await M(depth-1,value,tag);}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(int depth, global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : class"),
            ("RecursiveAsyncNullValueReturns", "static async Task<int?> M(int depth,Task<int> value,int tag){if(depth==0)return null;if(depth==1)return await value;return await M(depth-1,value,tag);}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(int depth, global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : struct"),
            ("RecursiveValueTaskNullReferenceReturns", "static async ValueTask<object?> M(int depth,ValueTask<object> value,int tag){if(depth==0)return null;if(depth==1)return await value;return await M(depth-1,value,tag);}", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(int depth, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : class"),
            ("RecursiveNullReferenceFirstCall", "static object? M(int depth,object value,int tag){if(depth>1)return M(depth-1,value,tag);if(depth==1)return value;return null;}", "T1? M<T1, T2>(int depth, T1 value, T2 tag) where T1 : class"),
            ("RecursiveNullableReferenceInput", "static object? M(int depth,object? value,int tag){if(depth==0)return null;if(depth==1)return value;return M(depth-1,value,tag);}", "T1? M<T1, T2>(int depth, T1? value, T2 tag) where T1 : class"),
            ("RecursiveNullableTupleReturns", "static (object? text,int number) M(int depth,object value,int number,int tag){if(depth==0)return(null,number);if(depth==1)return(value,number);return M(depth-1,value,number,tag);}", "(T1? text, T2 number) M<T1, T2, T3>(int depth, T1 value, T2 number, T3 tag) where T1 : class"),
            ("RecursiveNullConditionalReturns", "static object? M(int depth,object value,int tag)=>depth>1?M(depth-1,value,tag):depth==1?value:null;", "T1? M<T1, T2>(int depth, T1 value, T2 tag) where T1 : class"),
            ("RecursiveNullableReferenceLift", "static object? M(int depth,object value,int tag){if(depth==0)return value;return M(depth-1,value,tag);}", "T1? M<T1, T2>(int depth, T1 value, T2 tag) where T1 : class"),
            ("RecursiveValueTaskNullValueReturns", "static async ValueTask<int?> M(int depth,ValueTask<int> value,int tag){if(depth==0)return null;if(depth==1)return await value;return await M(depth-1,value,tag);}", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(int depth, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : struct"),
            ("RootNullReferenceReturns", "static object? M(bool flag,object value,int tag){if(flag)return value;return null;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : class"),
            ("RootNullFirstReferenceReturn", "static object? M(bool flag,object value,int tag){if(flag)return null;return value;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : class"),
            ("RootNullableReferenceInput", "static object? M(bool flag,object? value,int tag){if(flag)return value;return null;}", "T1? M<T1, T2>(bool flag, T1? value, T2 tag) where T1 : class"),
            ("RootNullSwitchStatement", "static object? M(int key,object value,int tag){switch(key){case 0:return value;default:return null;}}", "T1? M<T1, T2>(int key, T1 value, T2 tag) where T1 : class"),
            ("RootNullConditionalExpression", "static object? M(bool flag,object value,int tag)=>flag?value:null;", "T1? M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : class"),
            ("RootNullSwitchExpression", "static object? M(int key,object value,int tag)=>key switch{0=>value,_=>null};", "T1? M<T1, T2>(int key, T1 value, T2 tag) where T1 : class"),
            ("RootNullNullableValue", "static int? M(bool flag,int value,int tag){if(flag)return value;return null;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : struct"),
            ("RootNullNullableValueInput", "static int? M(bool flag,int? value,int tag){if(flag)return value;return null;}", "T1? M<T1, T2>(bool flag, T1? value, T2 tag) where T1 : struct"),
            ("RootNullArray", "static object[]? M(bool flag,object[] value,int tag){if(flag)return value;return null;}", "T1[]? M<T1, T2>(bool flag, T1[] value, T2 tag)"),
            ("RootNullList", "static List<int>? M(bool flag,List<int> value,int tag){if(flag)return value;return null;}", "global::System.Collections.Generic.List<T1>? M<T1, T2>(bool flag, global::System.Collections.Generic.List<T1> value, T2 tag)"),
            ("RootNullDelegate", "static Func<object>? M(bool flag,Func<object> value,int tag){if(flag)return value;return null;}", "global::System.Func<T1>? M<T1, T2>(bool flag, global::System.Func<T1> value, T2 tag)"),
            ("RootNullNullableTuple", "static (object left,int right)? M(bool flag,(object left,int right) value,int tag){if(flag)return value;return null;}", "(T1 left, T2 right)? M<T1, T2, T3>(bool flag, (T1 left, T2 right) value, T3 tag)"),
            ("RootNullTupleComponent", "static (object? text,int number) M(bool flag,object value,int number,int tag)=>(flag?value:null,number);", "(T1? text, T2 number) M<T1, T2, T3>(bool flag, T1 value, T2 number, T3 tag) where T1 : class"),
            ("RootNullAsyncReference", "static async Task<object?> M(bool flag,Task<object> value,int tag){if(flag)return await value;return null;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : class"),
            ("RootNullAsyncValue", "static async Task<int?> M(bool flag,Task<int> value,int tag){if(flag)return await value;return null;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : struct"),
            ("RootNullValueTaskReference", "static async ValueTask<object?> M(bool flag,ValueTask<object> value,int tag){if(flag)return await value;return null;}", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : class"),
            ("RootNullValueTaskValue", "static async ValueTask<int?> M(bool flag,ValueTask<int> value,int tag){if(flag)return await value;return null;}", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : struct"),
            ("RootReferenceDefaultLiteral", "static object? M(bool flag,object value,int tag){if(flag)return value;return default;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : class"),
            ("RootNullableValueDefaultLiteral", "static int? M(bool flag,int value,int tag){if(flag)return value;return default;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : struct"),
            ("RootSuppressedNullLiteral", "static object M(bool flag,object value,int tag){if(flag)return value;return(null!);}", "T1 M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : class"),
            ("RootTypedNullRetainsEndpoint", "static object? M(bool flag,object value,int tag){if(flag)return value;return(object?)null;}", "object? M<T1>(bool flag, object value, T1 tag)"),
            ("RootValueDefault", "static int M(bool flag,int value,int tag){if(flag)return value;return default;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("RootNullOnlyRetainsResult", "static object? M(int tag)=>null;", "object? M<T1>(T1 tag)"),
            ("RootNullPointer", "static unsafe int* M(bool flag,int* value,int tag){if(flag)return value;return null;}", "T1* M<T1, T2>(bool flag, T1* value, T2 tag) where T1 : unmanaged"),
            ("RootNullFunctionPointer", "static unsafe delegate*<int,int> M(bool flag,delegate*<int,int> value,int tag){if(flag)return value;return null;}", "delegate*<T1, T2> M<T1, T2, T3>(bool flag, delegate*<T1, T2> value, T3 tag)")
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
