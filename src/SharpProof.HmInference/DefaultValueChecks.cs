using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.HmInference;

internal static class DefaultValueChecks
{
    public static int Run(CancellationToken cancellationToken)
    {
        (string Name, string Source, string Signature)[] tests =
        [
            ("DefaultOwnerNestedCallbacks", "static void Visit<T>(IEnumerable<T> values,Action<T> action){foreach(var value in values)action(value);}static void M(IEnumerable<IEnumerable<int[]>> values,int tag){Visit(values,rows=>Visit(rows,row=>{ref var alias=ref row[0];alias=default;}));}", "void M<T1, T2>(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.IEnumerable<T1?[]>> values, T2 tag)"),
            ("DefaultOwnerCallbackDependency", "static void Visit<T>(IEnumerable<T> values,Action<T> action){foreach(var value in values)action(value);}static void M(IEnumerable<int[]> values,int tag){var source=values;var other=tag;Visit(source,row=>{var copy=row;ref var alias=ref copy[0];alias=default;GC.KeepAlive(other);});}", "void M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1?[]> values, T2 tag) where T2 : notnull"),
            ("DefaultOwnerCallbackSibling", "static U Visit<T,U>(T value,U tag,Func<T,U,U> action)=>action(value,tag);static string M(int[] value,string tag)=>Visit(value,tag,(row,other)=>{ref var alias=ref row[0];alias=default;return other;});", "T2 M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerCallbackArgument", "static void Visit<T>(IEnumerable<T> values,Action<T> action){foreach(var value in values)action(value);}static int M(IEnumerable<int[]> values,int tag){var result=default(int);Visit(values,row=>{ref var alias=ref row[0];alias=default;result=alias;});return result;}", "int M<T1>(global::System.Collections.Generic.IEnumerable<int[]> values, T1 tag)"),
            ("DefaultOwnerCallbackStore", "static void Visit<T>(IEnumerable<T> values,Action<T> action){foreach(var value in values)action(value);}static void M(IEnumerable<int[]> values,int tag){Visit(values,row=>{ref var alias=ref row[0];alias=default;});}", "void M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1?[]> values, T2 tag)"),
            ("DefaultOwnerDelegateCallback", "static void M(Action<Action<int[]>> visit,int tag){visit(row=>{ref var alias=ref row[0];alias=default;});}", "void M<T1, T2>(global::System.Action<global::System.Action<T1?[]>> visit, T2 tag)"),
            ("DefaultOwnerAsyncCallback", "static Task Visit<T>(T value,Func<T,Task> action)=>action(value);static Task M(int[] value,int tag)=>Visit(value,async row=>{await Task.Yield();ref var alias=ref row[0];alias=default;});", "global::System.Threading.Tasks.Task M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerTypedCallbackControl", "static void Visit<T>(IEnumerable<T> values,Action<T> action){foreach(var value in values)action(value);}static void M(IEnumerable<int[]> values,int tag){Visit(values,(int[] row)=>{ref var alias=ref row[0];alias=default;});}", "void M<T1>(global::System.Collections.Generic.IEnumerable<int[]> values, T1 tag)"),
            ("DefaultOwnerOutStore", "static void Copy<T>(T value,out T result){result=value;}static int M(int[] value,int tag){Copy(value,out var row);row[0]=default;return row[0];}", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerOutValueAwait", "static void Copy<T>(T value,out T result){result=value;}static async ValueTask<int> M(ValueTask<int[]> value,int tag){Copy(await value,out var row);ref var alias=ref row[0];alias=default;return alias;}", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(global::System.Threading.Tasks.ValueTask<T1?[]> value, T2 tag)"),
            ("DefaultOwnerOutCapture", "static void Copy<T>(T value,out T result){result=value;}static Func<int> M(int[] value,int tag)=>()=>{Copy(value,out var row);ref var alias=ref row[0];alias=default;return alias;};", "global::System.Func<T1?> M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerOutMultiple", "static void Copy<T,U>(T value,U tag,out T row,out U other){row=value;other=tag;}static (int tag,int value) M(int[] value,int tag){Copy(value,tag,out var row,out var other);ref var alias=ref row[0];alias=default;return(other,alias);}", "(T2 tag, T1? value) M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerOutCall", "static void Copy<T>(T value,out T result){result=value;}static int M(int[] value,int tag){Copy(value,out var row);ref var alias=ref row[0];alias=default;return alias;}", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerOutCondition", "static bool Copy<T>(T value,out T result){result=value;return true;}static int M(int[] value,int tag){if(Copy(value,out var row)){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerOutDictionary", "static int M(Dictionary<int,int[]> value,int key,int tag){if(value.TryGetValue(key,out var row)){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T2? M<T1, T2, T3>(global::System.Collections.Generic.Dictionary<T1, T2?[]> value, T1 key, T3 tag) where T1 : notnull"),
            ("DefaultOwnerOutTuple", "static void Copy<T>(T value,out T result){result=value;}static int M((int[] row,bool flag) value,int tag){Copy(value,out var pair);var(row,_)=pair;ref var alias=ref row[0];alias=default;return alias;}", "T1? M<T1, T2, T3>((T1?[] row, T2 flag) value, T3 tag)"),
            ("DefaultOwnerOutAwait", "static void Copy<T>(T value,out T result){result=value;}static async Task<int> M(Task<int[]> value,int tag){Copy(await value,out var row);ref var alias=ref row[0];alias=default;return alias;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Threading.Tasks.Task<T1?[]> value, T2 tag)"),
            ("DefaultOwnerOutDelegate", "delegate void Copier<T>(T value,out T result);static int M(Copier<int[]> copy,int[] value,int tag){copy(value,out var row);ref var alias=ref row[0];alias=default;return alias;}", "T1? M<T1, T2>(global::Cases.Copier<T1?[]> copy, T1?[] value, T2 tag)"),
            ("DefaultOwnerTypedOutControl", "static void Copy<T>(T value,out T result){result=value;}static int M(int[] value,int tag){Copy(value,out int[] row);ref var alias=ref row[0];alias=default;return alias;}", "int M<T1>(int[] value, T1 tag)"),
            ("DefaultOwnerCustomPattern", "class Box<T>{public T Row;public Box(T row){Row=row;}}static int M(Box<int[]> value,int tag){if(value is {Row:var row}){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(global::Cases.Box<T1?[]> value, T2 tag)"),
            ("DefaultOwnerNestedPattern", "static int M((int[][] rows,bool flag) value,int tag){if(value is {rows:[var row,..]}){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2, T3>((T1?[][] rows, T2 flag) value, T3 tag)"),
            ("DefaultOwnerTupleItemPattern", "static int M((int[] row,bool flag) value,int tag){if(value is {Item1:var row}){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2, T3>((T1?[] row, T2 flag) value, T3 tag)"),
            ("DefaultOwnerValueAsyncPattern", "static async ValueTask<int> M(ValueTask<int[]> value,int tag){if(await value is var row){ref var alias=ref row[0];alias=default;return alias;}return default;}", "global::System.Threading.Tasks.ValueTask<T1?> M<T1, T2>(global::System.Threading.Tasks.ValueTask<T1?[]> value, T2 tag)"),
            ("DefaultOwnerVarPattern", "static int M(int[] value,int tag){if(value is var row){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerPropertyPattern", "static int M((int[] row,bool flag) value,int tag){if(value is {row:var row}){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2, T3>((T1?[] row, T2 flag) value, T3 tag)"),
            ("DefaultOwnerPositionalPattern", "static int M((int[] row,bool flag) value,int tag){if(value is (var row,_)){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2, T3>((T1?[] row, T2 flag) value, T3 tag)"),
            ("DefaultOwnerListPattern", "static int M(int[][] value,int tag){if(value is [var row,..]){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(T1?[][] value, T2 tag)"),
            ("DefaultOwnerSlicePattern", "static int M(int[] value,int tag){if(value is [..var row]){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerSwitchPattern", "static int M((int[] row,bool flag) value,int tag){switch(value){case(var row,_):ref var alias=ref row[0];alias=default;return alias;}}", "T1? M<T1, T2, T3>((T1?[] row, T2 flag) value, T3 tag)"),
            ("DefaultOwnerSwitchExpression", "static int M(int[] value,int tag)=>value switch {var row=>row[0]=default};", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultOwnerAsyncPattern", "static async Task<int> M(Task<int[]> value,int tag){if(await value is var row){ref var alias=ref row[0];alias=default;return alias;}return default;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Threading.Tasks.Task<T1?[]> value, T2 tag)"),
            ("DefaultOwnerTypedPatternControl", "static int M(int[] value,int tag){if(value is int[] row){ref var alias=ref row[0];alias=default;return alias;}return default;}", "int M<T1, T2>(T1[] value, T2 tag)"),
            ("DefaultOwnerArrayLoop", "static int M(int[][] value,int tag){foreach(var row in value){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(T1?[][] value, T2 tag)"),
            ("DefaultOwnerEnumerableLoop", "static int M(IEnumerable<int[]> value,int tag){foreach(var row in value){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(global::System.Collections.Generic.IEnumerable<T1?[]> value, T2 tag)"),
            ("DefaultOwnerListLoop", "static int M(List<int[]> value,int tag){foreach(var row in value){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(global::System.Collections.Generic.List<T1?[]> value, T2 tag)"),
            ("DefaultOwnerNestedLoop", "static int M(IEnumerable<IEnumerable<int[]>> value,int tag){foreach(var batch in value)foreach(var row in batch){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.IEnumerable<T1?[]>> value, T2 tag)"),
            ("DefaultOwnerTuple", "static int M((int[] row,bool flag) value,int tag){var(row,_)=value;ref var alias=ref row[0];alias=default;return alias;}", "T1? M<T1, T2, T3>((T1?[] row, T2 flag) value, T3 tag)"),
            ("DefaultOwnerNestedTuple", "static int M((int[] row,(bool flag,int count) rest) value,int tag){var(row,(_,_))=value;ref var alias=ref row[0];alias=default;return alias;}", "T1? M<T1, T2, T3, T4>((T1?[] row, (T2 flag, T3 count) rest) value, T4 tag)"),
            ("DefaultOwnerCustomDeconstruct", "class Pair<T,U>{public T Row;public U Flag;public Pair(T row,U flag){Row=row;Flag=flag;}public void Deconstruct(out T row,out U flag){row=Row;flag=Flag;}}static int M(Pair<int[],bool> value,int tag){var(row,_)=value;ref var alias=ref row[0];alias=default;return alias;}", "T1? M<T1, T2, T3>(global::Cases.Pair<T1?[], T2> value, T3 tag)"),
            ("DefaultOwnerForeachDeconstruct", "static int M(IEnumerable<(int[] row,bool flag)> value,int tag){foreach(var(row,_)in value){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2, T3>(global::System.Collections.Generic.IEnumerable<(T1?[] row, T2 flag)> value, T3 tag)"),
            ("DefaultOwnerAsyncLoop", "static async Task<int> M(IAsyncEnumerable<int[]> value,int tag){await foreach(var row in value){ref var alias=ref row[0];alias=default;return alias;}return default;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Collections.Generic.IAsyncEnumerable<T1?[]> value, T2 tag)"),
            ("DefaultOwnerTypedLoopControl", "static int M(IEnumerable<int[]> value,int tag){foreach(int[] row in value){ref var alias=ref row[0];alias=default;return alias;}return default;}", "int M<T1>(global::System.Collections.Generic.IEnumerable<int[]> value, T1 tag)"),
            ("DefaultOwnerTypedDeconstructControl", "static int M((int[] row,bool flag) value,int tag){(int[] row,bool flag)=value;ref var alias=ref row[0];alias=default;return alias;}", "int M<T1>((int[] row, bool flag) value, T1 tag)"),
            ("DefaultOwnerRefLoop", "static int M(Span<int[]> value,int tag){foreach(ref var row in value){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2>(global::System.Span<T1?[]> value, T2 tag)"),
            ("DefaultOwnerForeachCustom", "class Pair<T,U>{public T Row;public U Flag;public Pair(T row,U flag){Row=row;Flag=flag;}public void Deconstruct(out T row,out U flag){row=Row;flag=Flag;}}static int M(IEnumerable<Pair<int[],bool>> value,int tag){foreach(var(row,_)in value){ref var alias=ref row[0];alias=default;return alias;}return default;}", "T1? M<T1, T2, T3>(global::System.Collections.Generic.IEnumerable<global::Cases.Pair<T1?[], T2>> value, T3 tag)"),
            ("DefaultOwnerAwaitDeconstruct", "static async Task<int> M(IAsyncEnumerable<(int[] row,bool flag)> value,int tag){await foreach(var(row,_)in value){ref var alias=ref row[0];alias=default;return alias;}return default;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2, T3>(global::System.Collections.Generic.IAsyncEnumerable<(T1?[] row, T2 flag)> value, T3 tag)"),
            ("DefaultOwnerReassignedDeconstruct", "static int M(int[] first,int[] second,int tag){var row=first;(row,_)=(second,0);ref var alias=ref row[0];alias=default;return first[0];}", "T1? M<T1, T2>(T1?[] first, T1?[] second, T2 tag)"),
            ("DefaultOwnerMixedDeconstruct", "static int M(int[] first,int[] second,int tag){var row=first;(row,var other)=(second,0);ref var alias=ref row[0];alias=default;return first[0];}", "T1? M<T1, T2>(T1?[] first, T1?[] second, T2 tag)"),
            ("DefaultOwnerMixedSource", "static int M(int[] first,int[] second,int tag){var row=first;(row,var other)=(second,first);ref var alias=ref other[0];alias=default;return row[0];}", "T1? M<T1, T2>(T1?[] first, T1?[] second, T2 tag)"),
            ("DefaultAliasLocal", "static int M(int value,int tag){var result=value;ref var alias=ref result;alias=default;return result;}", "T1? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAliasChain", "static int M(int value,int tag){var result=value;ref var first=ref result;ref var second=ref first;second=default;return result;}", "T1? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAliasCopy", "static int M(int value,int tag){var result=value;var copy=result;ref var alias=ref result;alias=default;return copy;}", "T1 M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAliasCapture", "static Func<int> M(int value,int tag){var result=value;var read=()=>result;ref var alias=ref result;alias=default;return read;}", "global::System.Func<T1?> M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAliasScope", "static int M(int value,int tag){var result=value;{ref var alias=ref result;alias=default;}return result;}", "T1? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAliasRebind", "static (int left,int right) M(int first,int second,int tag){var left=first;var right=second;ref var alias=ref left;alias=ref right;alias=default;return(left,right);}", "(T1? left, T1? right) M<T1, T2>(T1 first, T1 second, T2 tag)"),
            ("DefaultAliasTuple", "static (int left,int right) M((int left,int right) value,int tag){var result=value;ref var alias=ref result;alias=default;return result;}", "(T1? left, T2? right) M<T1, T2, T3>((T1 left, T2 right) value, T3 tag)"),
            ("DefaultAliasRefParameter", "static int M(ref int value,int tag){value=default;return value;}", "T1? M<T1, T2>(ref T1? value, T2 tag)"),
            ("DefaultAliasOutParameter", "static int M(int value,out int output){output=value;output=default;return output;}", "T1? M<T1>(T1 value, out T1? output)"),
            ("DefaultAliasTypedControl", "static int M(int value,int tag){ref int alias=ref value;alias=default;return value;}", "int M<T1>(int value, T1 tag)"),
            ("DefaultAliasArrayControl", "static int M(int[] value,int tag){ref var alias=ref value[0];alias=default;return value[0];}", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultIndirectMatrix", "static int M(int[,] value,int tag){ref var alias=ref value[0,0];alias=default;return alias;}", "T1? M<T1, T2>(T1?[,] value, T2 tag)"),
            ("DefaultIndirectJagged", "static int M(int[][] value,int tag){ref var alias=ref value[0][0];alias=default;return alias;}", "T1? M<T1, T2>(T1?[][] value, T2 tag)"),
            ("DefaultIndirectSpan", "static int M(Span<int> value,int tag){ref var alias=ref value[0];alias=default;return alias;}", "T1? M<T1, T2>(global::System.Span<T1?> value, T2 tag)"),
            ("DefaultIndirectField", "class Box<T>{public T Value;public Box(T value){Value=value;}}static int M(Box<int> box,int tag){ref var alias=ref box.Value;alias=default;return alias;}", "T1? M<T1, T2>(global::Cases.Box<T1?> box, T2 tag)"),
            ("DefaultIndirectProperty", "class Box<T>{T value;public Box(T value){this.value=value;}public ref T Slot=>ref value;}static int M(Box<int> box,int tag){ref var alias=ref box.Slot;alias=default;return alias;}", "T1? M<T1, T2>(global::Cases.Box<T1?> box, T2 tag)"),
            ("DefaultIndirectIndexer", "class Box<T>{T[] values;public Box(T[] values){this.values=values;}public ref T this[int index]=>ref values[index];}static int M(Box<int> box,int tag){ref var alias=ref box[0];alias=default;return alias;}", "T1? M<T1, T2>(global::Cases.Box<T1?> box, T2 tag)"),
            ("DefaultIndirectTupleField", "static int M((int left,int right) value,int tag){ref var alias=ref value.left;alias=default;return alias;}", "T1? M<T1, T2, T3>((T1? left, T2 right) value, T3 tag)"),
            ("DefaultIndirectBorrow", "static ref T Borrow<T>(ref T value)=>ref value;static int M(int[] value,int tag){ref var alias=ref Borrow(ref value[0]);alias=default;return value[0];}", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultIndirectReceiverCopy", "static int M(int[] value,int tag){var copy=value;ref var alias=ref copy[0];alias=default;return value[0];}", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultIndirectValueCopy", "static int M(int[] value,int tag){var copy=value[0];ref var alias=ref value[0];alias=default;return copy;}", "T1? M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultIndirectCapture", "static Func<int> M(int[] value,int tag){var read=()=>value[0];ref var alias=ref value[0];alias=default;return read;}", "global::System.Func<T1?> M<T1, T2>(T1?[] value, T2 tag)"),
            ("DefaultIndirectFill", "static int M(int[] value,int fill,int tag){value[0]=fill;ref var alias=ref value[0];alias=default;return fill;}", "T1 M<T1, T2>(T1?[] value, T1 fill, T2 tag)"),
            ("DefaultIndirectTypedControl", "static int M(int[] value,int tag){ref int alias=ref value[0];alias=default;return alias;}", "int M<T1>(int[] value, T1 tag)"),
            ("DefaultIndirectRebind", "static int M(int[] first,int[] second,int tag){ref var alias=ref first[0];alias=ref second[0];alias=default;return alias;}", "T1? M<T1, T2>(T1?[] first, T1?[] second, T2 tag)"),
            ("DefaultIndirectConditional", "static int M(bool flag,int[] first,int[] second,int tag){ref var alias=ref(flag?ref first[0]:ref second[0]);alias=default;return alias;}", "T1? M<T1, T2>(bool flag, T1?[] first, T1?[] second, T2 tag)"),
            ("DefaultIndirectReassignedReceiver", "static int M(int[] first,int[] second,int tag){var receiver=first;receiver=second;ref var alias=ref receiver[0];alias=default;return first[0];}", "T1? M<T1, T2>(T1?[] first, T1?[] second, T2 tag)"),
            ("DefaultIndirectSibling", "class Pair<T,U>{public T Left;public U Right;public Pair(T left,U right){Left=left;Right=right;}}static int M(Pair<int,int> pair,int tag){ref var alias=ref pair.Left;alias=default;return pair.Right;}", "T2 M<T1, T2, T3>(global::Cases.Pair<T1?, T2> pair, T3 tag)"),
            ("DefaultIndirectWidening", "static long M(long[] value,int fill,int tag){value[0]=fill;var copy=value[0];ref var alias=ref value[0];alias=default;return copy;}", "long M<T1>(long[] value, int fill, T1 tag)"),
            ("DefaultIndirectNullableLift", "static int? M(int?[] value,int fill,int tag){value[0]=fill;ref var alias=ref value[0];alias=default;return alias;}", "T1? M<T1, T2>(T1?[] value, T1 fill, T2 tag) where T1 : struct"),
            ("DefaultAliasCycle", "static int M(int value,int tag){var result=value;ref var first=ref result;ref var second=ref first;first=ref second;second=default;return result;}", "T1? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAliasConditional", "static (int left,int right) M(bool flag,int first,int second,int tag){var left=first;var right=second;ref var alias=ref(flag?ref left:ref right);alias=default;return(left,right);}", "(T1? left, T1? right) M<T1, T2>(bool flag, T1 first, T1 second, T2 tag)"),
            ("DefaultAliasConditionalStore", "static int M(bool flag,int value,int tag){ref var alias=ref value;alias=flag?value:default;return value;}", "T1? M<T1, T2>(bool flag, T1? value, T2 tag)"),
            ("DefaultAliasSwitchStore", "static int M(int key,int value,int tag){ref var alias=ref value;alias=key switch{0=>value,_=>default};return value;}", "T1? M<T1, T2>(int key, T1? value, T2 tag)"),
            ("DefaultAliasConditionalCapture", "static Func<int> M(bool flag,int value,int tag){var result=value;var read=()=>result;ref var alias=ref result;alias=flag?value:default;return read;}", "global::System.Func<T1?> M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultAliasAsync", "static async Task<int> M(Task<int> value,int tag){var result=await value;ref var alias=ref result;alias=default;return result;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(global::System.Threading.Tasks.Task<T1> value, T2 tag)"),
            ("DefaultAliasLocalFunction", "static int M(int value,int tag){var result=value;void Clear(){ref var alias=ref result;alias=default;}Clear();return result;}", "T1? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAssignedLocal", "static int M(int value,int tag){var result=value;result=default;return result;}", "T1? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAssignedBranch", "static int M(bool flag,int value,int tag){var result=value;if(flag)result=default;return result;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultAssignedParameter", "static int M(int value,int tag){value=default;return value;}", "T1? M<T1, T2>(T1? value, T2 tag)"),
            ("DefaultAssignedCopy", "static int M(int value,int tag){var result=value;var copy=result;result=default;return copy;}", "T1 M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAssignedReplacement", "static int M(bool flag,int value,int other,int tag){var result=value;if(flag)result=default;else result=other;return result;}", "T1? M<T1, T2>(bool flag, T1 value, T1 other, T2 tag)"),
            ("DefaultAssignedCallback", "static Func<int> M(int value,int tag){var result=value;Func<int> read=()=>result;result=default;return read;}", "global::System.Func<int> M<T1>(int value, T1 tag)"),
            ("DefaultAssignedNaturalCallback", "static int M(int value,int tag){var result=value;var read=()=>result;result=default;return read();}", "T1? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAssignedLocalFunction", "static int M(int value,int tag){var result=value;int Read()=>result;result=default;return Read();}", "int M<T1>(int value, T1 tag)"),
            ("DefaultAssignedLocalReset", "static int M(int value,int tag){var result=value;void Reset(){result=default;}Reset();return result;}", "T1? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAssignedLambdaReset", "static int M(int value,int tag){var result=value;Action reset=()=>{result=default;};reset();return result;}", "T1? M<T1, T2>(T1 value, T2 tag)"),
            ("DefaultAssignedMixedCapture", "static int M(bool flag,int value,int tag){var result=value;void Reset(){result=default;}if(flag)Reset();else result=default!;return result;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultAssignedAsync", "static async Task<int> M(bool flag,Task<int> value,int tag){var result=await value;if(flag)result=default;return result;}", "global::System.Threading.Tasks.Task<T1?> M<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag)"),
            ("DefaultAssignedLoop", "static int M(bool flag,int value,int tag){var result=value;while(flag){result=default;break;}return result;}", "T1? M<T1, T2>(bool flag, T1 value, T2 tag)"),
            ("DefaultAssignedTuple", "static (int left,int right) M((int left,int right) value,int tag){var result=value;result=default;return result;}", "(T1? left, T2? right) M<T1, T2, T3>((T1 left, T2 right) value, T3 tag)"),
            ("DefaultAssignedArray", "static int M(int[] values,int tag){values[0]=default;return values[0];}", "T1? M<T1, T2>(T1?[] values, T2 tag)"),
            ("DefaultAssignedList", "static int M(List<int> values,int tag){values[0]=default;return values[0];}", "T1? M<T1, T2>(global::System.Collections.Generic.List<T1?> values, T2 tag)"),
            ("DefaultAssignedTypedControl", "static int M(int value,int tag){int result=value;result=default;return result;}", "int M<T1>(int value, T1 tag)"),
            ("DefaultAssignedRefControl", "static int M(int value,int tag){ref var result=ref value;result=default;return result;}", "T1? M<T1, T2>(T1? value, T2 tag)"),
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
                foreach (var scenario in new[] { "Return", "LocalStore", "AliasParameter", "AliasLocal" })
                {
                    var body = scenario switch { "AliasParameter" => "static int M(bool flag,int value,int tag){ref var alias=ref value;if(flag)alias=default;return value;}", "AliasLocal" => "static int M(bool flag,int value,int tag){var result=value;ref var alias=ref result;if(flag)alias=default;return result;}", "LocalStore" => "static int M(bool flag,int value,int tag){var result=value;if(flag)result=default;return result;}", _ => "static int M(bool flag,int value,int tag)=>flag?value:default;" };
                    var tree = CSharpSyntaxTree.ParseText((nullable ? "#nullable enable\n" : "") + "class Cases{" + body + "}", new CSharpParseOptions(version), cancellationToken: cancellationToken);
                    var result = Program.Results(Program.Compile([tree]), cancellationToken).Single();
                    var signature = !nullable ? "T1 M<T1, T2>(bool flag, T1 value, T2 tag)"
                        : version < LanguageVersion.CSharp9 ? "T1 M<T1, T2>(bool flag, T1 value, T2 tag) where T1 : struct" : "T1? M<T1, T2>(bool flag, T1 value, T2 tag)";
                    if (scenario == "AliasParameter" && nullable && version >= LanguageVersion.CSharp9)
                    { signature = signature.Replace("T1 value", "T1? value", StringComparison.Ordinal); }
                    if (result.Status != "Suggestion" || result.Signature != signature)
                    { failures.Add("Default version " + version + " nullable=" + nullable + ": expected " + signature + "; got " + result.Status + ": " + (result.Signature ?? result.Message)); }
                    else
                    { Program.Log.Info("PASS DefaultValue" + version + "Nullable" + nullable + "Scenario" + scenario); }
                    versionChecks++;
                }
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
