#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-query-consumer.XXXXXX)
  cp global.json "$fixture/global.json"
  package_reference=""
  constants=""
  if [ "$language" != 8 ]; then constants=ANNOTATED_REF; fi
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$fixture/Queries.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><DefineConstants>$constants</DefineConstants><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>$package_reference</Project>
EOF
  cat > "$fixture/Methods.cs" << 'EOF'
using System;using System.Linq;using System.Collections.Generic;using System.Threading.Tasks;
public sealed class QueryBox<T>{public QueryBox<U> Select<U>(Func<T,U> selector)=>throw new NotImplementedException();}
public sealed class MemberContractCell<T>{
  [System.Diagnostics.CodeAnalysis.MaybeNull]public T Field=default!;
  [System.Diagnostics.CodeAnalysis.MaybeNull]public T Value{get=>Field!;set=>Field=value!;}
  public T Accessor{[return:System.Diagnostics.CodeAnalysis.MaybeNull]get=>Field!;}
  [System.Diagnostics.CodeAnalysis.MaybeNull]public T this[int index]=>Field!;
}
public sealed class RequiredMemberCell<T>where T:class{[System.Diagnostics.CodeAnalysis.NotNull]public T? Value=>throw new NotImplementedException();}
public sealed class InputCell<T>{public T Value=default!;public T Accept([System.Diagnostics.CodeAnalysis.AllowNull]T value,T fallback)=>Value=value is null?fallback:value;}
public delegate T InputAction<T>(InputCell<T> cell,[System.Diagnostics.CodeAnalysis.AllowNull]T value,T fallback);
public sealed class OutcomeCell<T>{public bool Try([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)]out T value){value=default!;return false;}}
public delegate bool OutcomeAction<T>(OutcomeCell<T> cell,[System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)]out T value);
public sealed class FieldCell<T>where T:class{public T? Value=default;}
#if ANNOTATED_REF
public sealed class StateBox<T>{public StateCell<T> Nested=new StateCell<T>();}
public sealed class StateCell<T>{public T? Field=default;public T? Property=>Field;[System.Diagnostics.CodeAnalysis.MemberNotNull(nameof(Field),nameof(Property))]public void Ensure(){if(Field is null||Property is null)throw new Exception();}[System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true,nameof(Field),nameof(Property))]public bool Ready=>Field is not null&&Property is not null;}
#endif
public delegate void RefSlotAction<T>([System.Diagnostics.CodeAnalysis.AllowNull][System.Diagnostics.CodeAnalysis.MaybeNull]ref T value);
public static class QueryConsumer{
  static void ClearRef<T>([System.Diagnostics.CodeAnalysis.AllowNull][System.Diagnostics.CodeAnalysis.MaybeNull]ref T value)=>value=default;
  static void StoreRef<T>(T value,[System.Diagnostics.CodeAnalysis.AllowNull][System.Diagnostics.CodeAnalysis.MaybeNull]ref T target)=>target=value;
  static void EnsureRef<T>([System.Diagnostics.CodeAnalysis.NotNull]ref T? value)where T:class{if(value is null)throw new Exception();}
  public static int ArrayRefSlot(int[] values){ClearRef(ref values[0]);return values[0];}
  public static int TupleRefSlot((int value,bool tag) pair){ClearRef(ref pair.value);return pair.Item1;}
  public static int AliasRefSlot(int[] values){ref var slot=ref values[0];ClearRef(ref slot);return values[0];}
  public static int InputRefSlot(int value,int[] target){StoreRef(value,ref target[0]);return target[0];}
  public static int DelegateRefSlot(RefSlotAction<int> clear,int[] values){clear(ref values[0]);return values[0];}
  public static string CheckedRefTuple((string? value,bool tag) pair){EnsureRef(ref pair.Item1);return pair.value;}
  public static async Task<int> AwaitRefSlot(Task<int[]> input){var values=await input;ClearRef(ref values[0]);return values[0];}
  public static async ValueTask<int> ValueAwaitRefSlot(ValueTask<int[]> input){var values=await input;ClearRef(ref values[0]);return values[0];}
  static void WriteSlot<T>([System.Diagnostics.CodeAnalysis.MaybeNull]out T value)=>value=default;
  static bool TrySlot<T>([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)]out T value){value=default!;return false;}
  public static int TuplePossibleSlot((int value,bool tag) pair){WriteSlot(out pair.value);return pair.value;}
  public static int ArrayPossibleSlot(int[] values){WriteSlot(out values[0]);return values[0];}
  public static int AliasPossibleSlot(int[] values,int fallback){ref var slot=ref values[0];return TrySlot(out slot)?values[0]:fallback;}
  public static int DictionaryPossibleSlot(Dictionary<int,int> values,int key,int[] output){values.TryGetValue(key,out output[0]);return output[0];}
  public static string TupleReferenceFill((string? value,int tag) pair,string fallback){FillField(out pair.Item1,fallback);return pair.value;}
