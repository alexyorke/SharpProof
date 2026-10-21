#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-fixed-arguments.XXXXXX)
  cp global.json "$fixture/global.json"
  package_reference=""
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$fixture/Arguments.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" << 'EOF'
using System;
using System.Threading.Tasks;
#pragma warning disable HM0001
public static class FixedApi{
    public static void Take(object value){}
    public static void Nullable(object? value){}
    public static void Array(object[] value){}
    public static void Params(params object[] value){}
    public static void In(in object value){}
    public static void Numeric(long value){}
    public static void Generic<T>(T value){}
}
public class FixedBox{public FixedBox(object value){}}
#pragma warning restore HM0001
public static class ArgumentConsumer{
    static readonly Action<object> callback=FixedApi.Take;
    public static object Call(object value,int tag){FixedApi.Take(value);return value;}
    public static object? Nullable(object? value,int tag){FixedApi.Nullable(value);return value;}
    public static object Constructor(object value,int tag){_=new FixedBox(value);return value;}
    public static object Delegate(object value,int tag){callback(value);return value;}
    public static object Generic(object value,int tag){FixedApi.Generic<object>(value);return value;}
    public static object Params(object value,int tag){FixedApi.Params(value,value);return value;}
    public static object[] Array(object[] value,int tag){FixedApi.Array(value);return value;}
    public static (object,object) Conditional(bool flag,object left,object right,int tag){FixedApi.Take(flag?left:right);return(left,right);}
    public static async Task<object?> Async(Task<object?> value,int tag){FixedApi.Nullable(await value);return await value;}
    public static async ValueTask<object> ValueAsync(ValueTask<object> value,int tag){FixedApi.Take(await value);return await value;}
    public static object OmittedIn(object value,int tag){FixedApi.In(value);return value;}
    public static int Numeric(int value,int tag){FixedApi.Numeric(value);return value;}
    public static object CallerTarget(object value,int tag){FixedApi.Take(value);return value;}
    public static object BoxedCaller(int value)=>CallerTarget(value,0);
}
EOF
  cat > "$fixture/.editorconfig" << 'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Arguments.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || {
      tail -35 "$fixture/restore.log"
      exit 1
    }
    NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Arguments.csproj" --no-restore > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    log="$fixture/build.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/package-fixed-arguments-$language.txt
  else
    dotnet build "$fixture/Arguments.csproj" > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    dotnet "$output" --project "$fixture/Arguments.csproj" --no-restore > "$fixture/cli.log" 2>&1 || {
      tail -35 "$fixture/cli.log"
      exit 1
    }
    log="$fixture/cli.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/cli-fixed-arguments-$language.txt
  fi
  cp "$log" "$destination"
  conditional='(T1, T2) Conditional<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull'
  if [ "$language" = 8 ]; then
    conditional='(T1, T2) Conditional<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull, T2 where T2 : notnull'
  fi
  for expected in \
    'T1 Call<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1? Nullable<T1, T2>(T1? value, T2 tag) where T1 : class' \
    'T1 Constructor<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Delegate<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Generic<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Params<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1[] Array<T1, T2>(T1[] value, T2 tag) where T1 : class' \
    "$conditional" \
    'global::System.Threading.Tasks.Task<T1?> Async<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 tag) where T1 : class' \
    'global::System.Threading.Tasks.ValueTask<T1> ValueAsync<T1, T2>(global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : notnull' \
    'object OmittedIn<T1>(object value, T1 tag)' \
    'int Numeric<T1>(int value, T1 tag)' \
    'object CallerTarget<T1>(object value, T1 tag)'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing fixed-argument signature (C# %s): %s\n' "$language" "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
  if [ "$#" -eq 1 ]; then printf 'PASS CliFixedArgumentConversionsCSharp%s\n' "$language"; fi
done
