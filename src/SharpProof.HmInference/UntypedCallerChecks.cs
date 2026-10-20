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
            ("TupleNull", "static (object? Value,int Count) M((object? Value,int Count) value,int tag)=>value;static (object?,int) Caller()=>M((null,1),0);", "(object? Value, T1 Count) M<T1, T2>((object? Value, T1 Count) value, T2 tag)"),
            ("NestedTupleNull", "static ((object? Value,int Count) Item,string Tag) M(((object? Value,int Count) Item,string Tag) value,int tag)=>value;static ((object?,int),string) Caller()=>M(((null,1),\"ok\"),0);", "((object? Value, T1 Count) Item, T2 Tag) M<T1, T2, T3>(((object? Value, T1 Count) Item, T2 Tag) value, T3 tag)"),
            ("ConditionalNull", "static object? M(object? value,int tag)=>value;static object? Caller(bool flag)=>M(flag?null:default,0);", "object? M<T1>(object? value, T1 tag)"),
            ("SwitchNull", "static object? M(object? value,int tag)=>value;static object? Caller(bool flag)=>M(flag switch{true=>null,false=>default},0);", "object? M<T1>(object? value, T1 tag)"),
            ("CollectionNull", "static int M(System.Collections.Generic.List<object?> value,int tag)=>value.Count;static int Caller()=>M([null],0);", "int M<T1>(global::System.Collections.Generic.List<object?> value, T1 tag)"),
            ("AsyncTupleOutput", "static System.Func<int,System.Threading.Tasks.Task<(object? Value,int Count)>> M(System.Func<int,System.Threading.Tasks.Task<(object? Value,int Count)>> map,int tag)=>map;static System.Func<int,System.Threading.Tasks.Task<(object? Value,int Count)>> Caller()=>M(async value=>{await System.Threading.Tasks.Task.Yield();return (null,value);},0);", "global::System.Func<int, global::System.Threading.Tasks.Task<(object? Value, int Count)>> M<T1>(global::System.Func<int, global::System.Threading.Tasks.Task<(object? Value, int Count)>> map, T1 tag)"),

            ("AsyncTypedTupleOutput", "static System.Func<int,System.Threading.Tasks.Task<(object? Value,int Count)>> M(System.Func<int,System.Threading.Tasks.Task<(object? Value,int Count)>> map,int tag)=>map;static System.Func<int,System.Threading.Tasks.Task<(object? Value,int Count)>> Caller()=>M(async value=>{await System.Threading.Tasks.Task.Yield();return ((object?)null,value);},0);", "global::System.Func<int, global::System.Threading.Tasks.Task<(T1? Value, T2 Count)>> M<T1, T2, T3>(global::System.Func<int, global::System.Threading.Tasks.Task<(T1? Value, T2 Count)>> map, T3 tag) where T1 : class"),
            ("NullableTuple", "static (object? Value,int Count)? M((object? Value,int Count)? value,int tag)=>value;static (object?,int)? Caller()=>M((null,1),0);", "(object? Value, int Count)? M<T1>((object? Value, int Count)? value, T1 tag)"),
            ("ConditionalTuple", "static (object? Value,int Count) M((object? Value,int Count) value,int tag)=>value;static (object?,int) Caller(bool flag)=>M(flag?(null,1):(null,2),0);", "(object? Value, int Count) M<T1>((object? Value, int Count) value, T1 tag)"),
            ("NestedCollection", "static int M(System.Collections.Generic.List<System.Collections.Generic.List<object?>> value,int tag)=>value.Count;static int Caller()=>M([[null]],0);", "int M<T1>(global::System.Collections.Generic.List<global::System.Collections.Generic.List<object?>> value, T1 tag)"),
            ("NaturalConditional", "static string? M(string? value,object? tag)=>value;static string? Caller(bool flag)=>M(flag?null:\"ok\",null);", "T1? M<T1>(T1? value, object? tag) where T1 : class"),
            ("LiftedNullableValue", "static int? M(int? value,int tag)=>value;static int? Caller()=>M(1,0);", "int? M<T1>(int? value, T1 tag)"),
            ("SharedNull", "static object? M(object? value,object? hint,object? tag,bool flag)=>flag?value:hint;static object? Caller()=>M(null,(object?)new object(),null,true);", "T1? M<T1>(T1? value, T1? hint, object? tag, bool flag) where T1 : class"),
            ("SharedTupleNull", "static object? M((object? Value,object? Hint) pair,object? tag,bool flag)=>flag?pair.Value:pair.Hint;static object? Caller()=>M((null,(object?)new object()),null,true);", "T1? M<T1>((T1? Value, T1? Hint) pair, object? tag, bool flag) where T1 : class"),
            ("SharedAsyncNull", "static async System.Threading.Tasks.Task<object?> M(object? value,object? hint,object? tag,bool flag){await System.Threading.Tasks.Task.Yield();return flag?value:hint;}static System.Threading.Tasks.Task<object?> Caller()=>M(null,(object?)new object(),null,true);", "global::System.Threading.Tasks.Task<T1?> M<T1>(T1? value, T1? hint, object? tag, bool flag) where T1 : class"),
            ("SharedNullWithFreeTag", "static object? M(object? value,object? hint,object? tag,int ignored,bool flag)=>flag?value:hint;static object? Caller()=>M(null,(object?)new object(),null,0,true);", "T1? M<T1, T2>(T1? value, T1? hint, object? tag, T2 ignored, bool flag) where T1 : class"),
            ("MethodGroup", "static System.Func<int,string> M(System.Func<int,string> map,int tag)=>map;static string Echo(int value)=>\"ok\";static System.Func<int,string> Caller()=>M(Echo,0);", "global::System.Func<int, T1> M<T1, T2>(global::System.Func<int, T1> map, T2 tag)"),
            ("GenericMethodGroup", "static System.Func<int,string> M(System.Func<int,string> map,int tag)=>map;static string Echo<T>(T value)=>\"ok\";static System.Func<int,string> Caller()=>M(Echo,0);", "global::System.Func<int, T1> M<T1, T2>(global::System.Func<int, T1> map, T2 tag)"),
            ("OverloadedMethodGroup", "static System.Func<int,string> M(System.Func<int,string> map,int tag)=>map;static string Echo(int value)=>\"int\";static string Echo(long value)=>\"long\";static System.Func<int,string> Caller()=>M(Echo,0);", "global::System.Func<int, T1> M<T1, T2>(global::System.Func<int, T1> map, T2 tag)"),
            ("AsyncMethodGroup", "static System.Func<int,System.Threading.Tasks.Task<string?>> M(System.Func<int,System.Threading.Tasks.Task<string?>> map,int tag)=>map;static async System.Threading.Tasks.Task<string?> Echo<T>(T value){await System.Threading.Tasks.Task.Yield();return null;}static System.Func<int,System.Threading.Tasks.Task<string?>> Caller()=>M(Echo,0);", "global::System.Func<int, global::System.Threading.Tasks.Task<T1?>> M<T1, T2>(global::System.Func<int, global::System.Threading.Tasks.Task<T1?>> map, T2 tag) where T1 : class"),
            ("CollectionLambda", "static System.Collections.Generic.List<System.Func<int,string>> M(System.Collections.Generic.List<System.Func<int,string>> maps,int tag)=>maps;static System.Collections.Generic.List<System.Func<int,string>> Caller()=>M([value=>\"ok\"],0);", "global::System.Collections.Generic.List<global::System.Func<int, T1>> M<T1, T2>(global::System.Collections.Generic.List<global::System.Func<int, T1>> maps, T2 tag)"),
            ("CollectionMethodGroup", "static System.Collections.Generic.List<System.Func<int,string>> M(System.Collections.Generic.List<System.Func<int,string>> maps,int tag)=>maps;static string Echo<T>(T value)=>\"ok\";static System.Collections.Generic.List<System.Func<int,string>> Caller()=>M([Echo],0);", "global::System.Collections.Generic.List<global::System.Func<int, T1>> M<T1, T2>(global::System.Collections.Generic.List<global::System.Func<int, T1>> maps, T2 tag)"),
            ("SharedCollectionNull", "static object? M(System.Collections.Generic.List<object?> values,object? hint,object? tag,bool flag)=>flag?values[0]:hint;static object? Caller()=>M([null],(object?)new object(),null,true);", "T1? M<T1>(global::System.Collections.Generic.List<T1?> values, T1? hint, object? tag, bool flag) where T1 : class"),
            ("InstanceGenericMethodGroup", "sealed class Receiver{public string Echo<T>(T value)=>\"ok\";}static System.Func<int,string> M(System.Func<int,string> map,int tag)=>map;static System.Func<int,string> Caller()=>M(new Receiver().Echo,0);", "global::System.Func<int, T1> M<T1, T2>(global::System.Func<int, T1> map, T2 tag)"),
            ("NullableMethodGroup", "static System.Func<int,string>? M(System.Func<int,string>? map,int tag)=>map;static string Echo<T>(T value)=>\"ok\";static System.Func<int,string>? Caller()=>M(Echo,0);", "global::System.Func<int, T1>? M<T1, T2>(global::System.Func<int, T1>? map, T2 tag)"),
            ("CustomDelegateMethodGroup", "delegate V Mapper<U,V>(U value);static Mapper<int,string> M(Mapper<int,string> map,int tag)=>map;static string Echo<T>(T value)=>\"ok\";static Mapper<int,string> Caller()=>M(Echo,0);", "global::Cases.Mapper<int, T1> M<T1, T2>(global::Cases.Mapper<int, T1> map, T2 tag)"),
            ("VoidMethodGroup", "static System.Action<int> M(System.Action<int> sink,int tag)=>sink;static void Consume<T>(T value){}static System.Action<int> Caller()=>M(Consume,0);", "global::System.Action<int> M<T1>(global::System.Action<int> sink, T1 tag)"),
            ("NoInputMethodGroup", "static System.Func<string> M(System.Func<string> map,int tag)=>map;static string Echo()=>\"ok\";static System.Func<string> Caller()=>M(Echo,0);", "global::System.Func<T1> M<T1, T2>(global::System.Func<T1> map, T2 tag)"),
            ("ClosedDelegateGroup", "delegate string Mapper(int value);static Mapper M(Mapper map,int tag)=>map;static string Echo(int value)=>\"ok\";static Mapper Caller()=>M(Echo,0);", "global::Cases.Mapper M<T1>(global::Cases.Mapper map, T1 tag)"),
            ("ClosedDelegateGenericGroup", "delegate string Mapper(int value);static Mapper M(Mapper map,int tag)=>map;static string Echo<T>(T value)=>\"ok\";static Mapper Caller()=>M(Echo,0);", "global::Cases.Mapper M<T1>(global::Cases.Mapper map, T1 tag)"),
            ("ClosedDelegateLambda", "delegate string Mapper(int value);static Mapper M(Mapper map,int tag)=>map;static Mapper Caller()=>M(value=>\"ok\",0);", "global::Cases.Mapper M<T1>(global::Cases.Mapper map, T1 tag)"),
            ("ClosedDelegateTypedLambda", "delegate string Mapper(int value);static Mapper M(Mapper map,int tag)=>map;static Mapper Caller()=>M((int value)=>\"ok\",0);", "global::Cases.Mapper M<T1>(global::Cases.Mapper map, T1 tag)"),
            ("ClosedNullableDelegate", "delegate string Mapper(int value);static Mapper? M(Mapper? map,int tag)=>map;static string Echo(int value)=>\"ok\";static Mapper? Caller()=>M(Echo,0);", "global::Cases.Mapper? M<T1>(global::Cases.Mapper? map, T1 tag)"),
            ("OwnerDelegateGroup", "class Owner<T>{public delegate V Mapper<V>(int value);}static Owner<int>.Mapper<string> M(Owner<int>.Mapper<string> map,int tag)=>map;static string Echo<T>(T value)=>\"ok\";static Owner<int>.Mapper<string> Caller()=>M(Echo,0);", "global::Cases.Owner<int>.Mapper<T1> M<T1, T2>(global::Cases.Owner<int>.Mapper<T1> map, T2 tag)"),
            ("OwnerDelegateLambda", "class Owner<T>{public delegate V Mapper<V>(int value);}static Owner<int>.Mapper<string> M(Owner<int>.Mapper<string> map,int tag)=>map;static Owner<int>.Mapper<string> Caller()=>M(value=>\"ok\",0);", "global::Cases.Owner<int>.Mapper<T1> M<T1, T2>(global::Cases.Owner<int>.Mapper<T1> map, T2 tag)"),
            ("ClosedDelegateExpression", "delegate string Mapper(int value);static System.Linq.Expressions.Expression<Mapper> M(System.Linq.Expressions.Expression<Mapper> map,int tag)=>map;static System.Linq.Expressions.Expression<Mapper> Caller()=>M(value=>\"ok\",0);", "global::System.Linq.Expressions.Expression<global::Cases.Mapper> M<T1>(global::System.Linq.Expressions.Expression<global::Cases.Mapper> map, T1 tag)"),
            ("ClosedAsyncDelegate", "delegate System.Threading.Tasks.Task<string?> Mapper(int value);static Mapper M(Mapper map,int tag)=>map;static async System.Threading.Tasks.Task<string?> Echo<T>(T value){await System.Threading.Tasks.Task.Yield();return null;}static Mapper Caller()=>M(Echo,0);", "global::Cases.Mapper M<T1>(global::Cases.Mapper map, T1 tag)"),
            ("ClosedDelegateTypedControl", "delegate string Mapper(int value);static Mapper M(Mapper map,int tag)=>map;static Mapper Caller()=>M((Mapper)(value=>\"ok\"),0);", "T1 M<T1, T2>(T1 map, T2 tag)"),
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
            CSharpSyntaxTree.ParseText("#nullable enable\npublic static class ContextualExtensions{public static object? M(this int receiver,object? value,int tag)=>value;public static int N(System.Collections.Generic.List<object?> values,int tag)=>values.Count;}", cancellationToken: cancellationToken),
            CSharpSyntaxTree.ParseText("public sealed class ContextReceiver{}public static class ContextGroups{public static string Render<T>(this ContextReceiver receiver,T value)=>\"ok\";public static System.Func<int,string> G(System.Func<int,string> map,int tag)=>map;}", cancellationToken: cancellationToken),
            CSharpSyntaxTree.ParseText("#nullable enable\npublic static class ContextualConsumer{public static object? Run()=>1.M(tag:0,value:null);public static int Count()=>ContextualExtensions.N([null],0);public static System.Func<int,string> Group()=>ContextGroups.G(new ContextReceiver().Render,0);}", cancellationToken: cancellationToken)]);
        var crossResult = Program.Results(crossFile, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; });
        if (crossResult.Status != "Suggestion" || crossResult.Signature != "object? M<T1, T2>(this T1 receiver, object? value, T2 tag)")
        { throw new InvalidOperationException("Cross-file named extension inference: " + (crossResult.Signature ?? crossResult.Message)); }
        Program.Log.Info("PASS UntypedCallerCrossFileNamedExtension");
        var crossCollection = Program.Results(crossFile, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "N"; });
        if (crossCollection.Status != "Suggestion" || crossCollection.Signature != "int N<T1>(global::System.Collections.Generic.List<object?> values, T1 tag)")
        { throw new InvalidOperationException("Cross-file contextual collection inference: " + (crossCollection.Signature ?? crossCollection.Message)); }
        Program.Log.Info("PASS UntypedCallerCrossFileCollection");
        var crossGroup = Program.Results(crossFile, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "G"; });
        if (crossGroup.Status != "Suggestion" || crossGroup.Signature != "global::System.Func<int, T1> G<T1, T2>(global::System.Func<int, T1> map, T2 tag)")
        { throw new InvalidOperationException("Cross-file reduced extension method group inference: " + (crossGroup.Signature ?? crossGroup.Message)); }
        Program.Log.Info("PASS UntypedCallerCrossFileReducedMethodGroup");
        var fixedOnly = Program.Compile([CSharpSyntaxTree.ParseText("#nullable enable\nstatic class Cases{static object? M(object? value)=>value;static object? Run()=>M(null);}", cancellationToken: cancellationToken)]);
        if (Program.Results(fixedOnly, cancellationToken).Single(result => { ArgumentNullException.ThrowIfNull(result); return result.Method == "M"; }).Status == "Suggestion")
        { throw new InvalidOperationException("A fully pinned contextual signature cannot become generic."); }
        Program.Log.Info("PASS UntypedCallerNoGenericComponentRemains");
        return tests.Length + 4;
    }
}