#if ANNOTATED_REF
  static void FillSlot<T>([System.Diagnostics.CodeAnalysis.NotNull]out T? value,T fallback){if(fallback is null)throw new Exception();value=fallback;}
  public static int TupleFilledSlot((int value,bool tag) pair,int fallback){FillSlot(out pair.Item1,fallback);return pair.value;}
  public static async ValueTask<int> TupleFilledAwait(ValueTask<(int value,bool tag)> input,int fallback){var pair=await input;FillSlot(out pair.value,fallback);return pair.Item1;}
#endif
  static void FillField<T>([System.Diagnostics.CodeAnalysis.NotNull]out T? value,T fallback)where T:class{if(fallback is null)throw new Exception();value=fallback;}
  static void ClearField<T>([System.Diagnostics.CodeAnalysis.MaybeNull]out T value)=>value=default;
  public static string FieldFill(FieldCell<string> cell,string fallback){FillField(out cell.Value,fallback);return cell.Value;}
  public static string? FieldClear(FieldCell<string> cell){ClearField(out cell.Value);return cell.Value;}
#if ANNOTATED_REF
  public static int StateRead(StateCell<int> cell){cell.Ensure();return cell.Field;}
  public static int StateProperty(StateCell<int> cell){cell.Ensure();return cell.Property;}
  public static int StateConditional(StateCell<int> cell,int fallback)=>cell.Ready?cell.Field:fallback;
  public static int StateFailure(StateCell<int> cell,int fallback){if(!cell.Ready)return cell.Field;return fallback;}
  public static int StateNested(StateBox<int> box,StateCell<int> other){other.Ensure();return box.Nested.Field;}
  public static int StateOther(StateCell<int> first,StateCell<int> other){first.Ensure();return other.Field;}
  public static int StateInvalidated(StateCell<int> cell){cell.Ensure();cell.Field=default;return cell.Field;}
  public static IEnumerable<int> StateQuery(IEnumerable<StateCell<int>> cells,int fallback)=>from cell in cells select cell.Ready?cell.Field:fallback;
  public static async Task<int> StateAsync(Task<StateCell<int>> input){var cell=await input;cell.Ensure();return cell.Field;}
  public static async ValueTask<int> StateValueAsync(ValueTask<StateCell<int>> input){var cell=await input;cell.Ensure();return cell.Property;}
