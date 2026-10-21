#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-null-joins.XXXXXX)
  cp global.json "$fixture/global.json"
  constants=""
  if [ "$language" != 8 ]; then constants=FUNCTION_POINTERS; fi
  package_reference=""
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$fixture/Joins.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks><DefineConstants>$constants</DefineConstants><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" << 'EOF'
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public static class NullJoinConsumer{
    public static object? Reference(bool flag,object value,int tag){if(flag)return value;return null;}
    public static int? Value(bool flag,int value,int tag){if(flag)return value;return null;}
    public static object[]? Array(bool flag,object[] value,int tag)=>flag?value:null;
    public static List<int>? List(bool flag,List<int> value,int tag){if(flag)return value;return default;}
    public static Func<object>? Delegate(bool flag,Func<object> value,int tag){if(flag)return value;return null;}
    public static (object left,int right)? Pair(bool flag,(object left,int right) value,int tag){if(flag)return value;return null;}
    public static object? Switch(int key,object value,int tag)=>key switch{0=>value,_=>null};
    public static object? Typed(bool flag,object value,int tag){if(flag)return value;return(object?)null;}
    public static int DefaultValue(bool flag,int value,int tag){if(flag)return value;return default;}
    public static async Task<object?> AsyncReference(bool flag,Task<object> value,int tag){if(flag)return await value;return null;}
    public static async Task<int?> AsyncValue(bool flag,Task<int> value,int tag){if(flag)return await value;return null;}
    public static async ValueTask<object?> ValueReference(bool flag,ValueTask<object> value,int tag){if(flag)return await value;return null;}
    public static async ValueTask<int?> ValueStruct(bool flag,ValueTask<int> value,int tag){if(flag)return await value;return null;}
    public static unsafe int* Pointer(bool flag,int* value,int tag){if(flag)return value;return null;}
#if FUNCTION_POINTERS
    public static unsafe delegate*<int,int> FunctionPointer(bool flag,delegate*<int,int> value,int tag){if(flag)return value;return null;}
#endif
}
EOF
  cat > "$fixture/.editorconfig" << 'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Joins.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || {
      tail -35 "$fixture/restore.log"
      exit 1
    }
    NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Joins.csproj" --no-restore > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    log="$fixture/build.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/package-null-joins-$language.txt
  else
    dotnet build "$fixture/Joins.csproj" > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    dotnet "$output" --project "$fixture/Joins.csproj" --no-restore > "$fixture/cli.log" 2>&1 || {
      tail -35 "$fixture/cli.log"
      exit 1
    }
    log="$fixture/cli.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/cli-null-joins-$language.txt
  fi
  cp "$log" "$destination"
  for expected in \
    'T1? Reference<T1, T2>(bool flag, T1 value, T2 tag) where T1 : class' \
    'T1? Value<T1, T2>(bool flag, T1 value, T2 tag) where T1 : struct' \
    'T1[]? Array<T1, T2>(bool flag, T1[] value, T2 tag)' \
    'global::System.Collections.Generic.List<T1>? List<T1, T2>(bool flag, global::System.Collections.Generic.List<T1> value, T2 tag)' \
    'global::System.Func<T1>? Delegate<T1, T2>(bool flag, global::System.Func<T1> value, T2 tag)' \
    '(T1 left, T2 right)? Pair<T1, T2, T3>(bool flag, (T1 left, T2 right) value, T3 tag)' \
    'T1? Switch<T1, T2>(int key, T1 value, T2 tag) where T1 : class' \
    'object? Typed<T1>(bool flag, object value, T1 tag)' \
    'int DefaultValue<T1>(bool flag, int value, T1 tag)' \
    'global::System.Threading.Tasks.Task<T1?> AsyncReference<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : class' \
    'global::System.Threading.Tasks.Task<T1?> AsyncValue<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : struct' \
    'global::System.Threading.Tasks.ValueTask<T1?> ValueReference<T1, T2>(bool flag, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : class' \
    'global::System.Threading.Tasks.ValueTask<T1?> ValueStruct<T1, T2>(bool flag, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : struct' \
    'T1* Pointer<T1, T2>(bool flag, T1* value, T2 tag) where T1 : unmanaged'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing null-join signature (C# %s): %s\n' "$language" "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
  if [ "$language" != 8 ]; then
    grep -Fq 'delegate*<T1, T2> FunctionPointer<T1, T2, T3>(bool flag, delegate*<T1, T2> value, T3 tag)' "$log"
  fi
  if [ "$#" -eq 1 ]; then printf 'PASS CliNullJoinInferenceCSharp%s\n' "$language"; fi
done
