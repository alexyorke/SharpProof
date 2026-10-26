#!/usr/bin/env bash
set -euo pipefail
output=$(realpath "$1")
evidence=/workspace/SharpProof/artifacts/hm-inference
fixture=$(mktemp -d /tmp/hm-inline-arrays.XXXXXX)
cp global.json "$fixture/global.json"
package_reference=""
if [ "$#" -eq 3 ]; then
  package_reference="<ItemGroup><PackageReference Include=\"SharpProof.HmInference\" Version=\"$3\" PrivateAssets=\"all\" /></ItemGroup>"
fi
cat > "$fixture/InlineArrays.cs" << 'EOF'
#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
[InlineArray(4)] public struct Buffer<T> { private T _element0; }
[InlineArray(4)] public struct AnnotatedBuffer<T> { [MaybeNull] private T _element0; }
public static class Forms {
    public static int ReadIn(in Buffer<int> values, int index, int ignored) => values[index];
    public static ReadOnlySpan<int> ReturnReadOnly(in Buffer<int> values, int ignored) => values;
    public static Span<int> ReturnWritable(ref Buffer<int> values, int ignored) => values;
    public static int ReadIndex(Buffer<int> values, Index index, int ignored) => values[index];
    public static int Last(Buffer<int> values, int ignored) => values[^1];
    public static Span<int> Tail(ref Buffer<int> values, int start, int ignored) => values[start..];
    public static ref readonly int AliasIn(in Buffer<int> values, int index, int ignored) => ref values[index];
    public static ReadOnlySpan<int> SliceIn(in Buffer<int> values, Range range, int ignored) => values[range];
    public static Span<int> SliceRef(ref Buffer<int> values, Range range, int ignored) => values[range];
    public static int? ReadConditionalValue(Buffer<int>? values, int index, int ignored) => values?[index];
    public static string? ReadConditionalReference(Buffer<string>? values, int index, int ignored) => values?[index];
    public static string? ReadAnnotated(AnnotatedBuffer<string> values, int ignored) => values[0];
}
EOF
cat > "$fixture/.editorconfig" << 'EOF'
root=true
[*.cs]
dotnet_diagnostic.HM0001.severity=warning
EOF
for language in 12 13; do
  if [ "$language" -eq 13 ]; then
    cat > "$fixture/Iteration.cs" << 'EOF'
public static class Iteration {
    public static int Count(Buffer<int> values, int ignored) { int count = 0; foreach (var item in values) count++; return count; }
    public static int CountIn(in Buffer<int> values, int ignored) { int count = 0; foreach (var item in values) count++; return count; }
    public static int CountRef(ref Buffer<int> values, int ignored) { int count = 0; foreach (var item in values) count++; return count; }
}
EOF
  fi
  cat > "$fixture/InlineArrays.csproj" << EOF
<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><LangVersion>$language</LangVersion><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
$package_reference
</Project>
EOF
  if [ "$#" -eq 3 ]; then
    NUGET_PACKAGES="$fixture/packages-$language" dotnet restore "$fixture/InlineArrays.csproj" --source "$2" > "$fixture/restore.log" 2>&1 || {
      tail -35 "$fixture/restore.log"
      exit 1
    }
    NUGET_PACKAGES="$fixture/packages-$language" dotnet build "$fixture/InlineArrays.csproj" --no-restore --no-incremental > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    log="$fixture/build.log"
    destination="$evidence/package-inline-arrays-$language.txt"
  else
    dotnet build "$fixture/InlineArrays.csproj" --no-incremental > "$fixture/build.log" 2>&1 || {
      tail -35 "$fixture/build.log"
      exit 1
    }
    dotnet "$output" --project "$fixture/InlineArrays.csproj" --no-restore > "$fixture/cli.log" 2>&1 || {
      tail -35 "$fixture/cli.log"
      exit 1
    }
    log="$fixture/cli.log"
    destination="$evidence/cli-inline-arrays-$language.txt"
  fi
  cp "$log" "$destination"
  if [ "$language" -eq 13 ]; then
    for expected in \
      'int Count<T1, T2>(global::Buffer<T1> values, T2 ignored)' \
      'int CountIn<T1, T2>(in global::Buffer<T1> values, T2 ignored)' \
      'int CountRef<T1, T2>(ref global::Buffer<T1> values, T2 ignored)'; do
      if ! grep -Fq "$expected" "$log"; then
        printf 'Missing inline-array iteration signature: %s\n' "$expected"
        tail -35 "$log"
        exit 1
      fi
    done
  fi
  if grep -Fq 'AD0001' "$log"; then
    tail -35 "$log"
    exit 1
  fi
  for expected in \
    'T1 ReadIn<T1, T2>(in global::Buffer<T1> values, int index, T2 ignored)' \
    'global::System.ReadOnlySpan<T1> ReturnReadOnly<T1, T2>(in global::Buffer<T1> values, T2 ignored)' \
    'global::System.Span<T1> ReturnWritable<T1, T2>(ref global::Buffer<T1> values, T2 ignored)' \
    'T1 ReadIndex<T1, T2>(global::Buffer<T1> values, global::System.Index index, T2 ignored)' \
    'T1 Last<T1, T2>(global::Buffer<T1> values, T2 ignored)' \
    'global::System.Span<T1> Tail<T1, T2>(ref global::Buffer<T1> values, int start, T2 ignored)' \
    'ref readonly T1 AliasIn<T1, T2>(in global::Buffer<T1> values, int index, T2 ignored)' \
    'global::System.ReadOnlySpan<T1> SliceIn<T1, T2>(in global::Buffer<T1> values, global::System.Range range, T2 ignored)' \
    'global::System.Span<T1> SliceRef<T1, T2>(ref global::Buffer<T1> values, global::System.Range range, T2 ignored)' \
    'T1? ReadConditionalValue<T1, T2>(global::Buffer<T1>? values, int index, T2 ignored) where T1 : struct' \
    'T1? ReadConditionalReference<T1, T2>(global::Buffer<T1>? values, int index, T2 ignored) where T1 : class' \
    'T1? ReadAnnotated<T1, T2>(global::AnnotatedBuffer<T1> values, T2 ignored) where T1 : class'; do
    if ! grep -Fq "$expected" "$log"; then
      printf 'Missing inline-array signature: %s\n' "$expected"
      tail -35 "$log"
      exit 1
    fi
  done
done
if [ "$#" -eq 1 ]; then printf '%s\n' 'PASS CliInlineArraysCSharp12And13'; fi
