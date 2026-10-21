using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class NullJoinChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
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
            ("RootValueDefaultRetainsEndpoint", "static int M(bool flag,int value,int tag){if(flag)return value;return default;}", "int M<T1>(bool flag, int value, T1 tag)"),
            ("RootNullOnlyRetainsResult", "static object? M(int tag)=>null;", "object? M<T1>(T1 tag)"),
            ("RootNullPointer", "static unsafe int* M(bool flag,int* value,int tag){if(flag)return value;return null;}", "T1* M<T1, T2>(bool flag, T1* value, T2 tag) where T1 : unmanaged"),
            ("RootNullFunctionPointer", "static unsafe delegate*<int,int> M(bool flag,delegate*<int,int> value,int tag){if(flag)return value;return null;}", "delegate*<T1, T2> M<T1, T2, T3>(bool flag, delegate*<T1, T2> value, T3 tag)")
        ];
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
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length;
    }
}
