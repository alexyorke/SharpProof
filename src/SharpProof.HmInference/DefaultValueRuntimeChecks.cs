using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class DefaultValueRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
            public sealed class IndirectCell<T>{
                public T Value;public int Reads;public IndirectCell(T value){Value=value;}
                public ref T Slot{get{Reads++;return ref Value;}}
                public ref T this[int index]{get{Reads++;return ref Value;}}
            }
            public sealed class IndirectPair<T,U>{public T Left;public U Right;public IndirectPair(T left,U right){Left=left;Right=right;}}
            public sealed class OwnerPair<T,U>{public T Row;public U Flag;public int Calls;public OwnerPair(T row,U flag){Row=row;Flag=flag;}public void Deconstruct(out T row,out U flag){Calls++;row=Row;flag=Flag;}}
            public sealed class PatternCell<T>{public T Value;public int Reads;public PatternCell(T value){Value=value;}public T Row{get{Reads++;return Value;}}}
            public delegate void OutCopier<T>(T value,out T result);
            public sealed class OwnerRows<T>:IEnumerable<T[]>,IEnumerator<T[]>{
                public T[] Row;public T Restore;public bool RestoreOnDispose;public bool HasRow=true;public Exception? MoveError,DisposeError;
                public int Gets,Moves,Disposals;bool moved;
                public OwnerRows(T[] row,T restore){Row=row;Restore=restore;}
                public IEnumerator<T[]> GetEnumerator(){Gets++;return this;}
                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()=>GetEnumerator();
                public T[] Current=>Row;object System.Collections.IEnumerator.Current=>Current;
                public bool MoveNext(){Moves++;if(MoveError!=null)throw MoveError;if(!HasRow||moved)return false;moved=true;return true;}
                public void Reset()=>throw new NotSupportedException();
                public void Dispose(){Disposals++;if(RestoreOnDispose)Row[0]=Restore;if(DisposeError!=null)throw DisposeError;}
            }
            public sealed class OwnerAsyncRows<T>:IAsyncEnumerable<T[]>,IAsyncEnumerator<T[]>{
                public T[] Row;public T Restore;public int Gets,Moves,Disposals;public CancellationToken Token;public Exception? DisposeError;
                public TaskCompletionSource<bool> MoveGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource<bool> DisposeGate=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource<bool> Disposing=new(TaskCreationOptions.RunContinuationsAsynchronously);
                public OwnerAsyncRows(T[] row,T restore){Row=row;Restore=restore;}
                public IAsyncEnumerator<T[]> GetAsyncEnumerator(CancellationToken cancellationToken=default){Gets++;Token=cancellationToken;return this;}
                public T[] Current=>Row;
                public ValueTask<bool> MoveNextAsync(){Moves++;return new ValueTask<bool>(MoveGate.Task);}
                public async ValueTask DisposeAsync(){Disposals++;Disposing.SetResult(true);await DisposeGate.Task;Row[0]=Restore;if(DisposeError!=null)throw DisposeError;}
            }
            public static class DefaultProbe{
                public static int OutCalls;
                static bool Transfer<T>(T value,out T result){Interlocked.Increment(ref OutCalls);result=value;return true;}
                static void TransferPair<T,U>(T value,U tag,out T row,out U other){Interlocked.Increment(ref OutCalls);row=value;other=tag;}
                public static (int tag,int value) OutMultiple(int[] value,int tag){TransferPair(value,tag,out var row,out var other);ref var alias=ref row[0];alias=default;return(other,alias);}
                public static int OutCall(int[] value){Transfer(value,out var row);ref var alias=ref row[0];alias=default;return alias;}
                public static int OutCondition(int[] value){if(Transfer(value,out var row)){row[0]=default;return row[0];}return default;}
                public static int OutTuple((int[] row,int tag) value){Transfer(value,out var pair);var(row,_)=pair;ref var alias=ref row[0];alias=default;return alias;}
                public static int OutDictionary(Dictionary<int,int[]> value,int key){if(value.TryGetValue(key,out var row)){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int OutDelegate(OutCopier<int[]> copy,int[] value){copy(value,out var row);ref var alias=ref row[0];alias=default;return alias;}
                public static Func<int> OutCapture(int[] value)=>()=>{Transfer(value,out var row);ref var alias=ref row[0];alias=default;return alias;};
                public static async Task<int> OutAwait(Task<int[]> value){Transfer(await value,out var row);ref var alias=ref row[0];alias=default;return alias;}
                public static async ValueTask<int> OutValueAwait(ValueTask<int[]> value){Transfer(await value,out var row);ref var alias=ref row[0];alias=default;return alias;}
                public static int PatternVar(int[] value){if(value is var row){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int PatternProperty(PatternCell<int[]> value){if(value is {Row:var row}){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int PatternTuple((int[] row,int flag) value){if(value is {row:var row}){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int PatternPositional(OwnerPair<int[],int> value){if(value is (var row,_)){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int PatternList(int[][] value){if(value is [var row,..]){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int PatternSlice(int[] value){if(value is [..var row]){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int PatternSwitch((int[] row,int flag) value){switch(value){case(var row,_)when row.Length>0:ref var alias=ref row[0];alias=default;return alias;default:return default;}}
                public static int PatternExpression(int[] value)=>value switch{var row=>row[0]=default};
                public static async Task<int> PatternAsync(Task<int[]> value){if(await value is var row){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static async ValueTask<int> PatternValueAsync(ValueTask<int[]> value){if(await value is var row){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int OwnerEnumerable(IEnumerable<int[]> value){foreach(var row in value){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int OwnerTuple((int[] row,int flag) value){var(row,_)=value;ref var alias=ref row[0];alias=default;return alias;}
                public static int OwnerCustom(OwnerPair<int[],int> value){var(row,_)=value;ref var alias=ref row[0];alias=default;return alias;}
                public static int OwnerForeachCustom(IEnumerable<OwnerPair<int[],int>> value){foreach(var(row,_)in value){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int OwnerMixed(int[] first,int[] second){var row=first;(row,var other)=(second,first);ref var alias=ref other[0];alias=default;return row[0];}
                public static int OwnerForeachTuple(IEnumerable<(int[] row,int flag)> value){foreach(var(row,_)in value){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int OwnerRef(Span<int[]> value){foreach(ref var row in value){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static async Task<int> OwnerAsync(IAsyncEnumerable<int[]> value,CancellationToken cancellationToken){await foreach(var row in value.WithCancellation(cancellationToken)){ref var alias=ref row[0];alias=default;return alias;}return default;}
                public static int IndirectSibling(IndirectPair<int,int> pair){ref var alias=ref pair.Left;alias=default;return pair.Right;}
                public static long IndirectWidening(long[] value,int fill){value[0]=fill;var copy=value[0];ref var alias=ref value[0];alias=default;return copy;}
                public static int? IndirectLift(int?[] value,int fill){value[0]=fill;ref var alias=ref value[0];alias=default;return alias;}
                public static int IndirectIndexCalls;
                static int IndirectIndex(){IndirectIndexCalls++;return 0;}
                static ref T IndirectBorrow<T>(ref T value)=>ref value;
                public static int IndirectArray(int[] value){ref var alias=ref value[IndirectIndex()];alias=default;return alias;}
                public static int IndirectMatrix(int[,] value){ref var alias=ref value[0,0];alias=default;return alias;}
                public static int IndirectJagged(int[][] value){ref var alias=ref value[0][0];alias=default;return alias;}
                public static int IndirectSpan(Span<int> value){ref var alias=ref value[0];alias=default;return alias;}
                public static int IndirectField(IndirectCell<int> cell){ref var alias=ref cell.Value;alias=default;return alias;}
                public static int IndirectProperty(IndirectCell<int> cell){ref var alias=ref cell.Slot;alias=default;return alias;}
                public static int IndirectIndexer(IndirectCell<int> cell){ref var alias=ref cell[0];alias=default;return alias;}
                public static int IndirectTuple((int left,int right) value){ref var alias=ref value.left;alias=default;return alias;}
                public static int IndirectReturn(int[] value){ref var alias=ref IndirectBorrow(ref value[0]);alias=default;return alias;}
                public static int IndirectCopy(int[] value){var copy=value[0];ref var alias=ref value[0];alias=default;return copy;}
                public static Func<int> IndirectCapture(int[] value){var read=()=>value[0];ref var alias=ref value[0];alias=default;return read;}
                public static int IndirectFill(int[] value,int fill){value[0]=fill;ref var alias=ref value[0];alias=default;return fill;}
                public static int IndirectRebind(int[] first,int[] second){ref var alias=ref first[0];alias=ref second[0];alias=default;return alias;}
                public static int Assigned(bool flag,int value){var result=value;if(flag)result=default;return result;}
                public static int Parameter(int value){value=default;return value;}
                public static int Reset(int value){var result=value;void Clear(){result=default;}Clear();return result;}
                public static int LambdaReset(int value){var result=value;Action clear=()=>{result=default;};clear();return result;}
                public static int ArrayStore(int[] values){values[0]=default;return values[0];}
                public static int ListStore(List<int> values){values[0]=default;return values[0];}
                public static int Copy(int value){var result=value;var copy=result;result=default;return copy;}
                public static Func<int> Captured(int value){var result=value;var read=()=>result;result=default;return read;}
                public static int Alias(int value){ref var result=ref value;result=default;return result;}
                public static int AliasLocal(int value){var result=value;ref var alias=ref result;alias=default;return result;}
                public static int AliasChain(int value){var result=value;ref var first=ref result;ref var second=ref first;first=ref second;second=default;return result;}
                public static int AliasCopy(int value){var result=value;var copy=result;ref var alias=ref result;alias=default;return copy;}
                public static Func<int> AliasCapture(int value){var result=value;var read=()=>result;ref var alias=ref result;alias=default;return read;}
                public static int AliasReset(int value){var result=value;void Clear(){ref var alias=ref result;alias=default;}Clear();return result;}
                public static int AliasRef(ref int value){value=default;return value;}
                public static int AliasOut(int value,out int output){output=value;output=default;return output;}
                public static (int left,int right) AliasRebind(int first,int second){var left=first;var right=second;ref var alias=ref left;alias=ref right;alias=default;return(left,right);}
                public static (int left,int right) AliasConditional(bool flag,int first,int second){var left=first;var right=second;ref var alias=ref(flag?ref left:ref right);alias=default;return(left,right);}
                public static (int left,int right) AliasTuple((int left,int right) value){var result=value;ref var alias=ref result;alias=default;return result;}
                public static async Task<int> AliasAsync(Task<int> value){var result=await value;ref var alias=ref result;alias=default;return result;}
                public static async Task<int> AssignedAsync(bool flag,Task<int> value){var result=await value;if(flag)result=default;return result;}
                public static int First(IEnumerable<int> values){using var e=values.GetEnumerator();return e.MoveNext()?e.Current:default;}
                public static int Choose(bool flag,int value)=>flag?value:default;
                public static int Branch(bool flag,int value){if(flag)return value;return default;}
                public static Func<int> Callback(bool flag,int value)=>()=>flag?value:default;
                public static (int left,int right) Tuple(bool flag,(int left,int right) value)=>flag?value:default;
                public static IEnumerable<int> Iterator(int value){yield return value;yield return default;}
                public static async IAsyncEnumerable<int> AsyncIterator(int value){await Task.Yield();yield return value;yield return default;}
                public static async Task<int> Async(bool flag,Task<int> value){if(flag)return await value;return default;}
                public static async ValueTask<int> ValueAsync(bool flag,ValueTask<int> value){if(flag)return await value;return default;}
                public static int Coalesce(int? value)=>value??default;
                public static int? Assign(int? value)=>value??=default;
                public static object Box(bool flag,int value)=>flag?value:default;
                public static int Typed(bool flag,int value,int tag)=>flag?value:default(int);
            }
            public static class OriginalDefaultRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    if(!Indirect())return false;
                    if(!await Owners(cancellationToken))return false;
                    if(!await Patterns(cancellationToken))return false;
                    if(!await Outputs(cancellationToken))return false;
                    foreach(bool flag in new[]{false,true}){
                        int expected=flag?7:0;
                        if(DefaultProbe.Assigned(!flag,7)!=expected||await DefaultProbe.AssignedAsync(!flag,Task.FromResult(7)).WaitAsync(cancellationToken)!=expected)return false;
                        if(DefaultProbe.Choose(flag,7)!=expected||DefaultProbe.Branch(flag,7)!=expected||DefaultProbe.Callback(flag,7)()!=expected
                            ||DefaultProbe.Tuple(flag,(7,8))!=(flag?(7,8):(0,0))||DefaultProbe.Box(flag,7) is not int boxed||boxed!=expected)return false;
                        if(await DefaultProbe.Async(flag,Task.FromResult(7)).WaitAsync(cancellationToken)!=expected
                            ||await DefaultProbe.ValueAsync(flag,new ValueTask<int>(7)).AsTask().WaitAsync(cancellationToken)!=expected)return false;
                    }
                    if(DefaultProbe.Copy(7)!=7||DefaultProbe.Parameter(7)!=0||DefaultProbe.Captured(7)()!=0||DefaultProbe.Alias(7)!=0)return false;
                    int slot=7;
                    if(DefaultProbe.AliasLocal(7)!=0||DefaultProbe.AliasChain(7)!=0||DefaultProbe.AliasCopy(7)!=7||DefaultProbe.AliasCapture(7)()!=0
                        ||DefaultProbe.AliasReset(7)!=0||DefaultProbe.AliasRef(ref slot)!=0||slot!=0||DefaultProbe.AliasOut(7,out var output)!=0||output!=0
                        ||DefaultProbe.AliasRebind(7,9)!=(7,0)||DefaultProbe.AliasConditional(true,7,9)!=(0,9)
                        ||DefaultProbe.AliasConditional(false,7,9)!=(7,0)||DefaultProbe.AliasTuple((7,9))!=(0,0))return false;
                    int[] stored={7};var list=new List<int>{7};
                    if(DefaultProbe.Reset(7)!=0||DefaultProbe.LambdaReset(7)!=0||DefaultProbe.ArrayStore(stored)!=0||stored[0]!=0||DefaultProbe.ListStore(list)!=0||list[0]!=0)return false;
                    if(DefaultProbe.First(Array.Empty<int>())!=0||DefaultProbe.First(new[]{7})!=7||!DefaultProbe.Iterator(7).SequenceEqual(new[]{7,0})||DefaultProbe.Coalesce((int?)null)!=0
                        ||DefaultProbe.Assign((int?)null) is not { } assigned||assigned!=0)return false;
                    var items=new List<int>();await foreach(var item in DefaultProbe.AsyncIterator(7).WithCancellation(cancellationToken))items.Add(item);
                    if(!items.SequenceEqual(new[]{7,0}))return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel"}){
                        var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var pending=DefaultProbe.Async(true,gate.Task);
                        var valuePending=DefaultProbe.ValueAsync(true,new ValueTask<int>(gate.Task)).AsTask();
                        var assignedPending=DefaultProbe.AssignedAsync(true,gate.Task);
                        var aliasPending=DefaultProbe.AliasAsync(gate.Task);
                        if(assignedPending.IsCompleted||aliasPending.IsCompleted)return false;
                        if(pending.IsCompleted||valuePending.IsCompleted||!DefaultProbe.Async(false,gate.Task).IsCompletedSuccessfully
                            ||!DefaultProbe.ValueAsync(false,new ValueTask<int>(gate.Task)).IsCompletedSuccessfully)return false;
                        var failure=new InvalidOperationException("default-await");
                        if(outcome=="Fault")gate.SetException(failure);else if(outcome=="Cancel")gate.SetCanceled(canceled.Token);else gate.SetResult(7);
                        foreach(var task in new[]{pending,valuePending}){
                            try{if(await task.WaitAsync(cancellationToken)!=7||outcome!="Value")return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                        try{if(await assignedPending.WaitAsync(cancellationToken)!=0||outcome!="Value")return false;}
                        catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                        catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        try{if(await aliasPending.WaitAsync(cancellationToken)!=0||outcome!="Value")return false;}
                        catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                        catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                    }
                    return true;
                }
                static bool Indirect(){
                    int[] values={7};int[,] matrix={{7}};int[][] jagged={new[]{7}};
                    long[] widened={7};int?[] optional={7};
                    if(DefaultProbe.IndirectWidening(widened,int.MinValue)!=(long)int.MinValue||widened[0]!=0
                        ||DefaultProbe.IndirectLift(optional,7)!=null||optional[0]!=null)return false;
                    DefaultProbe.IndirectIndexCalls=0;
                    if(DefaultProbe.IndirectArray(values)!=0||values[0]!=0||DefaultProbe.IndirectIndexCalls!=1
                        ||DefaultProbe.IndirectMatrix(matrix)!=0||matrix[0,0]!=0||DefaultProbe.IndirectJagged(jagged)!=0||jagged[0][0]!=0)return false;
                    values[0]=7;if(DefaultProbe.IndirectSpan(values.AsSpan())!=0||values[0]!=0)return false;
                    values[0]=7;if(DefaultProbe.IndirectReturn(values)!=0||values[0]!=0)return false;
                    values[0]=7;if(DefaultProbe.IndirectCopy(values)!=7||values[0]!=0)return false;
                    values[0]=7;if(DefaultProbe.IndirectCapture(values)()!=0||values[0]!=0)return false;
                    values[0]=7;if(DefaultProbe.IndirectFill(values,9)!=9||values[0]!=0)return false;
                    var field=new IndirectCell<int>(7);var property=new IndirectCell<int>(7);var indexer=new IndirectCell<int>(7);
                    var pair=new IndirectPair<int,int>(7,9);if(DefaultProbe.IndirectSibling(pair)!=9||pair.Left!=0||pair.Right!=9)return false;
                    if(DefaultProbe.IndirectField(field)!=0||field.Value!=0||field.Reads!=0
                        ||DefaultProbe.IndirectProperty(property)!=0||property.Value!=0||property.Reads!=1
                        ||DefaultProbe.IndirectIndexer(indexer)!=0||indexer.Value!=0||indexer.Reads!=1||DefaultProbe.IndirectTuple((7,9))!=0)return false;
                    int[] first={7},second={9};return DefaultProbe.IndirectRebind(first,second)==0&&first[0]==7&&second[0]==0;
                }
                static async Task<bool> Outputs(CancellationToken cancellationToken){
                    DefaultProbe.OutCalls=0;int[] first={7},conditional={7},tuple={7},stored={7},delegated={7},captured={7};
                    var dictionary=new Dictionary<int,int[]>{{1,stored}};
                    if(DefaultProbe.OutCall(first)!=0||first[0]!=0||DefaultProbe.OutCondition(conditional)!=0||conditional[0]!=0
                        ||DefaultProbe.OutTuple((tuple,9))!=0||tuple[0]!=0||DefaultProbe.OutDictionary(dictionary,1)!=0||stored[0]!=0
                        ||DefaultProbe.OutDictionary(dictionary,2)!=0||dictionary.Count!=1||DefaultProbe.OutCalls!=3)return false;
                    var callback=DefaultProbe.OutCapture(captured);if(captured[0]!=7||DefaultProbe.OutCalls!=3||callback()!=0||captured[0]!=0||DefaultProbe.OutCalls!=4)return false;
                    int calls=0;OutCopier<int[]> copy=(int[] input,out int[] output)=>{calls++;output=input;};
                    if(DefaultProbe.OutDelegate(copy,delegated)!=0||delegated[0]!=0||calls!=1)return false;
                    var multiple=new[]{7};if(DefaultProbe.OutMultiple(multiple,9)!=(9,0)||multiple[0]!=0||DefaultProbe.OutCalls!=5)return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel"}){
                        DefaultProbe.OutCalls=0;var row=new[]{7};var gate=new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var task=DefaultProbe.OutAwait(gate.Task);var valueTask=DefaultProbe.OutValueAwait(new ValueTask<int[]>(gate.Task)).AsTask();
                        if(task.IsCompleted||valueTask.IsCompleted||row[0]!=7||DefaultProbe.OutCalls!=0)return false;
                        var failure=new InvalidOperationException("out-await");
                        if(outcome=="Value")gate.SetResult(row);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                        foreach(var pending in new[]{task,valueTask}){
                            try{if(await pending.WaitAsync(cancellationToken)!=0||outcome!="Value")return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                        if(row[0]!=(outcome=="Value"?0:7)||DefaultProbe.OutCalls!=(outcome=="Value"?2:0))return false;
                    }
                    return true;
                }
                static async Task<bool> Patterns(CancellationToken cancellationToken){
                    var cell=new PatternCell<int[]>(new[]{7});var pair=new OwnerPair<int[],int>(new[]{7},9);
                    int[] variable={7},tuple={7},listed={7},slice={7},switched={7},expression={7};
                    if(DefaultProbe.PatternVar(variable)!=0||variable[0]!=0||DefaultProbe.PatternProperty(cell)!=0||cell.Value[0]!=0||cell.Reads!=1
                        ||DefaultProbe.PatternProperty((PatternCell<int[]>)null!)!=0||DefaultProbe.PatternTuple((tuple,9))!=0||tuple[0]!=0
                        ||DefaultProbe.PatternPositional(pair)!=0||pair.Row[0]!=0||pair.Calls!=1||DefaultProbe.PatternList(new[]{listed})!=0||listed[0]!=0
                        ||DefaultProbe.PatternList(Array.Empty<int[]>())!=0||DefaultProbe.PatternSlice(slice)!=0||slice[0]!=7
                        ||DefaultProbe.PatternSwitch((switched,9))!=0||switched[0]!=0||DefaultProbe.PatternSwitch((Array.Empty<int>(),9))!=0
                        ||DefaultProbe.PatternExpression(expression)!=0||expression[0]!=0)return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel"}){
                        var row=new[]{7};var gate=new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var task=DefaultProbe.PatternAsync(gate.Task);var valueTask=DefaultProbe.PatternValueAsync(new ValueTask<int[]>(gate.Task)).AsTask();
                        if(task.IsCompleted||valueTask.IsCompleted||row[0]!=7)return false;
                        var failure=new InvalidOperationException("pattern-await");
                        if(outcome=="Value")gate.SetResult(row);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                        foreach(var pending in new[]{task,valueTask}){
                            try{if(await pending.WaitAsync(cancellationToken)!=0||outcome!="Value")return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                        if(row[0]!=(outcome=="Value"?0:7))return false;
                    }
                    return true;
                }
                static async Task<bool> Owners(CancellationToken cancellationToken){
                    var rows=new OwnerRows<int>(new[]{7},99){RestoreOnDispose=true};
                    if(DefaultProbe.OwnerEnumerable(rows)!=0||rows.Row[0]!=99||rows.Gets!=1||rows.Moves!=1||rows.Disposals!=1)return false;
                    var empty=new OwnerRows<int>(new[]{7},99){HasRow=false};if(DefaultProbe.OwnerEnumerable(empty)!=0||empty.Disposals!=1)return false;
                    var failure=new InvalidOperationException("owner-move");var failed=new OwnerRows<int>(new[]{7},99){MoveError=failure};
                    try{_=DefaultProbe.OwnerEnumerable(failed);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,failure)){}
                    if(failed.Row[0]!=7||failed.Disposals!=1)return false;
                    var disposal=new OwnerRows<int>(new[]{7},99){DisposeError=failure};
                    try{_=DefaultProbe.OwnerEnumerable(disposal);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,failure)){}
                    if(disposal.Row[0]!=0||disposal.Disposals!=1)return false;
                    int[] tupleRow={7},customRow={7},foreachRow={7},spanRow={7};var pair=new OwnerPair<int[],int>(customRow,1);
                    int[] first={7},second={9};var loopPair=new OwnerPair<int[],int>(new[]{7},1);
                    if(DefaultProbe.OwnerMixed(first,second)!=9||first[0]!=0||second[0]!=9
                        ||DefaultProbe.OwnerForeachCustom(new[]{loopPair})!=0||loopPair.Row[0]!=0||loopPair.Calls!=1)return false;
                    if(DefaultProbe.OwnerTuple((tupleRow,1))!=0||tupleRow[0]!=0||DefaultProbe.OwnerCustom(pair)!=0||customRow[0]!=0||pair.Calls!=1
                        ||DefaultProbe.OwnerForeachTuple(new[]{(foreachRow,1)})!=0||foreachRow[0]!=0||DefaultProbe.OwnerRef(new[]{spanRow}.AsSpan())!=0||spanRow[0]!=0)return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Empty","Fault","Cancel","DisposeFault","DisposeCancel"}){
                        var source=new OwnerAsyncRows<int>(new[]{7},99);var pending=DefaultProbe.OwnerAsync(source,cancellationToken);
                        if(outcome=="DisposeFault")source.DisposeError=failure;
                        if(pending.IsCompleted)return false;
                        if(outcome=="Fault")source.MoveGate.SetException(failure);else if(outcome=="Cancel")source.MoveGate.SetCanceled(canceled.Token);else source.MoveGate.SetResult(outcome is "Value" or "DisposeFault" or "DisposeCancel");
                        await source.Disposing.Task.WaitAsync(cancellationToken);
                        if(pending.IsCompleted||source.Gets!=1||source.Moves!=1||source.Disposals!=1||source.Token!=cancellationToken||source.Row[0]!=(outcome is "Value" or "DisposeFault" or "DisposeCancel"?0:7))return false;
                        if(outcome=="DisposeCancel")source.DisposeGate.SetCanceled(canceled.Token);else source.DisposeGate.SetResult(true);
                        try{if(await pending.WaitAsync(cancellationToken)!=0||outcome is "Fault" or "Cancel" or "DisposeFault" or "DisposeCancel")return false;}
                        catch(InvalidOperationException error)when(outcome is "Fault" or "DisposeFault"&&ReferenceEquals(error,failure)){}
                        catch(OperationCanceledException error)when(outcome is "Cancel" or "DisposeCancel"&&error.CancellationToken==canceled.Token){}
                        if(source.Row[0]!=(outcome=="DisposeCancel"?0:99))return false;
                    }
                    return true;
                }
            }
            """;
        const string newSource = """
            #nullable enable
            using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
            public struct ConstructorProbe{public static int Calls;public int Value;public ConstructorProbe(){Calls++;Value=99;}}
            public static class NewDefaultRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    string text=new string('x',2);
                    if(!Indirect(text))return false;
                    if(!await Owners(text,cancellationToken))return false;
                    if(!await Patterns(text,cancellationToken))return false;
                    if(!await Outputs(text,cancellationToken))return false;
                    string other=new string('y',2);string? slot=text;decimal? number=23m;
                    if(DefaultProbe.Alias(text)!=null||DefaultProbe.AliasLocal(text)!=null||DefaultProbe.AliasChain(text)!=null||DefaultProbe.AliasReset(text)!=null
                        ||!ReferenceEquals(DefaultProbe.AliasCopy(text),text)||DefaultProbe.AliasCopy(text).Length!=text.Length||DefaultProbe.AliasCapture(text)()!=null||DefaultProbe.AliasRef(ref slot)!=null||slot!=null
                        ||DefaultProbe.AliasRef(ref number)!=null||number!=null||DefaultProbe.AliasOut(text,out var output)!=null||output!=null
                        ||DefaultProbe.AliasRebind(text,other)!=(text,null)||DefaultProbe.AliasConditional(true,text,other)!=(null,other)
                        ||DefaultProbe.AliasConditional(false,text,other)!=(text,null)||DefaultProbe.AliasTuple((text,23m))!=(null,0m)
                        ||DefaultProbe.AliasTuple((23m,text))!=(0m,null)||await DefaultProbe.AliasAsync(Task.FromResult(text)).WaitAsync(cancellationToken)!=null)return false;
                    string?[] stored={text};var list=new List<string?>{text};
                    if(DefaultProbe.Reset(text)!=null||DefaultProbe.LambdaReset(text)!=null||DefaultProbe.ArrayStore(stored)!=null||stored[0]!=null||DefaultProbe.ListStore(list)!=null||list[0]!=null)return false;
                    if(DefaultProbe.Assigned(true,text)!=null||!ReferenceEquals(DefaultProbe.Assigned(false,text),text)
                        ||DefaultProbe.Parameter(text)!=null||!ReferenceEquals(DefaultProbe.Copy(text),text)||DefaultProbe.Captured(text)()!=null
                        ||DefaultProbe.Assigned(true,(decimal?)23m)!=null||DefaultProbe.Assigned(true,23m)!=0m||DefaultProbe.Alias(23m)!=0m
                        ||await DefaultProbe.AssignedAsync(true,Task.FromResult(text)).WaitAsync(cancellationToken)!=null)return false;
                    if(DefaultProbe.First(Array.Empty<string>())!=null||!ReferenceEquals(DefaultProbe.First(new[]{text}),text)||!ReferenceEquals(DefaultProbe.Choose(true,text),text)||DefaultProbe.Choose(false,text)!=null
                        ||DefaultProbe.Branch(false,text)!=null||DefaultProbe.Callback(false,text)()!=null
                        ||!DefaultProbe.Iterator(text).SequenceEqual(new string?[]{text,null}))return false;
                    if(DefaultProbe.Choose(false,23m)!=0m||DefaultProbe.Choose(true,23m)!=23m
                        ||DefaultProbe.Choose(false,(decimal?)23m)!=null||DefaultProbe.Choose(true,(decimal?)23m)!=23m)return false;
                    var original=new ConstructorProbe();ConstructorProbe.Calls=0;
                    if(DefaultProbe.Choose(false,original).Value!=0||DefaultProbe.Assigned(true,original).Value!=0||DefaultProbe.Alias(original).Value!=0||DefaultProbe.AliasLocal(original).Value!=0||DefaultProbe.IndirectArray(new[]{original}).Value!=0||ConstructorProbe.Calls!=0)return false;
                    if(DefaultProbe.Tuple(false,(text,23m))!=(null,0m)||DefaultProbe.Tuple(true,(text,23m))!=(text,23m))return false;
                    if(DefaultProbe.Coalesce((decimal?)null)!=0m||DefaultProbe.Assign((decimal?)null) is not { } zero||zero!=0m
                        ||DefaultProbe.Box(false,23m) is not decimal boxed||boxed!=0m)return false;
                    if(await DefaultProbe.Async(false,Task.FromResult(text)).WaitAsync(cancellationToken)!=null
                        ||await DefaultProbe.Async(true,Task.FromResult(text)).WaitAsync(cancellationToken)!=text
                        ||await DefaultProbe.ValueAsync(false,new ValueTask<decimal>(23m)).AsTask().WaitAsync(cancellationToken)!=0m
                        ||await DefaultProbe.Async(false,Task.FromResult((decimal?)23m)).WaitAsync(cancellationToken)!=null)return false;
                    var items=new List<string?>();await foreach(var item in DefaultProbe.AsyncIterator(text).WithCancellation(cancellationToken))items.Add(item);
                    return items.SequenceEqual(new string?[]{text,null});
                }
                static bool Indirect(string text){
                    string?[] values={text};string?[,] matrix={{text}};string?[][] jagged={new[]{(string?)text}};
                    DefaultProbe.IndirectIndexCalls=0;
                    if(DefaultProbe.IndirectArray(values)!=null||values[0]!=null||DefaultProbe.IndirectIndexCalls!=1
                        ||DefaultProbe.IndirectMatrix(matrix)!=null||matrix[0,0]!=null||DefaultProbe.IndirectJagged(jagged)!=null||jagged[0][0]!=null)return false;
                    values[0]=text;if(DefaultProbe.IndirectSpan(values.AsSpan())!=null||values[0]!=null)return false;
                    values[0]=text;if(DefaultProbe.IndirectReturn(values)!=null||values[0]!=null)return false;
                    values[0]=text;if(!ReferenceEquals(DefaultProbe.IndirectCopy(values),text)||values[0]!=null)return false;
                    values[0]=text;if(DefaultProbe.IndirectCapture(values)()!=null||values[0]!=null)return false;
                    if(DefaultProbe.IndirectFill<string>(values,text).Length!=text.Length||values[0]!=null)return false;
                    var field=new IndirectCell<string?>(text);var property=new IndirectCell<string?>(text);var indexer=new IndirectCell<string?>(text);
                    var pair=new IndirectPair<string?,string>(text,text);
                    if(DefaultProbe.IndirectSibling<string,string>(pair).Length!=text.Length||pair.Left!=null||!ReferenceEquals(pair.Right,text))return false;
                    if(DefaultProbe.IndirectField(field)!=null||field.Value!=null||field.Reads!=0
                        ||DefaultProbe.IndirectProperty(property)!=null||property.Value!=null||property.Reads!=1
                        ||DefaultProbe.IndirectIndexer(indexer)!=null||indexer.Value!=null||indexer.Reads!=1
                        ||DefaultProbe.IndirectTuple(((string?)text,23m))!=null)return false;
                    decimal?[] numbers={23m};if(DefaultProbe.IndirectArray(numbers)!=null||numbers[0]!=null)return false;
                    numbers[0]=23m;if(DefaultProbe.IndirectLift(numbers,23m)!=null||numbers[0]!=null)return false;
                    var numberCell=new IndirectCell<decimal?>(23m);if(DefaultProbe.IndirectProperty(numberCell)!=null||numberCell.Value!=null)return false;
                    string?[] first={text},second={text};return DefaultProbe.IndirectRebind(first,second)==null&&ReferenceEquals(first[0],text)&&second[0]==null;
                }
                static async Task<bool> Outputs(string text,CancellationToken cancellationToken){
                    DefaultProbe.OutCalls=0;string?[] first={text},conditional={text},tuple={text},stored={text},delegated={text},captured={text};
                    var dictionary=new Dictionary<string,string?[]>{{text,stored}};
                    if(DefaultProbe.OutCall(first)!=null||first[0]!=null||DefaultProbe.OutCondition(conditional)!=null||conditional[0]!=null
                        ||DefaultProbe.OutTuple((tuple,23m))!=null||tuple[0]!=null||DefaultProbe.OutDictionary(dictionary,text)!=null||stored[0]!=null
                        ||DefaultProbe.OutDictionary(dictionary,"missing")!=null||dictionary.Count!=1||DefaultProbe.OutCalls!=3)return false;
                    var callback=DefaultProbe.OutCapture(captured);if(!ReferenceEquals(captured[0],text)||DefaultProbe.OutCalls!=3||callback()!=null||captured[0]!=null||DefaultProbe.OutCalls!=4)return false;
                    int calls=0;OutCopier<string?[]> copy=(string?[] input,out string?[] output)=>{calls++;output=input;};
                    if(DefaultProbe.OutDelegate(copy,delegated)!=null||delegated[0]!=null||calls!=1)return false;
                    var row=new[]{(string?)text};var gate=new TaskCompletionSource<string?[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var task=DefaultProbe.OutAwait(gate.Task);var valueTask=DefaultProbe.OutValueAwait(new ValueTask<string?[]>(gate.Task)).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||!ReferenceEquals(row[0],text)||DefaultProbe.OutCalls!=4)return false;
                    gate.SetResult(row);if(await task.WaitAsync(cancellationToken)!=null||await valueTask.WaitAsync(cancellationToken)!=null||row[0]!=null||DefaultProbe.OutCalls!=6)return false;
                    var multiple=new[]{(string?)text};var tag=new Uri("https://example.invalid/out");var result=DefaultProbe.OutMultiple(multiple,tag);
                    if(!ReferenceEquals(result.tag,tag)||result.value!=null||multiple[0]!=null||DefaultProbe.OutCalls!=7)return false;
                    decimal?[] number={23m};return DefaultProbe.OutCall(number)==null&&number[0]==null;
                }
                static async Task<bool> Patterns(string text,CancellationToken cancellationToken){
                    var cell=new PatternCell<string?[]>(new[]{(string?)text});var pair=new OwnerPair<string?[],Uri>(new[]{(string?)text},new Uri("https://example.invalid/pattern"));
                    string?[] variable={text},tuple={text},listed={text},slice={text},switched={text},expression={text};
                    if(DefaultProbe.PatternVar(variable)!=null||variable[0]!=null||DefaultProbe.PatternProperty(cell)!=null||cell.Value[0]!=null||cell.Reads!=1
                        ||DefaultProbe.PatternProperty<string>(null!)!=null||DefaultProbe.PatternTuple((tuple,23m))!=null||tuple[0]!=null
                        ||DefaultProbe.PatternPositional(pair)!=null||pair.Row[0]!=null||pair.Calls!=1||DefaultProbe.PatternList(new[]{listed})!=null||listed[0]!=null
                        ||DefaultProbe.PatternList(Array.Empty<string?[]>())!=null||DefaultProbe.PatternSlice(slice)!=null||!ReferenceEquals(slice[0],text)
                        ||DefaultProbe.PatternSwitch((switched,23m))!=null||switched[0]!=null||DefaultProbe.PatternSwitch((Array.Empty<string?>(),23m))!=null
                        ||DefaultProbe.PatternExpression(expression)!=null||expression[0]!=null)return false;
                    var row=new[]{(string?)text};var gate=new TaskCompletionSource<string?[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var task=DefaultProbe.PatternAsync(gate.Task);var valueTask=DefaultProbe.PatternValueAsync(new ValueTask<string?[]>(gate.Task)).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||!ReferenceEquals(row[0],text))return false;
                    gate.SetResult(row);if(await task.WaitAsync(cancellationToken)!=null||await valueTask.WaitAsync(cancellationToken)!=null||row[0]!=null)return false;
                    decimal?[] number={23m};if(DefaultProbe.PatternVar(number)!=null||number[0]!=null)return false;
                    number[0]=23m;return DefaultProbe.PatternSlice(number)==null&&number[0]==23m;
                }
                static async Task<bool> Owners(string text,CancellationToken cancellationToken){
                    string other=new string('z',2);var rows=new OwnerRows<string?>(new[]{(string?)text},other){RestoreOnDispose=true};
                    if(DefaultProbe.OwnerEnumerable(rows)!=null||!ReferenceEquals(rows.Row[0],other)||rows.Gets!=1||rows.Moves!=1||rows.Disposals!=1)return false;
                    string?[] tupleRow={text},customRow={text},foreachRow={text},spanRow={text};var pair=new OwnerPair<string?[],Uri>(customRow,new Uri("https://example.invalid/owner"));
                    string?[] first={text},second={other};var loopPair=new OwnerPair<string?[],string>(new[]{(string?)text},other);
                    if(!ReferenceEquals(DefaultProbe.OwnerMixed(first,second),other)||first[0]!=null||!ReferenceEquals(second[0],other)
                        ||DefaultProbe.OwnerForeachCustom(new[]{loopPair})!=null||loopPair.Row[0]!=null||loopPair.Calls!=1)return false;
                    if(DefaultProbe.OwnerTuple((tupleRow,other))!=null||tupleRow[0]!=null||DefaultProbe.OwnerCustom(pair)!=null||customRow[0]!=null||pair.Calls!=1
                        ||DefaultProbe.OwnerForeachTuple(new[]{(foreachRow,other)})!=null||foreachRow[0]!=null||DefaultProbe.OwnerRef(new[]{spanRow}.AsSpan())!=null||spanRow[0]!=null)return false;
                    var source=new OwnerAsyncRows<string?>(new[]{(string?)text},other);var pending=DefaultProbe.OwnerAsync(source,cancellationToken);
                    if(pending.IsCompleted)return false;source.MoveGate.SetResult(true);await source.Disposing.Task.WaitAsync(cancellationToken);
                    if(pending.IsCompleted||source.Row[0]!=null||source.Disposals!=1||source.Token!=cancellationToken)return false;
                    source.DisposeGate.SetResult(true);if(await pending.WaitAsync(cancellationToken)!=null||!ReferenceEquals(source.Row[0],other))return false;
                    decimal?[] numberRow={23m};var numbers=new OwnerRows<decimal?>(numberRow,99m);return DefaultProbe.OwnerEnumerable(numbers)==null&&numberRow[0]==null&&numbers.Disposals==1;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Defaults", "Defaults", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Defaults.cs", source);
        var originalCompilation = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var symbol = changed.GetTypeByMetadataName("DefaultProbe")!;
        var choose = symbol.GetMembers("Choose").OfType<IMethodSymbol>().Single();
        if (choose.Parameters[1].Type is not ITypeParameterSymbol { HasValueTypeConstraint: false, HasReferenceTypeConstraint: false }
            || choose.ReturnNullableAnnotation != NullableAnnotation.Annotated
            || symbol.GetMembers("Coalesce").OfType<IMethodSymbol>().Single().TypeParameters is not [{ HasValueTypeConstraint: true }]
            || symbol.GetMembers("Typed").OfType<IMethodSymbol>().Single().Parameters[1].Type.SpecialType != SpecialType.System_Int32)
        { throw new InvalidOperationException("Default Fix All lost annotations, struct constraints or typed endpoints: " + string.Join("; ", symbol.GetMembers().OfType<IMethodSymbol>().Select(method => { ArgumentNullException.ThrowIfNull(method); return method.ToDisplayString(); }))); }
        Program.Log.Info("PASS DefaultFixAllAnnotationsConstraintsAndTypedEndpoints");
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("DefaultValues", isCollectible: true);
        try
        {
            async Task Execute(Compilation compilation, string runner)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentException.ThrowIfNullOrEmpty(runner);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Default emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                var assembly = loadContext.LoadFromStream(image);
                var pending = (Task<bool>)assembly.GetType(runner)!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
                if (!await pending.WaitAsync(cancellationToken).ConfigureAwait(false))
                { throw new InvalidOperationException("Default runtime effects: " + runner); }
            }
            await Execute(originalCompilation.WithAssemblyName("BeforeDefaults"), "OriginalDefaultRunner").ConfigureAwait(false);
            await Execute(changed.WithAssemblyName("AfterDefaults"), "OriginalDefaultRunner").ConfigureAwait(false);
            Program.Log.Info("PASS DefaultOriginalValuesIteratorsSuspensionFaultAndCancellation");
            await Execute(changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken)).WithAssemblyName("NewDefaults"), "NewDefaultRunner").ConfigureAwait(false);
            Program.Log.Info("PASS DefaultNewReferenceNullableStructTupleIteratorAndAsyncDomains");
            var invalid = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("class Bad{void Check(){_=DefaultProbe.Coalesce<string>(null);_=DefaultProbe.Assign<string>(null);_=DefaultProbe.Box(false,\"x\");}}", cancellationToken: cancellationToken));
            if (invalid.GetDiagnostics(cancellationToken).Count(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Id == "CS0453"; }) != 3)
            { throw new InvalidOperationException("Default conversion admitted references into struct-only coalescing or boxing."); }
            Program.Log.Info("PASS DefaultRejectsInvalidCoalesceAndBoxingDomains");
        }
        finally { loadContext.Unload(); }
        return 4;
    }
}
