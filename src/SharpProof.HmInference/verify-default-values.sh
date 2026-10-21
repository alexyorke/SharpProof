#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-default-values.XXXXXX)
  cp global.json "$fixture/global.json"
  package_reference=""
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$fixture/Defaults.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" << 'EOF'
using System;using System.Collections.Generic;using System.Threading.Tasks;
public static class DefaultConsumer{
    public static int Choose(bool flag,int value)=>flag?value:default;
    public static int Branch(bool flag,int value){if(flag)return value;return default;}
    public static Func<int> Callback(bool flag,int value)=>()=>flag?value:default;
    public static IEnumerable<int> Iterator(int value){yield return value;yield return default;}
    public static async IAsyncEnumerable<int> AsyncIterator(int value){await Task.Yield();yield return value;yield return default;}
    public static async Task<int> Async(bool flag,Task<int> value){if(flag)return await value;return default;}
    public static async ValueTask<int> ValueAsync(bool flag,ValueTask<int> value){if(flag)return await value;return default;}
    public static int Coalesce(int? value)=>value??default;
    public static int? Assign(int? value)=>value??=default;
    public static object Box(bool flag,int value)=>flag?value:default;
    public static int Typed(bool flag,int value,int tag)=>flag?value:default(int);
}
EOF
  cat > "$fixture/.editorconfig" << 'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Defaults.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || {
      tail -35 "$fixture/restore.log"
      exit 1
    }
    NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Defaults.csproj" --no-restore > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    log="$fixture/build.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/package-default-values-$language.txt
  else
    dotnet build "$fixture/Defaults.csproj" > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    dotnet "$output" --project "$fixture/Defaults.csproj" --no-restore > "$fixture/cli.log" 2>&1 || {
      tail -35 "$fixture/cli.log"
      exit 1
    }
    log="$fixture/cli.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/cli-default-values-$language.txt
  fi
  cp "$log" "$destination"
  suffix=""
  element='T1?'
  if [ "$language" = 8 ]; then
    element=T1
    suffix=' where T1 : struct'
  fi
  for expected in \
    "$element Choose<T1>(bool flag, T1 value)$suffix" \
    "$element Branch<T1>(bool flag, T1 value)$suffix" \
    "global::System.Func<$element> Callback<T1>(bool flag, T1 value)$suffix" \
    "global::System.Collections.Generic.IEnumerable<$element> Iterator<T1>(T1 value)$suffix" \
    "global::System.Collections.Generic.IAsyncEnumerable<$element> AsyncIterator<T1>(T1 value)$suffix" \
    "global::System.Threading.Tasks.Task<$element> Async<T1>(bool flag, global::System.Threading.Tasks.Task<T1> value)$suffix" \
    "global::System.Threading.Tasks.ValueTask<$element> ValueAsync<T1>(bool flag, global::System.Threading.Tasks.ValueTask<T1> value)$suffix" \
    'T1 Coalesce<T1>(T1? value) where T1 : struct' \
    'T1? Assign<T1>(T1? value) where T1 : struct' \
    'object Box<T1>(bool flag, T1 value) where T1 : struct' \
    'int Typed<T1>(bool flag, int value, T1 tag)'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing default-value signature (C# %s): %s\n' "$language" "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
  if [ "$#" -eq 1 ]; then printf 'PASS CliDefaultValuesCSharp%s\n' "$language"; fi
done
