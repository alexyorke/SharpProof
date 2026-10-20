#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
for policy in warnings promoted; do
  fixture=$(mktemp -d /tmp/hm-nullable-callers.XXXXXX)
  cp global.json "$fixture/global.json"
  package_reference=""
  warning_policy=""
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  if [ "$policy" = promoted ]; then
    warning_policy="<NoWarn>CS8604;CS8620;CS8619</NoWarn><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors>"
  fi
  cat > "$fixture/NullableCalls.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers>$warning_policy</PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" <<'EOF'
using System;
using System.Threading.Tasks;
public static partial class NullableConsumer{
    static void Need<T>(T value)where T:notnull{}
    static void NeedClass<T>(T value)where T:class{}
    static void NeedInterface<T>(T value)where T:IComparable{}
    public static object NotNull(object value,int tag){Need(value);return value;}
    public static object Class(object value,int tag){NeedClass(value);return value;}
    public static IComparable Interface(IComparable value,int tag){NeedInterface(value);return value;}
    public static string Return(string value,int tag)=>value;
    public static string[] ArrayReturn(string[] value,int tag)=>value;
    public static (string left,int right) TupleReturn((string left,int right) pair,int tag)=>pair;
    public static async Task<object> Async(object value,int tag){await Task.Yield();Need(value);return value;}
}
EOF
  cat > "$fixture/Callers.cs" <<'EOF'
using System;
using System.Threading.Tasks;
public static partial class NullableConsumer{
    public static object? UseNotNull(string? value)=>NotNull(value,1);
    public static object? UseClass(string? value)=>Class(value,1);
    public static IComparable? UseInterface(string? value)=>Interface(value,1);
    public static string UseReturn(string? value)=>Return(value,1);
    public static string[] UseArray(string?[] value)=>ArrayReturn(value,1);
    public static (string,int) UseTuple((string?,int) pair)=>TupleReturn(pair,1);
    public static Task<object> UseAsync(string? value)=>Async(value,1);
}
EOF
  cat > "$fixture/.editorconfig" <<'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/NullableCalls.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || { tail -35 "$fixture/restore.log"; exit 1; }
    NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/NullableCalls.csproj" --no-restore > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
    log="$fixture/build.log"
    destination="$evidence/package-nullable-callers-$policy.txt"
  else
    dotnet build "$fixture/NullableCalls.csproj" > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
    dotnet "$output" --project "$fixture/NullableCalls.csproj" --no-restore > "$fixture/cli.log" 2>&1 || { tail -35 "$fixture/cli.log"; exit 1; }
    log="$fixture/cli.log"
    destination="$evidence/cli-nullable-callers-$policy.txt"
  fi
  cp "$log" "$destination"
  for expected in \
    'object NotNull<T1>(object value, T1 tag)' \
    'object Class<T1>(object value, T1 tag)' \
    'global::System.IComparable Interface<T1>(global::System.IComparable value, T1 tag)' \
    'string Return<T1>(string value, T1 tag)' \
    'string[] ArrayReturn<T1>(string[] value, T1 tag)' \
    '(string left, T1 right) TupleReturn<T1, T2>((string left, T1 right) pair, T2 tag)' \
    'global::System.Threading.Tasks.Task<object> Async<T1>(object value, T1 tag)'; do
    if ! grep -Fq "$expected" "$log"; then printf 'Missing nullable caller signature (%s): %s\n' "$policy" "$expected"; tail -35 "$log"; exit 1; fi
  done
done
if [ "$#" -eq 1 ]; then printf '%s\n' 'PASS CliNullableCallerWarningsAndPromotion'; fi
