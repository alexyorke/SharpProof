using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using SharpProof.HmInference.CodeFixes;

namespace SharpProof.HmInference;

internal static class QueryRuntimeChecks
{
    public static async Task<int> Run(CancellationToken cancellationToken)
    {
        const string source = """
            #nullable enable
            using System;using System.Linq;using System.Linq.Expressions;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
            [return:System.Diagnostics.CodeAnalysis.MaybeNull][return:System.Diagnostics.CodeAnalysis.NotNullIfNotNull("guard")]public delegate T QueryGuardedAction<T>(T value,object? guard);
            [return:System.Diagnostics.CodeAnalysis.MaybeNull]public delegate T QueryMaybeAction<T>(T value,bool keep);
            public static class QueryTrace{
                public static string Value="";public static Exception? Failure;public static Action? OnDispose;public static int RefCalls,IndexCalls,CaptureCalls,ContractCalls;public static Exception ContractFailure=new InvalidOperationException("contract-null");
                public static int Index(){IndexCalls++;return 0;}
                public static bool Accept<T>(T value){Value+="where;";return true;}
                public static T Touch<T>(T value){Value+="touch;";if(Failure!=null)throw Failure;return value;}
                public static IEnumerable<T> Source<T>(IEnumerable<T> values){Value+="open;";try{foreach(var value in values)yield return value;}finally{Value+="dispose;";OnDispose?.Invoke();}}
            }
            public sealed class QueryBox<T>{
                public IEnumerable<T> Values;public QueryBox(IEnumerable<T> values){Values=values;}
                public QueryBox<U> Select<U>(Func<T,U> selector){QueryTrace.Value+="select;";return new QueryBox<U>(Values.Select(selector));}
            }
            public delegate T QueryInputAction<T>(MemberCell<T> cell,[System.Diagnostics.CodeAnalysis.AllowNull]T value,T fallback);
            public delegate bool QueryOutcomeAction<T>(MemberCell<T> cell,[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)]out T value);
            public static class RequiredTrace{public static int Calls;}
            public delegate T RequiredAction<T>([System.Diagnostics.CodeAnalysis.DisallowNull]T value);
            public static partial class PartialInputProbe{public static partial int RequiredPartial(int value);public static partial int RequiredPartial(int value)=>QueryProbe.RequireInput(value);}
            public static class ReadonlyTrace{public static int Indices,Returns;}
            public sealed class ReadonlyCell<T>{
                public T Value;public T? Observed;public int Calls,ReadonlyCalls;public ReadonlyCell(T value){Value=value;Observed=value;}
                public void Observe([System.Diagnostics.CodeAnalysis.AllowNull]in T value){Interlocked.Increment(ref Calls);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;Observed=value;}
                public void ObserveReadonly([System.Diagnostics.CodeAnalysis.AllowNull]ref readonly T value){Interlocked.Increment(ref Calls);Interlocked.Increment(ref ReadonlyCalls);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;Observed=value;}
                [return:System.Diagnostics.CodeAnalysis.MaybeNull][return:System.Diagnostics.CodeAnalysis.NotNullIfNotNull("value")]public T Echo([System.Diagnostics.CodeAnalysis.AllowNull]in T value){Interlocked.Increment(ref Calls);Observed=value;return value!;}
            }
            public sealed class PriorityCell<T>{
                public T? Data;public int Reads;public PriorityCell(T? value){Data=value;}
                [System.Diagnostics.CodeAnalysis.MaybeNull]public T Maybe{[return:System.Diagnostics.CodeAnalysis.NotNull]get{Interlocked.Increment(ref Reads);return Data!;}}
                [System.Diagnostics.CodeAnalysis.NotNull]public T Required{[return:System.Diagnostics.CodeAnalysis.MaybeNull]get{Interlocked.Increment(ref Reads);if(Data is null)throw QueryTrace.ContractFailure;return Data;}}
            }
            [return:System.Diagnostics.CodeAnalysis.MaybeNull][return:System.Diagnostics.CodeAnalysis.NotNull]public delegate T PriorityAction<T>(T value,bool keep);
            public static class RefTrace{public static int Calls,Indices;}
            public delegate void RefSlotAction<T>([System.Diagnostics.CodeAnalysis.AllowNull][System.Diagnostics.CodeAnalysis.MaybeNull]ref T value);
            public static class SlotTrace{public static int Calls,Indices;}
            public sealed class StateBox<T>{public StateCell<T> Nested;public StateBox(StateCell<T> value){Nested=value;}}
            public sealed class StateCell<T>{
                public T? Field;public int Checks,Gets;public StateCell(T? value){Field=value;}
                public T? Property{get{Interlocked.Increment(ref Gets);return Field;}}
                [System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(Field),nameof(Property))]public void Ensure(){Interlocked.Increment(ref Checks);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;if(Field is null||Property is null)throw QueryTrace.ContractFailure;}
                [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true,nameof(Field),nameof(Property))]public bool Ready{get{Interlocked.Increment(ref Checks);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;return Field is not null&&Property is not null;}}
            }
            public sealed class MemberCell<T>{
                public T Stored;public T? OptionalStored;[System.Diagnostics.CodeAnalysis.MaybeNull]public T Field;public bool Present;public int Reads,Indices,Writes,Refs;
                public bool TryRead([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)]out T value){Interlocked.Increment(ref Reads);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;value=Present?Stored:default!;return Present;}
                public void ReadOptional([System.Diagnostics.CodeAnalysis.MaybeNull]out T value){Interlocked.Increment(ref Reads);value=Present?Stored:default;}
                public T StoreInput([System.Diagnostics.CodeAnalysis.AllowNull]T value,T fallback){Interlocked.Increment(ref Writes);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;Stored=value is null?fallback:value;return Stored;}
                public MemberCell(T value,bool present=true){Stored=value;OptionalStored=value;Field=value;Present=present;}
                [System.Diagnostics.CodeAnalysis.MaybeNull]public T Value{get{Interlocked.Increment(ref Reads);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;return Present?Stored:default;}set{Interlocked.Increment(ref Writes);Stored=value;Present=true;}}
                [System.Diagnostics.CodeAnalysis.MaybeNull]public T this[int index]{get{Interlocked.Increment(ref Indices);return Value;}set{Interlocked.Increment(ref Writes);Stored=value;Present=true;}}
                [System.Diagnostics.CodeAnalysis.MaybeNull]public ref T? RefSlot{get{Interlocked.Increment(ref Refs);return ref OptionalStored;}}
                [System.Diagnostics.CodeAnalysis.NotNull]public T? Required{get{Interlocked.Increment(ref Reads);if(Stored is null)throw QueryTrace.ContractFailure;return Stored;}}
            }
            public static class QueryProbe{
                static ref T Borrow<T>(ref T value){QueryTrace.RefCalls++;return ref value;}
                static ref T? BorrowNullable<T>(ref T? value){QueryTrace.RefCalls++;return ref value;}
                static ref T ArrayBorrow<T>(T[] value){QueryTrace.RefCalls++;return ref value[QueryTrace.Index()];}
                public static T RequireInput<T>([System.Diagnostics.CodeAnalysis.DisallowNull]T value){Interlocked.Increment(ref RequiredTrace.Calls);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;if(value is null)throw QueryTrace.ContractFailure;return value;}
                static void RequireReadonly<T>([System.Diagnostics.CodeAnalysis.DisallowNull]in T value){Interlocked.Increment(ref RequiredTrace.Calls);if(value is null)throw QueryTrace.ContractFailure;}
                public static int RequiredValue(int value)=>RequireInput(value);
                public static int RequiredNamed(int value)=>RequireInput(value:value);
                public static int RequiredDelegate(RequiredAction<int> require,int value)=>require(value);
                public static int RequiredReadonly(in int value){RequireReadonly(in value);return value;}
                public static async Task<int> RequiredTask(int value,Task gate){await gate;return RequireInput(value);}
                public static async ValueTask<int> RequiredValueTask(int value,Task gate){await gate;return RequireInput(value);}
                static int ReadonlyIndex(){Interlocked.Increment(ref ReadonlyTrace.Indices);return 0;}
                public static string ReadonlyValue(ReadonlyCell<string> cell,string? value){cell.Observe(in value);return cell.Value;}
                public static string ReadonlyRefValue(ReadonlyCell<string> cell,string? value){cell.ObserveReadonly(in value);return cell.Value;}
                public static string ReadonlyArray(ReadonlyCell<string> cell,string?[] values){cell.Observe(in values[ReadonlyIndex()]);return cell.Value;}
                public static string ReadonlyAlias(ReadonlyCell<string> cell,string?[] values){ref readonly var slot=ref values[ReadonlyIndex()];cell.Observe(in slot);return cell.Value;}
                public static int ReadonlyScalar(ReadonlyCell<int> cell,int value){cell.Observe(in value);return cell.Value;}
                public static int[]? ReadonlyEchoArray(ReadonlyCell<int[]> cell,int[]? value)=>cell.Echo(in value);
                public static async Task<string> ReadonlyTask(ReadonlyCell<string> cell,Task<string?> input){var value=await input;cell.Observe(in value);return cell.Value;}
                public static async ValueTask<string> ReadonlyValueTask(ReadonlyCell<string> cell,ValueTask<string?> input){var value=await input;cell.Observe(in value);return cell.Value;}
                [return:System.Diagnostics.CodeAnalysis.MaybeNull][return:System.Diagnostics.CodeAnalysis.NotNull]public static T MixedReturnValue<T>(T value,bool keep){Interlocked.Increment(ref ReadonlyTrace.Returns);return keep?value!:default!;}
                public static int MixedReturn(int value,bool keep)=>MixedReturnValue(value,keep);
                public static int MixedDelegate(PriorityAction<int> action,int value,bool keep)=>action(value,keep);
                public static int PriorityMaybe(PriorityCell<int> cell)=>cell.Maybe;
                public static int PriorityRequired(PriorityCell<int> cell)=>cell.Required;
                public static void ClearRefSlot<T>([System.Diagnostics.CodeAnalysis.AllowNull][System.Diagnostics.CodeAnalysis.MaybeNull]ref T value){Interlocked.Increment(ref RefTrace.Calls);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;value=default;}
                static void StoreRefSlot<T>(T value,[System.Diagnostics.CodeAnalysis.AllowNull][System.Diagnostics.CodeAnalysis.MaybeNull]ref T target){Interlocked.Increment(ref RefTrace.Calls);target=value;}
                static bool TryRefSlot<T>([System.Diagnostics.CodeAnalysis.AllowNull][System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)][System.Diagnostics.CodeAnalysis.NotNullWhen(true)]ref T target,T value,bool keep){Interlocked.Increment(ref RefTrace.Calls);target=keep?(value is null?throw QueryTrace.ContractFailure:value):default!;return keep;}
                static int RefSlotIndex(){Interlocked.Increment(ref RefTrace.Indices);return 0;}
                public static (int before,int after) RefSlotCopy(int[] values){var copy=values[0];ClearRefSlot(ref values[RefSlotIndex()]);return(copy,values[0]);}
                public static int RefSlotArray(int[] values){ClearRefSlot(ref values[RefSlotIndex()]);return values[0];}
                public static int RefSlotAlias(int[] values){ref var slot=ref values[RefSlotIndex()];ClearRefSlot(ref slot);return values[0];}
                public static int RefSlotInput(int value,int[] target){StoreRefSlot(value,ref target[RefSlotIndex()]);return target[0];}
                public static int RefSlotTuple(ref (int value,bool tag) pair){ClearRefSlot(ref pair.value);return pair.Item1;}
                public static int RefSlotConditional((int value,bool tag) pair,int value,bool keep){if(TryRefSlot(ref pair.value,value,keep))return pair.Item1;return value;}
                public static int RefSlotDelegate(RefSlotAction<int> clear,int[] values){clear(ref values[RefSlotIndex()]);return values[0];}
                public static async Task<int> RefSlotTask(Task<int[]> input){var values=await input;ClearRefSlot(ref values[RefSlotIndex()]);return values[0];}
                public static async ValueTask<int> RefSlotValueTask(ValueTask<int[]> input){var values=await input;ClearRefSlot(ref values[RefSlotIndex()]);return values[0];}
                public static void WritePossibleSlot<T>([System.Diagnostics.CodeAnalysis.MaybeNull]out T value){Interlocked.Increment(ref SlotTrace.Calls);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;value=default;}
                static void WriteKnownSlot<T>([System.Diagnostics.CodeAnalysis.NotNull]out T? value,T fallback){Interlocked.Increment(ref SlotTrace.Calls);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;if(fallback is null)throw QueryTrace.ContractFailure;value=fallback;}
                static int SlotIndex(){Interlocked.Increment(ref SlotTrace.Indices);return 0;}
                public static int TupleSlotKnown((int value,bool tag) pair,int fallback){WriteKnownSlot(out pair.Item1,fallback);return pair.value;}
                public static (int copy,int stored) TupleSlotCopy((int value,bool tag) pair){var copy=pair.value;WritePossibleSlot(out pair.value);return(copy,pair.Item1);}
                public static int TupleSlotRef(ref (int value,bool tag) pair){WritePossibleSlot(out pair.value);return pair.Item1;}
                public static int ArraySlotWrite(int[] values){WritePossibleSlot(out values[SlotIndex()]);return values[0];}
                public static int ArraySlotAlias(int[] values){ref var slot=ref values[SlotIndex()];WritePossibleSlot(out slot);return values[0];}
                public static int DictionarySlotWrite(Dictionary<int,int> values,int key,int[] output){values.TryGetValue(key,out output[SlotIndex()]);return output[0];}
                public static async Task<int> TupleSlotAsync(Task<(int value,bool tag)> input){var pair=await input;WritePossibleSlot(out pair.Item1);return pair.value;}
                public static async ValueTask<int> TupleSlotValueAsync(ValueTask<(int value,bool tag)> input){var pair=await input;WritePossibleSlot(out pair.value);return pair.Item1;}
                public static async ValueTask<int> TupleSlotKnownAsync(ValueTask<(int value,bool tag)> input,int fallback){var pair=await input;WriteKnownSlot(out pair.value,fallback);return pair.Item1;}
                public static int StateField(StateCell<int> cell){cell.Ensure();return cell.Field;}
                public static int StateProperty(StateCell<int> cell){cell.Ensure();return cell.Property;}
                public static int StateConditional(StateCell<int> cell,int fallback)=>cell.Ready?cell.Field:fallback;
                public static int StateFailure(StateCell<int> cell,int fallback){if(!cell.Ready)return cell.Field;return fallback;}
                public static int StateNested(StateBox<int> box,StateCell<int> other){other.Ensure();return box.Nested.Field;}
                public static int StateOther(StateCell<int> first,StateCell<int> other){first.Ensure();return other.Field;}
                public static int StateInvalidated(StateCell<int> cell){cell.Ensure();cell.Field=default;return cell.Field;}
                static void FillState<T>([System.Diagnostics.CodeAnalysis.NotNull]out T? value,T fallback){if(fallback is null)throw QueryTrace.ContractFailure;value=fallback;}
                static void ClearState<T>([System.Diagnostics.CodeAnalysis.MaybeNull]out T value)=>value=default;
                public static int StateOutFill(StateCell<int> cell,int fallback){FillState(out cell.Field,fallback);return cell.Field;}
                public static int StateOutClear(StateCell<int> cell){ClearState(out cell.Field);return cell.Field;}
                public static IEnumerable<int> StateQuery(IEnumerable<StateCell<int>> cells,int fallback)=>from cell in cells select cell.Ready?cell.Field:fallback;
                public static async Task<int> StateAsync(Task<StateCell<int>> input){var cell=await input;cell.Ensure();return cell.Field;}
                public static async ValueTask<int> StateValueAsync(ValueTask<StateCell<int>> input){var cell=await input;cell.Ensure();return cell.Property;}
                public static bool TryOutcome<T>(MemberCell<T> cell,[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)]out T value)=>cell.TryRead(out value);
                public static int OutcomeRead(MemberCell<int> cell){cell.TryRead(out var value);return value;}
                public static int OutcomeSuccess(MemberCell<int> cell,int fallback){if(cell.TryRead(out var value))return value;return fallback;}
                public static int OutcomeConditional(MemberCell<int> cell,int fallback)=>cell.TryRead(out var value)?value:fallback;
                public static int OutcomeDelegate(QueryOutcomeAction<int> action,MemberCell<int> cell){action(cell,out var value);return value;}
                public static int OutcomeOptional(MemberCell<int> cell){cell.ReadOptional(out var value);return value;}
                public static async Task<int> OutcomeAsync(Task<MemberCell<int>> input){var cell=await input;cell.TryRead(out var value);return value;}
                public static async ValueTask<int> OutcomeValueAsync(ValueTask<MemberCell<int>> input){var cell=await input;cell.TryRead(out var value);return value;}
                static void EnsureInput<T>([System.Diagnostics.CodeAnalysis.NotNull]T? value){if(value is null)throw QueryTrace.ContractFailure;}
                static bool HasInput<T>([System.Diagnostics.CodeAnalysis.NotNullWhen(true)]T? value)=>value is not null;
                public static string CheckedInput(string? value){EnsureInput(value);return value;}
                public static string ConditionalInput(string? value,string fallback)=>HasInput(value)?value:fallback;
                public static T StoreInput<T>(MemberCell<T> cell,[System.Diagnostics.CodeAnalysis.AllowNull]T value,T fallback){Interlocked.Increment(ref cell.Writes);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;cell.Stored=value is null?fallback:value;return cell.Stored;}
                public static string InputMethod(MemberCell<string> cell,string? value,string fallback)=>cell.StoreInput(value,fallback);
                public static string InputDelegate(QueryInputAction<string> put,MemberCell<string> cell,string? value,string fallback)=>put(cell,value,fallback);
                public static int? InputNullableValue(MemberCell<int?> cell,int? value,int? fallback)=>StoreInput(cell,value,fallback);
                public static IEnumerable<string> InputQuery(MemberCell<string> cell,IEnumerable<string?> values,string fallback)=>from value in values select cell.StoreInput(value,fallback);
                public static async Task<string> InputAsync(MemberCell<string> cell,Task<string?> value,string fallback)=>cell.StoreInput(await value,fallback);
                public static async ValueTask<string> InputValueAsync(MemberCell<string> cell,ValueTask<string?> value,string fallback)=>cell.StoreInput(await value,fallback);
                public static int MemberProperty(MemberCell<int> cell)=>cell.Value;
                public static int MemberField(MemberCell<int> cell)=>cell.Field;
                public static int MemberIndexer(MemberCell<int> cell,int index)=>cell[index];
                public static (int copy,int stored) MemberStore(MemberCell<int> cell,int fill){var copy=fill;cell.Value=fill;return(copy,cell.Value);}
                public static int MemberFieldStore(MemberCell<int> cell,int fill){cell.Field=fill;return cell.Field;}
                public static int MemberRefStore(MemberCell<int> cell,int fill){ref var slot=ref cell.RefSlot;slot=fill;return cell.RefSlot;}
                public static string MemberRequired(MemberCell<string?> cell)=>cell.Required;
                public static IEnumerable<int> MemberQuery(IEnumerable<MemberCell<int>> cells)=>from cell in cells select cell.Value;
                public static async Task<int> MemberAsync(Task<MemberCell<int>> cell)=>(await cell).Value;
                public static async ValueTask<int> MemberValueAsync(ValueTask<MemberCell<int>> cell)=>(await cell)[0];
                [return:System.Diagnostics.CodeAnalysis.MaybeNull][return:System.Diagnostics.CodeAnalysis.NotNullIfNotNull("guard")]public static T ConditionalContract<T>(T value,object? guard){Interlocked.Increment(ref QueryTrace.ContractCalls);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;if(guard is null)return default;if(value is null)throw QueryTrace.ContractFailure;return value;}
                public static int ConditionalKnown(int value)=>ConditionalContract(QueryTrace.Touch(value),"ready");
                public static int ConditionalUnknown(int value,object? guard)=>ConditionalContract(value,guard);
                public static int ConditionalDelegate(QueryGuardedAction<int> action,int value)=>action(value,"ready");
                public static async Task<int> ConditionalAsync(Task<int> value)=>ConditionalContract(await value,"ready");
                public static async ValueTask<int> ConditionalValueAsync(ValueTask<int> value,object? guard)=>ConditionalContract(await value,guard);
                [return:System.Diagnostics.CodeAnalysis.MaybeNull]public static T ContractMaybe<T>(T value,bool keep){Interlocked.Increment(ref QueryTrace.ContractCalls);if(QueryTrace.Failure!=null)throw QueryTrace.Failure;return keep?value:default;}
                [return:System.Diagnostics.CodeAnalysis.NotNull]static T? ContractRequired<T>(T? value){Interlocked.Increment(ref QueryTrace.ContractCalls);if(value is null)throw QueryTrace.ContractFailure;return value;}
                public static int ContractValue(int value,bool keep)=>ContractMaybe(value,keep);
                public static int[]? ContractArray(int[] value,bool keep)=>ContractMaybe(value,keep);
                public static (int value,bool sibling) ContractTuple(int value,bool sibling,bool keep)=>(ContractMaybe(value,keep),sibling);
                public static int ContractDelegate(QueryMaybeAction<int> action,int value,bool keep)=>action(value,keep);
                public static string ContractRequire(string? value)=>ContractRequired(value);
                public static async Task<int> ContractAsync(Task<int> value,bool keep)=>ContractMaybe(await value,keep);
                public static async ValueTask<int> ContractValueAsync(ValueTask<int> value,bool keep)=>ContractMaybe(await value,keep);
                static ref T BranchBorrow<T>(ref T value){QueryTrace.Value+="left;";QueryTrace.RefCalls++;return ref value;}
                static ref T BranchArrayBorrow<T>(T[] value){QueryTrace.Value+="right;";QueryTrace.RefCalls++;return ref value[QueryTrace.Index()];}
                static T BranchKeep<T>(T value){QueryTrace.Value+="keep;";if(QueryTrace.Failure!=null)throw QueryTrace.Failure;return value;}
                public static IEnumerable<(int left,int right)> ConditionalMixed(IEnumerable<(int[] first,int[] second)> values,bool clear)=>from pair in values select (BranchBorrow(ref pair.first[QueryTrace.Index()]),BranchArrayBorrow(pair.second))=clear?(default,default):(BranchKeep(pair.first[0]),BranchKeep(pair.second[0]));
                public static IEnumerable<(int left,int right)> SwitchMixed(IEnumerable<(int[] first,int[] second)> values,bool clear)=>from pair in values select (BranchBorrow(ref pair.first[QueryTrace.Index()]),BranchArrayBorrow(pair.second))=clear switch{true=>(default,default),false=>(BranchKeep(pair.first[0]),BranchKeep(pair.second[0]))};
                public static IEnumerable<(int left,(int mid,bool right) rest)> NestedConditionalMixed(IEnumerable<(int[] first,int[] second,bool[] third)> values,bool clear)=>from pair in values select (BranchBorrow(ref pair.first[QueryTrace.Index()]),(BranchArrayBorrow(pair.second),BranchArrayBorrow(pair.third)))=clear?(default,(default,default)):(BranchKeep(pair.first[0]),(BranchKeep(pair.second[0]),BranchKeep(pair.third[0])));
                public static async Task<IEnumerable<(int left,int right)>> ConditionalMixedAsync(Task<IEnumerable<(int[] first,int[] second)>> values,bool clear)=>from pair in await values select (BranchBorrow(ref pair.first[QueryTrace.Index()]),BranchArrayBorrow(pair.second))=clear?(default,default):(BranchKeep(pair.first[0]),BranchKeep(pair.second[0]));
                public static async ValueTask<IEnumerable<(int left,int right)>> ConditionalMixedValueAsync(ValueTask<IEnumerable<(int[] first,int[] second)>> values,bool clear)=>from pair in await values select (BranchBorrow(ref pair.first[QueryTrace.Index()]),BranchArrayBorrow(pair.second))=clear?(default,default):(BranchKeep(pair.first[0]),BranchKeep(pair.second[0]));
                public static (int left,int right) ConditionalArrayStores(int[] first,int[] second,bool clear){(first[0],second[0])=clear?(default,default):(first[0],second[0]);return(first[0],second[0]);}
                public static Func<int> TupleCapture(int value,bool other){var read=()=>value;(value,other)=(default,default);return read;}
                public static (Func<int> read,int copy) TupleLocalCopy(int value,bool other){var slot=value;var copy=slot;var read=()=>slot;(slot,other)=(default,default);return(read,copy);}
                public static (Func<int> read,Action clear) TupleDeferred(int value,bool other){var read=()=>value;Action clear=()=>{(value,other)=(default,default);};return(read,clear);}
                public static async Task<Func<int>> TupleTaskCapture(Task<int> value,bool other){var slot=await value;Interlocked.Increment(ref QueryTrace.CaptureCalls);var read=()=>slot;(slot,other)=(default,default);return read;}
                public static async ValueTask<Func<int>> TupleValueCapture(ValueTask<int> value,bool other){var slot=await value;Interlocked.Increment(ref QueryTrace.CaptureCalls);var read=()=>slot;(slot,other)=(default,default);return read;}
                public static int Grouped(int value){(value)=default;return value;}
                public static async Task<(int copy,int stored)> GroupedAsync(Task<int> value){var result=await value;var copy=result;((result))=default;return(copy,result);}
                public static async Task<(int left,int right)> TupleAsync(Task<int[]> first,int[] second){var result=await first;(Borrow(ref result[QueryTrace.Index()]),ArrayBorrow(second))=(default,default);return(result[0],second[0]);}
                public static async ValueTask<IEnumerable<(int left,int right)>> MixedAsync(ValueTask<IEnumerable<(int[] first,int[] second)>> values)=>from pair in await values select (Borrow(ref pair.first[QueryTrace.Index()]),ArrayBorrow(pair.second))=(default,default);
                public static IEnumerable<int> ArrayRefStore(IEnumerable<int[]> values)=>from row in values select ArrayBorrow(row)=default;
                public static void RefLocalQuery(IEnumerable<int[]> values){void Clear(){foreach(var item in(from row in values select Borrow(ref row[QueryTrace.Index()])=default)){}}Clear();}
                public static int RefLocal(int[] values){void Clear(){Borrow(ref values[QueryTrace.Index()])=default;}Clear();return values[0];}
                public static Func<int> RefCaptured(int[] values)=>()=>Borrow(ref values[QueryTrace.Index()])=default;
                #pragma warning disable CS8601
                public static IEnumerable<int> RefStoreSuppressed(IEnumerable<int[]> values)=>from row in values select Borrow(ref row[QueryTrace.Index()])=default;
                #pragma warning restore CS8601
                public static IEnumerable<int> RefStore(IEnumerable<int[]> values)=>from row in values select Borrow(ref row[QueryTrace.Index()])=default;
                public static IEnumerable<int> NullableRefStore(IEnumerable<int[]> values)=>from row in values select BorrowNullable(ref row[QueryTrace.Index()])=default;
                public static IEnumerable<(int copy,int stored)> RefCopy(IEnumerable<int[]> values)=>from row in values select(row[0],Borrow(ref row[QueryTrace.Index()])=default);
                public static IEnumerable<string> RefSibling(IEnumerable<(int[] row,string tag)> values)=>from pair in values select(Borrow(ref pair.row[QueryTrace.Index()])=default,pair.tag).Item2;
                public static int RefBlock(int[] values){values[0]=default;Borrow(ref values[QueryTrace.Index()])=default;return values[0];}
                public static int RefParameter(ref int value){Borrow(ref value)=default;return value;}
                public static async Task<IEnumerable<int>> RefAsync(Task<IEnumerable<int[]>> values)=>from row in await values select Borrow(ref row[QueryTrace.Index()])=default;
                public static async ValueTask<IEnumerable<int>> NullableRefAsync(ValueTask<IEnumerable<int[]>> values)=>from row in await values select BorrowNullable(ref row[QueryTrace.Index()])=default;
                public static IEnumerable<int> DefaultJoin(IEnumerable<int> values,bool flag)=>from x in values select flag?x:default;
                public static IEnumerable<string?> NullJoin(IEnumerable<string> values,bool flag)=>from x in values select flag?x:null;
                public static IEnumerable<int> Coalesce(IEnumerable<int?> values,int fallback)=>from x in values select x??fallback;
                public static IEnumerable<int> Lazy(IEnumerable<int> values)=>from x in values where QueryTrace.Accept(x) let pair=(QueryTrace.Touch(x),x) select pair.Item1;
                public static IEnumerable<(int left,string right)> Many(IEnumerable<int> values,IEnumerable<string> other)=>from x in values from y in other select(x,y);
                public static IEnumerable<int> Order(IEnumerable<int> values)=>from x in values orderby x descending,x select x;
                public static IEnumerable<int> Continue(IEnumerable<int> values)=>from x in values select(x,x) into pair select pair.Item2;
                public static IEnumerable<(string left,bool right)> Join(IEnumerable<(int key,string value)> left,IEnumerable<(int key,bool value)> right)=>from l in left join r in right on l.key equals r.key select(l.value,r.value);
                public static IEnumerable<IEnumerable<int>> GroupJoin(IEnumerable<int> left,IEnumerable<int> right)=>from l in left join r in right on l equals r into grouped select grouped;
                public static IEnumerable<IGrouping<int,string>> Group(IEnumerable<(int key,string value)> values)=>from x in values group x.value by x.key;
                public static IEnumerable<int> Typed(IEnumerable<object> values)=>from int x in values select x;
                public static IEnumerable<int> Numeric(IEnumerable<int> values)=>from x in values where x>0 select x;
                public static IQueryable<int> Quoted(IQueryable<int> values)=>from x in values where QueryTrace.Accept(x) select x;
                public static IQueryable<int> QuotedDefault(IQueryable<int> values,bool flag)=>from x in values select flag?x:default;
                public static QueryBox<int> Custom(QueryBox<int> values)=>from x in values select QueryTrace.Touch(x);
                public static async Task<IEnumerable<int>> Async(Task<IEnumerable<int>> values)=>from x in await values select QueryTrace.Touch(x);
                public static async ValueTask<IEnumerable<int>> ValueAsync(ValueTask<IEnumerable<int>> values)=>from x in await values select QueryTrace.Touch(x);
            }
            public static class OriginalQueryRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    if(!await Stores(cancellationToken))return false;
                    if(!QueryProbe.DefaultJoin(new[]{7},true).SequenceEqual(new[]{7})||!QueryProbe.DefaultJoin(new[]{7},false).SequenceEqual(new[]{0})
                        ||!QueryProbe.NullJoin(new[]{"value"},true).SequenceEqual(new[]{"value"})||QueryProbe.NullJoin(new[]{"value"},false).Single()!=null
                        ||!QueryProbe.Coalesce(new int?[]{null,7},9).SequenceEqual(new[]{9,7}))return false;
                    QueryTrace.Value="";var lazy=QueryProbe.Lazy(QueryTrace.Source(new[]{7,9}));
                    if(QueryTrace.Value!=""||lazy.Take(1).Single()!=7||QueryTrace.Value!="open;where;touch;dispose;")return false;
                    if(!QueryProbe.Many(new[]{1,2},new[]{"a","b"}).SequenceEqual(new[]{(1,"a"),(1,"b"),(2,"a"),(2,"b")})
                        ||!QueryProbe.Order(new[]{1,3,2}).SequenceEqual(new[]{3,2,1})||!QueryProbe.Continue(new[]{7,9}).SequenceEqual(new[]{7,9}))return false;
                    var joined=QueryProbe.Join(new[]{(1,"left")},new[]{(1,true),(2,false)}).ToArray();
                    if(joined.Length!=1||joined[0]!=("left",true))return false;
                    var groups=QueryProbe.GroupJoin(new[]{1,2},new[]{1,1,3}).ToArray();if(groups.Length!=2||!groups[0].SequenceEqual(new[]{1,1})||groups[1].Any())return false;
                    var grouped=QueryProbe.Group(new[]{(1,"a"),(1,"b"),(2,"c")}).ToArray();if(grouped.Length!=2||grouped[0].Key!=1||!grouped[0].SequenceEqual(new[]{"a","b"}))return false;
                    using(var items=QueryProbe.Typed(new object[]{7,"bad"}).GetEnumerator()){
                        if(!items.MoveNext()||items.Current!=7)return false;try{items.MoveNext();return false;}catch(InvalidCastException){}
                    }
                    if(!QueryProbe.Numeric(new[]{-1,0,1}).SequenceEqual(new[]{1}))return false;
                    QueryTrace.Value="";var quoted=QueryProbe.Quoted(new[]{7,9}.AsQueryable());
                    if(QueryTrace.Value!=""||quoted.Expression is not MethodCallExpression call||call.Method.DeclaringType!=typeof(Queryable)
                        ||call.Method.GetGenericArguments()[0]!=typeof(int)||!quoted.SequenceEqual(new[]{7,9})||QueryTrace.Value!="where;where;")return false;
                    QueryTrace.Value="";var custom=QueryProbe.Custom(new QueryBox<int>(new[]{7,9}));
                    var defaults=QueryProbe.QuotedDefault(new[]{7}.AsQueryable(),false);
                    var selector=(LambdaExpression)((UnaryExpression)((MethodCallExpression)defaults.Expression).Arguments[1]).Operand;
                    if(((ConditionalExpression)selector.Body).IfFalse is not ConstantExpression constant||constant.Type!=typeof(int)||!Equals(constant.Value,0)||defaults.Single()!=0)return false;
                    if(QueryTrace.Value!="select;"||!custom.Values.SequenceEqual(new[]{7,9})||QueryTrace.Value!="select;touch;touch;")return false;
                    var failure=new InvalidOperationException("query-selector");QueryTrace.Value="";QueryTrace.Failure=failure;var failed=QueryProbe.Lazy(new[]{7});
                    try{if(QueryTrace.Value!="")return false;_=failed.ToArray();return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,failure)){}
                    finally{QueryTrace.Failure=null;}
                    if(QueryTrace.Value!="where;touch;")return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel"}){
                        QueryTrace.Value="";var gate=new TaskCompletionSource<IEnumerable<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var task=QueryProbe.Async(gate.Task);var valueTask=QueryProbe.ValueAsync(new ValueTask<IEnumerable<int>>(gate.Task)).AsTask();
                        if(task.IsCompleted||valueTask.IsCompleted||QueryTrace.Value!="")return false;
                        if(outcome=="Value")gate.SetResult(new[]{7,9});else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                        foreach(var pending in new[]{task,valueTask}){
                            try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||QueryTrace.Value!=""||!result.SequenceEqual(new[]{7,9})||QueryTrace.Value!="touch;touch;")return false;QueryTrace.Value="";}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                    }
                    return true;
                }
                static async Task<bool> TupleCaptures(CancellationToken cancellationToken){
                    if(QueryProbe.TupleCapture(7,true)()!=0)return false;
                    var copied=QueryProbe.TupleLocalCopy(7,true);if(copied.copy!=7||copied.read()!=0)return false;
                    var deferred=QueryProbe.TupleDeferred(7,true);if(deferred.read()!=7)return false;deferred.clear();if(deferred.read()!=0)return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();var failure=new InvalidOperationException("tuple-capture");
                    foreach(var outcome in new[]{"Value","Fault","Cancel"}){
                        QueryTrace.Value="";QueryTrace.CaptureCalls=0;var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var task=QueryProbe.TupleTaskCapture(gate.Task,true);var valueTask=QueryProbe.TupleValueCapture(new ValueTask<int>(gate.Task),true).AsTask();
                        if(task.IsCompleted||valueTask.IsCompleted||QueryTrace.CaptureCalls!=0)return false;
                        if(outcome=="Value")gate.SetResult(7);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                        foreach(var pending in new[]{task,valueTask}){
                            try{var read=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||read()!=0)return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                        if(QueryTrace.CaptureCalls!=(outcome=="Value"?2:0))return false;
                    }
                    return true;
                }
                static async Task<bool> BranchStores(CancellationToken cancellationToken){
                    foreach(var clear in new[]{true,false}){
                        foreach(var switched in new[]{true,false}){
                            int[] first={7},second={9};QueryTrace.Value="";QueryTrace.RefCalls=QueryTrace.IndexCalls=0;
                            var source=QueryTrace.Source(new[]{(first,second)});var query=switched?QueryProbe.SwitchMixed(source,clear):QueryProbe.ConditionalMixed(source,clear);
                            if(QueryTrace.Value!=""||QueryTrace.RefCalls!=0||first[0]!=7||second[0]!=9)return false;
                            var result=query.Take(1).Single();if(result!=(clear?0:7,clear?0:9)||first[0]!=result.left||second[0]!=result.right||QueryTrace.RefCalls!=2||QueryTrace.IndexCalls!=2
                                ||QueryTrace.Value!=(clear?"open;left;right;dispose;":"open;left;right;keep;keep;dispose;"))return false;
                        }
                        int[] left={7},right={9};var stored=QueryProbe.ConditionalArrayStores(left,right,clear);if(stored!=(clear?0:7,clear?0:9)||left[0]!=stored.left||right[0]!=stored.right)return false;
                    }
                    int[] failedLeft={7},failedRight={9};var failure=new InvalidOperationException("branch-tuple-rhs");QueryTrace.Failure=failure;QueryTrace.Value="";
                    try{_=QueryProbe.ConditionalMixed(QueryTrace.Source(new[]{(failedLeft,failedRight)}),false).ToArray();return false;}
                    catch(InvalidOperationException error)when(ReferenceEquals(error,failure)){}
                    finally{QueryTrace.Failure=null;}
                    if(failedLeft[0]!=7||failedRight[0]!=9||QueryTrace.Value!="open;left;right;keep;dispose;")return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel"}){
                        int[] first={7},second={9};QueryTrace.Value="";QueryTrace.RefCalls=QueryTrace.IndexCalls=0;
                        var gate=new TaskCompletionSource<IEnumerable<(int[],int[])>>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var task=QueryProbe.ConditionalMixedAsync(gate.Task,true);var valueTask=QueryProbe.ConditionalMixedValueAsync(new ValueTask<IEnumerable<(int[],int[])>>(gate.Task),true).AsTask();
                        if(task.IsCompleted||valueTask.IsCompleted||QueryTrace.Value!=""||QueryTrace.RefCalls!=0)return false;
                        if(outcome=="Value")gate.SetResult(new[]{(first,second)});else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                        var executions=0;foreach(var pending in new[]{task,valueTask}){
                            try{var query=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||QueryTrace.RefCalls!=executions*2)return false;
                                if(query.Single()!=(0,0)||first[0]!=0||second[0]!=0||QueryTrace.RefCalls!=++executions*2||QueryTrace.IndexCalls!=executions*2)return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                        if(outcome!="Value"&&(first[0]!=7||second[0]!=9||QueryTrace.RefCalls!=0))return false;
                    }
                    return true;
                }
                static async Task<bool> ReturnContracts(CancellationToken cancellationToken){
                    QueryMaybeAction<int> action=QueryProbe.ContractMaybe;int[] array={7};
                    foreach(var keep in new[]{true,false}){
                        if(QueryProbe.ContractValue(7,keep)!=(keep?7:0)||QueryProbe.ContractDelegate(action,7,keep)!=(keep?7:0)||QueryProbe.ContractTuple(7,true,keep)!=(keep?7:0,true)
                            ||!ReferenceEquals(QueryProbe.ContractArray(array,keep),keep?array:null))return false;
                    }
                    if(QueryProbe.ContractRequire("value")!="value")return false;string? absent=null;try{_=QueryProbe.ContractRequire(absent);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    using var canceled=new CancellationTokenSource();canceled.Cancel();var failure=new InvalidOperationException("contract-call");
                    foreach(var outcome in new[]{"Value","Fault","Cancel","HelperFault"}){
                        QueryTrace.ContractCalls=0;QueryTrace.Failure=outcome=="HelperFault"?failure:null;
                        try{
                            var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);var task=QueryProbe.ContractAsync(gate.Task,false);var valueTask=QueryProbe.ContractValueAsync(new ValueTask<int>(gate.Task),false).AsTask();
                            if(task.IsCompleted||valueTask.IsCompleted||QueryTrace.ContractCalls!=0)return false;
                            if(outcome is "Value" or "HelperFault")gate.SetResult(7);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            foreach(var pending in new[]{task,valueTask}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||result!=0)return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "HelperFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            if(QueryTrace.ContractCalls!=(outcome is "Value" or "HelperFault"?2:0))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> ConditionalContracts(CancellationToken cancellationToken){
                    QueryTrace.Value="";QueryTrace.ContractCalls=0;QueryGuardedAction<int> action=QueryProbe.ConditionalContract;
                    if(QueryProbe.ConditionalKnown(7)!=7||QueryTrace.Value!="touch;"||QueryTrace.ContractCalls!=1||QueryProbe.ConditionalDelegate(action,7)!=7
                        ||QueryProbe.ConditionalUnknown(7,null)!=0||QueryProbe.ConditionalUnknown(7,"ready")!=7)return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();var failure=new InvalidOperationException("conditional-contract");
                    foreach(var outcome in new[]{"Value","Fault","Cancel","HelperFault"}){
                        QueryTrace.ContractCalls=0;QueryTrace.Failure=outcome=="HelperFault"?failure:null;
                        try{
                            var gate=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);var task=QueryProbe.ConditionalAsync(gate.Task);
                            var optional=QueryProbe.ConditionalValueAsync(new ValueTask<int>(gate.Task),null).AsTask();
                            if(task.IsCompleted||optional.IsCompleted||QueryTrace.ContractCalls!=0)return false;
                            if(outcome is "Value" or "HelperFault")gate.SetResult(7);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            var index=0;foreach(var pending in new[]{task,optional}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||result!=(index++==0?7:0))return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "HelperFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            if(QueryTrace.ContractCalls!=(outcome is "Value" or "HelperFault"?2:0))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> RequiredContracts(CancellationToken cancellationToken){
                    RequiredTrace.Calls=0;var value=17;if(QueryProbe.RequiredValue(7)!=7||QueryProbe.RequiredNamed(11)!=11||QueryProbe.RequiredDelegate(QueryProbe.RequireInput,13)!=13||QueryProbe.RequiredReadonly(in value)!=17||value!=17||PartialInputProbe.RequiredPartial(19)!=19||RequiredTrace.Calls!=5)return false;
                    var failure=new InvalidOperationException("required input");using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel","HelperFault"}){
                        RequiredTrace.Calls=0;var gate=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.Failure=outcome=="HelperFault"?failure:null;
                        try{var task=QueryProbe.RequiredTask(7,gate.Task);var valueTask=QueryProbe.RequiredValueTask(7,gate.Task).AsTask();if(task.IsCompleted||valueTask.IsCompleted||RequiredTrace.Calls!=0)return false;
                            if(outcome is "Value" or "HelperFault")gate.SetResult(true);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            foreach(var pending in new[]{task,valueTask}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||result!=7)return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "HelperFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            if(RequiredTrace.Calls!=(outcome is "Value" or "HelperFault"?2:0))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> ReadonlyContracts(CancellationToken cancellationToken){
                    if(!await RequiredContracts(cancellationToken))return false;
                    ReadonlyTrace.Indices=ReadonlyTrace.Returns=0;var text=new string('r',3);var cell=new ReadonlyCell<string>(text);string? missing=null;string?[] values={null};
                    if(!ReferenceEquals(QueryProbe.ReadonlyValue(cell,missing),text)||cell.Observed!=null||!ReferenceEquals(QueryProbe.ReadonlyArray(cell,values),text)||values[0]!=null||ReadonlyTrace.Indices!=1)return false;
                    values[0]=text;if(!ReferenceEquals(QueryProbe.ReadonlyAlias(cell,values),text)||!ReferenceEquals(values[0],text)||!ReferenceEquals(cell.Observed,text)||ReadonlyTrace.Indices!=2||!ReferenceEquals(QueryProbe.ReadonlyRefValue(cell,missing),text)||cell.Observed!=null||cell.Calls!=4||cell.ReadonlyCalls!=1)return false;
                    var numeric=new ReadonlyCell<int>(7);if(QueryProbe.ReadonlyScalar(numeric,3)!=7||numeric.Value!=7||numeric.Observed!=3||numeric.Calls!=1)return false;
                    int[] original={7};var arrays=new ReadonlyCell<int[]>(original);int[]? absent=null;if(QueryProbe.ReadonlyEchoArray(arrays,absent)!=null||!ReferenceEquals(QueryProbe.ReadonlyEchoArray(arrays,original),original)||!ReferenceEquals(arrays.Value,original))return false;
                    if(QueryProbe.MixedReturn(7,false)!=0||QueryProbe.MixedDelegate(QueryProbe.MixedReturnValue,7,false)!=0||ReadonlyTrace.Returns!=2)return false;
                    var priority=new PriorityCell<int>(7);if(QueryProbe.PriorityMaybe(priority)!=7||QueryProbe.PriorityRequired(priority)!=7||priority.Reads!=2)return false;
                    var failure=new InvalidOperationException("readonly input");using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel","HelperFault"}){
                        var delayed=new ReadonlyCell<string>(text);var gate=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.Failure=outcome=="HelperFault"?failure:null;
                        try{var task=QueryProbe.ReadonlyTask(delayed,gate.Task);var valueTask=QueryProbe.ReadonlyValueTask(delayed,new ValueTask<string?>(gate.Task)).AsTask();
                            if(task.IsCompleted||valueTask.IsCompleted||delayed.Calls!=0)return false;
                            if(outcome is "Value" or "HelperFault")gate.SetResult(null);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            foreach(var pending in new[]{task,valueTask}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||!ReferenceEquals(result,text))return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "HelperFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            if(delayed.Calls!=(outcome is "Value" or "HelperFault"?2:0)||(outcome=="Value"?delayed.Observed!=null:!ReferenceEquals(delayed.Observed,text)))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> RefSlotContracts(CancellationToken cancellationToken){
                    if(!await ReadonlyContracts(cancellationToken))return false;
                    RefTrace.Calls=RefTrace.Indices=0;int[] values={7};var pair=QueryProbe.RefSlotCopy(values);if(pair.before!=7||pair.after!=0||values[0]!=0)return false;values[0]=11;
                    if(QueryProbe.RefSlotArray(values)!=0||values[0]!=0)return false;values[0]=13;
                    if(QueryProbe.RefSlotAlias(values)!=0||values[0]!=0||QueryProbe.RefSlotInput(17,values)!=17||values[0]!=17)return false;
                    var tuple=(value:19,tag:true);if(QueryProbe.RefSlotTuple(ref tuple)!=0||tuple.value!=0||!tuple.tag)return false;
                    if(QueryProbe.RefSlotConditional((23,true),29,true)!=29||QueryProbe.RefSlotConditional((23,true),29,false)!=29||QueryProbe.RefSlotDelegate(QueryProbe.ClearRefSlot,values)!=0||values[0]!=0||RefTrace.Calls!=8||RefTrace.Indices!=5)return false;
                    var failure=new InvalidOperationException("ref slot");using var canceled=new CancellationTokenSource();canceled.Cancel();QueryTrace.Failure=failure;values[0]=31;
                    try{_=QueryProbe.RefSlotArray(values);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,failure)){if(values[0]!=31||RefTrace.Calls!=9||RefTrace.Indices!=6)return false;}finally{QueryTrace.Failure=null;}
                    foreach(var outcome in new[]{"Value","Fault","Cancel","HelperFault"}){
                        RefTrace.Calls=RefTrace.Indices=0;int[] delayed={7};var gate=new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.Failure=outcome=="HelperFault"?failure:null;
                        try{var task=QueryProbe.RefSlotTask(gate.Task);var valueTask=QueryProbe.RefSlotValueTask(new ValueTask<int[]>(gate.Task)).AsTask();
                            if(task.IsCompleted||valueTask.IsCompleted||RefTrace.Calls!=0||RefTrace.Indices!=0)return false;
                            if(outcome is "Value" or "HelperFault")gate.SetResult(delayed);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            foreach(var pending in new[]{task,valueTask}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||result!=0)return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "HelperFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            var expected=outcome is "Value" or "HelperFault"?2:0;if(RefTrace.Calls!=expected||RefTrace.Indices!=expected||delayed[0]!=(outcome=="Value"?0:7))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> SlotContracts(CancellationToken cancellationToken){
                    if(!await RefSlotContracts(cancellationToken))return false;
                    SlotTrace.Calls=SlotTrace.Indices=0;var original=(value:7,tag:true);var copy=QueryProbe.TupleSlotCopy(original);
                    if(copy.copy!=7||copy.stored!=0||original.value!=7||!original.tag||QueryProbe.TupleSlotKnown(original,9)!=9||SlotTrace.Calls!=2)return false;
                    if(QueryProbe.TupleSlotRef(ref original)!=0||original.value!=0||!original.tag)return false;
                    int[] array={7};if(QueryProbe.ArraySlotWrite(array)!=0||array[0]!=0||SlotTrace.Indices!=1)return false;array[0]=11;
                    if(QueryProbe.ArraySlotAlias(array)!=0||array[0]!=0||SlotTrace.Indices!=2||SlotTrace.Calls!=5)return false;
                    var dictionary=new Dictionary<int,int>{{3,7}};if(QueryProbe.DictionarySlotWrite(dictionary,3,array)!=7||array[0]!=7||QueryProbe.DictionarySlotWrite(dictionary,4,array)!=0||array[0]!=0||SlotTrace.Indices!=4)return false;
                    var failure=new InvalidOperationException("slot output");using var canceled=new CancellationTokenSource();canceled.Cancel();
                    QueryTrace.Failure=failure;array[0]=13;try{_=QueryProbe.ArraySlotWrite(array);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,failure)){if(array[0]!=13||SlotTrace.Indices!=5||SlotTrace.Calls!=6)return false;}finally{QueryTrace.Failure=null;}
                    foreach(var outcome in new[]{"Value","Fault","Cancel","HelperFault"}){
                        SlotTrace.Calls=0;var gate=new TaskCompletionSource<(int value,bool tag)>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.Failure=outcome=="HelperFault"?failure:null;
                        try{var task=QueryProbe.TupleSlotAsync(gate.Task);var valueTask=QueryProbe.TupleSlotValueAsync(new ValueTask<(int value,bool tag)>(gate.Task)).AsTask();
                            if(task.IsCompleted||valueTask.IsCompleted||SlotTrace.Calls!=0)return false;
                            if(outcome is "Value" or "HelperFault")gate.SetResult((7,true));else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            foreach(var pending in new[]{task,valueTask}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||result!=0)return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "HelperFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            if(SlotTrace.Calls!=(outcome is "Value" or "HelperFault"?2:0))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> MemberStateContracts(CancellationToken cancellationToken){
                    if(!await SlotContracts(cancellationToken))return false;
                    var cell=new StateCell<int>(7);if(QueryProbe.StateField(cell)!=7||cell.Checks!=1||cell.Gets!=1||QueryProbe.StateProperty(cell)!=7||cell.Checks!=2||cell.Gets!=3)return false;
                    if(QueryProbe.StateConditional(cell,9)!=7||QueryProbe.StateFailure(cell,9)!=9||cell.Checks!=4||cell.Gets!=5)return false;
                    var nested=new StateCell<int>(0);if(QueryProbe.StateNested(new StateBox<int>(nested),new StateCell<int>(7))!=0||nested.Checks!=0||nested.Gets!=0)return false;
                    var other=new StateCell<int>(3);if(QueryProbe.StateOther(cell,other)!=3||other.Checks!=0||other.Gets!=0||QueryProbe.StateInvalidated(cell)!=0||cell.Field!=0)return false;
                    if(QueryProbe.StateOutFill(other,11)!=11||other.Field!=11||QueryProbe.StateOutClear(other)!=0||other.Field!=0||other.Gets!=0)return false;
                    var first=new StateCell<int>(5);var second=new StateCell<int>(7);QueryTrace.Value="";var query=QueryProbe.StateQuery(QueryTrace.Source(new[]{first,second}),9);
                    if(first.Checks!=0||second.Checks!=0||QueryTrace.Value!="")return false;
                    using(var iterator=query.GetEnumerator()){if(!iterator.MoveNext()||iterator.Current!=5||first.Checks!=1||first.Gets!=1||second.Checks!=0)return false;}
                    if(QueryTrace.Value!="open;dispose;")return false;
                    var failure=new InvalidOperationException("member state");using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel","HelperFault"}){
                        var delayed=new StateCell<int>(7);var gate=new TaskCompletionSource<StateCell<int>>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.Failure=outcome=="HelperFault"?failure:null;
                        try{var task=QueryProbe.StateAsync(gate.Task);var valueTask=QueryProbe.StateValueAsync(new ValueTask<StateCell<int>>(gate.Task)).AsTask();
                            if(task.IsCompleted||valueTask.IsCompleted||delayed.Checks!=0||delayed.Gets!=0)return false;
                            if(outcome is "Value" or "HelperFault")gate.SetResult(delayed);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            foreach(var pending in new[]{task,valueTask}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||result!=7)return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "HelperFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            if(delayed.Checks!=(outcome is "Value" or "HelperFault"?2:0)||delayed.Gets!=(outcome=="Value"?3:0))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> PostconditionContracts(CancellationToken cancellationToken){
                    if(!await MemberStateContracts(cancellationToken))return false;
                    var cell=new MemberCell<int>(7);var absent=new MemberCell<int>(7,false);
                    if(QueryProbe.OutcomeRead(cell)!=7||QueryProbe.OutcomeRead(absent)!=0||QueryProbe.OutcomeSuccess(absent,9)!=9||QueryProbe.OutcomeConditional(cell,9)!=7||QueryProbe.OutcomeDelegate(QueryProbe.TryOutcome,absent)!=0||QueryProbe.OutcomeOptional(absent)!=0||cell.Reads!=2||absent.Reads!=4)return false;
                    string? missing=null;var text=new string('p',3);if(!ReferenceEquals(QueryProbe.CheckedInput(text),text)||!ReferenceEquals(QueryProbe.ConditionalInput(missing,text),text))return false;
                    try{_=QueryProbe.CheckedInput(missing);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    var failure=new InvalidOperationException("postcondition helper");using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel","HelperFault"}){
                        var delayed=new MemberCell<int>(7,false);var gate=new TaskCompletionSource<MemberCell<int>>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.Failure=outcome=="HelperFault"?failure:null;
                        try{var task=QueryProbe.OutcomeAsync(gate.Task);var valueTask=QueryProbe.OutcomeValueAsync(new ValueTask<MemberCell<int>>(gate.Task)).AsTask();
                            if(task.IsCompleted||valueTask.IsCompleted||delayed.Reads!=0)return false;
                            if(outcome is "Value" or "HelperFault")gate.SetResult(delayed);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            foreach(var pending in new[]{task,valueTask}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||result!=0)return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "HelperFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            if(delayed.Reads!=(outcome is "Value" or "HelperFault"?2:0))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> InputContracts(CancellationToken cancellationToken){
                    if(!await PostconditionContracts(cancellationToken))return false;
                    var text=new string('i',3);var cell=new MemberCell<string>(text);string? missing=null;
                    if(!ReferenceEquals(QueryProbe.InputMethod(cell,missing,text),text)||!ReferenceEquals(cell.Stored,text)||!ReferenceEquals(QueryProbe.InputDelegate(QueryProbe.StoreInput,cell,text,"fallback"),text)||cell.Writes!=2)return false;
                    var numeric=new MemberCell<int?>(7);if(QueryProbe.InputNullableValue(numeric,null,9)!=9||numeric.Stored!=9||numeric.Writes!=1)return false;
                    var query=QueryProbe.InputQuery(cell,new string?[]{null,text},text);if(cell.Writes!=2)return false;
                    using(var iterator=query.GetEnumerator()){if(!iterator.MoveNext()||!ReferenceEquals(iterator.Current,text)||cell.Writes!=3)return false;}
                    var failure=new InvalidOperationException("input contract");using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel","HelperFault"}){
                        var delayed=new MemberCell<string>(text);var gate=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.Failure=outcome=="HelperFault"?failure:null;
                        try{var task=QueryProbe.InputAsync(delayed,gate.Task,text);var valueTask=QueryProbe.InputValueAsync(delayed,new ValueTask<string?>(gate.Task),text).AsTask();
                            if(task.IsCompleted||valueTask.IsCompleted||delayed.Writes!=0)return false;
                            if(outcome is "Value" or "HelperFault")gate.SetResult(null);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            foreach(var pending in new[]{task,valueTask}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||!ReferenceEquals(result,text))return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "HelperFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            if(delayed.Writes!=(outcome is "Value" or "HelperFault"?2:0)||!ReferenceEquals(delayed.Stored,text))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> MemberContracts(CancellationToken cancellationToken){
                    if(!await InputContracts(cancellationToken))return false;
                    var cell=new MemberCell<int>(7);if(QueryProbe.MemberProperty(cell)!=7||cell.Reads!=1||QueryProbe.MemberField(cell)!=7||QueryProbe.MemberIndexer(cell,0)!=7||cell.Reads!=2||cell.Indices!=1)return false;
                    var pair=QueryProbe.MemberStore(cell,9);if(pair!=(9,9)||cell.Stored!=9||cell.Writes!=1||cell.Reads!=3)return false;
                    if(QueryProbe.MemberFieldStore(cell,11)!=11||cell.Field!=11||QueryProbe.MemberRefStore(cell,13)!=13||cell.OptionalStored!=13||cell.Refs!=2)return false;
                    var missing=new MemberCell<int>(7,false);if(QueryProbe.MemberProperty(missing)!=0)return false;
                    QueryTrace.Value="";missing.Reads=0;var query=QueryProbe.MemberQuery(QueryTrace.Source(new[]{missing}));if(QueryTrace.Value!=""||missing.Reads!=0||query.Take(1).Single()!=0||missing.Reads!=1||QueryTrace.Value!="open;dispose;")return false;
                    if(QueryProbe.MemberRequired(new MemberCell<string?>("value"))!="value")return false;
                    try{_=QueryProbe.MemberRequired(new MemberCell<string?>(null));return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    using var canceled=new CancellationTokenSource();canceled.Cancel();var failure=new InvalidOperationException("member-contract");
                    foreach(var outcome in new[]{"Value","Fault","Cancel","GetterFault"}){
                        var delayed=new MemberCell<int>(7);var gate=new TaskCompletionSource<MemberCell<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
                        QueryTrace.Failure=outcome=="GetterFault"?failure:null;
                        try{var task=QueryProbe.MemberAsync(gate.Task);var valueTask=QueryProbe.MemberValueAsync(new ValueTask<MemberCell<int>>(gate.Task)).AsTask();
                            if(task.IsCompleted||valueTask.IsCompleted||delayed.Reads!=0)return false;
                            if(outcome is "Value" or "GetterFault")gate.SetResult(delayed);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                            foreach(var pending in new[]{task,valueTask}){
                                try{var result=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||result!=7)return false;}
                                catch(InvalidOperationException error)when(outcome is "Fault" or "GetterFault"&&ReferenceEquals(error,failure)){}
                                catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                            }
                            if(delayed.Reads!=(outcome is "Value" or "GetterFault"?2:0)||delayed.Indices!=(outcome is "Value" or "GetterFault"?1:0))return false;
                        }finally{QueryTrace.Failure=null;}
                    }
                    return true;
                }
                static async Task<bool> Stores(CancellationToken cancellationToken){
                    if(!await MemberContracts(cancellationToken))return false;
                    if(!await ConditionalContracts(cancellationToken))return false;
                    if(!await ReturnContracts(cancellationToken))return false;
                    if(!await BranchStores(cancellationToken))return false;
                    if(!await TupleCaptures(cancellationToken))return false;
                    if(QueryProbe.Grouped(7)!=0)return false;
                    using(var cancel=new CancellationTokenSource()){
                        cancel.Cancel();var failure=new InvalidOperationException("tuple-await");
                        foreach(var outcome in new[]{"Value","Fault","Cancel"}){
                            int[] first={7},second={9};QueryTrace.RefCalls=QueryTrace.IndexCalls=0;
                            var gate=new TaskCompletionSource<int[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                            var task=QueryProbe.TupleAsync(gate.Task,second);
                            if(task.IsCompleted||QueryTrace.RefCalls!=0||first[0]!=7||second[0]!=9)return false;
                            if(outcome=="Value")gate.SetResult(first);else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(cancel.Token);
                            try{var result=await task.WaitAsync(cancellationToken);if(outcome!="Value"||result!=(0,0)||first[0]!=0||second[0]!=0||QueryTrace.RefCalls!=2||QueryTrace.IndexCalls!=2)return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==cancel.Token){}
                            if(outcome!="Value"&&(QueryTrace.RefCalls!=0||first[0]!=7||second[0]!=9))return false;
                        }
                    }
                    var grouped=await QueryProbe.GroupedAsync(Task.FromResult(7));if(grouped!=(7,0))return false;
                    int[] mixedLeft={7},mixedRight={9};var mixed=await QueryProbe.MixedAsync(new ValueTask<IEnumerable<(int[],int[])>>(new[]{(mixedLeft,mixedRight)}));
                    if(mixedLeft[0]!=7||mixedRight[0]!=9||mixed.Single()!=(0,0)||mixedLeft[0]!=0||mixedRight[0]!=0)return false;
                    int[] row={7};QueryTrace.Value="";QueryTrace.RefCalls=QueryTrace.IndexCalls=0;
                    QueryTrace.OnDispose=()=>row[0]=99;
                    try{var query=QueryProbe.RefStore(QueryTrace.Source(new[]{row}));if(row[0]!=7||QueryTrace.RefCalls!=0||QueryTrace.Value!="")return false;
                        if(query.Take(1).Single()!=0||row[0]!=99||QueryTrace.RefCalls!=1||QueryTrace.IndexCalls!=1||QueryTrace.Value!="open;dispose;")return false;
                    }finally{QueryTrace.OnDispose=null;}
                    row[0]=7;var copied=QueryProbe.RefCopy(new[]{row}).Single();if(copied!=(7,0)||row[0]!=0)return false;
                    row[0]=7;if(QueryProbe.RefSibling(new[]{(row,"tag")}).Single()!="tag"||row[0]!=0)return false;
                    row[0]=7;if(QueryProbe.RefBlock(row)!=0||row[0]!=0)return false;int value=7;if(QueryProbe.RefParameter(ref value)!=0||value!=0)return false;
                    row[0]=7;QueryProbe.RefLocalQuery(new[]{row});if(row[0]!=0)return false;row[0]=7;if(QueryProbe.RefLocal(row)!=0||row[0]!=0)return false;
                    row[0]=7;var captured=QueryProbe.RefCaptured(row);if(row[0]!=7||captured()!=0||row[0]!=0)return false;
                    row[0]=7;if(QueryProbe.ArrayRefStore(new[]{row}).Single()!=0||row[0]!=0)return false;
                    row[0]=7;if(QueryProbe.RefStoreSuppressed(new[]{row}).Single()!=0||row[0]!=0)return false;
                    using var canceled=new CancellationTokenSource();canceled.Cancel();
                    foreach(var outcome in new[]{"Value","Fault","Cancel"}){
                        row[0]=7;QueryTrace.RefCalls=QueryTrace.IndexCalls=0;var gate=new TaskCompletionSource<IEnumerable<int[]>>(TaskCreationOptions.RunContinuationsAsynchronously);
                        var task=QueryProbe.RefAsync(gate.Task);var valueTask=QueryProbe.NullableRefAsync(new ValueTask<IEnumerable<int[]>>(gate.Task)).AsTask();
                        if(task.IsCompleted||valueTask.IsCompleted||QueryTrace.RefCalls!=0)return false;var failure=new InvalidOperationException("ref-store-await");
                        if(outcome=="Value")gate.SetResult(new[]{row});else if(outcome=="Fault")gate.SetException(failure);else gate.SetCanceled(canceled.Token);
                        int executions=0;foreach(var pending in new[]{task,valueTask}){
                            try{var query=await pending.WaitAsync(cancellationToken);if(outcome!="Value"||QueryTrace.RefCalls!=executions)return false;
                                if(query.Single()!=0||row[0]!=0||QueryTrace.RefCalls!=++executions||QueryTrace.IndexCalls!=executions)return false;}
                            catch(InvalidOperationException error)when(outcome=="Fault"&&ReferenceEquals(error,failure)){}
                            catch(OperationCanceledException error)when(outcome=="Cancel"&&error.CancellationToken==canceled.Token){}
                        }
                        if(outcome!="Value"&&(row[0]!=7||QueryTrace.RefCalls!=0||QueryTrace.IndexCalls!=0))return false;
                    }
                    return true;
                }
            }
            """;
        const string newSource = """
            #nullable enable
            using System;using System.Linq;using System.Linq.Expressions;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;
            public struct QueryStoreValue{public static int Calls;public int Value;public string? Text;public QueryStoreValue(){Calls++;Value=99;Text="ctor";}}
            public static class NewQueryRunner{
                public static async Task<bool> Run(CancellationToken cancellationToken){
                    string text=new string('x',2);var uri=new Uri("https://example.invalid/query");
                    if(!await Stores(text,cancellationToken))return false;
                    if(!ReferenceEquals(QueryProbe.DefaultJoin(new[]{text},true).Single(),text)||QueryProbe.DefaultJoin(new[]{text},false).Single()!=null
                        ||QueryProbe.DefaultJoin(new decimal?[]{23m},false).Single()!=null||!ReferenceEquals(QueryProbe.NullJoin(new[]{uri},true).Single(),uri)
                        ||QueryProbe.NullJoin(new[]{uri},false).Single()!=null||!QueryProbe.Coalesce(new decimal?[]{null,23m},99m).SequenceEqual(new[]{99m,23m}))return false;
                    QueryTrace.Value="";var lazy=QueryProbe.Lazy(QueryTrace.Source(new string?[]{text,null}));
                    if(QueryTrace.Value!=""||!ReferenceEquals(lazy.Take(1).Single(),text)||QueryTrace.Value!="open;where;touch;dispose;")return false;
                    QueryTrace.Value="";if(!QueryProbe.Lazy(new string?[]{text,null}).SequenceEqual(new string?[]{text,null})||QueryTrace.Value!="where;touch;where;touch;")return false;
                    var pairs=QueryProbe.Many(new[]{text},new[]{uri}).ToArray();if(pairs.Length!=1||!ReferenceEquals(pairs[0].left,text)||!ReferenceEquals(pairs[0].right,uri))return false;
                    if(!QueryProbe.Order(new[]{"a","c","b"}).SequenceEqual(new[]{"c","b","a"})||!ReferenceEquals(QueryProbe.Continue(new[]{uri}).Single(),uri))return false;
                    var joined=QueryProbe.Join(new[]{("key",uri)},new[]{("key",23m)}).ToArray();if(joined.Length!=1||!ReferenceEquals(joined[0].left,uri)||joined[0].right!=23m)return false;
                    var groups=QueryProbe.GroupJoin(new[]{"a","b"},new[]{"a","a","c"}).ToArray();if(groups.Length!=2||!groups[0].SequenceEqual(new[]{"a","a"})||groups[1].Any())return false;
                    var grouped=QueryProbe.Group(new[]{("key",uri)}).Single();if(grouped.Key!="key"||!ReferenceEquals(grouped.Single(),uri))return false;
                    QueryTrace.Value="";var quoted=QueryProbe.Quoted(new string?[]{text,null}.AsQueryable());
                    if(QueryTrace.Value!=""||quoted.Expression is not MethodCallExpression call||call.Method.GetGenericArguments()[0]!=typeof(string)
                        ||!quoted.SequenceEqual(new string?[]{text,null})||QueryTrace.Value!="where;where;")return false;
                    QueryTrace.Value="";var custom=QueryProbe.Custom(new QueryBox<Uri>(new[]{uri}));if(QueryTrace.Value!="select;"||!ReferenceEquals(custom.Values.Single(),uri)||QueryTrace.Value!="select;touch;")return false;
                    var defaults=QueryProbe.QuotedDefault(new[]{text}.AsQueryable(),false);
                    var selector=(LambdaExpression)((UnaryExpression)((MethodCallExpression)defaults.Expression).Arguments[1]).Operand;
                    if(((ConditionalExpression)selector.Body).IfFalse is not ConstantExpression constant||constant.Type!=typeof(string)||constant.Value!=null||defaults.Single()!=null)return false;
                    QueryTrace.Value="";var gate=new TaskCompletionSource<IEnumerable<string?>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var task=QueryProbe.Async(gate.Task);var valueTask=QueryProbe.ValueAsync(new ValueTask<IEnumerable<string?>>(gate.Task)).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||QueryTrace.Value!="")return false;gate.SetResult(new string?[]{text,null});
                    foreach(var pending in new[]{task,valueTask}){var result=await pending.WaitAsync(cancellationToken);if(QueryTrace.Value!=""||!result.SequenceEqual(new string?[]{text,null})||QueryTrace.Value!="touch;touch;")return false;QueryTrace.Value="";}
                    return true;
                }
                static async Task<bool> TupleCaptures(string text,CancellationToken cancellationToken){
                    var tag=new Uri("https://example.invalid/captured-tuple");
                    if(QueryProbe.TupleCapture(text,tag)()!=null||QueryProbe.TupleCapture((decimal?)23m,tag)()!=null)return false;
                    var copied=QueryProbe.TupleLocalCopy(text,tag);if(!ReferenceEquals(copied.copy,text)||copied.read()!=null)return false;
                    var deferred=QueryProbe.TupleDeferred(text,tag);if(!ReferenceEquals(deferred.read(),text))return false;deferred.clear();if(deferred.read()!=null)return false;
                    var initial=new QueryStoreValue();QueryStoreValue.Calls=0;var stored=QueryProbe.TupleCapture(initial,tag)();
                    if(stored.Value!=0||stored.Text!=null||QueryStoreValue.Calls!=0)return false;
                    var gate=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.Value="";QueryTrace.CaptureCalls=0;
                    var task=QueryProbe.TupleTaskCapture(gate.Task,tag);var valueTask=QueryProbe.TupleValueCapture(new ValueTask<string>(gate.Task),tag).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||QueryTrace.CaptureCalls!=0)return false;gate.SetResult(text);
                    foreach(var pending in new[]{task,valueTask}){var read=await pending.WaitAsync(cancellationToken);if(read()!=null)return false;}
                    if(QueryTrace.CaptureCalls!=2)return false;
                    var optional=await QueryProbe.TupleValueCapture(new ValueTask<decimal?>(23m),tag);return optional()==null;
                }
                static async Task<bool> BranchStores(string text,CancellationToken cancellationToken){
                    foreach(var clear in new[]{true,false}){
                        foreach(var switched in new[]{true,false}){
                            decimal[] first={23m};string?[] second={text};QueryTrace.Value="";QueryTrace.RefCalls=QueryTrace.IndexCalls=0;
                            var source=QueryTrace.Source(new[]{(first,second)});var query=switched?QueryProbe.SwitchMixed(source,clear):QueryProbe.ConditionalMixed(source,clear);
                            if(QueryTrace.Value!=""||QueryTrace.RefCalls!=0||first[0]!=23m||!ReferenceEquals(second[0],text))return false;
                            var result=query.Take(1).Single();if(result.left!=(clear?0m:23m)||first[0]!=result.left||!ReferenceEquals(result.right,clear?null:text)||!ReferenceEquals(second[0],result.right)
                                ||QueryTrace.RefCalls!=2||QueryTrace.IndexCalls!=2||QueryTrace.Value!=(clear?"open;left;right;dispose;":"open;left;right;keep;keep;dispose;"))return false;
                        }
                        var initial=new QueryStoreValue();QueryStoreValue.Calls=0;var firstStore=new[]{initial};string?[] secondStore={text};decimal?[] thirdStore={23m};
                        var nested=QueryProbe.NestedConditionalMixed(new[]{(firstStore,secondStore,thirdStore)},clear).Single();
                        if(nested.left.Value!=(clear?0:99)||nested.left.Text!=(clear?null:"ctor")||!ReferenceEquals(nested.rest.mid,clear?null:text)||nested.rest.right!=(clear?null:23m)
                            ||firstStore[0].Value!=nested.left.Value||!ReferenceEquals(secondStore[0],nested.rest.mid)||thirdStore[0]!=nested.rest.right||QueryStoreValue.Calls!=0)return false;
                        string?[] ordinaryLeft={text};decimal?[] ordinaryRight={23m};var ordinary=QueryProbe.ConditionalArrayStores(ordinaryLeft,ordinaryRight,clear);
                        if(!ReferenceEquals(ordinary.left,clear?null:text)||ordinary.right!=(clear?null:23m)||!ReferenceEquals(ordinaryLeft[0],ordinary.left)||ordinaryRight[0]!=ordinary.right)return false;
                    }
                    decimal[] asyncLeft={23m};string?[] asyncRight={text};var gate=new TaskCompletionSource<IEnumerable<(decimal[],string?[])>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    QueryTrace.RefCalls=QueryTrace.IndexCalls=0;var task=QueryProbe.ConditionalMixedAsync(gate.Task,true);var valueTask=QueryProbe.ConditionalMixedValueAsync(new ValueTask<IEnumerable<(decimal[],string?[])>>(gate.Task),true).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||QueryTrace.RefCalls!=0)return false;gate.SetResult(new[]{(asyncLeft,asyncRight)});
                    var executed=0;foreach(var pending in new[]{task,valueTask}){var query=await pending.WaitAsync(cancellationToken);if(QueryTrace.RefCalls!=executed*2)return false;
                        var result=query.Single();if(result.left!=0m||result.right!=null||asyncLeft[0]!=0m||asyncRight[0]!=null||QueryTrace.RefCalls!=++executed*2)return false;}
                    return true;
                }
                static async Task<bool> ReturnContracts(string text,CancellationToken cancellationToken){
                    QueryMaybeAction<string> action=QueryProbe.ContractMaybe;var uri=new Uri("https://example.invalid/return-contract");string[] array={text};
                    foreach(var keep in new[]{true,false}){
                        if(!ReferenceEquals(QueryProbe.ContractValue(text,keep),keep?text:null)||!ReferenceEquals(QueryProbe.ContractDelegate(action,text,keep),keep?text:null)
                            ||!ReferenceEquals(QueryProbe.ContractArray(array,keep),keep?array:null))return false;
                        var pair=QueryProbe.ContractTuple(text,uri,keep);if(!ReferenceEquals(pair.value,keep?text:null)||!ReferenceEquals(pair.sibling,uri))return false;
                    }
                    if(QueryProbe.ContractValue((decimal?)23m,false)!=null)return false;
                    var initial=new QueryStoreValue();QueryStoreValue.Calls=0;var stored=QueryProbe.ContractValue(initial,false);if(stored.Value!=0||stored.Text!=null||QueryStoreValue.Calls!=0)return false;
                    if(!ReferenceEquals(QueryProbe.ContractRequire(uri),uri))return false;try{_=QueryProbe.ContractRequire<Uri>(null);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    var gate=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.ContractCalls=0;
                    var task=QueryProbe.ContractAsync(gate.Task,false);var valueTask=QueryProbe.ContractValueAsync(new ValueTask<string>(gate.Task),false).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||QueryTrace.ContractCalls!=0)return false;gate.SetResult(text);
                    foreach(var pending in new[]{task,valueTask}){var result=await pending.WaitAsync(cancellationToken);if(result!=null)return false;}
                    return QueryTrace.ContractCalls==2;
                }
                static async Task<bool> ConditionalContracts(string text,CancellationToken cancellationToken){
                    var uri=new Uri("https://example.invalid/conditional-contract");QueryGuardedAction<string> action=QueryProbe.ConditionalContract;
                    QueryTrace.Value="";QueryTrace.ContractCalls=0;if(!ReferenceEquals(QueryProbe.ConditionalKnown(text),text)||QueryTrace.Value!="touch;"||QueryTrace.ContractCalls!=1
                        ||!ReferenceEquals(QueryProbe.ConditionalDelegate(action,text),text)||QueryProbe.ConditionalUnknown(text,(object?)null)!=null
                        ||!ReferenceEquals(QueryProbe.ConditionalUnknown(text,uri),text)||QueryProbe.ConditionalUnknown((decimal?)23m,(object?)null)!=null)return false;
                    var initial=new QueryStoreValue();QueryStoreValue.Calls=0;var zero=QueryProbe.ConditionalUnknown(initial,(object?)null);if(zero.Value!=0||zero.Text!=null||QueryStoreValue.Calls!=0)return false;
                    try{_=QueryProbe.ConditionalKnown<string?>(null);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    var gate=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.ContractCalls=0;
                    var task=QueryProbe.ConditionalAsync(gate.Task);var optional=QueryProbe.ConditionalValueAsync(new ValueTask<string>(gate.Task),(object?)null).AsTask();
                    if(task.IsCompleted||optional.IsCompleted||QueryTrace.ContractCalls!=0)return false;gate.SetResult(text);
                    if(!ReferenceEquals(await task.WaitAsync(cancellationToken),text)||await optional.WaitAsync(cancellationToken)!=null||QueryTrace.ContractCalls!=2)return false;
                    return true;
                }
                static async Task<bool> RequiredContracts(string text,CancellationToken cancellationToken){
                    RequiredTrace.Calls=0;var uri=new Uri("https://example.invalid/required-input");decimal? number=23m;
                    if(!ReferenceEquals(QueryProbe.RequiredValue(text),text)||QueryProbe.RequiredNamed(number)!=23m||!ReferenceEquals(QueryProbe.RequiredDelegate(QueryProbe.RequireInput,uri),uri)||QueryProbe.RequiredReadonly(in number)!=23m||number!=23m||PartialInputProbe.RequiredPartial(number)!=23m||RequiredTrace.Calls!=5)return false;
                    try{_=QueryProbe.RequiredValue<string>(null!);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    try{_=PartialInputProbe.RequiredPartial<decimal?>(null!);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    var parameter=typeof(QueryProbe).GetMethod(nameof(QueryProbe.RequiredValue))!.GetParameters()[0];if(parameter.GetCustomAttributes(typeof(System.Diagnostics.CodeAnalysis.DisallowNullAttribute),false).Length!=1)return false;
                    var partialParameter=typeof(PartialInputProbe).GetMethod(nameof(PartialInputProbe.RequiredPartial))!.GetParameters()[0];if(partialParameter.GetCustomAttributes(typeof(System.Diagnostics.CodeAnalysis.DisallowNullAttribute),false).Length!=1)return false;
                    var gate=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);RequiredTrace.Calls=0;
                    var task=QueryProbe.RequiredTask(number,gate.Task);var valueTask=QueryProbe.RequiredValueTask(number,gate.Task).AsTask();if(task.IsCompleted||valueTask.IsCompleted||RequiredTrace.Calls!=0)return false;gate.SetResult(true);
                    foreach(var pending in new[]{task,valueTask}){if(await pending.WaitAsync(cancellationToken)!=23m)return false;}
                    return RequiredTrace.Calls==2;
                }
                static async Task<bool> ReadonlyContracts(string text,CancellationToken cancellationToken){
                    if(!await RequiredContracts(text,cancellationToken))return false;
                    ReadonlyTrace.Indices=ReadonlyTrace.Returns=0;var uri=new Uri("https://example.invalid/readonly-input");var cell=new ReadonlyCell<Uri>(uri);Uri? missing=null;Uri?[] values={null};
                    if(!ReferenceEquals(QueryProbe.ReadonlyValue(cell,missing),uri)||!ReferenceEquals(QueryProbe.ReadonlyArray(cell,values),uri)||values[0]!=null||ReadonlyTrace.Indices!=1)return false;
                    values[0]=uri;if(!ReferenceEquals(QueryProbe.ReadonlyAlias(cell,values),uri)||!ReferenceEquals(values[0],uri)||!ReferenceEquals(QueryProbe.ReadonlyRefValue(cell,missing),uri)||cell.Observed!=null||cell.Calls!=4||cell.ReadonlyCalls!=1||ReadonlyTrace.Indices!=2)return false;
                    var numeric=new ReadonlyCell<decimal?>(23m);decimal? absentNumber=null;if(QueryProbe.ReadonlyScalar(numeric,absentNumber)!=23m||numeric.Value!=23m||numeric.Observed!=null)return false;
                    Uri[] array={uri};var arrays=new ReadonlyCell<Uri[]>(array);Uri[]? absent=null;if(QueryProbe.ReadonlyEchoArray(arrays,absent)!=null||!ReferenceEquals(QueryProbe.ReadonlyEchoArray(arrays,array),array)||!ReferenceEquals(arrays.Value,array))return false;
                    if(QueryProbe.MixedReturn(text,false)!=null||!ReferenceEquals(QueryProbe.MixedDelegate(QueryProbe.MixedReturnValue,text,true),text)||ReadonlyTrace.Returns!=2||QueryProbe.MixedReturn((decimal?)23m,false)!=null)return false;
                    var priority=new PriorityCell<string>(null);if(QueryProbe.PriorityMaybe(priority)!=null)return false;try{_=QueryProbe.PriorityRequired(priority);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}if(priority.Reads!=2)return false;
                    var gate=new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);var delayed=new ReadonlyCell<Uri>(uri);
                    var task=QueryProbe.ReadonlyTask(delayed,gate.Task);var valueTask=QueryProbe.ReadonlyValueTask(delayed,new ValueTask<Uri?>(gate.Task)).AsTask();if(task.IsCompleted||valueTask.IsCompleted||delayed.Calls!=0)return false;gate.SetResult(null);
                    foreach(var pending in new[]{task,valueTask}){if(!ReferenceEquals(await pending.WaitAsync(cancellationToken),uri))return false;}
                    return delayed.Calls==2&&delayed.Observed==null;
                }
                static async Task<bool> RefSlotContracts(string text,CancellationToken cancellationToken){
                    if(!await ReadonlyContracts(text,cancellationToken))return false;
                    RefTrace.Calls=RefTrace.Indices=0;string?[] values={text};var pair=QueryProbe.RefSlotCopy(values);if(!ReferenceEquals(pair.before,text)||pair.after!=null||values[0]!=null)return false;values[0]=text;
                    if(QueryProbe.RefSlotArray(values)!=null||values[0]!=null)return false;values[0]=text;
                    if(QueryProbe.RefSlotAlias(values)!=null||values[0]!=null||!ReferenceEquals(QueryProbe.RefSlotInput(text,values),text)||!ReferenceEquals(values[0],text))return false;
                    var uri=new Uri("https://example.invalid/ref-slot");(string? value,Uri tag) tuple=(text,uri);if(QueryProbe.RefSlotTuple(ref tuple)!=null||tuple.value!=null||!ReferenceEquals(tuple.tag,uri))return false;
                    if(!ReferenceEquals(QueryProbe.RefSlotConditional(tuple,text,true),text)||!ReferenceEquals(QueryProbe.RefSlotConditional(tuple,text,false),text)||QueryProbe.RefSlotDelegate(QueryProbe.ClearRefSlot,values)!=null||values[0]!=null||RefTrace.Calls!=8||RefTrace.Indices!=5)return false;
                    decimal?[] numeric={23m};if(QueryProbe.RefSlotArray(numeric)!=null||numeric[0]!=null)return false;
                    var original=new QueryStoreValue();QueryStoreValue.Calls=0;QueryStoreValue[] structs={original};var zero=QueryProbe.RefSlotArray(structs);if(zero.Value!=0||zero.Text!=null||structs[0].Value!=0||structs[0].Text!=null||QueryStoreValue.Calls!=0)return false;
                    var gate=new TaskCompletionSource<string?[]>(TaskCreationOptions.RunContinuationsAsynchronously);string?[] delayed={text};RefTrace.Calls=RefTrace.Indices=0;
                    var task=QueryProbe.RefSlotTask(gate.Task);var valueTask=QueryProbe.RefSlotValueTask(new ValueTask<string?[]>(gate.Task)).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||RefTrace.Calls!=0)return false;gate.SetResult(delayed);
                    foreach(var pending in new[]{task,valueTask}){if(await pending.WaitAsync(cancellationToken)!=null)return false;}
                    return delayed[0]==null&&RefTrace.Calls==2&&RefTrace.Indices==2;
                }
                static async Task<bool> SlotContracts(string text,CancellationToken cancellationToken){
                    if(!await RefSlotContracts(text,cancellationToken))return false;
                    SlotTrace.Calls=SlotTrace.Indices=0;var uri=new Uri("https://example.invalid/tuple-slot");(string? value,Uri tag) original=(text,uri);var copy=QueryProbe.TupleSlotCopy(original);
                    if(!ReferenceEquals(copy.copy,text)||copy.stored!=null||!ReferenceEquals(original.value,text)||!ReferenceEquals(original.tag,uri)||!ReferenceEquals(QueryProbe.TupleSlotKnown(original,text),text)||SlotTrace.Calls!=2)return false;
                    if(QueryProbe.TupleSlotRef(ref original)!=null||original.value!=null||!ReferenceEquals(original.tag,uri))return false;
                    string?[] array={text};if(QueryProbe.ArraySlotWrite(array)!=null||array[0]!=null||SlotTrace.Indices!=1)return false;array[0]=text;
                    if(QueryProbe.ArraySlotAlias(array)!=null||array[0]!=null||SlotTrace.Indices!=2||SlotTrace.Calls!=5)return false;
                    var key=Guid.NewGuid();var dictionary=new Dictionary<Guid,string>{{key,text}};if(!ReferenceEquals(QueryProbe.DictionarySlotWrite(dictionary,key,array),text)||!ReferenceEquals(array[0],text)||QueryProbe.DictionarySlotWrite(dictionary,Guid.Empty,array)!=null||array[0]!=null||SlotTrace.Indices!=4)return false;
                    decimal?[] numeric={23m};if(QueryProbe.ArraySlotWrite(numeric)!=null||numeric[0]!=null)return false;
                    var value=new QueryStoreValue();QueryStoreValue.Calls=0;QueryStoreValue[] structs={value};var zero=QueryProbe.ArraySlotWrite(structs);if(zero.Value!=0||zero.Text!=null||structs[0].Value!=0||structs[0].Text!=null||QueryStoreValue.Calls!=0)return false;
                    var gate=new TaskCompletionSource<(string? value,Uri tag)>(TaskCreationOptions.RunContinuationsAsynchronously);SlotTrace.Calls=0;
                    var task=QueryProbe.TupleSlotAsync(gate.Task);var valueTask=QueryProbe.TupleSlotValueAsync(new ValueTask<(string? value,Uri tag)>(gate.Task)).AsTask();var filled=QueryProbe.TupleSlotKnownAsync(new ValueTask<(string? value,Uri tag)>(gate.Task),text).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||filled.IsCompleted||SlotTrace.Calls!=0)return false;gate.SetResult((text,uri));
                    if(await task.WaitAsync(cancellationToken)!=null||await valueTask.WaitAsync(cancellationToken)!=null||!ReferenceEquals(await filled.WaitAsync(cancellationToken),text)||SlotTrace.Calls!=3)return false;
                    return true;
                }
                static async Task<bool> MemberStateContracts(string text,CancellationToken cancellationToken){
                    if(!await SlotContracts(text,cancellationToken))return false;
                    var cell=new StateCell<string>(text);var missing=new StateCell<string>(null);
                    if(!ReferenceEquals(QueryProbe.StateField(cell),text)||!ReferenceEquals(QueryProbe.StateProperty(cell),text)||cell.Checks!=2||cell.Gets!=3||!ReferenceEquals(QueryProbe.StateConditional(missing,text),text)||QueryProbe.StateFailure(missing,text)!=null||missing.Checks!=2||missing.Gets!=0)return false;
                    var nested=new StateCell<string>(null);if(QueryProbe.StateNested(new StateBox<string>(nested),new StateCell<decimal?>(23m))!=null||nested.Checks!=0||nested.Gets!=0)return false;
                    var uri=new Uri("https://example.invalid/member-state");var receiver=new StateCell<Uri>(uri);if(QueryProbe.StateOther(receiver,missing)!=null||receiver.Checks!=1||missing.Checks!=2)return false;
                    if(QueryProbe.StateInvalidated(cell)!=null||cell.Field!=null||!ReferenceEquals(QueryProbe.StateOutFill(cell,text),text)||!ReferenceEquals(cell.Field,text)||QueryProbe.StateOutClear(cell)!=null||cell.Field!=null)return false;
                    var numeric=new StateCell<decimal?>(23m);if(QueryProbe.StateField(numeric)!=23m||QueryProbe.StateOutClear(numeric)!=null||numeric.Field!=null)return false;
                    try{_=QueryProbe.StateField(missing);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    var first=new StateCell<string>(text);var second=new StateCell<string>(null);var query=QueryProbe.StateQuery(new[]{first,second},text);if(first.Checks!=0||second.Checks!=0)return false;
                    var values=query.ToArray();if(values.Length!=2||!ReferenceEquals(values[0],text)||!ReferenceEquals(values[1],text)||first.Checks!=1||first.Gets!=1||second.Checks!=1||second.Gets!=0)return false;
                    var gate=new TaskCompletionSource<StateCell<string>>(TaskCreationOptions.RunContinuationsAsynchronously);var delayed=new StateCell<string>(text);
                    var task=QueryProbe.StateAsync(gate.Task);var valueTask=QueryProbe.StateValueAsync(new ValueTask<StateCell<string>>(gate.Task)).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||delayed.Checks!=0)return false;gate.SetResult(delayed);
                    foreach(var pending in new[]{task,valueTask}){if(!ReferenceEquals(await pending.WaitAsync(cancellationToken),text))return false;}
                    return delayed.Checks==2&&delayed.Gets==3;
                }
                static async Task<bool> PostconditionContracts(string text,CancellationToken cancellationToken){
                    if(!await MemberStateContracts(text,cancellationToken))return false;
                    var cell=new MemberCell<string>(text);var absent=new MemberCell<string>(text,false);
                    if(!ReferenceEquals(QueryProbe.OutcomeRead(cell),text)||QueryProbe.OutcomeRead(absent)!=null||!ReferenceEquals(QueryProbe.OutcomeSuccess(absent,text),text)||!ReferenceEquals(QueryProbe.OutcomeConditional(cell,text),text)||QueryProbe.OutcomeDelegate(QueryProbe.TryOutcome,absent)!=null||QueryProbe.OutcomeOptional(absent)!=null||cell.Reads!=2||absent.Reads!=4)return false;
                    if(QueryProbe.OutcomeRead(new MemberCell<decimal?>(23m,false))!=null||QueryProbe.OutcomeOptional(new MemberCell<decimal?>(23m,false))!=null)return false;
                    var uri=new Uri("https://example.invalid/postconditions");Uri? missing=null;if(!ReferenceEquals(QueryProbe.CheckedInput(uri),uri)||!ReferenceEquals(QueryProbe.ConditionalInput(missing,uri),uri))return false;
                    try{_=QueryProbe.CheckedInput(missing);return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    var gate=new TaskCompletionSource<MemberCell<string>>(TaskCreationOptions.RunContinuationsAsynchronously);var delayed=new MemberCell<string>(text,false);
                    var task=QueryProbe.OutcomeAsync(gate.Task);var valueTask=QueryProbe.OutcomeValueAsync(new ValueTask<MemberCell<string>>(gate.Task)).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||delayed.Reads!=0)return false;gate.SetResult(delayed);
                    foreach(var pending in new[]{task,valueTask}){if(await pending.WaitAsync(cancellationToken)!=null)return false;}
                    return delayed.Reads==2;
                }
                static async Task<bool> InputContracts(string text,CancellationToken cancellationToken){
                    if(!await PostconditionContracts(text,cancellationToken))return false;
                    var uri=new Uri("https://example.invalid/input-contract");var cell=new MemberCell<Uri>(uri);Uri? missing=null;
                    if(!ReferenceEquals(QueryProbe.InputMethod(cell,missing,uri),uri)||!ReferenceEquals(QueryProbe.InputDelegate(QueryProbe.StoreInput,cell,uri,uri),uri)||!ReferenceEquals(cell.Stored,uri)||cell.Writes!=2)return false;
                    var query=QueryProbe.InputQuery(cell,new Uri?[]{null,uri},uri);if(cell.Writes!=2)return false;
                    using(var iterator=query.GetEnumerator()){if(!iterator.MoveNext()||!ReferenceEquals(iterator.Current,uri)||cell.Writes!=3)return false;}
                    var numeric=new MemberCell<decimal?>(7m);if(QueryProbe.InputNullableValue(numeric,null,9m)!=9m||numeric.Stored!=9m||numeric.Writes!=1)return false;
                    var gate=new TaskCompletionSource<Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);var delayed=new MemberCell<Uri>(uri);
                    var task=QueryProbe.InputAsync(delayed,gate.Task,uri);var valueTask=QueryProbe.InputValueAsync(delayed,new ValueTask<Uri?>(gate.Task),uri).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||delayed.Writes!=0)return false;gate.SetResult(null);
                    foreach(var pending in new[]{task,valueTask}){if(!ReferenceEquals(await pending.WaitAsync(cancellationToken),uri))return false;}
                    return delayed.Writes==2&&ReferenceEquals(delayed.Stored,uri);
                }
                static async Task<bool> MemberContracts(string text,CancellationToken cancellationToken){
                    if(!await InputContracts(text,cancellationToken))return false;
                    var cell=new MemberCell<string>(text);if(!ReferenceEquals(QueryProbe.MemberProperty(cell),text)||!ReferenceEquals(QueryProbe.MemberField(cell),text)||!ReferenceEquals(QueryProbe.MemberIndexer(cell,0),text)||cell.Reads!=2||cell.Indices!=1)return false;
                    var next=new string('y',3);var pair=QueryProbe.MemberStore(cell,next);if(!ReferenceEquals(pair.copy,next)||!ReferenceEquals(pair.stored,next)||!ReferenceEquals(cell.Stored,next)||cell.Writes!=1)return false;
                    if(!ReferenceEquals(QueryProbe.MemberFieldStore(cell,text),text)||!ReferenceEquals(cell.Field,text)||!ReferenceEquals(QueryProbe.MemberRefStore(cell,text),text)||!ReferenceEquals(cell.OptionalStored,text)||cell.Refs!=2)return false;
                    var missing=new MemberCell<string>(text,false);if(QueryProbe.MemberProperty(missing)!=null||QueryProbe.MemberQuery(new[]{missing}).Single()!=null)return false;
                    if(QueryProbe.MemberProperty(new MemberCell<decimal?>(23m,false))!=null)return false;
                    var original=new QueryStoreValue();QueryStoreValue.Calls=0;var zero=QueryProbe.MemberProperty(new MemberCell<QueryStoreValue>(original,false));if(zero.Value!=0||zero.Text!=null||QueryStoreValue.Calls!=0)return false;
                    var uri=new Uri("https://example.invalid/member-required");if(!ReferenceEquals(QueryProbe.MemberRequired(new MemberCell<Uri?>(uri)),uri))return false;
                    try{_=QueryProbe.MemberRequired(new MemberCell<Uri?>(null));return false;}catch(InvalidOperationException error)when(ReferenceEquals(error,QueryTrace.ContractFailure)){}
                    var gate=new TaskCompletionSource<MemberCell<string>>(TaskCreationOptions.RunContinuationsAsynchronously);var delayed=new MemberCell<string>(text,false);
                    var task=QueryProbe.MemberAsync(gate.Task);var valueTask=QueryProbe.MemberValueAsync(new ValueTask<MemberCell<string>>(gate.Task)).AsTask();
                    if(task.IsCompleted||valueTask.IsCompleted||delayed.Reads!=0)return false;gate.SetResult(delayed);
                    foreach(var pending in new[]{task,valueTask}){var result=await pending.WaitAsync(cancellationToken);if(result!=null)return false;}
                    return delayed.Reads==2&&delayed.Indices==1;
                }
                static async Task<bool> Stores(string text,CancellationToken cancellationToken){
                    if(!await MemberContracts(text,cancellationToken))return false;
                    if(!await ConditionalContracts(text,cancellationToken))return false;
                    if(!await ReturnContracts(text,cancellationToken))return false;
                    if(!await BranchStores(text,cancellationToken))return false;
                    if(!await TupleCaptures(text,cancellationToken))return false;
                    if(QueryProbe.Grouped(text)!=null||QueryProbe.Grouped<decimal?>(23m)!=null)return false;
                    var gateCopy=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);var copyTask=QueryProbe.GroupedAsync(gateCopy.Task);
                    if(copyTask.IsCompleted)return false;gateCopy.SetResult(text);var grouped=await copyTask.WaitAsync(cancellationToken);
                    if(!ReferenceEquals(grouped.copy,text)||grouped.stored!=null)return false;
                    string?[] tupleLeft={text};decimal?[] tupleRight={23m};var tupleGate=new TaskCompletionSource<string?[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    QueryTrace.RefCalls=QueryTrace.IndexCalls=0;var tupleTask=QueryProbe.TupleAsync(tupleGate.Task,tupleRight);
                    if(tupleTask.IsCompleted||QueryTrace.RefCalls!=0)return false;tupleGate.SetResult(tupleLeft);var tuple=await tupleTask.WaitAsync(cancellationToken);
                    if(tuple.left!=null||tuple.right!=null||tupleLeft[0]!=null||tupleRight[0]!=null||QueryTrace.RefCalls!=2||QueryTrace.IndexCalls!=2)return false;
                    decimal[] mixedLeft={23m};string?[] mixedRight={text};QueryTrace.RefCalls=QueryTrace.IndexCalls=0;
                    var mixedGate=new TaskCompletionSource<IEnumerable<(decimal[],string?[])>>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var mixedTask=QueryProbe.MixedAsync(new ValueTask<IEnumerable<(decimal[],string?[])>>(mixedGate.Task)).AsTask();
                    if(mixedTask.IsCompleted||QueryTrace.RefCalls!=0)return false;mixedGate.SetResult(new[]{(mixedLeft,mixedRight)});
                    var mixed=await mixedTask.WaitAsync(cancellationToken);if(QueryTrace.RefCalls!=0||mixedLeft[0]!=23m||!ReferenceEquals(mixedRight[0],text))return false;
                    var mixedValue=mixed.Single();if(mixedValue.left!=0m||mixedValue.right!=null||mixedLeft[0]!=0m||mixedRight[0]!=null||QueryTrace.RefCalls!=2||QueryTrace.IndexCalls!=2)return false;
                    decimal?[] optionalMixed={23m};mixedLeft[0]=23m;var optionalQuery=await QueryProbe.MixedAsync(new ValueTask<IEnumerable<(decimal[],decimal?[])>>(new[]{(mixedLeft,optionalMixed)}));
                    var optionalValue=optionalQuery.Single();if(optionalValue.left!=0m||optionalValue.right!=null||optionalMixed[0]!=null)return false;
                    decimal[] number={23m};if(QueryProbe.RefStore(new[]{number}).Single()!=0m||number[0]!=0m)return false;
                    var original=new QueryStoreValue();QueryStoreValue.Calls=0;var row=new[]{original};var copied=QueryProbe.RefCopy(new[]{row}).Single();
                    if(copied.copy.Value!=99||copied.copy.Text!="ctor"||copied.stored.Value!=0||copied.stored.Text!=null||row[0].Value!=0||row[0].Text!=null||QueryStoreValue.Calls!=0)return false;
                    number[0]=23m;var tag=new Uri("https://example.invalid/ref-store");if(!ReferenceEquals(QueryProbe.RefSibling(new[]{(number,tag)}).Single(),tag)||number[0]!=0m)return false;
                    number[0]=23m;if(QueryProbe.RefBlock(number)!=0m||number[0]!=0m)return false;decimal value=23m;if(QueryProbe.RefParameter(ref value)!=0m||value!=0m)return false;
                    row[0]=original;QueryProbe.RefLocalQuery(new[]{row});if(row[0].Value!=0||row[0].Text!=null||QueryStoreValue.Calls!=0)return false;
                    string?[] broad={text};if(QueryProbe.RefBlock(broad)!=null||broad[0]!=null)return false;
                    broad[0]=text;if(QueryProbe.RefLocal(broad)!=null||broad[0]!=null)return false;
                    broad[0]=text;var captured=QueryProbe.RefCaptured(broad);if(!ReferenceEquals(broad[0],text)||captured()!=null||broad[0]!=null)return false;
                    broad[0]=text;if(QueryProbe.ArrayRefStore(new[]{broad}).Single()!=null||broad[0]!=null)return false;
                    broad[0]=text;if(QueryProbe.RefStoreSuppressed(new[]{broad}).Single()!=null||broad[0]!=null)return false;
                    string? slot=text;if(QueryProbe.RefParameter(ref slot)!=null||slot!=null)return false;
                    string?[] reference={text};QueryTrace.Value="";QueryTrace.RefCalls=QueryTrace.IndexCalls=0;QueryTrace.OnDispose=()=>reference[0]=text;
                    try{var query=QueryProbe.NullableRefStore(QueryTrace.Source(new[]{reference}));if(!ReferenceEquals(reference[0],text)||QueryTrace.RefCalls!=0)return false;
                        if(query.Take(1).Single()!=null||!ReferenceEquals(reference[0],text)||QueryTrace.RefCalls!=1||QueryTrace.IndexCalls!=1||QueryTrace.Value!="open;dispose;")return false;
                    }finally{QueryTrace.OnDispose=null;}
                    decimal?[] optional={23m};if(QueryProbe.NullableRefStore(new[]{optional}).Single()!=null||optional[0]!=null)return false;
                    var gate=new TaskCompletionSource<IEnumerable<string?[]>>(TaskCreationOptions.RunContinuationsAsynchronously);QueryTrace.RefCalls=QueryTrace.IndexCalls=0;
                    var pending=QueryProbe.NullableRefAsync(new ValueTask<IEnumerable<string?[]>>(gate.Task)).AsTask();
                    if(pending.IsCompleted||!ReferenceEquals(reference[0],text))return false;gate.SetResult(new[]{reference});
                    var delayed=await pending.WaitAsync(cancellationToken);if(QueryTrace.RefCalls!=0||!ReferenceEquals(reference[0],text))return false;
                    return delayed.Single()==null&&reference[0]==null&&QueryTrace.RefCalls==1&&QueryTrace.IndexCalls==1;
                }
            }
            """;
        using var workspace = new AdhocWorkspace();
        var project = workspace.AddProject(ProjectInfo.Create(ProjectId.CreateNewId(), VersionStamp.Create(), "Queries", "Queries", LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, generalDiagnosticOption: ReportDiagnostic.Error),
            metadataReferences: Program.Compile([]).References));
        var document = project.AddDocument("Queries.cs", source);
        var original = (await document.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var provider = new HmSignatureCodeFixProvider();
        var context = new FixAllContext(document, provider, FixAllScope.Project, "HM.GeneralizeSignature", provider.FixableDiagnosticIds, new CodeFixChecks.DiagnosticSource(), cancellationToken);
        var action = (await provider.GetFixAllProvider().GetFixAsync(context).ConfigureAwait(false))!;
        var solution = (await action.GetOperationsAsync(cancellationToken).ConfigureAwait(false)).OfType<ApplyChangesOperation>().Single().ChangedSolution;
        var changed = (await solution.GetDocument(document.Id)!.Project.GetCompilationAsync(cancellationToken).ConfigureAwait(false))!;
        var loadContext = new System.Runtime.Loader.AssemblyLoadContext("QueryInference", isCollectible: true);
        try
        {
            async Task Execute(Compilation compilation, string runner)
            {
                ArgumentNullException.ThrowIfNull(compilation);
                ArgumentException.ThrowIfNullOrEmpty(runner);
                using var image = new MemoryStream();
                var emitted = compilation.Emit(image, cancellationToken: cancellationToken);
                if (!emitted.Success)
                { throw new InvalidOperationException("Query emission: " + string.Join("\n", emitted.Diagnostics)); }
                image.Position = 0;
                var assembly = loadContext.LoadFromStream(image);
                var pending = (Task<bool>)assembly.GetType(runner)!.GetMethod("Run")!.Invoke(null, [cancellationToken])!;
                if (!await pending.WaitAsync(cancellationToken).ConfigureAwait(false))
                { throw new InvalidOperationException("Query runtime effects: " + runner); }
            }
            await Execute(original.WithAssemblyName("BeforeQueries"), "OriginalQueryRunner").ConfigureAwait(false);
            await Execute(changed.WithAssemblyName("AfterQueries"), "OriginalQueryRunner").ConfigureAwait(false);
            Program.Log.Info("PASS QueryOriginalLazinessDisposalOperatorsGroupsTreesAndAwaitEffects");
            await Execute(changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText(newSource, cancellationToken: cancellationToken)).WithAssemblyName("NewQueries"), "NewQueryRunner").ConfigureAwait(false);
            Program.Log.Info("PASS QueryNewReferenceNullableAndIndependentDomains");
            var invalid = changed.AddSyntaxTrees(CSharpSyntaxTree.ParseText("using System.Collections.Generic;class Bad{void Check(){_=QueryProbe.Numeric(new[]{\"bad\"});IEnumerable<string> values=QueryProbe.Typed(new[]{1});_=QueryProbe.RefStore(new[]{new[]{\"bad\"}});}}", cancellationToken: cancellationToken));
            if (invalid.GetDiagnostics(cancellationToken).Count(diagnostic =>
                { ArgumentNullException.ThrowIfNull(diagnostic); return diagnostic.Severity == DiagnosticSeverity.Error; }) != 3)
            { throw new InvalidOperationException("Query numeric or typed-from boundaries changed."); }
            Program.Log.Info("PASS QueryRetainsNumericAndExplicitCastDomains");
        }
        finally { loadContext.Unload(); }
        return 3;
    }
}
