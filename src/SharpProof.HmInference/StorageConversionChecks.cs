using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class StorageConversionChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("FixedObjectField", "static object field=null!;static object M(object value,int tag){field=value;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedNullableField", "static object? field;static object? M(object? value,int tag){field=value;return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("FixedInstanceField", "object field=null!;object M(object value,int tag){field=value;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedProperty", "class Box{public object Value{get;set;}=null!;}static object M(Box box,object value,int tag){box.Value=value;return value;}", "T1 M<T1, T2>(global::Cases.Box box, T1 value, T2 tag) where T1 : notnull"),
            ("FixedIndexer", "class Box{public object this[int index]{get=>null!;set{}}}static object M(Box box,int index,object value,int tag){box[index]=value;return value;}", "T1 M<T1, T2>(global::Cases.Box box, int index, T1 value, T2 tag) where T1 : notnull"),
            ("FixedArrayElement", "static object[] field=new object[1];static object M(object value,int tag){field[0]=value;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedCovariantArray", "static object[] field=null!;static object[] M(object[] value,int tag){field=value;return value;}", "T1[] M<T1, T2>(T1[] value, T2 tag) where T1 : class"),
            ("FixedTuple", "static (object,int) field;static (object,int) M((object,int) value,int tag){field=value;return value;}", "(T1, int) M<T1, T2>((T1, int) value, T2 tag) where T1 : notnull"),
            ("FixedCovariantDelegate", "static Func<object> field=null!;static Func<object> M(Func<object> value,int tag){field=value;return value;}", "global::System.Func<T1> M<T1, T2>(global::System.Func<T1> value, T2 tag) where T1 : class"),
            ("FixedInvariantContainer", "static List<object> field=null!;static List<object> M(List<object> value,int tag){field=value;return value;}", "global::System.Collections.Generic.List<object> M<T1>(global::System.Collections.Generic.List<object> value, T1 tag)"),
            ("FixedDeclaredLocal", "static object M(object value,int tag){object stored=value;_=stored;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedUninitializedLocal", "static object M(object value,int tag){object stored;stored=value;_=stored;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedDeconstructedField", "static object field=null!;static object M(object value,int tag){(field,_)=(value,0);return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedTypedDeconstruction", "static object M(object value,int tag){(object stored,int number)=(value,0);_=stored;_=number;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedMixedDeconstruction", "static (object,int) M(object value,int other,int tag){(object stored,var second)=(value,other);_=stored;_=second;return(value,other);}", "(T1, T2) M<T1, T2, T3>(T1 value, T2 other, T3 tag) where T1 : notnull"),
            ("FixedNullableDeconstructedField", "static object? field;static object? M(object? value,int tag){(field,_)=(value,0);return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("FixedNullableTypedDeconstruction", "static object? M(object? value,int tag){(object? stored,int number)=(value,0);_=stored;_=number;return value;}", "T1? M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("FixedLocalRead", "static object M(object value,int tag){object stored=value;return stored;}", "object M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedLocalConditional", "static (object,object) M(bool flag,object left,object right,int tag){object stored=flag?left:right;_=stored;return(left,right);}", "(T1, T2) M<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull"),
            ("FixedFieldSwitch", "static object field=null!;static (object,object) M(int key,object left,object right,int tag){field=key switch{0=>left,_=>right};return(left,right);}", "(T1, T2) M<T1, T2, T3>(int key, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull"),
            ("FixedNullableAwait", "static object? field;static async Task<object?> M(Task<object?> value,int tag){field=await value;return await value;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 tag) where T1 : class"),
            ("FixedAsyncLocalCapture", "static object field=null!;static Task<object> M(Task<object> value,int tag){async Task Local(){field=await value;}_=Local();return value;}", "global::System.Threading.Tasks.Task<T1> M<T1, T2>(global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : notnull"),
            ("FixedValueTaskAwait", "static object field=null!;static async ValueTask<object> M(ValueTask<object> value,int tag){field=await value;return await value;}", "global::System.Threading.Tasks.ValueTask<T1> M<T1, T2>(global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : notnull"),
            ("FixedObjectInitializer", "class Box{public object Value=null!;}static Box M(object value,int tag)=>new Box{Value=value};", "global::Cases.Box M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedNullableInitializer", "class Box{public object? Value;}static Box M(object? value,int tag)=>new Box{Value=value};", "global::Cases.Box M<T1, T2>(T1? value, T2 tag) where T1 : class"),
            ("FixedInterfaceField", "static IComparable field=null!;static IComparable M(IComparable value,int tag){field=value;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : global::System.IComparable"),
            ("FixedBaseField", "class Base{}static Base field=null!;static Base M(Base value,int tag){field=value;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : global::Cases.Base"),
            ("FixedOriginalBoxing", "static object field=null!;static int M(int value,int tag){field=value;return value;}", "T1 M<T1, T2>(T1 value, T2 tag) where T1 : notnull"),
            ("FixedOriginalWidening", "static long field;static int M(int value,int tag){field=value;return value;}", "int M<T1>(int value, T1 tag)"),
            ("InferredLocalRetainsStorage", "static object M(object value,int tag){var copy=value;copy=new object();return value;}", "object M<T1>(object value, T1 tag)"),
            ("ExplicitRefLocalRetainsStorage", "class Box<T>{public T Value=default!;}static Box<object> M(Box<object> value,int tag){ref object alias=ref value.Value;_=alias;return value;}", "global::Cases.Box<object> M<T1>(global::Cases.Box<object> value, T1 tag)"),
            ("ExplicitReadonlyRefLocalRetainsStorage", "class Box<T>{public T Value=default!;}static Box<object> M(Box<object> value,int tag){ref readonly object alias=ref value.Value;_=alias;return value;}", "global::Cases.Box<object> M<T1>(global::Cases.Box<object> value, T1 tag)"),
            ("RefReassignmentRetainsStorage", "static ref object M(ref object left,ref object right,int tag){ref object alias=ref left;alias=ref right;return ref alias;}", "ref object M<T1>(ref object left, ref object right, T1 tag)")
        ];
        foreach (var test in tests)
        {
            var source = "#nullable enable\nusing System;using System.Collections.Generic;using System.Threading.Tasks;class Cases{" + test.Source + "}";
            var compilation = Program.Compile([CSharpSyntaxTree.ParseText(source, cancellationToken: cancellationToken)]);
            var errors = compilation.GetDiagnostics(cancellationToken).Where(diagnostic =>
            { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }).ToArray();
            if (errors.Length != 0)
            { throw new InvalidOperationException(test.Name + " fixture: " + errors[0]); }
            var result = Program.Results(compilation, cancellationToken).Single(candidate =>
            { ArgumentNullException.ThrowIfNull(candidate); return candidate.Method == "M"; });
            if (result.Status != "Suggestion" || result.Signature != test.Signature)
            { throw new InvalidOperationException(test.Name + ": expected " + test.Signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
            Program.Log.Info("PASS " + test.Name);
        }
        return tests.Length;
    }
}
