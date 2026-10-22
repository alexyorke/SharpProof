#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
for language in 8 9 13; do
  fixture=$(mktemp -d /tmp/hm-default-values.XXXXXX)
  cp global.json "$fixture/global.json"
  constants=""
  if [ "$language" = 13 ]; then constants=MODERN_ALIASES; fi
  package_reference=""
  if [ "$#" -eq 3 ]; then
    package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
  fi
  cat > "$fixture/Defaults.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><DefineConstants>$constants</DefineConstants><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><WarningsNotAsErrors>HM0001</WarningsNotAsErrors><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
  cat > "$fixture/Methods.cs" << 'EOF'
using System;using System.Collections.Generic;using System.Threading.Tasks;
public sealed class IndirectConsumerCell<T>{public T Value;public IndirectConsumerCell(T value){Value=value;}public ref T Slot=>ref Value;public ref T this[int index]=>ref Value;}
public static class DefaultConsumer{
    static ref T Borrow<T>(ref T value)=>ref value;
    public static int IndirectArray(int[] value){ref var alias=ref value[0];alias=default;return alias;}
    public static int IndirectMatrix(int[,] value){ref var alias=ref value[0,0];alias=default;return alias;}
    public static int IndirectJagged(int[][] value){ref var alias=ref value[0][0];alias=default;return alias;}
    public static int IndirectSpan(Span<int> value){ref var alias=ref value[0];alias=default;return alias;}
    public static int IndirectField(IndirectConsumerCell<int> cell){ref var alias=ref cell.Value;alias=default;return alias;}
    public static int IndirectProperty(IndirectConsumerCell<int> cell){ref var alias=ref cell.Slot;alias=default;return alias;}
    public static int IndirectIndexer(IndirectConsumerCell<int> cell){ref var alias=ref cell[0];alias=default;return alias;}
    public static int IndirectReturn(int[] value){ref var alias=ref Borrow(ref value[0]);alias=default;return alias;}
    public static int IndirectFill(int[] value,int fill){value[0]=fill;ref var alias=ref value[0];alias=default;return fill;}
    public static int IndirectRebind(int[] first,int[] second){ref var alias=ref first[0];alias=ref second[0];alias=default;return alias;}
    public static int Alias(int value){ref var alias=ref value;alias=default;return value;}
    public static int AliasLocal(int value){var result=value;ref var alias=ref result;alias=default;return result;}
    public static int AliasChain(int value){var result=value;ref var first=ref result;ref var second=ref first;second=default;return result;}
    public static int AliasCopy(int value){var result=value;var copy=result;ref var alias=ref result;alias=default;return copy;}
    public static int AliasRef(ref int value){value=default;return value;}
    public static int AliasOut(int value,out int output){output=value;output=default;return output;}
    public static (int left,int right) AliasConditional(bool flag,int first,int second){var left=first;var right=second;ref var alias=ref(flag?ref left:ref right);alias=default;return(left,right);}
    public static int AliasConditionalStore(bool flag,int value){ref var alias=ref value;alias=flag?value:default;return value;}
#if MODERN_ALIASES
    public static Func<int> AliasCapture(int value){var result=value;var read=()=>result;ref var alias=ref result;alias=default;return read;}
    public static async Task<int> AliasAsync(Task<int> value){var result=await value;ref var alias=ref result;alias=default;return result;}
#endif
    public static int Assigned(bool flag,int value){var result=value;if(flag)result=default;return result;}
    public static int Parameter(int value){value=default;return value;}
    public static int Reset(int value){var result=value;void Clear(){result=default;}Clear();return result;}
    public static int ArrayStore(int[] values){values[0]=default;return values[0];}
    public static int ListStore(List<int> values){values[0]=default;return values[0];}
    public static async Task<int> AssignedAsync(bool flag,Task<int> value){var result=await value;if(flag)result=default;return result;}
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
    "$element IndirectArray<T1>(${element}[] value)$suffix" \
    "$element IndirectMatrix<T1>(${element}[,] value)$suffix" \
    "$element IndirectJagged<T1>(${element}[][] value)$suffix" \
    "$element IndirectSpan<T1>(global::System.Span<$element> value)$suffix" \
    "$element IndirectField<T1>(global::IndirectConsumerCell<$element> cell)$suffix" \
    "$element IndirectProperty<T1>(global::IndirectConsumerCell<$element> cell)$suffix" \
    "$element IndirectIndexer<T1>(global::IndirectConsumerCell<$element> cell)$suffix" \
    "$element IndirectReturn<T1>(${element}[] value)$suffix" \
    "T1 IndirectFill<T1>(${element}[] value, T1 fill)$suffix" \
    "$element IndirectRebind<T1>(${element}[] first, ${element}[] second)$suffix" \
    "$element Alias<T1>($element value)$suffix" \
    "$element AliasLocal<T1>(T1 value)$suffix" \
    "$element AliasChain<T1>(T1 value)$suffix" \
    "T1 AliasCopy<T1>(T1 value)$suffix" \
    "$element AliasRef<T1>(ref $element value)$suffix" \
    "$element AliasOut<T1>(T1 value, out $element output)$suffix" \
    "($element left, $element right) AliasConditional<T1>(bool flag, T1 first, T1 second)$suffix" \
    "$element AliasConditionalStore<T1>(bool flag, $element value)$suffix" \
    "$element Assigned<T1>(bool flag, T1 value)$suffix" \
    "$element Parameter<T1>($element value)$suffix" \
    "$element Reset<T1>(T1 value)$suffix" \
    "$element ArrayStore<T1>(${element}[] values)$suffix" \
    "$element ListStore<T1>(global::System.Collections.Generic.List<$element> values)$suffix" \
    "global::System.Threading.Tasks.Task<$element> AssignedAsync<T1>(bool flag, global::System.Threading.Tasks.Task<T1> value)$suffix" \
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
  if [ "$language" = 13 ]; then
    grep -Fq 'global::System.Func<T1?> AliasCapture<T1>(T1 value)' "$log"
    grep -Fq 'global::System.Threading.Tasks.Task<T1?> AliasAsync<T1>(global::System.Threading.Tasks.Task<T1> value)' "$log"
  fi
  if [ "$#" -eq 1 ]; then printf 'PASS CliDefaultValuesCSharp%s\n' "$language"; fi
done
