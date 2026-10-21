#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-expression-joins.XXXXXX)
  cp global.json "$fixture/global.json"
  package_reference=""
  constants=""
  if [ "$language" != 8 ]; then constants=CSHARP9ORLATER; fi
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$fixture/Joins.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><DefineConstants>$constants</DefineConstants><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" << 'EOF'
using System;
using System.Threading.Tasks;
public static class JoinConsumer{
    public static object Conditional(bool flag,object value,int tag){object Local()=>flag?value:new object();_=Local();return value;}
    public static object Switch(int key,object value,int tag){object Local()=>key switch{0=>value,_=>new object()};_=Local();return value;}
    public static object? Nullable(bool flag,object? value,int tag){object? Local()=>flag?value:null;_=Local();return value;}
    public static (object,object) Independent(bool flag,object left,object right,int tag){object Local()=>flag?left:right;_=Local();return(left,right);}
    public static Task<object> Async(bool flag,Task<object> value,int tag){async Task<object> Local()=>flag?await value:new object();_=Local();return value;}
    public static ValueTask<object> ValueAsync(int key,ValueTask<object> value,int tag){async ValueTask<object> Local()=>key switch{0=>await value,_=>new object()};_=Local();return value;}
    public static int? Lift(bool flag,int left,int? right,int tag)=>flag?left:right;
    public static long Numeric(bool flag,int left,long right,int tag)=>flag?left:right;
    public static long NumericSwitch(int key,int left,long right,int tag)=>key switch{0=>left,_=>right};
    public static int Homogeneous(bool flag,int left,int right)=>flag?left:right;
#if CSHARP9ORLATER
    public static object Target(bool flag,int left,string right,int tag)=>flag?left:right;
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
    destination=/workspace/SharpProof/artifacts/hm-inference/package-expression-joins-$language.txt
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
    destination=/workspace/SharpProof/artifacts/hm-inference/cli-expression-joins-$language.txt
  fi
  cp "$log" "$destination"
  independent='(T1, T2) Independent<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull'
  if [ "$language" = 8 ]; then
    independent='(T1, T2) Independent<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull, T2 where T2 : notnull'
  fi
  for expected in \
    'T1 Conditional<T1, T2>(bool flag, T1 value, T2 tag) where T1 : notnull' \
    'T1 Switch<T1, T2>(int key, T1 value, T2 tag) where T1 : notnull' \
    'T1? Nullable<T1, T2>(bool flag, T1? value, T2 tag) where T1 : class' \
    "$independent" \
    'global::System.Threading.Tasks.Task<T1> Async<T1, T2>(bool flag, global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : notnull' \
    'global::System.Threading.Tasks.ValueTask<T1> ValueAsync<T1, T2>(int key, global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : notnull' \
    'T1? Lift<T1, T2>(bool flag, T1 left, T1? right, T2 tag) where T1 : struct' \
    'long Numeric<T1>(bool flag, int left, long right, T1 tag)' \
    'long NumericSwitch<T1>(int key, int left, long right, T1 tag)' \
    'T1 Homogeneous<T1>(bool flag, T1 left, T1 right)'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing expression-join signature (C# %s): %s\n' "$language" "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
  if [ "$language" != 8 ]; then
    grep -Fq 'object Target<T1>(bool flag, int left, string right, T1 tag)' "$log"
  fi
  if [ "$#" -eq 1 ]; then printf 'PASS CliExpressionJoinsCSharp%s\n' "$language"; fi
done
