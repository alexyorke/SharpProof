#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
for policy in warnings promoted; do
  fixture=$(mktemp -d /tmp/hm-nullable-groups.XXXXXX)
  cp global.json "$fixture/global.json"
  package_reference=""
  warning_policy=""
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  if [ "$policy" = promoted ]; then
    warning_policy="<NoWarn>CS8622;CS8604</NoWarn><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors>"
  fi
  cat > "$fixture/NullableGroups.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><AllowUnsafeBlocks>true</AllowUnsafeBlocks><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers>$warning_policy</PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" <<'EOF'
using System;
using System.Threading.Tasks;
public static partial class NullableGroupConsumer{
    static void Need<T>(T value)where T:notnull{}
    static void NeedClass<T>(T value)where T:class{}
    static void NeedInterface<T>(T value)where T:IComparable{}
    public static object NotNull(object value,int tag){Need(value);return value;}
    public static object Class(object value,int tag){NeedClass(value);return value;}
    public static IComparable Interface(IComparable value,int tag){NeedInterface(value);return value;}
    public static string ReducedReturn(this string value,int tag)=>value;
    public static object ReducedNotNull(this string value,int tag){Need(value);return new object();}
    public static async Task<string> ReducedAsync(this string value,int tag){await Task.Yield();return value;}
    public static async ValueTask<string> ReducedValueAsync(this string value,int tag){await Task.Yield();return value;}
    public static string AddressReturn(string value,int tag)=>value;
    public static ref string AddressRef(ref string value,int tag)=>ref value;
    public static async Task<string> AddressAsync(string value,int tag){await Task.Yield();return value;}
    public static async ValueTask<string> AddressValueAsync(string value,int tag){await Task.Yield();return value;}
    public static string Return(string value,int tag)=>value;
    public static string[] ArrayReturn(string[] value,int tag)=>value;
    public static (string left,int right) TupleReturn((string left,int right) pair,int tag)=>pair;
    public static async ValueTask<object> ValueAsync(object value,int tag){await Task.Yield();NeedClass(value);return value;}
    public static async Task<object> Async(object value,int tag){await Task.Yield();Need(value);return value;}
}
EOF
  cat > "$fixture/Callers.cs" <<'EOF'
using System;
using System.Threading.Tasks;
public static partial class NullableGroupConsumer{
    public static Func<string?,int,object> UseNotNull()=>NotNull;
    public static Func<string?,int,object> UseClass()=>Class;
    public static Func<string?,int,IComparable> UseInterface()=>Interface;
    public static Func<int,string> UseReducedReturn(string? value)=>value.ReducedReturn;
    public static Func<int,object> UseReducedNotNull(string? value)=>value.ReducedNotNull;
    public static Func<int,Task<string>> UseReducedAsync(string? value)=>value.ReducedAsync;
    public static Func<int,ValueTask<string>> UseReducedValueAsync(string? value)=>(Func<int,ValueTask<string>>)value.ReducedValueAsync;
    public static unsafe delegate*<string?,int,string> UseAddressReturn()=>&AddressReturn;
    public static unsafe delegate*<ref string?,int,ref string> UseAddressRef()=>&AddressRef;
    public static unsafe delegate*<string?,int,Task<string>> UseAddressAsync()=>&AddressAsync;
    public static unsafe delegate*<string?,int,ValueTask<string>> UseAddressValueAsync()=>&AddressValueAsync;
    public static Func<string?,int,string> UseReturn()=>Return;
    public static Func<string?[],int,string[]> UseArray()=>ArrayReturn;
    public static Func<(string?,int),int,(string,int)> UseTuple()=>TupleReturn;
    public static Func<string?,int,Task<object>> UseAsync()=>(Func<string?,int,Task<object>>)Async;
    public static Func<string?,int,ValueTask<object>> UseValueAsync()=>(Func<string?,int,ValueTask<object>>)ValueAsync;
}
EOF
  cat > "$fixture/.editorconfig" <<'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/NullableGroups.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || { tail -35 "$fixture/restore.log"; exit 1; }
    NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/NullableGroups.csproj" --no-restore > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
    log="$fixture/build.log"
    destination="$evidence/package-nullable-groups-$policy.txt"
  else
    dotnet build "$fixture/NullableGroups.csproj" > "$fixture/build.log" 2>&1 || { tail -35 "$fixture/build.log"; exit 1; }
    dotnet "$output" --project "$fixture/NullableGroups.csproj" --no-restore > "$fixture/cli.log" 2>&1 || { tail -35 "$fixture/cli.log"; exit 1; }
    log="$fixture/cli.log"
    destination="$evidence/cli-nullable-groups-$policy.txt"
  fi
  cp "$log" "$destination"
  for expected in \
    'object NotNull<T1>(object value, T1 tag)' \
    'object Class<T1>(object value, T1 tag)' \
    'global::System.IComparable Interface<T1>(global::System.IComparable value, T1 tag)' \
    'string ReducedReturn<T1>(this string value, T1 tag)' \
    'object ReducedNotNull<T1>(this string value, T1 tag)' \
    'global::System.Threading.Tasks.Task<string> ReducedAsync<T1>(this string value, T1 tag)' \
    'global::System.Threading.Tasks.ValueTask<string> ReducedValueAsync<T1>(this string value, T1 tag)' \
    'string AddressReturn<T1>(string value, T1 tag)' \
    'ref string AddressRef<T1>(ref string value, T1 tag)' \
    'global::System.Threading.Tasks.Task<string> AddressAsync<T1>(string value, T1 tag)' \
    'global::System.Threading.Tasks.ValueTask<string> AddressValueAsync<T1>(string value, T1 tag)' \
    'string Return<T1>(string value, T1 tag)' \
    'string[] ArrayReturn<T1>(string[] value, T1 tag)' \
    '(string left, T1 right) TupleReturn<T1, T2>((string left, T1 right) pair, T2 tag)' \
    'global::System.Threading.Tasks.Task<object> Async<T1>(object value, T1 tag)' \
    'global::System.Threading.Tasks.ValueTask<object> ValueAsync<T1>(object value, T1 tag)'; do
    if ! grep -Fq "$expected" "$log"; then printf 'Missing nullable method-group signature (%s): %s\n' "$policy" "$expected"; tail -35 "$log"; exit 1; fi
  done
done
if [ "$#" -eq 1 ]; then printf '%s\n' 'PASS CliNullableMethodGroupWarningsAndPromotion'; fi
