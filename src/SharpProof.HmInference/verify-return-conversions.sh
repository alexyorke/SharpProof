#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-return-conversions.XXXXXX)
  cp global.json "$fixture/global.json"
  package_reference=""
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$fixture/Returns.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" << 'EOF'
#nullable enable
using System;using System.Collections.Generic;using System.Linq;using System.Threading;using System.Threading.Tasks;
public class ReturnBase{}
public sealed class ReturnDerived:ReturnBase{}
public sealed class ReturnOtherDerived:ReturnBase{}
public class ReturnBase<T>{public T Value;public ReturnBase(T value){Value=value;}}
public sealed class ReturnDerived<T>:ReturnBase<T>{public ReturnDerived(T value):base(value){}}
public static class ReturnConsumer{
    public static object Box(int value)=>value;
    public static object? Optional(int value)=>value;
    public static object? Nullable(int? value)=>value;
    public static IComparable Interface(int value)=>value;
    public static IComparable? NullableInterface(int? value)=>value;
    public static ValueType Value(int value)=>value;
    public static Enum Enum(DayOfWeek value)=>value;
    public static ReturnBase Base(ReturnDerived value)=>value;
    public static ReturnBase<int> Generic(ReturnDerived<int> value)=>value;
    public static IEnumerable<int> Array(int[] value)=>value;
    public static IEnumerable<int> List(List<int> value)=>value;
    public static object[] ArrayCovariance(string[] value)=>value;
    public static IEnumerable<object> InterfaceCovariance(IEnumerable<string> value)=>value;
    public static Func<object> DelegateCovariance(Func<string> value)=>value;
    public static long Numeric(int value,int tag)=>value;
    public static long? NullableNumeric(int? value,int tag)=>value;
    public static (object value,long number) Tuple(int value,int number)=>(value,number);
    public static object Local(int value,int tag){object Read()=>value;return Read();}
    public static Func<object> Lambda(int value,int tag)=>()=>value;
    public static object FixedLambda(int value,int tag){Func<object> read=()=>value;return read();}
    public static IEnumerable<object> Iterator(int value,int tag){yield return value;}
    public static async IAsyncEnumerable<object> AsyncIterator(int value,int tag){await Task.Yield();yield return value;}
    public static async Task<object> AsyncBox(Task<int> value)=>await value;
    public static async ValueTask<object?> AsyncNullable(ValueTask<int?> value)=>await value;
}
EOF
  cat > "$fixture/.editorconfig" << 'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Returns.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || {
      tail -35 "$fixture/restore.log"
      exit 1
    }
    NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Returns.csproj" --no-restore > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    log="$fixture/build.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/package-return-conversions-$language.txt
  else
    dotnet build "$fixture/Returns.csproj" > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    dotnet "$output" --project "$fixture/Returns.csproj" --no-restore > "$fixture/cli.log" 2>&1 || {
      tail -35 "$fixture/cli.log"
      exit 1
    }
    log="$fixture/cli.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/cli-return-conversions-$language.txt
  fi
  cp "$log" "$destination"
  for expected in \
    'object Box<T1>(T1 value) where T1 : notnull' \
    'object? Optional<T1>(T1 value)' \
    'object? Nullable<T1>(T1? value) where T1 : struct' \
    'global::System.IComparable Interface<T1>(T1 value) where T1 : global::System.IComparable' \
    'global::System.IComparable? NullableInterface<T1>(T1? value) where T1 : struct, global::System.IComparable' \
    'global::System.ValueType Value<T1>(T1 value) where T1 : struct' \
    'global::System.Enum Enum<T1>(T1 value) where T1 : global::System.Enum' \
    'global::ReturnBase Base<T1>(T1 value) where T1 : global::ReturnBase' \
    'global::ReturnBase<T1> Generic<T1>(global::ReturnDerived<T1> value)' \
    'global::System.Collections.Generic.IEnumerable<T1> Array<T1>(T1[] value)' \
    'global::System.Collections.Generic.IEnumerable<T1> List<T1>(global::System.Collections.Generic.List<T1> value)' \
    'object[] ArrayCovariance<T1>(T1[] value) where T1 : class' \
    'global::System.Collections.Generic.IEnumerable<object> InterfaceCovariance<T1>(global::System.Collections.Generic.IEnumerable<T1> value) where T1 : class' \
    'global::System.Func<object> DelegateCovariance<T1>(global::System.Func<T1> value) where T1 : class' \
    'long Numeric<T1>(int value, T1 tag)' \
    'long? NullableNumeric<T1>(int? value, T1 tag)' \
    '(object value, long number) Tuple<T1>(T1 value, int number) where T1 : notnull' \
    'object Local<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'global::System.Func<object> Lambda<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'object FixedLambda<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'global::System.Collections.Generic.IEnumerable<object> Iterator<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'global::System.Collections.Generic.IAsyncEnumerable<object> AsyncIterator<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'global::System.Threading.Tasks.Task<object> AsyncBox<T1>(global::System.Threading.Tasks.Task<T1> value) where T1 : notnull' \
    'global::System.Threading.Tasks.ValueTask<object?> AsyncNullable<T1>(global::System.Threading.Tasks.ValueTask<T1?> value) where T1 : struct'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing return-conversion signature (C# %s): %s\n' "$language" "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
  if [ "$#" -eq 1 ]; then printf 'PASS CliReturnConversionsCSharp%s\n' "$language"; fi
done
