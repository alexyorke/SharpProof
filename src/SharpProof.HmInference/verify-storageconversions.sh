#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-fixed-storage.XXXXXX)
  cp global.json "$fixture/global.json"
  package_reference=""
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$fixture/Storage.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" << 'EOF'
using System;
using System.Threading.Tasks;
public static class StorageConsumer{
    static object field=null!;
    static object? nullable;
    static object[] array=null!;
    static Func<object> factory=null!;
    public class Box{public object Value{get;set;}=null!;}
    public static object Field(object value,int tag){field=value;return value;}
    public static object? Nullable(object? value,int tag){nullable=value;return value;}
    public static object Deconstruct(object value,int tag){(object stored,int number)=(value,0);_=stored;_=number;return value;}
    public static object Local(object value,int tag){object stored=value;field=stored;return value;}
    public static object Property(Box box,object value,int tag){box.Value=value;return value;}
    public static Box Initializer(object value,int tag)=>new Box{Value=value};
    public static object[] Array(object[] value,int tag){array=value;return value;}
    public static Func<object> Factory(Func<object> value,int tag){factory=value;return value;}
    public static (object,object) Conditional(bool flag,object left,object right,int tag){field=flag?left:right;return(left,right);}
    public static async Task<object?> Async(Task<object?> value,int tag){nullable=await value;return await value;}
    public static async ValueTask<object> ValueAsync(ValueTask<object> value,int tag){field=await value;return await value;}
    public static int Boxing(int value,int tag){field=value;return value;}
    public static object CallerTarget(object value,int tag){field=value;return value;}
    public static object BoxedCaller(int value)=>CallerTarget(value,0);
    public static ref object Ref(ref object left,ref object right,int tag){ref object alias=ref left;alias=ref right;return ref alias;}
}
EOF
  cat > "$fixture/.editorconfig" << 'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Storage.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || {
      tail -35 "$fixture/restore.log"
      exit 1
    }
    NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Storage.csproj" --no-restore > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    log="$fixture/build.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/package-fixed-storage-$language.txt
  else
    dotnet build "$fixture/Storage.csproj" > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    dotnet "$output" --project "$fixture/Storage.csproj" --no-restore > "$fixture/cli.log" 2>&1 || {
      tail -35 "$fixture/cli.log"
      exit 1
    }
    log="$fixture/cli.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/cli-fixed-storage-$language.txt
  fi
  cp "$log" "$destination"
  conditional='(T1, T2) Conditional<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull'
  if [ "$language" = 8 ]; then
    conditional='(T1, T2) Conditional<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull, T2 where T2 : notnull'
  fi
  for expected in \
    'T1 Field<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1? Nullable<T1, T2>(T1? value, T2 tag) where T1 : class' \
    'T1 Deconstruct<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Local<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Property<T1, T2>(global::StorageConsumer.Box box, T1 value, T2 tag) where T1 : notnull' \
    'global::StorageConsumer.Box Initializer<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1[] Array<T1, T2>(T1[] value, T2 tag) where T1 : class' \
    'global::System.Func<T1> Factory<T1, T2>(global::System.Func<T1> value, T2 tag) where T1 : class' \
    "$conditional" \
    'global::System.Threading.Tasks.Task<T1?> Async<T1, T2>(global::System.Threading.Tasks.Task<T1?> value, T2 tag) where T1 : class' \
    'global::System.Threading.Tasks.ValueTask<T1> ValueAsync<T1, T2>(global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : notnull' \
    'int Boxing<T1>(int value, T1 tag)' \
    'object CallerTarget<T1>(object value, T1 tag)' \
    'ref object Ref<T1>(ref object left, ref object right, T1 tag)'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing fixed-storage signature (C# %s): %s\n' "$language" "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
  if [ "$#" -eq 1 ]; then printf 'PASS CliFixedStorageConversionsCSharp%s\n' "$language"; fi
done
