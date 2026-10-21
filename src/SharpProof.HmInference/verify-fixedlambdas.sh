#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-fixed-lambdas.XXXXXX)
  cp global.json "$fixture/global.json"
  constants=""
  if [ "$language" = 13 ]; then constants=MODERN; fi
  package_reference=""
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$fixture/Lambdas.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><DefineConstants>$constants</DefineConstants><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" << 'EOF'
using System;
using System.Threading.Tasks;
#pragma warning disable HM0001
public static class LambdaApi{
    public static void Take(Func<object> callback){}
    public static Func<T> Wrap<T>(Func<T> callback)=>callback;
}
public class LambdaBox{public LambdaBox(Func<object> callback){}}
#pragma warning restore HM0001
public static class LambdaConsumer{
    static Func<object> field=null!;
    public delegate object Callback();
    public delegate ref object RefCallback();
    public class Box<T>{public T Value=default!;}
    public static object Local(object value,int tag){Func<object> callback=()=>value;_=callback();return value;}
    public static object Field(object value,int tag){field=()=>value;return value;}
    public static object Argument(object value,int tag){LambdaApi.Take(()=>value);return value;}
    public static object Constructor(object value,int tag){_=new LambdaBox(()=>value);return value;}
    public static object Cast(object value,int tag){var callback=(Func<object>)(()=>value);_=callback();return value;}
    public static object Anonymous(object value,int tag){Func<object> callback=delegate{return value;};_=callback();return value;}
    public static object Nested(object value,int tag){Func<Func<object>> callback=()=>()=>value;_=callback()();return value;}
    public static object Custom(object value,int tag){Callback callback=()=>value;_=callback();return value;}
    public static object? Nullable(object? value,int tag){Func<object?> callback=()=>value;_=callback();return value;}
    public static (object,object) Conditional(bool flag,object left,object right,int tag){Func<object> callback=()=>flag?left:right;_=callback();return(left,right);}
    public static Task<object> Async(Task<object> value,int tag){Func<Task<object>> callback=async()=>await value;_=callback();return value;}
    public static ValueTask<object> ValueAsync(ValueTask<object> value,int tag){Func<ValueTask<object>> callback=async()=>await value;_=callback();return value;}
    public static int Scope(object value,int other,int tag){Func<object> callback=()=>value;var free=LambdaApi.Wrap(()=>other);_=callback();return free();}
    public static object Tree(object value,int tag){System.Linq.Expressions.Expression<Func<object>> callback=()=>value;_=callback;return value;}
    public static Box<object> Ref(Box<object> value,int tag){RefCallback callback=()=>ref value.Value;_=callback();return value;}
#if MODERN
    public static object Explicit(object value,int tag){var callback=object()=>value;_=callback();return value;}
    public static object Natural(object value,int tag){object callback=()=>value;_=callback;return value;}
#endif
}
EOF
  cat > "$fixture/.editorconfig" << 'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages" dotnet restore "$fixture/Lambdas.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || {
      tail -35 "$fixture/restore.log"
      exit 1
    }
    NUGET_PACKAGES="$fixture/packages" dotnet build "$fixture/Lambdas.csproj" --no-restore > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    log="$fixture/build.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/package-fixed-lambdas-$language.txt
  else
    dotnet build "$fixture/Lambdas.csproj" > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    dotnet "$output" --project "$fixture/Lambdas.csproj" --no-restore > "$fixture/cli.log" 2>&1 || {
      tail -35 "$fixture/cli.log"
      exit 1
    }
    log="$fixture/cli.log"
    destination=/workspace/SharpProof/artifacts/hm-inference/cli-fixed-lambdas-$language.txt
  fi
  cp "$log" "$destination"
  conditional='(T1, T2) Conditional<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull where T2 : notnull'
  if [ "$language" = 8 ]; then
    conditional='(T1, T2) Conditional<T1, T2, T3>(bool flag, T1 left, T2 right, T3 tag) where T1 : notnull, T2 where T2 : notnull'
  fi
  for expected in \
    'T1 Local<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Field<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Argument<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Constructor<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Cast<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Anonymous<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Nested<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1 Custom<T1, T2>(T1 value, T2 tag) where T1 : notnull' \
    'T1? Nullable<T1, T2>(T1? value, T2 tag) where T1 : class' \
    "$conditional" \
    'global::System.Threading.Tasks.Task<T1> Async<T1, T2>(global::System.Threading.Tasks.Task<T1> value, T2 tag) where T1 : notnull' \
    'global::System.Threading.Tasks.ValueTask<T1> ValueAsync<T1, T2>(global::System.Threading.Tasks.ValueTask<T1> value, T2 tag) where T1 : notnull' \
    'T2 Scope<T1, T2, T3>(T1 value, T2 other, T3 tag) where T1 : notnull' \
    'object Tree<T1>(object value, T1 tag)' \
    'global::LambdaConsumer.Box<object> Ref<T1>(global::LambdaConsumer.Box<object> value, T1 tag)'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing fixed-lambda signature (C# %s): %s\n' "$language" "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
  if [ "$language" = 13 ]; then
    grep -Fq 'T1 Explicit<T1, T2>(T1 value, T2 tag) where T1 : notnull' "$log"
    grep -Fq 'object Natural<T1>(object value, T1 tag)' "$log"
  fi
  if [ "$#" -eq 1 ]; then printf 'PASS CliFixedLambdaReturnsCSharp%s\n' "$language"; fi
done
