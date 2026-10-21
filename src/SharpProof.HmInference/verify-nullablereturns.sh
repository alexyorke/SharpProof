#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-nullable-returns.XXXXXX)
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
using System;
using System.Threading.Tasks;
public static class NullableReturnConsumer{
    public delegate ref object? RefCallback();
    public class Box<T>{public T Value=default!;}
    public class Base{}
    public static object Local(bool flag,object value,int tag){object? Get(){if(flag)return value;return null;}_=Get();return value;}
    public static object Lambda(bool flag,object value,int tag){Func<object?> callback=()=>{if(flag)return value;return null;};_=callback();return value;}
    public static object Anonymous(bool flag,object value,int tag){Func<object?> callback=delegate{if(flag)return value;return null;};_=callback();return value;}
    public static (object,object) Independent(bool flag,object left,object right,int tag){Func<object?> callback=()=>{if(flag)return left;return right;};_=callback();return(left,right);}
    public static object Iterator(object value,int tag){System.Collections.Generic.IEnumerable<object?> Get(){yield return value;yield return null;}foreach(var item in Get()){}return value;}
    public static object AsyncIterator(object value,int tag){async System.Collections.Generic.IAsyncEnumerable<object?> Get(){await Task.Yield();yield return value;yield return null;}_=Get();return value;}
    public static object? Narrow(object? value,int tag){Func<object> callback=()=>{if(value is null)return new object();return value;};_=callback();return value;}
    public static object Tuple(bool flag,object value,int tag){(object?,int) Get(){if(flag)return(value,1);return(null,2);}_=Get();return value;}
    public static object LocalTask(bool flag,object value,int tag){async Task<object?> Get(){await Task.Yield();if(flag)return value;return null;}_=Get();return value;}
    public static Task<object> Async(bool flag,Task<object> value,int tag){Func<Task<object?>> callback=async()=>{if(flag)return await value;return null;};_=callback();return value;}
    public static ValueTask<object> ValueAsync(bool flag,ValueTask<object> value,int tag){Func<ValueTask<object?>> callback=async()=>{if(flag)return await value;return null;};_=callback();return value;}
    public static IComparable Interface(bool flag,IComparable value,int tag){IComparable? Get(){if(flag)return value;return null;}_=Get();return value;}
    public static Base BaseResult(bool flag,Base value,int tag){Base? Get(){if(flag)return value;return null;}_=Get();return value;}
    public static int NullableValue(bool flag,int value,int tag){Func<int?> callback=()=>{if(flag)return value;return null;};_=callback();return value;}
    public static int Numeric(bool flag,int value,int tag){Func<long?> callback=()=>{if(flag)return value;return null;};_=callback();return value;}
    public static Box<object?> Ref(Box<object?> value,int tag){RefCallback callback=()=>ref value.Value;_=callback();return value;}
    public static object Tree(object value,int tag){System.Linq.Expressions.Expression<Func<object?>> callback=()=>value;_=callback;return value;}
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
    destination=/workspace/SharpProof/artifacts/hm-inference/package-nullable-returns-$language.txt
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
    destination=/workspace/SharpProof/artifacts/hm-inference/cli-nullable-returns-$language.txt
  fi
  cp "$log" "$destination"
  for expected in \
    'T1 Local<T1, T2>(bool flag, T1 value, T2 tag)' \
    'T1 Lambda<T1, T2>(bool flag, T1 value, T2 tag)' \
    'T1 Anonymous<T1, T2>(bool flag, T1 value, T2 tag)' \
    '(T1, T2) Independent<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag)' \
    'T1 Iterator<T1, T2>(T1 value, T2 tag)' \
    'T1 AsyncIterator<T1, T2>(T1 value, T2 tag)' \
    'T1? Narrow<T1, T2>(T1? value, T2 tag) where T1 : class' \
    'T1 Tuple<T1, T2>(bool flag, T1 value, T2 tag)' \
    'T1 LocalTask<T1, T2>(bool flag, T1 value, T2 tag)' \
    'global::System.Threading.Tasks.Task<T1> Async<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag)' \
    'global::System.Threading.Tasks.ValueTask<T1> ValueAsync<T1, T2>(bool flag, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag)' \
    'T1 Interface<T1, T2>(bool flag, T1 value, T2 tag) where T1 : global::System.IComparable?' \
    'T1 BaseResult<T1, T2>(bool flag, T1 value, T2 tag) where T1 : global::NullableReturnConsumer.Base?' \
    'int NullableValue<T1>(bool flag, int value, T1 tag)' \
    'int Numeric<T1>(bool flag, int value, T1 tag)' \
    'global::NullableReturnConsumer.Box<object?> Ref<T1>(global::NullableReturnConsumer.Box<object?> value, T1 tag)' \
    'object Tree<T1>(object value, T1 tag)'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing nullable-return signature (C# %s): %s\n' "$language" "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
  if [ "$#" -eq 1 ]; then printf 'PASS CliNullableReturnJoinsCSharp%s\n' "$language"; fi
done
