using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class FixedArgumentChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("FixedObjectCall", "static void Take(object value){}static object M(object value,int tag){Take(value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedNullableCall", "static void Take(object? value){}static object? M(object? value,int tag){Take(value);return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("FixedInterfaceCall", "static void Take(IComparable value){}static IComparable M(IComparable value,int tag){Take(value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : global::System.IComparable"),
            ("FixedBaseCall", "class Base{}static void Take(Base value){}static Base M(Base value,int tag){Take(value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : global::Cases.Base"),
            ("FixedArrayCall", "static void Take(object[] value){}static object[] M(object[] value,int tag){Take(value);return value;}", "T1[] M<T1, T2>(T1[] value, T2 tag) where T1 : class"),
            ("FixedTupleCall", "static void Take((object,int) value){}static (object,int) M((object,int) value,int tag){Take(value);return value;}", "(T1, int) M<T1, T2>((T1, int) value, T2 tag) where T1 : notnull"),
            ("FixedVariantCall", "static void Take(Func<object> value){}static Func<object> M(Func<object> value,int tag){Take(value);return value;}", "global::System.Func<T1> M<T1, T2>(global::System.Func<T1> value, T2 tag) where T1 : class"),
            ("FixedInvariantCall", "static void Take(List<object> value){}static List<object> M(List<object> value,int tag){Take(value);return value;}", "global::System.Collections.Generic.List<object> M<T1>(global::System.Collections.Generic.List<object> value, T1 tag)"),
            ("FixedConstructor", "class Box{public Box(object value){}}static object M(object value,int tag){_=new Box(value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedDelegateCall", "static Action<object> take=null!;static object M(object value,int tag){take(value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedExplicitGenericCall", "static void Take<T>(T value){}static object M(object value,int tag){Take<object>(value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedConditionalCall", "static void Take(object value){}static (object,object) M(bool flag,object left,object right,int tag){Take(flag?left:right);return(left,right);}", "(T1, T2) M<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull"),
            ("FixedSwitchCall", "static void Take(object value){}static (object,object) M(int key,object left,object right,int tag){Take(key switch{0=>left,_=>right});return(left,right);}", "(T1, T2) M<T1, T2, T3>(int key, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull"),
            ("FixedNullableAwaitCall", "static void Take(object? value){}static async Task<object?> M(Task<object?> value,int tag){Take(await value);return await value;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 tag) where T1 : class"),
            ("FixedValueTaskCall", "static void Take(object value){}static async ValueTask<object> M(ValueTask<object> value,int tag){Take(await value);return await value;}", "global::System.Threading.Tasks.ValueTask<T1> M<T1, T2>(global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : notnull"),
            ("FixedParamsCall", "static void Take(params object[] values){}static object M(object value,int tag){Take(value,value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedArrayElements", "static object M(object value,int tag){_=new object[]{value};return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedOriginalBoxingCall", "static void Take(object value){}static int M(int value,int tag){Take(value);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedNumericCall", "static void Take(long value){}static int M(int value,int tag){Take(value);return value;}", "int M<T1>(int value, T1 tag)"),
            ("FixedReferenceCall", "static void Take(ref object value){}static object M(object value,int tag){Take(ref value);return value;}", "object M<T1>(object value, T1 tag)"),
            ("FixedOmittedInCall", "static void Take(in object value){}static object M(object value,int tag){Take(value);return value;}", "object M<T1>(object value, T1 tag)"),
            ("FixedExplicitInCall", "static void Take(in object value){}static object M(object value,int tag){Take(in value);return value;}", "object M<T1>(object value, T1 tag)")
        ];
        foreach (var test in tests)
        {
            var tree = CSharpSyntaxTree.ParseText("#nullable enable\nusing System;using System.Collections.Generic;using System.Threading.Tasks;class Cases{" + test.Source + "}", cancellationToken: cancellationToken);
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