#endif
  public static int OutcomeRead(OutcomeCell<int> cell){cell.Try(out var value);return value;}
  public static int OutcomeSuccess(OutcomeCell<int> cell,int fallback){if(cell.Try(out var value))return value;return fallback;}
  public static int OutcomeConditional(OutcomeCell<int> cell,int fallback)=>cell.Try(out var value)?value:fallback;
  public static int OutcomeDelegate(OutcomeAction<int> action,OutcomeCell<int> cell){action(cell,out var value);return value;}
  public static async Task<int> OutcomeAsync(Task<OutcomeCell<int>> input){var cell=await input;cell.Try(out var value);return value;}
  public static async ValueTask<int> OutcomeValueAsync(ValueTask<OutcomeCell<int>> input){var cell=await input;cell.Try(out var value);return value;}
  static T AcceptInput<T>(InputCell<T> cell,[System.Diagnostics.CodeAnalysis.AllowNull]T value,T fallback)=>cell.Value=value is null?fallback:value;
  public static string InputRead(InputCell<string> cell,string? value,string fallback)=>cell.Accept(value,fallback);
  public static int InputValue(InputCell<int> cell,int value,int fallback)=>AcceptInput(cell,value,fallback);
  public static string InputDelegate(InputAction<string> put,InputCell<string> cell,string? value,string fallback)=>put(cell,value,fallback);
  public static async Task<string> InputAsync(InputCell<string> cell,Task<string?> value,string fallback)=>cell.Accept(await value,fallback);
  public static async ValueTask<string> InputValueAsync(InputCell<string> cell,ValueTask<string?> value,string fallback)=>cell.Accept(await value,fallback);
  public static int MemberRead(MemberContractCell<int> cell)=>cell.Value;
  public static int MemberField(MemberContractCell<int> cell)=>cell.Field;
  public static int MemberAccessor(MemberContractCell<int> cell)=>cell.Accessor;
  public static int MemberIndexer(MemberContractCell<int> cell)=>cell[0];
  public static async Task<int> MemberAsync(Task<MemberContractCell<int>> cell)=>(await cell).Value;
  public static async ValueTask<int> MemberValueAsync(ValueTask<MemberContractCell<int>> cell)=>(await cell)[0];
  public static string MemberRequired(RequiredMemberCell<string> cell)=>cell.Value;
  static ref T Borrow<T>(ref T value)=>ref value;
  static ref T ArrayBorrow<T>(T[] value)=>ref value[0];
  public static IEnumerable<(int left,int right)> ConditionalMixed(IEnumerable<(int[] first,int[] second)> values,bool clear)=>from pair in values select (Borrow(ref pair.first[0]),ArrayBorrow(pair.second))=clear?(default,default):(pair.first[0],pair.second[0]);
  public static IEnumerable<(int left,int right)> SwitchMixed(IEnumerable<(int[] first,int[] second)> values,bool clear)=>from pair in values select (Borrow(ref pair.first[0]),ArrayBorrow(pair.second))=clear switch{true=>(default,default),false=>(pair.first[0],pair.second[0])};
  public static async ValueTask<IEnumerable<(int left,int right)>> ConditionalMixedAsync(ValueTask<IEnumerable<(int[] first,int[] second)>> values,bool clear)=>from pair in await values select (Borrow(ref pair.first[0]),ArrayBorrow(pair.second))=clear?(default,default):(pair.first[0],pair.second[0]);
  public static (int left,int right) ConditionalArrayStores(int[] first,int[] second,bool clear){(first[0],second[0])=clear?(default,default):(first[0],second[0]);return(first[0],second[0]);}
  [return:System.Diagnostics.CodeAnalysis.MaybeNull][return:System.Diagnostics.CodeAnalysis.NotNullIfNotNull("guard")]static T Conditional<T>(T value,object? guard)=>guard is null?default:(value is null?throw new ArgumentNullException(nameof(value)):value);
  [return:System.Diagnostics.CodeAnalysis.MaybeNull][return:System.Diagnostics.CodeAnalysis.NotNullIfNotNull("guard")]static T ConditionalDefault<T>(T value,[System.Diagnostics.CodeAnalysis.AllowNull]T guard=default)=>guard is null?default:(value is null?throw new ArgumentNullException(nameof(value)):value);
  [return:System.Diagnostics.CodeAnalysis.MaybeNull][return:System.Diagnostics.CodeAnalysis.NotNullIfNotNull("guard")]public delegate T ConditionalAction<T>(T value,object? guard);
  public static int ConditionalKnown(int value,bool tag)=>Conditional(value,"ready");
  public static int ConditionalUnknown(int value,object? guard,bool tag)=>Conditional(value,guard);
  public static int ConditionalDefaultValue(int value,bool tag)=>ConditionalDefault(value);
  public static int ConditionalDelegate(ConditionalAction<int> action,int value,bool tag)=>action(value,"ready");
  public static async Task<int> ConditionalAsync(Task<int> value,bool tag)=>Conditional(await value,"ready");
  [return:System.Diagnostics.CodeAnalysis.MaybeNull]static T ContractMaybe<T>(T value)=>value;
  [return:System.Diagnostics.CodeAnalysis.NotNull]static T? ContractRequired<T>(T? value)where T:class=>value!;
  [return:System.Diagnostics.CodeAnalysis.MaybeNull]public delegate T ContractAction<T>(T value);
  public static string? ContractReference(string value,bool tag)=>ContractMaybe(value);
  public static int ContractValue(int value,bool tag)=>ContractMaybe(value);
  public static int[]? ContractArray(int[] value,bool tag)=>ContractMaybe(value);
  public static async Task<int> ContractAsync(Task<int> value,bool tag)=>ContractMaybe(await value);
  public static int ContractDelegate(ContractAction<int> action,int value,bool tag)=>action(value);
  public static string ContractRequire(string? value,bool tag)=>ContractRequired(value);
  static Func<T> Capture<T>(Func<T> read)=>read;
  public static Func<int> TupleCapture(int value,bool other){var read=Capture(()=>value);(value,other)=(default,default);return read;}
  public static (Func<int> read,Action clear) TupleDeferred(int value,bool other){var read=Capture(()=>value);Action clear=()=>{(value,other)=(default,default);};return(read,clear);}
  public static async Task<Func<int>> TupleTaskCapture(Task<int> value,bool other){var slot=await value;var read=Capture(()=>slot);(slot,other)=(default,default);return read;}
  public static async Task<(int copy,int stored)> GroupedAsync(Task<int> value){var result=await value;var copy=result;((result))=default;return(copy,result);}
  public static async Task<(int left,int right)> TupleAsync(Task<int[]> first,int[] second){var result=await first;(Borrow(ref result[0]),ArrayBorrow(second))=(default,default);return(result[0],second[0]);}
  public static async ValueTask<IEnumerable<(int left,int right)>> MixedAsync(ValueTask<IEnumerable<(int[] first,int[] second)>> values)=>from pair in await values select (Borrow(ref pair.first[0]),ArrayBorrow(pair.second))=(default,default);
  public static IEnumerable<int> ArrayRefStore(IEnumerable<int[]> values)=>from row in values select ArrayBorrow(row)=default;
  public static IEnumerable<int> RefStore(IEnumerable<int[]> values)=>from row in values select Borrow(ref row[0])=default;
  public static IEnumerable<string> RefSibling(IEnumerable<(int[] row,string tag)> values)=>from pair in values select(Borrow(ref pair.row[0])=default,pair.tag).Item2;
  public static int RefBlock(int[] values){values[0]=default;Borrow(ref values[0])=default;return values[0];}
  public static int RefParameter(ref int value){Borrow(ref value)=default;return value;}
  public static async Task<IEnumerable<int>> RefAsync(Task<IEnumerable<int[]>> values)=>from row in await values select Borrow(ref row[0])=default;
