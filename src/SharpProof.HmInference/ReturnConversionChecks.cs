using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class ReturnConversionChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("ReturnNullableValueCaller", "static object? M(int? value,int tag)=>value;static object? Caller()=>M(7,0);", "object? M<T1>(int? value, T1 tag)"),
            ("ReturnNullableNullCaller", "static object? M(int? value,int tag)=>value;static object? Caller()=>M(null,0);", "object? M<T1>(int? value, T1 tag)"),
            ("ReturnNaturalDelegateControl", "static object M(object value,int tag){object callback=()=>value;_=callback;return value;}", "object M<T1>(object value, T1 tag)"),
            ("ReturnNaturalDelegateValueControl", "static object M(int value,int tag)=>()=>value;", "object M<T1>(int value, T1 tag)"),
            ("ReturnExpressionTreeControl", "static object M(object value,int tag){System.Linq.Expressions.Expression<Func<object>> callback=()=>value;_=callback;return value;}", "object M<T1>(object value, T1 tag)"),
            ("ReturnBoxingWidenedCaller", "static object M(int value,int tag)=>value;static object Caller(short value)=>M(value,0);", "object M<T1>(int value, T1 tag)"),
            ("ReturnBoxing", "static object M(int value)=>value;", "object M<T1>(T1 value) where T1 : notnull"),
            ("ReturnNullableTargetBoxing", "static object? M(int value)=>value;", "object? M<T1>(T1 value)"),
            ("ReturnNullableValueBoxing", "static object? M(int? value)=>value;", "object? M<T1>(T1? value) where T1 : struct"),
            ("ReturnInterfaceBoxing", "static IComparable M(int value)=>value;", "global::System.IComparable M<T1>(T1 value) where T1 : global::System.IComparable"),
            ("ReturnNullableInterfaceBoxing", "static IComparable? M(int? value)=>value;", "global::System.IComparable? M<T1>(T1? value) where T1 : struct, global::System.IComparable"),
            ("ReturnValueTypeBoxing", "static ValueType M(int value)=>value;", "global::System.ValueType M<T1>(T1 value) where T1 : struct"),
            ("ReturnEnumBoxing", "static Enum M(DayOfWeek value)=>value;", "global::System.Enum M<T1>(T1 value) where T1 : global::System.Enum"),
            ("ReturnBaseReference", "static Base M(Derived value)=>value;class Base{}class Derived:Base{}", "global::Cases.Base M<T1>(T1 value) where T1 : global::Cases.Base"),
            ("ReturnGenericBaseReference", "static Base<int> M(Derived<int> value)=>value;class Base<T>{}class Derived<T>:Base<T>{}", "global::Cases.Base<T1> M<T1>(global::Cases.Derived<T1> value)"),
            ("ReturnListProjection", "static IEnumerable<int> M(List<int> value)=>value;", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(global::System.Collections.Generic.List<T1> value)"),
            ("ReturnArrayProjection", "static IEnumerable<int> M(int[] value)=>value;", "global::System.Collections.Generic.IEnumerable<T1> M<T1>(T1[] value)"),
            ("ReturnArrayCovariance", "static object[] M(string[] value)=>value;", "object[] M<T1>(T1[] value) where T1 : class"),
            ("ReturnInterfaceCovariance", "static IEnumerable<object> M(IEnumerable<string> value)=>value;", "global::System.Collections.Generic.IEnumerable<object> M<T1>(global::System.Collections.Generic.IEnumerable<T1> value) where T1 : class"),
            ("ReturnDelegateCovariance", "static Func<object> M(Func<string> value)=>value;", "global::System.Func<object> M<T1>(global::System.Func<T1> value) where T1 : class"),
            ("ReturnContravarianceControl", "static Action<string> M(Action<object> value,int tag)=>value;", "global::System.Action<string> M<T1>(global::System.Action<object> value, T1 tag)"),
            ("ReturnTupleConversions", "static (object value,long number) M(int value,int number)=>(value,number);", "(object value, long number) M<T1>(T1 value, int number) where T1 : notnull"),
            ("ReturnAsyncTaskBoxing", "static async Task<object> M(int value){await Task.Yield();return value;}", "global::System.Threading.Tasks.Task<object> M<T1>(T1 value) where T1 : notnull"),
            ("ReturnAsyncValueTaskBoxing", "static async ValueTask<object?> M(int? value){await Task.Yield();return value;}", "global::System.Threading.Tasks.ValueTask<object?> M<T1>(T1? value) where T1 : struct"),
            ("ReturnAsyncInterfaceBoxing", "static async Task<IComparable> M(int value){await Task.Yield();return value;}", "global::System.Threading.Tasks.Task<global::System.IComparable> M<T1>(T1 value) where T1 : global::System.IComparable"),
            ("ReturnNumericControl", "static long M(int value,int tag)=>value;", "long M<T1>(int value, T1 tag)"),
            ("ReturnNullableNumericControl", "static long? M(int? value,int tag)=>value;", "long? M<T1>(int? value, T1 tag)"),
            ("ReturnLocalBoxing", "static object M(int value,int tag){object Local()=>value;return Local();}", "object M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("ReturnLambdaBoxing", "static Func<object> M(int value,int tag)=>()=>value;", "global::System.Func<object> M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("ReturnLocalLambdaBoxing", "static object M(int value,int tag){Func<object> read=()=>value;return read();}", "object M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("ReturnIteratorBoxing", "static IEnumerable<object> M(int value,int tag){yield return value;}", "global::System.Collections.Generic.IEnumerable<object> M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("ReturnAsyncIteratorBoxing", "static async IAsyncEnumerable<object> M(int value,int tag){await Task.Yield();yield return value;}", "global::System.Collections.Generic.IAsyncEnumerable<object> M<T1, T2>(T1 value, T2 tag) where T1 : notnull")
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
