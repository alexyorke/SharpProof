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
public static class QueryConsumer{
  static ref T Borrow<T>(ref T value)=>ref value;
  static ref T ArrayBorrow<T>(T[] value)=>ref value[0];
  public static IEnumerable<(int left,int right)> ConditionalMixed(IEnumerable<(int[] first,int[] second)> values,bool clear)=>from pair in values select (Borrow(ref pair.first[0]),ArrayBorrow(pair.second))=clear?(default,default):(pair.first[0],pair.second[0]);
  public static IEnumerable<(int left,int right)> SwitchMixed(IEnumerable<(int[] first,int[] second)> values,bool clear)=>from pair in values select (Borrow(ref pair.first[0]),ArrayBorrow(pair.second))=clear switch{true=>(default,default),false=>(pair.first[0],pair.second[0])};
  public static async ValueTask<IEnumerable<(int left,int right)>> ConditionalMixedAsync(ValueTask<IEnumerable<(int[] first,int[] second)>> values,bool clear)=>from pair in await values select (Borrow(ref pair.first[0]),ArrayBorrow(pair.second))=clear?(default,default):(pair.first[0],pair.second[0]);
  public static (int left,int right) ConditionalArrayStores(int[] first,int[] second,bool clear){(first[0],second[0])=clear?(default,default):(first[0],second[0]);return(first[0],second[0]);}
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
  if [ "$language" = 8 ]; then
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
