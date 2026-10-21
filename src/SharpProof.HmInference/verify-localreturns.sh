#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
fixture=$(mktemp -d /tmp/hm-local-return-consumer.XXXXXX)
cp global.json "$fixture/global.json"
package_reference=""
if [ "$#" -eq 3 ]; then
  package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
fi
cat > "$fixture/LocalReturns.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
cat > "$fixture/Methods.cs" << 'EOF'
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public static class LocalReturnConsumer{
    public static object Scalar(object value,int tag){object Local()=>value;_=Local();return value;}
    public static object? Nullable(object? value,int tag){object? Local()=>value;_=Local();return value;}
    public static IComparable Interface(IComparable value,int tag){IComparable Local()=>value;_=Local();return value;}
    public static object[] Array(object[] value,int tag){object[] Local()=>value;_=Local();return value;}
    public static (object left,int right) Pair((object left,int right) value,int tag){(object left,int right) Local()=>value;_=Local();return value;}
    public static Func<object> Factory(Func<object> value,int tag){Func<object> Local()=>value;_=Local();return value;}
    public static object Iterator(object value,int tag){IEnumerable<object> Local(){yield return value;}foreach(var item in Local()){}return value;}
    public static object Multiple(object value,bool flag,int tag){object Local(){if(flag)return value;return new object();}_=Local();return value;}
    public static object Async(object value,int tag){async Task<object> Local(){await Task.Yield();return value;}_=Local();return value;}
    public static object ValueAsync(object value,int tag){async ValueTask<object> Local(){await Task.Yield();return value;}_=Local();return value;}
    public static List<object> Invariant(List<object> value,int tag){List<object> Local()=>value;_=Local();return value;}
    public static int Boxing(int value,int tag){object Local()=>value;_=Local();return value;}
}
EOF
cat > "$fixture/.editorconfig" << 'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
if [ "$#" -eq 3 ]; then
  NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/LocalReturns.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || {
    tail -35 "$fixture/restore.log"
    exit 1
  }
  NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/LocalReturns.csproj" --no-restore > "$fixture/build.log" 2>&1 || {
    tail -35 "$fixture/build.log"
    exit 1
  }
  log="$fixture/build.log"
  destination=/workspace/SharpProof/artifacts/hm-inference/package-local-returns.txt
else
  dotnet build "$fixture/LocalReturns.csproj" > "$fixture/build.log" 2>&1 || {
    tail -35 "$fixture/build.log"
    exit 1
  }
  dotnet "$output" --project "$fixture/LocalReturns.csproj" --no-restore > "$fixture/cli.log" 2>&1 || {
    tail -35 "$fixture/cli.log"
    exit 1
  }
  log="$fixture/cli.log"
  destination=/workspace/SharpProof/artifacts/hm-inference/cli-local-returns.txt
fi
cp "$log" "$destination"
for expected in \
  'T1 Scalar<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
  'T1? Nullable<T1, T2>(T1? value, T2 tag) where T1 : class' \
  'T1 Interface<T1, T2>(T1 value, T2 tag) where T1 : global::System.IComparable' \
  'T1[] Array<T1, T2>(T1[] value, T2 tag) where T1 : class' \
  '(T1 left, int right) Pair<T1, T2>((T1 left, int right) value, T2 tag) where T1 : notnull' \
  'global::System.Func<T1> Factory<T1, T2>(global::System.Func<T1> value, T2 tag) where T1 : class' \
  'T1 Iterator<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
  'T1 Multiple<T1, T2>(T1 value, bool flag, T2 tag) where T1 : notnull' \
  'T1 Async<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
  'T1 ValueAsync<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
  'global::System.Collections.Generic.List<object> Invariant<T1>(global::System.Collections.Generic.List<object> value, T1 tag)' \
  'T1 Boxing<T1, T2>(T1 value, T2 tag) where T1 : notnull'; do
  if ! grep -Fq "$expected" "$log"; then
    printf 'Missing local-return signature: %s\n' "$expected"
    tail -35 "$log"
    exit 1
  fi
done
if [ "$#" -eq 1 ]; then printf '%s\n' 'PASS CliDeclaredLocalReturnConversions'; fi