#if ANNOTATED_REF
  #pragma warning disable CS8601
  public static IEnumerable<int> RefStoreSuppressed(IEnumerable<int[]> values)=>from row in values select Borrow(ref row[0])=default;
  #pragma warning restore CS8601
  static ref T? BorrowNullable<T>(ref T? value)=>ref value;
  public static IEnumerable<int> NullableRefStore(IEnumerable<int[]> values)=>from row in values select BorrowNullable(ref row[0])=default;
  public static async ValueTask<IEnumerable<int>> NullableRefAsync(ValueTask<IEnumerable<int[]>> values)=>from row in await values select BorrowNullable(ref row[0])=default;
#endif
  public static IQueryable<int> QuotedDefault(IQueryable<int> values,bool flag,int tag)=>from x in values select flag?x:default;
  public static IEnumerable<int> DefaultJoin(IEnumerable<int> values,bool flag,int tag)=>from x in values select flag?x:default;
  public static IEnumerable<string?> NullJoin(IEnumerable<string> values,bool flag,int tag)=>from x in values select flag?x:null;
  public static IEnumerable<int> Coalesce(IEnumerable<int?> values,int fallback,bool tag)=>from x in values select x??fallback;
  public static IEnumerable<int> Identity(IEnumerable<int> values,bool tag)=>from x in values select x;
  public static IEnumerable<int> Where(IEnumerable<int> values,bool tag)=>from x in values where true select x;
  public static IEnumerable<int> Numeric(IEnumerable<int> values,bool tag)=>from x in values where x>0 select x;
  public static IEnumerable<(int left,int right)> Let(IEnumerable<int> values,bool tag)=>from x in values let pair=(x,x) select pair;
  public static IEnumerable<(int left,string right)> Many(IEnumerable<int> values,IEnumerable<string> other,bool tag)=>from x in values from y in other select(x,y);
  public static IEnumerable<int> Order(IEnumerable<int> values,bool tag)=>from x in values orderby x descending,x select x;
  public static IEnumerable<int> Continue(IEnumerable<int> values,bool tag)=>from x in values select(x,x) into pair select pair.Item2;
  public static IEnumerable<(string left,bool right)> Join(IEnumerable<(int key,string value)> left,IEnumerable<(int key,bool value)> right,int tag)=>from l in left join r in right on l.key equals r.key select(l.value,r.value);
  public static IEnumerable<IEnumerable<int>> GroupJoin(IEnumerable<int> left,IEnumerable<int> right,bool tag)=>from l in left join r in right on l equals r into grouped select grouped;
  public static IEnumerable<IGrouping<int,string>> Group(IEnumerable<(int key,string value)> values,int tag)=>from x in values group x.value by x.key;
  public static IEnumerable<IGrouping<int,int>> GroupContinue(IEnumerable<int> values,bool tag)=>from x in values group x by x into g select g;
  public static IEnumerable<int> Typed(IEnumerable<object> values,bool tag)=>from int x in values select x;
  public static IQueryable<int> Quoted(IQueryable<int> values,bool tag)=>from x in values select x;
  public static async Task<IEnumerable<int>> Async(Task<IEnumerable<int>> values,bool tag)=>from x in await values select x;
  public static QueryBox<int> Custom(QueryBox<int> values,bool tag)=>from x in values select x;
}
public sealed class QuerySeq<T>{public IEnumerable<T> Values;public QuerySeq(IEnumerable<T> values){Values=values;}}
public static class QueryOperators{
  public static IEnumerable<bool> Select(this QuerySeq<int> values,Func<int,bool> selector){IEnumerable<bool> result=values.Values.Select(selector);return result;}
  public static IEnumerable<U> Select<T,U>(this QuerySeq<T> values,Func<T,U> selector)=>values.Values.Select(selector);
}
public static class QueryBindingConsumer{public static IEnumerable<bool> Read(QuerySeq<string> values)=>from x in values select true;}
EOF
  cat > "$fixture/.editorconfig" << 'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Queries.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || {
      tail -35 "$fixture/restore.log"
      exit 1
    }
    NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Queries.csproj" --no-restore > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    log="$fixture/build.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/package-queries-$language.txt
  else
    dotnet build "$fixture/Queries.csproj" > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    log="$fixture/cli.log"
    dotnet "$output" --project "$fixture/Queries.csproj" > "$log"
    destination=/workspace/SharpProof/artifacts/hm-inference/queries-$language.txt
  fi
  cp "$log" "$destination"
  for expected in \
    'global::System.Collections.Generic.IEnumerable<T1> RefStore<T1>(global::System.Collections.Generic.IEnumerable<T1[]> values) where T1 : struct' \
    'global::System.Collections.Generic.IEnumerable<T2> RefSibling<T1, T2>(global::System.Collections.Generic.IEnumerable<(T1[] row, T2 tag)> values) where T1 : struct' \
    'global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<T1>> RefAsync<T1>(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<T1[]>> values) where T1 : struct' \
    'global::System.Collections.Generic.IEnumerable<T1?> NullJoin<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, bool flag, T2 tag) where T1 : class' \
    'global::System.Collections.Generic.IEnumerable<T1> Coalesce<T1, T2>(global::System.Collections.Generic.IEnumerable<T1?> values, T1 fallback, T2 tag) where T1 : struct' \
    'global::System.Collections.Generic.IEnumerable<T1> Identity<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)' \
    'global::System.Collections.Generic.IEnumerable<T1> Where<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)' \
    'global::System.Collections.Generic.IEnumerable<int> Numeric<T1>(global::System.Collections.Generic.IEnumerable<int> values, T1 tag)' \
    'global::System.Collections.Generic.IEnumerable<(T1 left, T1 right)> Let<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)' \
    'global::System.Collections.Generic.IEnumerable<(T1 left, T2 right)> Many<T1, T2, T3>(global::System.Collections.Generic.IEnumerable<T1> values, global::System.Collections.Generic.IEnumerable<T2> other, T3 tag)' \
    'global::System.Collections.Generic.IEnumerable<T1> Order<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)' \
    'global::System.Collections.Generic.IEnumerable<T1> Continue<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)' \
    'global::System.Collections.Generic.IEnumerable<(T2 left, T3 right)> Join<T1, T2, T3, T4>(global::System.Collections.Generic.IEnumerable<(T1 key, T2 value)> left, global::System.Collections.Generic.IEnumerable<(T1 key, T3 value)> right, T4 tag)' \
    'global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.IEnumerable<T1>> GroupJoin<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> left, global::System.Collections.Generic.IEnumerable<T1> right, T2 tag)' \
    'global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<T1, T2>> Group<T1, T2, T3>(global::System.Collections.Generic.IEnumerable<(T1 key, T2 value)> values, T3 tag)' \
    'global::System.Collections.Generic.IEnumerable<global::System.Linq.IGrouping<T1, T1>> GroupContinue<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)' \
    'global::System.Collections.Generic.IEnumerable<int> Typed<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, T2 tag)' \
    'global::System.Linq.IQueryable<T1> Quoted<T1, T2>(global::System.Linq.IQueryable<T1> values, T2 tag)' \
    'global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<T1>> Async<T1, T2>(global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<T1>> values, T2 tag)' \
    'global::QueryBox<T1> Custom<T1, T2>(global::QueryBox<T1> values, T2 tag)'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing query signature (C# %s): %s\n' "$language" "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
  if grep -Fq 'IEnumerable<bool> Select<T1>(' "$log"; then
    printf 'Query operator rebinding was suggested (C# %s)\n' "$language"
    exit 1
  fi
  grep -Fq 'T1 ConditionalKnown<T1, T2>(T1 value, T2 tag)' "$log"
  grep -Fq 'T1 ConditionalDelegate<T1, T2>(global::QueryConsumer.ConditionalAction<T1> action, T1 value, T2 tag)' "$log"
  grep -Fq 'global::System.Threading.Tasks.Task<T1> ConditionalAsync<T1, T2>(global::System.Threading.Tasks.Task<T1> value, T2 tag)' "$log"
  grep -Fq 'T1[]? ContractArray<T1, T2>(T1[] value, T2 tag)' "$log"
  grep -Fq 'T1 ContractRequire<T1, T2>(T1? value, T2 tag) where T1 : class' "$log"
  grep -Fq 'T1 CheckedRefTuple<T1, T2>((T1? value, T2 tag) pair) where T1 : class' "$log"
  grep -Fq 'T1 TupleReferenceFill<T1, T2>((T1? value, T2 tag) pair, T1 fallback) where T1 : class' "$log"
  grep -Fq 'T1 FieldFill<T1>(global::FieldCell<T1> cell, T1 fallback) where T1 : class' "$log"
  grep -Fq 'T1? FieldClear<T1>(global::FieldCell<T1> cell) where T1 : class' "$log"
  if [ "$language" != 8 ]; then
    grep -Fq 'T1 StateRead<T1>(global::StateCell<T1> cell)' "$log"
    grep -Fq 'T1 StateProperty<T1>(global::StateCell<T1> cell)' "$log"
    grep -Fq 'T1 StateConditional<T1>(global::StateCell<T1> cell, T1 fallback)' "$log"
    grep -Fq 'T1? StateFailure<T1>(global::StateCell<T1> cell, T1 fallback)' "$log"
    grep -Fq 'T1? StateNested<T1, T2>(global::StateBox<T1> box, global::StateCell<T2> other)' "$log"
    grep -Fq 'T2? StateOther<T1, T2>(global::StateCell<T1> first, global::StateCell<T2> other)' "$log"
    grep -Fq 'T1? StateInvalidated<T1>(global::StateCell<T1> cell)' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<T1> StateQuery<T1>(global::System.Collections.Generic.IEnumerable<global::StateCell<T1>> cells, T1 fallback)' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<T1> StateAsync<T1>(global::System.Threading.Tasks.Task<global::StateCell<T1>> input)' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<T1> StateValueAsync<T1>(global::System.Threading.Tasks.ValueTask<global::StateCell<T1>> input)' "$log"
    if grep -E ' (StateRead|StateProperty|StateConditional|StateQuery|StateAsync|StateValueAsync)<T1>\(' "$log" | grep -Fq 'where T1'; then
      printf 'Member postconditions gained an unnecessary bound (C# %s)\n' "$language"
      exit 1
    fi
  fi
  grep -Fq 'T1 OutcomeSuccess<T1>(global::OutcomeCell<T1> cell, T1 fallback)' "$log"
  grep -Fq 'T1 OutcomeConditional<T1>(global::OutcomeCell<T1> cell, T1 fallback)' "$log"
  if grep -E 'T1 Outcome(Success|Conditional)<T1>\(' "$log" | grep -Fq 'where T1'; then
    printf 'Successful output reads gained an unnecessary bound (C# %s)\n' "$language"
    exit 1
  fi
  grep -Fq 'T1 InputRead<T1>(global::InputCell<T1> cell, T1? value, T1 fallback) where T1 : class' "$log"
  grep -Fq 'T1 InputValue<T1>(global::InputCell<T1> cell, T1 value, T1 fallback)' "$log"
  if grep -F 'T1 InputValue<T1>(' "$log" | grep -Fq 'where T1'; then
    printf 'AllowNull unnecessarily constrained an ordinary input (C# %s)\n' "$language"
    exit 1
  fi
  grep -Fq 'T1 InputDelegate<T1>(global::InputAction<T1> put, global::InputCell<T1> cell, T1? value, T1 fallback) where T1 : class' "$log"
  grep -Fq 'global::System.Threading.Tasks.Task<T1> InputAsync<T1>(global::InputCell<T1> cell, global::System.Threading.Tasks.Task<T1?> value, T1 fallback) where T1 : class' "$log"
  grep -Fq 'global::System.Threading.Tasks.ValueTask<T1> InputValueAsync<T1>(global::InputCell<T1> cell, global::System.Threading.Tasks.ValueTask<T1?> value, T1 fallback) where T1 : class' "$log"
  grep -Fq 'T1 MemberRequired<T1>(global::RequiredMemberCell<T1> cell) where T1 : class' "$log"
  if [ "$language" = 8 ]; then
    for method in ArrayRefSlot AliasRefSlot; do
      grep -Fq "T1 $method<T1>(T1[] values) where T1 : struct" "$log"
    done
    grep -Fq 'T1 TupleRefSlot<T1, T2>((T1 value, T2 tag) pair) where T1 : struct' "$log"
    grep -Fq 'T1 InputRefSlot<T1>(T1 value, T1[] target) where T1 : struct' "$log"
    grep -Fq 'T1 DelegateRefSlot<T1>(global::RefSlotAction<T1> clear, T1[] values) where T1 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<T1> AwaitRefSlot<T1>(global::System.Threading.Tasks.Task<T1[]> input) where T1 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<T1> ValueAwaitRefSlot<T1>(global::System.Threading.Tasks.ValueTask<T1[]> input) where T1 : struct' "$log"
    grep -Fq 'T1 TuplePossibleSlot<T1, T2>((T1 value, T2 tag) pair) where T1 : struct' "$log"
    grep -Fq 'T1 ArrayPossibleSlot<T1>(T1[] values) where T1 : struct' "$log"
    grep -Fq 'T1 AliasPossibleSlot<T1>(T1[] values, T1 fallback) where T1 : struct' "$log"
    grep -Fq 'T2 DictionaryPossibleSlot<T1, T2>(global::System.Collections.Generic.Dictionary<T1, T2> values, T1 key, T2[] output) where T1 : notnull where T2 : struct' "$log"
    grep -Fq 'T1 OutcomeRead<T1>(global::OutcomeCell<T1> cell) where T1 : struct' "$log"
    grep -Fq 'T1 OutcomeDelegate<T1>(global::OutcomeAction<T1> action, global::OutcomeCell<T1> cell) where T1 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<T1> OutcomeAsync<T1>(global::System.Threading.Tasks.Task<global::OutcomeCell<T1>> input) where T1 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<T1> OutcomeValueAsync<T1>(global::System.Threading.Tasks.ValueTask<global::OutcomeCell<T1>> input) where T1 : struct' "$log"
    for member in MemberRead MemberField MemberAccessor MemberIndexer; do
      grep -Fq "T1 $member<T1>(global::MemberContractCell<T1> cell) where T1 : struct" "$log"
    done
    grep -Fq 'global::System.Threading.Tasks.Task<T1> MemberAsync<T1>(global::System.Threading.Tasks.Task<global::MemberContractCell<T1>> cell) where T1 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<T1> MemberValueAsync<T1>(global::System.Threading.Tasks.ValueTask<global::MemberContractCell<T1>> cell) where T1 : struct' "$log"
    grep -Fq 'T1 ConditionalUnknown<T1, T2, T3>(T1 value, T2? guard, T3 tag) where T1 : struct where T2 : class' "$log"
    grep -Fq 'T1 ConditionalDefaultValue<T1, T2>(T1 value, T2 tag) where T1 : struct' "$log"
    grep -Fq 'T1? ContractReference<T1, T2>(T1 value, T2 tag) where T1 : class' "$log"
    grep -Fq 'T1 ContractValue<T1, T2>(T1 value, T2 tag) where T1 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<T1> ContractAsync<T1, T2>(global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : struct' "$log"
    grep -Fq 'T1 ContractDelegate<T1, T2>(global::QueryConsumer.ContractAction<T1> action, T1 value, T2 tag) where T1 : struct' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<(T1 left, T2 right)> ConditionalMixed<T1, T2>(global::System.Collections.Generic.IEnumerable<(T1[] first, T2[] second)> values, bool clear) where T1 : struct where T2 : struct' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<(T1 left, T2 right)> SwitchMixed<T1, T2>(global::System.Collections.Generic.IEnumerable<(T1[] first, T2[] second)> values, bool clear) where T1 : struct where T2 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<(T1 left, T2 right)>> ConditionalMixedAsync<T1, T2>(global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<(T1[] first, T2[] second)>> values, bool clear) where T1 : struct where T2 : struct' "$log"
    grep -Fq '(T1 left, T2 right) ConditionalArrayStores<T1, T2>(T1[] first, T2[] second, bool clear) where T1 : struct where T2 : struct' "$log"
    grep -Fq 'global::System.Func<T1> TupleCapture<T1, T2>(T1 value, T2 other) where T1 : struct where T2 : struct' "$log"
    grep -Fq '(global::System.Func<T1> read, global::System.Action clear) TupleDeferred<T1, T2>(T1 value, T2 other) where T1 : struct where T2 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<global::System.Func<T1>> TupleTaskCapture<T1, T2>(global::System.Threading.Tasks.Task<T1> value, T2 other) where T1 : struct where T2 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<(T1 copy, T1 stored)> GroupedAsync<T1>(global::System.Threading.Tasks.Task<T1> value) where T1 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<(T1 left, T2 right)> TupleAsync<T1, T2>(global::System.Threading.Tasks.Task<T1[]> first, T2[] second) where T1 : struct where T2 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<(T1 left, T2 right)>> MixedAsync<T1, T2>(global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<(T1[] first, T2[] second)>> values) where T1 : struct where T2 : struct' "$log"
    grep -Fq 'T1 RefBlock<T1>(T1[] values) where T1 : struct' "$log"
    grep -Fq 'T1 RefParameter<T1>(ref T1 value) where T1 : struct' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<T1> ArrayRefStore<T1>(global::System.Collections.Generic.IEnumerable<T1[]> values) where T1 : struct' "$log"
    grep -Fq 'global::System.Linq.IQueryable<T1> QuotedDefault<T1, T2>(global::System.Linq.IQueryable<T1> values, bool flag, T2 tag) where T1 : struct' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<T1> DefaultJoin<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, bool flag, T2 tag) where T1 : struct' "$log"
  else
    for method in ArrayRefSlot AliasRefSlot; do
      grep -Fq "T1? $method<T1>(T1?[] values)" "$log"
    done
    grep -Fq 'T1? TupleRefSlot<T1, T2>((T1? value, T2 tag) pair)' "$log"
    grep -Fq 'T1? InputRefSlot<T1>(T1 value, T1?[] target)' "$log"
    grep -Fq 'T1? DelegateRefSlot<T1>(global::RefSlotAction<T1> clear, T1?[] values)' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<T1?> AwaitRefSlot<T1>(global::System.Threading.Tasks.Task<T1?[]> input)' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<T1?> ValueAwaitRefSlot<T1>(global::System.Threading.Tasks.ValueTask<T1?[]> input)' "$log"
    grep -Fq 'T1? TuplePossibleSlot<T1, T2>((T1? value, T2 tag) pair)' "$log"
    grep -Fq 'T1? ArrayPossibleSlot<T1>(T1?[] values)' "$log"
    grep -Fq 'T1? AliasPossibleSlot<T1>(T1?[] values, T1 fallback)' "$log"
    grep -Fq 'T2? DictionaryPossibleSlot<T1, T2>(global::System.Collections.Generic.Dictionary<T1, T2> values, T1 key, T2?[] output) where T1 : notnull' "$log"
    grep -Fq 'T1 TupleFilledSlot<T1, T2>((T1? value, T2 tag) pair, T1 fallback)' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<T1> TupleFilledAwait<T1, T2>(global::System.Threading.Tasks.ValueTask<(T1? value, T2 tag)> input, T1 fallback)' "$log"
    grep -Fq 'T1? OutcomeRead<T1>(global::OutcomeCell<T1> cell)' "$log"
    grep -Fq 'T1? OutcomeDelegate<T1>(global::OutcomeAction<T1> action, global::OutcomeCell<T1> cell)' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<T1?> OutcomeAsync<T1>(global::System.Threading.Tasks.Task<global::OutcomeCell<T1>> input)' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<T1?> OutcomeValueAsync<T1>(global::System.Threading.Tasks.ValueTask<global::OutcomeCell<T1>> input)' "$log"
    for member in MemberRead MemberField MemberAccessor MemberIndexer; do
      grep -Fq "T1? $member<T1>(global::MemberContractCell<T1> cell)" "$log"
    done
    grep -Fq 'global::System.Threading.Tasks.Task<T1?> MemberAsync<T1>(global::System.Threading.Tasks.Task<global::MemberContractCell<T1>> cell)' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<T1?> MemberValueAsync<T1>(global::System.Threading.Tasks.ValueTask<global::MemberContractCell<T1>> cell)' "$log"
    grep -Fq 'T1? ConditionalUnknown<T1, T2, T3>(T1 value, T2? guard, T3 tag) where T2 : class' "$log"
    grep -Fq 'T1? ConditionalDefaultValue<T1, T2>(T1 value, T2 tag)' "$log"
    grep -Fq 'T1? ContractReference<T1, T2>(T1 value, T2 tag)' "$log"
    grep -Fq 'T1? ContractValue<T1, T2>(T1 value, T2 tag)' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<T1?> ContractAsync<T1, T2>(global::System.Threading.Tasks.Task<T1> value, T2 tag)' "$log"
    grep -Fq 'T1? ContractDelegate<T1, T2>(global::QueryConsumer.ContractAction<T1> action, T1 value, T2 tag)' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<(T1 left, T2? right)> ConditionalMixed<T1, T2>(global::System.Collections.Generic.IEnumerable<(T1[] first, T2?[] second)> values, bool clear) where T1 : struct' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<(T1 left, T2? right)> SwitchMixed<T1, T2>(global::System.Collections.Generic.IEnumerable<(T1[] first, T2?[] second)> values, bool clear) where T1 : struct' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<(T1 left, T2? right)>> ConditionalMixedAsync<T1, T2>(global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<(T1[] first, T2?[] second)>> values, bool clear) where T1 : struct' "$log"
    grep -Fq '(T1? left, T2? right) ConditionalArrayStores<T1, T2>(T1?[] first, T2?[] second, bool clear)' "$log"
    grep -Fq 'global::System.Func<T1?> TupleCapture<T1, T2>(T1? value, T2? other)' "$log"
    grep -Fq '(global::System.Func<T1?> read, global::System.Action clear) TupleDeferred<T1, T2>(T1? value, T2? other)' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<global::System.Func<T1?>> TupleTaskCapture<T1, T2>(global::System.Threading.Tasks.Task<T1> value, T2? other)' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<(T1 copy, T1? stored)> GroupedAsync<T1>(global::System.Threading.Tasks.Task<T1> value)' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<(T1? left, T2? right)> TupleAsync<T1, T2>(global::System.Threading.Tasks.Task<T1?[]> first, T2?[] second)' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<(T1 left, T2? right)>> MixedAsync<T1, T2>(global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<(T1[] first, T2?[] second)>> values) where T1 : struct' "$log"
    grep -Fq 'T1? RefBlock<T1>(T1?[] values)' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<T1?> RefStoreSuppressed<T1>(global::System.Collections.Generic.IEnumerable<T1?[]> values)' "$log"
    grep -Fq 'T1? RefParameter<T1>(ref T1? value)' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<T1?> ArrayRefStore<T1>(global::System.Collections.Generic.IEnumerable<T1?[]> values)' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<T1?> NullableRefStore<T1>(global::System.Collections.Generic.IEnumerable<T1?[]> values)' "$log"
    grep -Fq 'global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<T1?>> NullableRefAsync<T1>(global::System.Threading.Tasks.ValueTask<global::System.Collections.Generic.IEnumerable<T1?[]>> values)' "$log"
    grep -Fq 'global::System.Linq.IQueryable<T1?> QuotedDefault<T1, T2>(global::System.Linq.IQueryable<T1> values, bool flag, T2 tag)' "$log"
    grep -Fq 'global::System.Collections.Generic.IEnumerable<T1?> DefaultJoin<T1, T2>(global::System.Collections.Generic.IEnumerable<T1> values, bool flag, T2 tag)' "$log"
  fi
  if [ "$#" -eq 1 ]; then printf 'PASS CliQueriesCSharp%s\n' "$language"; fi
done
